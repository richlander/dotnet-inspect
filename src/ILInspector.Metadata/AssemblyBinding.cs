using System.Collections.Immutable;
using System.Collections.Concurrent;

namespace ILInspector.Metadata;

/// <summary>Why a binding policy could not produce a usable selection.</summary>
public enum AssemblyBindingFailureKind
{
    IdentityPolicyRequired,
    CandidateUnavailable,
    UnsupportedScope,
    InvalidPolicyResult,
    InvalidBindingOrigin,
    RequestBudgetExceeded,
}

/// <summary>
/// A policy owner's statement about name ownership for one missing assembly
/// reference.
/// </summary>
public enum AssemblyBindingMissDisposition
{
    /// <summary>The producer supplied no owner-attested name decision.</summary>
    Undifferentiated,

    /// <summary>The policy's complete frozen inventory does not own the name.</summary>
    NoNameOwner,

    /// <summary>The policy owns the name but found no matching identity.</summary>
    NameOwnedNoMatch,
}

/// <summary>
/// Structured policy diagnostic carried by unavailable or rejected binding
/// selections. It describes policy or acquisition state, not type lookup.
/// </summary>
public sealed record AssemblyBindingFailure
{
    public AssemblyBindingFailure(
        AssemblyBindingFailureKind kind,
        CandidateOpenFailureKind? candidateFailureKind = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (candidateFailureKind is { } candidateFailure
            && !Enum.IsDefined(candidateFailure))
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateFailureKind));
        }

        Kind = kind;
        CandidateFailureKind = candidateFailureKind;
    }

    public AssemblyBindingFailureKind Kind { get; }
    public CandidateOpenFailureKind? CandidateFailureKind { get; }

    /// <summary>The exact malformed-root reason for an invalid candidate.</summary>
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

/// <summary>
/// Opaque identity for one stable snapshot of an assembly-binding policy.
/// Replace the instance before <see cref="IAssemblyBindingPolicy.Select"/>
/// could return a different answer for the same request.
/// </summary>
public sealed class AssemblyBindingPolicyVersion;

/// <summary>
/// Immutable policy-issued continuation. The version identifies the issuing
/// policy state; derived records retain every context distinction, including
/// delegated lineage, that can change a subsequent answer.
/// </summary>
public abstract record AssemblyBindingLineage
{
    protected AssemblyBindingLineage(AssemblyBindingPolicyVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        Version = version;
    }

    private AssemblyBindingLineage()
    {
    }

    public AssemblyBindingPolicyVersion? Version { get; }

    /// <summary>A context-free continuation using the policy's seed rules.</summary>
    public static AssemblyBindingLineage Seed { get; } = new SeedLineage();

    // Seed continuations explicitly opt into the existing seed rules.
    internal static AssemblyBindingLineage? BindingContext(
        AssemblyBindingLineage? lineage) =>
        lineage == Seed ? null : lineage;

    /// <summary>Pairs a selection with this issuing policy's continuation.</summary>
    protected AssemblyBindingOccurrence CreateOccurrence(
        ResolvedAssemblyReference assembly) => new(assembly, this);

    sealed record SeedLineage : AssemblyBindingLineage;
}

/// <summary>
/// A selected acquisition descriptor and its immutable binding continuation.
/// Neither lineage nor occurrence creates a new acquisition registration.
/// </summary>
public sealed record AssemblyBindingOccurrence
{
    internal AssemblyBindingOccurrence(
        ResolvedAssemblyReference assembly,
        AssemblyBindingLineage lineage)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(lineage);
        Assembly = assembly;
        Lineage = lineage;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public AssemblyBindingLineage Lineage { get; }

    public static AssemblyBindingOccurrence Seed(
        ResolvedAssemblyReference assembly) =>
        new(assembly, AssemblyBindingLineage.Seed);
}

/// <summary>
/// One immutable policy answer paired with the exact policy-state version that
/// produced it.
/// </summary>
public sealed class AssemblyBindingSelectionSnapshot
{
    public AssemblyBindingSelectionSnapshot(
        AssemblyBindingPolicyVersion version,
        AssemblyBindingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(selection);
        Version = version;
        Selection = selection;
    }

