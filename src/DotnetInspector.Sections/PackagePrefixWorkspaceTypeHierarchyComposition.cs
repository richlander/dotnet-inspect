using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record PackagePrefixWorkspaceTypeHierarchyRequest
{
    public PackagePrefixWorkspaceTypeHierarchyRequest(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        PackagePrefixWorkspaceScopeRealizationOutcome.Settled realization,
        WorkspaceDeclarationContext focusContext,
        WorkspaceTypeHierarchySubjectRelationsRequest hierarchy)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(focusContext);
        ArgumentNullException.ThrowIfNull(hierarchy);

        if (!ReferenceEquals(
                scope.Revision.Workspace,
                workspace.Identity)
            || !ReferenceEquals(
                focusContext.Receipt.Workspace,
                workspace.Identity))
        {
            throw new ArgumentException(
                "The Scope and focus context must belong to the selected "
                    + "Workspace.");
        }
        if (!focusContext.Receipt.Members.Any(member =>
                ReferenceEquals(
                    member.Occurrence,
                    hierarchy.Focus.Occurrence)))
        {
            throw new ArgumentException(
                "The exact hierarchy focus must belong to the selected focus "
                    + "context.",
                nameof(hierarchy));
        }
        WorkspaceScopeSnapshot? realizedScope =
            realization.ScopeOperation switch
            {
                WorkspaceScopeOperationResult.Committed committed =>
                    committed.Snapshot,
                WorkspaceScopeOperationResult.NoEffect noEffect =>
                    noEffect.Snapshot,
                _ => null,
            };
        if (realizedScope is null
            || !ReferenceEquals(
                realizedScope.Revision.Workspace,
                workspace.Identity))
        {
            throw new ArgumentException(
                "Hierarchy composition requires a committed or no-effect "
                    + "package-prefix Scope realization.",
                nameof(realization));
        }

        Workspace = workspace;
        Scope = scope;
        Realization = realization;
        FocusContext = focusContext;
        Hierarchy = hierarchy;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public PackagePrefixWorkspaceScopeRealizationOutcome.Settled Realization
    { get; }

    public WorkspaceDeclarationContext FocusContext { get; }

    public WorkspaceTypeHierarchySubjectRelationsRequest Hierarchy { get; }
}

public sealed record PackagePrefixWorkspaceTypeHierarchyAdmission
{
    internal PackagePrefixWorkspaceTypeHierarchyAdmission(
        PackagePrefixWorkspaceCandidateResult candidate,
        WorkspacePackageOccurrenceDescriptor occurrence,
        WorkspaceDeclarationContextReceipt? context,
        WorkspacePackageDeclarationAdmissionFailure? failure,
        bool isFocusSource)
    {
        Candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));
        Occurrence = occurrence
            ?? throw new ArgumentNullException(nameof(occurrence));
        if (candidate.Occurrence is null
            || !ReferenceEquals(
                candidate.Occurrence.Occurrence.Identity,
                occurrence.Occurrence.Identity))
        {
            throw new ArgumentException(
                "An admission must retain its realized package candidate.",
                nameof(occurrence));
        }
        if ((context is null) == (failure is null)
            || isFocusSource && context is null)
        {
            throw new ArgumentException(
                "An admission requires exactly one admitted context or "
                    + "failure.");
        }

        Context = context;
        Failure = failure;
        IsFocusSource = isFocusSource;
    }

    public PackagePrefixWorkspaceCandidateResult Candidate { get; }

    public WorkspacePackageOccurrenceDescriptor Occurrence { get; }

    public WorkspaceDeclarationContextReceipt? Context { get; }

    public WorkspacePackageDeclarationAdmissionFailure? Failure { get; }

    public bool IsFocusSource { get; }

    public bool IsAdmitted => Context is not null;
}

