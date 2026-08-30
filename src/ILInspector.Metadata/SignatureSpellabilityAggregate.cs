using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>The metadata table that owns a signature-bearing member.</summary>
public enum SignatureSpellabilityMemberKind
{
    Field,
    Property,
    Method,
}

/// <summary>
/// Catalog-minted coordinates for one signature in one exact registered source
/// candidate.
/// </summary>
/// <remarks>
/// Only the minting catalog can construct a subject, and the subject carries
/// the acquisition candidate itself rather than a substitutable module
/// identity. Two registrations that publish the same MVID remain distinct
/// subjects, so a subject minted for one registration can never be planned
/// against another. Gated by
/// <c>SignatureSpellability_BindsSubjectToSourceModule</c> and
/// <c>SignatureSpellability_BindsSubjectToExactRegistration</c>.
/// </remarks>
public sealed class SignatureSpellabilitySubject
{
    internal SignatureSpellabilitySubject(
        AssemblyCatalogId catalog,
        ResolvedAssemblyCandidate sourceCandidate,
        Guid sourceModuleVersionId,
        AssemblyResolutionScope authorizedScope,
        int declaringTypeToken,
        int memberToken,
        SignatureSpellabilityMemberKind memberKind)
    {
        Catalog = catalog;
        SourceCandidate = sourceCandidate;
        SourceModuleVersionId = sourceModuleVersionId;
        AuthorizedScope = authorizedScope;
        DeclaringTypeToken = declaringTypeToken;
        MemberToken = memberToken;
        MemberKind = memberKind;
    }

    /// <summary>The catalog that minted and owns this subject.</summary>
    public AssemblyCatalogId Catalog { get; }

    /// <summary>The exact registered source descriptor.</summary>
    public ResolvedAssemblyReference Source => SourceCandidate.Assembly;

    /// <summary>The MVID observed in the bound candidate's retained image.</summary>
    public Guid SourceModuleVersionId { get; }

    /// <summary>
    /// The scope the source candidate is already authorized under. Planning
    /// may tighten this, never loosen it.
    /// </summary>
    public AssemblyResolutionScope AuthorizedScope { get; }

    public int DeclaringTypeToken { get; }
    public int MemberToken { get; }
    public SignatureSpellabilityMemberKind MemberKind { get; }

    internal ResolvedAssemblyCandidate SourceCandidate { get; }
}

/// <summary>The role of one named type occurrence in a signature.</summary>
public enum SignatureSpellabilityOccurrenceRole
{
    Ordinary,
    RequiredModifier,
    OptionalModifier,
}

/// <summary>One named type occurrence retained in stable signature order.</summary>
public sealed class SignatureSpellabilityOccurrence
{
    internal SignatureSpellabilityOccurrence(
        int index,
        MetadataNamedTypeReference reference,
        SignatureSpellabilityOccurrenceRole role,
        bool accessibilityParticipates,
        TypeResolutionRequest? request)
    {
        Index = index;
        Reference = reference;
        Role = role;
        AccessibilityParticipates = accessibilityParticipates;
        Request = request;
    }

    public int Index { get; }
    public MetadataNamedTypeReference Reference { get; }
    public SignatureSpellabilityOccurrenceRole Role { get; }
    public bool AccessibilityParticipates { get; }
    public TypeResolutionRequest? Request { get; }
}

/// <summary>One deduplicated request in an immutable signature plan.</summary>
public sealed class SignatureSpellabilityPlannedRequest
{
    internal SignatureSpellabilityPlannedRequest(
        TypeResolutionRequest request,
        bool accessibilityParticipates)
    {
        Request = request;
        AccessibilityParticipates = accessibilityParticipates;
    }

    public TypeResolutionRequest Request { get; }
    public bool AccessibilityParticipates { get; }
}

/// <summary>
/// Reader-independent output of one successful, source-bound signature decode.
/// </summary>
/// <remarks>
/// Gated by <c>SignatureSpellability_CollectsEveryNamedChildOnce</c>.
/// </remarks>
public sealed class SignatureSpellabilityPlan
{
    internal SignatureSpellabilityPlan(
        AssemblyCatalogId catalog,
        SignatureSpellabilitySubject subject,
        AssemblyResolutionScope sourceScope,
        ImmutableArray<SignatureSpellabilityOccurrence> occurrences,
        ImmutableArray<SignatureSpellabilityPlannedRequest> requests)
    {
        Catalog = catalog;
        Subject = subject;
        SourceScope = sourceScope;
        Occurrences = occurrences;
        Requests = requests;
    }

    public AssemblyCatalogId Catalog { get; }
    public ResolvedAssemblyReference Source => Subject.Source;
    public SignatureSpellabilitySubject Subject { get; }

    /// <summary>
    /// The scope every planned request was derived under. It is never looser
    /// than <see cref="SignatureSpellabilitySubject.AuthorizedScope"/>.
    /// </summary>
    public AssemblyResolutionScope SourceScope { get; }
    public ImmutableArray<SignatureSpellabilityOccurrence> Occurrences
        { get; }
    public ImmutableArray<SignatureSpellabilityPlannedRequest> Requests
        { get; }

    internal ResolvedAssemblyCandidate SourceCandidate =>
        Subject.SourceCandidate;
}

/// <summary>Closed source-binding or signature-decode rejection.</summary>
public abstract class SignatureSpellabilityPlanFailure
{
    private protected SignatureSpellabilityPlanFailure()
    {
    }

