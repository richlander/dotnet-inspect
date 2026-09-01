using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>
/// Whether one admitted input inside one planned domain was evaluated.
/// </summary>
/// <remarks>
/// Domain-side planning is total: every admitted input of the scope's question
/// carries exactly one disposition inside exactly one domain, so a side with no
/// admitted input is distinguishable from a side whose input was deliberately
/// left unevaluated.
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c> gates that
/// totality.
/// </remarks>
public enum ResearchTargetDispositionKind
{
    /// <summary>One request and attempt were minted for this input.</summary>
    Requested,

    /// <summary>No request was minted for this input.</summary>
    NotRequested,
}

/// <summary>
/// Why one admitted input inside a planned domain was not evaluated.
/// </summary>
public enum ResearchTargetNotRequestedReason
{
    /// <summary>
    /// The scope's selection is an exact-address selection that designates
    /// another admitted input.
    /// </summary>
    ExactAddressDesignatesAnotherInput,
}

/// <summary>
/// The terminal arm one target attempt reached.
/// </summary>
/// <remarks>
/// <c>ResearchTargetAttempts_MapEveryMetadataDiagnosticKind</c> derives its
/// expected mapping from <see cref="MemberTargetDiagnosticKind"/> onto this
/// declaration, so a missing or stale member fails that gate.
/// </remarks>
public enum ResearchTargetOutcomeKind
{
    /// <summary>Metadata selection succeeded and Research validated it.</summary>
    Resolved,

    /// <summary>
    /// The declaring type or the requested member is absent from this input.
    /// </summary>
    NotFound,

    /// <summary>Metadata selection was ambiguous.</summary>
    Ambiguous,

    /// <summary>The selector was invalid or unstable for this input.</summary>
    Rejected,

    /// <summary>The input cannot supply an implementation target.</summary>
    Unavailable,

    /// <summary>Research validation or resolution failed.</summary>
    Failed,
}

/// <summary>
/// Why one bounded Research-owned target diagnostic was issued.
/// </summary>
/// <remarks>
/// Every member maps to exactly one <see cref="ResearchTargetOutcomeKind"/>,
/// and the summary text is a fixed Research-owned sentence per member. No raw
/// exception message, borrowed path, rendered diagnostic row, or candidate
/// display string can reach it.
/// <c>ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation</c>
/// gates that boundary.
/// </remarks>
public enum ResearchTargetDiagnosticKind
{
    /// <summary>
    /// The declaring type has no type definition and no covering type
    /// forwarder in this input.
    /// </summary>
    DeclaringTypeAbsent,

    /// <summary>
    /// The declaring type has no type definition here, but a type forwarder
    /// exactly covers it, so absence is not provable from this input.
    /// </summary>
    DeclaringTypeForwarded,

    /// <summary>
    /// More than one retained type definition or forwarder has the exact
    /// requested metadata full name.
    /// </summary>
    DeclaringTypeAmbiguous,

    /// <summary>
    /// A Metadata inspection failure may cover the requested declaring type or
    /// member, so absence cannot be established from the partial surface.
    /// </summary>
    IncompleteMetadataSurface,

    /// <summary>
    /// The input is admitted for reference resolution only, so it was never
    /// opened.
    /// </summary>
    ReferenceOnlyInput,

    /// <summary>
    /// The domain holds more than one admitted input on one side, so no
    /// unambiguous pairing exists.
    /// </summary>
    DomainAmbiguous,

    /// <summary>
    /// The live image, the acquisition descriptor, and the Analysis body index
    /// do not name the same assembly.
    /// </summary>
    AssemblyIdentityMismatch,

    /// <summary>
    /// The live image, artifact-bound acquisition descriptor, and Analysis
    /// body index do not name the same module generation.
    /// </summary>
    ModuleIdentityMismatch,

    /// <summary>
    /// The image is a standalone managed module, so it has no assembly
    /// identity to validate against.
    /// </summary>
    StandaloneModule,

