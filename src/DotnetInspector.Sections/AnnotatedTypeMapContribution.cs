using System.Collections.Immutable;

using DotnetInspector.Queries;

using ILInspector.Metadata;

using InertText;

using Inspector.Findings;

namespace DotnetInspector.Sections;

// docs/design/annotated-type-map-generation.md owns this content.

/// <summary>
/// An entry's outcome, divided as the map divides it: <see cref="Complete"/>
/// and <see cref="NoApplicableInput"/> are settled; the rest are unsettled.
/// </summary>
public enum AnnotatedTypeMapOutcomeKind
{
    Complete,
    NoApplicableInput,
    Failed,
    Unavailable,
    Indeterminate,
}

public sealed record AnnotatedTypeMapOutcome
{
    public AnnotatedTypeMapOutcome(AnnotatedTypeMapOutcomeKind kind, string? reason = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        Reason = reason;
    }

    public static AnnotatedTypeMapOutcome Complete { get; } = new(AnnotatedTypeMapOutcomeKind.Complete);

    public static AnnotatedTypeMapOutcome NoApplicableInput { get; } = new(AnnotatedTypeMapOutcomeKind.NoApplicableInput);

    public AnnotatedTypeMapOutcomeKind Kind { get; }

    public string? Reason { get; }

    public bool IsSettled =>
        Kind is AnnotatedTypeMapOutcomeKind.Complete or AnnotatedTypeMapOutcomeKind.NoApplicableInput;
}

/// <summary>
/// One entry of a family's content: its outcome and, only when
/// <see cref="AnnotatedTypeMapOutcomeKind.Complete"/>, its content. A
/// settled entry with no applicable input is an empty answer; an unsettled
/// entry carries no partial content.
/// </summary>
public sealed class AnnotatedTypeMapEntry<TContent>
    where TContent : class
{
    public AnnotatedTypeMapEntry(AnnotatedTypeMapOutcome outcome, TContent? content = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if ((outcome.Kind == AnnotatedTypeMapOutcomeKind.Complete) != (content is not null))
        {
            throw new ArgumentException(
                "Only a Complete entry carries content, and a Complete entry always does.",
                nameof(content));
        }

        Outcome = outcome;
        Content = content;
    }

    public AnnotatedTypeMapOutcome Outcome { get; }

    public TContent? Content { get; }
}

/// <summary>One family's settled results for one version side.</summary>
public abstract class AnnotatedTypeMapFamilyContent
{
    private protected AnnotatedTypeMapFamilyContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapFamilyKind expectedKind,
        ImmutableArray<AnnotatedTypeMapMemberKey> keys,
        bool hasTypeEntry)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Family '{family.Id}' is {family.Kind}; this content is for {expectedKind}.",
                nameof(family));
        }

        if (keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("Member keys cannot repeat.", nameof(keys));

        Family = family;
        Keys = keys;
        HasTypeEntry = hasTypeEntry;
    }

    public AnnotatedTypeMapFamily Family { get; }

    /// <summary>The Member keys this side contributes, in contribution order.</summary>
    public ImmutableArray<AnnotatedTypeMapMemberKey> Keys { get; }

    /// <summary>Whether the content holds the Type's own entry; false only when the Type is absent.</summary>
    public bool HasTypeEntry { get; }
}

public abstract class AnnotatedTypeMapFamilyContent<TType, TMember> : AnnotatedTypeMapFamilyContent
    where TType : class
    where TMember : class
{
    private protected AnnotatedTypeMapFamilyContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapFamilyKind expectedKind,
        AnnotatedTypeMapEntry<TType>? typeEntry,
        IEnumerable<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<TMember>>> members)
        : this(family, expectedKind, typeEntry, [.. members ?? throw new ArgumentNullException(nameof(members))])
    {
    }

    private AnnotatedTypeMapFamilyContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapFamilyKind expectedKind,
        AnnotatedTypeMapEntry<TType>? typeEntry,
        ImmutableArray<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<TMember>>> members)
        : base(family, expectedKind, [.. members.Select(static member => member.Key)], typeEntry is not null)
    {
        if (members.Any(static member => member.Key is null || member.Value is null))
            throw new ArgumentException("Member entries cannot contain null.", nameof(members));
        TypeEntry = typeEntry;
        Members = members.ToImmutableDictionary(static member => member.Key, static member => member.Value);
    }

    public AnnotatedTypeMapEntry<TType>? TypeEntry { get; }

    public ImmutableDictionary<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<TMember>> Members { get; }
}

