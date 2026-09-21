using System.Collections.Immutable;
using System.Text;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

public enum CSharpTypeBodyMode
{
    Bodies,
    Skeleton,
    SelectedBody,
}

public enum CSharpTypePlacementFilter
{
    All,
    Instance,
    Static,
}

public enum CSharpTypeProjectionFailureKind
{
    SelectedMemberRequired,
    SelectedMemberNotFound,
    SelectedMemberHidden,
    SelectedMemberHasNoImplementationDifference,
}

public enum CSharpTypeProjectionDiagnosticKind
{
    HiddenSelectedBodyContribution,
    BodyUnavailable,
}

public sealed record CSharpTypeProjectionDiagnostic(
    CSharpTypeProjectionDiagnosticKind Kind,
    string Message,
    int? DeclarationId = null,
    int? BodyId = null,
    CSharpTypeBodyContributionRole? ContributionRole = null);

public sealed class CSharpTypeProjectionRequest
{
    public CSharpTypeProjectionRequest(
        CSharpTypeBodyMode bodyMode = CSharpTypeBodyMode.Bodies,
        MemberAnchor? selectedMember = null,
        CSharpTypePlacementFilter placement = CSharpTypePlacementFilter.All,
        IEnumerable<CSharpTypeAccessibility>? accessibilities = null,
        bool includeGenerated = true,
        bool includeDocumentation = true,
        bool includeAttributes = true)
    {
        if (!Enum.IsDefined(bodyMode))
            throw new ArgumentOutOfRangeException(nameof(bodyMode));
        if (!Enum.IsDefined(placement))
            throw new ArgumentOutOfRangeException(nameof(placement));

        var accessibilitySet = accessibilities is null
            ? Enum.GetValues<CSharpTypeAccessibility>().ToImmutableHashSet()
            : accessibilities.ToImmutableHashSet();
        if (accessibilitySet.Any(static accessibility => !Enum.IsDefined(accessibility)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibilities),
                "Accessibility filters must contain only defined values.");
        }

        BodyMode = bodyMode;
        SelectedMember = selectedMember is null ? null : selectedMember with { };
        Placement = placement;
        Accessibilities = accessibilitySet;
        IncludeGenerated = includeGenerated;
        IncludeDocumentation = includeDocumentation;
        IncludeAttributes = includeAttributes;
    }

    public CSharpTypeBodyMode BodyMode { get; }

    public MemberAnchor? SelectedMember { get; }

    public CSharpTypePlacementFilter Placement { get; }

    public ImmutableHashSet<CSharpTypeAccessibility> Accessibilities { get; }

    public bool IncludeGenerated { get; }

    public bool IncludeDocumentation { get; }

    public bool IncludeAttributes { get; }
}

public sealed record CSharpTypeProjectedRegion(
    CSharpTypeRegionRole Role,
    CSharpSourceRange Range);

public sealed record CSharpTypeProjectedBody(
    int BodyId,
    CSharpSourceRange Range,
    bool HasDrillDownDestination);

public sealed record CSharpTypeProjectedContribution(
    int BodyId,
    CSharpTypeBodyContributionRole Role,
    CSharpSourceRange Range);

public sealed record CSharpTypeProjectedDeclaration(
    int DeclarationId,
    MemberAnchor Anchor,
    int DeclarationToken,
    CSharpTypeDeclarationKind Kind,
    CSharpTypeAccessibility Accessibility,
    CSharpTypeDeclarationPlacement Placement,
    CSharpTypeOrigin Origin,
    CSharpSourceRange Range,
    ImmutableArray<CSharpTypeProjectedRegion> Regions,
    ImmutableArray<CSharpTypeProjectedBody> Bodies,
    ImmutableArray<CSharpTypeProjectedContribution> Contributions);

public sealed record CSharpTypeDocumentProjection(
    CSharpDocumentRevision Revision,
    string Text,
    ImmutableArray<CSharpTypeProjectedRegion> FrameRegions,
    ImmutableArray<CSharpTypeProjectedContribution> FrameContributions,
    ImmutableArray<CSharpTypeProjectedDeclaration> Declarations,
    ImmutableArray<CSharpTypeProjectionDiagnostic> Diagnostics);

public abstract record CSharpTypeProjectionOutcome
{
    private CSharpTypeProjectionOutcome()
    {
    }

    public sealed record Projected(CSharpTypeDocumentProjection Projection)
        : CSharpTypeProjectionOutcome;

