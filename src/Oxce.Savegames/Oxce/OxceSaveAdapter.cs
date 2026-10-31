using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Scripting.Globals;

namespace Oxce.Savegames.Oxce;

/// <summary>
/// Reads and writes the strategic subset of the two-document OXCE save stream. Unknown
/// fields remain owned by <see cref="OxceSaveDocument"/> and are overlaid on write.
/// Reference: SavedGame.cpp, GameTime.cpp, Country.cpp, Region.cpp, Base.cpp, and
/// BaseFacility.cpp at OXCE commit 4df3a5e.
/// </summary>
public static partial class OxceSaveAdapter
{
    private static readonly string[] TransferPayloadKeys = ["soldier", "craft", "itemId", "scientists", "engineers"];
    internal static readonly SourceSpan GeneratedSpan = new(
        "(generated OXCE save)", new SourcePosition(1, 1, 0), new SourcePosition(1, 1, 0));

    public static LoadedOxceCampaign LoadFile(
        string path,
        RuntimeContent content,
        IStatefulRandomSource random,
        OxceSaveLoadOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var fullPath = Path.GetFullPath(path);
        var maximumBytes = options.Yaml?.MaxBytes ?? YamlReadOptions.DefaultMaxBytes;
        if (new FileInfo(fullPath).Length > maximumBytes)
            throw new InvalidDataException($"OXCE save exceeds the {maximumBytes}-byte YAML input limit.");
        var bytes = File.ReadAllBytes(fullPath);
        var yaml = YamlCompatibilityReader.DecodeText(bytes, out var legacyEncoding);
        var loaded = Load(yaml, fullPath, bytes, content, random, options);
        // Reported only after a successful load so a rejected save leaves no partial output.
        if (legacyEncoding) options.Diagnostics?.Report(YamlCompatibilityReader.LegacyEncodingWarning(fullPath));
        return loaded;
    }

    public static LoadedOxceCampaign Load(
        string yaml,
        string sourceName,
        RuntimeContent content,
        IStatefulRandomSource random,
        OxceSaveLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return Load(yaml, sourceName, Encoding.UTF8.GetBytes(yaml), content, random, options);
    }

    public static string EmitNewCampaign(
        CampaignSnapshot snapshot,
        OxceSaveWriteOptions? options = null) => Emit(snapshot, null, options);

    public static string EmitLoadedCampaign(
        CampaignSnapshot snapshot,
        OxceSaveDocument source,
        OxceSaveWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Emit(snapshot, source, options);
    }

    private static string Emit(
        CampaignSnapshot snapshot,
        OxceSaveDocument? source,
        OxceSaveWriteOptions? options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new OxceSaveWriteOptions();
        var header = BuildHeader(snapshot, source, options);
        var body = BuildBody(snapshot, source, options.GlobeView);
        return YamlCompatibilityWriter.Emit(new YamlDocumentSet(
            "(generated OXCE save)",
            [new YamlDocument(header, GeneratedSpan), new YamlDocument(body, GeneratedSpan)]),
            options.Yaml);
    }

