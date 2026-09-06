using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    public const int MaximumLogisticsLines = 10_000;
    private long _nextQuoteId;
    private LogisticsQuote? _logisticsQuote;

    private BaseState FindBase(int id) => _bases.FirstOrDefault(b => b.Id == id) ??
        throw new ArgumentException($"Base {id} does not exist.", nameof(id));

    private CampaignCommandResult QuoteLogistics(PrepareLogisticsQuote command)
    {
        _logisticsQuote = null;
        if (!Enum.IsDefined(command.Operation)) throw new ArgumentOutOfRangeException(nameof(command));
        var origin = FindBase(command.BaseId);
        var destination = command.Operation == LogisticsOperation.Transfer
            ? FindBase(command.DestinationBaseId ?? throw new ArgumentException("Transfer destination is required.", nameof(command)))
            : origin;
        if (command.Operation == LogisticsOperation.Transfer && origin == destination)
            return Blocked("A transfer needs two different bases.");
        var restriction = LogisticsRestriction(origin) ?? LogisticsRestriction(destination);
        if (restriction is not null) return Blocked(restriction);
        var rows = new List<LogisticsRow>();
        var distance = StrategicLogisticsMath.TransferDistance(origin.Longitude, origin.Latitude, destination.Longitude, destination.Latitude);
        var hours = StrategicLogisticsMath.TransferHours(distance);
        if (command.Operation == LogisticsOperation.Purchase) AddRecruitRows(origin, rows);
        AddStaff(CampaignTransferKind.Scientist, "STR_SCIENTIST", origin.Scientists,
            _content.RuntimeRules.Campaign.CostHireScientist, _content.RuntimeRules.Campaign.HireScientistsUnlockResearch,
            _content.RuntimeRules.Campaign.HireScientistsBaseFunctions);
        AddStaff(CampaignTransferKind.Engineer, "STR_ENGINEER", origin.Engineers,
            _content.RuntimeRules.Campaign.CostHireEngineer, _content.RuntimeRules.Campaign.HireEngineersUnlockResearch,
            _content.RuntimeRules.Campaign.HireEngineersBaseFunctions);
        foreach (var entry in _content.RuntimeRules.Items.Rules)
        {
            var handle = _content.RuntimeRules.Items.GetRequired(entry.Id);
            var rule = entry.Value;
            var owned = origin.Items.GetValueOrDefault(handle);
            if (command.Operation != LogisticsOperation.Purchase && owned == 0) continue;
            string? unavailable = null;
            var cost = command.Operation switch
            {
                LogisticsOperation.Transfer => StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Item),
                _ => ItemPrice(entry.Id, rule, command.Operation == LogisticsOperation.Purchase, out unavailable),
            };
            if (command.Operation == LogisticsOperation.Purchase)
            {
                if (rule.CostBuy == 0) unavailable ??= "This item is not purchasable.";
                if (!_debugMode && rule.Requirements.Any(r => !_completedResearch.Contains(_content.RuntimeRules.Research.GetExternalId(r))))
                    unavailable ??= "Required research is not complete.";
                unavailable ??= PurchaseRestriction(origin, rule.Purchase);
                if (cost == 0) unavailable ??= "The reference purchase path cannot divide by a zero adjusted price.";
            }
            var max = command.Operation == LogisticsOperation.Purchase ? int.MaxValue : owned;
            if (command.Operation != LogisticsOperation.Sell)
            {
                if (command.Operation == LogisticsOperation.Purchase && cost > 0) max = (int)Math.Min(max, Math.Max(0, _funds[^1] / cost));
                if (command.Operation == LogisticsOperation.Purchase && rule.Purchase.MonthlyLimit > 0)
                    max = Math.Min(max, Math.Max(0, rule.Purchase.MonthlyLimit - _monthlyPurchaseLog.GetValueOrDefault(entry.Id)));
                max = StorageLimit(destination, rule, max);
            }
            if (max <= 0) unavailable ??= "Funds, capacity or purchase limit leaves no available quantity.";
            if (unavailable is not null) max = 0;
            rows.Add(new(rows.Count, CampaignTransferKind.Item, entry.Id, 0,
                rule.Name.Length == 0 ? entry.Id : rule.Name, owned, cost, max,
                command.Operation == LogisticsOperation.Transfer ? hours : rule.TransferTime, unavailable));
        }
        if (rows.Count > MaximumLogisticsLines) return Blocked("The logistics catalog exceeds the bounded row limit.");
        var quote = new LogisticsQuote(checked(++_nextQuoteId), origin.Id, command.Operation,
            command.Operation == LogisticsOperation.Transfer ? destination.Id : null, _funds[^1], UsedStores(destination),
            AvailableStores(destination), UsedQuarters(destination), AvailableQuarters(destination), CampaignSnapshot.ReadOnly(rows));
        _logisticsQuote = quote;
        return new CampaignCommandResult([new LogisticsQuoted(quote)]);

        void AddStaff(CampaignTransferKind kind, string label, int owned, int hireCost,
            RuleHandle<ResearchRuleFamily>? unlock, IReadOnlyList<string> functions)
        {
            if (command.Operation != LogisticsOperation.Purchase && owned == 0) return;
            var cost = command.Operation switch
            {
                LogisticsOperation.Purchase => hireCost,
                LogisticsOperation.Transfer => StrategicLogisticsMath.TransferUnitCost(distance, kind),
                _ => 0,
            };
            string? unavailable = null;
            var max = command.Operation == LogisticsOperation.Purchase ? int.MaxValue : owned;
            if (command.Operation == LogisticsOperation.Purchase)
            {
                if (unlock is { } research && !_debugMode && !_completedResearch.Contains(_content.RuntimeRules.Research.GetExternalId(research)))
                    unavailable = "Hiring research is not complete.";
                if (!HasFunctions(origin, functions)) unavailable = "Required base functions are missing.";
                if (cost == 0) unavailable = "The reference purchase path cannot divide by a zero hire price.";
            }
            if (command.Operation != LogisticsOperation.Sell)
            {
                max = Math.Min(max, Math.Max(0, AvailableQuarters(destination) - UsedQuarters(destination)));
                if (command.Operation == LogisticsOperation.Purchase && cost > 0) max = (int)Math.Min(max, Math.Max(0, _funds[^1] / cost));
            }
            if (max <= 0) unavailable ??= "No available staff, funds or living space.";
            rows.Add(new(rows.Count, kind, "", 0, label, owned, cost, unavailable is null ? max : 0,
                command.Operation == LogisticsOperation.Transfer ? hours : _content.RuntimeRules.Campaign.PersonnelTransferTime, unavailable));
        }
    }

    private CampaignCommandResult SubmitLogistics(SubmitLogisticsOrder command)
    {
        ArgumentNullException.ThrowIfNull(command.Lines);
        var quote = _logisticsQuote;
        if (quote is null || quote.Id != command.QuoteId) return Blocked("This quote has expired; reopen the logistics screen.");
        if (command.Lines.Count == 0 || command.Lines.Count > MaximumLogisticsLines)
            return Blocked("An order must contain a bounded non-empty selection.");
        var origin = FindBase(quote.BaseId);
        var destination = FindBase(quote.DestinationBaseId ?? quote.BaseId);
        var seen = new HashSet<int>();
        long subtotal = 0;
        var storageAdded = 0.0;
        long peopleAdded = 0;
        var prisoners = new Dictionary<int, long>();
        foreach (var selection in command.Lines)
        {
            if (!seen.Add(selection.RowId) || (uint)selection.RowId >= (uint)quote.Rows.Count)
                return Blocked("Order contains a duplicate or invalid row.");
            var row = quote.Rows[selection.RowId];
            if (selection.Quantity <= 0 || selection.Quantity > row.MaximumQuantity)
                return Blocked(row.UnavailableReason ?? "Quantity exceeds the legal limit.");
            subtotal = checked(subtotal + (long)selection.Quantity * row.UnitCost);
            if (row.Kind == CampaignTransferKind.Item)
            {
                var rule = _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(row.RuleId)].Value;
                storageAdded += rule.Size * selection.Quantity;
                if (rule.IsAlien) prisoners[rule.PrisonType] = prisoners.GetValueOrDefault(rule.PrisonType) + selection.Quantity;
            }
            else peopleAdded += selection.Quantity;
        }
        if (quote.Operation != LogisticsOperation.Sell)
        {
            if (subtotal is < int.MinValue or > int.MaxValue) return Blocked("Order exceeds the reference 32-bit transaction range.");
            if (storageAdded > 0 && StrategicLogisticsMath.StoresOverfull(AvailableStores(destination), UsedStores(destination), storageAdded))
                return Blocked("STR_NOT_ENOUGH_STORE_SPACE");
            if (peopleAdded > 0 && peopleAdded > AvailableQuarters(destination) - (long)UsedQuarters(destination)) return Blocked("STR_NOT_ENOUGH_LIVING_SPACE");
            foreach (var pair in prisoners)
                if (pair.Value > AvailableContainment(destination, pair.Key) - (long)UsedContainment(destination, pair.Key))
                    return Blocked("STR_NOT_ENOUGH_PRISON_SPACE");
            if (quote.Operation == LogisticsOperation.Transfer)
                subtotal = StrategicLogisticsMath.TransferTotal((int)subtotal, _content.RuntimeRules.Campaign.GlobalTransferCostMultiplier,
                    _content.RuntimeRules.Campaign.GlobalTransferCostDivisor);
            if (subtotal > _funds[^1]) return Blocked("STR_NOT_ENOUGH_MONEY");
        }
        var delta = quote.Operation == LogisticsOperation.Sell ? subtotal : -subtotal;
        var funds = checked(_funds[^1] + delta);
        var accounting = delta > 0 ? checked(_incomes[^1] + delta) : checked(_expenditures[^1] - delta);
        // All eligibility/capacity/cost checks precede stock and fund mutation.
        var transfers = new List<TransferSnapshot>();
        var nextTransferId = NextTransferId();
        var transferCount = quote.Operation == LogisticsOperation.Sell ? 0L : command.Lines.Sum(s =>
            quote.Rows[s.RowId].Kind == CampaignTransferKind.Soldier ? (long)s.Quantity : 1);
        if (transferCount > MaximumLogisticsLines) return Blocked("Order exceeds the bounded transfer count.");
        if (transferCount > int.MaxValue - nextTransferId)
            return Blocked("Transfer identity range is exhausted.");
        var recruitCount = quote.Operation == LogisticsOperation.Purchase ? command.Lines.Where(s =>
            quote.Rows[s.RowId].Kind == CampaignTransferKind.Soldier).Sum(s => s.Quantity) : 0;
        var nextSoldierId = NextSoldierId();
        if (recruitCount > int.MaxValue - nextSoldierId) return Blocked("Soldier identity range is exhausted.");
        var names = _bases.SelectMany(b => b.Soldiers.Select(s => s.Personal?.Name).Concat(
            b.Transfers.Select(t => t.Soldier?.Personal?.Name))).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var selection in command.Lines)
        {
            var row = quote.Rows[selection.RowId];
            if (quote.Operation != LogisticsOperation.Sell)
            {
                if (row.Kind == CampaignTransferKind.Soldier)
                {
                    var rule = _content.RuntimeRules.Soldiers[_content.RuntimeRules.Soldiers.GetRequired(row.RuleId)].Value;
                    for (var i = 0; i < selection.Quantity; i++)
                    {
                        var soldierId = nextSoldierId++;
                        var personal = SoldierGeneration.Generate(rule, _content.RuntimeRules.Armors.GetExternalId(rule.Armor),
                            SelectNationality(rule, origin), names, _random);
                        names.Add(personal.Name);
                        var soldier = new SoldierSnapshot(row.RuleId, soldierId)
                        {
                            Personal = personal, PreservationKey = $"{Identity.Id}:soldier:{soldierId}",
                        };
                        var transferId = nextTransferId++;
                        transfers.Add(new(transferId, row.Hours, row.Kind, row.RuleId, 1, soldier)
                        {
                            PreservationKey = $"{Identity.Id}:transfer:{transferId}",
                        });
                    }
                    continue;
                }
                var id = nextTransferId++;
                transfers.Add(new(id, row.Hours, row.Kind, row.RuleId, selection.Quantity)
                {
                    PreservationKey = $"{Identity.Id}:transfer:{id}",
                });
            }
        }
        foreach (var selection in command.Lines)
        {
            var row = quote.Rows[selection.RowId];
            if (quote.Operation != LogisticsOperation.Purchase) RemoveStock(origin, row, selection.Quantity);
            if (quote.Operation == LogisticsOperation.Purchase && MonthlyLimit(row) > 0)
                _monthlyPurchaseLog[row.RuleId] = checked(_monthlyPurchaseLog.GetValueOrDefault(row.RuleId) + selection.Quantity);
        }
        destination.Transfers.AddRange(transfers);
        _funds[^1] = funds;
        if (transfers.Count != 0) _nextIds["oxcePortTransfer"] = nextTransferId;
        if (recruitCount != 0) _nextIds["STR_SOLDIER"] = nextSoldierId;
        if (delta > 0) _incomes[^1] = accounting; else _expenditures[^1] = accounting;
        _logisticsQuote = null;
        return new CampaignCommandResult([new LogisticsOrderCompleted(origin.Id, quote.Operation, subtotal)]);
    }

    private int NextTransferId()
    {
        const string key = "oxcePortTransfer";
        var minimum = _bases.SelectMany(b => b.Transfers).Select(t => t.Id).DefaultIfEmpty(0).Max();
        return Math.Max(_nextIds.GetValueOrDefault(key, 1), minimum == int.MaxValue ? int.MaxValue : minimum + 1);
    }

    private void RemoveStock(BaseState origin, LogisticsRow row, int count)
    {
        switch (row.Kind)
        {
            case CampaignTransferKind.Scientist: origin.Scientists -= count; break;
            case CampaignTransferKind.Engineer: origin.Engineers -= count; break;
            case CampaignTransferKind.Item:
                var handle = _content.RuntimeRules.Items.GetRequired(row.RuleId);
                var remainder = origin.Items[handle] - count;
                if (remainder == 0) origin.Items.Remove(handle); else origin.Items[handle] = remainder;
                break;
            default: throw new InvalidOperationException("Unsupported stock kind.");
        }
    }

    private string? LogisticsRestriction(BaseState state) =>
        _restrictions.FirstOrDefault(r => r.BlocksLogistics && (r.BaseId is null || r.BaseId == state.Id))?.Feature;

    private string? PurchaseRestriction(BaseState state, RuntimePurchaseRequirements rule)
    {
        if (!_debugMode && rule.BuyResearch.Any(r => !_completedResearch.Contains(r))) return "Required purchase research is not complete.";
        if (!HasFunctions(state, rule.BaseFunctions)) return "Required base functions are missing.";
        if (rule.AlliedCountry.Length != 0 && _countries.Any(c => c.Pact && _content.RuntimeRules.Countries.GetExternalId(c.Rule) == rule.AlliedCountry))
            return "Required country has signed an alien pact.";
        return null;
    }

    private bool HasFunctions(BaseState state, IReadOnlyList<string> required)
    {
        if (required.Count == 0) return true;
        var provided = state.Facilities.Where(f => f.BuildTime <= 0).SelectMany(f => _content.RuntimeRules.Facilities[f.Rule].Value.ProvidedBaseFunctions).ToHashSet(StringComparer.Ordinal);
        // Base location services are added by their containing country/region.
        foreach (var country in _countries)
        {
            var rule = _content.RuntimeRules.Countries[country.Rule].Value;
            if (!rule.Areas.Any(a => AreaContains(a, state.Longitude, state.Latitude))) continue;
            provided.UnionWith(rule.ProvidedBaseFunctions);
            break;
        }
        foreach (var region in _regions)
        {
            var rule = _content.RuntimeRules.Regions[region.Rule].Value;
            if (!rule.Areas.Any(a => AreaContains(a, state.Longitude, state.Latitude))) continue;
            provided.UnionWith(rule.ProvidedBaseFunctions);
            break;
        }
        return required.All(provided.Contains);
    }

    private int AvailableStores(BaseState state) => state.Facilities.Where(f => f.BuildTime == 0).Sum(f => _content.RuntimeRules.Facilities[f.Rule].Value.Storage);
    private int AvailableQuarters(BaseState state) => state.Facilities.Where(f => f.BuildTime == 0).Sum(f => _content.RuntimeRules.Facilities[f.Rule].Value.Personnel);
    private static int UsedQuarters(BaseState state) => checked(state.Soldiers.Count + state.Scientists + state.Engineers + state.Transfers.Where(t => !t.Delivered && t.Kind is CampaignTransferKind.Soldier or CampaignTransferKind.Scientist or CampaignTransferKind.Engineer).Sum(t => t.Quantity));
    private double UsedStores(BaseState state) => state.Items.Sum(p => _content.RuntimeRules.Items[p.Key].Value.Size * p.Value) +
        state.Transfers.Where(t => !t.Delivered && t.Kind == CampaignTransferKind.Item).Sum(t => _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(t.RuleId)].Value.Size * t.Quantity);
    private int AvailableContainment(BaseState state, int prisonType) => state.Facilities.Where(f => f.BuildTime == 0).Select(f => _content.RuntimeRules.Facilities[f.Rule].Value).Where(f => f.PrisonType == prisonType).Sum(f => f.Aliens);
    private int UsedContainment(BaseState state, int prisonType) => state.Items.Where(p => _content.RuntimeRules.Items[p.Key].Value is { IsAlien: true } rule && rule.PrisonType == prisonType).Sum(p => p.Value) +
        state.Transfers.Where(t => !t.Delivered && t.Kind == CampaignTransferKind.Item && _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(t.RuleId)].Value is { IsAlien: true } rule && rule.PrisonType == prisonType).Sum(t => t.Quantity);

    private int StorageLimit(BaseState state, RuntimeItemRule rule, int maximum)
    {
        if (rule.IsAlien) maximum = Math.Min(maximum, Math.Max(0, AvailableContainment(state, rule.PrisonType) - UsedContainment(state, rule.PrisonType)));
        if (rule.Size > 0.000001)
        {
            var limit = (AvailableStores(state) - UsedStores(state) + 0.05) / rule.Size;
            maximum = (int)Math.Min(maximum, Math.Clamp(limit, 0, int.MaxValue));
        }
        return maximum;
    }

    private static CampaignCommandResult Blocked(string reason) => new([new CampaignActionBlocked(reason)]);

    private string? PreflightTransfers()
    {
        foreach (var state in _bases)
        {
            if (state.Transfers.Count == 0) continue;
            long scientists = state.Scientists, engineers = state.Engineers;
            var incoming = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var transfer in state.Transfers)
            {
                if (transfer.Delivered || transfer.Hours > 1) continue;
                switch (transfer.Kind)
                {
                    case CampaignTransferKind.Scientist: scientists += transfer.Quantity; break;
                    case CampaignTransferKind.Engineer: engineers += transfer.Quantity; break;
                    case CampaignTransferKind.Item:
                        var current = incoming.GetValueOrDefault(transfer.RuleId,
                            state.Items.GetValueOrDefault(_content.RuntimeRules.Items.GetRequired(transfer.RuleId)));
                        incoming[transfer.RuleId] = current + transfer.Quantity;
                        if (incoming[transfer.RuleId] > int.MaxValue) return "Arrival exceeds the item quantity range.";
                        break;
                    case CampaignTransferKind.Soldier:
                        if (transfer.Soldier?.Personal is null) return "Soldier arrival requires complete personal state.";
                        break;
                    default: return "Craft arrival requires its entity lifecycle provider.";
                }
            }
            if (scientists > int.MaxValue || engineers > int.MaxValue) return "Arrival exceeds the personnel quantity range.";
        }
        return null;
    }

    private void AdvanceTransfers(TimeEffects effects)
    {
        foreach (var state in _bases)
        {
            List<int>? arrived = null;
            for (var index = 0; index < state.Transfers.Count; index++)
            {
                var transfer = state.Transfers[index];
                if (transfer.Delivered) continue;
                transfer = transfer with { Hours = transfer.Hours - 1 };
                if (transfer.Hours <= 0)
                {
                    switch (transfer.Kind)
                    {
                        case CampaignTransferKind.Scientist: state.Scientists += transfer.Quantity; break;
                        case CampaignTransferKind.Engineer: state.Engineers += transfer.Quantity; break;
                        case CampaignTransferKind.Soldier:
                            var soldier = transfer.Soldier!;
                            state.Soldiers.Add(new(_content.RuntimeRules.Soldiers.GetRequired(soldier.RuleId), soldier.Id)
                            {
                                Personal = soldier.Personal, PreservationKey = soldier.PreservationKey,
                            });
                            break;
                        case CampaignTransferKind.Item:
                            var handle = _content.RuntimeRules.Items.GetRequired(transfer.RuleId);
                            state.Items[handle] = state.Items.GetValueOrDefault(handle) + transfer.Quantity;
                            break;
                        default: throw new InvalidOperationException("Arrival provider was not preflighted.");
                    }
                    (arrived ??= []).Add(transfer.Id);
                    transfer = transfer with { Delivered = true };
                }
                state.Transfers[index] = transfer;
            }
            if (arrived is not null)
            {
                state.Transfers.RemoveAll(static transfer => transfer.Delivered);
                effects.Notify(new SuppliesArrived(state.Id, CampaignSnapshot.ReadOnly(arrived)));
            }
        }
    }

    private static bool AreaContains(GeographicArea area, double longitude, double latitude)
    {
        var inLongitude = area.LongitudeMinimum <= area.LongitudeMaximum
            ? longitude >= area.LongitudeMinimum && longitude < area.LongitudeMaximum
            : longitude >= area.LongitudeMinimum || longitude < area.LongitudeMaximum;
        var inLatitude = latitude > 0 ? latitude > area.LatitudeMinimum && latitude <= area.LatitudeMaximum
            : latitude >= area.LatitudeMinimum && latitude < area.LatitudeMaximum;
        return inLongitude && inLatitude;
    }
}
