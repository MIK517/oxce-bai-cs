using System.Diagnostics;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Accumulated wall time of one named phase of time advance.</summary>
public readonly record struct CampaignTimeProfileEntry(string Name, TimeSpan Elapsed, long Calls);

/// <summary>
/// Opt-in diagnostics for time advance: wall time per registered preflight check, per time handler
/// and per world-simulation preflight step. It never affects gameplay and is not saved. Attach it
/// with <see cref="CampaignState.TimeProfile"/>; timing adds about two clock reads per phase, so
/// compare profiled runs only with each other.
/// </summary>
public sealed class CampaignTimeProfile
{
    /// <summary>Prefix of registered preflight checks.</summary>
    public const string PreflightPrefix = "preflight: ";

    /// <summary>Prefix of time handlers.</summary>
    public const string ApplyPrefix = "apply: ";

    /// <summary>Prefix of world-simulation preflight steps, which are part of its preflight check.</summary>
    public const string WorldPrefix = "world simulation/";

    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);
    private readonly List<string> _names = [];
    private long[] _ticks = new long[64];
    private long[] _calls = new long[64];

    /// <summary>Phases in the order they were first seen.</summary>
    public IReadOnlyList<CampaignTimeProfileEntry> Entries
    {
        get
        {
            var entries = new CampaignTimeProfileEntry[_names.Count];
            for (var index = 0; index < entries.Length; index++)
                entries[index] = new(_names[index], Stopwatch.GetElapsedTime(0, _ticks[index]), _calls[index]);
            return entries;
        }
    }

    /// <summary>Clears accumulated time and calls but keeps the phase names.</summary>
    public void Reset()
    {
        Array.Clear(_ticks);
        Array.Clear(_calls);
    }

    internal void Record(string name, long started)
    {
        var elapsed = Stopwatch.GetTimestamp() - started;
        var index = Index(name);
        _ticks[index] += elapsed;
        _calls[index]++;
    }

    private int Index(string name)
    {
        if (_indexes.TryGetValue(name, out var index)) return index;
        index = _names.Count;
        _names.Add(name);
        _indexes.Add(name, index);
        if (index == _ticks.Length)
        {
            Array.Resize(ref _ticks, index * 2);
            Array.Resize(ref _calls, index * 2);
        }
        return index;
    }

    /// <summary>Times consecutive steps: each <see cref="Mark"/> records the time since the previous one.
    /// Does nothing without a profile.</summary>
    internal struct Steps(CampaignTimeProfile? profile)
    {
        private long _last = profile is null ? 0 : Stopwatch.GetTimestamp();

        public void Mark(string name)
        {
            if (profile is null) return;
            profile.Record(name, _last);
            _last = Stopwatch.GetTimestamp();
        }
    }
}