    public static void WriteNewCampaignAtomic(
        string path,
        CampaignSnapshot snapshot,
        OxceSaveWriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        WriteAtomic(path, snapshot, null, options, cancellationToken);

    public static void RewriteLoadedCampaignAtomic(
        string path,
        CampaignSnapshot snapshot,
        OxceSaveDocument source,
        OxceSaveWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        WriteAtomic(path, snapshot, source, options, cancellationToken);
    }

    private static void WriteAtomic(
        string path,
        CampaignSnapshot snapshot,
        OxceSaveDocument? source,
        OxceSaveWriteOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var output = Emit(snapshot, source, options);
        cancellationToken.ThrowIfCancellationRequested();
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target) ??
            throw new ArgumentException("Save path has no parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var backup = target + ".bak";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       81920, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 81920, leaveOpen: true))
            {
                writer.Write(output);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(target))
                File.Replace(temporary, target, backup, ignoreMetadataErrors: true);
            else
                File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static LoadedOxceCampaign Load(
        string yaml,
        string sourceName,
        byte[] identityBytes,
        RuntimeContent content,
        IStatefulRandomSource random,
        OxceSaveLoadOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var documents = YamlCompatibilityReader.Parse(yaml, sourceName, options.Yaml);
        if (documents.Documents.Count != 2 ||
            documents.Documents[0].Root is not YamlMappingNode header ||
            documents.Documents[1].Root is not YamlMappingNode body)
            throw new InvalidDataException("OXCE saves must contain exactly two mapping documents.");
        RejectDuplicateKnownKeys(header, ["name", "time", "mods", "ironman", "oxcePortCampaignId"]);
        RejectDuplicateKnownKeys(body,
            ["difficulty", "end", "monthsPassed", "daysPassed", "rng", "funds", "maintenance", "incomes",
                "expenditures", "researchScores", "ids", "countries", "regions", "bases", "tags", "warned"]);

        var modLabels = Sequence(header, "mods").Select(YamlValueReader.ReadString).ToArray();
        var modIds = modLabels.Select(SanitizeModName).ToArray();
        if (modIds.Length == 0) modIds = ["xcom1"];
        if (!modIds.Contains(options.MasterId, StringComparer.Ordinal))
            throw new InvalidDataException($"Save does not target active master '{options.MasterId}'.");
        var missingMods = modIds.Where(id => !options.ActiveMods.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (missingMods.Length != 0)
            throw new InvalidDataException($"Save requires inactive mod(s): {string.Join(", ", missingMods)}.");

        var time = ReadTime(RequiredMap(header, "time"));
        var name = header.ReadString("name", Path.GetFileNameWithoutExtension(sourceName));
        var campaignId = TryString(header, "oxcePortCampaignId", out var idText) && Guid.TryParse(idText, out var id)
            ? new CampaignId(id)
            : new CampaignId(DeriveGuid(identityBytes));
        var created = TryString(header, "oxcePortCreatedAtUtc", out var createdText) &&
            DateTimeOffset.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var createdAt)
            ? createdAt.ToUniversalTime()
            : DateTimeOffset.UnixEpoch;
        var identity = new CampaignIdentity(
            campaignId,
            name,
            options.MasterId,
            Array.AsReadOnly(modIds),
            created,
            header.ReadBoolean("ironman", false));
        var world = ReadWorld(body, content);
        var markers = new WorldMarkerIndex(world.MissionSites, world.AlienBases, content, body);
        var difficultyValue = body.ReadInt32("difficulty", 0);
        if (difficultyValue is < 0 or > 4) throw new InvalidDataException("Save difficulty is outside 0..4.");
        var snapshot = new CampaignSnapshot(
            identity,
            (CampaignDifficulty)difficultyValue,
            time,
            body.ReadInt32("end", 0),
            body.ReadInt32("monthsPassed", -1),
            body.ReadInt32("daysPassed", 0),
            body.ReadUInt64("rng", 0),
            LongHistory(body, "funds", [0]),
            LongHistory(body, "maintenance", [0]),
            LongHistory(body, "incomes", [0]),
            LongHistory(body, "expenditures", [0]),
            IntHistory(body, "researchScores", [0]),
            ReadIds(body),
            ReadCountries(body, content),
            ReadRegions(body),
            ReadBases(body, content, markers),
            ReadScriptValues(body, content, "GeoscapeGame"))
        {
            World = world,
            Battle = ReadBattle(body, content, options.Diagnostics),
            DeadSoldiers = ReadDeadSoldiers(body, content),
            // SavedGame::load accepts repeated topics; the port keeps one entry per topic.
            CompletedResearch = Array.AsReadOnly(Sequence(body, "discovered").Select(YamlValueReader.ReadString)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
            ResearchRuleStatus = ReadIntMap(body, "researchRuleStatus"),
            ResearchDiary = ReadResearchDiary(body, content),
            ResearchDiarySequence = body.ReadUInt64("oxcePortResearchDiarySequence", 0),
            ManufactureRuleStatus = ReadIntMap(body, "manufactureRuleStatus"),
            MonthlyPurchaseLog = ReadIntMap(body, "monthlyPurchaseLimitLog"),
            // Read only: the node stays as the reference wrote it.
            CustomRuleCraftDeployments = body.TryGet("customRuleCraftDeployments", out var craftDeployments)
                ? new ReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>>(YamlValueReader.ReadMap(craftDeployments!,
                    YamlValueReader.ReadString, node => (IReadOnlyList<IReadOnlyList<int>>)Array.AsReadOnly(
                        YamlValueReader.ReadSequence(node, point => (IReadOnlyList<int>)Array.AsReadOnly(
                            YamlValueReader.ReadSequence(point, YamlValueReader.ReadInt32)))), StringComparer.Ordinal))
                : CampaignSnapshot.EmptyCraftDeployments,
            DebugMode = body.ReadBoolean("debug", false),
            Warned = body.ReadBoolean("warned", false),
            MissionStatistics = Array.AsReadOnly(Maps(body, "missionStatistics").Select(ReadMissionStatistic).ToArray()),
            Options = body.TryGet("oxcePortOptions", out var optionsNode)
                ? ReadOptions(RequireMap(optionsNode!, "oxcePortOptions"))
                : ReadReferenceOptions(body),
        };
        var campaign = CampaignState.Restore(snapshot, content, random, options.BattleTerrainFiles);
        return new LoadedOxceCampaign(campaign,
            new OxceSaveDocument(header, body, Array.AsReadOnly(modLabels), new OxceGlobeView(
                body.ReadDouble("globeLon", 0), body.ReadDouble("globeLat", 0), body.ReadInt32("globeZoom", 0))));
    }

    // SavedGame deadSoldiers: each soldier as it died, with SoldierDeath's time; the cause stays in the node.
    private static ReadOnlyCollection<DeadSoldierSnapshot> ReadDeadSoldiers(YamlMappingNode body, RuntimeContent content)
    {
        var defaultSoldier = content.RuntimeRules.Soldiers.Rules.Count == 0 ? string.Empty : content.RuntimeRules.Soldiers.Rules[0].Id;
        return Array.AsReadOnly(Sequence(body, "deadSoldiers").Select(item =>
        {
            var map = RequireMap(item, "dead soldier");
            var death = map.TryGet("death", out var deathNode) ? RequireMap(deathNode!, "soldier death") : null;
            var time = death is not null && death.TryGet("time", out var timeNode) ? ReadTime(RequireMap(timeNode!, "death time")) : (CampaignTime?)null;
            return new DeadSoldierSnapshot(ReadSoldier(map, defaultSoldier, content), time);
        }).ToArray());
    }

    private static YamlSequenceNode? BuildDeadSoldiers(IReadOnlyList<DeadSoldierSnapshot> dead, YamlMappingNode? body)
    {
        if (dead.Count == 0) return null;
        EnsureUnique(dead.Select(static item => item.Soldier.Id), "dead soldier ID");
        var buried = new Dictionary<int, YamlMappingNode>();
        foreach (var map in Maps(body, "deadSoldiers")) buried.TryAdd(map.ReadInt32("id", 0), map);
        // A soldier who died in this session overlays its node from the living soldiers.
        var living = IndexEntities(body).Soldiers;
        var index = new SaveEntityIndex();
        foreach (var item in dead)
            if ((buried.GetValueOrDefault(item.Soldier.Id) ?? living.GetValueOrDefault(item.Soldier.Id)) is { } node)
                index.Soldiers[item.Soldier.Id] = node;
        return Sequence(dead.Select(item =>
        {
            var source = buried.GetValueOrDefault(item.Soldier.Id);
            var death = source is not null && source.TryGet("death", out var savedDeath) ? savedDeath as YamlMappingNode : null;
            var deathNode = item.DiedAt is not { } time ? death : Overlay(death, [Pair("time", Time(time))]);
            // A soldier who died in this session leaves the live node behind: Soldier::die cleared its
            // equipment layouts and promotion flag.
            return Overlay(BuildSoldier(item.Soldier, index), source is not null ? [Pair("death", deathNode)] :
            [
                Pair("equipmentLayout", null), Pair("personalEquipmentLayout", null), Pair("recentlyPromoted", null),
                Pair("death", deathNode),
            ]);
        }));
    }

    /// <summary>
    /// SavedGame::load's <c>battleGame</c> (Phase 7 branch 2 S5), read by <see cref="OxceBattleSave.Read"/>; none when absent.
    /// The reference loads any present node (even a null one), whose missing <c>selectedUnit</c> then fails the load, so a
    /// node that is no mapping is a corrupted save.
    /// </summary>
    private static Gameplay.Battles.BattleSnapshot? ReadBattle(YamlMappingNode body, RuntimeContent content, IDiagnosticSink? diagnostics) =>
        !body.TryGet("battleGame", out var battle) ? null
        : battle is YamlMappingNode map ? OxceBattleSave.Read(map, content, diagnostics)
        : throw new InvalidDataException("Battle save 'battleGame' is not a mapping.");

    private static bool HasContents(YamlMappingNode map, string key) => map.TryGet(key, out var node) && node switch
    {
        YamlSequenceNode sequence => sequence.Items.Count != 0,
        YamlMappingNode mapping => mapping.Entries.Count != 0,
        _ => false,
    };

    private static ReadOnlyCollection<CountrySnapshot> ReadCountries(YamlMappingNode body, RuntimeContent content) =>
        Array.AsReadOnly(Sequence(body, "countries").Select(node =>
        {
            var map = RequireMap(node, "country");
            return new CountrySnapshot(
                RequiredString(map, "type"),
                IntHistory(map, "funding", null),
                IntHistory(map, "activityXcom", [0]),
                IntHistory(map, "activityAlien", [0]),
                map.ReadBoolean("pact", false),
                map.ReadBoolean("newPact", false),
                map.ReadBoolean("cancelPact", false),
                ReadScriptValues(map, content, "Country"));
        }).ToArray());

    private static ReadOnlyCollection<RegionSnapshot> ReadRegions(YamlMappingNode body) =>
        Array.AsReadOnly(Sequence(body, "regions").Select(node =>
        {
            var map = RequireMap(node, "region");
            return new RegionSnapshot(
                RequiredString(map, "type"),
                IntHistory(map, "activityXcom", [0]),
                IntHistory(map, "activityAlien", [0]));
        }).ToArray());

    private static ReadOnlyCollection<BaseSnapshot> ReadBases(
        YamlMappingNode body, RuntimeContent content, WorldMarkerIndex markers)
    {
        var entityIndex = IndexEntities(body);
        var defaultSoldier = content.RuntimeRules.Soldiers.Rules.Count == 0
            ? string.Empty
            : content.RuntimeRules.Soldiers.Rules[0].Id;
        return Array.AsReadOnly(IdentifyBases(body).Select(identified =>
        {
            var map = identified.Value;
            var facilities = Sequence(map, "facilities").Select(item =>
            {
                var facility = RequireMap(item, "facility");
                return new FacilitySnapshot(
                    RequiredString(facility, "type"), facility.ReadInt32("x", -1), facility.ReadInt32("y", -1),
                    facility.ReadInt32("buildTime", 0), facility.ReadInt32("ammo", 0),
                    facility.ReadBoolean("ammoMissingReported", false), facility.ReadBoolean("disabled", false),
                    facility.ReadBoolean("hadPreviousFacility", false))
                { PreservationKey = facility.ReadString("oxcePortEntityKey", string.Empty) };
            }).ToArray();
            var crafts = Sequence(map, "crafts").Select(item =>
            {
                var craft = RequireMap(item, "craft");
                return ReadCraft(craft, markers, content);
            }).ToArray();
            var soldiers = Sequence(map, "soldiers").Select(item =>
            {
                var soldier = RequireMap(item, "soldier");
                return ReadSoldier(soldier, defaultSoldier, content);
            }).ToArray();
            var items = ReadIntMap(map, "items");
            // Base::load skips projects whose rules are no longer defined and returns their staff.
            var scientists = map.ReadInt32("scientists", 0);
            var research = KnownProjects(Maps(map, "research").Select(project => new ResearchProjectSnapshot(
                    RequiredString(project, "project"), project.ReadInt32("assigned", 0),
                    project.ReadInt32("spent", 0), project.ReadInt32("cost", 0))),
                project => content.RuntimeRules.Research.TryGet(project.RuleId, out _),
                project => project.Assigned, ref scientists);
            var engineers = map.ReadInt32("engineers", 0);
            var productions = KnownProjects(Maps(map, "productions").Select(ReadProduction),
                production => content.RuntimeRules.Manufacture.TryGet(production.RuleId, out _),
                production => production.Assigned, ref engineers);
            return new BaseSnapshot(
                identified.Id, map.ReadString("name", string.Empty), map.ReadDouble("lon", 0),
                map.ReadDouble("lat", 0), Array.AsReadOnly(facilities), Array.AsReadOnly(crafts),
                Array.AsReadOnly(soldiers), items, scientists, engineers)
            {
                Transfers = Array.AsReadOnly(Maps(map, "transfers").Select(transfer =>
                    ReadTransfer(transfer, entityIndex.TransferIds[transfer], defaultSoldier, markers, content)).ToArray()),
                FakeUnderwater = map.ReadBoolean("fakeUnderwater", false),
                RetaliationTarget = map.ReadBoolean("retaliationTarget", false),
                InBattlescape = map.ReadBoolean("inBattlescape", false),
                RetaliationMissionId = map.TryGet("retaliationMissionUniqueId", out _)
                    ? map.ReadInt32("retaliationMissionUniqueId", 0) : null,
                Research = research,
                Productions = productions,
            };
        }).ToArray());
    }

    private static ReadOnlyCollection<T> KnownProjects<T>(
        IEnumerable<T> projects, Func<T, bool> isKnown, Func<T, int> assigned, ref int releasedStaff)
    {
        var known = new List<T>();
        foreach (var project in projects)
        {
            if (isKnown(project))
            {
                known.Add(project);
                continue;
            }
            if (assigned(project) < 0)
                throw new InvalidDataException("Assigned project staff cannot be negative.");
            try { releasedStaff = checked(releasedStaff + assigned(project)); }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("Base staff released from unknown projects exceeds the supported range.",
                    exception);
            }
        }
        return Array.AsReadOnly(known.ToArray());
    }

    private static ProductionSnapshot ReadProduction(YamlMappingNode production)
    {
        var amount = production.ReadInt32("amount", 0);
        var legacyInfinite = amount == int.MaxValue;
        return new ProductionSnapshot(
            RequiredString(production, "item"), production.ReadInt32("assigned", 0),
            production.ReadInt32("spent", 0), legacyInfinite ? 999 : amount,
            legacyInfinite || production.ReadBoolean("infinite", false),
            legacyInfinite || production.ReadBoolean("sell", false),
            production.ReadBoolean("isFallback", false),
            ReadIntMap(production, "randomProductionInfo"));
    }

    private static ReadOnlyCollection<ScriptValueEntry> ReadScriptValues(
        YamlMappingNode owner,
        RuntimeContent content,
        string ownerType)
    {
        if (!owner.TryGet("tags", out var node)) return Array.AsReadOnly(Array.Empty<ScriptValueEntry>());
        var mapping = RequireMap(node!, "tags");
        var type = content.Tags.Types.FirstOrDefault(candidate => candidate.Name == ownerType) ??
            throw new InvalidDataException($"Save contains tags for unavailable script type '{ownerType}'.");
        var values = new List<ScriptValueEntry>();
        foreach (var entry in mapping.Entries)
        {
            var name = entry.ScalarKey ?? throw new InvalidDataException("Script tag names must be scalars.");
            if (!content.Tags.TryGetTag(type.Id, "Tag." + name, out var tag))
                throw new InvalidDataException($"Save references unknown {ownerType} tag '{name}'.");
            values.Add(new ScriptValueEntry(name, tag!.ValueType, YamlValueReader.ReadInt32(entry.Value)));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static YamlMappingNode BuildHeader(
        CampaignSnapshot snapshot,
        OxceSaveDocument? source,
        OxceSaveWriteOptions options)
    {
        var labels = source is not null &&
            source.ModLabels.Select(SanitizeModName).SequenceEqual(snapshot.Identity.ActiveMods)
            ? source.ModLabels
            : snapshot.Identity.ActiveMods;
        return Overlay(source?.Header,
        [
            Pair("name", Scalar(snapshot.Identity.Name)),
            Pair("version", Scalar(options.Version)),
            Pair("engine", Scalar(options.Engine)),
            Pair("build", Scalar(options.Build)),
            Pair("time", Time(snapshot.Time)),
            // SavedGame::save writes the battle's mission type, target, craft or base line and turn while a battle exists.
            Pair("mission", snapshot.Battle is { } battle ? Scalar(battle.MissionType) : null),
            Pair("target", snapshot.Battle is { } target ? Scalar(target.TargetName) : null),
            Pair("craftOrBase", snapshot.Battle is { } craftOrBase
                ? new YamlScalarNode(GeneratedSpan, craftOrBase.CraftOrBaseName, YamlScalarStyle.DoubleQuoted) : null),
            Pair("turn", snapshot.Battle is { } turn ? Integer(turn.Turn) : null),
            Pair("mods", Sequence(labels.Select(Scalar))),
            Pair("ironman", snapshot.Identity.Ironman ? Boolean(true) : null),
            Pair("oxcePortCampaignId", Scalar(snapshot.Identity.Id.ToString())),
            Pair("oxcePortCreatedAtUtc", Scalar(snapshot.Identity.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture))),
        ]);
    }

    private static YamlMappingNode BuildBody(CampaignSnapshot snapshot, OxceSaveDocument? source, OxceGlobeView? globeView)
    {
        // SavedGame::save always writes the globe view; a rewrite without a new view keeps the loaded one.
        var view = globeView ?? (source is null ? new OxceGlobeView(0, 0, 0) : null);
        if (view is not null && (!double.IsFinite(view.Longitude) || !double.IsFinite(view.Latitude)))
            throw new InvalidDataException("The globe view is not a finite position.");
        var originalCountries = Maps(source?.Body, "countries").ToDictionary(
            map => map.ReadString("type", string.Empty), StringComparer.Ordinal);
        var originalRegions = Maps(source?.Body, "regions").ToDictionary(
            map => map.ReadString("type", string.Empty), StringComparer.Ordinal);
        EnsureUnique(snapshot.Bases.Select(static item => item.Id), "base ID");
        var originalBases = IdentifyBases(source?.Body).ToDictionary(static item => item.Id, static item => item.Value);
        var entityIndex = IndexEntities(source?.Body);
        var originalMissions = FirstByKey(Maps(source?.Body, "alienMissions"),
            static map => map.ReadString("uniqueID", string.Empty));
        var originalUfos = FirstByKey(Maps(source?.Body, "ufos"),
            static map => map.ReadString("uniqueId", string.Empty));
        var originalWaypoints = FirstByKey(Maps(source?.Body, "waypoints"),
            static map => map.ReadString("id", string.Empty));
        var originalSites = FirstByKey(Maps(source?.Body, "missionSites"), static map =>
            $"{map.ReadString("deployment", "STR_TERROR_MISSION")}:{map.ReadInt32("id", 0)}");
        var originalAlienBases = MatchAlienBases(snapshot.World.AlienBases, Maps(source?.Body, "alienBases"));
        var originalEvents = MatchScheduledEvents(snapshot.World.Events, Maps(source?.Body, "geoscapeEvents"));
        YamlMappingNode? originalStrategy = null;
        if (source?.Body is { } sourceBody && sourceBody.TryGet("alienStrategy", out var strategyNode))
            originalStrategy = strategyNode as YamlMappingNode;
        YamlMappingNode? originalBattle = null;
        if (source?.Body is { } battleBody && battleBody.TryGet("battleGame", out var battleNode))
            originalBattle = battleNode as YamlMappingNode;
        EnsureUnique(snapshot.Bases.SelectMany(b => b.Soldiers.Select(s => s.Id).Concat(
            b.Transfers.Where(t => !t.Delivered && t.Soldier is not null).Select(t => t.Soldier!.Id))), "soldier ID");
        EnsureUnique(snapshot.Bases.SelectMany(b => b.Crafts.Select(EntityIdentity).Concat(
            b.Transfers.Where(t => !t.Delivered && t.Craft is not null).Select(t => EntityIdentity(t.Craft!)))), "craft identity");
        EnsureUnique(snapshot.Bases.SelectMany(b => b.Transfers.Select(t => t.Id)), "transfer ID");
        var countries = snapshot.Countries.Select(country => BuildCountry(
            country, originalCountries.GetValueOrDefault(country.RuleId))).ToArray();
        var regions = snapshot.Regions.Select(region => BuildRegion(
            region, originalRegions.GetValueOrDefault(region.RuleId))).ToArray();
        var bases = snapshot.Bases.Select(item => BuildBase(
            item, originalBases.GetValueOrDefault(item.Id), entityIndex)).ToArray();
        return Overlay(source?.Body,
        [
            Pair("difficulty", Integer((int)snapshot.Difficulty)),
            Pair("end", Integer(snapshot.Ending)),
            Pair("monthsPassed", Integer(snapshot.MonthsPassed)),
            Pair("daysPassed", Integer(snapshot.DaysPassed)),
            Pair("rng", ULong(snapshot.RandomState)),
            Pair("funds", Sequence(snapshot.Funds.Select(Long))),
            Pair("maintenance", Sequence(snapshot.Maintenance.Select(Long))),
            Pair("incomes", Sequence(snapshot.Incomes.Select(Long))),
            Pair("expenditures", Sequence(snapshot.Expenditures.Select(Long))),
            Pair("researchScores", Sequence(snapshot.ResearchScores.Select(Integer))),
            .. (view is null ? [] : new[]
            {
                Pair("globeLon", Real(view.Longitude)),
                Pair("globeLat", Real(view.Latitude)),
                Pair("globeZoom", Integer(view.Zoom)),
            }),
            Pair("ids", IntMapping(snapshot.NextIds)),
            Pair("countries", Sequence(countries)),
            Pair("regions", Sequence(regions)),
            Pair("bases", Sequence(bases)),
            Pair("alienMissions", BuildWorldSection(snapshot.World.Missions.Count,
                snapshot.World.Missions.Select(mission => BuildAlienMission(mission,
                    originalMissions.GetValueOrDefault(mission.Id.ToString(CultureInfo.InvariantCulture)))))),
            Pair("ufos", BuildWorldSection(snapshot.World.Ufos.Count,
                snapshot.World.Ufos.Select(ufo => BuildUfo(ufo,
                    originalUfos.GetValueOrDefault(ufo.UniqueId.ToString(CultureInfo.InvariantCulture)),
                    snapshot.MonthsPassed == -1)))),
            Pair("waypoints", BuildWorldSection(snapshot.World.Waypoints.Count,
                snapshot.World.Waypoints.Select(waypoint => Overlay(
                    originalWaypoints.GetValueOrDefault(waypoint.Id.ToString(CultureInfo.InvariantCulture)),
                [
                    Pair("lon", Real(waypoint.Longitude)), Pair("lat", Real(waypoint.Latitude)),
                    Pair("id", waypoint.Id == 0 ? null : Integer(waypoint.Id)),
                    Pair("name", waypoint.Name.Length == 0 ? null : Scalar(waypoint.Name)),
                ])))),
            Pair("missionSites", BuildWorldSection(snapshot.World.MissionSites.Count,
                snapshot.World.MissionSites.Select(site => BuildMissionSite(site,
                    originalSites.GetValueOrDefault($"{site.DeploymentId}:{site.Id}"))))),
            Pair("alienBases", BuildWorldSection(snapshot.World.AlienBases.Count,
                snapshot.World.AlienBases.Select((alienBase, index) => BuildAlienBase(alienBase,
                    originalAlienBases[index])))),
            Pair("geoscapeEvents", BuildWorldSection(snapshot.World.Events.Count,
                snapshot.World.Events.Select((scheduled, index) => Overlay(originalEvents[index],
                [
                    Pair("name", Scalar(scheduled.RuleId)),
                    Pair("oxcePortEntityKey", Scalar(scheduled.PreservationKey)),
                    Pair("spawnCountdown", Integer(scheduled.SpawnCountdown)),
                    Pair("over", scheduled.Over ? Boolean(true) : null),
                ])))),
            Pair("alienStrategy", BuildAlienStrategy(snapshot.World.Strategy, originalStrategy)),
            Pair("generatedEvents", IntMapping(snapshot.World.GeneratedEvents)),
            Pair("craftLostDogfight", Integer(snapshot.World.CraftLostDogfight)),
            Pair("oxcePortDeploymentRequest", BuildDeploymentRequest(snapshot.World.DeploymentRequest)),
            Pair("oxcePortAlienBaseBirthSequence", snapshot.World.AlienBaseBirthSequence == 0
                ? null : ULong(snapshot.World.AlienBaseBirthSequence)),
            // Legacy terror sites were imported as mission sites, so the legacy node is not rewritten.
            Pair("terrorSites", null),
            // SavedGame::save writes the battle after the options' snapshot and before the script values.
            Pair("battleGame", snapshot.Battle is { } battle ? OxceBattleSave.Write(battle, originalBattle) : null),
            Pair("tags", ScriptValues(snapshot.ScriptValues)),
            Pair("deadSoldiers", BuildDeadSoldiers(snapshot.DeadSoldiers, source?.Body)),
            Pair("discovered", snapshot.CompletedResearch.Count == 0 ? null : Sequence(snapshot.CompletedResearch.Select(Scalar))),
            Pair("researchRuleStatus", snapshot.ResearchRuleStatus.Count == 0 ? null :
                IntMapping(snapshot.ResearchRuleStatus)),
            Pair("researchDiary", WriteResearchDiary(snapshot.ResearchDiary, source?.Body)),
            Pair("oxcePortResearchDiarySequence", snapshot.ResearchDiarySequence == 0 ? null : ULong(snapshot.ResearchDiarySequence)),
            Pair("manufactureRuleStatus", snapshot.ManufactureRuleStatus.Count == 0 ? null :
                IntMapping(snapshot.ManufactureRuleStatus)),
            Pair("monthlyPurchaseLimitLog", snapshot.MonthlyPurchaseLog.Count == 0 ? null : IntMapping(snapshot.MonthlyPurchaseLog)),
            Pair("debug", snapshot.DebugMode ? Boolean(true) : null),
            // SavedGame::save always writes it.
            Pair("warned", Boolean(snapshot.Warned)),
            Pair("oxcePortOptions", BuildOptions(snapshot.Options)),
        ]);
    }

    private static YamlMappingNode BuildCountry(CountrySnapshot country, YamlMappingNode? source) => Overlay(source,
    [
        Pair("type", Scalar(country.RuleId)),
        Pair("funding", Sequence(country.Funding.Select(Integer))),
        Pair("activityXcom", Sequence(country.ActivityXcom.Select(Integer))),
        Pair("activityAlien", Sequence(country.ActivityAlien.Select(Integer))),
        Pair("pact", country.Pact ? Boolean(true) : null),
        Pair("newPact", country.NewPact ? Boolean(true) : null),
        Pair("cancelPact", country.CancelPact ? Boolean(true) : null),
        Pair("tags", ScriptValues(country.ScriptValues)),
    ]);

    private static YamlMappingNode BuildRegion(RegionSnapshot region, YamlMappingNode? source) => Overlay(source,
    [
        Pair("type", Scalar(region.RuleId)),
        Pair("activityXcom", Sequence(region.ActivityXcom.Select(Integer))),
        Pair("activityAlien", Sequence(region.ActivityAlien.Select(Integer))),
    ]);

    private static YamlMappingNode BuildBase(BaseSnapshot value, YamlMappingNode? source, SaveEntityIndex entityIndex)
    {
        EnsureUnique(value.Facilities.Select(FacilityIdentity), "facility identity");
        var oldFacilities = Maps(source, "facilities").ToDictionary(FacilityIdentity);
        EnsureUnique(value.Research.Select(static project => project.RuleId), "research project");
        EnsureUnique(value.Productions.Select(static production => production.RuleId), "production project");
        var oldResearch = FirstByKey(Maps(source, "research"), static project => RequiredString(project, "project"));
        var oldProductions = FirstByKey(Maps(source, "productions"), static production => RequiredString(production, "item"));
        var facilities = value.Facilities.Select(facility => Overlay(
            oldFacilities.GetValueOrDefault(FacilityIdentity(facility)),
            [
                Pair("type", Scalar(facility.RuleId)), Pair("x", Integer(facility.X)), Pair("y", Integer(facility.Y)),
                Pair("buildTime", facility.BuildTime == 0 ? null : Integer(facility.BuildTime)),
                Pair("ammo", facility.Ammo == 0 ? null : Integer(facility.Ammo)),
                Pair("ammoMissingReported", facility.AmmoMissingReported ? Boolean(true) : null),
                Pair("disabled", facility.Disabled ? Boolean(true) : null),
                Pair("hadPreviousFacility", facility.HadPreviousFacility ? Boolean(true) : null),
                Pair("oxcePortEntityKey", facility.PreservationKey.Length == 0 ? null : Scalar(facility.PreservationKey)),
            ])).ToArray();
        var crafts = value.Crafts.Select(craft => BuildCraft(craft, entityIndex)).ToArray();
        var soldiers = value.Soldiers.Select(soldier => BuildSoldier(soldier, entityIndex)).ToArray();
        return Overlay(source,
        [
            Pair("lon", Real(value.Longitude)), Pair("lat", Real(value.Latitude)),
            Pair("id", Integer(value.Id)),
            Pair("name", value.Name.Length == 0 ? null : Scalar(value.Name)),
            Pair("fakeUnderwater", value.FakeUnderwater ? Boolean(true) : null),
            Pair("retaliationTarget", value.RetaliationTarget ? Boolean(true) : null),
            Pair("retaliationMissionUniqueId", value.RetaliationMissionId is { } missionId ? Integer(missionId) : null),
            Pair("facilities", Sequence(facilities)), Pair("soldiers", Sequence(soldiers)),
            Pair("crafts", Sequence(crafts)),
            Pair("items", IntMapping(value.Items)),
            Pair("scientists", Integer(value.Scientists)), Pair("engineers", Integer(value.Engineers)),
            Pair("inBattlescape", value.InBattlescape ? Boolean(true) : null),
            Pair("transfers", value.Transfers.Count == 0 ? null : Sequence(value.Transfers.Select(t => BuildTransfer(t, entityIndex)))),
            Pair("research", value.Research.Count == 0 ? null : Sequence(value.Research.Select(project => Overlay(
                oldResearch.GetValueOrDefault(project.RuleId),
            [
                Pair("project", Scalar(project.RuleId)), Pair("assigned", Integer(project.Assigned)),
                Pair("spent", Integer(project.Spent)), Pair("cost", Integer(project.Cost)),
            ])))),
            Pair("productions", value.Productions.Count == 0 ? null : Sequence(value.Productions.Select(production => Overlay(
                oldProductions.GetValueOrDefault(production.RuleId),
            [
                Pair("item", Scalar(production.RuleId)), Pair("assigned", Integer(production.Assigned)),
                Pair("spent", Integer(production.Spent)), Pair("amount", Integer(production.Amount)),
                Pair("infinite", Boolean(production.Infinite)),
                Pair("sell", production.Sell ? Boolean(true) : null),
                Pair("isFallback", production.IsFallback ? Boolean(true) : null),
                Pair("randomProductionInfo", production.RandomProductionInfo.Count == 0 ? null :
                    IntMapping(production.RandomProductionInfo)),
            ])))),
        ]);
    }

    private static YamlMappingNode BuildSoldier(SoldierSnapshot value, SaveEntityIndex index)
    {
        var source = MatchingSource(index.Soldiers.GetValueOrDefault(value.Id), value.PreservationKey, $"soldier:{value.Id}");
        var identity = Overlay(source,
            [Pair("type", Scalar(value.RuleId)), Pair("id", Integer(value.Id)), Pair("oxcePortEntityKey", Scalar(value.PreservationKey))]);
        if (value.Personal is not { } personal) return identity;
        return Overlay(identity,
        [
            Pair("name", Scalar(personal.Name)), Pair("callsign", personal.Callsign.Length == 0 ? null : Scalar(personal.Callsign)),
            Pair("nationality", Integer(personal.Nationality)), Pair("gender", Integer(personal.Gender)),
            Pair("look", Integer(personal.Look)), Pair("lookVariant", Integer(personal.LookVariant)),
            Pair("rank", Integer(personal.Rank)), Pair("armor", Scalar(personal.Armor)),
            Pair("missions", Integer(personal.Missions)),
            Pair("kills", Integer(personal.Kills)),
            Pair("stuns", Integer(personal.Stuns)),
            Pair("diary", BuildSoldierDiary(source, personal)),
            Pair("dailyDogfightExperienceCache", personal.DailyDogfightExperience.IsEmpty ? null : Overlay(
                source is not null && source.TryGet("dailyDogfightExperienceCache", out var cache) ? RequireMap(cache!, "dogfight experience") : null,
                [Pair("firing", Integer(personal.DailyDogfightExperience.Firing)),
                    Pair("reactions", Integer(personal.DailyDogfightExperience.Reactions)),
                    Pair("bravery", Integer(personal.DailyDogfightExperience.Bravery))])),
            Pair("manaMissing", Integer(personal.ManaMissing)),
            Pair("healthMissing", Integer(personal.HealthMissing)),
            Pair("improvement", Integer(personal.Improvement)),
            Pair("psiStrImprovement", Integer(personal.PsiStrImprovement)),
            Pair("allowAutoCombat", Boolean(personal.AllowAutoCombat)),
            Pair("isLeeroyJenkins", Boolean(personal.IsLeeroyJenkins)),
            Pair("corpseRecovered", Boolean(personal.CorpseRecovered)),
            Pair("previousTransformations", personal.PreviousTransformations.Count == 0 ? null :
                Mapping(personal.PreviousTransformations.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Pair(p.Key, Integer(p.Value))))),
            Pair("transformationBonuses", personal.TransformationBonuses.Count == 0 ? null :
                Mapping(personal.TransformationBonuses.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Pair(p.Key, Integer(p.Value))))),
            Pair("replacedArmor", personal.ReplacedArmor.Length == 0 ? null : Scalar(personal.ReplacedArmor)),
            Pair("transformedArmor", personal.TransformedArmor.Length == 0 ? null : Scalar(personal.TransformedArmor)),
            Pair("personalEquipmentArmor", personal.PersonalEquipmentArmor.Length == 0 ? null : Scalar(personal.PersonalEquipmentArmor)),
            Pair("recovery", Scalar(personal.Recovery.ToString("R", CultureInfo.InvariantCulture))),
            Pair("initialStats", Mapping(personal.InitialStats.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Pair(p.Key, Integer(p.Value))))),
            Pair("currentStats", Mapping(personal.CurrentStats.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Pair(p.Key, Integer(p.Value))))),
            Pair("psiTraining", personal.PsiTraining ? Boolean(true) : null),
            Pair("training", personal.Training ? Boolean(true) : null),
            Pair("returnToTrainingWhenHealed", personal.ReturnToTrainingWhenHealed ? Boolean(true) : null),
            Pair("craft", personal.CraftType.Length == 0 ? null : Mapping(
                [Pair("type", Scalar(personal.CraftType)), Pair("id", Integer(personal.CraftId))])),
        ]);
    }

    private static YamlMappingNode BuildCraft(CraftSnapshot value, SaveEntityIndex index)
    {
        var source = MatchingSource(index.Crafts.GetValueOrDefault(EntityIdentity(value)), value.PreservationKey, $"craft:{value.RuleId}:{value.Id}");
        var identity = Overlay(source,
            [Pair("type", Scalar(value.RuleId)), Pair("id", Integer(value.Id)), Pair("oxcePortEntityKey", Scalar(value.PreservationKey)),
                Pair("tags", ScriptValues(value.ScriptValues))]);
        if (value.Logistics is not { } state) return identity;
        var originalWeapons = Maps(source, "weapons").ToArray();
        var originalVehicles = Maps(source, "vehicles").Select((node, slot) => new
        {
            Identity = (Type: node.ReadString("type", ""),
                PreservationKey: node.ReadString("oxcePortEntityKey", $"{value.PreservationKey}:vehicle:{slot}")),
            Node = node,
        }).ToDictionary(entry => entry.Identity, entry => entry.Node);
        return Overlay(identity,
        [
            Pair("fuel", Integer(state.Fuel)), Pair("damage", Integer(state.Damage)), Pair("status", Scalar(state.Status)),
            Pair("name", state.Name.Length == 0 ? null : Scalar(state.Name)),
            Pair("excessFuel", Integer(state.ExcessFuel)), Pair("lowFuel", state.LowFuel ? Boolean(true) : null),
            Pair("isAutoPatrolling", state.IsAutoPatrolling ? Boolean(true) : null),
            Pair("shield", Integer(state.Shield)),
            Pair("dest", TargetReference(state.Destination)),
            Pair("takeoff", state.Takeoff == 0 ? null : Integer(state.Takeoff)),
            Pair("mission", state.MissionComplete ? Boolean(true) : null),
            Pair("inBattlescape", state.InBattlescape ? Boolean(true) : null),
            Pair("interceptionOrder", state.InterceptionOrder == 0 ? null : Integer(state.InterceptionOrder)),
            Pair("pilots", state.Pilots.Count == 0 ? null : Sequence(state.Pilots.Select(Integer))),
            Pair("lonAuto", Real(state.AutoPatrolLongitude)), Pair("latAuto", Real(state.AutoPatrolLatitude)),
            Pair("speed", Integer(state.Speed)),
            Pair("speedLon", Real(state.SpeedLongitude)), Pair("speedLat", Real(state.SpeedLatitude)),
            Pair("speedRadian", Real(state.SpeedRadian)),
            Pair("lon", Scalar(state.Longitude.ToString("R", CultureInfo.InvariantCulture))),
            Pair("lat", Scalar(state.Latitude.ToString("R", CultureInfo.InvariantCulture))),
            Pair("items", IntMapping(state.Items)),
            Pair("weapons", Sequence(state.Weapons.Select((weapon, slot) => weapon is null ? Mapping([Pair("type", Scalar("0"))]) :
                Overlay(slot < originalWeapons.Length && YamlValueReader.ReadString(originalWeapons[slot], "type", "") == weapon.RuleId ? originalWeapons[slot] : null,
                    [Pair("type", Scalar(weapon.RuleId)), Pair("ammo", Integer(weapon.Ammo)),
                        Pair("rearming", weapon.Rearming ? Boolean(true) : null), Pair("disabled", weapon.Disabled ? Boolean(true) : null)])))),
            Pair("vehicles", Sequence(state.Vehicles.Select((vehicle, slot) =>
            {
                var key = vehicle.PreservationKey.Length == 0 ? $"{value.PreservationKey}:vehicle:{slot}" : vehicle.PreservationKey;
                originalVehicles.TryGetValue((vehicle.RuleId, key), out var original);
                return Overlay(original,
                [Pair("type", Scalar(vehicle.RuleId)), Pair("ammo", Integer(vehicle.Ammo)), Pair("oxcePortEntityKey", Scalar(key)),
                    Pair("size", vehicle.Size is { } size ? Integer(size) : null),
                    Pair("spaceOccupied", vehicle.SpaceOccupied is { } space ? Integer(space) : null)]);
            }))),
            // Craft::save writes them only when not empty; a reset deployment leaves the node.
            Pair("customSoldierDeployment", state.CustomSoldierDeployment.Count == 0 ? null : Mapping(state.CustomSoldierDeployment
                .OrderBy(static pair => pair.Key).Select(pair => Pair(pair.Key.ToString(CultureInfo.InvariantCulture),
                    Sequence([DeploymentPosition(pair.Value), Integer(pair.Value.Direction)]))))),
            Pair("customVehicleDeployment", state.CustomVehicleDeployment.Count == 0 ? null : Sequence(state.CustomVehicleDeployment
                .Select(vehicle => Mapping([Pair("type", Scalar(vehicle.Type)), Pair("pos", DeploymentPosition(vehicle.Point)),
                    Pair("dir", Integer(vehicle.Point.Direction))])))),
            Pair("skinIndex", state.SkinIndex == 0 ? null : Integer(state.SkinIndex)),
        ]);
    }

    private static YamlSequenceNode DeploymentPosition(CraftDeploymentPoint point) =>
        Sequence([Integer(point.X), Integer(point.Y), Integer(point.Z)]);

    private static YamlMappingNode BuildTransfer(TransferSnapshot value, SaveEntityIndex index) =>
        Overlay(MatchingSource(index.Transfers.GetValueOrDefault(value.Id), value.PreservationKey, $"transfer:{value.Id}"),
        [
            Pair("oxcePortTransferId", Integer(value.Id)), Pair("hours", Integer(value.Hours)),
            Pair("oxcePortEntityKey", Scalar(value.PreservationKey)),
            Pair("delivered", value.Delivered ? Boolean(true) : null),
            Pair("soldier", value.Soldier is { } soldier ? BuildSoldier(soldier, index) : null),
            Pair("craft", value.Craft is { } craft ? BuildCraft(craft, index) : null),
            Pair("itemId", value.Kind == CampaignTransferKind.Item ? Scalar(value.RuleId) : null),
            Pair("itemQty", value.Kind == CampaignTransferKind.Item ? Integer(value.Quantity) : null),
            Pair("scientists", value.Kind == CampaignTransferKind.Scientist ? Integer(value.Quantity) : null),
            Pair("engineers", value.Kind == CampaignTransferKind.Engineer ? Integer(value.Quantity) : null),
        ]);

    private static YamlMappingNode? MatchingSource(YamlMappingNode? source, string key, string legacyKey) =>
        source is not null && key.Length != 0 && source.ReadString("oxcePortEntityKey", legacyKey) == key ? source : null;

    private static YamlMappingNode? BuildSoldierDiary(YamlMappingNode? source, SoldierPersonalState personal)
    {
        var diary = source is not null && source.TryGet("diary", out var node) ? RequireMap(node!, "soldier diary") : null;
        if (diary is null && personal.Commendations.Count == 0 && personal.UfosShotDownTotal == 0 && personal.UfosDamageTotal == 0 &&
            personal.Diary.Counters.Count == 0 && personal.Diary.Kills.Count == 0 && personal.Diary.MissionIds.Count == 0)
            return null;
        var previous = Maps(diary, "commendations").ToArray();
        var commendations = personal.Commendations.Select((commendation, index) =>
        {
            var old = index < previous.Length && YamlValueReader.ReadString(previous[index], "commendationName", "") == commendation.RuleId &&
                YamlValueReader.ReadString(previous[index], "noun", "noNoun") == commendation.Noun ? previous[index] : null;
            return Overlay(old,
            [
                Pair("commendationName", Scalar(commendation.RuleId)),
                Pair("noun", commendation.Noun == "noNoun" ? null : Scalar(commendation.Noun)),
                Pair("decorationLevel", Integer(commendation.DecorationLevel)),
            ]);
        });
        var result = Overlay(diary,
        [
            Pair("commendations", Sequence(commendations)),
            Pair("ufosShotDownTotal", personal.UfosShotDownTotal == 0 ? null : Integer(personal.UfosShotDownTotal)),
            Pair("ufosDamageTotal", personal.UfosDamageTotal == 0 ? null : Integer(personal.UfosDamageTotal)),
            // SoldierDiary::save writes a counter only when it is not zero. The kill list and mission IDs change only in
            // battle (Phase 7), so their source nodes are kept.
            .. SoldierDiaryState.CounterKeys.Select(key => Pair(key, personal.Diary.Counter(key) is var value && value == 0 ? null
                : SoldierDiaryState.IsFlag(key) ? Boolean(true) : Integer(value))),
        ]);
        // A diary without a source (a new soldier) writes its kill list and mission IDs as SoldierDiary::save does.
        return diary is null ? Overlay(result,
        [
            Pair("killList", Sequence(personal.Diary.Kills.Select(static kill => (YamlNode)Overlay(null,
            [
                Pair("name", kill.Name.Length == 0 ? null : Scalar(kill.Name)),
                Pair("type", kill.Type.Length == 0 ? null : Scalar(kill.Type)),
                Pair("rank", Scalar(kill.Rank)), Pair("race", Scalar(kill.Race)), Pair("weapon", Scalar(kill.Weapon)),
                Pair("weaponAmmo", Scalar(kill.WeaponAmmo)), Pair("status", Integer(kill.Status)), Pair("faction", Integer(kill.Faction)),
                Pair("mission", Integer(kill.Mission)), Pair("turn", Integer(kill.Turn)), Pair("side", Integer(kill.Side)),
                Pair("bodypart", Integer(kill.BodyPart)), Pair("id", Integer(kill.Id)),
            ])))),
            Pair("missionIdList", personal.Diary.MissionIds.Count == 0 ? null : Sequence(personal.Diary.MissionIds.Select(static id => (YamlNode)Integer(id)))),
        ]) : result;
    }

    /// <summary>SoldierDiary::load: the counters, the kill list and the mission IDs.</summary>
    private static SoldierDiaryState ReadDiary(YamlMappingNode? diary)
    {
        if (diary is null) return SoldierDiaryState.Empty;
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in SoldierDiaryState.CounterKeys)
        {
            var value = SoldierDiaryState.IsFlag(key) ? diary.ReadBoolean(key, false) ? 1 : 0 : diary.ReadInt32(key, 0);
            if (value != 0) counters[key] = value;
        }
        // BattleUnitKills(reader) leaves an absent field unset; the port takes the default constructor's value.
        var kills = Maps(diary, "killList").Select(static kill => new SoldierKill(kill.ReadString("rank", ""), kill.ReadString("race", ""),
            kill.ReadString("weapon", ""), kill.ReadString("weaponAmmo", ""), kill.ReadInt32("status", SoldierKill.StatusIgnoreMe),
            kill.ReadInt32("faction", SoldierKill.FactionHostile), kill.ReadInt32("mission", 0), kill.ReadInt32("turn", 0),
            kill.ReadInt32("side", 0), kill.ReadInt32("bodypart", 0))
        {
            Type = kill.ReadString("type", ""),
            // BattleUnitKills::load reads the name only for a kill without a type.
            Name = kill.TryGet("type", out var type) && YamlValueReader.ReadString(type!).Length != 0 ? "" : kill.ReadString("name", ""),
            Id = kill.ReadInt32("id", 0),
        }).ToArray();
        var missions = diary.TryGet("missionIdList", out var node)
            ? YamlValueReader.ReadSequence(node!, YamlValueReader.ReadInt32) : [];
        return new SoldierDiaryState
        {
            Counters = new ReadOnlyDictionary<string, int>(counters),
            Kills = Array.AsReadOnly(kills),
            MissionIds = Array.AsReadOnly(missions.ToArray()),
        };
    }

    /// <summary>MissionStatistics::load over the default constructor's values, as commendations read it.</summary>
    private static MissionStatisticSnapshot ReadMissionStatistic(YamlMappingNode map) => new(map.ReadInt32("id", 0),
        map.ReadString("markerName", ""), map.ReadString("region", "STR_REGION_UNKNOWN"), map.ReadString("country", "STR_UNKNOWN"),
        map.ReadString("type", ""), map.ReadString("ufo", "NO_UFO"), map.ReadBoolean("success", false), map.ReadInt32("score", 0), map.ReadInt32("daylight", 0),
        map.ReadBoolean("valiantCrux", false), map.ReadInt32("lootValue", 0));

    private static SoldierSnapshot ReadSoldier(YamlMappingNode map, string defaultSoldier, RuntimeContent content) =>
        new(map.ReadString("type", defaultSoldier), map.ReadInt32("id", 0))
        {
            PreservationKey = map.ReadString("oxcePortEntityKey", $"soldier:{YamlValueReader.ReadInt32(map, "id", 0)}"),
            Personal = ReadSoldierPersonal(map, content),
        };

    private static SoldierPersonalState? ReadSoldierPersonal(YamlMappingNode map, RuntimeContent content)
    {
        if (!map.TryGet("initialStats", out _)) return null;
        var craft = map.TryGet("craft", out var node) ? RequireMap(node!, "soldier craft") : null;
        var diary = map.TryGet("diary", out var diaryNode) ? RequireMap(diaryNode!, "soldier diary") : null;
        return new(map.ReadString("name", ""), map.ReadString("callsign", ""), map.ReadInt32("nationality", 0),
            map.ReadInt32("gender", 0), map.ReadInt32("look", 0), map.ReadInt32("lookVariant", 0), map.ReadString("armor", ""),
            Stats("initialStats"), Stats("currentStats"))
        {
            Rank = map.ReadInt32("rank", 0),
            Missions = map.ReadInt32("missions", 0),
            Kills = map.ReadInt32("kills", 0),
            Stuns = map.ReadInt32("stuns", 0),
            ManaMissing = map.ReadInt32("manaMissing", 0),
            HealthMissing = map.ReadInt32("healthMissing", 0),
            Improvement = map.ReadInt32("improvement", 0),
            PsiStrImprovement = map.ReadInt32("psiStrImprovement", 0),
            AllowAutoCombat = map.ReadBoolean("allowAutoCombat", true),
            IsLeeroyJenkins = map.ReadBoolean("isLeeroyJenkins", true),
            CorpseRecovered = map.ReadBoolean("corpseRecovered", false),
            PreviousTransformations = ReadIntMap(map, "previousTransformations"),
            TransformationBonuses = ReadIntMap(map, "transformationBonuses"),
            // SoldierDiary::load drops a commendation whose rule no longer exists.
            Commendations = Array.AsReadOnly(Maps(diary, "commendations").Select(c => new SoldierCommendation(
                    RequiredString(c, "commendationName"), c.ReadString("noun", "noNoun"), c.ReadInt32("decorationLevel", 0)))
                .Where(c => content.RuntimeRules.Commendations.TryGet(c.RuleId, out _)).ToArray()),
            UfosShotDownTotal = diary is null ? 0 : diary.ReadInt32("ufosShotDownTotal", 0),
            UfosDamageTotal = diary is null ? 0 : diary.ReadInt32("ufosDamageTotal", 0),
            Diary = ReadDiary(diary),
            DailyDogfightExperience = map.TryGet("dailyDogfightExperienceCache", out var cacheNode) &&
                RequireMap(cacheNode!, "dogfight experience") is var cache
                ? new(cache.ReadInt32("firing", 0), cache.ReadInt32("reactions", 0), cache.ReadInt32("bravery", 0))
                : SoldierDogfightExperience.None,
            ReplacedArmor = map.ReadString("replacedArmor", ""),
            TransformedArmor = map.ReadString("transformedArmor", ""),
            PersonalEquipmentArmor = map.ReadString("personalEquipmentArmor", ""),
            EquipmentLayout = ReadEquipmentLayout(map, content),
            Recovery = (float)map.ReadDouble("recovery", 0),
            PsiTraining = map.ReadBoolean("psiTraining", false),
            Training = map.ReadBoolean("training", false),
            ReturnToTrainingWhenHealed = map.ReadBoolean("returnToTrainingWhenHealed", false),
            CraftType = craft is null ? "" : RequiredString(craft, "type"),
            CraftId = craft is null ? 0 : craft.ReadInt32("id", 0),
        };

        ReadOnlyDictionary<string, short> Stats(string key) => new(ReadIntMap(map, key).ToDictionary(
            p => p.Key, p => checked((short)p.Value), StringComparer.Ordinal));
    }

    // Soldier::load: an entry whose item, section or ammunition does not exist is logged and skipped.
    private static ReadOnlyCollection<SoldierLayoutItem> ReadEquipmentLayout(YamlMappingNode soldier, RuntimeContent content)
    {
        var rules = content.RuntimeRules;
        var result = new List<SoldierLayoutItem>();
        foreach (var entry in Maps(soldier, "equipmentLayout"))
        {
            var item = entry.ReadString("itemType", "");
            var slot = entry.ReadString("slot", "");
            if (!rules.Items.TryGet(item, out _) || !rules.Inventories.TryGet(slot, out _)) continue;
            var ammo = new string[4];
            Array.Fill(ammo, "");
            if (entry.TryGet("ammoItemSlots", out var slots) && slots is YamlSequenceNode sequence)
            {
                // EquipmentLayoutItem::load reads up to four slots, stopping at the first missing one; NONE is empty.
                for (var index = 0; index < Math.Min(4, sequence.Items.Count); index++)
                {
                    var name = YamlValueReader.ReadString(sequence.Items[index]);
                    ammo[index] = name == "NONE" ? "" : name;
                }
            }
            else if (entry.TryGet("ammoItem", out var single)) ammo[0] = YamlValueReader.ReadString(single!);
            if (ammo.Any(name => name.Length != 0 && !rules.Items.TryGet(name, out _))) continue;
            result.Add(new(item, slot, entry.ReadInt32("slotX", 0), entry.ReadInt32("slotY", 0), Array.AsReadOnly(ammo),
                entry.ReadInt32("fuseTimer", -1), entry.ReadBoolean("fixed", false)));
        }
        return result.AsReadOnly();
    }

    private static CraftSnapshot ReadCraft(YamlMappingNode map, WorldMarkerIndex markers, RuntimeContent content)
    {
        var type = RequiredString(map, "type");
        var id = map.ReadInt32("id", 0);
        var preservationKey = map.ReadString("oxcePortEntityKey", $"craft:{type}:{id}");
        var logistics = ReadCraftLogistics(map, preservationKey, markers);
        // Craft::load: a skin above the craft rule's maxSkinIndex, even a negative one, falls back to 0.
        if (logistics is not null && content.RuntimeRules.Crafts.TryGet(type, out var rule) &&
            logistics.SkinIndex > content.RuntimeRules.Crafts[rule].Value.MaximumSkinIndex)
            logistics = logistics with { SkinIndex = 0 };
        return new(type, id)
        {
            PreservationKey = preservationKey,
            Logistics = logistics,
            ScriptValues = ReadScriptValues(map, content, "Craft"),
        };
    }

    private static readonly string[] OptionKeys = CampaignOptions.Fields.Select(static field => field.Key).ToArray();

    // Absent options keep the record defaults.
    private static CampaignOptions ReadOptions(YamlMappingNode map)
    {
        RejectDuplicateKnownKeys(map, OptionKeys);
        var options = new CampaignOptions();
        foreach (var field in CampaignOptions.Fields)
        {
            if (!map.TryGet(field.Key, out var node)) continue;
            options = field.With(options, field.IsInteger ? YamlValueReader.ReadInt32(node!) : YamlValueReader.ReadBoolean(node!) ? 1 : 0);
        }
        return options;
    }

    private static YamlMappingNode BuildOptions(CampaignOptions options) => Mapping(CampaignOptions.Fields.Select(field =>
        Pair(field.Key, field.IsInteger ? Integer(field.Get(options)) : Boolean(field.Get(options) != 0))));

    /// <summary>
    /// A reference save has no port options, but SavedGame::save writes a debugging snapshot of
    /// the player's options. The reference never reads it back; it is the best record of the
    /// options the campaign was played with, so matching boolean entries (and the wounded-defender
    /// percentage) seed the session options. Anything unreadable falls back to the defaults instead
    /// of rejecting the save.
    /// </summary>
    private static CampaignOptions ReadReferenceOptions(YamlMappingNode body)
    {
        var options = new CampaignOptions();
        if (!body.TryGet("options", out var node) || node is not YamlMappingNode map) return options;
        foreach (var field in CampaignOptions.Fields)
        {
            if (!field.IsUserOption || !map.TryGet(field.Key, out var value) || value is not YamlScalarNode { Value: { } text })
                continue;
            if (field.IsInteger)
            {
                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                    options = field.With(options, parsed);
            }
            else if (text is "true" or "false") options = field.With(options, text == "true" ? 1 : 0);
        }
        return options;
    }

    private static CraftLogisticsState? ReadCraftLogistics(
        YamlMappingNode map, string craftPreservationKey, WorldMarkerIndex markers)
    {
        if (!map.TryGet("status", out _) && !map.TryGet("weapons", out _) && !map.TryGet("items", out _)) return null;
        var state = ReadCraftLogisticsState(map, craftPreservationKey, markers);
        // Craft::load: a craft in the battlescape stands still (setSpeed(0)).
        return state.InBattlescape ? state with { Speed = 0, SpeedLongitude = 0, SpeedLatitude = 0, SpeedRadian = 0 } : state;
    }

    private static CraftLogisticsState ReadCraftLogisticsState(YamlMappingNode map, string craftPreservationKey, WorldMarkerIndex markers)
    {
        return new(map.ReadInt32("fuel", 0), map.ReadInt32("damage", 0), map.ReadString("status", "STR_READY"),
            Array.AsReadOnly(Maps(map, "weapons").Select(w => w.ReadString("type", "0") == "0" ? null :
                new CraftWeaponSnapshot(RequiredString(w, "type"), w.ReadInt32("ammo", 0), w.ReadBoolean("rearming", false), w.ReadBoolean("disabled", false))).ToArray()),
            ReadIntMap(map, "items"), Array.AsReadOnly(Maps(map, "vehicles").Select((v, slot) =>
                new CraftVehicleSnapshot(RequiredString(v, "type"), v.ReadInt32("ammo", 0))
                {
                    Size = v.TryGet("size", out _) ? v.ReadInt32("size", 0) : null,
                    SpaceOccupied = v.TryGet("spaceOccupied", out _) ? v.ReadInt32("spaceOccupied", 0) : null,
                    PreservationKey = v.ReadString("oxcePortEntityKey", $"{craftPreservationKey}:vehicle:{slot}"),
                }).ToArray()))
        {
            Longitude = map.ReadDouble("lon", 0),
            Latitude = map.ReadDouble("lat", 0),
            Name = map.ReadString("name", ""),
            ExcessFuel = map.ReadInt32("excessFuel", 0),
            LowFuel = map.ReadBoolean("lowFuel", false),
            IsAutoPatrolling = map.ReadBoolean("isAutoPatrolling", false),
            Shield = map.ReadInt32("shield", 0),
            Destination = ReadTargetReference(map, markers, map.ReadDouble("lon", 0), map.ReadDouble("lat", 0)),
            Takeoff = map.ReadInt32("takeoff", 0),
            MissionComplete = map.ReadBoolean("mission", false),
            InBattlescape = map.ReadBoolean("inBattlescape", false),
            InterceptionOrder = map.ReadInt32("interceptionOrder", 0),
            Pilots = IntHistory(map, "pilots", []),
            AutoPatrolLongitude = map.ReadDouble("lonAuto", 0),
            AutoPatrolLatitude = map.ReadDouble("latAuto", 0),
            Speed = map.ReadInt32("speed", 0),
            SpeedLongitude = map.ReadDouble("speedLon", 0),
            SpeedLatitude = map.ReadDouble("speedLat", 0),
            SpeedRadian = map.ReadDouble("speedRadian", 0),
            SkinIndex = map.ReadInt32("skinIndex", 0),
            CustomSoldierDeployment = map.TryGet("customSoldierDeployment", out var soldiers)
                ? new ReadOnlyDictionary<int, CraftDeploymentPoint>(YamlValueReader.ReadMap(soldiers!, YamlValueReader.ReadInt32, node =>
                {
                    // std::pair<Position, int>: [[x, y, z], dir].
                    var (position, direction) = YamlValueReader.ReadPair(node, ReadPosition, YamlValueReader.ReadInt32);
                    return new CraftDeploymentPoint(position.X, position.Y, position.Z, direction);
                }))
                : new ReadOnlyDictionary<int, CraftDeploymentPoint>(new Dictionary<int, CraftDeploymentPoint>()),
            // VehicleDeploymentData: a missing type stays empty, a missing pos is [-1, -1, -1].
            CustomVehicleDeployment = Array.AsReadOnly(Maps(map, "customVehicleDeployment").Select(vehicle =>
            {
                var position = vehicle.TryGet("pos", out var pos) ? ReadPosition(pos!) : (X: -1, Y: -1, Z: -1);
                return new CraftVehicleDeployment(vehicle.ReadString("type", ""),
                    new(position.X, position.Y, position.Z, vehicle.ReadInt32("dir", 0)));
            }).ToArray()),
        };
    }

    /// <summary>Position's reader: a sequence of three Sint16 values.</summary>
    private static (int X, int Y, int Z) ReadPosition(YamlNode node)
    {
        var (x, y, z) = YamlValueReader.ReadTuple(node, YamlValueReader.ReadInt16, YamlValueReader.ReadInt16, YamlValueReader.ReadInt16);
        return (x, y, z);
    }

    private static TransferSnapshot ReadTransfer(
        YamlMappingNode map, int id, string defaultSoldier, WorldMarkerIndex markers, RuntimeContent content)
    {
        RejectDuplicateKnownKeys(map, ["hours", "soldier", "craft", "itemId", "itemQty", "scientists", "engineers", "delivered", "oxcePortTransferId"]);
        var payloads = TransferPayloadKeys.Count(key => map.TryGet(key, out _));
        if (payloads != 1) throw new InvalidDataException("A transfer must contain exactly one payload.");
        var hours = map.ReadInt32("hours", 0);
        var delivered = map.ReadBoolean("delivered", false);
        if (map.TryGet("soldier", out var soldierNode))
        {
            var soldier = RequireMap(soldierNode!, "transfer soldier");
            var value = ReadSoldier(soldier, defaultSoldier, content);
            return Preserve(new(id, hours, CampaignTransferKind.Soldier, value.RuleId, 1, value, Delivered: delivered));
        }
        if (map.TryGet("craft", out var craftNode))
        {
            var craft = RequireMap(craftNode!, "transfer craft");
            var value = ReadCraft(craft, markers, content);
            return Preserve(new(id, hours, CampaignTransferKind.Craft, value.RuleId, 1, Craft: value, Delivered: delivered));
        }
        if (map.TryGet("itemId", out _))
            return Preserve(new(id, hours, CampaignTransferKind.Item, RequiredString(map, "itemId"), map.ReadInt32("itemQty", 0), Delivered: delivered));
        var scientist = map.TryGet("scientists", out _);
        return Preserve(new(id, hours, scientist ? CampaignTransferKind.Scientist : CampaignTransferKind.Engineer,
            string.Empty, map.ReadInt32(scientist ? "scientists" : "engineers", 0), Delivered: delivered));

        TransferSnapshot Preserve(TransferSnapshot value) => value with
        {
            PreservationKey = map.ReadString("oxcePortEntityKey", $"transfer:{id}"),
        };
    }

    private static SaveEntityIndex IndexEntities(YamlMappingNode? body)
    {
        var index = new SaveEntityIndex();
        var bases = Maps(body, "bases").ToArray();
        var transfers = bases.SelectMany(b => Maps(b, "transfers")).ToArray();
        var used = new HashSet<int>();
        foreach (var transfer in transfers)
        {
            if (!transfer.TryGet("oxcePortTransferId", out var node)) continue;
            var id = YamlValueReader.ReadInt32(node!);
            if (id <= 0 || !used.Add(id)) throw new InvalidDataException("Invalid or duplicate transfer ID.");
        }
        var nextId = 1;
        foreach (var transfer in transfers)
        {
            int id;
            if (transfer.TryGet("oxcePortTransferId", out var node)) id = YamlValueReader.ReadInt32(node!);
            else
            {
                while (used.Contains(nextId)) nextId = checked(nextId + 1);
                id = nextId;
                used.Add(id);
            }
            index.TransferIds.Add(transfer, id);
            index.Transfers.Add(id, transfer);
        }
        foreach (var baseMap in bases)
        {
            foreach (var soldier in Maps(baseMap, "soldiers")) AddSoldier(soldier);
            foreach (var craft in Maps(baseMap, "crafts")) AddCraft(craft);
        }
        foreach (var transfer in transfers)
        {
            if (transfer.ReadBoolean("delivered", false)) continue;
            if (transfer.TryGet("soldier", out var soldier)) AddSoldier(RequireMap(soldier!, "transfer soldier"));
            if (transfer.TryGet("craft", out var craft)) AddCraft(RequireMap(craft!, "transfer craft"));
        }
        return index;

        void AddSoldier(YamlMappingNode soldier)
        {
            if (!index.Soldiers.TryAdd(soldier.ReadInt32("id", 0), soldier))
                throw new InvalidDataException("Duplicate soldier ownership across bases/transfers.");
        }
        void AddCraft(YamlMappingNode craft)
        {
            if (!index.Crafts.TryAdd(EntityIdentity(craft), craft))
                throw new InvalidDataException("Duplicate craft ownership across bases/transfers.");
        }
    }

    private sealed class SaveEntityIndex
    {
        public Dictionary<int, YamlMappingNode> Soldiers { get; } = [];
        public Dictionary<string, YamlMappingNode> Crafts { get; } = new(StringComparer.Ordinal);
        public Dictionary<int, YamlMappingNode> Transfers { get; } = [];
        public Dictionary<YamlMappingNode, int> TransferIds { get; } = [];
    }

    private static string EntityIdentity(YamlMappingNode value) =>
        $"{value.ReadString("type", string.Empty)}\0{value.ReadInt32("id", 0)}";

    private static string EntityIdentity(CraftSnapshot value) => $"{value.RuleId}\0{value.Id}";

    private static BaseFacilityIdentity FacilityIdentity(YamlMappingNode value) => new(
        value.ReadString("type", string.Empty), value.ReadInt32("x", -1), value.ReadInt32("y", -1), value.ReadString("oxcePortEntityKey", string.Empty));

    private static BaseFacilityIdentity FacilityIdentity(FacilitySnapshot value) => new(
        value.RuleId, value.X, value.Y, value.PreservationKey);

    private static List<IdentifiedBase> IdentifyBases(YamlMappingNode? body)
    {
        var bases = Maps(body, "bases").ToArray();
        var explicitIds = new HashSet<int>();
        foreach (var map in bases)
        {
            if (!map.TryGet("id", out var node)) continue;
            var id = YamlValueReader.ReadInt32(node!);
            if (id < 0) throw new InvalidDataException("Base IDs cannot be negative.");
            if (!explicitIds.Add(id)) throw new InvalidDataException($"Duplicate base ID '{id}'.");
        }

        var used = new HashSet<int>(explicitIds);
        var next = 0;
        var result = new List<IdentifiedBase>(bases.Length);
        foreach (var map in bases)
        {
            int id;
            if (map.TryGet("id", out var node))
            {
                id = YamlValueReader.ReadInt32(node!);
            }
            else
            {
                while (used.Contains(next)) next = checked(next + 1);
                id = next;
                used.Add(id);
                next = checked(next + 1);
            }
            result.Add(new IdentifiedBase(id, map));
        }
        return result;
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string name) where T : notnull
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
            if (!seen.Add(value)) throw new InvalidDataException($"Duplicate {name} '{value}'.");
    }

    private static Dictionary<string, YamlMappingNode> FirstByKey(
        IEnumerable<YamlMappingNode> nodes, Func<YamlMappingNode, string> key)
    {
        var result = new Dictionary<string, YamlMappingNode>(StringComparer.Ordinal);
        foreach (var node in nodes) result.TryAdd(key(node), node);
        return result;
    }

    private readonly record struct IdentifiedBase(int Id, YamlMappingNode Value);

    private readonly record struct BaseFacilityIdentity(string RuleId, int X, int Y, string PreservationKey);

    internal static YamlMappingNode? ScriptValues(IReadOnlyList<ScriptValueEntry> values) => values.Count == 0
        ? null
        : Mapping(values.Select(value => Pair(value.TagName, Integer(value.Value))));

    private static YamlMappingNode Time(CampaignTime value) => Mapping(
    [
        Pair("second", Integer(value.Second)), Pair("minute", Integer(value.Minute)),
        Pair("hour", Integer(value.Hour)), Pair("weekday", Integer(value.Weekday)),
        Pair("day", Integer(value.Day)), Pair("month", Integer(value.Month)), Pair("year", Integer(value.Year)),
    ]);

    private static CampaignTime ReadTime(YamlMappingNode value) => new CampaignTime(
        value.ReadInt32("weekday", 6), value.ReadInt32("day", 1), value.ReadInt32("month", 1),
        value.ReadInt32("year", 1999), value.ReadInt32("hour", 12), value.ReadInt32("minute", 0),
        value.ReadInt32("second", 0)).Validate();

    internal static YamlMappingNode Overlay(YamlMappingNode? source, IReadOnlyList<(string Key, YamlNode? Value)> values)
    {
        var replacements = values.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<YamlMappingEntry>();
        if (source is not null)
        {
            foreach (var entry in source.Entries)
            {
                if (entry.ScalarKey is not { } key || !replacements.TryGetValue(key, out var replacement))
                {
                    entries.Add(entry);
                    continue;
                }
                if (emitted.Add(key) && replacement is not null) entries.Add(PairNode(key, replacement));
            }
        }
        foreach (var item in values)
            if (emitted.Add(item.Key) && item.Value is not null) entries.Add(PairNode(item.Key, item.Value));
        return new YamlMappingNode(source?.Span ?? GeneratedSpan, entries, source?.Tag, source?.Anchor);
    }

    private static ReadOnlyDictionary<string, int> ReadIds(YamlMappingNode body) =>
        body.TryGet("ids", out var node)
            ? new ReadOnlyDictionary<string, int>(YamlValueReader.ReadMap(node!, YamlValueReader.ReadString,
                YamlValueReader.ReadInt32, StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(StringComparer.Ordinal));

    private static ReadOnlyDictionary<string, int> ReadIntMap(YamlMappingNode owner, string key) =>
        owner.TryGet(key, out var node)
            ? new ReadOnlyDictionary<string, int>(YamlValueReader.ReadMap(node!, YamlValueReader.ReadString,
                YamlValueReader.ReadInt32, StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(StringComparer.Ordinal));

    private static ReadOnlyCollection<int> IntHistory(YamlMappingNode owner, string key, int[]? fallback) =>
        owner.TryGet(key, out var node)
            ? Array.AsReadOnly(YamlValueReader.ReadSequence(node!, YamlValueReader.ReadInt32))
            : fallback is not null ? Array.AsReadOnly(fallback) : throw new InvalidDataException($"Missing '{key}'.");

    private static ReadOnlyCollection<long> LongHistory(YamlMappingNode owner, string key, long[] fallback) =>
        owner.TryGet(key, out var node)
            ? Array.AsReadOnly(YamlValueReader.ReadSequence(node!, YamlValueReader.ReadInt64))
            : Array.AsReadOnly(fallback);

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode owner, string key)
    {
        if (!owner.TryGet(key, out var node)) return [];
        return node is YamlSequenceNode sequence
            ? sequence.Items
            : throw new InvalidDataException($"OXCE save '{key}' must be a sequence.");
    }

    private static IEnumerable<YamlMappingNode> Maps(YamlMappingNode? owner, string key) => owner is null
        ? []
        : Sequence(owner, key).Select(node => RequireMap(node, key));

    private static YamlMappingNode RequiredMap(YamlMappingNode owner, string key) => owner.TryGet(key, out var node)
        ? RequireMap(node!, key)
        : throw new InvalidDataException($"Missing required mapping '{key}'.");

    private static YamlMappingNode RequireMap(YamlNode node, string name) => node as YamlMappingNode ??
        throw new InvalidDataException($"OXCE save '{name}' must be a mapping.");

    private static string RequiredString(YamlMappingNode owner, string key) => owner.TryGet(key, out var node)
        ? YamlValueReader.ReadString(node!)
        : throw new InvalidDataException($"Missing required string '{key}'.");

    private static bool TryString(YamlMappingNode owner, string key, out string value)
    {
        if (owner.TryGet(key, out var node))
        {
            value = YamlValueReader.ReadString(node!);
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static void RejectDuplicateKnownKeys(YamlMappingNode owner, IReadOnlyCollection<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in owner.Entries)
            if (entry.ScalarKey is { } key && keys.Contains(key) && !seen.Add(key))
                throw new InvalidDataException($"Duplicate save field '{key}'.");
    }

    private static string SanitizeModName(string value)
    {
        var index = value.IndexOf(" ver: ", StringComparison.Ordinal);
        return index < 0 ? value : value[..index];
    }

    private static Guid DeriveGuid(byte[] data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return new Guid(hash[..16]);
    }

    internal static (string Key, YamlNode? Value) Pair(string key, YamlNode? value) => (key, value);
    private static YamlMappingEntry PairNode(string key, YamlNode value) => new(Scalar(key), value);
    internal static YamlScalarNode Scalar(string value) => new(GeneratedSpan, value, YamlScalarStyle.Plain);
    internal static YamlScalarNode Integer(int value) => Scalar(value.ToString(CultureInfo.InvariantCulture));
    private static YamlScalarNode Long(long value) => Scalar(value.ToString(CultureInfo.InvariantCulture));
    private static YamlScalarNode ULong(ulong value) => Scalar(value.ToString(CultureInfo.InvariantCulture));
    internal static YamlScalarNode Real(double value) => Scalar(value.ToString("R", CultureInfo.InvariantCulture));
    internal static YamlScalarNode Boolean(bool value) => Scalar(value ? "true" : "false");
    internal static YamlSequenceNode Sequence(IEnumerable<YamlNode> values) => new(GeneratedSpan, values);
    // Reference containers (std::map, ItemContainer::save) write keys in byte order; sorting also
    // keeps output independent of dictionary mutation history.
    internal static YamlMappingNode IntMapping(IEnumerable<KeyValuePair<string, int>> values) =>
        Mapping(values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => Pair(pair.Key, Integer(pair.Value))));
    private static YamlMappingNode Mapping(IEnumerable<(string Key, YamlNode? Value)> values) =>
        new(GeneratedSpan, values.Where(static pair => pair.Value is not null)
            .Select(static pair => PairNode(pair.Key, pair.Value!)));
}
