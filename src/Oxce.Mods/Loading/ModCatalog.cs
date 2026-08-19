using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Mods.Discovery;

namespace Oxce.Mods.Loading;

public sealed class ModCatalog
{
    private readonly ReadOnlyDictionary<string, ModCandidate> _mods;

    private ModCatalog(Dictionary<string, ModCandidate> mods)
    {
        _mods = new ReadOnlyDictionary<string, ModCandidate>(mods);
    }

    public IReadOnlyDictionary<string, ModCandidate> Mods => _mods;

    public static ModCatalog Create(IEnumerable<ModCandidate> candidates, IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        diagnostics ??= NullDiagnosticSink.Instance;
        var available = new Dictionary<string, ModCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (!available.TryAdd(candidate.Metadata.Id, candidate))
            {
                diagnostics.Report(Diagnostic(
                    ModDiagnosticCodes.DuplicateId,
                    DiagnosticSeverity.Error,
                    $"Mod ID '{candidate.Metadata.Id}' is already mapped; skipping '{candidate.Metadata.Path}'.",
                    candidate,
                    candidate.Metadata.Id));
            }
        }

        var invalid = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in available.Values.OrderBy(candidate => candidate.Metadata.Id, StringComparer.Ordinal))
        {
            if (invalid.Contains(candidate.Metadata.Id))
            {
                continue;
            }

            var chain = new HashSet<string>(StringComparer.Ordinal) { candidate.Metadata.Id };
            var current = candidate;
            while (current.Metadata.MasterId.Length != 0)
            {
                var masterId = current.Metadata.MasterId;
                if (invalid.Contains(masterId))
                {
                    break;
                }

                if (!available.TryGetValue(masterId, out var master))
                {
                    invalid.Add(candidate.Metadata.Id);
                    diagnostics.Report(Diagnostic(
                        ModDiagnosticCodes.MissingMaster,
                        DiagnosticSeverity.Warning,
                        $"Mod '{current.Metadata.Id}' is missing master mod '{masterId}'.",
                        current,
                        masterId));
                    break;
                }

                if (!chain.Add(masterId))
                {
                    invalid.UnionWith(chain);
                    diagnostics.Report(Diagnostic(
                        ModDiagnosticCodes.DependencyCycle,
                        DiagnosticSeverity.Warning,
                        $"Dependency loop detected while resolving mod '{candidate.Metadata.Id}' through '{masterId}'.",
                        current,
                        masterId));
                    break;
                }

                current = master;
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in available.Values)
            {
                if (!invalid.Contains(candidate.Metadata.Id) && invalid.Contains(candidate.Metadata.MasterId))
                {
                    invalid.Add(candidate.Metadata.Id);
                    diagnostics.Report(Diagnostic(
                        ModDiagnosticCodes.DependentRemoved,
                        DiagnosticSeverity.Information,
                        $"Mod '{candidate.Metadata.Id}' was removed because master '{candidate.Metadata.MasterId}' is unavailable.",
                        candidate,
                        candidate.Metadata.MasterId));
                    changed = true;
                }
            }
        }

        foreach (var id in invalid)
        {
            available.Remove(id);
        }

        return new ModCatalog(available);
    }

    public bool TryGet(string id, out ModCandidate? candidate) => _mods.TryGetValue(id, out candidate);

    private static DiagnosticEvent Diagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        ModCandidate candidate,
        string relatedId) => new(
            code,
            severity,
            message,
            context: new DiagnosticContext(
                LayerId: candidate.Layer.Provenance.LayerId,
                ModId: candidate.Metadata.Id,
                RelatedId: relatedId));
}
