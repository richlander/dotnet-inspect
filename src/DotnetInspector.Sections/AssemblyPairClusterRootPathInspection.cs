using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one selected cluster public-root-path composition and returns its
/// detached host-neutral envelope.
/// </summary>
public static class AssemblyPairClusterRootPathInspection
{
    public static InspectionEnvelope<AssemblyPairClusterRootPathResult>
        Execute(
            AssemblyContextGroup group,
            AssemblyPairDirectUseClusterProjection selection,
            AssemblyPairClusterRootPathLimits limits,
            CancellationToken cancellationToken = default)
    {
        AssemblyPairClusterRootPathResult result =
            AssemblyPairClusterRootPathQuery.Execute(
                group,
                selection,
                limits,
                cancellationToken);
        return new(
            new ResourcePath("assembly-pair/cluster-public-root-paths"),
            InspectionContentKind.Document,
            result,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported),
            Diagnostics(result));
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        AssemblyPairClusterRootPathResult result)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        if (!result.Selection.IsComplete)
        {
            diagnostics.Add(
                new(
                    "cluster-root-paths.pair-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    "Pairwise call-use evidence is incomplete; "
                        + "observed public-root paths remain positive "
                        + "evidence, but cluster-level absence is not "
                        + "complete."));
        }

        foreach (AssemblyPairClusterRootPathFailure failure
            in result.Failures)
        {
            diagnostics.Add(
                new(
                    failure.Kind switch
                    {
                        AssemblyPairClusterRootPathFailureKind
                            .ParticipantRejected =>
                            "cluster-root-paths.participant-rejected",
                        AssemblyPairClusterRootPathFailureKind
                            .InvalidImage =>
                            "cluster-root-paths.invalid-image",
                        _ => throw new InvalidOperationException(
                            "Unknown cluster root-path failure."),
                    },
                    InspectionDiagnosticSeverity.Error,
                    failure.Detail,
                    failure.Source.Identity.Name));
        }

        if (result.PublicRoots?.Boundary is { } rootBoundary)
        {
            diagnostics.Add(
                new(
                    "cluster-root-paths.metadata-limit",
                    InspectionDiagnosticSeverity.Warning,
                    $"Public MethodDef root inventory reached its "
                        + $"{Format(rootBoundary.Limit)} limit "
                        + $"of {rootBoundary.Maximum}; retained roots "
                        + "remain positive evidence."));
        }

        if (result.Paths is { } paths)
        {
            foreach (LibraryBodyRootPathBoundary boundary
                in paths.Boundaries)
            {
                diagnostics.Add(
                    new(
                        "cluster-root-paths.analysis-boundary",
                        InspectionDiagnosticSeverity.Warning,
                        Format(boundary)));
            }
        }

        return diagnostics.ToImmutable();
    }

    static string Format(
        PublicMethodRootInventoryLimit limit) =>
        limit switch
        {
            PublicMethodRootInventoryLimit.TypeDefinitions =>
                "TypeDef",
            PublicMethodRootInventoryLimit.MethodDefinitions =>
                "MethodDef",
            PublicMethodRootInventoryLimit.Roots =>
                "retained-root",
            _ => throw new InvalidOperationException(
                "Unknown public-root inventory limit."),
        };

    static string Format(
        LibraryBodyRootPathBoundary boundary) =>
        boundary switch
        {
            LibraryBodyRootPathBoundary.AnalysisIncomplete value =>
                $"Analysis reported {value.DiagnosticCount} body "
                    + "diagnostics; retained paths remain positive "
                    + "evidence.",
            LibraryBodyRootPathBoundary.PartialMethodEvidenceScope =>
                "Analysis used a partial MethodDef evidence scope; "
                    + "retained paths remain positive evidence.",
            LibraryBodyRootPathBoundary.UnresolvedLocalCalls value =>
                $"Analysis could not resolve {value.Count} local "
                    + "call operands; retained paths remain positive "
                    + "evidence.",
            LibraryBodyRootPathBoundary.UnattributedGeneratedBodies value =>
                $"Analysis could not attribute {value.Count} generated "
                    + "execution bodies; retained paths remain positive "
                    + "evidence.",
            LibraryBodyRootPathBoundary.DepthLimit value =>
                $"Analysis reached depth {value.MaximumDepth} while "
                    + $"searching destination 0x{value.Destination.Token:X8}.",
            LibraryBodyRootPathBoundary.NodeBudget value =>
                $"Analysis reached {value.MaximumNodes} search nodes "
                    + $"at destination 0x{value.Destination.Token:X8}.",
            LibraryBodyRootPathBoundary.EdgeBudget value =>
                $"Analysis reached {value.MaximumEdges} searched edges "
                    + $"at destination 0x{value.Destination.Token:X8}.",
            LibraryBodyRootPathBoundary.PathBudget value =>
                $"Analysis reached {value.MaximumPaths} retained paths "
                    + $"after observing root 0x{value.Root.Token:X8} "
                    + $"to destination "
                    + $"0x{value.Destination.Token:X8}.",
            _ => throw new InvalidOperationException(
                "Unknown root-path boundary."),
        };
}