    /// <summary>Gets the policy-state version that produced the selection.</summary>
    public AssemblyBindingPolicyVersion Version { get; }

    /// <summary>Gets the structured selection produced by that state.</summary>
    public AssemblyBindingSelection Selection { get; }
}

/// <summary>
/// The thing a binding policy is asked to select. This is deliberately
/// separate from <see cref="TypeResolutionStart"/>, which describes where
/// type resolution begins, and <see cref="TypeResolutionRequest"/>, which
/// pairs that start with the type name.
/// </summary>
public abstract record AssemblyBindingTarget
{
    private protected AssemblyBindingTarget()
    {
    }

    private protected abstract int Discriminator { get; }

    /// <summary>Creates a target for an exact metadata assembly reference.</summary>
    public static AssemblyBindingTarget Reference(
        AssemblyReferenceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new AssemblyReference(identity);
    }

    /// <summary>
    /// Creates the intrinsic core-library target for a requesting assembly.
    /// No synthetic assembly identity is introduced.
    /// </summary>
    public static AssemblyBindingTarget CoreLibrary() =>
        new IntrinsicCoreLibrary();

    /// <summary>An explicit metadata assembly-reference target.</summary>
    public sealed record AssemblyReference : AssemblyBindingTarget
    {
        internal AssemblyReference(AssemblyReferenceIdentity identity) =>
            Identity = identity;

        private protected override int Discriminator => 0;
        public AssemblyReferenceIdentity Identity { get; }
    }

    /// <summary>
    /// The core library selected from the requesting assembly's binding domain.
    /// </summary>
    public sealed record IntrinsicCoreLibrary : AssemblyBindingTarget
    {
        internal IntrinsicCoreLibrary()
        {
        }

        private protected override int Discriminator => 1;
    }
}

/// <summary>
/// The domain from which a binding request is made. Source-relative origins
/// prevent two assemblies with the same reference from sharing a policy answer
/// merely because their target identities match.
/// </summary>
public abstract class AssemblyBindingOrigin
{
    private protected AssemblyBindingOrigin()
    {
    }

    /// <summary>Creates an origin not associated with a requesting assembly.</summary>
    public static AssemblyBindingOrigin Global() => new GlobalOrigin();

    /// <summary>
    /// Creates a source-relative origin from an acquisition registration.
    /// </summary>
    public static RequestingAssembly FromAssembly(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new RequestingAssembly(assembly, null);
    }

    /// <summary>Continues from the exact occurrence returned by a selection.</summary>
    public static RequestingAssembly FromOccurrence(
        AssemblyBindingOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return new RequestingAssembly(occurrence.Assembly, occurrence);
    }

    /// <summary>A binding request with no requesting-assembly domain.</summary>
    public sealed class GlobalOrigin : AssemblyBindingOrigin
    {
        internal GlobalOrigin()
        {
        }
    }

    /// <summary>
    /// A binding request relative to one registered requesting assembly.
    /// </summary>
    public sealed class RequestingAssembly : AssemblyBindingOrigin
    {
        internal RequestingAssembly(
            ResolvedAssemblyReference assembly,
            AssemblyBindingOccurrence? occurrence)
        {
            Assembly = assembly;
            Occurrence = occurrence;
        }

        public ResolvedAssemblyReference Assembly { get; }
        public AssemblyBindingOccurrence? Occurrence { get; }
        public AssemblyBindingLineage? Lineage => Occurrence?.Lineage;
        public AssemblyAcquisitionRegistration Registration =>
            Assembly.Registration;
    }
}

/// <summary>
/// One policy question: select <see cref="Target"/> from
/// <see cref="Origin"/> under <see cref="Scope"/>.
/// </summary>
public sealed class AssemblyBindingRequest
{
    public AssemblyBindingRequest(
        AssemblyBindingTarget target,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        Target = target;
        Origin = origin;
        Scope = scope;
    }

    public AssemblyBindingTarget Target { get; }
    public AssemblyBindingOrigin Origin { get; }
    public AssemblyResolutionScope Scope { get; }
}

