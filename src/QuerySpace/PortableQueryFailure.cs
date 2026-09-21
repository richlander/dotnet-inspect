namespace QuerySpace;

/// <summary>
/// Why an intent did not resolve.
/// </summary>
/// <remarks>
/// A closed union. A vocabulary cannot add to it; a new reason is a change to
/// the intent contract. Each reason fixes its own location and offending
/// identity, so two hosts resolving one intent produce the same failure and not
/// merely the same reason.
/// </remarks>
public enum PortableQueryFailureReason
{
    /// <summary>The current build does not offer the named vocabulary.</summary>
    UnknownVocabulary,

    /// <summary>The vocabulary declares no such key.</summary>
    UnknownKey,

    /// <summary>The key does not admit the operator the term carries.</summary>
    OperatorNotAdmitted,

    /// <summary>The key's binder refused the term's value.</summary>
    ValueRejected,

    /// <summary>Two distinct terms the binder maps to one predicate, which this vocabulary refuses.</summary>
    DuplicateAfterBinding,

    /// <summary>Two bound terms the vocabulary declares incompatible.</summary>
    TermsIncompatible,

    /// <summary>No bound term belongs to a family the vocabulary requires.</summary>
    RequiredTermFamilyMissing,

    /// <summary>The vocabulary declares no such execution-bound dimension.</summary>
    UnknownDimension,

    /// <summary>
    /// The requested maximum is outside the dimension's declared range, which may
    /// depend on the terms already bound.
    /// </summary>
    MaximumOutsideRange,

    /// <summary>The intent carries no bound for a dimension the vocabulary requires.</summary>
    RequiredDimensionMissing,

    /// <summary>The vocabulary does not declare this stage kind.</summary>
    StageNotAdmitted,

    /// <summary>The vocabulary declares no such order reference.</summary>
    UnknownOrderReference,

    /// <summary>The reference exists but nothing can be ordered by it.</summary>
    OrderReferenceNotOrderable,

    /// <summary>A sequence-purpose named order was supplied for a ranking role.</summary>
    OrderNotARanking,

    /// <summary>A ranking stage was reached with no bound operation and no declared default.</summary>
    RankingMissing
}

/// <summary>
/// Which part of an intent a failure is located in.
/// </summary>
public enum PortableQueryPart
{
    /// <summary>The vocabulary itself, which is not one of the serializable parts.</summary>
    Vocabulary,
    Terms,
    Bounds,
    Stages,
    Order
}

/// <summary>
/// Where in an intent a failure occurred: the part, and the element within it.
/// </summary>
/// <remarks>
/// <para>
/// Term and bound indexes use the model's semantic order rather than caller
/// order. A missing required family or dimension is located at the next
/// position after the present semantic terms or bounds. Stage indexes retain
/// declaration order; order-operation indexes use their model-defined order.
/// </para>
/// <para>
/// <see cref="FieldTermIndex"/> is present only for a failure inside a
/// field-list order operation, where the operation alone does not say which
/// term of it failed.
/// </para>
/// </remarks>
public readonly record struct PortableQueryLocation
{
    private PortableQueryLocation(
        PortableQueryPart part,
        int index,
        int? fieldTermIndex)
    {
        Part = part;
        Index = index;
        FieldTermIndex = fieldTermIndex;
    }

    public PortableQueryPart Part { get; }

    /// <summary>The element within the part, or -1 for the vocabulary.</summary>
    public int Index { get; }

    public int? FieldTermIndex { get; }

    public static PortableQueryLocation Vocabulary { get; } =
        new(PortableQueryPart.Vocabulary, -1, null);

    public static PortableQueryLocation Term(int index) =>
        new(PortableQueryPart.Terms, Validate(index), null);

    public static PortableQueryLocation Bound(int index) =>
        new(PortableQueryPart.Bounds, Validate(index), null);

    public static PortableQueryLocation Stage(int index) =>
        new(PortableQueryPart.Stages, Validate(index), null);

    public static PortableQueryLocation Operation(int index) =>
        new(PortableQueryPart.Order, Validate(index), null);

    public static PortableQueryLocation FieldTerm(int index, int fieldTermIndex) =>
        new(PortableQueryPart.Order, Validate(index), Validate(fieldTermIndex));

    private static int Validate(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return index;
    }
}

