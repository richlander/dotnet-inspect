using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed class LibraryTypeMemberGroupPopulationInspectionBounds
{
    public LibraryTypeMemberGroupPopulationInspectionBounds(
        int maximumAssemblyBytes,
        int maximumMetadataRows,
        int maximumRetainedGroups,
        long maximumNameWorkBytes,
        int maximumRetainedTextCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMetadataRows);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedGroups);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumNameWorkBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumMetadataRows = maximumMetadataRows;
        MaximumRetainedGroups = maximumRetainedGroups;
        MaximumNameWorkBytes = maximumNameWorkBytes;
        MaximumRetainedTextCharacters =
            maximumRetainedTextCharacters;
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumMetadataRows { get; }
    public int MaximumRetainedGroups { get; }
    public long MaximumNameWorkBytes { get; }
    public int MaximumRetainedTextCharacters { get; }
}

public sealed record LibraryTypeMemberGroupPopulationSourcePlan
{
    public LibraryTypeMemberGroupPopulationSourcePlan(
        AssemblyTypeMemberGroupCategory category,
        AssemblyTypeMemberReceiverKinds receivers,
        AssemblyTypeMemberGroupTerminal terminal,
        IReadOnlyList<AssemblyTypeMemberGroupSelectionStage> selection,
        bool includeExactMemberCount)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if ((receivers & ~AssemblyTypeMemberReceiverKinds.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivers));
        }
        if (!Enum.IsDefined(terminal))
            throw new ArgumentOutOfRangeException(nameof(terminal));
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Any(static stage => stage is null))
        {
            throw new ArgumentException(
                "Member-group selection stages cannot contain null.",
                nameof(selection));
        }
        if (terminal == AssemblyTypeMemberGroupTerminal.Count
            && includeExactMemberCount)
        {
            throw new ArgumentException(
                "A Member-group Count cannot request child Counts.",
                nameof(includeExactMemberCount));
        }

        Category = category;
        Receivers = receivers;
        Terminal = terminal;
        Selection = selection.ToArray();
        IncludeExactMemberCount = includeExactMemberCount;
    }

    public AssemblyTypeMemberGroupCategory Category { get; }
    public AssemblyTypeMemberReceiverKinds Receivers { get; }
    public AssemblyTypeMemberGroupTerminal Terminal { get; }
    public IReadOnlyList<AssemblyTypeMemberGroupSelectionStage> Selection
    { get; }
    public bool IncludeExactMemberCount { get; }
}

public sealed class LibraryTypeMemberGroupPopulationInspectionRequest
{
    public LibraryTypeMemberGroupPopulationInspectionRequest(
        LibraryReference library,
        MetadataTypeDefinitionName type,
        LibraryTypeMemberGroupPopulationSourcePlan plan,
        LibraryTypeMemberGroupPopulationInspectionBounds bounds)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
    }

    public LibraryReference Library { get; }
    public MetadataTypeDefinitionName Type { get; }
    public LibraryTypeMemberGroupPopulationSourcePlan Plan { get; }
    public LibraryTypeMemberGroupPopulationInspectionBounds Bounds { get; }
}

public sealed class LibraryTypeMemberGroupPopulationSubject
{
    internal LibraryTypeMemberGroupPopulationSubject(
        LibraryContentReference apiContent,
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
        MetadataTypeDefinitionName type,
        MetadataTypeDefinitionAddress typeAddress,
        int assemblyBytes)
    {
        ApiContent = apiContent;
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
        Type = type;
        TypeAddress = typeAddress;
        AssemblyBytes = assemblyBytes;
    }

    public LibraryReference Library => ApiContent.Library;
    public LibraryContentReference ApiContent { get; }
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public Guid ModuleVersionId { get; }
    public MetadataTypeDefinitionName Type { get; }
    public MetadataTypeDefinitionAddress TypeAddress { get; }
    public int AssemblyBytes { get; }
}

public sealed class LibraryTypeMemberGroupPopulationCorrespondence
{
    internal LibraryTypeMemberGroupPopulationCorrespondence(
        LibraryTypeMemberGroupPopulationSubject subject,
        long metadataRows,
        AssemblyTypeMemberGroupPopulationOutcome population)
    {
        Subject = subject;
        MetadataRows = metadataRows;
        Population = population;
    }

