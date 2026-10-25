using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

public enum ProjectInputKind
{
    AuthoredProject,
    RestoredProject,
}

public sealed record ProjectInputDocument(
    ProjectInputKind Kind,
    InertString DeclaredName,
    DependencyEvidenceProjection Evidence,
    AuthoredProjectDependencyFactsResult? AuthoredProject,
    RestoredProjectDependencyTraversalResult? RestoredTraversal);

/// <summary>
/// Composes already-acquired project bytes into dependency evidence without
/// evaluating, restoring, or acquiring dependency content.
/// </summary>
public static class ProjectInputInspection
{
    public static InspectionEnvelope<ProjectInputDocument> Execute(
        ProjectInputKind kind,
        string declaredName,
        ReadOnlyMemory<byte> content,
        RestoredProjectTargetRequest? target = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredName);
        var name = new InertString(TextPolicy.Field, declaredName, 1024);
        if (name.IsTruncated)
            throw new ArgumentException(
                "The declared project name exceeds the display limit.",
                nameof(declaredName));
        if (kind == ProjectInputKind.AuthoredProject && target is not null)
            throw new ArgumentException(
                "Authored project syntax does not select a restored target.",
                nameof(target));

        AuthoredProjectDependencyFactsResult? authored = null;
        RestoredProjectDependencyTraversalResult? traversal = null;
        ImmutableArray<PackageDependencyEvidenceInput> inputs;
        ImmutableArray<PackageDependencyEvidenceRootFailure> failures = [];
        switch (kind)
        {
            case ProjectInputKind.AuthoredProject:
                authored = AuthoredProjectDependencyFactsQuery.Execute(content);
                inputs =
                [
                    PackageDependencyEvidenceQuery.CreateAuthoredProjectInput(
                        authored,
                        sourceLabel: name),
                ];
                break;
            case ProjectInputKind.RestoredProject:
                traversal = RestoredProjectDependencyTraversalQuery.Execute(
                    content,
                    new RestoredProjectDependencyTraversalRequest(target));
                if (traversal is RestoredProjectDependencyTraversalResult.Failed
                    {
                        Failure: RestoredProjectDependencyTraversalFailure.Document document,
                    })
                {
                    inputs = [];
                    failures =
                    [
                        new PackageDependencyEvidenceRootFailure.RestoredProject(
                            PackageDependencyEvidenceAcquisitionForm.ProjectAssets,
                            document.Failure,
                            name),
                    ];
                    break;
                }

                RestoredProjectDependencyFacts facts = traversal switch
                {
                    RestoredProjectDependencyTraversalResult.Available available =>
                        available.Value.Facts,
                    RestoredProjectDependencyTraversalResult.Unavailable unavailable =>
                        unavailable.Facts,
                    RestoredProjectDependencyTraversalResult.Failed
                    {
                        Failure: RestoredProjectDependencyTraversalFailure.Graph graph,
                    } => graph.Facts,
                    _ => throw new InvalidOperationException(
                        "Unknown restored project traversal outcome."),
                };
                inputs =
                [
                    PackageDependencyEvidenceQuery.CreateRestoredProjectInput(
                        facts,
                        PackageDependencyEvidenceAcquisitionForm.ProjectAssets,
                        name),
                ];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        PackageDependencyEvidenceOutcome evidence =
            PackageDependencyEvidenceQuery.Execute(new(inputs, failures));
        var occurrence = new DependencyRootOccurrenceIdentity(1);
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(new(
                evidence,
                [.. evidence.Roots.Select(_ => occurrence)],
                [.. evidence.FailedRoots.Select(_ =>
                    (DependencyRootOccurrenceIdentity?)occurrence)]));
        return new(
            new(kind, name, projection, authored, traversal),
            new InspectionShare.NonProjectable(
                "project/input",
                "Local project evidence is not included in a Share packet."));
    }
}
