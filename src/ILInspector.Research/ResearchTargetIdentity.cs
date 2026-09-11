using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Whether one admitted input may supply implementation target evidence.
/// </summary>
/// <remarks>
/// The role is Research-owned target-planning evidence supplied by the caller
/// for every admitted input. It is deliberately not a member of
/// <see cref="ImplementationAssemblyInput"/>: that legacy borrowed value
/// carries acquisition evidence, not a Research planning decision.
/// <c>ResearchTargetAttempts_MapEveryMetadataDiagnosticKind</c> and
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c> exercise
/// both members; a <see cref="ReferenceOnly"/> request terminates
/// <c>Unavailable</c> without opening the borrowed input.
/// </remarks>
public enum ResearchTargetInputRole
{
    /// <summary>The input may supply implementation target evidence.</summary>
    Implementation,

    /// <summary>
    /// The input is admitted for reference resolution only and cannot supply
    /// an implementation target.
    /// </summary>
    ReferenceOnly,
}

/// <summary>
/// Whether one selection occurrence carries an API selector into every
/// admitted input of its question, or designates one exact physical address in
/// one admitted input.
/// </summary>
public enum ResearchTargetRequestKind
{
    /// <summary>
    /// An API-level selector evaluated against every admitted input in the
    /// question.
    /// </summary>
    Carried,

    /// <summary>
    /// One exact physical method address asserted for exactly one admitted
    /// input.
    /// </summary>
    ExactAddress,
}

/// <summary>
/// The physical method relationship one resolved target has to its selected
/// API member.
/// </summary>
/// <remarks>
/// The role is derived only from the selected <c>ApiMember</c>'s accessor
/// MethodDef tokens and the resolved body token, never from an ordinal.
/// <c>ResearchTargetRequests_CarriedRoleIsDerivedOnlyAfterResolution</c> and
/// <c>ResearchTargetResolution_PreservesMetadataDiagnosticsAndAccessorRoles</c>
/// gate that.
/// </remarks>
public enum ResearchTargetRelationshipRole
{
    /// <summary>No physical method relationship exists for this member.</summary>
    None,

    /// <summary>The member's own MethodDef.</summary>
    Method,

    /// <summary>A property's get accessor.</summary>
    Getter,

    /// <summary>A property's set accessor.</summary>
    Setter,

    /// <summary>An event's add accessor.</summary>
    Adder,

    /// <summary>An event's remove accessor.</summary>
    Remover,
}

/// <summary>
/// Opaque Research identity for one target scope: exactly one immutable
/// member-selection occurrence within one admitted question.
/// </summary>
/// <remarks>
/// Identity is reference identity. There is no public constructor, parsing,
/// string conversion, ordinal, or selector surrogate. A selector or
/// declaring-type filter is intent inside the scope, never the scope identity.
/// <c>ResearchTargetScopes_DeriveBijectivelyFromSelectionOccurrences</c> gates
/// the bijection, and
/// <c>ResearchTargetCancellation_RetryPreservesAdmissionAndMintsFreshTargets</c>
/// gates that a retry mints a fresh scope.
/// </remarks>
public sealed class ResearchTargetScopeId
{
    internal ResearchTargetScopeId(ResearchComparisonQuestionId question)
        => Question = question;

    /// <summary>The question that parents this scope.</summary>
    public ResearchComparisonQuestionId Question { get; }

    /// <summary>The operation that parents this scope.</summary>
    public ResearchComparisonOperationId Operation => Question.Operation;
}

/// <summary>
/// Opaque Research identity for one logical assembly comparison domain inside
/// exactly one target scope.
/// </summary>
/// <remarks>
/// Identity is reference identity; see <see cref="ResearchTargetScopeId"/>.
/// One domain identity is minted per scope, so two scopes never share a domain
/// even when their <see cref="ResearchTargetDomainKey"/> values are equivalent.
/// </remarks>
public sealed class ResearchTargetDomainId
{
    internal ResearchTargetDomainId(ResearchTargetScopeId scope)
        => Scope = scope;

    /// <summary>The scope that parents this domain.</summary>
    public ResearchTargetScopeId Scope { get; }

    /// <summary>The question that parents this domain.</summary>
    public ResearchComparisonQuestionId Question => Scope.Question;

    /// <summary>The operation that parents this domain.</summary>
    public ResearchComparisonOperationId Operation => Scope.Operation;
}

/// <summary>
/// Opaque Research identity for one side-local target request.
/// </summary>
/// <remarks>
/// Identity is reference identity; see <see cref="ResearchTargetScopeId"/>.
/// Side, admitted input, scope, and domain all participate in the parented
/// identity, so no request fans across sides, inputs, questions, operations,
/// or scopes.
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c> gates that
/// locality, and
/// <c>ResearchTargetIdentities_AreOwnerIssuedReferenceIdentities</c> gates the
/// owner-issued reference-identity shape of all four target identities.
/// </remarks>
public sealed class ResearchTargetRequestId
{
    internal ResearchTargetRequestId(
        ResearchTargetDomainId domain,
        ResearchComparisonInputId input)
    {
        Domain = domain;
        Input = input;
    }

