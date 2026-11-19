using System.Collections.Immutable;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>A Member to diff: its declaring Type and its anchor on either side.</summary>
public sealed record MemberDiffTarget(
    MetadataTypeDefinitionName DeclaringType,
    MemberAnchor Anchor);

/// <summary>
/// The Public API Member diffs of the selected Members over one completed
/// API comparison. The labels name the two versions in status notes.
/// </summary>
public sealed record MemberDiffRequest(
    AssemblyContextApiComparisonResult Comparison,
    string BaselineLabel,
    string CurrentLabel,
    ImmutableArray<MemberDiffTarget> Targets);

/// <summary>The Member diffs of a request.</summary>
public abstract record MemberDiffDocumentResult
{
    MemberDiffDocumentResult()
    {
    }

    /// <summary>One diff per matched Member pair, in the comparison's pair order.</summary>
    public sealed record Published(ImmutableArray<MemberDiffDocument> Documents) : MemberDiffDocumentResult;

    /// <summary>No Member of either version matches a target.</summary>
    public sealed record NotFound : MemberDiffDocumentResult;

    /// <summary>
    /// The comparison is incomplete, so a missing relation may be a
    /// correspondence it did not reach. Each target is an Unavailable diff
    /// with no lines.
    /// </summary>
    public sealed record Unavailable(
        string Reason,
        ImmutableArray<MemberDiffDocument> Documents) : MemberDiffDocumentResult;
}

