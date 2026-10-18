using System.Globalization;
using Oxce.Core.Compatibility;
using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Extensions;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Loading;
using Oxce.Platform.Sdl;
using Oxce.Savegames.Oxce;

internal static class CampaignSdlCommand
{
    public static int Run(
        string installationRoot, string masterId, string addOnId, string destination, InputValidationMode validationMode,
        bool loadAtStart = false)
    {
        if (loadAtStart && destination == "-")
        {
            Console.Error.WriteLine("--load needs a save path.");
            return 2;
        }
        var request = InstallationLoadRequest.ForMasterAndAddOn(
            installationRoot, masterId, addOnId, new ModEngineIdentity("Extended", "8.6.1.0"), validationMode);
        var loaded = InstallationContentLoader.Load(request);
        if (!loaded.IsSuccess) throw new InvalidDataException(loaded.DescribeFailure());
        var content = loaded.Content!;
        var startupDiagnostics = new DiagnosticCollector();
        foreach (var diagnostic in loaded.Diagnostics) startupDiagnostics.Report(diagnostic);
        var assets = CampaignUiAssets.Load(loaded.VirtualFiles!, content.Presentation, startupDiagnostics);
        var geoscapeAssets = GeoscapeAssets.Load(loaded.VirtualFiles!, content.Resources, content.RuntimeRules.Campaign.Globe);
        PrintWarnings(startupDiagnostics);
        var battleFiles = new VirtualBattleTerrainFiles(loaded.VirtualFiles!);
        loaded = null!;
        var activeMods = request.ActiveMods;

        var campaign = CampaignFactory.Create(
            content,
            new NewCampaignRequest(new CampaignId(Guid.NewGuid()), "SDL campaign", masterId, activeMods,
                CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(0x4F584345UL),
            SystemCampaignClock.Instance);
        campaign.BattleTerrainFiles = battleFiles;
        var extensionDiagnostics = new DiagnosticCollector();
        using var extensions = ManagedExtensionHost.LoadFromDirectory(
            Path.Combine(Path.GetFullPath(installationRoot), "extensions"), extensionDiagnostics);
        var extensionSession = extensions.AttachCampaign(campaign, campaign);
        OxceSaveDocument? source = null;
        var savePath = destination == "-" ? null : Path.GetFullPath(destination);
        CampaignUiSession session;
        try
        {
            session = loadAtStart ? Load() : CampaignUiSession.ForCampaign(campaign, extensionSession, extensionSession);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            extensionSession.Dispose();
            Console.Error.WriteLine($"The save could not be loaded: {error.Message}");
            return 1;
        }
        GeoscapeClient? client = null;
        client = new GeoscapeClient(new GeoscapeClientSetup(session, content.RuntimeRules,
            geoscapeAssets, assets.Font, assets.Localize)
        {
            Save = savePath is null ? null : () => Save(savePath),
            // MonthlyReportState's SaveGameState: the ironman save replaces the campaign's save; the autosave writes
            // _autogeo_.asav beside it.
            SaveRequested = savePath is null ? null : kind => Save(kind == CampaignSaveKind.Ironman ? savePath
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savePath))!, "_autogeo_.asav")),
            Load = savePath is null ? null : Load,
        });
        Console.WriteLine("Geoscape: 1-6 speed, arrows rotate, +/- zoom, wheel zoom, right click centre, middle drag scroll,");
        Console.WriteLine("Tab detail, R radar, ./, select target, Enter target info, B bases, I intercept, F funding, N new base, F5/F9 save/load, Esc quit");
        Console.WriteLine("(while the starting base's site is chosen, close the window to quit).");
        Console.WriteLine("Popups: Enter/Esc or 1-9 pick a button; Shift+click (or Shift+digit) adds craft to a wing;");
        Console.WriteLine("choosing a destination: left click a target or empty globe for a waypoint, Esc cancels.");
        Console.WriteLine("Bases: a new game starts by clicking the starting base's site; N (or N in the base screens) builds");
        Console.WriteLine("a new base: click a site, confirm the cost, type a name and press Enter.");
        Console.WriteLine("Bases: I stores; B/S/T buy/sell/transfer; arrows and +/- quantities; Enter then Y confirm; Esc back.");
        var host = new SdlIndexedWindowHost(client, new SdlWindowOptions("OXCE .NET geoscape")
        {
            Scale = 2,
            ExitOnEscape = false,
            // BaseNameState types base names.
            EnableTextInput = true,
        });
        int result;
        try
        {
            result = host.Run();
            if (savePath is not null)
            {
                try { Save(savePath); }
                catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); }
            }
        }
        finally { extensionSession.Dispose(); }
        PrintWarnings(extensionDiagnostics);
        return result;

        void Save(string path)
        {
            // D2: a pending acknowledgement with gameplay effects cannot be saved; the geoscape shows why.
            if (campaign.CaptureForSave(out var refusal) is not { } snapshot)
                throw new InvalidOperationException($"The campaign was not saved: {refusal}");
            // SavedGame keeps where the globe looked.
            var view = client!.CurrentView;
            var options = new OxceSaveWriteOptions { GlobeView = new(view.Longitude, view.Latitude, view.Zoom) };
            if (source is null) OxceSaveAdapter.WriteNewCampaignAtomic(path, snapshot, options);
            else OxceSaveAdapter.RewriteLoadedCampaignAtomic(path, snapshot, source, options);
        }

        static void PrintWarnings(DiagnosticCollector diagnostics)
        {
            foreach (var diagnostic in diagnostics.Snapshot()
                         .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning))
                Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        }

        CampaignUiSession Load()
        {
            var saveDiagnostics = new DiagnosticCollector();
            var restored = OxceSaveAdapter.LoadFile(savePath!, content, new SplitMix64RandomSource(0),
                new(masterId, activeMods.ToHashSet(StringComparer.Ordinal), Diagnostics: saveDiagnostics));
            PrintWarnings(saveDiagnostics);
            restored.Campaign.BattleTerrainFiles = battleFiles;
            // Attach before swapping so a failure leaves the running campaign and its session intact.
            var attached = extensions.AttachCampaign(restored.Campaign, restored.Campaign);
            extensionSession.Dispose();
            extensionSession = attached;
            campaign = restored.Campaign;
            source = restored.Source;
            var view = source.GlobeView;
            return CampaignUiSession.ForCampaign(campaign, extensionSession, extensionSession) with
            {
                GlobeView = new GlobeViewState(view.Longitude, view.Latitude, view.Zoom),
            };
        }
    }

    /// <summary>
    /// An unattended geoscape window run: a new campaign with its starting base on the first legal
    /// site, then scripted speed, interception, detail and zoom inputs for a few seconds.
    /// </summary>
    public static int RunSmoke(string installationRoot, string masterId, string addOnId, InputValidationMode validationMode)
    {
        var request = InstallationLoadRequest.ForMasterAndAddOn(
            installationRoot, masterId, addOnId, new ModEngineIdentity("Extended", "8.6.1.0"), validationMode);
        var loaded = InstallationContentLoader.Load(request);
        if (!loaded.IsSuccess) throw new InvalidDataException(loaded.DescribeFailure());
        var content = loaded.Content!;
        var diagnostics = new DiagnosticCollector();
        foreach (var diagnostic in loaded.Diagnostics) diagnostics.Report(diagnostic);
        var assets = CampaignUiAssets.Load(loaded.VirtualFiles!, content.Presentation, diagnostics);
        var geoscapeAssets = GeoscapeAssets.Load(loaded.VirtualFiles!, content.Resources, content.RuntimeRules.Campaign.Globe);
        var campaign = CampaignFactory.Create(content,
            new NewCampaignRequest(new CampaignId(Guid.NewGuid()), "SDL smoke", masterId, request.ActiveMods,
                CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(0x534D4F4BUL), SystemCampaignClock.Instance);
        var site = campaign.QueryBaseSites(true).FirstOrDefault(candidate => candidate.Longitude != 0 || candidate.Latitude != 0)
            ?? throw new InvalidDataException("The globe has no legal starting-base site.");
        if (campaign.Execute(new PlaceStartingBase(0, "Smoke", site.Longitude, site.Latitude)).Events
                .OfType<CampaignActionBlocked>().FirstOrDefault() is { } refused)
            throw new InvalidDataException("The starting base could not be placed: " + refused.Reason);
        var start = campaign.QueryOverview().Time;
        var client = new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            content.RuntimeRules, geoscapeAssets, assets.Font, assets.Localize));
        static ScriptedInput Key(double seconds, uint key) => new(TimeSpan.FromSeconds(seconds),
            GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));
        // Five-second speed keeps alerts away while the screens are opened and closed by key: the
        // Intercept list, globe detail, zoom, the base (selected with '.') and its Intercept list.
        ScriptedInput[] script =
        [
            Key(0.5, '1'), Key(2.0, 'i'), Key(2.5, 27), Key(3.0, 9), Key(3.5, '+'), Key(4.0, '.'), Key(4.5, 13), Key(5.0, 27),
            Key(5.5, '2'),
        ];
        var scripted = new ScriptedLoopClient(client, script, TimeSpan.FromSeconds(8));
        var host = new SdlIndexedWindowHost(scripted, new SdlWindowOptions("OXCE .NET geoscape smoke")
        {
            Scale = 2,
            ExitOnEscape = false,
            EnableTextInput = true,
            MaximumRunTime = TimeSpan.FromSeconds(20),
        });
        var result = host.Run();
        var run = host.LastRunDiagnostics;
        Console.WriteLine($"SDL video driver: {run?.VideoDriver}; renderer: {run?.Renderer}");
        Console.WriteLine($"Ticks: {run?.TickCount}; presented frames: {run?.PresentedFrameCount}; " +
            $"average presentation us: {run?.AveragePresentationDuration.TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}; " +
            $"maximum presentation us: {run?.MaximumPresentationDuration.TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Conversion and upload average us: {run?.AverageUploadDuration.TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}; " +
            $"maximum us: {run?.MaximumUploadDuration.TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Client tick p50 us: {scripted.TickDurationPercentile(50).TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}; " +
            $"p95 us: {scripted.TickDurationPercentile(95).TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}; " +
            $"maximum us: {scripted.TickDurationPercentile(100).TotalMicroseconds.ToString("F3", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Scripted inputs replayed: {scripted.Replayed}; campaign time {start} -> {campaign.QueryOverview().Time}; " +
            $"popups open: {client.Popups.Count}; status: {client.Time.HaltReason ?? client.Status}");
        foreach (var diagnostic in diagnostics.Snapshot().Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning))
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        // A window closed early, a run cut by the time limit or a popup left open is a failed smoke.
        if (result == 0 && (scripted.Replayed < script.Length || client.Popups.Count != 0 || client.ExitRequested))
        {
            Console.Error.WriteLine("The scripted geoscape run did not complete.");
            return 1;
        }
        return result;
    }
}