/// <summary>
/// One structured resolution failure. No plan and no partial binding accompany
/// it.
/// </summary>
/// <remarks>
/// Presentation-free by construction: the vocabulary identity, a typed location,
/// an owner-issued offending identity where the reason has one, and a typed
/// reason. No diagnostic sentence, rendered value, localized text, or exception
/// text is part of this contract — a host renders a failure, and what it renders
/// is its own.
/// </remarks>
public sealed record PortableQueryFailure
{
    private PortableQueryFailure(
        string vocabulary,
        PortableQueryFailureReason reason,
        PortableQueryLocation location,
        string? offender)
    {
        Vocabulary = vocabulary;
        Reason = reason;
        Location = location;
        Offender = offender;
    }

    /// <summary>The vocabulary the intent was resolved against.</summary>
    public string Vocabulary { get; }

    public PortableQueryFailureReason Reason { get; }

    public PortableQueryLocation Location { get; }

    /// <summary>
    /// The owner-issued identity that offended, or <see langword="null"/> where
    /// the reason has none — carried as absent rather than as an empty or
    /// invented one.
    /// </summary>
    public string? Offender { get; }

    /// <summary>
    /// Builds a failure, checking that the reason is paired with the location
    /// and offender the contract fixes for it.
    /// </summary>
    /// <remarks>
    /// The pairing is not advisory. Two hosts must produce the same failure and
    /// not merely the same reason, so a reason reported at the wrong part, or
    /// carrying an offender the table says it has none of, is a defect in the
    /// resolver rather than a variation a caller may choose.
    /// </remarks>
    public static PortableQueryFailure Create(
        string vocabulary,
        PortableQueryFailureReason reason,
        PortableQueryLocation location,
        string? offender)
    {
        ArgumentException.ThrowIfNullOrEmpty(vocabulary);
        if (!Enum.IsDefined(reason))
            throw PortableQueryModel.Undefined(reason, nameof(reason));

        PortableQueryPart expectedPart = PartOf(reason);
        if (location.Part != expectedPart)
        {
            throw new ArgumentException(
                $"{reason} is located at {expectedPart}, not {location.Part}.",
                nameof(location));
        }

        bool carriesOffender = CarriesOffender(reason);
        if (carriesOffender && string.IsNullOrEmpty(offender))
        {
            throw new ArgumentException(
                $"{reason} names an offending identity.",
                nameof(offender));
        }

        if (!carriesOffender && offender is not null)
        {
            throw new ArgumentException(
                $"{reason} has no offending identity.",
                nameof(offender));
        }

        return new(vocabulary, reason, location, offender);
    }

    /// <summary>The part each reason is located at.</summary>
    public static PortableQueryPart PartOf(PortableQueryFailureReason reason) => reason switch
    {
        PortableQueryFailureReason.UnknownVocabulary => PortableQueryPart.Vocabulary,
        PortableQueryFailureReason.UnknownKey
            or PortableQueryFailureReason.OperatorNotAdmitted
            or PortableQueryFailureReason.ValueRejected
            or PortableQueryFailureReason.DuplicateAfterBinding
            or PortableQueryFailureReason.TermsIncompatible
            or PortableQueryFailureReason.RequiredTermFamilyMissing =>
                PortableQueryPart.Terms,
        PortableQueryFailureReason.UnknownDimension
            or PortableQueryFailureReason.MaximumOutsideRange
            or PortableQueryFailureReason.RequiredDimensionMissing =>
                PortableQueryPart.Bounds,
        PortableQueryFailureReason.StageNotAdmitted
            or PortableQueryFailureReason.RankingMissing => PortableQueryPart.Stages,
        PortableQueryFailureReason.UnknownOrderReference
            or PortableQueryFailureReason.OrderReferenceNotOrderable
            or PortableQueryFailureReason.OrderNotARanking => PortableQueryPart.Order,
        _ => throw PortableQueryModel.Undefined(reason, nameof(reason))
    };

    /// <summary>Whether a reason names an offending identity.</summary>
    public static bool CarriesOffender(PortableQueryFailureReason reason) => reason switch
    {
        PortableQueryFailureReason.UnknownVocabulary
            or PortableQueryFailureReason.RankingMissing => false,
        _ when Enum.IsDefined(reason) => true,
        _ => throw PortableQueryModel.Undefined(reason, nameof(reason))
    };
}
