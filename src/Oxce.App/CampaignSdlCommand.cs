using Oxce.Core.Diagnostics;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Platform.Sdl;
using Oxce.Savegames.Oxce;

internal static class CampaignSdlCommand
{
    public static int Run(string installationRoot, string masterId, string addOnId, string destination)
    {
        var root = Path.GetFullPath(installationRoot);
        var diagnostics = new DiagnosticCollector(100_000);
        var options = new ModDiscoveryOptions { ExternalResourceRoots = [root] };
        var standard = ModDiscovery.ScanDirectory(Path.Combine(root, "standard"), diagnostics, options);
        var user = ModDiscovery.ScanDirectory(Path.Combine(root, "user", "mods"), diagnostics, options);
        var catalog = ModCatalog.Create(standard.Mods.Concat(user.Mods), diagnostics);
        string[] activeMods = addOnId == "-" ? [masterId] : [masterId, addOnId];
        var plan = ModLoadPlanner.Create(
            catalog,
            activeMods.Select(static id => new ModActivation(id, true)),
            masterId,
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);
        var content = ContentSnapshotBuilder.Build(plan, diagnostics);
        if (!content.Capabilities.Has(ContentLoadStage.RuntimeLinked))
        {
            var errors = content.Diagnostics.Where(static item => item.Severity >= DiagnosticSeverity.Error);
            throw new InvalidDataException(string.Join(Environment.NewLine,
                errors.Take(25).Select(static item => $"{item.Code}: {item.Message}")));
        }

        var campaign = CampaignFactory.Create(
            content.Content,
            new NewCampaignRequest(new CampaignId(Guid.NewGuid()), "SDL campaign", masterId, activeMods,
                CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(0x4F584345UL),
            SystemCampaignClock.Instance);
        var client = new CampaignOverviewClient(campaign, campaign);
        Console.WriteLine("Click the globe to place the starting base. Press Space to advance one minute; Escape quits.");
        var host = new SdlIndexedWindowHost(client, new SdlWindowOptions("OXCE .NET campaign foundation")
        {
            Scale = 3,
        });
        var result = host.Run();
        if (destination != "-")
        {
            OxceSaveAdapter.WriteAtomic(Path.GetFullPath(destination), campaign.Capture());
            Console.WriteLine($"Campaign saved to {Path.GetFullPath(destination)}");
        }
        return result;
    }
}
