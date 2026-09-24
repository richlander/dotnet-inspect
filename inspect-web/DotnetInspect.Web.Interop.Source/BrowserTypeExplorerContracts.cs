using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Web.Interop.Source;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeExplorerBodyMode>))]
public enum BrowserTypeExplorerBodyMode
{
    Bodies,
    Skeleton,
    SelectedBody,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeExplorerPlacement>))]
public enum BrowserTypeExplorerPlacement
{
    All,
    Instance,
    Static,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeExplorerAccessibility>))]
public enum BrowserTypeExplorerAccessibility
{
    Unknown,
    Private,
    PrivateProtected,
    Protected,
    Internal,
    ProtectedInternal,
    Public,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeExplorerOutcomeKind>))]
public enum BrowserTypeExplorerOutcomeKind
{
    Available,
    Incomplete,
    Unavailable,
    Rejected,
}

public sealed record BrowserTypeExplorerMemberIdentity(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName)
{
    internal static BrowserTypeExplorerMemberIdentity From(
        MemberAnchor anchor) =>
        new(
            anchor.StableSelector,
            anchor.CanonicalSignature,
            anchor.Fingerprint,
            anchor.TypeFullName,
            anchor.MemberName);
}

public sealed record BrowserTypeExplorerRequest(
    BrowserTypeExplorerBodyMode BodyMode,
    int? SelectedDeclarationId,
    string? DocumentRevision,
    BrowserTypeExplorerPlacement Placement,
    BrowserTypeExplorerAccessibility[] Accessibilities,
    bool IncludeGenerated,
    bool IncludeDocumentation,
    bool IncludeAttributes)
{
    internal bool TryToProjectionRequest(
        CSharpTypeDocument document,
        out CSharpTypeProjectionRequest projectionRequest,
        out BrowserTypeExplorerProjectionFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(Accessibilities);
        MemberAnchor? selectedMember = null;
        if (SelectedDeclarationId is int declarationId)
        {
            if (!string.Equals(
                DocumentRevision,
                document.Revision.Sha256,
                StringComparison.OrdinalIgnoreCase))
            {
                projectionRequest = null!;
                failure = new(
                    "SelectedDocumentRevisionMismatch",
                    "The selected declaration belongs to a different Type document revision.");
                return false;
            }
            CSharpTypeDeclaration? declaration =
                document.Declarations.FirstOrDefault(
                    candidate => candidate.Id == declarationId);
            if (declaration is null)
            {
                projectionRequest = null!;
                failure = new(
                    "SelectedDeclarationNotFound",
                    "The selected declaration is not present in this Type document.");
                return false;
            }
            selectedMember = declaration.Anchor;
        }
        projectionRequest = new(
            BodyMode switch
            {
                BrowserTypeExplorerBodyMode.Bodies =>
                    CSharpTypeBodyMode.Bodies,
                BrowserTypeExplorerBodyMode.Skeleton =>
                    CSharpTypeBodyMode.Skeleton,
                BrowserTypeExplorerBodyMode.SelectedBody =>
                    CSharpTypeBodyMode.SelectedBody,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(BodyMode)),
            },
            selectedMember,
            Placement switch
            {
                BrowserTypeExplorerPlacement.All =>
                    CSharpTypePlacementFilter.All,
                BrowserTypeExplorerPlacement.Instance =>
                    CSharpTypePlacementFilter.Instance,
                BrowserTypeExplorerPlacement.Static =>
                    CSharpTypePlacementFilter.Static,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(Placement)),
            },
            Accessibilities.Select(MapAccessibility),
            IncludeGenerated,
            IncludeDocumentation,
            IncludeAttributes);
        failure = null;
        return true;
    }

    private static CSharpTypeAccessibility MapAccessibility(
        BrowserTypeExplorerAccessibility accessibility) =>
        accessibility switch
        {
            BrowserTypeExplorerAccessibility.Unknown =>
                CSharpTypeAccessibility.Unknown,
            BrowserTypeExplorerAccessibility.Private =>
                CSharpTypeAccessibility.Private,
            BrowserTypeExplorerAccessibility.PrivateProtected =>
                CSharpTypeAccessibility.PrivateProtected,
            BrowserTypeExplorerAccessibility.Protected =>
                CSharpTypeAccessibility.Protected,
            BrowserTypeExplorerAccessibility.Internal =>
                CSharpTypeAccessibility.Internal,
            BrowserTypeExplorerAccessibility.ProtectedInternal =>
                CSharpTypeAccessibility.ProtectedInternal,
            BrowserTypeExplorerAccessibility.Public =>
                CSharpTypeAccessibility.Public,
            _ => throw new ArgumentOutOfRangeException(
                nameof(accessibility)),
        };
}