    public sealed class CandidateRejected : SignatureSpellabilityPlanFailure
    {
        internal CandidateRejected(CandidateOpenFailure failure) =>
            Failure = failure;

        public CandidateOpenFailure Failure { get; }
    }

    public sealed class SourceModuleMismatch : SignatureSpellabilityPlanFailure
    {
        internal SourceModuleMismatch(Guid expected, Guid actual)
        {
            Expected = expected;
            Actual = actual;
        }

        public Guid Expected { get; }
        public Guid Actual { get; }
    }

    public sealed class InvalidDeclaringType : SignatureSpellabilityPlanFailure
    {
        internal InvalidDeclaringType(int token) => Token = token;
        public int Token { get; }
    }

    public sealed class InvalidMember : SignatureSpellabilityPlanFailure
    {
        internal InvalidMember(
            int token,
            SignatureSpellabilityMemberKind expectedKind)
        {
            Token = token;
            ExpectedKind = expectedKind;
        }

        public int Token { get; }
        public SignatureSpellabilityMemberKind ExpectedKind { get; }
    }

    public sealed class DeclaringTypeMismatch
        : SignatureSpellabilityPlanFailure
    {
        internal DeclaringTypeMismatch(int declaringTypeToken, int memberToken)
        {
            DeclaringTypeToken = declaringTypeToken;
            MemberToken = memberToken;
        }

        public int DeclaringTypeToken { get; }
        public int MemberToken { get; }
    }

    public sealed class SignatureRejected : SignatureSpellabilityPlanFailure
    {
        internal SignatureRejected()
        {
        }
    }
}

/// <summary>Closed result of catalog-owned signature planning.</summary>
public abstract class SignatureSpellabilityPlanOutcome
{
    private protected SignatureSpellabilityPlanOutcome()
    {
    }

    public sealed class Planned : SignatureSpellabilityPlanOutcome
    {
        internal Planned(SignatureSpellabilityPlan plan) => Plan = plan;
        public SignatureSpellabilityPlan Plan { get; }
    }

    public sealed class Rejected : SignatureSpellabilityPlanOutcome
    {
        internal Rejected(SignatureSpellabilityPlanFailure failure) =>
            Failure = failure;

        public SignatureSpellabilityPlanFailure Failure { get; }
    }
}

/// <summary>Closed result of minting one acquisition-bound subject.</summary>
public abstract class SignatureSpellabilitySubjectOutcome
{
    private protected SignatureSpellabilitySubjectOutcome()
    {
    }

    public sealed class Created : SignatureSpellabilitySubjectOutcome
    {
        internal Created(SignatureSpellabilitySubject subject) =>
            Subject = subject;

        public SignatureSpellabilitySubject Subject { get; }
    }

    public sealed class Rejected : SignatureSpellabilitySubjectOutcome
    {
        internal Rejected(SignatureSpellabilityPlanFailure failure) =>
            Failure = failure;

        public SignatureSpellabilityPlanFailure Failure { get; }
    }
}

/// <summary>
/// Closed reason why terminal TypeDef accessibility could not be classified.
/// </summary>
public abstract class TypeDefinitionAccessibilityFailure
{
    private protected TypeDefinitionAccessibilityFailure()
    {
    }

    public sealed class IncomparableCatalog
        : TypeDefinitionAccessibilityFailure
    {
        internal IncomparableCatalog(
            AssemblyCatalogId expected,
            AssemblyCatalogId actual)
        {
            Expected = expected;
            Actual = actual;
        }

        public AssemblyCatalogId Expected { get; }
        public AssemblyCatalogId Actual { get; }
    }

    public sealed class StaleGeneration : TypeDefinitionAccessibilityFailure
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId keyGeneration,
            AssemblyCatalogGenerationId? currentGeneration)
        {
            KeyGeneration = keyGeneration;
            CurrentGeneration = currentGeneration;
        }

        public AssemblyCatalogGenerationId KeyGeneration { get; }
        public AssemblyCatalogGenerationId? CurrentGeneration { get; }
    }

    public sealed class CandidateUnavailable
        : TypeDefinitionAccessibilityFailure
    {
        internal CandidateUnavailable()
        {
        }
    }

    public sealed class CandidateOpenRejected
        : TypeDefinitionAccessibilityFailure
    {
        internal CandidateOpenRejected(CandidateOpenFailure failure) =>
            Failure = failure;

        public CandidateOpenFailure Failure { get; }
    }

    public sealed class InvalidDefinition
        : TypeDefinitionAccessibilityFailure
    {
        internal InvalidDefinition()
        {
        }
    }

    public sealed class InvalidDeclaringChain
        : TypeDefinitionAccessibilityFailure
    {
        internal InvalidDeclaringChain()
        {
        }
    }

    public sealed class CatalogUnavailable
        : TypeDefinitionAccessibilityFailure
    {
        internal CatalogUnavailable()
        {
        }
    }
}

/// <summary>Closed terminal TypeDef external-accessibility answer.</summary>
public abstract class TypeDefinitionAccessibilityOutcome
{
    private protected TypeDefinitionAccessibilityOutcome()
    {
    }

    public sealed class Accessible : TypeDefinitionAccessibilityOutcome
    {
        internal Accessible()
        {
        }
    }

