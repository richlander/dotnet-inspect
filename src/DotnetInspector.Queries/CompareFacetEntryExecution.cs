namespace DotnetInspector.Queries;

/// <summary>
/// One exact Navigation-authorized Compare entry request.
/// </summary>
public sealed class CompareFacetEntryRequest
{
    internal CompareFacetEntryRequest(
        NavigationLensIdentity lens,
        ViewFacetDescriptor descriptor,
        StructuralSubjectIdentity.PackageSubject package)
    {
        ArgumentNullException.ThrowIfNull(lens);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(package);
        if (descriptor.Id != lens.Facet
            || descriptor.Kind != lens.Subject.Kind)
        {
            throw new ArgumentException(
                "A Compare entry descriptor must match its exact lens.",
                nameof(descriptor));
        }
        if (!ReferenceEquals(package.Workspace, lens.Subject.Workspace))
        {
            throw new ArgumentException(
                "A Compare entry package must belong to the exact subject Workspace.",
                nameof(package));
        }

        Lens = lens;
        Descriptor = descriptor;
        Package = package;
    }

    /// <summary>The exact subject-bound Compare lens authorized by Navigation.</summary>
    public NavigationLensIdentity Lens { get; }

    /// <summary>The public Compare descriptor selected by the private binding.</summary>
    public ViewFacetDescriptor Descriptor { get; }

    /// <summary>The exact requested structural subject.</summary>
    public StructuralSubjectIdentity Subject => Lens.Subject;

    /// <summary>The exact Workspace containing the requested subject.</summary>
    public StructuralSubjectIdentity.WorkspaceSubject Workspace =>
        Subject.Workspace;

    /// <summary>The exact retained Package occurrence containing the subject.</summary>
    public StructuralSubjectIdentity.PackageSubject Package { get; }
}

/// <summary>
/// One successful subject-scoped Compare entry. Diff and Clone are not encoded
/// here; they remain modes inside the selected entry.
/// </summary>
public abstract class CompareFacetEntry
{
    private protected CompareFacetEntry(CompareFacetEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public CompareFacetEntryRequest Request { get; }

    public sealed class Library : CompareFacetEntry
    {
        internal Library(CompareFacetEntryRequest request)
            : base(request)
        {
        }
    }

    public sealed class Type : CompareFacetEntry
    {
        internal Type(CompareFacetEntryRequest request)
            : base(request)
        {
            Subject = (StructuralSubjectIdentity.TypeSubject)request.Subject;
        }

        public StructuralSubjectIdentity.TypeSubject Subject { get; }
    }

    public sealed class Member : CompareFacetEntry
    {
        internal Member(CompareFacetEntryRequest request)
            : base(request)
        {
            Subject =
                (StructuralSubjectIdentity.MemberSubject)request.Subject;
        }

        public StructuralSubjectIdentity.MemberSubject Subject { get; }
    }
}

/// <summary>
/// Closed result of entering one Navigation-authorized Compare facet.
/// </summary>
public abstract class CompareFacetEntryResult
{
    private protected CompareFacetEntryResult(
        CompareFacetEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public CompareFacetEntryRequest Request { get; }

    public sealed class Available : CompareFacetEntryResult
    {
        internal Available(CompareFacetEntry entry)
            : base(RequireEntry(entry).Request)
        {
            Entry = entry;
        }

        public CompareFacetEntry Entry { get; }

        static CompareFacetEntry RequireEntry(CompareFacetEntry? entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return entry;
        }
    }

    public sealed class Unavailable : CompareFacetEntryResult
    {
        internal Unavailable(
            CompareFacetEntryRequest request,
            ViewFacetUnavailableReason reason)
            : base(request)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Reason = reason;
        }

        public ViewFacetUnavailableReason Reason { get; }
    }

    public sealed class Failed : CompareFacetEntryResult
    {
        internal Failed(
            CompareFacetEntryRequest request,
            string message,
            IViewFacetDiagnosticEvidence evidence)
            : base(request)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            ArgumentNullException.ThrowIfNull(evidence);
            Message = message;
            Evidence = evidence;
        }

        public string Message { get; }
        public IViewFacetDiagnosticEvidence Evidence { get; }
    }
}