    public sealed record Rejected(
        CSharpTypeProjectionFailureKind Kind,
        string Message)
        : CSharpTypeProjectionOutcome;
}

public static class CSharpTypeDocumentProjector
{
    public static CSharpTypeProjectionOutcome Project(
        CSharpTypeDocument document,
        CSharpTypeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        CSharpTypeDeclaration? selected = null;
        if (request.BodyMode == CSharpTypeBodyMode.SelectedBody)
        {
            if (request.SelectedMember is null)
            {
                return Reject(
                    CSharpTypeProjectionFailureKind.SelectedMemberRequired,
                    "Selected body requires an exact member identity.");
            }

            selected = document.Declarations.FirstOrDefault(
                declaration => declaration.Anchor == request.SelectedMember);
            if (selected is null)
            {
                return Reject(
                    CSharpTypeProjectionFailureKind.SelectedMemberNotFound,
                    "The selected member does not belong to this document.");
            }
            if (!IsVisible(selected, request))
            {
                return Reject(
                    CSharpTypeProjectionFailureKind.SelectedMemberHidden,
                    "The selected member is excluded by the structural filters.");
            }
        }

        ImmutableHashSet<int> selectedOwnedBodies = selected is null
            ? []
            : selected.Parts
                .SelectMany(static part => part.OwnedBodies)
                .Select(static body => body.BodyId)
                .ToImmutableHashSet();

        if (selected is not null
            && !HasSelectedImplementationDifference(
                document,
                selected,
                selectedOwnedBodies,
                request))
        {
            return Reject(
                CSharpTypeProjectionFailureKind.SelectedMemberHasNoImplementationDifference,
                "The selected member has no observable implementation difference.");
        }

        var text = new StringBuilder();
        var frameRegions = ImmutableArray.CreateBuilder<CSharpTypeProjectedRegion>();
        var frameContributions =
            ImmutableArray.CreateBuilder<CSharpTypeProjectedContribution>();
        var projectedDeclarations =
            ImmutableArray.CreateBuilder<CSharpTypeProjectedDeclaration>();
        var diagnostics =
            ImmutableArray.CreateBuilder<CSharpTypeProjectionDiagnostic>();
        var diagnosedBodies = new HashSet<int>();

        AppendFrameParts(
            document,
            request,
            selectedOwnedBodies,
            text,
            frameRegions,
            frameContributions,
            diagnostics,
            diagnosedBodies);

        foreach (CSharpTypeDeclaration declaration in document.Declarations)
        {
            if (!IsVisible(declaration, request))
            {
                if (selected is not null)
                {
                    foreach (CSharpTypeBodyContribution contribution
                        in declaration.Parts
                            .SelectMany(static part => part.Contributions)
                            .Where(contribution =>
                                selectedOwnedBodies.Contains(contribution.BodyId)))
                    {
                        diagnostics.Add(new(
                            CSharpTypeProjectionDiagnosticKind.HiddenSelectedBodyContribution,
                            $"Declaration {declaration.Id} contains a {contribution.Role} contribution from selected body {contribution.BodyId} hidden by structural filters.",
                            declaration.Id,
                            contribution.BodyId,
                            contribution.Role));
                    }
                }
                continue;
            }

            AppendText(text, document.Frame.DeclarationSeparator);
            int declarationStart = text.Length;
            var regions = ImmutableArray.CreateBuilder<CSharpTypeProjectedRegion>();
            var bodies = ImmutableArray.CreateBuilder<CSharpTypeProjectedBody>();
            var contributions =
                ImmutableArray.CreateBuilder<CSharpTypeProjectedContribution>();

            foreach (CSharpTypeRenderPart part in declaration.Parts)
            {
                AppendDeclarationPart(
                    document,
                    declaration,
                    part,
                    request,
                    selected,
                    selectedOwnedBodies,
                    text,
                    regions,
                    bodies,
                    contributions,
                    diagnostics,
                    diagnosedBodies);
            }

            int declarationLength = text.Length - declarationStart;
            projectedDeclarations.Add(new(
                declaration.Id,
                declaration.Anchor,
                declaration.DeclarationToken,
                declaration.Kind,
                declaration.Accessibility,
                declaration.Placement,
                declaration.Origin,
                new CSharpSourceRange(declarationStart, declarationLength),
                regions.ToImmutable(),
                bodies.ToImmutable(),
                contributions.ToImmutable()));
        }

        AppendText(text, document.Frame.Suffix);
        return new CSharpTypeProjectionOutcome.Projected(
            new CSharpTypeDocumentProjection(
                document.Revision,
                text.ToString(),
                frameRegions.ToImmutable(),
                frameContributions.ToImmutable(),
                projectedDeclarations.ToImmutable(),
                diagnostics.ToImmutable()));
    }

