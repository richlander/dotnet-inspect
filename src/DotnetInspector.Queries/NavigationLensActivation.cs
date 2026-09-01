namespace DotnetInspector.Queries;

/// <summary>A typed rejection of one exact navigation-lens request.</summary>
public abstract record NavigationLensRejection
{
    private protected NavigationLensRejection()
    {
    }

    /// <summary>The request was bound to a subject other than the active subject.</summary>
    public sealed record SubjectMismatch : NavigationLensRejection
    {
        internal SubjectMismatch(StructuralSubjectIdentity activeSubject)
        {
            ArgumentNullException.ThrowIfNull(activeSubject);
            ActiveSubject = activeSubject;
        }

        public StructuralSubjectIdentity ActiveSubject { get; }
    }

    /// <summary>The registry rejected the exact requested facet.</summary>
    public sealed record Registry : NavigationLensRejection
    {
        internal Registry(
            NavigationLensIdentity request,
            ViewFacetResolution result)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(result);
            if (result is not ViewFacetResolution.Inapplicable
                and not ViewFacetResolution.Unknown)
            {
                throw new ArgumentException(
                    "A registry rejection must be inapplicable or unknown.",
                    nameof(result));
            }

            Basis = new NavigationLensEvaluationBasis.ExactRequest(
                request,
                result);
        }

        public NavigationLensEvaluationBasis.ExactRequest Basis { get; }

        public ViewFacetResolution Result => Basis.Result;
    }
}

/// <summary>The semantic result of activating one exact navigation lens.</summary>
public abstract record NavigationLensActivationResult
{
    private protected NavigationLensActivationResult(
        NavigationLensIdentity request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    /// <summary>The complete exact request correlated with this result.</summary>
    public NavigationLensIdentity Request { get; }

    /// <summary>The exact requested lens is available.</summary>
    public sealed record Applied : NavigationLensActivationResult
    {
        internal Applied(
            NavigationLensIdentity request,
            NavigationLensOutcome.Effective outcome)
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            NavigationLensEvaluationBasis.ExactRequest exact =
                ValidateExactOutcome(request, outcome);
            if (exact.Result is not ViewFacetResolution.Available)
            {
                throw new ArgumentException(
                    "An applied activation requires an available Registry result.",
                    nameof(outcome));
            }
            if (outcome.EffectiveLens != request)
            {
                throw new ArgumentException(
                    "An applied activation must produce the exact requested lens.",
                    nameof(outcome));
            }
            Outcome = outcome;
        }

        public NavigationLensOutcome.Effective Outcome { get; }
    }

    /// <summary>The exact requested lens is currently unavailable.</summary>
    public sealed record Unavailable : NavigationLensActivationResult
    {
        internal Unavailable(
            NavigationLensIdentity request,
            NavigationLensOutcome.Unavailable outcome)
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            NavigationLensEvaluationBasis.ExactRequest exact =
                ValidateExactOutcome(request, outcome);
            if (exact.Result is not ViewFacetResolution.Unavailable)
            {
                throw new ArgumentException(
                    "An unavailable activation requires an unavailable Registry result.",
                    nameof(outcome));
            }
            Outcome = outcome;
        }

