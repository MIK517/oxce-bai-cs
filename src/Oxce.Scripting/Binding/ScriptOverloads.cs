using Oxce.Scripting.Types;

namespace Oxce.Scripting.Binding;

public readonly record struct ScriptOperationId
{
    public ScriptOperationId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }
}

public sealed class ScriptOperationOverload
{
    public ScriptOperationOverload(
        ScriptOperationId id,
        string name,
        IEnumerable<IEnumerable<ScriptTypeRef>> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        Id = id;
        Name = name;
        Arguments = Array.AsReadOnly(arguments
            .Select(static alternatives => Array.AsReadOnly(alternatives.ToArray()))
            .ToArray());
        if (Arguments.Count > ScriptLimits.MaximumArguments)
        {
            throw new ArgumentOutOfRangeException(nameof(arguments));
        }
        if (Arguments.Any(static alternatives => alternatives.Count == 0))
        {
            throw new ArgumentException("Each operation argument requires at least one type alternative.", nameof(arguments));
        }
    }

    public ScriptOperationId Id { get; }

    public string Name { get; }

    public IReadOnlyList<IReadOnlyList<ScriptTypeRef>> Arguments { get; }
}

public enum ScriptOverloadResolutionKind
{
    NoMatch,
    Selected,
    Ambiguous,
}

public enum ScriptArgumentClassification
{
    Known,
    UnknownSimple,
    UnknownSegment,
    Placeholder,
}

public readonly record struct ScriptArgumentType
{
    private ScriptArgumentType(ScriptArgumentClassification classification, ScriptTypeRef knownType)
    {
        Classification = classification;
        KnownType = knownType;
    }

    public ScriptArgumentClassification Classification { get; }

    public ScriptTypeRef KnownType { get; }

    public bool IsKnown => Classification == ScriptArgumentClassification.Known;

    public static ScriptArgumentType UnknownSimple { get; } =
        new(ScriptArgumentClassification.UnknownSimple, default);

    public static ScriptArgumentType UnknownSegment { get; } =
        new(ScriptArgumentClassification.UnknownSegment, default);

    public static ScriptArgumentType Placeholder { get; } =
        new(ScriptArgumentClassification.Placeholder, default);

    public static implicit operator ScriptArgumentType(ScriptTypeRef type) =>
        new(ScriptArgumentClassification.Known, type);
}

public sealed record ScriptOverloadResolution(
    ScriptOverloadResolutionKind Kind,
    ScriptOperationOverload? Selected,
    int Score);

public static class ScriptOverloadResolver
{
    public static ScriptOverloadResolution Resolve(
        IEnumerable<ScriptOperationOverload> candidates,
        IReadOnlyList<ScriptArgumentType> arguments)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(arguments);
        var bestScore = 0;
        ScriptOperationOverload? best = null;
        var ambiguous = false;

        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            var score = Score(candidate, arguments);
            if (score == 0)
            {
                continue;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                ambiguous = false;
            }
            else if (score == bestScore)
            {
                best = null;
                ambiguous = true;
            }
        }

        return bestScore == 0
            ? new ScriptOverloadResolution(ScriptOverloadResolutionKind.NoMatch, null, 0)
            : ambiguous
                ? new ScriptOverloadResolution(ScriptOverloadResolutionKind.Ambiguous, null, bestScore)
                : new ScriptOverloadResolution(ScriptOverloadResolutionKind.Selected, best, bestScore);
    }

    public static int Score(ScriptOperationOverload candidate, IReadOnlyList<ScriptArgumentType> arguments)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(arguments);
        if (candidate.Arguments.Count != arguments.Count)
        {
            return 0;
        }

        var score = 255;
        for (var index = 0; index < arguments.Count; index++)
        {
            var alternatives = candidate.Arguments[index];
            if (!arguments[index].IsKnown)
            {
                continue;
            }
            var argumentScore = 0;
            foreach (var expected in alternatives)
            {
                argumentScore = Math.Max(argumentScore, CompatibilityScore(
                    expected,
                    arguments[index].KnownType,
                    alternatives.Count - 1));
            }
            score = Math.Min(score, argumentScore);
        }
        return score;
    }

    public static int CompatibilityScore(
        ScriptTypeRef expected,
        ScriptTypeRef actual,
        int alternativeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(alternativeCount);
        if (expected.Id != actual.Id || expected.IsRegister != actual.IsRegister ||
            expected.IsReference != actual.IsReference || expected.IsWritable && expected != actual ||
            expected.IsEditableReference && !actual.IsEditableReference)
        {
            return 0;
        }

        return 255 - (expected.IsEditableReference != actual.IsEditableReference ? 128 : 0) -
            (expected.IsWritable != actual.IsWritable ? 64 : 0) - Math.Min(alternativeCount, 8);
    }
}
