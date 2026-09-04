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
public static class OxceSaveAdapter
{
    private static readonly SourceSpan GeneratedSpan = new(
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
        return Load(Decode(bytes), fullPath, bytes, content, random, options);
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
        var body = BuildBody(snapshot, source);
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
                "expenditures", "researchScores", "ids", "countries", "regions", "bases", "tags"]);

        var modLabels = Sequence(header, "mods").Select(YamlValueReader.ReadString).ToArray();
        var modIds = modLabels.Select(SanitizeModName).ToArray();
        if (modIds.Length == 0) modIds = ["xcom1"];
        if (!modIds.Contains(options.MasterId, StringComparer.Ordinal))
            throw new InvalidDataException($"Save does not target active master '{options.MasterId}'.");
        var missingMods = modIds.Where(id => !options.ActiveMods.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (missingMods.Length != 0)
            throw new InvalidDataException($"Save requires inactive mod(s): {string.Join(", ", missingMods)}.");

        var time = ReadTime(RequiredMap(header, "time"));
        var name = String(header, "name", Path.GetFileNameWithoutExtension(sourceName));
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
            Boolean(header, "ironman", false));
        var difficultyValue = Integer(body, "difficulty", 0);
        if (difficultyValue is < 0 or > 4) throw new InvalidDataException("Save difficulty is outside 0..4.");
        var snapshot = new CampaignSnapshot(
            identity,
            (CampaignDifficulty)difficultyValue,
            time,
            Integer(body, "end", 0),
            Integer(body, "monthsPassed", -1),
            Integer(body, "daysPassed", 0),
            ULong(body, "rng", 0),
            LongHistory(body, "funds", [0]),
            LongHistory(body, "maintenance", [0]),
            LongHistory(body, "incomes", [0]),
            LongHistory(body, "expenditures", [0]),
            IntHistory(body, "researchScores", [0]),
            ReadIds(body),
            ReadCountries(body, content),
            ReadRegions(body),
            ReadBases(body, content),
            ReadScriptValues(body, content, "GeoscapeGame"));
        var campaign = CampaignState.Restore(snapshot, content, random);
        return new LoadedOxceCampaign(campaign,
            new OxceSaveDocument(header, body, Array.AsReadOnly(modLabels)));
    }

    private static ReadOnlyCollection<CountrySnapshot> ReadCountries(YamlMappingNode body, RuntimeContent content) =>
        Array.AsReadOnly(Sequence(body, "countries").Select(node =>
        {
            var map = RequireMap(node, "country");
            return new CountrySnapshot(
                RequiredString(map, "type"),
                IntHistory(map, "funding", null),
                IntHistory(map, "activityXcom", [0]),
                IntHistory(map, "activityAlien", [0]),
                Boolean(map, "pact", false),
                Boolean(map, "newPact", false),
                Boolean(map, "cancelPact", false),
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

    private static ReadOnlyCollection<BaseSnapshot> ReadBases(YamlMappingNode body, RuntimeContent content)
    {
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
                    RequiredString(facility, "type"), Integer(facility, "x", -1), Integer(facility, "y", -1),
                    Integer(facility, "buildTime", 0), Integer(facility, "ammo", 0),
                    Boolean(facility, "ammoMissingReported", false), Boolean(facility, "disabled", false),
                    Boolean(facility, "hadPreviousFacility", false));
            }).ToArray();
            var crafts = Sequence(map, "crafts").Select(item =>
            {
                var craft = RequireMap(item, "craft");
                return new CraftSnapshot(RequiredString(craft, "type"), Integer(craft, "id", 0));
            }).ToArray();
            var soldiers = Sequence(map, "soldiers").Select(item =>
            {
                var soldier = RequireMap(item, "soldier");
                return new SoldierSnapshot(String(soldier, "type", defaultSoldier), Integer(soldier, "id", 0));
            }).ToArray();
            var items = ReadIntMap(map, "items");
            return new BaseSnapshot(
                identified.Id, String(map, "name", string.Empty), Double(map, "lon", 0),
                Double(map, "lat", 0), Array.AsReadOnly(facilities), Array.AsReadOnly(crafts),
                Array.AsReadOnly(soldiers), items, Integer(map, "scientists", 0), Integer(map, "engineers", 0));
        }).ToArray());
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
            Pair("mods", Sequence(labels.Select(Scalar))),
            Pair("ironman", snapshot.Identity.Ironman ? Boolean(true) : null),
            Pair("oxcePortCampaignId", Scalar(snapshot.Identity.Id.ToString())),
            Pair("oxcePortCreatedAtUtc", Scalar(snapshot.Identity.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture))),
        ]);
    }

    private static YamlMappingNode BuildBody(CampaignSnapshot snapshot, OxceSaveDocument? source)
    {
        var originalCountries = Maps(source?.Body, "countries").ToDictionary(
            map => String(map, "type", string.Empty), StringComparer.Ordinal);
        var originalRegions = Maps(source?.Body, "regions").ToDictionary(
            map => String(map, "type", string.Empty), StringComparer.Ordinal);
        EnsureUnique(snapshot.Bases.Select(static item => item.Id), "base ID");
        var originalBases = IdentifyBases(source?.Body).ToDictionary(static item => item.Id, static item => item.Value);
        var countries = snapshot.Countries.Select(country => BuildCountry(
            country, originalCountries.GetValueOrDefault(country.RuleId))).ToArray();
        var regions = snapshot.Regions.Select(region => BuildRegion(
            region, originalRegions.GetValueOrDefault(region.RuleId))).ToArray();
        var bases = snapshot.Bases.Select(item => BuildBase(
            item, originalBases.GetValueOrDefault(item.Id))).ToArray();
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
            Pair("ids", Mapping(snapshot.NextIds.Select(pair => Pair(pair.Key, Integer(pair.Value))))),
            Pair("countries", Sequence(countries)),
            Pair("regions", Sequence(regions)),
            Pair("bases", Sequence(bases)),
            Pair("tags", ScriptValues(snapshot.ScriptValues)),
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

    private static YamlMappingNode BuildBase(BaseSnapshot value, YamlMappingNode? source)
    {
        EnsureUnique(value.Facilities.Select(FacilityIdentity), "facility identity");
        var oldFacilities = Maps(source, "facilities").ToDictionary(FacilityIdentity);
        var oldCrafts = Maps(source, "crafts").ToDictionary(EntityIdentity, StringComparer.Ordinal);
        // Soldier IDs are global; legacy saves may omit the type that ReadBases defaults.
        var oldSoldiers = Maps(source, "soldiers").ToDictionary(map => Integer(map, "id", 0));
        var facilities = value.Facilities.Select(facility => Overlay(
            oldFacilities.GetValueOrDefault(FacilityIdentity(facility)),
            [
                Pair("type", Scalar(facility.RuleId)), Pair("x", Integer(facility.X)), Pair("y", Integer(facility.Y)),
                Pair("buildTime", facility.BuildTime == 0 ? null : Integer(facility.BuildTime)),
                Pair("ammo", facility.Ammo == 0 ? null : Integer(facility.Ammo)),
                Pair("ammoMissingReported", facility.AmmoMissingReported ? Boolean(true) : null),
                Pair("disabled", facility.Disabled ? Boolean(true) : null),
                Pair("hadPreviousFacility", facility.HadPreviousFacility ? Boolean(true) : null),
            ])).ToArray();
        var crafts = value.Crafts.Select(craft => Overlay(oldCrafts.GetValueOrDefault(EntityIdentity(craft)),
            [Pair("type", Scalar(craft.RuleId)), Pair("id", Integer(craft.Id))])).ToArray();
        var soldiers = value.Soldiers.Select(soldier => Overlay(oldSoldiers.GetValueOrDefault(soldier.Id),
            [Pair("type", Scalar(soldier.RuleId)), Pair("id", Integer(soldier.Id))])).ToArray();
        return Overlay(source,
        [
            Pair("lon", Real(value.Longitude)), Pair("lat", Real(value.Latitude)),
            Pair("id", Integer(value.Id)),
            Pair("name", value.Name.Length == 0 ? null : Scalar(value.Name)),
            Pair("facilities", Sequence(facilities)), Pair("soldiers", Sequence(soldiers)),
            Pair("crafts", Sequence(crafts)),
            Pair("items", Mapping(value.Items.Select(pair => Pair(pair.Key, Integer(pair.Value))))),
            Pair("scientists", Integer(value.Scientists)), Pair("engineers", Integer(value.Engineers)),
        ]);
    }

    private static string EntityIdentity(YamlMappingNode value) =>
        $"{String(value, "type", string.Empty)}\0{Integer(value, "id", 0)}";

    private static string EntityIdentity(CraftSnapshot value) => $"{value.RuleId}\0{value.Id}";

    private static BaseFacilityIdentity FacilityIdentity(YamlMappingNode value) => new(
        String(value, "type", string.Empty), Integer(value, "x", -1), Integer(value, "y", -1));

    private static BaseFacilityIdentity FacilityIdentity(FacilitySnapshot value) => new(
        value.RuleId, value.X, value.Y);

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

    private readonly record struct IdentifiedBase(int Id, YamlMappingNode Value);

    private readonly record struct BaseFacilityIdentity(string RuleId, int X, int Y);

    private static YamlMappingNode? ScriptValues(IReadOnlyList<ScriptValueEntry> values) => values.Count == 0
        ? null
        : Mapping(values.Select(value => Pair(value.TagName, Integer(value.Value))));

    private static YamlMappingNode Time(CampaignTime value) => Mapping(
    [
        Pair("second", Integer(value.Second)), Pair("minute", Integer(value.Minute)),
        Pair("hour", Integer(value.Hour)), Pair("weekday", Integer(value.Weekday)),
        Pair("day", Integer(value.Day)), Pair("month", Integer(value.Month)), Pair("year", Integer(value.Year)),
    ]);

    private static CampaignTime ReadTime(YamlMappingNode value) => new CampaignTime(
        Integer(value, "weekday", 6), Integer(value, "day", 1), Integer(value, "month", 1),
        Integer(value, "year", 1999), Integer(value, "hour", 12), Integer(value, "minute", 0),
        Integer(value, "second", 0)).Validate();

    private static YamlMappingNode Overlay(YamlMappingNode? source, IReadOnlyList<(string Key, YamlNode? Value)> values)
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

    private static string String(YamlMappingNode owner, string key, string fallback) =>
        owner.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : fallback;

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

    private static int Integer(YamlMappingNode owner, string key, int fallback) =>
        owner.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : fallback;

    private static ulong ULong(YamlMappingNode owner, string key, ulong fallback) =>
        owner.TryGet(key, out var node) ? YamlValueReader.ReadUInt64(node!) : fallback;

    private static double Double(YamlMappingNode owner, string key, double fallback) =>
        owner.TryGet(key, out var node) ? YamlValueReader.ReadDouble(node!) : fallback;

    private static bool Boolean(YamlMappingNode owner, string key, bool fallback) =>
        owner.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : fallback;

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

    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            ReadOnlySpan<char> replacements =
            [
                '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
                '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
                '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
                '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
            ];
            var characters = new char[bytes.Length];
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                characters[index] = value is >= 0x80 and <= 0x9F ? replacements[value - 0x80] : (char)value;
            }
            return new string(characters);
        }
    }

    private static (string Key, YamlNode? Value) Pair(string key, YamlNode? value) => (key, value);
    private static YamlMappingEntry PairNode(string key, YamlNode value) => new(Scalar(key), value);
    private static YamlScalarNode Scalar(string value) => new(GeneratedSpan, value, YamlScalarStyle.Plain);
    private static YamlScalarNode Integer(int value) => Scalar(value.ToString(CultureInfo.InvariantCulture));
    private static YamlScalarNode Long(long value) => Scalar(value.ToString(CultureInfo.InvariantCulture));
    private static YamlScalarNode ULong(ulong value) => Scalar(value.ToString(CultureInfo.InvariantCulture));
    private static YamlScalarNode Real(double value) => Scalar(value.ToString("R", CultureInfo.InvariantCulture));
    private static YamlScalarNode Boolean(bool value) => Scalar(value ? "true" : "false");
    private static YamlSequenceNode Sequence(IEnumerable<YamlNode> values) => new(GeneratedSpan, values);
    private static YamlMappingNode Mapping(IEnumerable<(string Key, YamlNode? Value)> values) =>
        new(GeneratedSpan, values.Where(static pair => pair.Value is not null)
            .Select(static pair => PairNode(pair.Key, pair.Value!)));
}