/// <summary>
/// Consumes the Registry-private Compare execution target for one exact
/// Navigation activation and returns only the public subject-scoped entry.
/// </summary>
public static class CompareFacetEntryExecution
{
    public static CompareFacetEntryResult Execute(
        NavigationLensActivationResult activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        NavigationLensEvaluationBasis.ExactRequest basis =
            ExactBasis(activation);
        ViewFacetDescriptor descriptor = Descriptor(basis.Result);
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        if (!registry.TryGetDescriptor(
                activation.Request.Facet.Value,
                out ViewFacetDescriptor? registered)
            || !ReferenceEquals(registered, descriptor))
        {
            throw new ArgumentException(
                "The activation does not carry exact evidence from the product Registry.",
                nameof(activation));
        }

        ViewFacetExecutionBinding binding = registry.ActiveBindings.Single(
            candidate => candidate.Id == descriptor.Id);
        if (binding.Target is not InspectionViewFacetExecution target
            || !IsCompareTarget(target))
        {
            throw new ArgumentException(
                "The Navigation activation does not select a Compare facet.",
                nameof(activation));
        }

        CompareFacetEntryRequest request = new(
            activation.Request,
            descriptor,
            Package(activation.Request.Subject));
        ValidateTarget(target, request.Subject);

        return activation switch
        {
            NavigationLensActivationResult.Applied =>
                new CompareFacetEntryResult.Available(
                    Entry(target, request)),
            NavigationLensActivationResult.Unavailable =>
                new CompareFacetEntryResult.Unavailable(
                    request,
                    ((ViewFacetResolution.Unavailable)basis.Result).Reason),
            NavigationLensActivationResult.Failed =>
                Failed(request, (ViewFacetResolution.Failed)basis.Result),
            _ => throw new ArgumentException(
                "A Compare entry requires an applied, unavailable, or failed exact activation.",
                nameof(activation)),
        };
    }

    static NavigationLensEvaluationBasis.ExactRequest ExactBasis(
        NavigationLensActivationResult activation) =>
        activation switch
        {
            NavigationLensActivationResult.Applied applied =>
                (NavigationLensEvaluationBasis.ExactRequest)
                    applied.Outcome.Basis,
            NavigationLensActivationResult.Unavailable unavailable =>
                (NavigationLensEvaluationBasis.ExactRequest)
                    unavailable.Outcome.Basis,
            NavigationLensActivationResult.Failed failed =>
                (NavigationLensEvaluationBasis.ExactRequest)
                    failed.Outcome.Basis,
            _ => throw new ArgumentException(
                "A Compare entry requires an exact Registry activation result.",
                nameof(activation)),
        };

    static ViewFacetDescriptor Descriptor(ViewFacetResolution result) =>
        result switch
        {
            ViewFacetResolution.Available available =>
                available.Descriptor,
            ViewFacetResolution.Unavailable unavailable =>
                unavailable.Descriptor,
            ViewFacetResolution.Failed failed =>
                failed.Descriptor,
            _ => throw new ArgumentException(
                "A Compare entry requires an applicable Registry result.",
                nameof(result)),
        };

    static StructuralSubjectIdentity.PackageSubject Package(
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.AllLibrariesSubject allLibraries =>
                allLibraries.Package,
            StructuralSubjectIdentity.LibrarySubject library =>
                library.Package,
            StructuralSubjectIdentity.TypeSubject type =>
                type.Library.Package,
            StructuralSubjectIdentity.MemberSubject member =>
                member.DeclaringType.Library.Package,
            _ => throw new ArgumentException(
                "Compare is defined only for Package-bound Library, Type, and Member subjects.",
                nameof(subject)),
        };

    static bool IsCompareTarget(InspectionViewFacetExecution target) =>
        target is InspectionViewFacetExecution.LibraryCompare
            or InspectionViewFacetExecution.TypeCompare
            or InspectionViewFacetExecution.MemberCompare;

    static void ValidateTarget(
        InspectionViewFacetExecution target,
        StructuralSubjectIdentity subject)
    {
        bool valid = target switch
        {
            InspectionViewFacetExecution.LibraryCompare =>
                subject.Kind == StructuralSubjectKind.Library,
            InspectionViewFacetExecution.TypeCompare =>
                subject is StructuralSubjectIdentity.TypeSubject,
            InspectionViewFacetExecution.MemberCompare =>
                subject is StructuralSubjectIdentity.MemberSubject,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidOperationException(
                "The product Registry Compare target does not match its subject.");
        }
    }

    static CompareFacetEntry Entry(
        InspectionViewFacetExecution target,
        CompareFacetEntryRequest request) =>
        target switch
        {
            InspectionViewFacetExecution.LibraryCompare =>
                new CompareFacetEntry.Library(request),
            InspectionViewFacetExecution.TypeCompare =>
                new CompareFacetEntry.Type(request),
            InspectionViewFacetExecution.MemberCompare =>
                new CompareFacetEntry.Member(request),
            _ => throw new InvalidOperationException(
                "Unknown Compare execution target."),
        };

    static CompareFacetEntryResult.Failed Failed(
        CompareFacetEntryRequest request,
        ViewFacetResolution.Failed resolution) =>
        new(
            request,
            resolution.Message,
            resolution.Evidence);
}