    /// <summary>
    /// The resolved body token is not a MethodDef, or its row is outside the
    /// module's MethodDef table.
    /// </summary>
    InvalidMethodDefinitionToken,

    /// <summary>
    /// The derived durable address does not equal the asserted exact address.
    /// </summary>
    AddressEvidenceMismatch,

    /// <summary>
    /// The derived relationship role does not equal the asserted role, or the
    /// resolved body token matches no physical member or accessor.
    /// </summary>
    RelationshipRoleEvidenceMismatch,

    /// <summary>The borrowed input could not be opened or read.</summary>
    InputUnreadable,

    /// <summary>Research target resolution failed for this input.</summary>
    ResolutionFailed,
}

/// <summary>
/// One bounded Research-owned target diagnostic.
/// </summary>
/// <remarks>
/// The summary is derived only from <see cref="Kind"/>. It never carries an
/// exception message, stack trace, borrowed path, or rendered presentation row.
/// </remarks>
public sealed class ResearchTargetDiagnostic
{
    internal ResearchTargetDiagnostic(ResearchTargetDiagnosticKind kind)
    {
        Kind = kind;
        Summary = SummaryFor(kind);
    }

    /// <summary>Why this diagnostic was issued.</summary>
    public ResearchTargetDiagnosticKind Kind { get; }

    /// <summary>A bounded Research-owned summary.</summary>
    public string Summary { get; }

