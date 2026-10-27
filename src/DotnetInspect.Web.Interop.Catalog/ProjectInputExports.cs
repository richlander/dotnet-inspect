using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Catalog;

public sealed record BrowserProjectWorkspacePreparationResult(
    BrowserProjectInputInspection? Inspection,
    BrowserRetainedWorkspacePreparationResult Preparation);

public sealed record BrowserProjectInputInspection(
    BrowserProjectInputContent Content,
    BrowserProjectInputShare Share,
    BrowserProjectInputDiagnostic[] Diagnostics);

public sealed record BrowserProjectInputShare(
    string Kind, string? FullUrl, string? Packet, string? Path, string? Reason);

public sealed record BrowserProjectInputDiagnostic(
    string Code, string Severity, string Summary, string? Correspondence);

public sealed record BrowserProjectInputContent(
    string Kind,
    string DeclaredName,
    string ProviderOutcome,
    string? TraversalCompletion,
    string RootSetCompletion,
    bool IsTruncated,
    BrowserProjectInputField[] Metadata,
    BrowserProjectInputRoot[] Roots,
    BrowserProjectInputDeclaration[] Declarations,
    BrowserProjectInputPackage[] Packages,
    BrowserProjectInputEdge[] Edges,
    BrowserProjectInputFailure[] Failures);

public sealed record BrowserProjectInputField(string Name, string Value);

public sealed record BrowserProjectInputRoot(
    int RootIndex,
    string Owner,
    string SourceKind,
    string DeclarationState,
    string? DeclarationCompletion,
    string SelectionStatus,
    string? RequestedFramework,
    string? SelectedFramework,
    string GraphState,
    string? GraphCompletion,
    string? RestoredTargetFramework,
    string? RestoredRuntimeIdentifier,
    string? TargetProvenance);

public sealed record BrowserProjectInputDeclaration(
    string? PackageId,
    string? VersionConstraint,
    string SourcePackageId,
    string? SourceVersionConstraint,
    string Condition,
    int SourceOccurrences,
    int? GroupIndex);

public sealed record BrowserProjectInputPackage(
    string PackageId, string Version, string Role);

public sealed record BrowserProjectInputEdge(
    string ParentKind,
    string? ParentPackageId,
    string? ParentPackageVersion,
    string? ParentProjectIdentity,
    string PackageId,
    string Version,
    string VersionConstraint,
    string SourceVersionConstraint,
    string Role);

public sealed record BrowserProjectInputFailure(
    string Phase, string Reason, string Message, int Occurrences);

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    [JSExport]
    public static async Task<string> SelectProjectInspector(
        string retainedDefinitionId, string realizationId, string actionId)
    {
        BrowserRetainedWorkspacePosting posting = await
            BrowserRetainedWorkspaceActivationService.SelectProjectInspectorAsync(
                retainedDefinitionId, realizationId, actionId).ConfigureAwait(false);
        return JsonSerializer.Serialize(
            posting, BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePosting);
    }

    [JSExport]
    public static async Task<string> PrepareProjectWorkspaceDefinition(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string kind,
        string declaredName,
        byte[] content,
        string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(content);
        InspectionEnvelope<ProjectInputDocument>? inspection = null;
        BrowserRetainedWorkspacePreparationResult preparation;
        try
        {
            ProjectInputKind inputKind = kind switch
            {
                "authoredProject" => ProjectInputKind.AuthoredProject,
                "restoredProject" => ProjectInputKind.RestoredProject,
                _ => throw new ArgumentException(
                    "Supported Project kinds are authoredProject and restoredProject.",
                    nameof(kind)),
            };
            inspection = ProjectInputInspection.Execute(
                inputKind,
                declaredName,
                content,
                string.IsNullOrEmpty(targetFramework)
                    ? null
                    : new RestoredProjectTargetRequest(targetFramework));
            preparation = await BrowserRetainedWorkspaceActivationService
                .PrepareProjectAsync(
                    retainedDefinitionId,
                    inspection.Content.DeclaredName.ToString(),
                    canonicalLocation,
                    inspection).ConfigureAwait(false);
        }
        catch (ArgumentException failure)
        {
            preparation = BrowserRetainedWorkspaceActivationService
                .FailedPreparation("InvalidProjectInput", failure.Message);
        }

        return JsonSerializer.Serialize(
            new BrowserProjectWorkspacePreparationResult(
                inspection is null
                    ? null
                    : BrowserProjectInputProjection.Project(inspection),
                preparation),
            BrowserCatalogJsonContext.Default.BrowserProjectWorkspacePreparationResult);
    }
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserRetainedWorkspaceActivationService
{
    internal static async Task<BrowserRetainedWorkspacePosting> SelectProjectInspectorAsync(
        string retainedDefinitionId, string realizationId, string actionId) =>
        Posting(await Owner.SelectProjectInspectorAsync(
            retainedDefinitionId, realizationId, actionId).ConfigureAwait(false));

    internal static Task<BrowserRetainedWorkspacePreparationResult> PrepareProjectAsync(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        InspectionEnvelope<ProjectInputDocument> inspection) =>
        PrepareAsync(new BrowserRetainedWorkspaceActivationRequest(
            retainedDefinitionId,
            label,
            canonicalLocation,
            new BrowserRetainedWorkspaceInput.Project(
                inspection,
                BrowserPackageWorkspace.ProductWorkspacePlan.TraversalTargetPolicy)));
}

