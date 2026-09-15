namespace ILInspector.Research;

/// <summary>
/// The rank-1 comparison profile that one Research admission describes.
/// </summary>
/// <remarks>
/// Admission derives its per-profile shape validation from this declaration.
/// <c>ResearchAdmission_AdmitsEveryDeclaredProfile</c> derives its expected set
/// from these members, so a missing or stale member fails that gate.
/// </remarks>
public enum ResearchComparisonProfile
{
    /// <summary>
    /// Two acquired assembly descriptors, their resolvers, and their body
    /// indexes compared for C# and IL implementation evidence.
    /// </summary>
    ImplementationComparison,

    /// <summary>Analysis body indexes compared for body signals.</summary>
    BodySignal,
}

/// <summary>
/// The side of one comparison question that a side-local input occupies.
/// </summary>
/// <remarks>
/// <c>ResearchAdmission_MintsFreshParentedIdentitiesForEveryOccurrence</c>
/// derives its expected side set from this declaration.
/// </remarks>
public enum ResearchComparisonSide
{
    /// <summary>The earlier side of the comparison question.</summary>
    Before,

    /// <summary>The later side of the comparison question.</summary>
    After,
}

/// <summary>
/// Opaque Research identity for one admitted comparison operation.
/// </summary>
/// <remarks>
/// Identity is reference identity. There is no public constructor, parsing,
/// string conversion, ordinal, or MVID/path/name surrogate. Only a new
/// admission mints one.
/// <c>ResearchAdmissionIdentities_AreOwnerIssuedReferenceIdentities</c> and
/// <c>ResearchAdmission_NewAdmissionMintsFreshOperationAndPopulation</c> gate
/// those properties.
/// </remarks>
public sealed class ResearchComparisonOperationId
{
    internal ResearchComparisonOperationId()
    {
    }
}

/// <summary>
/// Opaque Research identity for one comparison question, parented by exactly
/// one admitted operation.
/// </summary>
/// <remarks>
/// Identity is reference identity; see
/// <see cref="ResearchComparisonOperationId"/>.
/// </remarks>
public sealed class ResearchComparisonQuestionId
{
    internal ResearchComparisonQuestionId(ResearchComparisonOperationId operation)
        => Operation = operation;

    /// <summary>The operation that parents this question.</summary>
    public ResearchComparisonOperationId Operation { get; }
}

/// <summary>
/// Opaque Research identity for one side-local admitted input, parented by
/// exactly one operation and question and carrying its explicit side.
/// </summary>
/// <remarks>
/// One identity is minted per admitted input occurrence. Repeating the same
/// borrowed input value in two occurrences mints two distinct identities.
/// Identity is reference identity; see
/// <see cref="ResearchComparisonOperationId"/>.
/// </remarks>
public sealed class ResearchComparisonInputId
{
    internal ResearchComparisonInputId(
        ResearchComparisonQuestionId question,
        ResearchComparisonSide side)
    {
        Question = question;
        Side = side;
    }

    /// <summary>The operation that parents this input.</summary>
    public ResearchComparisonOperationId Operation => Question.Operation;

    /// <summary>The question that parents this input.</summary>
    public ResearchComparisonQuestionId Question { get; }

    /// <summary>The side this input occupies within its question.</summary>
    public ResearchComparisonSide Side { get; }
}
