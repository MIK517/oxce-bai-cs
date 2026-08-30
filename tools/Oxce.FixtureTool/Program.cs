using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.FixtureSupport;
using Oxce.Formats.Yaml;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Phase3;

return FixtureTool.Run(args, Console.Out, Console.Error);

internal static class FixtureTool
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            return args switch
            {
                ["hash", var path] => Hash(path, output),
                ["inspect", var path] => Inspect(path, output),
                ["normalize", var input] => Normalize(input, null, output),
                ["normalize", var input, var destination] => Normalize(input, destination, output),
                ["normalize-yaml", var input] => NormalizeYaml(input, null, output),
                ["normalize-yaml", var input, var destination] => NormalizeYaml(input, destination, output),
                ["dump-rules", var modsRoot, var masterId] => DumpRules(modsRoot, masterId, null, output),
                ["dump-rules", var modsRoot, var masterId, var destination] =>
                    DumpRules(modsRoot, masterId, destination, output),
                ["dump-typed-rules", var modsRoot, var masterId] =>
                    DumpTypedRules(modsRoot, masterId, null, output),
                ["dump-typed-rules", var modsRoot, var masterId, var destination] =>
                    DumpTypedRules(modsRoot, masterId, destination, output),
                ["audit-typed-install", var modsRoot, var resourceRoot, var masterId, var destination] =>
                    AuditTypedInstall(modsRoot, resourceRoot, masterId, destination, output),
                ["audit-content-install", var installationRoot, var masterId, var addOnId, var destination] =>
                    AuditContentInstall(installationRoot, masterId, addOnId, destination, output),
                ["compare", var expected, var actual] => Compare(expected, actual, output),
                _ => Usage(error),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
        {
            error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Hash(string path, TextWriter output)
    {
        var digest = FileDigest.Calculate(path);
        output.WriteLine(JsonSerializer.Serialize(new { path, size = digest.Size, sha256 = digest.Sha256 }));
        return 0;
    }

    private static int Inspect(string path, TextWriter output)
    {
        var manifest = FixtureManifestLoader.Load(path);
        FixtureManifestVerifier.VerifyFiles(manifest, Directory.GetCurrentDirectory());
        output.WriteLine($"{manifest.Id}: {manifest.Inputs.Count} input(s), reference={manifest.Reference.Kind}");
        return 0;
    }

    private static int Normalize(string input, string? destination, TextWriter output)
    {
        var normalized = CanonicalJson.NormalizeFile(input);
        if (destination is null)
        {
            output.Write(normalized);
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
            Directory.CreateDirectory(directory!);
            File.WriteAllText(destination, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            output.WriteLine(destination);
        }

        return 0;
    }

    private static int Compare(string expected, string actual, TextWriter output)
    {
        if (CanonicalJson.FilesSemanticallyEqual(expected, actual))
        {
            output.WriteLine("equivalent");
            return 0;
        }

        output.WriteLine("different");
        return 1;
    }

    private static int NormalizeYaml(string input, string? destination, TextWriter output)
    {
        var normalized = YamlSemanticNormalizer.NormalizeToUtf8Json(YamlCompatibilityReader.ParseFile(input));
        if (destination is null)
        {
            output.Write(Encoding.UTF8.GetString(normalized));
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
            Directory.CreateDirectory(directory!);
            File.WriteAllBytes(destination, normalized);
            output.WriteLine(destination);
        }

        return 0;
    }

    private static int DumpRules(string modsRoot, string masterId, string? destination, TextWriter output)
    {
        var root = Path.GetFullPath(modsRoot);
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        var activations = catalog.Mods.Values
            .OrderBy(mod => mod.Metadata.Id, StringComparer.Ordinal)
            .Select(mod => new ModActivation(mod.Metadata.Id, true));
        var plan = ModLoadPlanner.Create(
            catalog,
            activations,
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"));
        var rules = RulesetComposer.Compose(plan);
        var normalized = RulesetCatalogNormalizer.NormalizeToUtf8Json(
            rules,
            new RulesetCatalogNormalizationOptions
            {
                NormalizeSourceName = source => Path.GetRelativePath(root, source).Replace('\\', '/'),
            });
        if (destination is null)
        {
            output.Write(Encoding.UTF8.GetString(normalized));
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
            Directory.CreateDirectory(directory!);
            File.WriteAllBytes(destination, normalized);
            output.WriteLine(destination);
        }

        return 0;
    }

    private static int DumpTypedRules(string modsRoot, string masterId, string? destination, TextWriter output)
    {
        var root = Path.GetFullPath(modsRoot);
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        var activations = catalog.Mods.Values
            .OrderBy(mod => mod.Metadata.Id, StringComparer.Ordinal)
            .Select(mod => new ModActivation(mod.Metadata.Id, true));
        var plan = ModLoadPlanner.Create(
            catalog,
            activations,
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"));
        var content = Phase3ContentCatalog.Build(plan);
        var normalized = Phase3ContentManifestNormalizer.NormalizeToUtf8Json(
            content,
            new RulesetCatalogNormalizationOptions
            {
                NormalizeSourceName = source => Path.GetRelativePath(root, source).Replace('\\', '/'),
            });
        if (destination is null)
        {
            output.Write(Encoding.UTF8.GetString(normalized));
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
            Directory.CreateDirectory(directory!);
            File.WriteAllBytes(destination, normalized);
            output.WriteLine(destination);
        }

        return content.Catalog.Capabilities.Has(ContentLoadStage.Linked) ? 0 : 1;
    }

    private static int AuditTypedInstall(
        string modsRoot,
        string resourceRoot,
        string masterId,
        string destination,
        TextWriter output)
    {
        var root = Path.GetFullPath(modsRoot);
        var diagnostics = new DiagnosticCollector(100_000);
        var discovery = ModDiscovery.ScanDirectory(
            root,
            diagnostics,
            new ModDiscoveryOptions { ExternalResourceRoots = [Path.GetFullPath(resourceRoot)] });
        var catalog = ModCatalog.Create(discovery.Mods, diagnostics);
        var activations = catalog.Mods.Values
            .OrderBy(mod => mod.Metadata.Id, StringComparer.Ordinal)
            .Select(mod => new ModActivation(mod.Metadata.Id, true));
        var plan = ModLoadPlanner.Create(
            catalog,
            activations,
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);
        return WriteContentAudit(plan, diagnostics, root, destination, output);
    }

    private static int AuditContentInstall(
        string installationRoot,
        string masterId,
        string addOnId,
        string destination,
        TextWriter output)
    {
        var root = Path.GetFullPath(installationRoot);
        var diagnostics = new DiagnosticCollector(100_000);
        var options = new ModDiscoveryOptions { ExternalResourceRoots = [root] };
        var standard = ModDiscovery.ScanDirectory(Path.Combine(root, "standard"), diagnostics, options);
        var user = ModDiscovery.ScanDirectory(Path.Combine(root, "user", "mods"), diagnostics, options);
        var catalog = ModCatalog.Create(standard.Mods.Concat(user.Mods), diagnostics);
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation(masterId, true), new ModActivation(addOnId, true)],
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);
        return WriteContentAudit(plan, diagnostics, root, destination, output);
    }

    private static int WriteContentAudit(
        ModLoadPlan plan,
        DiagnosticCollector diagnostics,
        string normalizationRoot,
        string destination,
        TextWriter output)
    {
        var managedBytesBeforeBuild = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBytesBeforeBuild = GC.GetAllocatedBytesForCurrentThread();
        var buildTimer = Stopwatch.StartNew();
        var snapshot = ContentSnapshotBuilder.Build(plan, diagnostics);
        buildTimer.Stop();
        var allocatedBytesDuringBuild = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBeforeBuild;
        var retained = diagnostics.Snapshot();
        var reportedDiagnostics = diagnostics.ReportedCount;
        var droppedDiagnostics = diagnostics.DroppedCount;
        var errors = retained.Count(item => item.Severity >= DiagnosticSeverity.Error);
        var warnings = retained.Count(item => item.Severity == DiagnosticSeverity.Warning);
        var firstErrors = retained.Where(item => item.Severity >= DiagnosticSeverity.Error)
            .Take(20).Select(item => new { item.Code, item.Message, Source = item.Source?.ToString() }).ToArray();
        var completedWithoutErrors = snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled) && errors == 0;
        retained = default;
        diagnostics = null!;
        var managedBytesAfterBuild = GC.GetTotalMemory(forceFullCollection: true);
        var content = snapshot.Content;
        var normalizationTimer = Stopwatch.StartNew();
        var normalized = Phase3ContentManifestNormalizer.NormalizeToUtf8Json(
            content,
            new RulesetCatalogNormalizationOptions
            {
                NormalizeSourceName = source => Path.GetRelativePath(normalizationRoot, source).Replace('\\', '/'),
            });
        normalizationTimer.Stop();
        var destinationPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllBytes(destinationPath, normalized);

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            stage = snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled)
                ? "scripts-compiled"
                : content.Catalog.Capabilities.Has(ContentLoadStage.Linked) ? "linked" : "typed",
            parsedFiles = content.ParsedFileCount,
            attemptedScripts = snapshot.CompiledScriptCount,
            scriptArtifacts = snapshot.Scripts.Count,
            eventPlans = snapshot.EventPlans.Count,
            tags = snapshot.Tags.Tags.Count,
            initialValues = snapshot.InitialValues.Count,
            diagnostics = reportedDiagnostics,
            errors,
            warnings,
            droppedDiagnostics,
            manifestBytes = normalized.Length,
            buildElapsedMilliseconds = buildTimer.Elapsed.TotalMilliseconds,
            normalizationElapsedMilliseconds = normalizationTimer.Elapsed.TotalMilliseconds,
            allocatedBytesDuringBuild,
            managedBytesBeforeBuild,
            managedBytesAfterBuild,
            managedBytesRetainedByBuild = managedBytesAfterBuild - managedBytesBeforeBuild,
            workingSetBytes = process.WorkingSet64,
            peakWorkingSetBytes = process.PeakWorkingSet64,
            destination = destinationPath,
            firstErrors,
        }));

        return completedWithoutErrors ? 0 : 1;
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  fixture hash <file>");
        error.WriteLine("  fixture inspect <manifest.json>");
        error.WriteLine("  fixture normalize <input.json> [output.json]");
        error.WriteLine("  fixture normalize-yaml <input.yml> [output.json]");
        error.WriteLine("  fixture dump-rules <mods-root> <master-id> [output.json]");
        error.WriteLine("  fixture dump-typed-rules <mods-root> <master-id> [output.json]");
        error.WriteLine("  fixture audit-typed-install <mods-root> <resource-root> <master-id> <output.json>");
        error.WriteLine("  fixture audit-content-install <installation-root> <master-id> <add-on-id> <output.json>");
        error.WriteLine("  fixture compare <expected.json> <actual.json>");
        return 2;
    }
}
