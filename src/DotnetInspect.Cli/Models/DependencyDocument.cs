using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using InertText;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Models;

internal enum DependencyCompletion
{
    Complete,
    DepthBounded,
    SourceBounded,
    Partial,
    Failed,
    NotRequested,
}

internal sealed record DependencyRootOccurrence(
    int Index,
    DependencyRootKind Kind,
    InertString Locator,
    int? NodeId,
    int? EvidenceRootIndex,
    DependencyCompletion Admission,
    DependencyCompletion Traversal);

internal sealed record DependencyTraversalFailure(
    ImmutableArray<int> Roots,
    string Phase,
    string Reason,
    InertString Message)
{
    internal PackageDependencyTraversalFailure? PackageManifest { get; init; }
    internal PackageDependencyTraversalFailedResolutionNode? PackageResolution { get; init; }
    internal PackageDependencyTraversalWorkBudgetNode? PackageBudget { get; init; }
    internal RestoredProjectGraphFailure? RestoredGraph { get; init; }
    internal CandidateOpenFailure? MetadataFailure { get; init; }
    internal DependencyGraphNodeIdentity? SourceIdentity { get; init; }
    internal DependencyGraphNodeIdentity? TargetIdentity { get; init; }
    internal DependencyGraphEvidenceIdentity? EvidenceIdentity { get; init; }
}

/// <summary>One completed CLI plan. Renderers never acquire or traverse from this snapshot.</summary>
internal sealed record DependencyDocument(
    ImmutableArray<DependencyRootOccurrence> Roots,
    DependencyGraphDocument Graph,
    PackageDependencyEvidenceOutcome EvidenceOutcome,
    DependencyEvidenceProjection Evidence,
    ImmutableArray<DependencyTraversalFailure> TraversalFailures,
    PackageDependencyTraversalOutcome? PackageTraversal,
    ImmutableDictionary<int, RestoredProjectDependencyTraversalResult> RestoredTraversals,
    DependencyCompletion Traversal,
    int? RequestedDepth)
{
    internal ImmutableArray<int> PackageRootOccurrences { get; init; } = [];

    internal bool IsSuccessful =>
        Traversal is not (DependencyCompletion.Partial or DependencyCompletion.Failed)
        && Roots.All(root => root.Admission == DependencyCompletion.Complete)
        && TraversalFailures.IsEmpty
        && Evidence.Failures.IsEmpty
        && Commands.DependencyEvidenceCommand.ExitCode(Evidence) == 0;
}