/// <summary>
/// The Type's API shape facts on one side: its admission, and its detached,
/// partial projection at the <c>ApiAxis</c> scope when admitted.
/// </summary>
public sealed class AnnotatedTypeMapApiTypeFacts
{
    public AnnotatedTypeMapApiTypeFacts(bool admitted, ApiSurface? projection)
    {
        if (admitted != (projection is not null))
            throw new ArgumentException("An admitted Type has a projection; a Type not admitted has none.");
        Admitted = admitted;
        Projection = projection;
    }

    public bool Admitted { get; }

    /// <summary>The Type row and the projected Members, with the Type context the classifier reads.</summary>
    public ApiSurface? Projection { get; }
}

/// <summary>
/// A Member's API shape facts on one side: its projection in the API surface,
/// including its status facts, when that surface admits the Member.
/// </summary>
public sealed class AnnotatedTypeMapApiMemberFacts
{
    public AnnotatedTypeMapApiMemberFacts(ApiMember? projection) => Projection = projection;

    public ApiMember? Projection { get; }
}

/// <summary>One owned method's body identity under Finding Diff Any's Body equality.</summary>
public sealed record AnnotatedTypeMapBodyIdentity(OpaqueByteRange Method, OpaqueByteRange Identity);

/// <summary>
/// One declaration fact with its recorded admission: the API axis admission
/// evaluated as if the declaring Type were public on that side.
/// </summary>
public sealed record AnnotatedTypeMapDeclarationFact(OpaqueByteRange Fact, bool ApiAdmittedAsIfPublic);

public sealed class AnnotatedTypeMapBodyFacts
{
    public AnnotatedTypeMapBodyFacts(
        IEnumerable<AnnotatedTypeMapBodyIdentity> bodies,
        IEnumerable<AnnotatedTypeMapDeclarationFact> declarations)
    {
        Bodies = [.. bodies ?? throw new ArgumentNullException(nameof(bodies))];
        Declarations = [.. declarations ?? throw new ArgumentNullException(nameof(declarations))];
        if (Bodies.Any(static body => body is null) || Declarations.Any(static fact => fact is null))
            throw new ArgumentException("Body facts cannot contain null.");
    }

    public ImmutableArray<AnnotatedTypeMapBodyIdentity> Bodies { get; }

    public ImmutableArray<AnnotatedTypeMapDeclarationFact> Declarations { get; }
}

/// <summary>One method's fact keys, every fact in its census in stream order.</summary>
public sealed class AnnotatedTypeMapMethodFactStream
{
    public AnnotatedTypeMapMethodFactStream(OpaqueByteRange method, IEnumerable<OpaqueByteRange> facts)
    {
        Method = method;
        Facts = [.. facts ?? throw new ArgumentNullException(nameof(facts))];
    }

    public OpaqueByteRange Method { get; }

    public ImmutableArray<OpaqueByteRange> Facts { get; }
}

public sealed class AnnotatedTypeMapFactStreams
{
    public AnnotatedTypeMapFactStreams(IEnumerable<AnnotatedTypeMapMethodFactStream> methods)
    {
        Methods = [.. methods ?? throw new ArgumentNullException(nameof(methods))];
        if (Methods.Any(static method => method is null))
            throw new ArgumentException("Fact streams cannot contain null.", nameof(methods));
    }

    public ImmutableArray<AnnotatedTypeMapMethodFactStream> Methods { get; }
}

public sealed class AnnotatedTypeMapFindings
{
    public AnnotatedTypeMapFindings(IEnumerable<IFinding> findings)
    {
        Findings = [.. findings ?? throw new ArgumentNullException(nameof(findings))];
        if (Findings.Any(static finding => finding is null))
            throw new ArgumentException("Findings cannot contain null.", nameof(findings));
    }

    public ImmutableArray<IFinding> Findings { get; }
}

public sealed class AnnotatedTypeMapApiShapeContent
    : AnnotatedTypeMapFamilyContent<AnnotatedTypeMapApiTypeFacts, AnnotatedTypeMapApiMemberFacts>
{
    public AnnotatedTypeMapApiShapeContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapEntry<AnnotatedTypeMapApiTypeFacts>? typeEntry,
        IEnumerable<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<AnnotatedTypeMapApiMemberFacts>>> members)
        : base(family, AnnotatedTypeMapFamilyKind.ApiShape, typeEntry, members)
    {
    }
}

