using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// An ordered weighted option set. Reference: <c>Savegame/WeightedOptions.cpp</c>; the
/// reference <c>std::map</c> ordering is reproduced with ordinal string ordering so that
/// selection is deterministic for a given roll.
/// </summary>
public sealed class WorldWeightedOptions
{
    private readonly SortedDictionary<string, ulong> _choices = new(StringComparer.Ordinal);
    private ulong _totalWeight;

    public WorldWeightedOptions() { }

    public WorldWeightedOptions(IEnumerable<KeyValuePair<string, ulong>> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        foreach (var choice in choices) Set(choice.Key, choice.Value);
    }

    public ulong TotalWeight => _totalWeight;

    public bool IsEmpty => _totalWeight == 0;

    public IReadOnlyCollection<string> Names => _choices.Keys;

    public IEnumerable<KeyValuePair<string, ulong>> Entries => _choices;

    public ulong this[string id] => _choices.GetValueOrDefault(id);

    /// <summary>WeightedOptions::set; a zero weight removes the option.</summary>
    public void Set(string id, ulong weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_choices.TryGetValue(id, out var existing))
        {
            _totalWeight -= existing;
            if (weight != 0)
            {
                _choices[id] = weight;
                _totalWeight = Add(_totalWeight, weight);
            }
            else
            {
                _choices.Remove(id);
            }
            return;
        }
        if (weight == 0) return;
        _choices.Add(id, weight);
        _totalWeight = Add(_totalWeight, weight);
    }

    public WorldWeightedOptions Copy()
    {
        var copy = new WorldWeightedOptions();
        foreach (var choice in _choices) copy.Set(choice.Key, choice.Value);
        return copy;
    }

    /// <summary>WeightedOptions::choose; an empty set selects nothing.</summary>
    public string Choose(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (_totalWeight == 0) return string.Empty;
        return ChooseAt((ulong)random.NextInclusive(1, checked((int)_totalWeight)));
    }

    /// <summary>The selection for an already drawn roll in [1, <see cref="TotalWeight"/>].</summary>
    public string ChooseAt(ulong roll)
    {
        if (_totalWeight == 0) return string.Empty;
        if (roll < 1 || roll > _totalWeight) throw new ArgumentOutOfRangeException(nameof(roll));
        foreach (var choice in _choices)
        {
            if (roll <= choice.Value) return choice.Key;
            roll -= choice.Value;
        }
        return _choices.Last().Key;
    }

    private static ulong Add(ulong total, ulong weight)
    {
        var result = total + weight;
        if (result < total || result > int.MaxValue)
            throw new InvalidDataException("Weighted option totals exceed the supported range.");
        return result;
    }
}