    public sealed class Inaccessible : TypeDefinitionAccessibilityOutcome
    {
        internal Inaccessible()
        {
        }
    }

    public sealed class Rejected : TypeDefinitionAccessibilityOutcome
    {
        internal Rejected(TypeDefinitionAccessibilityFailure failure) =>
            Failure = failure;

        public TypeDefinitionAccessibilityFailure Failure { get; }
    }
}

/// <summary>One complete aggregate evidence entry.</summary>
public abstract class SignatureSpellabilityEvidence
{
    private protected SignatureSpellabilityEvidence()
    {
    }

    public sealed class IntrinsicPrimitive : SignatureSpellabilityEvidence
    {
        internal IntrinsicPrimitive(SignatureSpellabilityOccurrence occurrence)
            => Occurrence = occurrence;

        public SignatureSpellabilityOccurrence Occurrence { get; }
    }

    public sealed class LocalRequirement : SignatureSpellabilityEvidence
    {
        internal LocalRequirement(
            TypeResolutionRequest request,
            ResolvedTypeDefinition definition,
            bool accessibilityParticipates)
        {
            Request = request;
            Definition = definition;
            AccessibilityParticipates = accessibilityParticipates;
        }

        public TypeResolutionRequest Request { get; }
        public ResolvedTypeDefinition Definition { get; }
        public bool AccessibilityParticipates { get; }
    }

    public sealed class ExternalDefinition : SignatureSpellabilityEvidence
    {
        internal ExternalDefinition(
            TypeResolutionRequest request,
            ResolvedTypeDefinition definition,
            bool accessibilityParticipates,
            TypeDefinitionAccessibilityOutcome accessibility)
        {
            Request = request;
            Definition = definition;
            AccessibilityParticipates = accessibilityParticipates;
            Accessibility = accessibility;
        }

        public TypeResolutionRequest Request { get; }
        public ResolvedTypeDefinition Definition { get; }
        public bool AccessibilityParticipates { get; }
        public TypeDefinitionAccessibilityOutcome Accessibility { get; }
    }

    public sealed class Unresolved : SignatureSpellabilityEvidence
    {
        internal Unresolved(
            TypeResolutionRequest request,
            bool accessibilityParticipates,
            TypeResolutionOutcome outcome)
        {
            Request = request;
            AccessibilityParticipates = accessibilityParticipates;
            Outcome = outcome;
        }

        public TypeResolutionRequest Request { get; }
        public bool AccessibilityParticipates { get; }
        public TypeResolutionOutcome Outcome { get; }
    }
}

/// <summary>
/// Typed caller attestation that local definitions are included and nameable
/// in the generated artifact.
/// </summary>
/// <remarks>
/// Supply the exact opaque keys carried by the aggregate's
/// <see cref="SignatureSpellabilityEvidence.LocalRequirement"/> entries.
/// Callers do not compare or project these keys.
/// </remarks>
public sealed class SignatureLocalRequirementProof
{
    readonly HashSet<ResolvedTypeDefinitionKey> _definitions =
        new(ReferenceEqualityComparer.Instance);

    public SignatureLocalRequirementProof(
        IEnumerable<ResolvedTypeDefinitionKey> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (ResolvedTypeDefinitionKey definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            _definitions.Add(definition);
        }
    }

    internal bool Contains(ResolvedTypeDefinitionKey definition) =>
        _definitions.Contains(definition);
}

/// <summary>Closed evaluation rejection before complete evidence exists.</summary>
public abstract class SignatureSpellabilityEvaluationFailure
{
    private protected SignatureSpellabilityEvaluationFailure()
    {
    }

    public sealed class IncomparableCatalog
        : SignatureSpellabilityEvaluationFailure
    {
        internal IncomparableCatalog(
            AssemblyCatalogId expected,
            AssemblyCatalogId actual)
        {
            Expected = expected;
            Actual = actual;
        }

        public AssemblyCatalogId Expected { get; }
        public AssemblyCatalogId Actual { get; }
    }

    public sealed class SourceUnavailable
        : SignatureSpellabilityEvaluationFailure
    {
        internal SourceUnavailable()
        {
        }
    }

    public sealed class StaleSource : SignatureSpellabilityEvaluationFailure
    {
        internal StaleSource()
        {
        }
    }

    public sealed class PlanExpansionRequired
        : SignatureSpellabilityEvaluationFailure
    {
        internal PlanExpansionRequired(TypeResolutionOutcome.Rejected outcome)
            => Outcome = outcome;

        public TypeResolutionOutcome.Rejected Outcome { get; }
    }
}

/// <summary>Closed result of evaluating one immutable signature plan.</summary>
public abstract class SignatureSpellabilityAggregate
{
    private protected SignatureSpellabilityAggregate()
    {
    }

    public sealed class Complete : SignatureSpellabilityAggregate
    {
        internal Complete(
            ImmutableArray<SignatureSpellabilityEvidence> evidence) =>
            Evidence = evidence;

        public ImmutableArray<SignatureSpellabilityEvidence> Evidence
            { get; }

