using DotnetInspector.Queries;

namespace DotnetInspector.EcosystemLoading;

/// <summary>
/// Application-issued exact correspondence between one lower registration and
/// one static loader binding.
/// </summary>
public abstract class EcosystemPopulationLoaderCorrespondence
{
    private protected EcosystemPopulationLoaderCorrespondence(
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationLoaderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(binding);
        Registration = registration;
        Binding = binding;
    }

    public WorkspaceEcosystemRegistrationDeclaration Registration { get; }
    public EcosystemPopulationLoaderBinding Binding { get; }

    public abstract EcosystemPopulationLoaderSelection Select(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration retainedRegistration,
        EcosystemPopulationDemand demand);
}

/// <summary>
/// Typed application-issued correspondence used to form a bound request.
/// </summary>
public sealed class EcosystemPopulationLoaderCorrespondence<TInputs> :
    EcosystemPopulationLoaderCorrespondence
    where TInputs : class, IEcosystemPopulationLoadInputs
{
    readonly EcosystemPopulationLoaderBinding<TInputs> _binding;

    public EcosystemPopulationLoaderCorrespondence(
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationLoaderBinding<TInputs> binding)
        : base(registration, binding) =>
        _binding = binding;

    public new EcosystemPopulationLoaderBinding<TInputs> Binding => _binding;

    public override EcosystemPopulationLoaderSelection Select(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration retainedRegistration,
        EcosystemPopulationDemand demand)
    {
        EcosystemPopulationRegistrationValidation.ValidateRetained(
            revision,
            retainedRegistration,
            nameof(retainedRegistration));
        ArgumentNullException.ThrowIfNull(demand);

        if (!ReferenceEquals(Registration, retainedRegistration))
        {
            return EcosystemPopulationLoaderSelection.CreateRejected(
                revision,
                retainedRegistration,
                demand,
                [
                    new(
                        "ecosystem-loader.registration-mismatch",
                        "The selected application loader does not correspond to the retained Ecosystem registration."),
                ]);
        }

        return new EcosystemPopulationLoaderSelection.Known<TInputs>(
            revision,
            retainedRegistration,
            Binding,
            demand);
    }

}

/// <summary>
/// Closed result of selecting a special loader before request construction.
/// </summary>
public abstract class EcosystemPopulationLoaderSelection
{
    private protected EcosystemPopulationLoaderSelection(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationDemand demand)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(demand);
        Revision = revision;
        Registration = registration;
        Demand = demand;
    }

    public WorkspaceRegistrationRevision Revision { get; }
    public WorkspaceEcosystemRegistrationDeclaration Registration { get; }
    public EcosystemPopulationDemand Demand { get; }

    public static Unavailable UnavailableFor(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationDemand demand,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics)
    {
        EcosystemPopulationRegistrationValidation.ValidateRetained(
            revision,
            registration,
            nameof(registration));
        return new Unavailable(
            revision,
            registration,
            demand,
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));
    }

    public static Rejected RejectedFor(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationDemand demand,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics)
    {
        EcosystemPopulationRegistrationValidation.ValidateRetained(
            revision,
            registration,
            nameof(registration));
        return CreateRejected(
            revision,
            registration,
            demand,
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));
    }

    internal static Rejected CreateRejected(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationDemand demand,
        IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics) =>
        new(revision, registration, demand, diagnostics);

    public abstract class Known : EcosystemPopulationLoaderSelection
    {
        private protected Known(
            WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration registration,
            EcosystemPopulationLoaderBinding binding,
            EcosystemPopulationDemand demand)
            : base(revision, registration, demand)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        public EcosystemPopulationLoaderBinding Binding { get; }
    }

    public sealed class Known<TInputs> : Known
        where TInputs : class, IEcosystemPopulationLoadInputs
    {
        internal Known(
            WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration registration,
            EcosystemPopulationLoaderBinding<TInputs> binding,
            EcosystemPopulationDemand demand)
            : base(revision, registration, binding, demand) =>
            _binding = binding;

        readonly EcosystemPopulationLoaderBinding<TInputs> _binding;

        public new EcosystemPopulationLoaderBinding<TInputs> Binding =>
            _binding;

        public EcosystemPopulationLoadRequest<TInputs> CreateRequest(
            TInputs inputs,
            CancellationToken cancellationToken = default) =>
            new(this, inputs, cancellationToken);
    }

    public sealed class Unavailable : EcosystemPopulationLoaderSelection
    {
        internal Unavailable(
            WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration registration,
            EcosystemPopulationDemand demand,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(revision, registration, demand) =>
            Diagnostics = Array.AsReadOnly([.. diagnostics]);

        public IReadOnlyList<EcosystemPopulationLoadDiagnostic> Diagnostics
        {
            get;
        }
    }

    public sealed class Rejected : EcosystemPopulationLoaderSelection
    {
        internal Rejected(
            WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration registration,
            EcosystemPopulationDemand demand,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(revision, registration, demand) =>
            Diagnostics = Array.AsReadOnly([.. diagnostics]);

        public IReadOnlyList<EcosystemPopulationLoadDiagnostic> Diagnostics
        {
            get;
        }
    }

}

static class EcosystemPopulationRegistrationValidation
{
    public static void ValidateRetained(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(registration);
        if (!revision.Registrations.Any(
                item =>
                    item is WorkspaceRegistration.Ecosystem ecosystem
                    && ReferenceEquals(
                        ecosystem.Declaration,
                        registration)))
        {
            throw new ArgumentException(
                "The exact Ecosystem registration is not retained by the supplied Workspace revision.",
                parameterName);
        }
    }
}