    static void AppendFrameParts(
        CSharpTypeDocument document,
        CSharpTypeProjectionRequest request,
        ImmutableHashSet<int> selectedOwnedBodies,
        StringBuilder text,
        ImmutableArray<CSharpTypeProjectedRegion>.Builder regions,
        ImmutableArray<CSharpTypeProjectedContribution>.Builder contributions,
        ImmutableArray<CSharpTypeProjectionDiagnostic>.Builder diagnostics,
        HashSet<int> diagnosedBodies)
    {
        foreach (CSharpTypeRenderPart part in document.Frame.PrefixParts)
        {
            if (!IncludePart(part, request))
                continue;

            bool useFull = UseFullAlternative(
                document,
                part,
                request.BodyMode,
                selectedDeclaration: false,
                selectedOwnedBodies);
            string value = useFull ? part.FullText : part.SkeletonText;
            int start = text.Length;
            AppendText(text, value);
            if (value.Length > 0)
            {
                regions.Add(new(
                    part.Region,
                    new CSharpSourceRange(start, value.Length)));
            }
            if (useFull)
            {
                foreach (CSharpTypeBodyContribution contribution
                    in part.Contributions)
                {
                    contributions.Add(new(
                        contribution.BodyId,
                        contribution.Role,
                        Rebase(contribution.FullRange, start)));
                }
            }
            AddUnavailableBodyDiagnostics(
                document,
                part,
                declarationId: null,
                diagnostics,
                diagnosedBodies);
        }
    }

    static void AppendDeclarationPart(
        CSharpTypeDocument document,
        CSharpTypeDeclaration declaration,
        CSharpTypeRenderPart part,
        CSharpTypeProjectionRequest request,
        CSharpTypeDeclaration? selected,
        ImmutableHashSet<int> selectedOwnedBodies,
        StringBuilder text,
        ImmutableArray<CSharpTypeProjectedRegion>.Builder regions,
        ImmutableArray<CSharpTypeProjectedBody>.Builder bodies,
        ImmutableArray<CSharpTypeProjectedContribution>.Builder contributions,
        ImmutableArray<CSharpTypeProjectionDiagnostic>.Builder diagnostics,
        HashSet<int> diagnosedBodies)
    {
        if (!IncludePart(part, request))
            return;

        bool useFull = UseFullAlternative(
            document,
            part,
            request.BodyMode,
            selected?.Id == declaration.Id,
            selectedOwnedBodies);
        string value = useFull ? part.FullText : part.SkeletonText;
        int partStart = text.Length;
        AppendText(text, value);
        if (value.Length > 0)
        {
            regions.Add(new(
                part.Region,
                new CSharpSourceRange(partStart, value.Length)));
        }
        if (useFull)
        {
            foreach (CSharpTypeOwnedBodyReference body in part.OwnedBodies)
            {
                bodies.Add(new(
                    body.BodyId,
                    Rebase(body.FullRange, partStart),
                    body.HasDrillDownDestination));
            }
            foreach (CSharpTypeBodyContribution contribution in part.Contributions)
            {
                contributions.Add(new(
                    contribution.BodyId,
                    contribution.Role,
                    Rebase(contribution.FullRange, partStart)));
            }
        }
        AddUnavailableBodyDiagnostics(
            document,
            part,
            declaration.Id,
            diagnostics,
            diagnosedBodies);
    }

    static bool IncludePart(
        CSharpTypeRenderPart part,
        CSharpTypeProjectionRequest request)
        => part.Kind switch
        {
            CSharpTypeRenderPartKind.Documentation =>
                request.IncludeDocumentation,
            CSharpTypeRenderPartKind.Attributes =>
                request.IncludeAttributes,
            _ => true,
        };

    static bool UseFullAlternative(
        CSharpTypeDocument document,
        CSharpTypeRenderPart part,
        CSharpTypeBodyMode mode,
        bool selectedDeclaration,
        ImmutableHashSet<int> selectedOwnedBodies)
    {
        if (part.Kind != CSharpTypeRenderPartKind.Implementation)
            return true;

        bool selected = mode switch
        {
            CSharpTypeBodyMode.Bodies => true,
            CSharpTypeBodyMode.Skeleton => false,
            CSharpTypeBodyMode.SelectedBody =>
                selectedDeclaration
                || UsesSelectedBodyContribution(part, selectedOwnedBodies),
            _ => throw new InvalidOperationException(),
        };
        return selected && ReferencedBodiesAreAvailable(document, part);
    }