    static string SummaryFor(ResearchTargetDiagnosticKind kind)
        => kind switch
        {
            ResearchTargetDiagnosticKind.DeclaringTypeAbsent =>
                "The declaring type is absent from this admitted input.",
            ResearchTargetDiagnosticKind.DeclaringTypeForwarded =>
                "The declaring type is forwarded away from this admitted input.",
            ResearchTargetDiagnosticKind.DeclaringTypeAmbiguous =>
                "More than one declaration has the requested metadata full name.",
            ResearchTargetDiagnosticKind.IncompleteMetadataSurface =>
                "The Metadata surface is incomplete for the requested target.",
            ResearchTargetDiagnosticKind.ReferenceOnlyInput =>
                "This admitted input is reference-only and supplies no implementation target.",
            ResearchTargetDiagnosticKind.DomainAmbiguous =>
                "This domain admits more than one input on one side.",
            ResearchTargetDiagnosticKind.AssemblyIdentityMismatch =>
                "The live image, descriptor, and body index name different assemblies.",
            ResearchTargetDiagnosticKind.ModuleIdentityMismatch =>
                "The live image and the body index name different modules.",
            ResearchTargetDiagnosticKind.StandaloneModule =>
                "The image is a standalone managed module with no assembly identity.",
            ResearchTargetDiagnosticKind.InvalidMethodDefinitionToken =>
                "The resolved body token is not an in-range MethodDef of this module.",
            ResearchTargetDiagnosticKind.AddressEvidenceMismatch =>
                "The derived durable address does not equal the asserted address.",
            ResearchTargetDiagnosticKind.RelationshipRoleEvidenceMismatch =>
                "The derived relationship role does not match the selected member.",
            ResearchTargetDiagnosticKind.InputUnreadable =>
                "The admitted input could not be opened or read.",
            ResearchTargetDiagnosticKind.ResolutionFailed =>
                "Research target resolution failed for this admitted input.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}

/// <summary>
/// Exactly one terminal outcome of one target attempt.
/// </summary>
/// <remarks>
/// The hierarchy is closed. An exception, diagnostic message, candidate
/// display string, or empty result never substitutes for one of these arms,
/// and expected resolution outcomes do not throw.
/// <c>ResearchTargetAttempts_MapEveryMetadataDiagnosticKind</c>,
/// <c>ResearchTargetResolution_PreservesMetadataDiagnosticsAndAccessorRoles</c>,
/// and <c>ResearchTargetInputValidation_RejectsMismatchedModuleEvidence</c>
/// gate the arms an admitted input can reach, and
/// <c>ResearchTargetResolutionValidator</c> re-runs the arm-to-evidence
/// binding at construction.
/// </remarks>
public abstract class ResearchTargetOutcome
{
    private protected ResearchTargetOutcome(ResearchTargetOutcomeKind kind)
        => Kind = kind;

    /// <summary>The terminal arm this outcome occupies.</summary>
    public ResearchTargetOutcomeKind Kind { get; }

    /// <summary>
    /// Metadata selection succeeded, and Research validated that the selected
    /// target and durable address belong to the same admitted assembly and
    /// module.
    /// </summary>
    public sealed class Resolved : ResearchTargetOutcome
    {
        internal Resolved(
            ResolvedMemberTarget target,
            MetadataMethodAddress? address,
            ResearchTargetRelationshipRole role,
            LibraryBodyModuleIdentity module,
            ImmutableArray<MemberTargetCandidate> candidates,
            ResearchTargetBodyIdentity? bodyIdentity = null)
            : base(ResearchTargetOutcomeKind.Resolved)
        {
            Target = target;
            Address = address;
            Role = role;
            Module = module;
            Candidates = candidates;
            BodyIdentity = bodyIdentity;
        }

        /// <summary>The exact Metadata-issued resolved target.</summary>
        public ResolvedMemberTarget Target { get; }

        /// <summary>The exact Metadata-issued anchor of that target.</summary>
        public MemberAnchor Anchor => Target.Anchor;

        /// <summary>
        /// The durable method address when the selected member has a physical
        /// method relationship, otherwise <see langword="null"/>.
        /// </summary>
        public MetadataMethodAddress? Address { get; }

        /// <summary>
        /// The relationship role derived only after successful Metadata
        /// selection.
        /// </summary>
        public ResearchTargetRelationshipRole Role { get; }

        /// <summary>
        /// The exact Analysis-issued module identity of the image this target
        /// was selected from.
        /// </summary>
        public LibraryBodyModuleIdentity Module { get; }

        /// <summary>The exact Metadata-issued candidate set.</summary>
        public ImmutableArray<MemberTargetCandidate> Candidates { get; }

        /// <summary>
        /// The Analysis-issued structured physical body identity. Null exactly
        /// when <see cref="Role"/> is <see cref="ResearchTargetRelationshipRole.None"/>.
        /// </summary>
        public ResearchTargetBodyIdentity? BodyIdentity { get; }
    }

    /// <summary>
    /// The declaring type or the requested member is absent from this input.
    /// This is an input-local fact, not semantic absence.
    /// </summary>
    public sealed class NotFound : ResearchTargetOutcome
    {
        internal NotFound(
            MemberTargetDiagnostic? metadataDiagnostic,
            ResearchTargetDiagnostic? researchDiagnostic,
            ImmutableArray<MemberTargetCandidate> candidates)
            : base(ResearchTargetOutcomeKind.NotFound)
        {
            MetadataDiagnostic = metadataDiagnostic;
            ResearchDiagnostic = researchDiagnostic;
            Candidates = candidates;
        }

        /// <summary>
        /// The exact Metadata-owned <c>MissingMember</c> or
        /// <c>DigestNotFound</c> diagnostic, or <see langword="null"/> when the
        /// declaring type itself is absent.
        /// </summary>
        public MemberTargetDiagnostic? MetadataDiagnostic { get; }

        /// <summary>
        /// The bounded Research-owned <c>DeclaringTypeAbsent</c> diagnostic, or
        /// <see langword="null"/> when Metadata issued the diagnostic.
        /// </summary>
        public ResearchTargetDiagnostic? ResearchDiagnostic { get; }

        /// <summary>The exact Metadata-issued candidate set.</summary>
        public ImmutableArray<MemberTargetCandidate> Candidates { get; }
    }

    /// <summary>Metadata selection was ambiguous.</summary>
    public sealed class Ambiguous : ResearchTargetOutcome
    {
        internal Ambiguous(
            MemberTargetDiagnostic diagnostic,
            ImmutableArray<MemberTargetCandidate> candidates)
            : base(ResearchTargetOutcomeKind.Ambiguous)
        {
            Diagnostic = diagnostic;
            Candidates = candidates;
        }

        /// <summary>The exact Metadata-owned ambiguity diagnostic.</summary>
        public MemberTargetDiagnostic Diagnostic { get; }

        /// <summary>The exact Metadata-issued candidate set.</summary>
        public ImmutableArray<MemberTargetCandidate> Candidates { get; }
    }

    /// <summary>The selector was invalid or unstable for this input.</summary>
    public sealed class Rejected : ResearchTargetOutcome
    {
        internal Rejected(
            MemberTargetDiagnostic diagnostic,
            ImmutableArray<MemberTargetCandidate> candidates)
            : base(ResearchTargetOutcomeKind.Rejected)
        {
            Diagnostic = diagnostic;
            Candidates = candidates;
        }

        /// <summary>
        /// The exact Metadata-owned <c>ConflictingSelectors</c> or
        /// <c>OverloadOutOfRange</c> diagnostic.
        /// </summary>
        public MemberTargetDiagnostic Diagnostic { get; }

        /// <summary>The exact Metadata-issued candidate set.</summary>
        public ImmutableArray<MemberTargetCandidate> Candidates { get; }
    }

    /// <summary>
    /// The admitted input cannot supply an implementation target.
    /// </summary>
    public sealed class Unavailable : ResearchTargetOutcome
    {
        internal Unavailable(ResearchTargetDiagnostic diagnostic)
            : base(ResearchTargetOutcomeKind.Unavailable)
            => Diagnostic = diagnostic;

        /// <summary>The bounded Research-owned diagnostic.</summary>
        public ResearchTargetDiagnostic Diagnostic { get; }
    }

    /// <summary>Research validation or resolution failed.</summary>
    public sealed class Failed : ResearchTargetOutcome
    {
        internal Failed(ResearchTargetDiagnostic diagnostic)
            : base(ResearchTargetOutcomeKind.Failed)
            => Diagnostic = diagnostic;

        /// <summary>The bounded Research-owned diagnostic.</summary>
        public ResearchTargetDiagnostic Diagnostic { get; }
    }
}

/// <summary>
/// One owner-issued side-local target request.
/// </summary>
/// <remarks>
/// The request retains only Research identities, side, scope, domain, the exact
/// typed selection intent, the pinned surface scope, the request kind, and the
/// optional asserted exact-address evidence. It retains no
/// <see cref="ResearchAdmittedInput"/>, occurrence, acquisition descriptor,
/// reference resolver, or body index.
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c> and
/// <c>ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation</c>
/// gate those properties.
/// </remarks>
public sealed class ResearchTargetRequest
{
    internal ResearchTargetRequest(
        ResearchTargetRequestId id,
        string declaringTypeFullName,
        MemberTargetSelector selector,
        ResearchTargetRequestKind kind,
        MetadataMethodAddress? assertedAddress,
        ResearchTargetRelationshipRole? assertedRole)
    {
        Id = id;
        DeclaringTypeFullName = declaringTypeFullName;
        Selector = selector;
        Kind = kind;
        AssertedAddress = assertedAddress;
        AssertedRole = assertedRole;
    }

    /// <summary>The owner-issued request identity.</summary>
    public ResearchTargetRequestId Id { get; }

    /// <summary>The operation that parents this request.</summary>
    public ResearchComparisonOperationId Operation => Id.Operation;

    /// <summary>The question that parents this request.</summary>
    public ResearchComparisonQuestionId Question => Id.Question;

    /// <summary>The scope that parents this request.</summary>
    public ResearchTargetScopeId Scope => Id.Scope;

    /// <summary>The domain that parents this request.</summary>
    public ResearchTargetDomainId Domain => Id.Domain;

    /// <summary>The side-local admitted input this request evaluates.</summary>
    public ResearchComparisonInputId Input => Id.Input;

    /// <summary>The side this request occupies.</summary>
    public ResearchComparisonSide Side => Id.Side;

    /// <summary>The exact declaring-type full-name intent.</summary>
    public string DeclaringTypeFullName { get; }

    /// <summary>The exact typed Metadata selector.</summary>
    public MemberTargetSelector Selector { get; }

    /// <summary>The pinned API-surface scope this request evaluates.</summary>
    public ResearchTargetSurfaceScope Surface
        => ResearchTargetSurfaceScope.MetadataApiSurface;

    /// <summary>Whether this request is carried or exact-address.</summary>
    public ResearchTargetRequestKind Kind { get; }

    /// <summary>
    /// The asserted durable address for an exact-address request, otherwise
    /// <see langword="null"/>.
    /// </summary>
    public MetadataMethodAddress? AssertedAddress { get; }

    /// <summary>
    /// The asserted relationship role for an exact-address request, otherwise
    /// <see langword="null"/>. A carried request never asserts a role, because
    /// the role exists only after Metadata selection.
    /// </summary>
    public ResearchTargetRelationshipRole? AssertedRole { get; }
}

/// <summary>
/// One attempt at exactly one request, and its single terminal outcome.
/// </summary>
public sealed class ResearchTargetAttempt
{
    internal ResearchTargetAttempt(
        ResearchTargetAttemptId id,
        ResearchTargetRequest request,
        ResearchTargetOutcome outcome)
    {
        Id = id;
        Request = request;
        Outcome = outcome;
    }

    /// <summary>The owner-issued attempt identity.</summary>
    public ResearchTargetAttemptId Id { get; }

    /// <summary>The request this attempt evaluated.</summary>
    public ResearchTargetRequest Request { get; }

    /// <summary>The single terminal outcome.</summary>
    public ResearchTargetOutcome Outcome { get; }
}

/// <summary>
/// The closed planning disposition of one admitted input inside one domain.
/// </summary>
/// <remarks>
/// This is inert planning evidence. It names the side-local Research input
/// identity, the assigned role, and whether a request was minted. It does not
/// name the admitted input object, its occurrence, or its borrowed values.
/// <c>ResearchTargetRequests_AreStrictlySideInputAndScopeLocal</c> gates the
/// totality of the disposition set, and
/// <c>ResearchTargetAttempt_AddressEvidenceMismatchBlocksBeforeCensus</c>
/// gates the unevaluated case.
/// </remarks>
public sealed class ResearchTargetInputDisposition
{
    internal ResearchTargetInputDisposition(
        ResearchComparisonInputId input,
        ResearchTargetInputRole role,
        ResearchTargetDispositionKind kind,
        ResearchTargetNotRequestedReason? notRequestedReason,
        ResearchTargetRequestId? request)
    {
        Input = input;
        Role = role;
        Kind = kind;
        NotRequestedReason = notRequestedReason;
        Request = request;
    }

    /// <summary>The side-local admitted input identity.</summary>
    public ResearchComparisonInputId Input { get; }

    /// <summary>The side this input occupies.</summary>
    public ResearchComparisonSide Side => Input.Side;

    /// <summary>The Research-owned role assigned to this input.</summary>
    public ResearchTargetInputRole Role { get; }

    /// <summary>Whether a request was minted for this input.</summary>
    public ResearchTargetDispositionKind Kind { get; }

    /// <summary>
    /// Why no request was minted, or <see langword="null"/> when one was.
    /// </summary>
    public ResearchTargetNotRequestedReason? NotRequestedReason { get; }

    /// <summary>
    /// The minted request, or <see langword="null"/> when none was minted.
    /// </summary>
    public ResearchTargetRequestId? Request { get; }
}

/// <summary>
/// One planned logical assembly comparison domain inside one scope.
/// </summary>
/// <remarks>
/// Domain-side planning is total, so <see cref="Inputs"/> accounts for every
/// admitted input of the scope's question whose version-erased identity
/// matches <see cref="Key"/>. Duplicate same-side candidates set
/// <see cref="IsAmbiguous"/> and retain the complete conflicting input-ID set;
/// every request in that domain terminates <c>Unavailable</c> with
/// <c>DomainAmbiguous</c>, and no other domain is affected.
/// <c>ResearchTargetDomains_RejectDuplicateSameSideCandidates</c> and
/// <c>ResearchTargetDomains_BlockOnlyTheirOwnCensus</c> gate that.
/// </remarks>
public sealed class ResearchTargetDomain
{
    internal ResearchTargetDomain(
        ResearchTargetDomainId id,
        ResearchTargetDomainKey key,
        ImmutableArray<ResearchTargetInputDisposition> inputs,
        ImmutableArray<ResearchComparisonInputId> conflictingInputs,
        ImmutableArray<ResearchTargetRequest> requests,
        ImmutableArray<ResearchTargetAttempt> attempts)
    {
        Id = id;
        Key = key;
        Inputs = inputs;
        ConflictingInputs = conflictingInputs;
        Requests = requests;
        Attempts = attempts;
    }

    /// <summary>The owner-issued domain identity.</summary>
    public ResearchTargetDomainId Id { get; }

    /// <summary>The scope that parents this domain.</summary>
    public ResearchTargetScopeId Scope => Id.Scope;

    /// <summary>The version-erased Metadata-equivalent domain key.</summary>
    public ResearchTargetDomainKey Key { get; }

    /// <summary>
    /// The closed disposition of every admitted input in this domain.
    /// </summary>
    public ImmutableArray<ResearchTargetInputDisposition> Inputs { get; }

    /// <summary>
    /// Whether more than one admitted input occupies one side of this domain.
    /// </summary>
    public bool IsAmbiguous => !ConflictingInputs.IsEmpty;

    /// <summary>
    /// The complete conflicting input-ID set when this domain is ambiguous,
    /// otherwise empty.
    /// </summary>
    public ImmutableArray<ResearchComparisonInputId> ConflictingInputs { get; }

    /// <summary>The requests minted in this domain.</summary>
    public ImmutableArray<ResearchTargetRequest> Requests { get; }

    /// <summary>The attempts made in this domain.</summary>
    public ImmutableArray<ResearchTargetAttempt> Attempts { get; }

    /// <summary>The dispositions on one side of this domain.</summary>
    public ImmutableArray<ResearchTargetInputDisposition> Side(
        ResearchComparisonSide side)
        => [.. Inputs.Where(input => input.Side == side)];
}

/// <summary>
/// One planned target scope: exactly one member-selection occurrence and the
/// domains its question's admitted inputs occupy.
/// </summary>
public sealed class ResearchTargetScope
{
    internal ResearchTargetScope(
        ResearchTargetScopeId id,
        string declaringTypeFullName,
        MemberTargetSelector selector,
        ResearchTargetRequestKind kind,
        ImmutableArray<ResearchTargetDomain> domains)
    {
        Id = id;
        DeclaringTypeFullName = declaringTypeFullName;
        Selector = selector;
        Kind = kind;
        Domains = domains;
    }

    /// <summary>The owner-issued scope identity.</summary>
    public ResearchTargetScopeId Id { get; }

    /// <summary>The question that parents this scope.</summary>
    public ResearchComparisonQuestionId Question => Id.Question;

    /// <summary>The exact declaring-type full-name intent.</summary>
    public string DeclaringTypeFullName { get; }

    /// <summary>The exact typed Metadata selector.</summary>
    public MemberTargetSelector Selector { get; }

    /// <summary>Whether this scope is carried or exact-address.</summary>
    public ResearchTargetRequestKind Kind { get; }

    /// <summary>The domains planned inside this scope.</summary>
    public ImmutableArray<ResearchTargetDomain> Domains { get; }
}

/// <summary>
/// The complete, inert result of one Research target-planning invocation.
/// </summary>
/// <remarks>
/// The result retains opaque Research identities, side, relationship role,
/// exact owner-issued Metadata target, anchor, candidate, and diagnostic
/// values, durable metadata addresses, exact Analysis module identities, and
/// bounded Research diagnostics. It retains no admitted population, selection
/// occurrence, acquisition descriptor, reference resolver, body index, metadata
/// reader, PE reader, stream, callback, lease, raw exception, producer,
/// presentation row, or mutable caller collection.
/// <c>ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation</c>
/// gates that boundary, and
/// <c>ResearchTargetAttempts_AccountForEveryRequestExactlyOnce</c> gates the
/// request-to-attempt bijection.
/// </remarks>
public sealed class ResearchTargetResolution
{
    readonly ImmutableDictionary<ResearchTargetRequestId, ResearchTargetAttempt>
        _byRequest;

    internal ResearchTargetResolution(
        ResearchComparisonOperationId operation,
        ImmutableArray<ResearchTargetScope> scopes)
        : this(
            operation,
            scopes,
            ResearchTargetCorrespondenceBuilder.Build(scopes))
    {
    }

    ResearchTargetResolution(
        ResearchComparisonOperationId operation,
        ImmutableArray<ResearchTargetScope> scopes,
        ResearchTargetCorrespondenceProjection correspondence)
        : this(
            operation,
            scopes,
            correspondence.Censuses,
            correspondence.Outcomes)
    {
    }

    internal ResearchTargetResolution(
        ResearchComparisonOperationId operation,
        ImmutableArray<ResearchTargetScope> scopes,
        ImmutableArray<ResearchTargetDomainSideCensus> censuses,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences)
    {
        Operation = operation;
        Scopes = scopes;
        Censuses = censuses;
        Correspondences = correspondences;
        Domains = [.. scopes.SelectMany(static scope => scope.Domains)];
        Requests = [.. Domains.SelectMany(static domain => domain.Requests)];
        Attempts = [.. Domains.SelectMany(static domain => domain.Attempts)];
        _byRequest = Attempts.ToImmutableDictionary(
            static attempt => attempt.Request.Id,
            static attempt => attempt,
            (IEqualityComparer<ResearchTargetRequestId>)
                ReferenceEqualityComparer.Instance);
    }

    /// <summary>The admitted operation this resolution accounts for.</summary>
    public ResearchComparisonOperationId Operation { get; }

    /// <summary>The planned scopes, in selection-occurrence order.</summary>
    public ImmutableArray<ResearchTargetScope> Scopes { get; }

    /// <summary>Every planned domain across every scope.</summary>
    public ImmutableArray<ResearchTargetDomain> Domains { get; }

    /// <summary>Every minted request across every domain.</summary>
    public ImmutableArray<ResearchTargetRequest> Requests { get; }

    /// <summary>Every terminal attempt across every domain.</summary>
    public ImmutableArray<ResearchTargetAttempt> Attempts { get; }

    /// <summary>Every complete domain-side census.</summary>
    public ImmutableArray<ResearchTargetDomainSideCensus> Censuses { get; }

    /// <summary>Every closed domain-local correspondence outcome.</summary>
    public ImmutableArray<ResearchTargetCorrespondenceOutcome> Correspondences
    {
        get;
    }

    /// <summary>
    /// The attempt made for one exact request. Association is by request
    /// reference identity.
    /// </summary>
    public bool TryGetAttempt(
        ResearchTargetRequestId request,
        [MaybeNullWhen(false)] out ResearchTargetAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _byRequest.TryGetValue(request, out attempt);
    }

    /// <summary>The attempt made for one exact request.</summary>
    /// <exception cref="ArgumentException">
    /// The request does not belong to this resolution.
    /// </exception>
    public ResearchTargetAttempt GetAttempt(ResearchTargetRequestId request)
        => TryGetAttempt(request, out ResearchTargetAttempt? attempt)
            ? attempt
            : throw new ArgumentException(
                "The request does not belong to this resolution.",
                nameof(request));
}