public sealed record PackagePrefixWorkspaceTypeHierarchyEvidence
{
    internal PackagePrefixWorkspaceTypeHierarchyEvidence(
        PackagePrefixWorkspaceScopeRealizationOutcome.Settled realization,
        WorkspaceScopeSnapshot scope,
        WorkspaceDeclarationContextReceipt focus,
        ImmutableArray<PackagePrefixWorkspaceTypeHierarchyAdmission>
            admissions)
    {
        Realization = realization
            ?? throw new ArgumentNullException(nameof(realization));
        Scope = scope
            ?? throw new ArgumentNullException(nameof(scope));
        Focus = focus
            ?? throw new ArgumentNullException(nameof(focus));
        Admissions = admissions.IsDefault
            || admissions.Any(static admission => admission is null)
            ? throw new ArgumentException(
                "Hierarchy composition admissions must be initialized.",
                nameof(admissions))
            : admissions;
    }

    public PackagePrefixWorkspaceScopeRealizationOutcome.Settled Realization
    { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public WorkspaceDeclarationContextReceipt Focus { get; }

    public ImmutableArray<PackagePrefixWorkspaceTypeHierarchyAdmission>
        Admissions
    { get; }

    public bool IsComplete =>
        Realization.IsComplete
        && Admissions.All(static admission => admission.IsAdmitted);
}

public sealed class
    PackagePrefixWorkspaceTypeHierarchyContinuationAuthority
{
    private PackagePrefixWorkspaceTypeHierarchyContinuationAuthority(
        PackagePrefixWorkspaceScopeRealizationOutcome.Settled realization,
        WorkspaceScopeRevisionIdentity scopeRevision,
        WorkspaceDeclarationContext focusContext,
        WorkspaceDeclarationPopulation population,
        PackagePrefixWorkspaceTypeHierarchyEvidence evidence,
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority hierarchy)
    {
        Realization = realization;
        ScopeRevision = scopeRevision;
        FocusContext = focusContext;
        Population = population;
        Evidence = evidence;
        Hierarchy = hierarchy;
    }

    internal PackagePrefixWorkspaceScopeRealizationOutcome.Settled Realization
    { get; }

    internal WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    internal WorkspaceDeclarationContext FocusContext { get; }

    internal WorkspaceDeclarationPopulation Population { get; }

    internal PackagePrefixWorkspaceTypeHierarchyEvidence Evidence { get; }

    internal WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
        Hierarchy
    { get; }

    internal static
        PackagePrefixWorkspaceTypeHierarchyContinuationAuthority Capture(
            PackagePrefixWorkspaceTypeHierarchyRequest request,
            WorkspaceDeclarationPopulation population,
            PackagePrefixWorkspaceTypeHierarchyEvidence evidence,
            WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
                hierarchy) =>
        new(
            request.Realization,
            request.Scope.Revision.Identity,
            request.FocusContext,
            population,
            evidence,
            hierarchy);
}

public sealed record PackagePrefixWorkspaceTypeHierarchyExecution
{
    internal PackagePrefixWorkspaceTypeHierarchyExecution(
        WorkspaceTypeHierarchySubjectRelationsExecution hierarchy,
        PackagePrefixWorkspaceTypeHierarchyEvidence evidence,
        WorkspaceDeclarationPopulationReceipt population,
        PackagePrefixWorkspaceTypeHierarchyContinuationAuthority?
            continuationAuthority)
    {
        Hierarchy = hierarchy
            ?? throw new ArgumentNullException(nameof(hierarchy));
        Evidence = evidence
            ?? throw new ArgumentNullException(nameof(evidence));
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        if ((hierarchy.ContinuationAuthority is null)
            != (continuationAuthority is null))
        {
            throw new ArgumentException(
                "Composition continuation authority must exactly accompany "
                    + "the hierarchy continuation.",
                nameof(continuationAuthority));
        }

        ContinuationAuthority = continuationAuthority;
    }

    public WorkspaceTypeHierarchySubjectRelationsExecution Hierarchy { get; }

    public InspectionEnvelope<WorkspaceTypeHierarchySubjectRelationsDocument>
        Inspection => Hierarchy.Inspection;

