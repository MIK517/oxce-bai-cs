using System.Diagnostics;
using System.Globalization;
using Oxce.Gameplay.Campaigns;
using static Oxce.Benchmarks.CampaignTimeAdvanceBenchmarks;

namespace Oxce.Benchmarks;

/// <summary>
/// <c>--time-profile [scenario...] [--speed FiveSeconds|OneDay] [--seconds N] [--top N]</c>: runs the
/// <see cref="CampaignTimeAdvanceBenchmarks"/> scenarios and prints the cost per tick, the tick work per
/// in-game day against the one-day speed target, and where the time goes by phase
/// (<see cref="CampaignTimeProfile"/>). The per-tick and per-day figures come from an unprofiled run; the
/// phase table from a separate profiled run, whose clock reads add roughly 20 ns per phase.
/// Not statistical evidence: use the benchmark for comparisons.
/// </summary>
internal static class TimeAdvanceProfiler
{
    /// <summary>Soft target in docs/performance-targets.md: tick work per in-game day at one-day speed.</summary>
    private static readonly TimeSpan DayTarget = TimeSpan.FromSeconds(0.6);
    private const int TicksPerDay = 17_280;

    public static int Run(string[] args)
    {
        var scenarios = new List<Scenario>();
        var speed = CampaignTimeSpeed.OneDay;
        var seconds = 3.0;
        var top = 25;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--speed": speed = Enum.Parse<CampaignTimeSpeed>(Value(args, ++index), ignoreCase: true); break;
                case "--seconds": seconds = double.Parse(Value(args, ++index), CultureInfo.InvariantCulture); break;
                case "--top": top = int.Parse(Value(args, ++index), CultureInfo.InvariantCulture); break;
                default: scenarios.Add(Enum.Parse<Scenario>(args[index], ignoreCase: true)); break;
            }
        }
        if (scenarios.Count == 0) scenarios.AddRange(Enum.GetValues<Scenario>());
        var exceeded = false;
        foreach (var scenario in scenarios)
        {
            var benchmark = new CampaignTimeAdvanceBenchmarks { Case = scenario, Speed = speed };
            benchmark.Setup();
            Measure(benchmark, seconds / 2);
            var perTick = Measure(benchmark, seconds);
            var perDay = perTick * TicksPerDay;
            var over = speed == CampaignTimeSpeed.OneDay && perDay > DayTarget;
            exceeded |= over;
            var verdict = speed != CampaignTimeSpeed.OneDay ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" (target {DayTarget.TotalSeconds:F1} s{(over ? ", EXCEEDED" : "")})");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{scenario} at {speed}: {perTick.TotalMicroseconds:F2} us/tick, {perDay.TotalSeconds:F3} s per in-game day{verdict}"));

            var profile = new CampaignTimeProfile();
            benchmark.Profile = profile;
            Measure(benchmark, seconds / 2);
            profile.Reset();
            var ticks = MeasureTicks(benchmark, seconds, out _);
            benchmark.Profile = null;
            foreach (var entry in profile.Entries.Where(static entry => entry.Calls > 0)
                         .OrderByDescending(static entry => entry.Elapsed).Take(top))
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {entry.Elapsed.TotalMicroseconds / ticks,9:F3} us/tick {entry.Calls / (double)ticks,6:F2} calls/tick  {entry.Name}"));
            Console.WriteLine();
        }
        return exceeded ? 3 : 0;
    }

    private static string Value(string[] args, int index) =>
        index < args.Length ? args[index] : throw new ArgumentException($"{args[index - 1]} needs a value.");

    private static TimeSpan Measure(CampaignTimeAdvanceBenchmarks benchmark, double seconds)
    {
        var ticks = MeasureTicks(benchmark, seconds, out var elapsed);
        return elapsed / ticks;
    }

    private static long MeasureTicks(CampaignTimeAdvanceBenchmarks benchmark, double seconds, out TimeSpan elapsed)
    {
        long ticks = 0;
        var started = Stopwatch.GetTimestamp();
        do
        {
            benchmark.Restore();
            ticks += benchmark.Advance();
            elapsed = Stopwatch.GetElapsedTime(started);
        } while (elapsed.TotalSeconds < seconds);
        return ticks;
    }
}