/// <summary>
/// Composes Public API Member diff documents (docs/design/member-diff.md)
/// from the Metadata-owned API comparison: the member pairs the Library API
/// Diff relates, the exact-identity pairs of unrelated Members, and the
/// classified changes, with each version's surface for enclosure.
/// </summary>
public static class MemberDiffDocumentQuery
{
    public static MemberDiffDocumentResult Execute(MemberDiffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Comparison);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaselineLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CurrentLabel);
        if (request.Targets.IsDefault || request.Targets.Any(static target => target is null))
            throw new ArgumentException("Targets must be initialized and non-null.", nameof(request));

        AssemblyContextApiComparisonResult comparison = request.Comparison;
        if (!comparison.IsComplete
            || comparison.Comparison is not { } api
            || comparison.Before.Surface is not { } baselineSurface
            || comparison.After.Surface is not { } currentSurface)
        {
            string reason = IncompleteReason(comparison);
            return new MemberDiffDocumentResult.Unavailable(
                reason,
                [.. request.Targets.Select(target => MemberDiffDocument.Unavailable(
                    MemberDiffContentKind.PublicApi,
                    new MemberDiffSubject(target.DeclaringType.ToEscapedFullName(), target.Anchor.StableSelector),
                    reason))]);
        }

        var members = (FindingComparison<ApiMemberHandle>.Complete)api.Members.Value;
        var types = (FindingComparison<ApiTypeHandle>.Complete)api.Types.Value;
        var context = new Context(
            request,
            api.ApiDiff,
            new SurfaceTypes(baselineSurface),
            new SurfaceTypes(currentSurface),
            TypeStatus(types, PairKind.Added),
            TypeStatus(types, PairKind.Removed));

        var documents = ImmutableArray.CreateBuilder<MemberDiffDocument>();
        foreach (PairFinding<ApiMemberHandle> pair in members.Pairs)
        {
            var (baseline, current, related) = Sides(pair);
            if (Matches(request.Targets, baseline) || Matches(request.Targets, current))
                documents.Add(Create(context, baseline, current, related));
        }

        return documents.Count == 0
            ? new MemberDiffDocumentResult.NotFound()
            : new MemberDiffDocumentResult.Published(documents.ToImmutable());
    }

    sealed record Context(
        MemberDiffRequest Request,
        ApiDiff Diff,
        SurfaceTypes Baseline,
        SurfaceTypes Current,
        ImmutableHashSet<MetadataTypeDefinitionName> AddedTypes,
        ImmutableHashSet<MetadataTypeDefinitionName> RemovedTypes);

    static MemberDiffDocument Create(
        Context context,
        ApiMemberHandle? baseline,
        ApiMemberHandle? current,
        bool related)
    {
        var (baselineDeclaration, currentDeclaration) = CSharpMemberDeclaration.RenderComparablePair(
            baseline?.Type,
            baseline?.Member,
            current?.Type,
            current?.Member);

        ApiMemberHandle subjectHandle = current ?? baseline!;
        var subject = new MemberDiffSubject(
            TypeName(subjectHandle.Type),
            subjectHandle.Anchor?.StableSelector ?? subjectHandle.Member.Name);

        MemberDiffStatus status = (baseline, current) switch
        {
            (null, _) => MemberDiffStatus.Added,
            (_, null) => MemberDiffStatus.Removed,
            _ when related => MemberDiffStatus.Changed,
            // An unrelated exact-identity pair: the declaration comparison decides.
            _ => baselineDeclaration!.Text == currentDeclaration!.Text
                ? MemberDiffStatus.Unchanged
                : MemberDiffStatus.Changed,
        };

        MetadataTypeDefinitionName? baselineTypeName = (baseline ?? current)!.Type.DefinitionName;
        MetadataTypeDefinitionName? currentTypeName = (current ?? baseline)!.Type.DefinitionName;
        var baselineEnclosure = Enclosure(context, context.Baseline, baseline?.Type, baselineTypeName, memberPresent: baseline is not null);
        var currentEnclosure = Enclosure(context, context.Current, current?.Type, currentTypeName, memberPresent: current is not null);
        bool acrossTypes = baseline is not null
            && current is not null
            && !Equals(baseline.Type.DefinitionName, current.Type.DefinitionName);
        ImmutableArray<MemberDiffSideKind> printed = acrossTypes
            ? [MemberDiffSideKind.Baseline, MemberDiffSideKind.Current]
            : currentEnclosure.IsEmpty
                ? [MemberDiffSideKind.Baseline]
                : [MemberDiffSideKind.Current];

        ImmutableArray<MemberDiffCaret> carets = baselineDeclaration is not null && currentDeclaration is not null
            ? MemberDiffAnnotations.Carets(baselineDeclaration, currentDeclaration)
            : [];
        (MemberDiffSideKind noteSide, CSharpDeclarationParts noted) = currentDeclaration is not null
            ? (MemberDiffSideKind.Current, currentDeclaration)
            : (MemberDiffSideKind.Baseline, baselineDeclaration!);
        int noteLine = MemberDiffAnnotations.DeclarationLine(noted);
        ImmutableArray<MemberDiffChangeNote> changeNotes = MemberDiffAnnotations.ChangeNotes(
            context.Diff,
            baseline,
            current,
            noteSide,
            noteLine);
        ImmutableArray<MemberDiffLineStatusNote> statusNotes = status == MemberDiffStatus.Unchanged
            ? [new MemberDiffLineStatusNote(
                noteSide,
                noteLine,
                new MemberDiffStatusNote(MemberDiffStatusNoteKind.UnchangedSince, context.Request.BaselineLabel))]
            : [];

        return MemberDiffDocument.Create(
            MemberDiffContentKind.PublicApi,
            subject,
            status,
            Side(baselineDeclaration),
            Side(currentDeclaration),
            baselineEnclosure,
            currentEnclosure,
            printed,
            carets,
            changeNotes,
            statusNotes);
    }

    static MemberDiffSide? Side(CSharpDeclarationParts? declaration)
        => declaration is null
            ? null
            : new MemberDiffSide([.. declaration.Text.Split('\n')], declaration.UnidentifiedReason);

    /// <summary>
    /// The declarations enclosing a Member in one version, outermost first.
    /// The declaring Type is the side's own when the Member is present, and
    /// otherwise the version's Type of the same definition name, if any. A
    /// containing Type outside the surface is recorded by name only.
    /// </summary>
    static ImmutableArray<MemberDiffEnclosure> Enclosure(
        Context context,
        SurfaceTypes surface,
        ApiType? declaringType,
        MetadataTypeDefinitionName? definitionName,
        bool memberPresent)
    {
        if (definitionName is null)
        {
            if (declaringType is null)
                return [];
            // A surface without definition names: the Type alone, without containers.
            return [TypeEnclosure(context, declaringType, null, declaringType.Members.Count - (memberPresent ? 1 : 0))];
        }

        declaringType ??= surface.Find(definitionName.Namespace, definitionName.Segments);
        if (declaringType is null)
            return [];

        var enclosure = ImmutableArray.CreateBuilder<MemberDiffEnclosure>();
        if (definitionName.Namespace.Length > 0)
        {
            enclosure.Add(new MemberDiffEnclosure(
                MemberDiffEnclosureKind.Namespace,
                definitionName.Namespace,
                $"namespace {CSharpFormatter.EscapeNamespace(definitionName.Namespace)};",
                OtherMembers: null,
                Notes: []));
        }
        for (int length = 1; length < definitionName.Segments.Length; length++)
        {
            ImmutableArray<string> prefix = definitionName.Segments[..length];
            ApiType? container = surface.Find(definitionName.Namespace, prefix);
            enclosure.Add(container is null
                ? new MemberDiffEnclosure(
                    MemberDiffEnclosureKind.Type,
                    SimpleName(prefix[^1]),
                    Text: null,
                    OtherMembers: null,
                    Notes: [])
                : TypeEnclosure(context, container, prefix[^1], otherMembers: null));
        }
        enclosure.Add(TypeEnclosure(
            context,
            declaringType,
            definitionName.Segments[^1],
            declaringType.Members.Count - (memberPresent ? 1 : 0)));
        return enclosure.ToImmutable();
    }

    static MemberDiffEnclosure TypeEnclosure(
        Context context,
        ApiType type,
        string? segment,
        int? otherMembers)
    {
        var notes = ImmutableArray.CreateBuilder<MemberDiffStatusNote>();
        if (type.DefinitionName is { } name)
        {
            if (context.AddedTypes.Contains(name))
                notes.Add(new MemberDiffStatusNote(MemberDiffStatusNoteKind.TypeAdded, context.Request.CurrentLabel));
            if (context.RemovedTypes.Contains(name))
                notes.Add(new MemberDiffStatusNote(MemberDiffStatusNoteKind.TypeRemoved, context.Request.CurrentLabel));
        }
        return new MemberDiffEnclosure(
            MemberDiffEnclosureKind.Type,
            segment is null ? type.Name : SimpleName(segment),
            CSharpMemberDeclaration.RenderEnclosingTypeDeclaration(type),
            otherMembers,
            notes.ToImmutable());
    }

    static string SimpleName(string segment)
    {
        int arity = segment.IndexOf('`');
        return arity < 0 ? segment : segment[..arity];
    }

    static string TypeName(ApiType type)
        => type.DefinitionName?.ToEscapedFullName() ?? type.FullName;

    static (ApiMemberHandle? Baseline, ApiMemberHandle? Current, bool Related) Sides(
        PairFinding<ApiMemberHandle> pair)
        => pair.Value switch
        {
            PairFinding<ApiMemberHandle>.Added added => (null, added.New.Payload, true),
            PairFinding<ApiMemberHandle>.Removed removed => (removed.Old.Payload, null, true),
            PairFinding<ApiMemberHandle>.Changed changed => (changed.Old.Payload, changed.New.Payload, true),
            PairFinding<ApiMemberHandle>.Present present => (present.Old.Payload, present.New.Payload, false),
            _ => throw new InvalidOperationException("Unknown member pair kind."),
        };

    static bool Matches(ImmutableArray<MemberDiffTarget> targets, ApiMemberHandle? handle)
        => handle is { Type.DefinitionName: { } typeName, Anchor: { } anchor }
            && targets.Any(target => target.DeclaringType.Equals(typeName) && target.Anchor == anchor);

    static ImmutableHashSet<MetadataTypeDefinitionName> TypeStatus(
        FindingComparison<ApiTypeHandle>.Complete types,
        PairKind kind)
        => types.Pairs
            .Where(pair => pair.Kind == kind)
            .Select(static pair => pair.Value switch
            {
                PairFinding<ApiTypeHandle>.Added added => added.New.Payload.Type.DefinitionName,
                PairFinding<ApiTypeHandle>.Removed removed => removed.Old.Payload.Type.DefinitionName,
                _ => null,
            })
            .OfType<MetadataTypeDefinitionName>()
            .ToImmutableHashSet();

    static string IncompleteReason(AssemblyContextApiComparisonResult comparison)
    {
        if (!comparison.Before.IsComplete && !comparison.After.IsComplete)
            return "Neither version's API surface was projected completely.";
        if (!comparison.Before.IsComplete)
            return "The baseline version's API surface was not projected completely.";
        if (!comparison.After.IsComplete)
            return "The current version's API surface was not projected completely.";
        return comparison.Comparison?.Members.Failure
            ?? comparison.Comparison?.Types.Failure
            ?? "The API comparison did not complete.";
    }

    /// <summary>One version's Types by definition name.</summary>
    sealed class SurfaceTypes(ApiSurface surface)
    {
        readonly Dictionary<string, ApiType> _types = surface.Types
            .Where(static type => type.DefinitionName is not null)
            .GroupBy(static type => Key(type.DefinitionName!.Namespace, type.DefinitionName.Segments))
            .ToDictionary(static group => group.Key, static group => group.First());

        public ApiType? Find(string @namespace, ImmutableArray<string> segments)
            => _types.GetValueOrDefault(Key(@namespace, segments));

        static string Key(string @namespace, ImmutableArray<string> segments)
            => @namespace + "\0" + string.Join('\0', segments);
    }
}