    public PackagePrefixWorkspaceTypeHierarchyEvidence Evidence { get; }

    public WorkspaceDeclarationPopulationReceipt Population { get; }

    public PackagePrefixWorkspaceTypeHierarchyContinuationAuthority?
        ContinuationAuthority
    { get; }

    public bool IsComplete =>
        Evidence.IsComplete
        && Population.IsRealizationComplete
        && Inspection.Content.Relations.Evidence.IsComplete;
}

public abstract record PackagePrefixWorkspaceTypeHierarchyOutcome
{
    private protected PackagePrefixWorkspaceTypeHierarchyOutcome()
    {
    }

    public sealed record Completed(
        PackagePrefixWorkspaceTypeHierarchyExecution Execution)
        : PackagePrefixWorkspaceTypeHierarchyOutcome;

    public sealed record Unavailable(
        WorkspaceDeclarationPopulationFailure Failure,
        PackagePrefixWorkspaceTypeHierarchyEvidence Evidence)
        : PackagePrefixWorkspaceTypeHierarchyOutcome;
}

/// <summary>
/// Admits one realized package-prefix Scope population and delegates focused
/// hierarchy execution to the ordinary Workspace operation.
/// </summary>
public static class PackagePrefixWorkspaceTypeHierarchyComposition
{
    public static async ValueTask<
        PackagePrefixWorkspaceTypeHierarchyOutcome> ExecuteAsync(
        PackagePrefixWorkspaceTypeHierarchyRequest request,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        bool includeHidden = false,
        PackagePrefixWorkspaceTypeHierarchyContinuationAuthority?
            continuationAuthority = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceDeclarationPopulation population;
        PackagePrefixWorkspaceTypeHierarchyEvidence evidence;
        if (continuationAuthority is null)
        {
            (
                WorkspaceDeclarationPopulationCapture capture,
                evidence) =
                await PrepareAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            if (capture
                is not WorkspaceDeclarationPopulationCapture.Captured
                    captured)
            {
                return new PackagePrefixWorkspaceTypeHierarchyOutcome
                    .Unavailable(
                        ((WorkspaceDeclarationPopulationCapture.Rejected)
                            capture).Failure,
                        evidence);
            }

            population = captured.Population;
        }
        else
        {
            ValidateContinuation(request, continuationAuthority);
            population = continuationAuthority.Population;
            evidence = continuationAuthority.Evidence;
        }

        WorkspaceTypeHierarchySubjectRelationsExecution hierarchy =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                request.Hierarchy,
                policy,
                includeNonPublic,
                includeHidden,
                continuationAuthority?.Hierarchy,
                cancellationToken);
        hierarchy = RetainCompositionCompletion(
            hierarchy,
            evidence);
        PackagePrefixWorkspaceTypeHierarchyContinuationAuthority?
            outputAuthority =
            hierarchy.ContinuationAuthority is { } hierarchyAuthority
                ? PackagePrefixWorkspaceTypeHierarchyContinuationAuthority
                    .Capture(
                        request,
                        population,
                        evidence,
                        hierarchyAuthority)
                : null;
        return new PackagePrefixWorkspaceTypeHierarchyOutcome.Completed(
            new(
                hierarchy,
                evidence,
                population.Receipt,
                outputAuthority));
    }

    private static WorkspaceTypeHierarchySubjectRelationsExecution
        RetainCompositionCompletion(
            WorkspaceTypeHierarchySubjectRelationsExecution hierarchy,
            PackagePrefixWorkspaceTypeHierarchyEvidence evidence)
    {
        if (evidence.IsComplete
            || hierarchy.Inspection.Content.Relations.Count
                is not SubjectRelationPopulationCountOutcome.Counted)
        {
            return hierarchy;
        }

        SubjectRelationPopulationResult relations =
            hierarchy.Inspection.Content.Relations;
        var content =
            new WorkspaceTypeHierarchySubjectRelationsDocument(
                new(
                    relations.Binding,
                    relations.Evidence,
                    new SubjectRelationPopulationCountOutcome.Incomplete(),
                    relations.Rows),
                hierarchy.Inspection.Content.Implementers,
                hierarchy.Inspection.Content.DerivedTypes);
        var inspection =
            new InspectionEnvelope<
                WorkspaceTypeHierarchySubjectRelationsDocument>(
                content,
                hierarchy.Inspection.Share,
                hierarchy.Inspection.Diagnostics);
        return new(
            inspection,
            hierarchy.ContinuationAuthority);
    }