/// <summary>
/// The descriptor-level answer returned by
/// <see cref="IAssemblyBindingPolicy"/> during context discovery. Selections
/// contain acquisition descriptors; Metadata later interns them into
/// candidate-bearing <see cref="AssemblyBindingOutcome"/> values.
/// </summary>
public abstract class AssemblyBindingSelection
{
    private protected AssemblyBindingSelection()
    {
    }

    /// <summary>
    /// Returns one selected acquisition descriptor and optional descriptors
    /// retained as inactive shadow evidence.
    /// </summary>
    public static AssemblyBindingSelection Found(
        ResolvedAssemblyReference assembly,
        ImmutableArray<ResolvedAssemblyReference> shadowedAssemblies = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return FoundOccurrence(
            AssemblyBindingOccurrence.Seed(assembly),
            shadowedAssemblies);
    }

    /// <summary>Retains the selection issuer's occurrence unchanged.</summary>
    public static AssemblyBindingSelection FoundOccurrence(
        AssemblyBindingOccurrence occurrence,
        ImmutableArray<ResolvedAssemblyReference> shadowedAssemblies = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (shadowedAssemblies.IsDefault)
            shadowedAssemblies = [];
        if (shadowedAssemblies.Any(static shadow => shadow is null))
        {
            throw new ArgumentException(
                "Shadow evidence cannot contain null descriptors.",
                nameof(shadowedAssemblies));
        }

        return new Selected(occurrence, shadowedAssemblies);
    }

    /// <summary>
    /// Reports that policy found no candidate without attesting name
    /// ownership.
    /// </summary>
    public static AssemblyBindingSelection NotFound() =>
        new Missing(AssemblyBindingMissDisposition.Undifferentiated);

    /// <summary>Reports that the policy's complete inventory does not own the name.</summary>
    public static AssemblyBindingSelection NameNotOwned() =>
        new Missing(AssemblyBindingMissDisposition.NoNameOwner);

    /// <summary>Reports that the policy owns the name but found no identity match.</summary>
    public static AssemblyBindingSelection NameOwnedButNoMatch() =>
        new Missing(AssemblyBindingMissDisposition.NameOwnedNoMatch);

    /// <summary>
    /// Reports that policy understood the request but could not select a
    /// candidate under the requested policy or scope.
    /// </summary>
    public static AssemblyBindingSelection CannotSelect(
        AssemblyBindingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Unavailable(failure);
    }

    /// <summary>
    /// Reports multiple candidates without choosing by enumeration order.
    /// </summary>
    public static AssemblyBindingSelection Multiple(
        ImmutableArray<ResolvedAssemblyReference> assemblies)
    {
        if (assemblies.IsDefaultOrEmpty)
            throw new ArgumentException(
                "An ambiguous selection must contain candidates.",
                nameof(assemblies));
        if (assemblies.Any(static assembly => assembly is null))
            throw new ArgumentException(
                "An ambiguous selection cannot contain null candidates.",
                nameof(assemblies));
        return new Ambiguous(assemblies);
    }

    /// <summary>Reports an invalid request or policy response.</summary>
    public static AssemblyBindingSelection Invalid(
        AssemblyBindingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Rejected(failure);
    }

