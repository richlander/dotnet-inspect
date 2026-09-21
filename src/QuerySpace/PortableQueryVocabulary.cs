using System.Diagnostics.CodeAnalysis;
using QuerySpace.Rows;

namespace QuerySpace;

/// <summary>
/// How the terms of one vocabulary-declared family compose with each other.
/// </summary>
public enum PortableQueryFamilyKind
{
    /// <summary>The family's terms form an OR-union, and the union conjoins with everything outside it.</summary>
    Combining,

    /// <summary>Two of the family's terms in one intent are a contradiction, and resolution refuses the pair.</summary>
    Exclusive
}

/// <summary>
/// What a named order is for.
/// </summary>
public enum PortableQueryOrderPurpose
{
    /// <summary>Orders a sequence. Supplying one for a ranking role is a failure.</summary>
    Sequence,

    /// <summary>Ranks, and so may serve a ranking stage.</summary>
    Ranking
}

/// <summary>
/// What a key's binder made of one term.
/// </summary>
/// <remarks>
/// The identity is the vocabulary's, and it is what decides a duplicate: two
/// distinct terms the binder maps to one identity have collided, whatever their
/// spellings were. What the predicate itself is, this layer never asks.
/// </remarks>
public readonly record struct PortableQueryBinding<TPredicate>
{
    private PortableQueryBinding(
        bool bound,
        string predicateIdentity,
        TPredicate? predicate)
    {
        IsBound = bound;
        PredicateIdentity = predicateIdentity;
        BoundPredicate = predicate;
    }

    public bool IsBound { get; }

    public string PredicateIdentity { get; }

    private TPredicate? BoundPredicate { get; }

    public TPredicate Predicate =>
        IsBound
            ? BoundPredicate!
            : throw new InvalidOperationException(
                "A rejected value has no predicate.");

    /// <summary>The binder refused this value for this key.</summary>
    public static PortableQueryBinding<TPredicate> Rejected { get; } =
        new(false, "", default);

    /// <summary>The binder accepted, and names the predicate it produced.</summary>
    public static PortableQueryBinding<TPredicate> Bound(
        string predicateIdentity,
        TPredicate predicate)
    {
        ArgumentException.ThrowIfNullOrEmpty(predicateIdentity);
        return new(true, predicateIdentity, predicate);
    }
}

/// <summary>
/// One key a vocabulary declares: which operators it admits, how its values
/// bind, and which family it belongs to.
/// </summary>
public abstract class PortableQueryKeyDeclaration<TPredicate>
{
    /// <summary>The canonical query key, which is its identity.</summary>
    public abstract string Key { get; }

    /// <summary>
    /// Whether this key admits an operator. Intent never widens the set a
    /// vocabulary declares.
    /// </summary>
    public abstract bool AdmitsOperator(PortableQueryOperator @operator);

    /// <summary>
    /// Interprets one value. Everything this layer refuses to know about a value
    /// — what it means, whether it is well-formed, whether two spellings are
    /// equivalent — is decided here.
    /// </summary>
    public abstract PortableQueryBinding<TPredicate> Bind(
        PortableQueryOperator @operator,
        string value);

    /// <summary>
    /// The family this key belongs to, or <see langword="null"/> for a plain
    /// conjunct. Membership is declared here rather than marked on a term, so
    /// the serialized shape stays one flat set.
    /// </summary>
    public virtual string? Family => null;

    /// <summary>How this key's family composes. Meaningless without a family.</summary>
    public virtual PortableQueryFamilyKind FamilyKind =>
        PortableQueryFamilyKind.Combining;
}

/// <summary>
/// One execution-bound dimension a vocabulary declares.
/// </summary>
public abstract class PortableQueryDimensionDeclaration<TPredicate>
{
    /// <summary>The owner-issued dimension identity.</summary>
    public abstract string Dimension { get; }

    /// <summary>
    /// Whether a requested maximum is inside this dimension's range.
    /// </summary>
    /// <remarks>
    /// The range may depend on the terms already bound — Package Query admits at
    /// most twenty candidates once a package-content term is present, because
    /// each candidate then costs an archive — so the bound terms are supplied,
    /// and the resolution order guarantees they are complete before this is
    /// asked.
    /// </remarks>
    public abstract bool Admits(
        int requestedMaximum,
        IReadOnlyList<PortableQueryResolvedTerm<TPredicate>> boundTerms);
}

/// <summary>One term, and what the vocabulary's binder made of it.</summary>
public sealed record PortableQueryResolvedTerm<TPredicate>(
    PortableQueryTerm Term,
    string PredicateIdentity,
    TPredicate Predicate);

/// <summary>
/// The ranking one stage resolved to: the operation bound to it, or the
/// vocabulary's declared default.
/// </summary>
public sealed record PortableQueryResolvedRanking(
    int StageIndex,
    PortableQueryOrderOperation? Operation,
    string? DefaultReference);