    private static async ValueTask<(
        WorkspaceDeclarationPopulationCapture Capture,
        PackagePrefixWorkspaceTypeHierarchyEvidence Evidence)> PrepareAsync(
        PackagePrefixWorkspaceTypeHierarchyRequest request,
        CancellationToken cancellationToken)
    {
        WorkspaceDeclarationMember focusMember =
            request.FocusContext.Receipt.Members.Single(member =>
                ReferenceEquals(
                    member.Occurrence,
                    request.Hierarchy.Focus.Occurrence));
        var contexts =
            ImmutableArray.CreateBuilder<WorkspaceDeclarationContext>();
        contexts.Add(request.FocusContext);
        var selected =
            new HashSet<WorkspaceDeclarationContext>(
                ReferenceEqualityComparer.Instance)
            {
                request.FocusContext,
            };
        var admissions =
            ImmutableArray.CreateBuilder<
                PackagePrefixWorkspaceTypeHierarchyAdmission>();
        var excludedOccurrences =
            ImmutableArray.CreateBuilder<
                WorkspaceDeclarationOccurrence>();

        foreach (PackagePrefixWorkspaceCandidateResult candidate
            in request.Realization.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Disposition
                    is not PackagePrefixWorkspaceCandidateDisposition.Existing
                        and not
                            PackagePrefixWorkspaceCandidateDisposition.Admitted
                || candidate.Occurrence is not { } realizedOccurrence)
            {
                continue;
            }

            WorkspacePackageOccurrenceDescriptor? currentOccurrence =
                request.Scope.Packages.FirstOrDefault(current =>
                    ReferenceEquals(
                        current.Occurrence.Identity,
                        realizedOccurrence.Occurrence.Identity));
            if (currentOccurrence is null)
            {
                admissions.Add(new(
                    candidate,
                    realizedOccurrence,
                    context: null,
                    new(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .OccurrenceNotCurrent),
                    isFocusSource: false));
                continue;
            }

            WorkspacePackageOccurrenceDescriptor occurrence =
                currentOccurrence;
            WorkspacePackageDeclarationAdmissionResult result =
                await request.Workspace.AdmitPackageScopeDeclarationAsync(
                        occurrence,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (result
                is WorkspacePackageDeclarationAdmissionResult.Admitted
                    admitted)
            {
                WorkspaceDeclarationOccurrence? excludedFocusSource =
                    ExcludedFocusSource(
                        request.FocusContext,
                        focusMember,
                        admitted.Admission.Context,
                        occurrence,
                        cancellationToken);
                bool isFocusSource =
                    excludedFocusSource is not null
                    || IsSamePackageScopeFocus(
                        focusMember,
                        occurrence);
                admissions.Add(new(
                    candidate,
                    occurrence,
                    admitted.Admission.Context.Receipt,
                    failure: null,
                    isFocusSource));
                if (selected.Add(admitted.Admission.Context))
                {
                    contexts.Add(admitted.Admission.Context);
                }
                if (excludedFocusSource is not null)
                {
                    excludedOccurrences.Add(
                        excludedFocusSource);
                }
                continue;
            }

            admissions.Add(new(
                candidate,
                occurrence,
                context: null,
                ((WorkspacePackageDeclarationAdmissionResult.Rejected)
                    result).Failure,
                isFocusSource: false));
        }

        var evidence =
            new PackagePrefixWorkspaceTypeHierarchyEvidence(
                request.Realization,
                request.Scope,
                request.FocusContext.Receipt,
                admissions.ToImmutable());
        return (
            request.Workspace.CaptureDeclarationPopulation(
                contexts.ToImmutable(),
                excludedOccurrences.ToImmutable()),
            evidence);
    }