    /// <summary>
    /// Validates a policy answer against the original target before a wrapper
    /// or Metadata adapter interprets it.
    /// </summary>
    public static AssemblyBindingSelection ValidateForRequest(
        AssemblyBindingRequest request,
        AssemblyBindingSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(request);

        return selection is null
            || selection is Missing
            && request.Target
                is not AssemblyBindingTarget.AssemblyReference
            ? Invalid(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.InvalidPolicyResult))
            : selection;
    }

    /// <summary>
    /// A policy selection containing one descriptor and inactive shadow
    /// evidence.
    /// </summary>
    public sealed class Selected : AssemblyBindingSelection
    {
        internal Selected(
            AssemblyBindingOccurrence occurrence,
            ImmutableArray<ResolvedAssemblyReference> shadowedAssemblies)
        {
            Occurrence = occurrence;
            ShadowedAssemblies = shadowedAssemblies;
        }

        public AssemblyBindingOccurrence Occurrence { get; }
        public ResolvedAssemblyReference Assembly => Occurrence.Assembly;
        public ImmutableArray<ResolvedAssemblyReference> ShadowedAssemblies
        {
            get;
        }
    }

    /// <summary>A policy selection with no matching descriptor.</summary>
    public sealed class Missing : AssemblyBindingSelection
    {
        internal Missing(AssemblyBindingMissDisposition disposition)
        {
            if (!Enum.IsDefined(disposition))
                throw new ArgumentOutOfRangeException(nameof(disposition));

            Disposition = disposition;
        }

        public AssemblyBindingMissDisposition Disposition { get; }
    }

    /// <summary>
    /// A policy selection that could not choose a descriptor.
    /// </summary>
    public sealed class Unavailable : AssemblyBindingSelection
    {
        internal Unavailable(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    /// <summary>A policy selection containing multiple descriptors.</summary>
    public sealed class Ambiguous : AssemblyBindingSelection
    {
        internal Ambiguous(
            ImmutableArray<ResolvedAssemblyReference> assemblies) =>
            Assemblies = assemblies;

        public ImmutableArray<ResolvedAssemblyReference> Assemblies { get; }
    }

    /// <summary>A policy selection rejected as invalid.</summary>
    public sealed class Rejected : AssemblyBindingSelection
    {
        internal Rejected(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }
}

/// <summary>
/// External assembly-selection policy used while a resolution context is
/// being discovered. The policy returns descriptors, not catalog candidates.
/// </summary>
public interface IAssemblyBindingPolicy
{
    /// <summary>Gets the identity of the policy snapshot in use.</summary>
    AssemblyBindingPolicyVersion Version { get; }

    /// <summary>
    /// Selects descriptor candidates and atomically identifies the policy state
    /// that produced the answer.
    /// </summary>
    AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request);
}

/// <summary>
/// Compatibility adapter for an <see cref="IAssemblyReferenceResolver"/>.
/// Structured binding policies are forwarded transparently; nullable legacy
/// resolvers are snapshotted for one inspection lifetime. New acquisition
/// owners should implement <see cref="IAssemblyBindingPolicy"/> directly.
/// </summary>
public sealed class AssemblyReferenceBindingPolicy : IAssemblyBindingPolicy
{
    readonly IAssemblyReferenceResolver _resolver;
    readonly IAssemblyBindingPolicy? _bindingPolicy;
    readonly AssemblyBindingPolicyVersion _version = new();
    readonly ConcurrentDictionary<
        SelectionKey,
        Lazy<AssemblyBindingSelectionSnapshot>> _selections = new();

    public AssemblyReferenceBindingPolicy(IAssemblyReferenceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _bindingPolicy = resolver as IAssemblyBindingPolicy;
    }

    public AssemblyBindingPolicyVersion Version =>
        _bindingPolicy is { } bindingPolicy
            ? bindingPolicy.Version
            : _version;

    public AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_bindingPolicy is { } bindingPolicy)
            return bindingPolicy.Select(request);

        if (request.Origin is AssemblyBindingOrigin.RequestingAssembly
            {
                Lineage.Version: { } version,
            } && !ReferenceEquals(version, _version))
        {
            return new AssemblyBindingSelectionSnapshot(
                _version,
                AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidBindingOrigin)));
        }

        var key = SelectionKey.From(request);
        return _selections.GetOrAdd(
            key,
            _ => new Lazy<AssemblyBindingSelectionSnapshot>(
                () => new AssemblyBindingSelectionSnapshot(
                    _version,
                    SelectLegacy(request)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    AssemblyBindingSelection SelectLegacy(AssemblyBindingRequest request)
    {
        try
        {
            return request.Target switch
            {
                AssemblyBindingTarget.AssemblyReference reference =>
                    SelectReference(reference.Identity, request.Scope),
                AssemblyBindingTarget.IntrinsicCoreLibrary =>
                    AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.UnsupportedScope)),
                _ => AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult)),
            };
        }
        catch (Exception ex) when (
            ex is not UnsupportedMetadataFormatException
                and not MalformedMetadataRootException
                and (IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException))
        {
            return AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }

    AssemblyBindingSelection SelectReference(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope) =>
        _resolver.Resolve(identity, scope) is { } assembly
            ? AssemblyBindingSelection.Found(assembly)
            : AssemblyBindingSelection.NotFound();

    readonly record struct SelectionKey(
        AssemblyBindingTarget Target,
        ManifestOriginKey Origin,
        AssemblyResolutionScope Scope)
    {
        internal static SelectionKey From(AssemblyBindingRequest request) =>
            new(
                request.Target,
                ManifestOriginKey.From(request.Origin),
                request.Scope);
    }
}

