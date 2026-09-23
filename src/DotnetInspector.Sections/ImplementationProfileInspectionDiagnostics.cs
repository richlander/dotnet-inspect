using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

internal static class ImplementationProfileInspectionDiagnostics
{
    public static ImmutableArray<InspectionDiagnostic> Create<TContent>(
        AssemblyContextEntry<TContent> content,
        Func<TContent, ImmutableArray<AnalysisDiagnostic>>
            analysisDiagnostics,
        Func<TContent, ImmutableArray<ApiSurfaceInspectionFailure>>
            apiSurfaceFailures)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        switch (content)
        {
            case AssemblyContextEntry<TContent>.Rejected rejected:
                diagnostics.Add(
                    new InspectionDiagnostic(
                        "implementation-profiles.participant-rejected",
                        InspectionDiagnosticSeverity.Warning,
                        rejected.Failure.Detail,
                        rejected.Subject.Identity.Name));
                break;
            case AssemblyContextEntry<TContent>.Failed failed:
                diagnostics.Add(
                    new InspectionDiagnostic(
                        "implementation-profiles.participant-failed",
                        InspectionDiagnosticSeverity.Error,
                        failed.Error.Message,
                        failed.Subject.Identity.Name));
                break;
            case AssemblyContextEntry<TContent>.Available available:
                foreach (AnalysisDiagnostic diagnostic
                    in analysisDiagnostics(available.Value))
                {
                    diagnostics.Add(
                        new InspectionDiagnostic(
                            "implementation-profiles.analysis-incomplete",
                            InspectionDiagnosticSeverity.Warning,
                            diagnostic.Message,
                            $"0x{diagnostic.MethodToken:X8}"));
                }
                foreach (ApiSurfaceInspectionFailure failure
                    in apiSurfaceFailures(available.Value))
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