    private static bool IsSamePackageScopeFocus(
        WorkspaceDeclarationMember focus,
        WorkspacePackageOccurrenceDescriptor occurrence) =>
        focus.Origin switch
        {
            WorkspaceDeclarationOrigin.PackageScope package =>
                ReferenceEquals(
                    package.Occurrence.Occurrence.Identity,
                    occurrence.Occurrence.Identity),
            _ => false,
        };

    private static WorkspaceDeclarationOccurrence?
        ExcludedFocusSource(
            WorkspaceDeclarationContext focusContext,
            WorkspaceDeclarationMember focus,
            WorkspaceDeclarationContext admittedContext,
            WorkspacePackageOccurrenceDescriptor occurrence,
            CancellationToken cancellationToken)
    {
        if (focus.Origin
            is not WorkspaceDeclarationOrigin.ContextLoad
            {
                Realized: RealizedMemberCoordinate.Package package,
            }
            || focusContext.Group is not { } focusGroup
            || admittedContext.Group is not { } admittedGroup)
        {
            return null;
        }
        if (!SamePackageAcquisitionTarget(
                package,
                occurrence.Occurrence.Package.Coordinate))
        {
            return null;
        }

        int focusIndex =
            focusContext.Receipt.Members.IndexOf(focus);
        if (focusIndex < 0
            || focusIndex >= focusGroup.Participants.Length)
        {
            return null;
        }

        Guid? focusModuleVersionId =
            ObservedModuleVersionId(
                focusGroup,
                focusGroup.Participants[focusIndex].Assembly,
                cancellationToken);
        if (focusModuleVersionId is null)
            return null;

        for (int index = 0;
             index < admittedContext.Receipt.Members.Length;
             index++)
        {
            WorkspaceDeclarationMember candidate =
                admittedContext.Receipt.Members[index];
            if (candidate.AssemblyIdentity == focus.AssemblyIdentity
                && ObservedModuleVersionId(
                    admittedGroup,
                    admittedGroup.Participants[index].Assembly,
                    cancellationToken)
                    == focusModuleVersionId)
            {
                return candidate.Occurrence;
            }
        }
        return null;
    }

    private static bool SamePackageAcquisitionTarget(
        RealizedMemberCoordinate.Package left,
        RealizedMemberCoordinate.Package right) =>
        left.PackageId == right.PackageId
        && left.Version == right.Version
        && left.Framework == right.Framework
        && left.RuntimeIdentifier == right.RuntimeIdentifier;

    private static Guid? ObservedModuleVersionId(
        AssemblyContextGroup group,
        ResolvedAssemblyReference assembly,
        CancellationToken cancellationToken)
    {
        AssemblyImageAccessResult<Guid> result =
            group.UseAssemblySession(
                assembly,
                cancellationToken,
                static (session, source) =>
                {
                    Guid moduleVersionId = session.ModuleVersionId();
                    source.Registration.BindObservedModuleVersionId(
                        moduleVersionId);
                    return moduleVersionId;
                });
        return result
            is AssemblyImageAccessResult<Guid>.Available available
                ? available.Value
                : null;
    }

    private static void ValidateContinuation(
        PackagePrefixWorkspaceTypeHierarchyRequest request,
        PackagePrefixWorkspaceTypeHierarchyContinuationAuthority authority)
    {
        if (!ReferenceEquals(
                authority.Realization,
                request.Realization)
            || !ReferenceEquals(
                authority.ScopeRevision,
                request.Scope.Revision.Identity)
            || !ReferenceEquals(
                authority.FocusContext,
                request.FocusContext))
        {
            throw new ArgumentException(
                "Hierarchy continuation authority belongs to a different "
                    + "registered Scope composition.",
                nameof(authority));
        }
    }
}