/// <summary>
/// Catalog-interned binding result stored in a frozen
/// <see cref="TypeResolutionContext"/>. Unlike
/// <see cref="AssemblyBindingSelection"/>, successful and ambiguous arms carry
/// catalog candidates. A resolved outcome retains descriptor-level shadow
/// evidence without interning it as active candidates. Policies cannot
/// construct these outcomes.
/// </summary>
public abstract class AssemblyBindingOutcome
{
    private protected AssemblyBindingOutcome()
    {
    }

    /// <summary>
    /// One descriptor was interned as a catalog candidate; any shadow
    /// descriptors remain inactive evidence.
    /// </summary>
    public sealed class Resolved : AssemblyBindingOutcome
    {
        internal Resolved(
            ResolvedAssemblyCandidate candidate,
            AssemblyBindingOccurrence occurrence,
            ImmutableArray<ResolvedAssemblyReference> shadowedAssemblies)
        {
            Candidate = candidate;
            Occurrence = occurrence;
            ShadowedAssemblies = shadowedAssemblies;
        }

        public ResolvedAssemblyCandidate Candidate { get; }
        public AssemblyBindingOccurrence Occurrence { get; }
        public ImmutableArray<ResolvedAssemblyReference> ShadowedAssemblies
        {
            get;
        }
    }

    /// <summary>The policy found no candidate.</summary>
    public sealed class Missing : AssemblyBindingOutcome
    {
        internal Missing(AssemblyBindingMissDisposition disposition)
        {
            if (!Enum.IsDefined(disposition))
                throw new ArgumentOutOfRangeException(nameof(disposition));

            Disposition = disposition;
        }

        public AssemblyBindingMissDisposition Disposition { get; }
    }

    /// <summary>
    /// Binding could not produce a usable catalog candidate.
    /// </summary>
    public sealed class Unavailable : AssemblyBindingOutcome
    {
        internal Unavailable(
            AssemblyBindingFailure failure,
            ImmutableArray<ResolvedAssemblyReference> shadowedAssemblies =
                default)
        {
            Failure = failure;
            ShadowedAssemblies = shadowedAssemblies.IsDefault
                ? []
                : shadowedAssemblies;
        }

        public AssemblyBindingFailure Failure { get; }
        public ImmutableArray<ResolvedAssemblyReference> ShadowedAssemblies
        {
            get;
        }
    }

    /// <summary>Several catalog candidates remain plausible.</summary>
    public sealed class Ambiguous : AssemblyBindingOutcome
    {
        internal Ambiguous(
            ImmutableArray<ResolvedAssemblyCandidate> candidates) =>
            Candidates = candidates;

        public ImmutableArray<ResolvedAssemblyCandidate> Candidates { get; }
    }

    /// <summary>The binding request or policy result was invalid.</summary>
    public sealed class Rejected : AssemblyBindingOutcome
    {
        internal Rejected(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    /// <summary>
    /// The binding request was not present in this frozen context's manifest.
    /// A coordinator may include <see cref="Request"/> in a later generation.
    /// </summary>
    public sealed class ExpansionRequired : AssemblyBindingOutcome
    {
        internal ExpansionRequired(AssemblyBindingRequest request) =>
            Request = request;

        public AssemblyBindingRequest Request { get; }
    }
}
