using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>One exact view facet bound to one exact structural subject.</summary>
public sealed record NavigationLensIdentity
{
    public NavigationLensIdentity(
        StructuralSubjectIdentity subject,
        ViewFacetId facet)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(facet);
        Subject = subject;
        Facet = facet;
    }

    public StructuralSubjectIdentity Subject { get; }
    public ViewFacetId Facet { get; }
}

/// <summary>The exact input retained for one navigation lens outcome.</summary>
public abstract record NavigationLensEvaluationBasis
{
    private protected NavigationLensEvaluationBasis(
        StructuralSubjectIdentity subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Subject = subject;
    }

    public StructuralSubjectIdentity Subject { get; }

    public sealed record Recommendation :
        NavigationLensEvaluationBasis
    {
        internal Recommendation(
            StructuralSubjectIdentity subject,
            ViewFacetRole preferredRole,
            ImmutableArray<ViewFacetOption> options)
            : base(subject)
        {
            if (!Enum.IsDefined(preferredRole))
                throw new ArgumentOutOfRangeException(nameof(preferredRole));
            if (options.IsDefault)
            {
                throw new ArgumentException(
                    "Recommendation options must be an initialized immutable array.",
                    nameof(options));
            }

            PreferredRole = preferredRole;
            Options = options;
        }

        public ViewFacetRole PreferredRole { get; }
        public ImmutableArray<ViewFacetOption> Options { get; }

        public bool Equals(Recommendation? other) =>
            ReferenceEquals(this, other)
            || other is not null
            && Subject == other.Subject
            && PreferredRole == other.PreferredRole
            && Options.SequenceEqual(other.Options);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Subject);
            hash.Add(PreferredRole);
            foreach (ViewFacetOption option in Options)
                hash.Add(option);
            return hash.ToHashCode();
        }
    }

    public sealed record ExactRequest :
        NavigationLensEvaluationBasis
    {
        internal ExactRequest(
            NavigationLensIdentity request,
            ViewFacetResolution result)
            : base(RequireRequest(request).Subject)
        {
            ArgumentNullException.ThrowIfNull(result);
            ViewFacetDescriptor? descriptor = ResultDescriptor(result);
            if (descriptor is not null
                && descriptor.Id != request.Facet)
            {
                throw new ArgumentException(
                    "An exact-request result must describe the requested facet.",
                    nameof(result));
            }
            if (descriptor is not null
                && result is not ViewFacetResolution.Inapplicable
                && descriptor.Kind != request.Subject.Kind)
            {
                throw new ArgumentException(
                    "A non-inapplicable exact-request result must match the requested subject kind.",
                    nameof(result));
            }
            Request = request;
            Result = result;
        }

        public NavigationLensIdentity Request { get; }
        public ViewFacetResolution Result { get; }

        static NavigationLensIdentity RequireRequest(
            NavigationLensIdentity? request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return request;
        }

        static ViewFacetDescriptor? ResultDescriptor(
            ViewFacetResolution result) =>
            result switch
            {
                ViewFacetResolution.Available available =>
                    available.Descriptor,
                ViewFacetResolution.Unavailable unavailable =>
                    unavailable.Descriptor,
                ViewFacetResolution.Failed failed =>
                    failed.Descriptor,
                ViewFacetResolution.Inapplicable inapplicable =>
                    inapplicable.Descriptor,
                ViewFacetResolution.Unknown => null,
                _ => throw new InvalidOperationException(
                    "Unknown view-facet resolution result."),
            };
    }
}

/// <summary>A typed Navigation-owned lens policy failure.</summary>
public enum NavigationLensPolicyFailureKind
{
    EmptyOptions,
    MissingPreferredRole,
}

/// <summary>The source of a non-effective failed lens outcome.</summary>
public abstract record NavigationLensFailure
{
    private protected NavigationLensFailure()
    {
    }

    public sealed record Policy : NavigationLensFailure
    {
        internal Policy(NavigationLensPolicyFailureKind kind)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
        }

        public NavigationLensPolicyFailureKind Kind { get; }
    }

    public sealed record RegistryEvaluation : NavigationLensFailure
    {
        public static RegistryEvaluation Instance { get; } = new();

        private RegistryEvaluation()
        {
        }
    }
}

/// <summary>
/// Effective or non-effective result plus its exact retained evaluation basis.
/// </summary>
public abstract record NavigationLensOutcome
{
    private protected NavigationLensOutcome(
        NavigationLensEvaluationBasis basis,
        NavigationLensIdentity? effectiveLens)
    {
        ArgumentNullException.ThrowIfNull(basis);
        if (effectiveLens is not null
            && effectiveLens.Subject != basis.Subject)
        {
            throw new ArgumentException(
                "An effective lens must bind the basis subject.",
                nameof(effectiveLens));
        }

        Basis = basis;
        EffectiveLens = effectiveLens;
    }

    public NavigationLensEvaluationBasis Basis { get; }
    public NavigationLensIdentity? EffectiveLens { get; }