/// <summary>
/// An intent that resolved: every term bound, every bound inside its range,
/// every stage admitted, and every ranking stage carrying a ranking.
/// </summary>
/// <remarks>
/// This is what a vocabulary turns into its own executable plan. It holds no
/// outcome and no presentation state, and it exists only inside the process that
/// resolved it.
/// </remarks>
public sealed record PortableQueryResolvedIntent<TPredicate>(
    IReadOnlyList<PortableQueryResolvedTerm<TPredicate>> Terms,
    IReadOnlyList<PortableQueryBound> Bounds,
    IReadOnlyList<PortableQueryStage> Stages,
    PortableQueryOrderOperation? Baseline,
    IReadOnlyList<PortableQueryResolvedRanking> Rankings);

/// <summary>
/// One vocabulary: the namespace an intent's keys, dimensions, and order
/// references resolve against, and the owner of what they mean.
/// </summary>
/// <remarks>
/// <para>
/// Keys, operator identities, vocabulary identities, and bound dimension
/// identities are a persisted compatibility surface. Once a build emits one into
/// a share link or a demo record, removing or renaming it breaks artifacts that
/// already exist — and a restored intent naming something this build cannot
/// resolve fails visibly rather than being dropped, defaulted, narrowed, or
/// widened.
/// </para>
/// <para>Owner: <c>docs/design/portable-query-intent.md</c>.</para>
/// </remarks>
public abstract class PortableQueryVocabulary<TPredicate, TPlan>
{
    /// <summary>The owner-issued vocabulary identity, which an intent's identity pairs with its bytes.</summary>
    public abstract string Identity { get; }

    /// <summary>
    /// Term families of which an intent must contain at least one member.
    /// </summary>
    /// <remarks>
    /// A required family expresses owner-required choice without privileging a
    /// key in the intent model. Package Query, for example, requires one member
    /// of its exact-package-or-prefix population family.
    /// </remarks>
    public virtual IReadOnlyList<string> RequiredTermFamilies => [];

    /// <summary>
    /// Execution-bound dimensions that every intent for this vocabulary must
    /// carry.
    /// </summary>
    public virtual IReadOnlyList<string> RequiredDimensions => [];

    public abstract bool TryGetKey(
        string key,
        [NotNullWhen(true)] out PortableQueryKeyDeclaration<TPredicate>? declaration);

    public abstract bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)] out PortableQueryDimensionDeclaration<TPredicate>? declaration);

    /// <summary>
    /// Whether this vocabulary declares a stage kind. Admission is per kind
    /// rather than all or none: Package Query selects final package rows with
    /// head, tail, and window and admits no top, because it has no order
    /// namespace to rank by.
    /// </summary>
    public abstract bool AdmitsStageKind(RowSelectionStageKind kind);

    /// <summary>
    /// Looks up a named order and says what it is for, or reports that this
    /// vocabulary declares no such reference.
    /// </summary>
    public abstract bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose);

    /// <summary>
    /// Whether a key can be ordered by. A key that exists but cannot be ordered
    /// by fails differently from one that does not exist.
    /// </summary>
    public abstract bool IsOrderable(string key);

    /// <summary>
    /// The ranking a ranking stage takes when no operation is bound to it, or
    /// <see langword="null"/> when this vocabulary declares none and such a
    /// stage fails.
    /// </summary>
    public virtual string? DefaultRanking => null;

    /// <summary>
    /// Whether two distinct terms that bind to one predicate collapse
    /// idempotently, or fail.
    /// </summary>
    /// <remarks>
    /// Either is a legitimate choice and it is this owner's. Collapsing means two
    /// intents differing only in a spelling this vocabulary treats as equal
    /// behave alike; refusing means the sender is told rather than silently
    /// given a narrower question.
    /// </remarks>
    public virtual bool CollapsesDuplicateBindings => false;

    /// <summary>
    /// Whether two bound terms may occur together when their relationship is
    /// not expressed by one family.
    /// </summary>
    /// <remarks>
    /// The relation must be symmetric. Accepted terms still compose only by
    /// their declared families; this hook can refuse a pair, not introduce a
    /// new composition rule.
    /// </remarks>
    public virtual bool AreTermsCompatible(
        PortableQueryResolvedTerm<TPredicate> first,
        PortableQueryResolvedTerm<TPredicate> second) => true;

    /// <summary>
    /// Builds this owner's executable plan from an intent that resolved.
    /// </summary>
    /// <remarks>
    /// Reached only after every check has passed, so a plan is never built from a
    /// partial binding.
    /// </remarks>
    public abstract TPlan CreatePlan(PortableQueryResolvedIntent<TPredicate> resolved);
}