internal static class BrowserProjectInputProjection
{
    internal static BrowserProjectInputInspection Project(
        InspectionEnvelope<ProjectInputDocument> inspection)
    {
        ProjectInputDocument document = inspection.Content;
        DependencyEvidenceProjection evidence = document.Evidence;
        AuthoredProjectDependencyFacts? authored = document.AuthoredProject switch
        {
            AuthoredProjectDependencyFactsResult.Available available => available.Value,
            AuthoredProjectDependencyFactsResult.Incomplete incomplete => incomplete.Value,
            _ => null,
        };
        var metadata = new List<BrowserProjectInputField>();
        if (authored is not null)
        {
            metadata.Add(new("Facts identity", authored.Identity.FactsDigest));
            metadata.Add(new("Content SHA-256", authored.ContentProvenance.Sha256));
            metadata.AddRange(authored.TargetFrameworks.Select(target =>
                new BrowserProjectInputField(
                    $"Target framework ({target.Identity.Kind})",
                    target.SourceSpelling.ToString())));
            metadata.AddRange(authored.UnresolvedDependencySyntax.Select(syntax =>
                new BrowserProjectInputField(
                    $"Unresolved syntax ({syntax.Kind})", syntax.OpaqueIdentity)));
        }
        if (document.AuthoredProject is AuthoredProjectDependencyFactsResult.Incomplete limitations)
        {
            metadata.AddRange(limitations.Limitations.Select(limitation =>
                new BrowserProjectInputField(
                    limitation.Reason.ToString(), limitation.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))));
        }
        if (document.AuthoredProject is AuthoredProjectDependencyFactsResult.Failed authoredFailure)
            metadata.Add(new(authoredFailure.Failure.Reason.ToString(), authoredFailure.Failure.Message));

        RestoredProjectDependencyFacts? restored = document.RestoredTraversal switch
        {
            RestoredProjectDependencyTraversalResult.Available available => available.Value.Facts,
            RestoredProjectDependencyTraversalResult.Unavailable unavailable => unavailable.Facts,
            RestoredProjectDependencyTraversalResult.Failed
                { Failure: RestoredProjectDependencyTraversalFailure.Graph graph } => graph.Facts,
            _ => null,
        };
        if (restored is not null)
        {
            metadata.Add(new("Content SHA-256", restored.ContentProvenance.Sha256));
            metadata.Add(new("Selection identity", restored.SelectionIdentity.FactsDigest));
            metadata.Add(new("Selected target identity", restored.SelectionIdentity.TargetIdentity));
            metadata.Add(new("Package pruning", restored.PackagePruning switch
            {
                RestoredProjectPackagePruningResult.Available => "Available",
                RestoredProjectPackagePruningResult.Unavailable => "Unavailable",
                RestoredProjectPackagePruningResult.Failed failed =>
                    $"Failed: {failed.Failure.Reason}: {failed.Failure.Message}",
                _ => throw new InvalidOperationException("Unknown Project pruning outcome."),
            }));
        }

