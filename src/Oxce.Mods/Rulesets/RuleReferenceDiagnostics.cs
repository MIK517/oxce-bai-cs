using Oxce.Core.Diagnostics;

namespace Oxce.Mods.Rulesets;

internal static class RuleReferenceDiagnostics
{
    public static void ReportDeferred(
        IDiagnosticSink diagnostics,
        RuleOperationSource source,
        string ruleType,
        string ruleId,
        string property,
        string relatedId)
    {
        diagnostics.Report(new DiagnosticEvent(
            ModDiagnosticCodes.DeferredRuleReference,
            DiagnosticSeverity.Warning,
            $"Rule '{ruleId}' property '{property}' retains unresolved runtime reference '{relatedId}'.",
            source.Span,
            new DiagnosticContext(source.LayerId, source.ModId, ruleType, ruleId, relatedId)));
    }
}
