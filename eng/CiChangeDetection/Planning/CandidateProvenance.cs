namespace CiChangeDetection.Planning;

/// <summary>
/// The closed set of event provenance cases the planner accepts.
/// </summary>
internal enum PlanEventKind
{
    PullRequestSyntheticCandidate,
    Push,
    MergeGroup,
}

/// <summary>
/// One immutable provenance case naming the two endpoint object IDs whose
/// difference defines the checked candidate.
/// </summary>
internal sealed class CandidateProvenance
{
    private CandidateProvenance(
        PlanEventKind kind,
        string baseObjectId,
        string candidateObjectId)
    {
        Kind = kind;
        BaseObjectId = baseObjectId;
        CandidateObjectId = candidateObjectId;
    }

    internal PlanEventKind Kind { get; }

    internal string BaseObjectId { get; }

    internal string CandidateObjectId { get; }

    /// <summary>
    /// Gets the lower camel case wire name of a provenance kind.
    /// </summary>
    /// <param name="kind">The provenance kind.</param>
    /// <returns>The ASCII kind name.</returns>
    internal static string KindName(PlanEventKind kind) => kind switch
    {
        PlanEventKind.PullRequestSyntheticCandidate =>
            "pullRequestSyntheticCandidate",
        PlanEventKind.Push => "push",
        PlanEventKind.MergeGroup => "mergeGroup",
        _ => throw new PlanRefusalException(
            PlanRefusalCategory.Usage,
            "unsupported provenance kind"),
    };

    /// <summary>
    /// Parses a provenance kind from its wire name.
    /// </summary>
    /// <param name="name">The candidate kind name.</param>
    /// <returns>The parsed kind.</returns>
    internal static PlanEventKind ParseKindName(string name) => name switch
    {
        "pullRequestSyntheticCandidate" =>
            PlanEventKind.PullRequestSyntheticCandidate,
        "push" => PlanEventKind.Push,
        "mergeGroup" => PlanEventKind.MergeGroup,
        _ => throw new PlanRefusalException(
            PlanRefusalCategory.Usage,
            "unsupported provenance kind name"),
    };

    /// <summary>
    /// Creates a provenance case after validating both endpoint object IDs.
    /// </summary>
    /// <param name="kind">The provenance kind.</param>
    /// <param name="baseObjectId">The base endpoint object ID.</param>
    /// <param name="candidateObjectId">The candidate endpoint object ID.</param>
    /// <returns>The validated provenance case.</returns>
    internal static CandidateProvenance Create(
        PlanEventKind kind,
        string baseObjectId,
        string candidateObjectId)
    {
        _ = KindName(kind);
        ValidateObjectId(baseObjectId, "base");
        ValidateObjectId(candidateObjectId, "candidate");
        return new CandidateProvenance(kind, baseObjectId, candidateObjectId);
    }

    /// <summary>
    /// Requires a full canonical lowercase hexadecimal Git object ID of either
    /// supported hash width. Abbreviations, revision expressions, uppercase
    /// spellings, and the all-zero identifier are refused.
    /// </summary>
    /// <param name="objectId">The candidate object ID.</param>
    /// <param name="role">The endpoint role named in a refusal.</param>
    internal static void ValidateObjectId(string objectId, string role)
    {
        if (objectId.Length is not (40 or 64))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.ObjectIdFormat,
                $"{role} object ID is not a full SHA-1 or SHA-256 identifier");
        }

        bool allZero = true;
        foreach (char character in objectId)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.ObjectIdFormat,
                    $"{role} object ID is not lowercase hexadecimal");
            }

            allZero &= character == '0';
        }

        if (allZero)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.ObjectIdFormat,
                $"{role} object ID is the zero identifier");
        }
    }
}
