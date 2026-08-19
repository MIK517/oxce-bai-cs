using System.Collections;
using Microsoft.Extensions.Logging;
using Oxce.Core.Diagnostics;

namespace Oxce.Engine.Diagnostics;

public sealed class LoggingDiagnosticSink(ILogger logger) : IDiagnosticSink
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Report(DiagnosticEvent diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var level = MapSeverity(diagnostic.Severity);
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        _logger.Log(
            level,
            new EventId(0, diagnostic.Code),
            new DiagnosticLogState(diagnostic),
            exception: null,
            static (state, _) => state.ToString());
    }

    private static LogLevel MapSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Trace => LogLevel.Trace,
        DiagnosticSeverity.Information => LogLevel.Information,
        DiagnosticSeverity.Warning => LogLevel.Warning,
        DiagnosticSeverity.Error => LogLevel.Error,
        DiagnosticSeverity.Critical => LogLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private sealed class DiagnosticLogState(DiagnosticEvent diagnostic) :
        IReadOnlyList<KeyValuePair<string, object?>>
    {
        private const string Format = "{DiagnosticCode}: {DiagnosticMessage}";

        public int Count => 11;

        public KeyValuePair<string, object?> this[int index] => index switch
        {
            0 => Pair("DiagnosticCode", diagnostic.Code),
            1 => Pair("DiagnosticMessage", diagnostic.Message),
            2 => Pair("Source", diagnostic.Source?.SourceName),
            3 => Pair("Line", diagnostic.Source?.Start.Line),
            4 => Pair("Column", diagnostic.Source?.Start.Column),
            5 => Pair("LayerId", diagnostic.Context.LayerId),
            6 => Pair("ModId", diagnostic.Context.ModId),
            7 => Pair("RuleType", diagnostic.Context.RuleType),
            8 => Pair("RuleId", diagnostic.Context.RuleId),
            9 => Pair("RelatedId", diagnostic.Context.RelatedId),
            10 => Pair("{OriginalFormat}", Format),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{diagnostic.Code}: {diagnostic.Message}";

        private static KeyValuePair<string, object?> Pair(string key, object? value) => new(key, value);
    }
}
