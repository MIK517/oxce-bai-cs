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
        using var extensionSession = extensions.AttachCampaign(campaign, campaign);
        var client = new CampaignOverviewClient(extensionSession, extensionSession);
        Console.WriteLine("Click the globe to place the starting base. Press Space to advance one minute; Escape quits.");
        var host = new SdlIndexedWindowHost(client, new SdlWindowOptions("OXCE .NET campaign foundation")
        {
            Scale = 3,
        });
        var result = host.Run();
        if (destination != "-")
        {
            OxceSaveAdapter.WriteNewCampaignAtomic(Path.GetFullPath(destination), campaign.Capture());
            Console.WriteLine($"Campaign saved to {Path.GetFullPath(destination)}");
        }
        foreach (var diagnostic in extensionDiagnostics.Snapshot()
                     .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning))
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        return result;
    }
}
