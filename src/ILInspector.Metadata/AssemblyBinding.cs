using System.Collections.Immutable;

namespace ILInspector.Metadata;

public enum AssemblyBindingFailureKind
{
    IdentityPolicyRequired,
    CandidateUnavailable,
    UnsupportedScope,
    InvalidPolicyResult,
}

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

public sealed class AssemblyBindingPolicyVersion;

public abstract record AssemblyBindingTarget
{
    private protected AssemblyBindingTarget()
    {
    }

    private protected abstract int Discriminator { get; }

    public static AssemblyBindingTarget Reference(
        AssemblyReferenceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new AssemblyReference(identity);
    }

    public static AssemblyBindingTarget CoreLibrary() =>
        new IntrinsicCoreLibrary();

    public sealed record AssemblyReference : AssemblyBindingTarget
    {
        internal AssemblyReference(AssemblyReferenceIdentity identity) =>
            Identity = identity;

        private protected override int Discriminator => 0;
        public AssemblyReferenceIdentity Identity { get; }
    }

    public sealed record IntrinsicCoreLibrary : AssemblyBindingTarget
    {
        internal IntrinsicCoreLibrary()
        {
        }

        private protected override int Discriminator => 1;
    }
}

public abstract class AssemblyBindingOrigin
{
    private protected AssemblyBindingOrigin()
    {
    }

    public static AssemblyBindingOrigin Global() => new GlobalOrigin();

    public static RequestingAssembly FromAssembly(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new RequestingAssembly(assembly.Registration);
    }

    public sealed class GlobalOrigin : AssemblyBindingOrigin
    {
        internal GlobalOrigin()
        {
        }
    }

    public sealed class RequestingAssembly : AssemblyBindingOrigin
    {
        internal RequestingAssembly(
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;

        public AssemblyAcquisitionRegistration Registration { get; }
    }
}

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

public abstract class AssemblyBindingSelection
{
    private protected AssemblyBindingSelection()
    {
    }

    public static AssemblyBindingSelection Found(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new Selected(assembly);
    }

    public static AssemblyBindingSelection NotFound() => new Missing();

    public static AssemblyBindingSelection CannotSelect(
        AssemblyBindingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Unavailable(failure);
    }

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

    public static AssemblyBindingSelection Invalid(
        AssemblyBindingFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Rejected(failure);
    }

    public sealed class Selected : AssemblyBindingSelection
    {
        internal Selected(ResolvedAssemblyReference assembly) =>
            Assembly = assembly;

        public ResolvedAssemblyReference Assembly { get; }
    }

    public sealed class Missing : AssemblyBindingSelection
    {
        internal Missing()
        {
        }
    }

    public sealed class Unavailable : AssemblyBindingSelection
    {
        internal Unavailable(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : AssemblyBindingSelection
    {
        internal Ambiguous(
            ImmutableArray<ResolvedAssemblyReference> assemblies) =>
            Assemblies = assemblies;

        public ImmutableArray<ResolvedAssemblyReference> Assemblies { get; }
    }

    public sealed class Rejected : AssemblyBindingSelection
    {
        internal Rejected(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }
}

public interface IAssemblyBindingPolicy
{
    AssemblyBindingPolicyVersion Version { get; }
    AssemblyBindingSelection Select(AssemblyBindingRequest request);
}

public abstract class AssemblyBindingOutcome
{
    private protected AssemblyBindingOutcome()
    {
    }

    public sealed class Resolved : AssemblyBindingOutcome
    {
        internal Resolved(ResolvedAssemblyCandidate candidate) =>
            Candidate = candidate;

        public ResolvedAssemblyCandidate Candidate { get; }
    }

    public sealed class Missing : AssemblyBindingOutcome
    {
        internal Missing()
        {
        }
    }

    public sealed class Unavailable : AssemblyBindingOutcome
    {
        internal Unavailable(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : AssemblyBindingOutcome
    {
        internal Ambiguous(
            ImmutableArray<ResolvedAssemblyCandidate> candidates) =>
            Candidates = candidates;

        public ImmutableArray<ResolvedAssemblyCandidate> Candidates { get; }
    }

    public sealed class Rejected : AssemblyBindingOutcome
    {
        internal Rejected(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class ExpansionRequired : AssemblyBindingOutcome
    {
        internal ExpansionRequired(AssemblyBindingRequest request) =>
            Request = request;

        public AssemblyBindingRequest Request { get; }
    }
}