    /// <summary>The domain that parents this request.</summary>
    public ResearchTargetDomainId Domain { get; }

    /// <summary>The side-local admitted input this request evaluates.</summary>
    public ResearchComparisonInputId Input { get; }

    /// <summary>The scope that parents this request.</summary>
    public ResearchTargetScopeId Scope => Domain.Scope;

    /// <summary>The question that parents this request.</summary>
    public ResearchComparisonQuestionId Question => Domain.Question;

    /// <summary>The operation that parents this request.</summary>
    public ResearchComparisonOperationId Operation => Domain.Operation;

    /// <summary>The side this request occupies within its question.</summary>
    public ResearchComparisonSide Side => Input.Side;
}

/// <summary>
/// Opaque Research identity for one attempt to resolve exactly one request.
/// </summary>
/// <remarks>
/// Identity is reference identity; see <see cref="ResearchTargetScopeId"/>.
/// <c>ResearchTargetAttempts_AccountForEveryRequestExactlyOnce</c> gates the
/// request-to-attempt bijection.
/// </remarks>
public sealed class ResearchTargetAttemptId
{
    internal ResearchTargetAttemptId(ResearchTargetRequestId request)
        => Request = request;

    /// <summary>The request this attempt evaluates.</summary>
    public ResearchTargetRequestId Request { get; }

    /// <summary>The domain that parents this attempt.</summary>
    public ResearchTargetDomainId Domain => Request.Domain;

    /// <summary>The scope that parents this attempt.</summary>
    public ResearchTargetScopeId Scope => Request.Scope;

    /// <summary>The operation that parents this attempt.</summary>
    public ResearchComparisonOperationId Operation => Request.Operation;
}

/// <summary>
/// The owner-issued key that groups admitted inputs into one logical assembly
/// comparison domain.
/// </summary>
/// <remarks>
/// The key retains the Metadata-owned <see cref="AssemblyReferenceIdentity"/>
/// with only <see cref="AssemblyReferenceIdentity.Version"/> erased, and
/// compares through
/// <see cref="AssemblyReferenceIdentity.EquivalentComparer"/>. Research does
/// not renormalize name, culture, or public-key-token fields itself, and never
/// derives a key from a formatted assembly name or a body-index path.
/// <c>ResearchTargetDomains_EraseOnlyAssemblyVersion</c> gates that exactly
/// the version is erased.
/// </remarks>
public sealed class ResearchTargetDomainKey : IEquatable<ResearchTargetDomainKey>
{
    ResearchTargetDomainKey(AssemblyReferenceIdentity identity)
        => Identity = identity;

    /// <summary>
    /// The Metadata-owned identity with only <c>Version</c> erased. Every
    /// other field is retained exactly as acquisition supplied it.
    /// </summary>
    public AssemblyReferenceIdentity Identity { get; }

    internal static ResearchTargetDomainKey From(
        AssemblyReferenceIdentity identity)
        => new(identity.Version is null ? identity : identity with { Version = null });

    public bool Equals(ResearchTargetDomainKey? other)
        => other is not null
            && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                Identity,
                other.Identity);

    public override bool Equals(object? obj)
        => Equals(obj as ResearchTargetDomainKey);

    public override int GetHashCode()
        => AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(Identity);
}

/// <summary>
/// The existing Metadata API-surface scope every Research target request
/// evaluates against.
/// </summary>
/// <remarks>
/// Every request evaluates the same surface: public and non-public API-surface
/// members, Metadata-supported compiler-generated types and fields, and no
/// member-kind filter. Synthesized methods that Metadata excludes from its API
/// surface are not added back by Research.
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c> gates that
/// every request carries exactly this pinned scope.
/// </remarks>
public sealed class ResearchTargetSurfaceScope
{
    ResearchTargetSurfaceScope()
    {
    }

    /// <summary>The single pinned surface scope.</summary>
    public static ResearchTargetSurfaceScope MetadataApiSurface { get; } = new();

    /// <summary>Extraction includes non-public API-surface members.</summary>
    public bool IncludeNonPublic => true;

    /// <summary>
    /// Extraction includes Metadata-supported compiler-generated types and
    /// fields; it does not change Metadata's synthesized-method policy.
    /// </summary>
    public bool IncludeCompilerGeneratedTypesAndFields => true;

    /// <summary>Extraction materializes members, not types alone.</summary>
    public bool TypesOnly => false;

    /// <summary>
    /// The member-kind filter passed to Metadata selection. Always
    /// <see langword="null"/>: Research never narrows Metadata's candidate set
    /// by member kind.
    /// </summary>
    public IReadOnlyCollection<string>? KindFilter => null;
}
