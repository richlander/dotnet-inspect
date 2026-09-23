using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Returns one completed participant-scoped implementation-profile inspection.
/// </summary>
public static class ImplementationProfileInspectionOperation
{
    public static InspectionEnvelope<
        AssemblyContextEntry<AssemblyImplementationProfileInspection>> Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        AssemblyContextEntry<AssemblyImplementationProfileInspection> content =
            AssemblyContextImplementationProfilesQuery.ExecuteParticipant(
                group,
                participant);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "implementation-profiles/share",
                "Implementation profiles do not yet have a canonical "
                    + "Workspace Share projection."),
            Diagnostics(content));
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        AssemblyContextEntry<AssemblyImplementationProfileInspection> content)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        switch (content)
        {
            case AssemblyContextEntry<
                AssemblyImplementationProfileInspection>.Rejected rejected:
                diagnostics.Add(
                    new InspectionDiagnostic(
                        "implementation-profiles.participant-rejected",
                        InspectionDiagnosticSeverity.Warning,
                        rejected.Failure.Detail,
                        rejected.Subject.Identity.Name));
                break;
            case AssemblyContextEntry<
                AssemblyImplementationProfileInspection>.Failed failed:
                diagnostics.Add(
                    new InspectionDiagnostic(
                        "implementation-profiles.participant-failed",
                        InspectionDiagnosticSeverity.Error,
                        failed.Error.Message,
                        failed.Subject.Identity.Name));
                break;
            case AssemblyContextEntry<
                AssemblyImplementationProfileInspection>.Available available:
                foreach (ILInspector.Analysis.AnalysisDiagnostic diagnostic
                    in available.Value.Diagnostics)
                {
                    diagnostics.Add(
                        new InspectionDiagnostic(
                            "implementation-profiles.analysis-incomplete",
                            InspectionDiagnosticSeverity.Warning,
                            diagnostic.Message,
                            $"0x{diagnostic.MethodToken:X8}"));
                }
                foreach (ILInspector.Metadata.ApiSurfaceInspectionFailure failure
                    in available.Value.ApiSurfaceInspectionFailures)
                {
                    diagnostics.Add(
                        new InspectionDiagnostic(
                            "implementation-profiles.api-surface-incomplete",
                            InspectionDiagnosticSeverity.Warning,
                            $"{failure.Operation}: {failure.Detail}",
                            $"0x{failure.SubjectToken:X8}"));
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown implementation-profile content "
                        + $"'{content.GetType().Name}'.");
        }
        return diagnostics.DrainToImmutable();
    }
}