public sealed record BrowserTypeExplorerRange(
    int Start,
    int Length);

public sealed record BrowserTypeExplorerRegion(
    string Role,
    BrowserTypeExplorerRange Range);

public sealed record BrowserTypeExplorerBody(
    int BodyId,
    BrowserTypeExplorerRange Range,
    bool HasDrillDownDestination);

public sealed record BrowserTypeExplorerContribution(
    int BodyId,
    string Role,
    BrowserTypeExplorerRange Range);

public sealed record BrowserTypeExplorerDeclaration(
    int DeclarationId,
    BrowserTypeExplorerMemberIdentity Identity,
    int DeclarationToken,
    string Kind,
    string Accessibility,
    string Placement,
    string Origin,
    bool SupportsSelectedBody,
    BrowserTypeExplorerRange Range,
    BrowserTypeExplorerRegion[] Regions,
    BrowserTypeExplorerBody[] Bodies,
    BrowserTypeExplorerContribution[] Contributions);

public sealed record BrowserTypeExplorerProjectionDiagnostic(
    string Kind,
    string Message,
    int? DeclarationId,
    int? BodyId,
    string? ContributionRole);

public sealed record BrowserTypeExplorerProjection(
    string Revision,
    string Text,
    BrowserTypeExplorerRegion[] FrameRegions,
    BrowserTypeExplorerContribution[] FrameContributions,
    BrowserTypeExplorerDeclaration[] Declarations,
    BrowserTypeExplorerProjectionDiagnostic[] Diagnostics);

public sealed record BrowserTypeExplorerProjectionFailure(
    string Kind,
    string Message);

public sealed record BrowserTypeExplorerDocument(
    string TypeNamespace,
    string[] TypeSegments,
    string AssemblyName,
    bool PdbSupplied,
    string SymbolSource,
    string RenderingPolicy,
    string DocumentationCapability,
    string ContractRelationshipCapability,
    BrowserTypeExplorerProjection? Projection,
    BrowserTypeExplorerProjectionFailure? ProjectionFailure);

public sealed record BrowserTypeExplorerInspection(
    BrowserTypeExplorerOutcomeKind Outcome,
    string? Reason,
    int BodyProjectionsAttempted,
    int[] FailedBodyIds,
    BrowserTypeExplorerDocument? Document,
    InspectionShare Share,
    InspectionDiagnostic[] Diagnostics);

public sealed record BrowserTypeExplorerResult(
    int Version,
    BrowserTypeSourceResultKind Kind,
    BrowserTypeExplorerInspection? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason);

internal static class BrowserTypeExplorerAdapter
{
    internal static BrowserTypeExplorerInspection From(
        InspectionEnvelope<CSharpTypeDocumentOutcome> inspection,
        BrowserTypeExplorerRequest request)
    {
        (
            BrowserTypeExplorerOutcomeKind outcome,
            string? reason,
            int[] failedBodyIds,
            CSharpTypeDocument? document
        ) = inspection.Content switch
        {
            CSharpTypeDocumentOutcome.Available available =>
                (BrowserTypeExplorerOutcomeKind.Available, (string?)null,
                    Array.Empty<int>(),
                    (CSharpTypeDocument?)available.Document),
            CSharpTypeDocumentOutcome.Incomplete incomplete =>
                (BrowserTypeExplorerOutcomeKind.Incomplete, (string?)null,
                    [.. incomplete.FailedBodyIds],
                    (CSharpTypeDocument?)incomplete.Document),
            CSharpTypeDocumentOutcome.Unavailable unavailable =>
                (BrowserTypeExplorerOutcomeKind.Unavailable,
                    unavailable.Reason, Array.Empty<int>(),
                    (CSharpTypeDocument?)null),
            CSharpTypeDocumentOutcome.Rejected rejected =>
                (BrowserTypeExplorerOutcomeKind.Rejected,
                    rejected.Reason, Array.Empty<int>(),
                    (CSharpTypeDocument?)null),
            _ => throw new InvalidOperationException(
                "Unknown C# Type document outcome."),
        };

        return new(
            outcome,
            reason,
            inspection.Content.BodyProjectionsAttempted,
            failedBodyIds,
            document is null ? null : AdaptDocument(document, request),
            inspection.Share,
            [.. inspection.Diagnostics]);
    }

