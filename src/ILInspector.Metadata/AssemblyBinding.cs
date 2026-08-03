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
}

/// <summary>
/// Structured policy diagnostic carried by unavailable or rejected binding
/// selections. It describes policy or acquisition state, not type lookup.
/// </summary>
public sealed record AssemblyBindingFailure
{
    public AssemblyBindingFailure(AssemblyBindingFailureKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
    }

    public AssemblyBindingFailureKind Kind { get; }
}

/// <summary>
/// Opaque identity for one stable snapshot of an assembly-binding policy.
/// Replace the instance before <see cref="IAssemblyBindingPolicy.Select"/>
/// could return a different answer for the same request.
/// </summary>
public sealed class AssemblyBindingPolicyVersion;

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
        return new RequestingAssembly(assembly.Registration);
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
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;

        public AssemblyAcquisitionRegistration Registration { get; }
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

    /// <summary>Returns one selected acquisition descriptor.</summary>
    public static AssemblyBindingSelection Found(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new Selected(assembly);
    }

    /// <summary>Reports that policy found no candidate.</summary>
    public static AssemblyBindingSelection NotFound() => new Missing();

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

    /// <summary>A policy selection containing one descriptor.</summary>
    public sealed class Selected : AssemblyBindingSelection
    {
        internal Selected(ResolvedAssemblyReference assembly) =>
            Assembly = assembly;

        public ResolvedAssemblyReference Assembly { get; }
    }

    /// <summary>A policy selection with no matching descriptor.</summary>
    public sealed class Missing : AssemblyBindingSelection
    {
        internal Missing()
        {
        }
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

    /// <summary>Selects descriptor candidates for one structured request.</summary>
    AssemblyBindingSelection Select(AssemblyBindingRequest request);
}

/// <summary>
/// Migration policy that snapshots answers from an
/// <see cref="IAssemblyReferenceResolver"/> for one inspection lifetime.
/// New acquisition owners should implement <see cref="IAssemblyBindingPolicy"/>
/// directly.
/// </summary>
public sealed class AssemblyReferenceBindingPolicy : IAssemblyBindingPolicy
{
    readonly IAssemblyReferenceResolver _resolver;
    readonly ConcurrentDictionary<
        SelectionKey,
        Lazy<AssemblyBindingSelection>> _selections = new();

    public AssemblyReferenceBindingPolicy(IAssemblyReferenceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    public AssemblyBindingPolicyVersion Version { get; } = new();

    public AssemblyBindingSelection Select(AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = SelectionKey.From(request);
        return _selections.GetOrAdd(
            key,
            _ => new Lazy<AssemblyBindingSelection>(
                () => SelectCore(request),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    AssemblyBindingSelection SelectCore(AssemblyBindingRequest request)
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
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException)
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
        AssemblyAcquisitionRegistration? Origin,
        bool GlobalOrigin,
        AssemblyResolutionScope Scope)
    {
        internal static SelectionKey From(
            AssemblyBindingRequest request) =>
            request.Origin switch
            {
                AssemblyBindingOrigin.GlobalOrigin =>
                    new(request.Target, null, true, request.Scope),
                AssemblyBindingOrigin.RequestingAssembly requesting =>
                    new(
                        request.Target,
                        requesting.Registration,
                        false,
                        request.Scope),
                _ => throw new InvalidOperationException(
                    "Unknown assembly-binding origin."),
            };
    }
}

/// <summary>
/// Catalog-interned binding result stored in a frozen
/// <see cref="TypeResolutionContext"/>. Unlike
/// <see cref="AssemblyBindingSelection"/>, successful and ambiguous arms carry
/// catalog candidates. Policies cannot construct these outcomes.
/// </summary>
public abstract class AssemblyBindingOutcome
{
    private protected AssemblyBindingOutcome()
    {
    }

    /// <summary>One descriptor was interned as a catalog candidate.</summary>
    public sealed class Resolved : AssemblyBindingOutcome
    {
        internal Resolved(ResolvedAssemblyCandidate candidate) =>
            Candidate = candidate;

        public ResolvedAssemblyCandidate Candidate { get; }
    }

    /// <summary>The policy found no candidate.</summary>
    public sealed class Missing : AssemblyBindingOutcome
    {
        internal Missing()
        {
        }
    }

    /// <summary>
    /// Binding could not produce a usable catalog candidate.
    /// </summary>
    public sealed class Unavailable : AssemblyBindingOutcome
    {
        internal Unavailable(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
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