        BrowserProjectInputDeclaration[] declarations = authored is not null
            ? [.. authored.PackageDeclarations.Select(declaration =>
                new BrowserProjectInputDeclaration(
                    declaration.CanonicalPackageId,
                    declaration.CanonicalVersionConstraint,
                    declaration.SourcePackageIdSpelling?.ToString() ?? "",
                    declaration.SourceVersionConstraintSpelling?.ToString(),
                    Condition(declaration.Condition),
                    declaration.SourceOccurrenceCount,
                    null))]
            : [.. evidence.Dependencies.Select(declaration =>
                new BrowserProjectInputDeclaration(
                    declaration.PackageId,
                    declaration.VersionConstraint,
                    declaration.SourcePackageIdSpelling.ToString(),
                    declaration.SourceVersionConstraintSpelling.ToString(),
                    declaration.FrameworkSpelling.ToString(),
                    declaration.SourceOccurrences,
                    declaration.GroupIndex))];
        string providerOutcome = document.Kind switch
        {
            ProjectInputKind.AuthoredProject => document.AuthoredProject switch
            {
                AuthoredProjectDependencyFactsResult.Available => "Available",
                AuthoredProjectDependencyFactsResult.Incomplete => "Incomplete",
                AuthoredProjectDependencyFactsResult.Failed => "Failed",
                _ => throw new InvalidOperationException("Missing authored Project outcome."),
            },
            ProjectInputKind.RestoredProject => document.RestoredTraversal switch
            {
                RestoredProjectDependencyTraversalResult.Available => "Available",
                RestoredProjectDependencyTraversalResult.Unavailable => "Unavailable",
                RestoredProjectDependencyTraversalResult.Failed => "Failed",
                _ => throw new InvalidOperationException("Missing restored Project outcome."),
            },
            _ => throw new InvalidOperationException("Unknown Project input kind."),
        };
        return new(
            new(
                document.Kind.ToString(),
                document.DeclaredName.ToString(),
                providerOutcome,
                (document.RestoredTraversal as RestoredProjectDependencyTraversalResult.Available)
                    ?.Value.Completion.ToString(),
                evidence.Summary.RootSetCompletion.ToString(),
                evidence.Summary.IsTruncated,
                [.. metadata],
                [.. evidence.Roots.Select(root => new BrowserProjectInputRoot(
                    root.RootIndex, root.Owner.ToString(), root.SourceKind.ToString(),
                    root.DeclarationState.ToString(), root.DeclarationCompletion?.ToString(),
                    root.SelectionStatus.ToString(), root.RequestedFramework?.ToString(),
                    root.SelectedFramework?.ToString(), root.GraphState.ToString(),
                    root.GraphCompletion?.ToString(), root.RestoredTargetFrameworkIdentity,
                    root.RestoredRuntimeIdentifier, root.RestoredTargetProvenance?.ToString()))],
                declarations,
                [.. evidence.RestoredPackages.Select(package =>
                    new BrowserProjectInputPackage(package.PackageId, package.PackageVersion, package.Role.ToString()))],
                [.. evidence.RestoredEdges.Select(edge => new BrowserProjectInputEdge(
                    edge.ParentKind.ToString(), edge.ParentPackageId, edge.ParentPackageVersion,
                    edge.ParentProjectIdentity, edge.PackageId, edge.PackageVersion,
                    edge.VersionConstraint, edge.SourceVersionConstraintSpelling.ToString(), edge.Role.ToString()))],
                [.. evidence.Failures.Select(failure =>
                    new BrowserProjectInputFailure(
                        failure.Phase.ToString(), failure.Reason, failure.Message.ToString(), failure.Occurrences))]),
            inspection.Share switch
            {
                InspectionShare.Available available =>
                    new("available", available.FullUrl, available.Packet, null, null),
                InspectionShare.NonProjectable local =>
                    new("nonProjectable", null, null, local.Path, local.Reason.ToString()),
                _ => throw new InvalidOperationException("Unknown Project Share outcome."),
            },
            [.. inspection.Diagnostics.Select(diagnostic => new BrowserProjectInputDiagnostic(
                diagnostic.Code, diagnostic.Severity.ToString(),
                diagnostic.Summary.ToString(), diagnostic.Correspondence?.ToString()))]);
    }

    static string Condition(AuthoredProjectDependencyCondition condition) =>
        condition switch
        {
            AuthoredProjectDependencyCondition.Unconditional => "Unconditional",
            AuthoredProjectDependencyCondition.TargetFramework target =>
                target.SourceSpelling.ToString(),
            AuthoredProjectDependencyCondition.Unresolved unresolved =>
                $"Unresolved: {string.Join("; ", unresolved.SourceSpellings.Select(spelling => spelling.ToString()))}",
            _ => throw new InvalidOperationException("Unknown authored Project condition."),
        };
}
