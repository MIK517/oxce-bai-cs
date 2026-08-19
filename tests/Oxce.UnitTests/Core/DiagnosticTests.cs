using Microsoft.Extensions.Logging;
using Oxce.Core.Diagnostics;
using Oxce.Engine.Diagnostics;
using Xunit;

namespace Oxce.UnitTests.Core;

public sealed class DiagnosticTests
{
    [Fact]
    public void CollectorBoundsUntrustedDiagnosticVolume()
    {
        var collector = new DiagnosticCollector(maximumDiagnostics: 2);
        collector.Report(Event("OXCE1001", DiagnosticSeverity.Warning));
        collector.Report(Event("OXCE1002", DiagnosticSeverity.Error));
        collector.Report(Event("OXCE1003", DiagnosticSeverity.Critical));

        Assert.Equal(["OXCE1001", "OXCE1002"], collector.Snapshot().Select(item => item.Code).ToArray());
        Assert.Equal(1, collector.DroppedCount);
    }

    [Fact]
    public void LoggingAdapterPreservesStructuredCompatibilityContext()
    {
        var logger = new RecordingLogger();
        var source = new SourceSpan("rules/example.rul", new SourcePosition(7, 11, 42), new SourcePosition(7, 15, 46));
        var diagnostic = new DiagnosticEvent(
            "OXCE-MOD-0001",
            DiagnosticSeverity.Error,
            "Missing required rule.",
            source,
            new DiagnosticContext("layer-2", "example", "items", "STR_ITEM", "STR_AMMO"));

        new LoggingDiagnosticSink(logger).Report(diagnostic);

        Assert.Equal(LogLevel.Error, logger.Level);
        Assert.Equal("OXCE-MOD-0001", logger.EventId.Name);
        Assert.Equal("OXCE-MOD-0001: Missing required rule.", logger.Message);
        Assert.Equal("example", logger.Properties["ModId"]);
        Assert.Equal("STR_ITEM", logger.Properties["RuleId"]);
        Assert.Equal("STR_AMMO", logger.Properties["RelatedId"]);
        Assert.Equal(7, logger.Properties["Line"]);
    }

    [Fact]
    public void DiagnosticRequiresStableCodeAndMessage()
    {
        Assert.Throws<ArgumentException>(
            () => new DiagnosticEvent("", DiagnosticSeverity.Error, "message"));
        Assert.Throws<ArgumentException>(
            () => new DiagnosticEvent("OXCE1001", DiagnosticSeverity.Error, ""));
    }

    private static DiagnosticEvent Event(string code, DiagnosticSeverity severity) =>
        new(code, severity, "message");

    private sealed class RecordingLogger : ILogger
    {
        internal LogLevel Level { get; private set; }

        internal EventId EventId { get; private set; }

        internal string? Message { get; private set; }

        internal Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            EventId = eventId;
            Message = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var property in properties)
                {
                    Properties[property.Key] = property.Value;
                }
            }
        }
    }
}
