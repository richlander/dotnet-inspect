using System.Collections.Immutable;

namespace ILInspector.Metadata;

public sealed record MetadataTypeMemberPopulationRequest
{
    public MetadataTypeMemberPopulationRequest(
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        if (!Enum.IsDefined(accessibility)
            || accessibility == MetadataMethodAccessibilityFilter.All)
        {
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        }

        Type = type;
        Spelling = spelling;
        IncludeHidden = includeHidden;
        Accessibility = accessibility;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MetadataMemberSpelling Spelling { get; }
    public bool IncludeHidden { get; }
    public MetadataMethodAccessibilityFilter Accessibility { get; }
}

public sealed record MetadataTypeMemberPopulationGroup(
    string Key,
    string Name,
    string Kind,
    int CompleteCount,
    ImmutableArray<ApiMember> Members);

public sealed record MetadataTypeMemberPopulation(
    MetadataTypeDefinitionName Type,
    MetadataMemberSpelling Spelling,
    MetadataMethodAccessibilityFilter Accessibility,
    MetadataTypeMemberComposition Composition,
    ImmutableArray<MetadataTypeMemberPopulationGroup> Groups,
    ApiType Subject);

public abstract record MetadataTypeMemberPopulationOutcome
{
    private protected MetadataTypeMemberPopulationOutcome()
    {
    }

    public sealed record Available(MetadataTypeMemberPopulation Population)
        : MetadataTypeMemberPopulationOutcome;

    public sealed record TypeNotFound : MetadataTypeMemberPopulationOutcome;

    public sealed record TypeAmbiguous : MetadataTypeMemberPopulationOutcome;

    public sealed record Incomplete(ApiSurfaceExtractionBound Bound)
        : MetadataTypeMemberPopulationOutcome;

    public sealed record Failed(string Detail)
        : MetadataTypeMemberPopulationOutcome;
}

/// <summary>
/// Projects one exact Type's selected Member rows, complete per-group Counts,
/// and Composition Count from one bounded Metadata session.
/// </summary>
public static class MetadataTypeMemberPopulationInspection
{
    public static MetadataTypeMemberPopulationOutcome Inspect(
        AssemblyInspectionSession session,
        MetadataTypeMemberPopulationRequest request,
        ApiSurfaceExtractionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bounds);

        ApiSurfaceExtractionResult extraction =
            session.BoundedApiSurface(
                ApiSurfaceExtractionScope.IncludeAll,
                bounds);
        if (extraction is ApiSurfaceExtractionResult.Exceeded exceeded)
        {
            return new MetadataTypeMemberPopulationOutcome.Incomplete(
                exceeded.Bound);
        }
        ApiSurface surface =
            ((ApiSurfaceExtractionResult.Extracted)extraction).Surface;
        ApiType[] matches =
        [
            .. surface.Types.Where(type =>
                type.DefinitionName == request.Type),
        ];
        if (matches.Length == 0)
            return new MetadataTypeMemberPopulationOutcome.TypeNotFound();
        if (matches.Length != 1)
            return new MetadataTypeMemberPopulationOutcome.TypeAmbiguous();

        ApiType type = matches[0];
        using MetadataDeclarationSession declarations =
            session.CreateDeclarationSession(
                new MetadataOperationContext(
                    new MetadataOperationPolicy(bounds.MaxMetadataRows)));
        if (declarations.InspectTypeMemberComposition(
                request.Type,
                request.Spelling,
                request.IncludeHidden,
                request.Accessibility)
            is not MetadataTypeMemberCompositionOutcome.Counted counted)
        {
            return new MetadataTypeMemberPopulationOutcome.Failed(
                "The Type Member composition could not be counted.");
        }

        IReadOnlyList<ApiMember> allMembers =
            ApiTypeMemberPopulationProjection.Project(
                type,
                request.Spelling,
                request.IncludeHidden);
        type.Members = [.. allMembers];
        var projectedSurface = new ApiSurface();
        projectedSurface.Types.Add(type);
        ApiMemberIdentity.PopulateCanonicalIdentities(projectedSurface);

        MetadataMethodAccessibilityFilter selectedAccessibility =
            request.Accessibility;
        var groups =
            ImmutableArray.CreateBuilder<
                MetadataTypeMemberPopulationGroup>();
        foreach (IGrouping<string, ApiMember> group in allMembers.GroupBy(
                     GroupKey,
                     StringComparer.Ordinal))
        {
            ImmutableArray<ApiMember> selected =
            [
                .. group.Where(member =>
                    Accessibility(member.Accessibility)
                        == selectedAccessibility),
            ];
            if (selected.IsEmpty)
                continue;
            ApiMember first = selected[0];
            groups.Add(new(
                group.Key,
                first.Name,
                first.Kind,
                group.Count(),
                selected));
        }

        int selectedCount = groups.Sum(group => group.Members.Length);
        int compositionCount = request.Accessibility switch
        {
            MetadataMethodAccessibilityFilter.Public =>
                counted.Composition.Public,
            MetadataMethodAccessibilityFilter.Protected =>
                counted.Composition.Protected,
            MetadataMethodAccessibilityFilter.Internal =>
                counted.Composition.Internal,
            MetadataMethodAccessibilityFilter.Private =>
                counted.Composition.Private,
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.Accessibility)),
        };
        if (selectedCount != compositionCount)
        {
            return new MetadataTypeMemberPopulationOutcome.Failed(
                $"The selected Member rows ({selectedCount}) disagree with "
                    + $"their Composition Count ({compositionCount}).");
        }

        return new MetadataTypeMemberPopulationOutcome.Available(
            new(
                request.Type,
                request.Spelling,
                request.Accessibility,
                counted.Composition,
                groups.DrainToImmutable(),
                type));
    }

    static string GroupKey(ApiMember member) =>
        $"{member.Kind}:{member.Name}";

    static MetadataMethodAccessibilityFilter Accessibility(
        string? accessibility)
    {
        if (string.IsNullOrWhiteSpace(accessibility))
            return MetadataMethodAccessibilityFilter.Public;
        if (accessibility.Contains(
                "protected",
                StringComparison.OrdinalIgnoreCase))
        {
            return MetadataMethodAccessibilityFilter.Protected;
        }
        if (accessibility.Contains(
                "internal",
                StringComparison.OrdinalIgnoreCase))
        {
            return MetadataMethodAccessibilityFilter.Internal;
        }
        return MetadataMethodAccessibilityFilter.Private;
    }
}