    public LibraryTypeMemberGroupPopulationSubject Subject { get; }
    public long MetadataRows { get; }
    public AssemblyTypeMemberGroupPopulationOutcome Population { get; }
}

public enum LibraryTypeMemberGroupPopulationUnavailableReason
{
    TypeNotFound,
    TypeAmbiguous,
    TypeNotDefined,
}

public enum LibraryTypeMemberGroupPopulationInspectionBound
{
    AssemblyBytes,
    MetadataRows,
    TypeResolution,
    RetainedGroups,
    NameWorkBytes,
    RetainedTextCharacters,
}

public enum LibraryTypeMemberGroupPopulationInspectionRejectionKind
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryTypeMemberGroupPopulationInspectionFailureKind
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public abstract record LibraryTypeMemberGroupPopulationInspectionOutcome
{
    private protected LibraryTypeMemberGroupPopulationInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryTypeMemberGroupPopulationCorrespondence Correspondence)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Unavailable(
        LibraryTypeMemberGroupPopulationSubject? Subject,
        LibraryTypeMemberGroupPopulationUnavailableReason Reason)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Incomplete(
        LibraryTypeMemberGroupPopulationSubject? Subject,
        LibraryTypeMemberGroupPopulationInspectionBound Bound,
        long Measured,
        string? Detail = null)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeMemberGroupPopulationInspectionRejectionKind Kind)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Failed(
        LibraryTypeMemberGroupPopulationInspectionFailureKind Kind)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;
}

public static class LibraryTypeMemberGroupPopulationInspection
{
    public static LibraryTypeMemberGroupPopulationInspectionOutcome Execute(
        LibraryTypeMemberGroupPopulationInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryTypeMemberGroupPopulationInspectionOutcome
                .Rejected(
                    LibraryTypeMemberGroupPopulationInspectionRejectionKind
                        .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryTypeMemberGroupPopulationInspectionOutcome
        Inspect(
            scoped LibraryContentView view,
            LibraryTypeMemberGroupPopulationInspectionRequest request,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int assemblyBytes = view.Content.Length;
        if (assemblyBytes > request.Bounds.MaximumAssemblyBytes)
        {
            return new LibraryTypeMemberGroupPopulationInspectionOutcome
                .Incomplete(
                    Subject: null,
                    LibraryTypeMemberGroupPopulationInspectionBound
                        .AssemblyBytes,
                    assemblyBytes);
        }
        if (view.Content.IsEmpty)
            return Failed(
                LibraryTypeMemberGroupPopulationInspectionFailureKind
                    .MalformedMetadata);

        LibraryContentReference reference = view.Reference;
        return view.UseReadStream(
            content => Inspect(
                content,
                reference,
                request,
                assemblyBytes,
                cancellationToken));
    }

    private static LibraryTypeMemberGroupPopulationInspectionOutcome
        Inspect(
            Stream content,
            LibraryContentReference reference,
            LibraryTypeMemberGroupPopulationInspectionRequest request,
            int assemblyBytes,
            CancellationToken cancellationToken)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
            if (!session.HasMetadata)
            {
                return Failed(
                    LibraryTypeMemberGroupPopulationInspectionFailureKind
                        .NotManagedAssembly);
            }
            if (!session.IsAssembly)
            {
                return Failed(
                    LibraryTypeMemberGroupPopulationInspectionFailureKind
                        .ManagedModule);
            }

            AssemblyReferenceIdentity identity = session.AssemblyIdentity();
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Rejected(
                        LibraryTypeMemberGroupPopulationInspectionRejectionKind
                            .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryTypeMemberGroupPopulationInspectionFailureKind
                        .EmptyModuleVersionId);
            }

            using var metadataOperation = new MetadataOperationContext(
                new MetadataOperationPolicy(
                    request.Bounds.MaximumMetadataRows));
            using MetadataDeclarationSession declarationSession =
                session.CreateDeclarationSession(metadataOperation);
            if (declarationSession.ImageAdmission
                is MetadataImageAdmissionResult.Rejected rejected)
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Incomplete(
                        Subject: null,
                        LibraryTypeMemberGroupPopulationInspectionBound
                            .MetadataRows,
                        rejected.Failure.ImageMetadataRows);
            }
            long metadataRows =
                ((MetadataImageAdmissionResult.Admitted)
                    declarationSession.ImageAdmission).ImageMetadataRows;