    static bool ReferencedBodiesAreAvailable(
        CSharpTypeDocument document,
        CSharpTypeRenderPart part)
        => part.OwnedBodies
            .Select(static reference => reference.BodyId)
            .Concat(part.Contributions.Select(static contribution =>
                contribution.BodyId))
            .Distinct()
            .All(bodyId =>
                document.Bodies[bodyId].Outcome
                    == CSharpTypeBodyOutcome.Available);

    static bool UsesSelectedBodyContribution(
        CSharpTypeRenderPart part,
        ImmutableHashSet<int> selectedOwnedBodies)
        => part.Kind == CSharpTypeRenderPartKind.Implementation
            && part.Contributions.Any(
                contribution => selectedOwnedBodies.Contains(contribution.BodyId));

    static bool HasSelectedImplementationDifference(
        CSharpTypeDocument document,
        CSharpTypeDeclaration selected,
        ImmutableHashSet<int> selectedOwnedBodies,
        CSharpTypeProjectionRequest request)
    {
        if (selected.Parts.Any(part =>
            part.FullText != part.SkeletonText
            && UseFullAlternative(
                document,
                part,
                CSharpTypeBodyMode.SelectedBody,
                selectedDeclaration: true,
                selectedOwnedBodies)))
        {
            return true;
        }

        return document.Declarations.Any(declaration =>
            IsVisible(declaration, request)
            && declaration.Parts.Any(part =>
                part.FullText != part.SkeletonText
                && UseFullAlternative(
                    document,
                    part,
                    CSharpTypeBodyMode.SelectedBody,
                    selectedDeclaration: false,
                    selectedOwnedBodies)))
            || document.Frame.PrefixParts.Any(part =>
                part.FullText != part.SkeletonText
                && UseFullAlternative(
                    document,
                    part,
                    CSharpTypeBodyMode.SelectedBody,
                    selectedDeclaration: false,
                    selectedOwnedBodies));
    }

    static bool IsVisible(
        CSharpTypeDeclaration declaration,
        CSharpTypeProjectionRequest request)
    {
        if (declaration.Origin == CSharpTypeOrigin.Generated
            && !request.IncludeGenerated)
        {
            return false;
        }
        if (!request.Accessibilities.Contains(declaration.Accessibility))
            return false;

        return request.Placement switch
        {
            CSharpTypePlacementFilter.All => true,
            CSharpTypePlacementFilter.Instance =>
                declaration.Placement
                    == CSharpTypeDeclarationPlacement.Instance,
            CSharpTypePlacementFilter.Static =>
                declaration.Placement
                    == CSharpTypeDeclarationPlacement.Static,
            _ => throw new InvalidOperationException(),
        };
    }

    static void AddUnavailableBodyDiagnostics(
        CSharpTypeDocument document,
        CSharpTypeRenderPart part,
        int? declarationId,
        ImmutableArray<CSharpTypeProjectionDiagnostic>.Builder diagnostics,
        HashSet<int> diagnosedBodies)
    {
        foreach (int bodyId in part.OwnedBodies
            .Select(static reference => reference.BodyId)
            .Concat(part.Contributions.Select(static contribution => contribution.BodyId))
            .Distinct())
        {
            CSharpTypePhysicalBody body = document.Bodies[bodyId];
            if (body.Outcome == CSharpTypeBodyOutcome.Available
                || !diagnosedBodies.Add(bodyId))
            {
                continue;
            }
            diagnostics.Add(new(
                CSharpTypeProjectionDiagnosticKind.BodyUnavailable,
                $"Physical body {bodyId} is {body.Outcome}.",
                declarationId,
                bodyId));
        }
    }

    static CSharpSourceRange Rebase(CSharpSourceRange range, int offset)
        => new(checked(offset + range.Start), range.Length);

    static void AppendText(StringBuilder builder, string text)
    {
        if (text.Length
            > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars
                - builder.Length)
        {
            throw new InvalidOperationException(
                "C# Type projection exceeds the validated text budget.");
        }
        builder.Append(text);
    }

    static CSharpTypeProjectionOutcome.Rejected Reject(
        CSharpTypeProjectionFailureKind kind,
        string message)
        => new(kind, message);
}
