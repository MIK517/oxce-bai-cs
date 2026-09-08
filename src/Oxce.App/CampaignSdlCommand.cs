using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Extensions;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Loading;
using Oxce.Platform.Sdl;
using Oxce.Savegames.Oxce;

internal static class CampaignSdlCommand
{
    public static int Run(string installationRoot, string masterId, string addOnId, string destination)
    {
        var request = InstallationLoadRequest.ForMasterAndAddOn(
            installationRoot, masterId, addOnId, new ModEngineIdentity("Extended", "8.6.1.0"));
        var loaded = InstallationContentLoader.Load(request);
        if (!loaded.IsSuccess) throw new InvalidDataException(loaded.DescribeFailure());
        var content = loaded.Content!;
        var plan = InstallationPlanBuilder.Create(request);
        if (!plan.IsSuccess) throw new InvalidDataException(plan.DescribeFailure());
        var assets = CampaignUiAssets.Load(plan.Plan!.CreateVirtualFileCatalog(), installationRoot,
            content.Presentation);
        loaded = null!;
        var activeMods = request.ActiveMods;

        var campaign = CampaignFactory.Create(
            content,
            new NewCampaignRequest(new CampaignId(Guid.NewGuid()), "SDL campaign", masterId, activeMods,
                CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(0x4F584345UL),
            SystemCampaignClock.Instance);
        var extensionDiagnostics = new DiagnosticCollector();
        using var extensions = ManagedExtensionHost.LoadFromDirectory(
            Path.Combine(Path.GetFullPath(installationRoot), "extensions"), extensionDiagnostics);
        var extensionSession = extensions.AttachCampaign(campaign, campaign);
        OxceSaveDocument? source = null;
        var savePath = destination == "-" ? null : Path.GetFullPath(destination);
        var client = new CampaignLogisticsClient(new(extensionSession, extensionSession), assets.Font, assets.Localize,
            savePath is null ? null : Save, savePath is null ? null : Load);
        Console.WriteLine("I: stores; B/S/T: buy/sell/transfer; arrows and +/-: quantities; Enter then Y: confirm. F5/F9: save/load.");
        var host = new SdlIndexedWindowHost(client, new SdlWindowOptions("OXCE .NET strategic logistics")
        {
            Scale = 2,
            ExitOnEscape = false,
        });
        int result;
        try
        {
            result = host.Run();
            if (savePath is not null) Save();
        }
        finally { extensionSession.Dispose(); }
        foreach (var diagnostic in extensionDiagnostics.Snapshot()
                     .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning))
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        return result;

        void Save()
        {
            if (source is null) OxceSaveAdapter.WriteNewCampaignAtomic(savePath!, campaign.Capture());
            else OxceSaveAdapter.RewriteLoadedCampaignAtomic(savePath!, campaign.Capture(), source);
        }

        CampaignUiSession Load()
        {
            var restored = OxceSaveAdapter.LoadFile(savePath!, content, new SplitMix64RandomSource(0),
                new(masterId, activeMods.ToHashSet(StringComparer.Ordinal)));
            extensionSession.Dispose();
            campaign = restored.Campaign;
            source = restored.Source;
            extensionSession = extensions.AttachCampaign(campaign, campaign);
            return new(extensionSession, extensionSession);
        }
    }
}