            cancellationToken.ThrowIfCancellationRequested();
            TypeDeclarationResult declaration =
                session.ProbeDeclaration(request.Type);
            if (declaration
                is TypeDeclarationResult.BudgetExceeded budgetExceeded)
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Incomplete(
                        Subject: null,
                        LibraryTypeMemberGroupPopulationInspectionBound
                            .TypeResolution,
                        budgetExceeded.Budget,
                        budgetExceeded.Detail);
            }
            TypeDefinitionToken? definition = declaration switch
            {
                TypeDeclarationResult.Defined defined =>
                    defined.Definition,
                TypeDeclarationResult.DefinitionKindUnavailable unavailable =>
                    unavailable.Definition,
                _ => null,
            };
            if (definition is null)
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Unavailable(
                        Subject: null,
                        declaration switch
                        {
                            TypeDeclarationResult.Missing =>
                                LibraryTypeMemberGroupPopulationUnavailableReason
                                    .TypeNotFound,
                            TypeDeclarationResult.Ambiguous =>
                                LibraryTypeMemberGroupPopulationUnavailableReason
                                    .TypeAmbiguous,
                            TypeDeclarationResult.Forwarded
                                or TypeDeclarationResult.ExportedFromModule =>
                                LibraryTypeMemberGroupPopulationUnavailableReason
                                    .TypeNotDefined,
                            TypeDeclarationResult.Rejected =>
                                throw new BadImageFormatException(
                                    "The selected Type identity is malformed."),
                            _ => throw new InvalidOperationException(
                                "Unknown Type declaration result."),
                        });
            }

            MetadataTypeDefinitionAddress address =
                MetadataTypeDefinitionAddress.FromToken(
                    moduleVersionId,
                    definition.Value.Value);
            var subject =
                new LibraryTypeMemberGroupPopulationSubject(
                    reference,
                    identity,
                    moduleVersionId,
                    request.Type,
                    address,
                    assemblyBytes);
            AssemblyTypeMemberGroupPopulationOutcome population =
                session.TypeMemberGroups(
                    new(
                        address,
                        request.Plan.Category,
                        request.Plan.Receivers,
                        request.Plan.Terminal,
                        request.Plan.Selection,
                        request.Plan.IncludeExactMemberCount,
                        request.Bounds.MaximumRetainedGroups,
                        request.Bounds.MaximumNameWorkBytes,
                        request.Bounds.MaximumRetainedTextCharacters),
                    cancellationToken);
            return population switch
            {
                AssemblyTypeMemberGroupPopulationOutcome.Incomplete
                    incomplete =>
                    new LibraryTypeMemberGroupPopulationInspectionOutcome
                        .Incomplete(
                            subject,
                            incomplete.Bound switch
                            {
                                AssemblyTypeMemberGroupPopulationBound
                                        .RetainedGroups =>
                                    LibraryTypeMemberGroupPopulationInspectionBound
                                        .RetainedGroups,
                                AssemblyTypeMemberGroupPopulationBound
                                        .NameWorkBytes =>
                                    LibraryTypeMemberGroupPopulationInspectionBound
                                        .NameWorkBytes,
                                AssemblyTypeMemberGroupPopulationBound
                                        .RetainedTextCharacters =>
                                    LibraryTypeMemberGroupPopulationInspectionBound
                                        .RetainedTextCharacters,
                                _ => throw new InvalidOperationException(
                                    "Unknown MemberGroup bound."),
                            },
                            incomplete.Measured),
                AssemblyTypeMemberGroupPopulationOutcome.Failed =>
                    Failed(
                        LibraryTypeMemberGroupPopulationInspectionFailureKind
                            .MalformedMetadata),
                _ =>
                    new LibraryTypeMemberGroupPopulationInspectionOutcome
                        .Completed(
                            new(
                                subject,
                                metadataRows,
                                population)),
            };
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryTypeMemberGroupPopulationInspectionFailureKind
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryTypeMemberGroupPopulationInspectionFailureKind
                    .MalformedMetadata);
        }
    }

    private static
        LibraryTypeMemberGroupPopulationInspectionOutcome.Failed Failed(
            LibraryTypeMemberGroupPopulationInspectionFailureKind kind) =>
        new(kind);
}