    private static BrowserTypeExplorerDocument AdaptDocument(
        CSharpTypeDocument document,
        BrowserTypeExplorerRequest request)
    {
        if (!request.TryToProjectionRequest(
            document,
            out CSharpTypeProjectionRequest projectionRequest,
            out BrowserTypeExplorerProjectionFailure? requestFailure))
        {
            return new(
                document.TypeName.Namespace,
                [.. document.TypeName.Segments],
                document.Source.AssemblyName,
                document.Source.PdbSupplied,
                document.Source.Symbols.ToString(),
                document.Source.RenderingPolicy,
                document.Documentation.ToString(),
                document.ContractRelationships.ToString(),
                Projection: null,
                ProjectionFailure: requestFailure);
        }
        CSharpTypeProjectionOutcome outcome =
            CSharpTypeDocumentProjector.Project(document, projectionRequest);
        BrowserTypeExplorerProjection? projection = outcome switch
        {
            CSharpTypeProjectionOutcome.Projected projected =>
                AdaptProjection(projected.Projection),
            _ => null,
        };
        BrowserTypeExplorerProjectionFailure? failure = outcome switch
        {
            CSharpTypeProjectionOutcome.Rejected rejected =>
                new(rejected.Kind.ToString(), rejected.Message),
            _ => null,
        };
        return new(
            document.TypeName.Namespace,
            [.. document.TypeName.Segments],
            document.Source.AssemblyName,
            document.Source.PdbSupplied,
            document.Source.Symbols.ToString(),
            document.Source.RenderingPolicy,
            document.Documentation.ToString(),
            document.ContractRelationships.ToString(),
            projection,
            failure);
    }

    private static BrowserTypeExplorerProjection AdaptProjection(
        CSharpTypeDocumentProjection projection) =>
        new(
            projection.Revision.Sha256,
            projection.Text,
            [.. projection.FrameRegions.Select(AdaptRegion)],
            [.. projection.FrameContributions.Select(AdaptContribution)],
            [.. projection.Declarations.Select(AdaptDeclaration)],
            [.. projection.Diagnostics.Select(static diagnostic =>
                new BrowserTypeExplorerProjectionDiagnostic(
                    diagnostic.Kind.ToString(),
                    diagnostic.Message,
                    diagnostic.DeclarationId,
                    diagnostic.BodyId,
                    diagnostic.ContributionRole?.ToString()))]);

    private static BrowserTypeExplorerDeclaration AdaptDeclaration(
        CSharpTypeProjectedDeclaration declaration) =>
        new(
            declaration.DeclarationId,
            BrowserTypeExplorerMemberIdentity.From(declaration.Anchor),
            declaration.DeclarationToken,
            declaration.Kind.ToString(),
            declaration.Accessibility.ToString(),
            declaration.Placement.ToString(),
            declaration.Origin.ToString(),
            declaration.SupportsSelectedBody,
            AdaptRange(declaration.Range),
            [.. declaration.Regions.Select(AdaptRegion)],
            [.. declaration.Bodies.Select(static body =>
                new BrowserTypeExplorerBody(
                    body.BodyId,
                    AdaptRange(body.Range),
                    body.HasDrillDownDestination))],
            [.. declaration.Contributions.Select(AdaptContribution)]);

    private static BrowserTypeExplorerRegion AdaptRegion(
        CSharpTypeProjectedRegion region) =>
        new(region.Role.ToString(), AdaptRange(region.Range));

    private static BrowserTypeExplorerContribution AdaptContribution(
        CSharpTypeProjectedContribution contribution) =>
        new(
            contribution.BodyId,
            contribution.Role.ToString(),
            AdaptRange(contribution.Range));

    private static BrowserTypeExplorerRange AdaptRange(
        CSharpSourceRange range) =>
        new(range.Start, range.Length);
}