        /// <summary>
        /// Projects complete evidence into the legacy boolean question while
        /// requiring typed proof for every local definition.
        /// </summary>
        /// <remarks>
        /// Gated by
        /// <c>SignatureSpellability_RequiresLocalArtifactProof</c> and
        /// <c>SignatureSpellability_MergesModifierParticipation</c>.
        /// </remarks>
        public bool CanSpell(
            SignatureLocalRequirementProof? localProof = null)
        {
            foreach (SignatureSpellabilityEvidence entry in Evidence)
            {
                switch (entry)
                {
                    case SignatureSpellabilityEvidence.LocalRequirement local
                        when localProof is null
                            || !localProof.Contains(local.Definition.Key):
                        return false;
                    case SignatureSpellabilityEvidence.ExternalDefinition
                    {
                        AccessibilityParticipates: true,
                        Accessibility:
                            not TypeDefinitionAccessibilityOutcome.Accessible,
                    }:
                        return false;
                    case SignatureSpellabilityEvidence.Unresolved:
                        return false;
                }
            }

            return true;
        }
    }

    public sealed class Rejected : SignatureSpellabilityAggregate
    {
        internal Rejected(SignatureSpellabilityEvaluationFailure failure) =>
            Failure = failure;

        public SignatureSpellabilityEvaluationFailure Failure { get; }
    }
}

internal readonly record struct DefinitionAccessibilityCacheKey(
    AssemblyCandidateId Candidate,
    TypeDefinitionToken Definition);

internal abstract class SessionAccessibilityOutcome
{
    private protected SessionAccessibilityOutcome()
    {
    }

    internal sealed class Accessible : SessionAccessibilityOutcome;
    internal sealed class Inaccessible : SessionAccessibilityOutcome;
    internal sealed class InvalidDefinition : SessionAccessibilityOutcome;
    internal sealed class InvalidDeclaringChain : SessionAccessibilityOutcome;
}

internal static class SignatureSpellabilityPlanner
{
    internal static SignatureSpellabilityPlanOutcome Plan(
        AssemblyCatalogId catalog,
        SignatureSpellabilitySubject subject,
        AssemblyInspectionSession session,
        AssemblyResolutionScope requestedScope)
    {
        Guid actualMvid = session.ModuleVersionId();
        if (actualMvid != subject.SourceModuleVersionId)
        {
            return Rejected(
                new SignatureSpellabilityPlanFailure.SourceModuleMismatch(
                    subject.SourceModuleVersionId,
                    actualMvid));
        }

        // A caller may only add the platform constraint; the baseline the
        // source candidate was minted with can never be widened back to Any.
        AssemblyResolutionScope sourceScope =
            AssemblyResolutionScopes.Tighten(
                subject.AuthorizedScope,
                requestedScope);

        SignaturePlanDecodeOutcome decoded =
            session.DecodeSignatureSpellability(subject);
        if (decoded is SignaturePlanDecodeOutcome.Rejected rejected)
            return Rejected(rejected.Failure);

        ImmutableArray<RawOccurrence> raw =
            ((SignaturePlanDecodeOutcome.Decoded)decoded).Occurrences;
        var occurrences =
            ImmutableArray.CreateBuilder<SignatureSpellabilityOccurrence>(
                raw.Length);
        var requestOrder = new List<TypeResolutionRequest>();
        var participation = new Dictionary<TypeResolutionRequest, bool>(
            TypeResolutionRequestComparer.Instance);
        for (int index = 0; index < raw.Length; index++)
        {
            RawOccurrence occurrence = raw[index];
            TypeResolutionRequest? request = Request(
                subject.Source,
                sourceScope,
                occurrence.Reference);
            bool participates =
                occurrence.Role
                    is SignatureSpellabilityOccurrenceRole.Ordinary
                        or SignatureSpellabilityOccurrenceRole.RequiredModifier;
            occurrences.Add(
                new SignatureSpellabilityOccurrence(
                    index,
                    occurrence.Reference,
                    occurrence.Role,
                    participates,
                    request));
            if (request is null)
                continue;

            if (participation.TryGetValue(request, out bool existing))
            {
                participation[request] = existing || participates;
            }
            else
            {
                participation.Add(request, participates);
                requestOrder.Add(request);
            }
        }

        var requests =
            ImmutableArray.CreateBuilder<SignatureSpellabilityPlannedRequest>(
                requestOrder.Count);
        foreach (TypeResolutionRequest request in requestOrder)
        {
            requests.Add(
                new SignatureSpellabilityPlannedRequest(
                    request,
                    participation[request]));
        }

        return new SignatureSpellabilityPlanOutcome.Planned(
            new SignatureSpellabilityPlan(
                catalog,
                subject,
                sourceScope,
                occurrences.ToImmutable(),
                requests.ToImmutable()));
    }

    static TypeResolutionRequest? Request(
        ResolvedAssemblyReference source,
        AssemblyResolutionScope sourceScope,
        MetadataNamedTypeReference reference) =>
        reference.Scope switch
        {
            MetadataTypeReferenceScope.CurrentAssembly =>
                TypeResolutionRequest.FromAssembly(
                    source,
                    sourceScope,
                    reference.Type),
            MetadataTypeReferenceScope.AssemblyReference assembly =>
                TypeResolutionRequest.FromReference(
                    assembly.Assembly,
                    AssemblyBindingOrigin.FromAssembly(source),
                    AssemblyResolutionScopes.Tighten(
                        sourceScope,
                        assembly.Assembly),
                    reference.Type),
            MetadataTypeReferenceScope.IntrinsicCoreLibrary => null,
            MetadataTypeReferenceScope.ModuleReference module =>
                TypeResolutionRequest.FromModule(
                    source,
                    module.Name,
                    reference.Type),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-reference scope."),
        };

