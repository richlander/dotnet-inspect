using System.Collections.ObjectModel;

namespace CiChangeDetection.Planning;

/// <summary>
/// The changed-input descriptor: the canonical record count and the digest of
/// the canonical record stream exactly as acquired.
/// </summary>
internal sealed class PlanInputDescriptor
{
    internal PlanInputDescriptor(int recordCount, string sha256)
    {
        if (recordCount < 0)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "input record count is negative");
        }

        PlanDigest.Validate(sha256, "input");
        RecordCount = recordCount;
        Sha256 = sha256;
    }

    internal int RecordCount { get; }

    internal string Sha256 { get; }
}

/// <summary>
/// A bounded scoped-evidence descriptor binding one consuming scope to its
/// artifact name, record framing, record count, and byte digest.
/// </summary>
internal sealed class PlanScopeDescriptor
{
    /// <summary>
    /// The only record framing this schema version defines: exact path bytes
    /// followed by one NUL terminator, in plan input order.
    /// </summary>
    internal const string NulTerminatedFraming = "pathBytesNulTerminated";

    /// <summary>
    /// The scope name and workflow artifact name of the TLA+ path corpus.
    /// </summary>
    internal const string TlaScope = "tla";

    internal const string TlaArtifact = "ci-plan-tla-paths0";

    internal PlanScopeDescriptor(
        string scope,
        string artifact,
        string framing,
        int recordCount,
        string sha256)
    {
        if (scope != TlaScope || artifact != TlaArtifact)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "unsupported scope or artifact name");
        }

        if (framing != NulTerminatedFraming)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "unsupported scope record framing");
        }

        if (recordCount < 0)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "scope record count is negative");
        }

        PlanDigest.Validate(sha256, "scope");
        Scope = scope;
        Artifact = artifact;
        Framing = framing;
        RecordCount = recordCount;
        Sha256 = sha256;
    }

    internal string Scope { get; }

    internal string Artifact { get; }

    internal string Framing { get; }

    internal int RecordCount { get; }

    internal string Sha256 { get; }
}

/// <summary>
/// The bounded diagnostic codes a valid plan may carry. Each records a
/// deliberately conservative policy choice; none replaces a refusal.
/// </summary>
internal static class PlanDiagnosticCodes
{
    /// <summary>
    /// The inspect-web project inventory was missing or malformed, so every
    /// <c>src</c> change broadened to the Browser/Wasm lane.
    /// </summary>
    internal const string InspectWebInventoryUnavailable =
        "inspectWebInventoryUnavailable";

    /// <summary>
    /// The decompiler skip inventory was missing or malformed, so no source,
    /// test, or tool project was exempted from the decompiler gates.
    /// </summary>
    internal const string DecompilerSkipInventoryUnavailable =
        "decompilerSkipInventoryUnavailable";

    /// <summary>
    /// The complete set of defined codes, in serialized order.
    /// </summary>
    internal static IReadOnlyList<string> All { get; } =
    [
        DecompilerSkipInventoryUnavailable,
        InspectWebInventoryUnavailable,
    ];
}

/// <summary>
/// Digest spelling rules for plan fields.
/// </summary>
internal static class PlanDigest
{
    /// <summary>
    /// Requires a 64-character lowercase hexadecimal SHA-256 spelling.
    /// </summary>
    /// <param name="value">The candidate digest.</param>
    /// <param name="role">The field role named in a refusal.</param>
    internal static void Validate(string value, string role)
    {
        if (value.Length != 64)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                $"{role} digest is not a SHA-256 digest");
        }

        foreach (char character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    $"{role} digest is not lowercase hexadecimal");
            }
        }
    }
}

/// <summary>
/// One immutable, versioned CI plan. One planning operation constructs exactly
/// one of these, and every workflow output is a mechanical projection of it.
/// </summary>
internal sealed class ChangePlan
{
    /// <summary>
    /// The only schema version this repository produces or consumes.
    /// </summary>
    internal const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The only status valid in a serialized plan.
    /// </summary>
    internal const string PlannedStatus = "planned";

    /// <summary>
    /// The serialized plan's byte ceiling, leaving margin under the observed
    /// workflow-expression scalar boundary.
    /// </summary>
    internal const int MaximumSerializedBytes = 16 * 1024;

    internal ChangePlan(
        int schemaVersion,
        string status,
        CandidateProvenance provenance,
        PlanInputDescriptor input,
        ValidationSelections validations,
        IReadOnlyList<PlanScopeDescriptor> scopes,
        IReadOnlyList<string> diagnostics)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "unsupported plan schema version");
        }

        if (status != PlannedStatus)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "unsupported plan status");
        }

        PlanScopeDescriptor[] orderedScopes = [.. scopes];
        if (orderedScopes.Length > 1
            || (orderedScopes.Length == 1 && !validations.Tla))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "scope descriptors do not match the selected validations");
        }

        if (validations.Tla && orderedScopes.Length != 1)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "a selected tla validation requires its scope descriptor");
        }

        string[] orderedDiagnostics = [.. diagnostics];
        foreach (string diagnostic in orderedDiagnostics)
        {
            if (!PlanDiagnosticCodes.All.Contains(diagnostic))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    "unsupported plan diagnostic code");
            }
        }

        if (orderedDiagnostics.Length
            != orderedDiagnostics.Distinct(StringComparer.Ordinal).Count())
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "duplicate plan diagnostic code");
        }

        SchemaVersion = schemaVersion;
        Status = status;
        Provenance = provenance;
        Input = input;
        Validations = validations;
        Scopes = Array.AsReadOnly(orderedScopes);
        Diagnostics = Array.AsReadOnly(orderedDiagnostics);
    }

    internal int SchemaVersion { get; }

    internal string Status { get; }

    internal CandidateProvenance Provenance { get; }

    internal PlanInputDescriptor Input { get; }

    internal ValidationSelections Validations { get; }

    internal ReadOnlyCollection<PlanScopeDescriptor> Scopes { get; }

    internal ReadOnlyCollection<string> Diagnostics { get; }

    /// <summary>
    /// Gets the TLA+ scope descriptor, or null when TLA+ is not selected.
    /// </summary>
    internal PlanScopeDescriptor? TlaScope =>
        Scopes.FirstOrDefault(scope =>
            scope.Scope == PlanScopeDescriptor.TlaScope);
}
