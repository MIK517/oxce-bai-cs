using System.Text;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>One row of the event window's item list: an item given, or taken (negative) by an inverted event.</summary>
public sealed record CampaignEventItemRow(string ItemId, int Quantity);

/// <summary>
/// GeoscapeEventState as it shows a popped event: its rule, the region and city that place it, the items it gave or took
/// at once, and the research articles its OK opens. Texts are string IDs.
/// </summary>
public sealed record CampaignEventWindow(string RuleId, string? Region, string? City, IReadOnlyList<CampaignEventItemRow> Items,
    string ResearchName, string BonusResearchName);

/// <summary>An event popped and its payload ran; the window waits for its acknowledgement.</summary>
public sealed record CampaignEventOpened(CampaignEventWindow Window) : ICampaignEvent;

/// <summary>GeoscapeEventState::btnOkClick for the first open event window.</summary>
public sealed record AcknowledgeCampaignEvent : ICampaignCommand;

/// <summary>
/// The event window's OK: the cutscene it plays (which may end the campaign), whether the HQ's stores are overfull (the
/// sell screen), and the research articles it opens.
/// </summary>
public sealed record CampaignEventAcknowledged(string RuleId, string? Cutscene, bool StorageExceeded, string BonusResearchName,
    string ResearchName) : ICampaignEvent;

public interface ICampaignEventWindowQuery
{
    /// <summary>The open event windows, first to be answered first.</summary>
    IReadOnlyList<CampaignEventWindow> QueryEventWindows();
}

public sealed partial class CampaignState : ICampaignEventWindowQuery
{
    internal const string EventWindowOpen = "A strategic event window is open.";
    private const int MaximumEventSoldiers = 10_000;

    /// <summary>The event windows popped and not yet answered; modal and not saved (D2).</summary>
    private readonly List<CampaignEventWindow> _eventWindows = [];

    /// <summary>The tick that opened the windows: a popup pauses after its tick, so that tick's later levels still run.</summary>
    private CampaignTime _eventWindowTick;

    private void RegisterEvents(CampaignCapabilityRegistry registry)
    {
        registry.Query<ICampaignEventWindowQuery>(this);
        registry.Command<AcknowledgeCampaignEvent>(_ => AcknowledgeEvent());
        // time1Month queues the monthly report before this tick's events, so the report is answered first.
        registry.CommandGate(command => _eventWindows.Count != 0 &&
            command is not (AcknowledgeCampaignEvent or AcknowledgeMonthlyReport or AdvanceCampaignTime)
            ? new CampaignCommandResult([new CampaignActionBlocked(EventWindowOpen)]) : null);
        registry.Preflight(CampaignPreflightOrder.EventWindows, "event windows", (next, _) =>
            _eventWindows.Count != 0 && next != _eventWindowTick ? EventWindowOpen : null);
        registry.SaveGate(() => _eventWindows.Count != 0 ? EventWindowOpen : null);
        registry.Transient("event windows", this, static (source, shadow) =>
        {
            shadow._eventWindows.Clear();
            shadow._eventWindows.AddRange(source._eventWindows);
            shadow._eventWindowTick = source._eventWindowTick;
        });
    }

    IReadOnlyList<CampaignEventWindow> ICampaignEventWindowQuery.QueryEventWindows() => Read(() => _eventWindows.ToArray());

    private CampaignCommandResult AcknowledgeEvent()
    {
        if (_eventWindows.Count == 0) return Blocked("No strategic event window is open.");
        var window = _eventWindows[0];
        _eventWindows.RemoveAt(0);
        var rule = EventRule(window.RuleId);
        List<ICampaignEvent> events = [];
        string? cutscene = null;
        var payload = rule.Payload;
        if (payload.Cutscene.Length != 0)
        {
            cutscene = payload.Cutscene;
            // D4: an event cutscene ends the campaign only while it has no ending. Mod::getVideo throws for an
            // undefined video; the port plays nothing (ADR 0036).
            if (!_content.RuntimeRules.Campaign.Cutscenes.TryGetValue(payload.Cutscene, out var video)) cutscene = null;
            else if (Ending == 0)
            {
                if (video.WinGame) EndCampaign(1);
                if (video.LoseGame) EndCampaign(2);
                if (Ending != 0) events.Add(new CampaignEnded(Ending));
            }
        }
        // The storage-overfull sell screen, skipped before the first month and after an ending.
        var exceeded = Ending == 0 && MonthsPassed > -1 && Options.StorageLimitsEnforced && _bases.Count != 0 &&
            StoresOverfull(_bases[0], 0);
        events.Insert(0, new CampaignEventAcknowledged(window.RuleId, cutscene, exceeded, window.BonusResearchName, window.ResearchName));
        return new(events);
    }