        public NavigationLensOutcome.Unavailable Outcome { get; }
    }

    /// <summary>The exact request is invalid for the active subject or registry.</summary>
    public sealed record Rejected : NavigationLensActivationResult
    {
        internal Rejected(
            NavigationLensIdentity request,
            NavigationLensRejection rejection)
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            switch (rejection)
            {
                case NavigationLensRejection.SubjectMismatch mismatch
                    when mismatch.ActiveSubject == request.Subject:
                    throw new ArgumentException(
                        "A subject-mismatch rejection requires different exact subjects.",
                        nameof(rejection));
                case NavigationLensRejection.Registry registry:
                    if (registry.Basis.Request != request)
                    {
                        throw new ArgumentException(
                            "A Registry rejection must retain the exact request.",
                            nameof(rejection));
                    }
                    break;
            }
            Rejection = rejection;
        }

        public NavigationLensRejection Rejection { get; }
    }

    /// <summary>The registry failed while evaluating the exact requested lens.</summary>
    public sealed record Failed : NavigationLensActivationResult
    {
        internal Failed(
            NavigationLensIdentity request,
            NavigationLensOutcome.Failed outcome)
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            NavigationLensEvaluationBasis.ExactRequest exact =
                ValidateExactOutcome(request, outcome);
            if (exact.Result is not ViewFacetResolution.Failed
                || outcome.Failure
                    is not NavigationLensFailure.RegistryEvaluation)
            {
                throw new ArgumentException(
                    "A failed activation requires the exact failed Registry result.",
                    nameof(outcome));
            }
            Outcome = outcome;
        }

        public NavigationLensOutcome.Failed Outcome { get; }
    }

    static NavigationLensEvaluationBasis.ExactRequest ValidateExactOutcome(
        NavigationLensIdentity request,
        NavigationLensOutcome outcome)
    {
        if (outcome.Basis
                is not NavigationLensEvaluationBasis.ExactRequest exact
            || exact.Request != request)
        {
            throw new ArgumentException(
                "An activation outcome must retain the exact request.",
                nameof(outcome));
        }

        return exact;
    }
}

/// <summary>Pure product mapping for one exact navigation-lens request.</summary>
public static class NavigationLensActivation
{
    public static NavigationLensActivationResult Activate(
        StructuralSubjectIdentity activeSubject,
        NavigationLensIdentity request,
        ViewFacetRegistry registry,
        IViewFacetAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(activeSubject);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(facts);

        if (request.Subject != activeSubject)
        {
            return new NavigationLensActivationResult.Rejected(
                request,
                new NavigationLensRejection.SubjectMismatch(activeSubject));
        }

        ViewFacetResolution result = registry.Resolve(
            request.Facet.Value,
            Target(activeSubject),
            facts);
        return result switch
        {
            ViewFacetResolution.Available =>
                Applied(request, result),
            ViewFacetResolution.Unavailable =>
                Unavailable(request, result),
            ViewFacetResolution.Failed =>
                Failed(request, result),
            ViewFacetResolution.Inapplicable
                or ViewFacetResolution.Unknown =>
                new NavigationLensActivationResult.Rejected(
                    request,
                    new NavigationLensRejection.Registry(request, result)),
            _ => throw new InvalidOperationException(
                "Unknown view-facet resolution result."),
        };
    }

    static NavigationLensActivationResult Applied(
        NavigationLensIdentity request,
        ViewFacetResolution result)
    {
        var basis = new NavigationLensEvaluationBasis.ExactRequest(
            request,
            result);
        return new NavigationLensActivationResult.Applied(
            request,
            new NavigationLensOutcome.Effective(basis, request));
    }

    static NavigationLensActivationResult Unavailable(
        NavigationLensIdentity request,
        ViewFacetResolution result)
    {
        var basis = new NavigationLensEvaluationBasis.ExactRequest(
            request,
            result);
        return new NavigationLensActivationResult.Unavailable(
            request,
            new NavigationLensOutcome.Unavailable(basis));
    }

    static NavigationLensActivationResult Failed(
        NavigationLensIdentity request,
        ViewFacetResolution result)
    {
        var basis = new NavigationLensEvaluationBasis.ExactRequest(
            request,
            result);
        return new NavigationLensActivationResult.Failed(
            request,
            new NavigationLensOutcome.Failed(
                basis,
                NavigationLensFailure.RegistryEvaluation.Instance));
    }

    static ViewFacetTarget Target(StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.RootSubject root =>
                ViewFacetTarget.ForRoot(root),
            StructuralSubjectIdentity.AllLibrariesSubject
                or StructuralSubjectIdentity.LibrarySubject
                or StructuralSubjectIdentity.TypeSubject
                or StructuralSubjectIdentity.MemberSubject =>
                ViewFacetTarget.ForSubject(subject),
            _ => throw new InvalidOperationException(
                "Unknown structural subject kind."),
        };
}
