using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Bootstrap;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Owned UFO, TFTD and modded saves driven through the geoscape client with the real globe
/// resources: every target can be opened, a game day runs with the alerts answered, and a quick
/// save and load continue the same campaign.
/// </summary>
public sealed class PrivateGeoscapeScenarioTests
{
    // Real mods run with the production forecast work bounds (branch 6 S3).
    public PrivateGeoscapeScenarioTests() => Oxce.Gameplay.Campaigns.World.CampaignWorld.ProductionWorkBounds = true;

    [Fact]
    public void UfoSavesPlayThroughTheGeoscape() => Play("xcom1", "-", "vanilla-ufo");

    [Fact]
    public void TftdSavesPlayThroughTheGeoscape() => Play("xcom2", "-", "vanilla-tftd");

    [Fact]
    public void ModdedSavesPlayThroughTheGeoscape() => Play("40k", "40k_ROSIGMA_edits", "modded/rosigma");

    private static void Play(string master, string addon, string family)
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addon, new("Extended", "8.6.1.0"));
        var loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
        var content = loaded.Content!;
        var assets = GeoscapeAssets.Load(loaded.VirtualFiles!, content.Resources, content.RuntimeRules.Campaign.Globe);
        var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
            new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
        var directory = TestFixtures.RepositoryPath("fixtures", "private", "saves", family, "early");
        var files = Directory.EnumerateFiles(directory, "*.sav").Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var source = OxceSaveAdapter.LoadFile(file, content, new SplitMix64RandomSource(31), options);
            var campaign = source.Campaign;
            string? saved = null;
            GeoscapeClient? client = null;
            client = new GeoscapeClient(new GeoscapeClientSetup(Session(campaign, source.Source.GlobeView),
                content.RuntimeRules, assets, Oxce.Rendering.IndexedInterfaceFont.Create(), static text => text)
            {
                Stopwatch = static () => TimeSpan.Zero,
                Save = () =>
                {
                    var view = client!.CurrentView;
                    saved = OxceSaveAdapter.EmitLoadedCampaign(campaign.Capture(), source.Source,
                        new OxceSaveWriteOptions { GlobeView = new(view.Longitude, view.Latitude, view.Zoom) });
                },
                Load = () =>
                {
                    var reloaded = OxceSaveAdapter.Load(saved!, name, content, new SplitMix64RandomSource(31), options);
                    campaign = reloaded.Campaign;
                    return Session(campaign, reloaded.Source.GlobeView);
                },
            });
            var view = source.Source.GlobeView;
            Assert.Equal((view.Longitude, view.Latitude), (client.CurrentView.Longitude, client.CurrentView.Latitude));

            // Every target the globe offers opens its screen and closes with Cancel or OK.
            foreach (var target in GlobeTargets.Build(client.World).All)
            {
                client.View.Center(target.Longitude, target.Latitude);
                client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, 256, 200, 1, 1));
                for (var guard = 0; client.Popups.Count != 0 && guard < 8; guard++)
                    client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 27, InputKeyModifiers.None));
                Assert.Empty(client.Popups);
            }

            if (client.TimeCanRun)
            {
                var start = campaign.QueryOverview().Time;
                Key(client, '5');
                var seen = StrategicWorldGeoscapeScenarioTests.Drive(client, () => campaign, 24 * 60, () => false);
                // A day runs unless a branch 5/6 boundary halts the clock with its reason.
                var after = campaign.QueryOverview().Time;
                var minutes = (after.Day - start.Day) * 1440 + (after.Hour - start.Hour) * 60 + after.Minute - start.Minute;
                Assert.True(after.Month != start.Month || minutes >= 24 * 60 || client.Time.HaltReason is not null,
                    $"{family}/{name} stopped after {minutes} minutes without a reason: {string.Join(" | ", seen)}");
            }

            Key(client, 0x4000003E);
            Assert.Equal("Campaign saved.", client.Status);
            var before = campaign.Capture();
            Key(client, 0x40000042);
            Key(client, 13);
            Assert.Equal("Campaign loaded.", client.Status);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
    }

    private static CampaignUiSession Session(CampaignState campaign, OxceGlobeView view) =>
        CampaignUiSession.ForCampaign(campaign, campaign, campaign) with
        {
            GlobeView = new GlobeViewState(view.Longitude, view.Latitude, view.Zoom),
        };

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));
}