    static SignatureSpellabilityPlanOutcome.Rejected Rejected(
        SignatureSpellabilityPlanFailure failure) =>
        new(failure);
}

internal static class SignatureSpellabilityEvaluator
{
    /// <summary>
    /// Evaluates one plan while the caller holds a generation lease, so source
    /// currency, every resolution, and every accessibility classification
    /// observe one generation.
    /// </summary>
    /// <remarks>
    /// Gated by
    /// <c>SignatureSpellability_HoldsOneGenerationAcrossEvaluation</c>.
    /// </remarks>
    internal static SignatureSpellabilityAggregate Evaluate(
        TypeResolutionContext context,
        SignatureSpellabilityPlan plan)
    {
        switch (context.SourceStatus(plan.SourceCandidate))
        {
            case SignaturePlanSourceStatus.Current:
                break;
            case SignaturePlanSourceStatus.Stale:
                return new SignatureSpellabilityAggregate.Rejected(
                    new SignatureSpellabilityEvaluationFailure.StaleSource());
            case SignaturePlanSourceStatus.Unavailable:
                return new SignatureSpellabilityAggregate.Rejected(
                    new SignatureSpellabilityEvaluationFailure
                        .SourceUnavailable());
            default:
                throw new InvalidOperationException(
                    "Unknown signature plan source status.");
        }

        var outcomes = new Dictionary<
            TypeResolutionRequest,
            TypeResolutionOutcome>(
                TypeResolutionRequestComparer.Instance);
        foreach (SignatureSpellabilityPlannedRequest planned in plan.Requests)
        {
            TypeResolutionOutcome outcome =
                context.Resolve(planned.Request);
            if (outcome is TypeResolutionOutcome.Rejected
                {
                    Failure:
                        TypeResolutionFailure.PlanExpansionRequired,
                } expansion)
            {
                return new SignatureSpellabilityAggregate.Rejected(
                    new SignatureSpellabilityEvaluationFailure
                        .PlanExpansionRequired(expansion));
            }

            outcomes.Add(planned.Request, outcome);
        }

        var evidence =
            ImmutableArray.CreateBuilder<SignatureSpellabilityEvidence>();
        var emitted = new HashSet<TypeResolutionRequest>(
            TypeResolutionRequestComparer.Instance);
        var participation = plan.Requests.ToDictionary(
            request => request.Request,
            request => request.AccessibilityParticipates,
            TypeResolutionRequestComparer.Instance);
        foreach (SignatureSpellabilityOccurrence occurrence
            in plan.Occurrences)
        {
            if (occurrence.Request is null)
            {
                evidence.Add(
                    new SignatureSpellabilityEvidence.IntrinsicPrimitive(
                        occurrence));
                continue;
            }
            if (!emitted.Add(occurrence.Request))
                continue;

            TypeResolutionOutcome outcome = outcomes[occurrence.Request];
            bool participates = participation[occurrence.Request];
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
            {
                evidence.Add(
                    new SignatureSpellabilityEvidence.Unresolved(
                        occurrence.Request,
                        participates,
                        outcome));
                continue;
            }

            if (resolved.Definition.Key.Assembly
                == plan.SourceCandidate.Id)
            {
                evidence.Add(
                    new SignatureSpellabilityEvidence.LocalRequirement(
                        occurrence.Request,
                        resolved.Definition,
                        participates));
                continue;
            }

            evidence.Add(
                new SignatureSpellabilityEvidence.ExternalDefinition(
                    occurrence.Request,
                    resolved.Definition,
                    participates,
                    context.GetTerminalDefinitionAccessibility(
                        resolved.Definition.Key)));
        }

        return new SignatureSpellabilityAggregate.Complete(
            evidence.ToImmutable());
    }
}

internal abstract class SignaturePlanDecodeOutcome
{
    private protected SignaturePlanDecodeOutcome()
    {
    }

    internal sealed class Decoded(
        ImmutableArray<RawOccurrence> occurrences)
        : SignaturePlanDecodeOutcome
    {
        internal ImmutableArray<RawOccurrence> Occurrences { get; } =
            occurrences;
    }

    internal sealed class Rejected(
        SignatureSpellabilityPlanFailure failure)
        : SignaturePlanDecodeOutcome
    {
        internal SignatureSpellabilityPlanFailure Failure { get; } = failure;
    }
}

internal readonly record struct RawOccurrence(
    MetadataNamedTypeReference Reference,
    SignatureSpellabilityOccurrenceRole Role);

internal readonly record struct SignatureTypeOccurrences(
    ImmutableArray<RawOccurrence> Values)
{
    internal static SignatureTypeOccurrences Empty =>
        new(ImmutableArray<RawOccurrence>.Empty);
}