    private RuntimeEventRule EventRule(string id) =>
        _content.RuntimeRules.Events[_content.RuntimeRules.Events.GetRequired(id)].Value;

    /// <summary>
    /// GeoscapeEventState::eventLogic, which its constructor runs inside the tick, and the window it then shows. A name
    /// the reference looks up with an error, a payload the port cannot bound, or a full history fails the tick, which a
    /// staged tick reports as a refusal before anything changes (ADR 0036).
    /// </summary>
    internal void RunEventPayload(string ruleId, TimeEffects effects)
    {
        try
        {
            var window = EventLogic(ruleId, EventRule(ruleId), effects);
            if (_eventWindows.Count == 0) _eventWindowTick = Time;
            _eventWindows.Add(window);
            effects.Notify(new CampaignEventOpened(window));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // A capacity the payload cannot reserve, or craft or soldier values outside the supported range.
            throw new InvalidDataException(error.Message, error);
        }
    }

    private CampaignEventWindow EventLogic(string ruleId, RuntimeEventRule rule, TimeEffects effects)
    {
        var rules = _content.RuntimeRules;
        var payload = rule.Payload;
        if (rule.AdhocMissionScriptTags.Count != 0)
        {
            var inputs = WorldSchedulingInputs(isNewMonth: false);
            var scheduled = _handlers.Operation<ICampaignWorldAdhocScripts>().Schedule(ruleId, inputs.Score, inputs.Funds);
            if (scheduled.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
                throw new InvalidDataException(blocked.Reason);
            foreach (var notification in scheduled.Events) effects.Notify(notification);
        }
        if (_bases.Count == 0) throw new InvalidDataException("A strategic event payload requires a base.");
        var hq = _bases[0];

        RuleHandle<RegionRuleFamily>? region = null;
        (string Name, double Longitude, double Latitude)? city = null;
        if (payload.Regions.Count != 0)
        {
            var regionId = payload.Regions[_random.NextInclusive(0, payload.Regions.Count - 1)];
            // An empty name or STR_NULL names no region: the score goes to research. A city-specific event then reads
            // the cities of no region in the reference; the port refuses it (ADR 0036).
            region = OptionalRule(rules.Regions, regionId, "region");
            if (region is null && payload.CitySpecific)
                throw new InvalidDataException("A city-specific strategic event names no region.");
        }
        // Even an event that is not city-specific picks a city to represent its region (and maybe a country).
        if (region is { } picked && Cities(rules.Regions[picked].Value) is { Count: > 0 } cities)
            city = cities[_random.NextInclusive(0, cities.Count - 1)];

        // 1. Score: the region's X-COM activity, or research.
        if (region is { } scored)
        {
            var target = _regions.Find(state => state.Rule == scored);
            if (target is not null) target.ActivityXcom[^1] = checked(target.ActivityXcom[^1] + payload.Points);
        }
        else _researchScores[^1] = checked(_researchScores[^1] + payload.Points);
        // 2. Funds.
        _funds[^1] = checked(_funds[^1] + payload.Funds);

        // 3. Persons into the HQ: staff by transfer, or soldiers placed by the city.
        if (payload.SpawnedPersons > 0 && payload.SpawnedPersonType.Length != 0)
        {
            if (payload.SpawnedPersonType == "STR_SCIENTIST")
                QueueTransfer(hq, 24, CampaignTransferKind.Scientist, "", payload.SpawnedPersons);
            else if (payload.SpawnedPersonType == "STR_ENGINEER")
                QueueTransfer(hq, 24, CampaignTransferKind.Engineer, "", payload.SpawnedPersons);
            // Mod::getSoldier without an error: an unknown type spawns nobody.
            else if (rules.Soldiers.TryGet(payload.SpawnedPersonType, out var soldierRule))
            {
                if (payload.SpawnedPersons > MaximumEventSoldiers) throw new InvalidDataException(EventSoldierReason);
                var definition = rules.Soldiers[soldierRule].Value;
                for (var index = 0; index < payload.SpawnedPersons; index++)
                {
                    var soldier = SpawnSoldier(payload.SpawnedPersonType, definition, EventNationality(definition, city),
                        payload.SpawnedSoldierTemplate, payload.SpawnedPersonName);
                    QueueTransfer(hq, 24, CampaignTransferKind.Soldier, payload.SpawnedPersonType, 1, soldier: soldier);
                }
            }
        }
        // 3. Soldier lists, merged by rule (std::map by rule address: load order).
        var soldiers = new Dictionary<RuleHandle<SoldierRuleFamily>, long>();
        foreach (var pair in payload.EveryMultiSoldiers) AddSoldiers(pair);
        if (payload.RandomMultiSoldiers.Count != 0)
            foreach (var pair in payload.RandomMultiSoldiers[_random.NextInclusive(0, payload.RandomMultiSoldiers.Count - 1)])
                AddSoldiers(pair);
        if (soldiers.Values.Sum() > MaximumEventSoldiers) throw new InvalidDataException(EventSoldierReason);
        foreach (var entry in rules.Soldiers.Rules)
        {
            var handle = rules.Soldiers.GetRequired(entry.Id);
            for (long index = 0; index < soldiers.GetValueOrDefault(handle); index++)
            {
                // A merged list soldier takes the template, then a new name in place of anything it loaded.
                var soldier = SpawnSoldier(entry.Id, entry.Value, EventNationality(entry.Value, city), payload.SpawnedSoldierTemplate, "");
                QueueTransfer(hq, 24, CampaignTransferKind.Soldier, entry.Id, 1, soldier: soldier);
            }
        }

        // 3. Items into the HQ (std::map by item type: UTF-8 bytes).
        var items = new SortedDictionary<string, int>(Utf8Order);
        foreach (var pair in payload.EveryMultiItems) AddItem(pair.Key, pair.Value);
        foreach (var id in payload.EveryItems) AddItem(id, 1);
        if (payload.RandomItems.Count != 0) AddItem(payload.RandomItems[_random.NextInclusive(0, payload.RandomItems.Count - 1)], 1);
        if (payload.RandomMultiItems.Count != 0)
            foreach (var pair in payload.RandomMultiItems[_random.NextInclusive(0, payload.RandomMultiItems.Count - 1)])
                AddItem(pair.Key, pair.Value);
        if (payload.WeightedItems.Count != 0)
        {
            // WeightedOptions::choose: nothing when every weight is zero.
            var choice = SelectWeighted(payload.WeightedItems, static pair => pair.Value, "Strategic event item weights exceed the supported range.");
            if (choice.HasValue) AddItem(choice.Value.Key, 1);
        }
        List<CampaignEventItemRow> rows = [];
        foreach (var (id, quantity) in items)
        {
            var handle = rules.Items.GetRequired(id);
            if (payload.Invert)
            {
                // Taken from the stores, then from crafts that are not out, base by base.
                var remaining = quantity;
                var removed = 0;
                foreach (var owner in _bases)
                {
                    var stored = owner.Items.GetValueOrDefault(handle);
                    if (stored > 0)
                    {
                        var take = Math.Min(stored, remaining);
                        if (stored == take) owner.Items.Remove(handle); else owner.Items[handle] = stored - take;
                        remaining -= take;
                        removed += take;
                    }
                    if (remaining <= 0) break;
                }
                if (remaining > 0)
                {
                    foreach (var owner in _bases)
                    {
                        for (var index = 0; index < owner.Crafts.Count; index++)
                        {
                            var craft = owner.Crafts[index];
                            var carried = craft.Logistics?.Items.GetValueOrDefault(id) ?? 0;
                            if (carried > 0 && craft.Logistics!.Status != "STR_OUT")
                            {
                                var take = Math.Min(carried, remaining);
                                var cargo = new Dictionary<string, int>(craft.Logistics.Items, StringComparer.Ordinal);
                                if (carried == take) cargo.Remove(id);
                                else cargo[id] = carried - take;
                                owner.Crafts[index] = craft with
                                {
                                    Logistics = craft.Logistics with { Items = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(cargo) },
                                };
                                remaining -= take;
                                removed += take;
                            }
                            if (remaining <= 0) break;
                        }
                        if (remaining <= 0) break;
                    }
                }
                rows.Add(new(id, -removed));
            }
            else if (Options.GeoscapeEventsInstantDelivery)
            {
                hq.Items[handle] = checked(hq.Items.GetValueOrDefault(handle) + quantity);
                rows.Add(new(id, quantity));
            }
            else QueueTransfer(hq, 1, CampaignTransferKind.Item, id, quantity);
        }

        // 3b. A craft into the HQ: ready as if manufactured, or by transfer as if bought.
        if (OptionalRule(rules.Crafts, payload.SpawnedCraftType, "craft") is { } craftRule)
        {
            var type = rules.Crafts.GetExternalId(craftRule);
            var craftId = NextCraftId(type);
            _nextIds[type] = checked(craftId + 1);
            var key = FormattableString.Invariant($"created:{Identity.Id}:craft:{type}:{craftId}");
            if (Options.GeoscapeEventsInstantDelivery)
                hq.Crafts.Add(new(craftRule, craftId) { PreservationKey = key, Logistics = ProducedCraftLogistics(hq, craftRule) });
            else
                QueueTransfer(hq, 1, CampaignTransferKind.Craft, type, 1, craft: new CraftSnapshot(type, craftId)
                {
                    PreservationKey = key,
                    Logistics = CraftLogistics.Purchase(rules.Crafts[craftRule].Value, rules, hq.Longitude, hq.Latitude),
                });
        }

        // 4. Bonus research: an unknown topic, or a known one that can still give a free topic.
        var researchName = "";
        var bonusName = "";
        List<string> topics = [];
        var possibilities = payload.Research.Where(id => !_completedResearch.Contains(id) ||
            HasRemainingResearchReward(rules.Research[rules.Research.GetRequired(id)].Value)).ToArray();
        if (possibilities.Length != 0)
        {
            var id = possibilities[_random.NextInclusive(0, possibilities.Length - 1)];
            var research = rules.Research[rules.Research.GetRequired(id)].Value;
            // An article already seen is not shown again.
            var alreadyResearched = _completedResearch.Contains(research.Lookup.Length != 0 ? research.Lookup : id);
            var discoveries = new List<string>();
            RecordResearchDiary(id, ResearchDiscoverySource.Event, ruleId, considerDebug: false);
            AddFinishedResearch(hq, id, discoveries);
            topics.Add(id);
            researchName = alreadyResearched ? "" : id;
            if (research.Lookup.Length != 0)
            {
                RecordResearchDiary(research.Lookup, ResearchDiscoverySource.Event, ruleId, considerDebug: false);
                AddFinishedResearch(hq, research.Lookup, discoveries);
                researchName = alreadyResearched ? "" : research.Lookup;
            }
            if (SelectResearchReward(research) is { } bonus)
            {
                RecordResearchDiary(bonus, ResearchDiscoverySource.FreeFrom, id, considerDebug: false);
                AddFinishedResearch(hq, bonus, discoveries);
                topics.Add(bonus);
                bonusName = bonus;
                if (rules.Research.TryGet(bonus, out var bonusHandle) && rules.Research[bonusHandle].Value.Lookup is { Length: > 0 } bonusLookup)
                {
                    RecordResearchDiary(bonusLookup, ResearchDiscoverySource.FreeFrom, id, considerDebug: false);
                    AddFinishedResearch(hq, bonusLookup, discoveries);
                    bonusName = bonusLookup;
                }
            }
        }
        // Side effects: obsolete projects, spawned items, spawned events and counters, topic by topic.
        foreach (var topic in topics)
            if (rules.Research.TryGet(topic, out var handle))
                ApplyResearchSideEffects(hq, rules.Research[handle].Value, topic, effects);

        return new CampaignEventWindow(ruleId, region is { } shown ? rules.Regions.GetExternalId(shown) : null, city?.Name,
            rows.AsReadOnly(), researchName, bonusName);

        void AddSoldiers(KeyValuePair<string, int> pair)
        {
            if (OptionalRule(rules.Soldiers, pair.Key, "soldier") is { } handle)
                soldiers[handle] = checked(soldiers.GetValueOrDefault(handle) + pair.Value);
        }

        void AddItem(string id, int quantity)
        {
            // Mod::getItem names no item for STR_NONE.
            if (id == "STR_NONE" || OptionalRule(rules.Items, id, "item") is not { } handle) return;
            var type = rules.Items.GetExternalId(handle);
            items[type] = checked(items.GetValueOrDefault(type) + quantity);
        }
    }

    private const string EventSoldierReason = "A strategic event payload spawns more soldiers than supported.";

    private static readonly Comparer<string> Utf8Order = Comparer<string>.Create(static (left, right) =>
        Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right)));

    /// <summary>Mod::getRule with an error: empty names and <c>STR_NULL</c> name no rule; another unknown name fails.</summary>
    private static RuleHandle<TFamily>? OptionalRule<TFamily, TProjection>(RuntimeRuleFamily<TFamily, TProjection> family, string id,
        string kind) where TProjection : notnull
    {
        if (id.Length == 0 || id == "STR_NULL") return null;
        return family.TryGet(id, out var handle) ? handle
            : throw new InvalidDataException($"Strategic event {kind} '{id}' is not defined.");
    }

    /// <summary>RuleRegion::getCities: the named point areas of its mission zones, in zone order.</summary>
    private static List<(string Name, double Longitude, double Latitude)> Cities(RuntimeRegionRule region)
    {
        List<(string, double, double)> cities = [];
        foreach (var zone in region.MissionZones)
            foreach (var area in zone.Areas)
                if (area.IsPoint && area.Name.Length != 0) cities.Add((area.Name, area.LongitudeMinimum, area.LatitudeMinimum));
        return cities;
    }

    /// <summary>SavedGame::selectSoldierNationalityByLocation for the event's city; without one, no draw.</summary>
    private int EventNationality(RuntimeSoldierRule rule, (string Name, double Longitude, double Latitude)? city) =>
        city is { } target ? SelectNationality(rule, target.Longitude, target.Latitude) : -1;
}
