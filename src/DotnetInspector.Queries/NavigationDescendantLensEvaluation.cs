namespace DotnetInspector.Queries;

/// <summary>
/// One exact current source subject and exact subject-bound destination lens.
/// </summary>
public sealed record DescendantSubjectLensRequest
{
    public DescendantSubjectLensRequest(
        StructuralSubjectIdentity source,
        NavigationLensIdentity destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        Source = source;
        Destination = destination;
    }

    public StructuralSubjectIdentity Source { get; }

    public NavigationLensIdentity Destination { get; }
}

/// <summary>Why a descendant subject+lens request was rejected pre-Registry.</summary>
public enum NavigationDescendantLensRejectionKind
{
    SourceMismatch,
    ForeignWorkspace,
    ForeignOccurrence,
    NonDescendant,
}

/// <summary>
/// Stateless semantic result for one exact descendant subject+lens request.
/// </summary>
public abstract record NavigationDescendantLensResult
{
    private protected NavigationDescendantLensResult(
        DescendantSubjectLensRequest request,
        NavigationWorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        Request = request;
        Snapshot = snapshot;
    }

    public DescendantSubjectLensRequest Request { get; }

    public NavigationWorkspaceSnapshot Snapshot { get; }

    public sealed record Applied : NavigationDescendantLensResult
    {
        internal Applied(
            DescendantSubjectLensRequest request,
            NavigationWorkspaceSnapshot snapshot,
            NavigationLensActivationResult.Applied activation)
            : base(request, snapshot)
        {
            Activation = activation;
        }

        public NavigationLensActivationResult.Applied Activation { get; }
    }

    public sealed record Unavailable : NavigationDescendantLensResult
    {
        internal Unavailable(
            DescendantSubjectLensRequest request,
            NavigationWorkspaceSnapshot snapshot,
            NavigationLensActivationResult.Unavailable activation)
            : base(request, snapshot)
        {
            Activation = activation;
        }

        public NavigationLensActivationResult.Unavailable Activation { get; }
    }

    public sealed record Failed : NavigationDescendantLensResult
    {
        internal Failed(
            DescendantSubjectLensRequest request,
            NavigationWorkspaceSnapshot snapshot,
            NavigationLensActivationResult.Failed activation)
            : base(request, snapshot)
        {
            Activation = activation;
        }

        public NavigationLensActivationResult.Failed Activation { get; }
    }

    public sealed record Rejected : NavigationDescendantLensResult
    {
        internal Rejected(
            DescendantSubjectLensRequest request,
            NavigationWorkspaceSnapshot snapshot,
            NavigationDescendantLensRejectionKind? validation,
            NavigationLensActivationResult.Rejected? activation)
            : base(request, snapshot)
        {
            if ((validation is null) == (activation is null))
            {
                throw new ArgumentException(
                    "A rejection must come from exactly one validation boundary.");
            }
            Validation = validation;
            Activation = activation;
        }

        public NavigationDescendantLensRejectionKind? Validation { get; }

        public NavigationLensActivationResult.Rejected? Activation { get; }
    }
}

/// <summary>Pure exact descendant+lens mapping over one stateless snapshot.</summary>
public static class NavigationDescendantLensEvaluation
{
    public static NavigationDescendantLensResult Evaluate(
        NavigationWorkspaceSnapshot snapshot,
        DescendantSubjectLensRequest request,
        ViewFacetRegistry registry,
        IViewFacetAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Evaluate(snapshot, request, registry, (_, _) => facts);
    }

    public static NavigationDescendantLensResult Evaluate(
        NavigationWorkspaceSnapshot snapshot,
        DescendantSubjectLensRequest request,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(availability);

        StructuralSubjectIdentity source = request.Source;
        StructuralSubjectIdentity destination =
            request.Destination.Subject;
        if (source != snapshot.ActiveSubject)
        {
            return Rejected(
                NavigationDescendantLensRejectionKind.SourceMismatch);
        }
        if (source.Workspace != destination.Workspace
            || source.Workspace != snapshot.Workspace)
        {
            return Rejected(
                NavigationDescendantLensRejectionKind.ForeignWorkspace);
        }

        StructuralSubjectIdentity.PackageSubject? sourcePackage =
            Package(source);
        StructuralSubjectIdentity.PackageSubject? destinationPackage =
            Package(destination);
        if (sourcePackage is null
            || destinationPackage is null
            || sourcePackage.Occurrence != destinationPackage.Occurrence
            || snapshot.ActiveOccurrence != sourcePackage.Occurrence)
        {
            return Rejected(
                NavigationDescendantLensRejectionKind.ForeignOccurrence);
        }
        if (!IsEligibleDescendant(snapshot, source, destination))
        {
            return Rejected(
                NavigationDescendantLensRejectionKind.NonDescendant);
        }

        NavigationLensActivationResult activation =
            NavigationLensActivation.ResolveExact(
                request.Destination,
                registry,
                availability(destination, snapshot.Inventory));
        return activation switch
        {
            NavigationLensActivationResult.Applied applied =>
                new NavigationDescendantLensResult.Applied(
                    request,
                    NavigationWorkspaceSnapshotEvaluation
                        .WithAppliedDestination(
                            snapshot,
                            destination,
                            applied.Outcome,
                            registry,
                            availability),
                    applied),
            NavigationLensActivationResult.Unavailable unavailable =>
                new NavigationDescendantLensResult.Unavailable(
                    request,
                    snapshot,
                    unavailable),
            NavigationLensActivationResult.Failed failed =>
                new NavigationDescendantLensResult.Failed(
                    request,
                    snapshot,
                    failed),
            NavigationLensActivationResult.Rejected rejected =>
                new NavigationDescendantLensResult.Rejected(
                    request,
                    snapshot,
                    validation: null,
                    rejected),
            _ => throw new InvalidOperationException(
                "Unknown exact lens activation result."),
        };

        NavigationDescendantLensResult.Rejected Rejected(
            NavigationDescendantLensRejectionKind reason) =>
            new(
                request,
                snapshot,
                reason,
                activation: null);
    }

    static bool IsEligibleDescendant(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity source,
        StructuralSubjectIdentity destination) =>
        (source, destination) switch
        {
            (
                StructuralSubjectIdentity.LibrarySubject library,
                StructuralSubjectIdentity.TypeSubject type) =>
                type.Library == library
                && snapshot.Types.Any(row => row.Row.Subject == type),
            (
                StructuralSubjectIdentity.AllLibrariesSubject libraries,
                StructuralSubjectIdentity.TypeSubject type) =>
                type.Library.Package == libraries.Package
                && snapshot.Libraries.Any(row =>
                    row.Subject == type.Library)
                && snapshot.Types.Any(row => row.Row.Subject == type),
            (
                StructuralSubjectIdentity.TypeSubject type,
                StructuralSubjectIdentity.MemberSubject member) =>
                member.DeclaringType == type
                && snapshot.Types.Any(row =>
                    row.Row.Subject == type
                    && row.Row.Members.Any(candidate =>
                        candidate.ContainingType == type
                        && candidate.Subject == member)),
            _ => false,
        };

    static StructuralSubjectIdentity.PackageSubject? Package(
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.PackageSubject package =>
                package,
            StructuralSubjectIdentity.AllLibrariesSubject libraries =>
                libraries.Package,
            StructuralSubjectIdentity.LibrarySubject library =>
                library.Package,
            StructuralSubjectIdentity.TypeSubject type =>
                type.Library.Package,
            StructuralSubjectIdentity.MemberSubject member =>
                member.DeclaringType.Library.Package,
            _ => null,
        };
}