internal sealed class SignatureOccurrenceProvider
    : ISignatureTypeProvider<SignatureTypeOccurrences, object?>
{
    readonly SignatureOccurrenceWorkBudget _workBudget = new();
    readonly Dictionary<TypeReferenceHandle, MetadataNamedTypeReference>
        _referenceProjections = [];
    readonly Dictionary<TypeDefinitionHandle, MetadataNamedTypeReference>
        _definitionProjections = [];
    readonly Dictionary<EntityHandle, MetadataTypeReferenceScope>
        _scopeProjections = [];
    MetadataReader? _projectionReader;

    // Every name read walks a chain -- the declaring chain for a TypeDef, the
    // resolution scope for a TypeRef -- and each walk is real work that the
    // ledger must price. This callback charges exactly the node count, once per
    // walk, and is passed at every read site.
    //
    // It is deliberately not the reader's beforeMaterialize hook. That hook
    // also fires per name component with the component's UTF-8 length, so an
    // observer that charges names through ChargeName would pay for them twice.
    //
    // GetTypeFromReference passes it even though it charges a chain length of
    // its own: the read walks the resolution scope, and the explicit walk that
    // recovers the terminal walks it a second time. Two walks happen, so two
    // charges are owed.
    //
    // Both walks are enforced by
    // SignatureSpellabilityAggregateTests
    //     .SignatureSpellability_ChargesBothResolutionScopeChainWalks,
    // which decodes 400 references over maximum-depth chains. That input is
    // admitted when either walk goes uncharged and refused when both are
    // charged, so the gate fails if a charge is removed or made unreachable.
    //
    // The delegate is cached because a decode projects many names.
    readonly Action<int> _chargeChainWalk;

    internal SignatureOccurrenceProvider() =>
        _chargeChainWalk =
            chainLength => _workBudget.ChargeMetadataWork(chainLength);

    public SignatureTypeOccurrences GetPrimitiveType(
        PrimitiveTypeCode typeCode)
    {
        _workBudget.ChargeNode();
        return Named(
            new MetadataTypeReferenceScope.IntrinsicCoreLibrary(),
            "System",
            typeCode switch
            {
                PrimitiveTypeCode.Boolean => "Boolean",
                PrimitiveTypeCode.Byte => "Byte",
                PrimitiveTypeCode.SByte => "SByte",
                PrimitiveTypeCode.Char => "Char",
                PrimitiveTypeCode.Int16 => "Int16",
                PrimitiveTypeCode.UInt16 => "UInt16",
                PrimitiveTypeCode.Int32 => "Int32",
                PrimitiveTypeCode.UInt32 => "UInt32",
                PrimitiveTypeCode.Int64 => "Int64",
                PrimitiveTypeCode.UInt64 => "UInt64",
                PrimitiveTypeCode.Single => "Single",
                PrimitiveTypeCode.Double => "Double",
                PrimitiveTypeCode.IntPtr => "IntPtr",
                PrimitiveTypeCode.UIntPtr => "UIntPtr",
                PrimitiveTypeCode.String => "String",
                PrimitiveTypeCode.Object => "Object",
                PrimitiveTypeCode.Void => "Void",
                PrimitiveTypeCode.TypedReference => "TypedReference",
                _ => throw new BadImageFormatException(
                    "The primitive type code is unsupported."),
            });
    }

    public SignatureTypeOccurrences GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        _workBudget.ChargeNode();
        BindProjectionReader(reader);
        if (!_definitionProjections.TryGetValue(
                handle,
                out MetadataNamedTypeReference? projection))
        {
            if (MetadataTypeDefinitionNameReader.Read(
                    reader,
                    handle,
                    chargeChain: _chargeChainWalk)
                is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                throw new BadImageFormatException(
                    "The signature TypeDef name could not be read.");
            }

            ChargeName(read.Name);
            projection = new MetadataNamedTypeReference(
                new MetadataTypeReferenceScope.CurrentAssembly(),
                read.Name);
            _definitionProjections[handle] = projection;
        }

        return One(projection);
    }

    public SignatureTypeOccurrences GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        _workBudget.ChargeNode();
        BindProjectionReader(reader);
        if (_referenceProjections.TryGetValue(
                handle,
                out MetadataNamedTypeReference? cached))
        {
            return One(cached);
        }

        if (MetadataTypeDefinitionNameReader.Read(
                reader,
                handle,
                chargeChain: _chargeChainWalk)
            is not MetadataTypeDefinitionNameReadResult.Read read)
        {
            throw new BadImageFormatException(
                "The signature TypeRef name could not be read.");
        }

        ChargeName(read.Name);
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out int chainLength,
                    out EntityHandle terminal,
                    out _))
        {
            throw new BadImageFormatException(
                "The TypeRef resolution-scope chain was rejected.");
        }

        _workBudget.ChargeMetadataWork(chainLength);
        var projection = new MetadataNamedTypeReference(
            ProjectScope(reader, terminal),
            read.Name);
        _referenceProjections[handle] = projection;
        return One(projection);
    }

    // Distinct TypeRefs commonly share one resolution-scope terminal, so
    // projecting the terminal per TypeRef re-reads the same assembly name,
    // culture, and public key -- and re-hashes the key -- once per reference.
    // Cache the terminal projection and charge its storage before reading it.
    MetadataTypeReferenceScope ProjectScope(
        MetadataReader reader,
        EntityHandle terminal)
    {
        if (_scopeProjections.TryGetValue(
                terminal,
                out MetadataTypeReferenceScope? cached))
        {
            return cached;
        }

        MetadataTypeReferenceScope scope;
        switch (terminal.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = (AssemblyReferenceHandle)terminal;
                System.Reflection.Metadata.AssemblyReference reference =
                    reader.GetAssemblyReference(assembly);
                ChargeStorage(reader, reference.Name);
                ChargeStorage(reader, reference.Culture);
                ChargeStorage(reader, reference.PublicKeyOrToken);
                scope = new MetadataTypeReferenceScope.AssemblyReference(
                    AssemblyReferenceIdentity.From(reader, assembly));
                break;
            case HandleKind.ModuleReference:
                scope = ModuleScope(reader, (ModuleReferenceHandle)terminal);
                break;
            case HandleKind.ModuleDefinition:
                scope = new MetadataTypeReferenceScope.CurrentAssembly();
                break;
            default:
                scope = terminal.IsNil
                    ? new MetadataTypeReferenceScope.CurrentAssembly()
                    : throw new BadImageFormatException(
                        "The TypeRef has an unsupported resolution scope.");
                break;
        }

        _scopeProjections[terminal] = scope;
        return scope;
    }

    void ChargeStorage(MetadataReader reader, StringHandle handle)
    {
        if (!handle.IsNil)
        {
            _workBudget.ChargeMetadataWork(
                reader.GetBlobReader(handle).Length);
        }
    }

    void ChargeStorage(MetadataReader reader, BlobHandle handle)
    {
        if (!handle.IsNil)
        {
            _workBudget.ChargeMetadataWork(
                reader.GetBlobReader(handle).Length);
        }
    }

    public SignatureTypeOccurrences GetTypeFromSpecification(
        MetadataReader reader,
        object? context,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        _workBudget.ChargeNode();
        // The completeness check below scans the whole TypeSpec blob, and a
        // shared TypeSpec is re-entered once per occurrence. Charge the bytes
        // that scan reads so repetition is bounded by cost, not by entry count.
        _workBudget.ChargeMetadataWork(
            reader.GetBlobReader(
                reader.GetTypeSpecification(handle).Signature).Length);

        // A nested TypeSpec must be fully consumed by the TypeSpec grammar.
        // SRM decodes one Type and stops, so a safe-prefix-only check would let
        // unconsumed trailing bytes ride along with the named children this
        // plan retains.
        if (!TypeSpecGuard.TryEnterComplete(reader, handle, out var scope))
        {
            throw new BadImageFormatException(
                "The TypeSpec recursion guard rejected the signature.");
        }

        using (scope)
        {
            return reader.GetTypeSpecification(handle)
                .DecodeSignature(this, context);
        }
    }

    public SignatureTypeOccurrences GetSZArrayType(
        SignatureTypeOccurrences elementType)
    {
        _workBudget.ChargeNode();
        return elementType;
    }

    public SignatureTypeOccurrences GetArrayType(
        SignatureTypeOccurrences elementType,
        ArrayShape shape)
    {
        // A wide shape costs more than the single node charged here. That
        // width is bounded by SignatureBlobGuard, which charges the declared
        // size and lower-bound counts against its own allowance and refuses
        // the blob before decoding begins. Do not attribute the bound to the
        // TypeSpec blob charge instead: a shape reached from a method or field
        // signature never passes through GetTypeFromSpecification, so that
        // charge does not run on every path that reaches here.
        _workBudget.ChargeNode();
        return elementType;
    }

    public SignatureTypeOccurrences GetByReferenceType(
        SignatureTypeOccurrences elementType)
    {
        _workBudget.ChargeNode();
        return elementType;
    }

    public SignatureTypeOccurrences GetPointerType(
        SignatureTypeOccurrences elementType)
    {
        _workBudget.ChargeNode();
        return elementType;
    }

    public SignatureTypeOccurrences GetPinnedType(
        SignatureTypeOccurrences elementType)
    {
        _workBudget.ChargeNode();
        return elementType;
    }

    public SignatureTypeOccurrences GetGenericInstantiation(
        SignatureTypeOccurrences genericType,
        ImmutableArray<SignatureTypeOccurrences> typeArguments)
    {
        _workBudget.ChargeNode();
        return Combine(genericType, typeArguments);
    }

    public SignatureTypeOccurrences GetGenericTypeParameter(
        object? context,
        int index)
    {
        _workBudget.ChargeNode();
        return SignatureTypeOccurrences.Empty;
    }

    public SignatureTypeOccurrences GetGenericMethodParameter(
        object? context,
        int index)
    {
        _workBudget.ChargeNode();
        return SignatureTypeOccurrences.Empty;
    }

    public SignatureTypeOccurrences GetFunctionPointerType(
        MethodSignature<SignatureTypeOccurrences> signature)
    {
        _workBudget.ChargeNode();
        return Combine(
            signature.ReturnType,
            signature.ParameterTypes);
    }

    public SignatureTypeOccurrences GetModifiedType(
        SignatureTypeOccurrences modifier,
        SignatureTypeOccurrences unmodifiedType,
        bool isRequired)
    {
        _workBudget.ChargeNode();
        return Combine(
            WithRole(
                modifier,
                isRequired
                    ? SignatureSpellabilityOccurrenceRole.RequiredModifier
                    : SignatureSpellabilityOccurrenceRole.OptionalModifier),
            [unmodifiedType]);
    }

    internal SignatureTypeOccurrences Combine(
        SignatureTypeOccurrences first,
        IEnumerable<SignatureTypeOccurrences> rest)
    {
        var remaining = new List<SignatureTypeOccurrences>();
        int count = first.Values.Length;
        if (count > MetadataSafetyPolicy.MaxSignatureTypeNodes)
        {
            throw new BadImageFormatException(
                "The signature occurrence result exceeds its node budget.");
        }

        foreach (SignatureTypeOccurrences value in rest)
        {
            if (value.Values.Length
                > MetadataSafetyPolicy.MaxSignatureTypeNodes - count)
            {
                throw new BadImageFormatException(
                    "The signature occurrence result exceeds its node budget.");
            }

            count += value.Values.Length;
            remaining.Add(value);
        }

        _workBudget.ChargeMaterialization(count);
        var builder = ImmutableArray.CreateBuilder<RawOccurrence>(count);
        builder.AddRange(first.Values);
        foreach (SignatureTypeOccurrences value in remaining)
            builder.AddRange(value.Values);
        return new SignatureTypeOccurrences(builder.MoveToImmutable());
    }

    SignatureTypeOccurrences WithRole(
        SignatureTypeOccurrences value,
        SignatureSpellabilityOccurrenceRole role)
    {
        _workBudget.ChargeMaterialization(value.Values.Length);
        var builder = ImmutableArray.CreateBuilder<RawOccurrence>(
            value.Values.Length);
        foreach (RawOccurrence occurrence in value.Values)
            builder.Add(occurrence with { Role = role });
        return new SignatureTypeOccurrences(builder.MoveToImmutable());
    }

    // A decode runs against the reader that owns the signature. If a different
    // reader ever appears, drop the projections rather than answering from
    // another module's tables; the work ledger is not reset, so alternating
    // readers cannot buy back budget.
    void BindProjectionReader(MetadataReader reader)
    {
        if (ReferenceEquals(_projectionReader, reader))
        {
            return;
        }

        _projectionReader = reader;
        _referenceProjections.Clear();
        _definitionProjections.Clear();
        _scopeProjections.Clear();
    }

    void ChargeName(MetadataTypeDefinitionName name)
    {
        long characters = name.Namespace.Length;
        foreach (string segment in name.Segments)
        {
            characters += segment.Length;
        }

        _workBudget.ChargeMetadataWork(characters);
    }

    SignatureTypeOccurrences One(
        MetadataNamedTypeReference reference)
    {
        _workBudget.ChargeMaterialization(1);
        return new(
            ImmutableArray.Create(
                new RawOccurrence(
                    reference,
                    SignatureSpellabilityOccurrenceRole.Ordinary)));
    }

    SignatureTypeOccurrences Named(
        MetadataTypeReferenceScope scope,
        string @namespace,
        string name) =>
        MetadataTypeDefinitionName.Create(
            @namespace,
            ImmutableArray.Create(name))
            is MetadataTypeDefinitionNameResult.Valid valid
                ? One(new MetadataNamedTypeReference(scope, valid.Name))
                : throw new BadImageFormatException(
                    "A primitive type name could not be represented.");

    sealed class SignatureOccurrenceWorkBudget
    {
        // Normal decoding copies occurrences through a few immutable aggregate
        // layers. Keep that work linear in the expanded-node ceiling.
        const int MaxMaterializationWork =
                MetadataSafetyPolicy.MaxSignatureTypeNodes * 8;

        // Counting callbacks bounds how many run, not what each one costs.
        // Names, resolution-scope chains, TypeSpec completeness scans, and
        // array shapes each read a different amount of metadata, so charge the
        // metadata actually examined against its own ceiling. One decode may
        // examine the equivalent of 64 maximum-length type names; real member
        // signatures reference tens of types, not thousands.
        const int MaxMetadataWork =
                MetadataSafetyPolicy.MaxTypeNameCharacters * 64;

        int _remainingNodes = MetadataSafetyPolicy.MaxSignatureTypeNodes;
        int _remainingMaterializationWork = MaxMaterializationWork;
        long _remainingMetadataWork = MaxMetadataWork;

        internal void ChargeNode()
        {
            if (_remainingNodes == 0)
            {
                throw new BadImageFormatException(
                    "The expanded signature exceeds its node budget.");
            }

            _remainingNodes--;
        }

        internal void ChargeMetadataWork(long units)
        {
            if (units < 0 || units > _remainingMetadataWork)
            {
                throw new BadImageFormatException(
                    "The signature decode exceeds its metadata work budget.");
            }

            _remainingMetadataWork -= units;
        }

        internal void ChargeMaterialization(int occurrences)
        {
                if (occurrences < 0
                    || occurrences > _remainingMaterializationWork)
                {
                    throw new BadImageFormatException(
                        "The signature occurrence materialization exceeds its "
                        + "work budget.");
                }

                _remainingMaterializationWork -= occurrences;
        }
    }

    MetadataTypeReferenceScope ModuleScope(
        MetadataReader reader,
        ModuleReferenceHandle handle)
    {
        StringHandle nameHandle = reader.GetModuleReference(handle).Name;
        ChargeStorage(reader, nameHandle);
        string name = reader.GetString(nameHandle);
        return string.IsNullOrWhiteSpace(name)
            ? throw new BadImageFormatException(
                "The module reference name is empty.")
            : new MetadataTypeReferenceScope.ModuleReference(name);
    }
}

internal enum SignaturePlanSourceStatus
{
    Current,
    Stale,
    Unavailable,
}

/// <summary>
/// Whether a caller holds the catalog's current generation for the duration of
/// a multi-step evaluation.
/// </summary>
internal enum GenerationLeaseStatus
{
    Acquired,
    Stale,
    Unavailable,
}