    public sealed record Effective : NavigationLensOutcome
    {
        internal Effective(
            NavigationLensEvaluationBasis basis,
            NavigationLensIdentity lens)
            : base(basis, ValidateLens(basis, lens))
        {
        }

        static NavigationLensIdentity ValidateLens(
            NavigationLensEvaluationBasis? basis,
            NavigationLensIdentity? lens)
        {
            ArgumentNullException.ThrowIfNull(basis);
            ArgumentNullException.ThrowIfNull(lens);
            bool isAvailable = basis switch
            {
                NavigationLensEvaluationBasis.Recommendation recommendation =>
                    recommendation.Options.Any(option =>
                        option.Descriptor.Id == lens.Facet
                        && option.Availability
                            is ViewFacetAvailability.Available),
                NavigationLensEvaluationBasis.ExactRequest exact =>
                    exact.Request == lens
                    && exact.Result is ViewFacetResolution.Available,
                _ => throw new InvalidOperationException(
                    "Unknown navigation lens evaluation basis."),
            };
            return isAvailable
                ? lens
                : throw new ArgumentException(
                    "An effective lens must be available in its retained basis.",
                    nameof(lens));
        }
    }

    public sealed record Unavailable : NavigationLensOutcome
    {
        internal Unavailable(
            NavigationLensEvaluationBasis basis)
            : base(basis, effectiveLens: null)
        {
        }
    }

    public sealed record Failed : NavigationLensOutcome
    {
        internal Failed(
            NavigationLensEvaluationBasis basis,
            NavigationLensFailure failure)
            : base(basis, effectiveLens: null)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public NavigationLensFailure Failure { get; }
    }
}

/// <summary>Pure product policy for choosing one lens for one exact subject.</summary>
public static class NavigationLensRecommendation
{
    public static NavigationLensOutcome Recommend(
        StructuralSubjectIdentity subject,
        ImmutableArray<ViewFacetOption> options)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (options.IsDefault)
        {
            throw new ArgumentException(
                "Recommendation options must be an initialized immutable array.",
                nameof(options));
        }

        ViewFacetRole preferredRole = PreferredRole(subject);
        var basis = new NavigationLensEvaluationBasis.Recommendation(
            subject,
            preferredRole,
            options);
        if (options.IsEmpty)
        {
            return PolicyFailure(
                basis,
                NavigationLensPolicyFailureKind.EmptyOptions);
        }

        ViewFacetOption? preferred = null;
        foreach (ViewFacetOption option in options)
        {
            ArgumentNullException.ThrowIfNull(option);
            if (option.Descriptor.Kind != subject.Kind)
            {
                throw new ArgumentException(
                    "Every recommendation option must apply to the exact subject kind.",
                    nameof(options));
            }
            if (option.Descriptor.Role != preferredRole)
                continue;
            if (preferred is not null)
            {
                throw new InvalidOperationException(
                    "Target-aware options contain the preferred role more than once.");
            }
            preferred = option;
        }

        if (preferred is null)
        {
            return PolicyFailure(
                basis,
                NavigationLensPolicyFailureKind.MissingPreferredRole);
        }

        if (preferred.Availability is ViewFacetAvailability.Available)
            return Effective(basis, subject, preferred.Descriptor.Id);

        foreach (ViewFacetOption option in options)
        {
            if (option.Availability is ViewFacetAvailability.Available)
                return Effective(basis, subject, option.Descriptor.Id);
        }

        if (options.Any(static option =>
            option.Availability is ViewFacetAvailability.Failed))
        {
            return new NavigationLensOutcome.Failed(
                basis,
                NavigationLensFailure.RegistryEvaluation.Instance);
        }

        return new NavigationLensOutcome.Unavailable(basis);
    }

    static NavigationLensOutcome Effective(
        NavigationLensEvaluationBasis.Recommendation basis,
        StructuralSubjectIdentity subject,
        ViewFacetId facet) =>
        new NavigationLensOutcome.Effective(
            basis,
            new NavigationLensIdentity(subject, facet));

    static NavigationLensOutcome PolicyFailure(
        NavigationLensEvaluationBasis.Recommendation basis,
        NavigationLensPolicyFailureKind kind) =>
        new NavigationLensOutcome.Failed(
            basis,
            new NavigationLensFailure.Policy(kind));

    static ViewFacetRole PreferredRole(
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.RootSubject root
                when ViewFacetTarget.ForRoot(root).RootKind
                    == ViewFacetRootKind.PackageCapable =>
                ViewFacetRole.PackageOverview,
            StructuralSubjectIdentity.RootSubject =>
                ViewFacetRole.RootOverview,
            StructuralSubjectIdentity.AllLibrariesSubject
                or StructuralSubjectIdentity.LibrarySubject =>
                ViewFacetRole.LibraryReferences,
            StructuralSubjectIdentity.TypeSubject =>
                ViewFacetRole.TypeApi,
            StructuralSubjectIdentity.MemberSubject =>
                ViewFacetRole.MemberOverview,
            _ => throw new InvalidOperationException(
                "Unknown structural subject kind."),
        };
}
