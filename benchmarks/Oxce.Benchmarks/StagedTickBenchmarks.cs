using BenchmarkDotNet.Attributes;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using static Oxce.Benchmarks.PublicCampaignFixtures;

namespace Oxce.Benchmarks;

/// <summary>
/// Branch 6 S2 (ADR 0035): one midnight tick of a populated world, run directly and staged after its day level
/// on a shadow copy. The difference is the cost of a stage point: a capture, a restore and the shadow's preflights
/// and day handlers. A month tick pays it once a month, an event payload tick once per due event.
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class StagedTickBenchmarks
{
    private RuntimeContent _content = null!;
    private CampaignSnapshot _snapshot = null!;
    private CampaignState _campaign = null!;

    /// <summary>Flying UFOs in the world.</summary>
    [Params(8, 128)]
    public int Ufos { get; set; }

    [Params(false, true)]
    public bool Staged { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _content = LoadContent(["strategic-world.rul"]);
        var initial = PlacedCampaign(_content, new CampaignTime(5, 1, 1, 1999, 23, 59, 55));
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [Mission(4, "MISSION_SCOUT", Ufos)],
                Ufos = [.. Enumerable.Range(0, Ufos).Select(index => FlyingUfo(10 + index, 4 + index, "UFO_SCOUT", 4,
                    1.0 + index * 0.01, -0.2 - index * 0.002, 50))],
            },
        };
        _snapshot = WithNextId(initial, "STR_UFO", 4 + Ufos);
        Restore();
        if (Advance() != 1) throw new InvalidOperationException("The midnight tick did not run.");
    }

    /// <summary>Every invocation is a fresh midnight tick; the staging switch is set outside the measurement.</summary>
    [IterationSetup]
    public void Restore()
    {
        CampaignState.StageEveryTickEverywhere = Staged;
        _campaign = CampaignState.Restore(_snapshot, _content, new SplitMix64RandomSource(40));
    }

    [Benchmark]
    public int Advance() =>
        ((CampaignTimeAdvanced)_campaign.Execute(new AdvanceCampaignTime(1)).Events[0]).Summary.TickCount;
}