public sealed class AnnotatedTypeMapBodyShapeContent
    : AnnotatedTypeMapFamilyContent<AnnotatedTypeMapBodyFacts, AnnotatedTypeMapBodyFacts>
{
    public AnnotatedTypeMapBodyShapeContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapEntry<AnnotatedTypeMapBodyFacts>? typeEntry,
        IEnumerable<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<AnnotatedTypeMapBodyFacts>>> members)
        : base(family, AnnotatedTypeMapFamilyKind.BodyShape, typeEntry, members)
    {
    }
}

public sealed class AnnotatedTypeMapFactStreamContent
    : AnnotatedTypeMapFamilyContent<AnnotatedTypeMapFactStreams, AnnotatedTypeMapFactStreams>
{
    public AnnotatedTypeMapFactStreamContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapEntry<AnnotatedTypeMapFactStreams>? typeEntry,
        IEnumerable<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<AnnotatedTypeMapFactStreams>>> members)
        : base(family, AnnotatedTypeMapFamilyKind.FactStream, typeEntry, members)
    {
    }
}

public sealed class AnnotatedTypeMapFindingContent
    : AnnotatedTypeMapFamilyContent<AnnotatedTypeMapFindings, AnnotatedTypeMapFindings>
{
    public AnnotatedTypeMapFindingContent(
        AnnotatedTypeMapFamily family,
        AnnotatedTypeMapEntry<AnnotatedTypeMapFindings>? typeEntry,
        IEnumerable<KeyValuePair<AnnotatedTypeMapMemberKey, AnnotatedTypeMapEntry<AnnotatedTypeMapFindings>>> members)
        : base(family, AnnotatedTypeMapFamilyKind.Finding, typeEntry, members)
    {
    }
}

public enum AnnotatedTypeMapTypePresence
{
    Present,
    Absent,
}

/// <summary>
/// The concrete result type of every Annotated Type Map adapter query: one
/// query's results for one version side, a content set keyed by the families
/// the plan binds to that query. General query infrastructure never
/// interprets it.
/// </summary>
public sealed class AnnotatedTypeMapContribution
{
    public AnnotatedTypeMapContribution(
        SubjectRelationFocusAuthority focus,
        AnnotatedTypeMapTypePresence presence,
        IEnumerable<AnnotatedTypeMapFamilyContent> families)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(families);
        if (!Enum.IsDefined(presence))
            throw new ArgumentOutOfRangeException(nameof(presence));

        ImmutableArray<AnnotatedTypeMapFamilyContent> snapshot = [.. families];
        if (snapshot.IsEmpty)
            throw new ArgumentException("A contribution holds at least one family.", nameof(families));
        if (snapshot.Any(static content => content is null))
            throw new ArgumentException("Families cannot contain null.", nameof(families));
        if (snapshot.Select(static content => content.Family).Distinct(ReferenceEqualityComparer.Instance).Count()
            != snapshot.Length)
        {
            throw new ArgumentException("A contribution holds each family once.", nameof(families));
        }

        foreach (AnnotatedTypeMapFamilyContent content in snapshot)
        {
            bool absent = presence == AnnotatedTypeMapTypePresence.Absent;
            if (absent && (content.HasTypeEntry || !content.Keys.IsEmpty))
            {
                throw new ArgumentException(
                    $"An absent Type contributes no entries, but family '{content.Family.Id}' has some.",
                    nameof(families));
            }

            if (!absent && !content.HasTypeEntry)
            {
                throw new ArgumentException(
                    $"A present Type's family '{content.Family.Id}' must hold the Type's own entry.",
                    nameof(families));
            }
        }

        Focus = focus;
        Presence = presence;
        Families = snapshot;
    }

    public SubjectRelationFocusAuthority Focus { get; }

    /// <summary>The Type's presence on this side.</summary>
    public AnnotatedTypeMapTypePresence Presence { get; }

    /// <summary>The families this contribution holds: the trail from a result back to its plan.</summary>
    public ImmutableArray<AnnotatedTypeMapFamilyContent> Families { get; }
}
