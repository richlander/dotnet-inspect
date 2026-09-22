using System.Runtime.ExceptionServices;
using DotnetInspector.Queries;

namespace DotnetInspector.EcosystemLoading;

public enum EcosystemPopulationChildAdmissionUnsupportedReason
{
    LibrariesWithoutArtifactSession,
    ArtifactSessionWithoutLibraries,
}

/// <summary>
/// One resource-bearing loader child and its actual Workspace-admission
/// disposition.
/// </summary>
public abstract class EcosystemPopulationChildAdmission
{
    private protected EcosystemPopulationChildAdmission(
        EcosystemPopulationChildSettlement child,
        IReadOnlyList<EcosystemPopulationLoadedLibraryReference> libraries)
    {
        Child = child;
        Libraries = Array.AsReadOnly([.. libraries]);
    }

    public EcosystemPopulationChildSettlement Child { get; }

    public IReadOnlyList<EcosystemPopulationLoadedLibraryReference> Libraries
    {
        get;
    }

    public sealed class Attempted : EcosystemPopulationChildAdmission
    {
        internal Attempted(
            EcosystemPopulationChildSettlement child,
            IReadOnlyList<EcosystemPopulationLoadedLibraryReference> libraries,
            WorkspaceLibraryAdmissionOutcome outcome)
            : base(child, libraries) =>
            Outcome = outcome;

        public WorkspaceLibraryAdmissionOutcome Outcome { get; }
    }

    public sealed class Unsupported : EcosystemPopulationChildAdmission
    {
        internal Unsupported(
            EcosystemPopulationChildSettlement child,
            IReadOnlyList<EcosystemPopulationLoadedLibraryReference> libraries,
            EcosystemPopulationChildAdmissionUnsupportedReason reason,
            IReadOnlyList<Exception> retirementFailures)
            : base(child, libraries)
        {
            Reason = reason;
            RetirementFailures =
                Array.AsReadOnly([.. retirementFailures]);
        }

        public EcosystemPopulationChildAdmissionUnsupportedReason Reason
        {
            get;
        }

        public IReadOnlyList<Exception> RetirementFailures { get; }
    }
}

/// <summary>
/// Exact historical correspondence between one loaded Library and one
/// accepted Workspace occurrence.
/// </summary>
public sealed class EcosystemPopulationLibraryAdmissionCorrespondence
{
    internal EcosystemPopulationLibraryAdmissionCorrespondence(
        EcosystemPopulationLoadReceipt loadReceipt,
        EcosystemPopulationLoadedLibraryReference loadedLibrary,
        WorkspaceLibraryAdmissionReceipt admission,
        WorkspaceLibraryOccurrence occurrence)
    {
        LoadReceipt = loadReceipt;
        LoadedLibrary = loadedLibrary;
        Admission = admission;
        Occurrence = occurrence;
    }

    public EcosystemPopulationLoadReceipt LoadReceipt { get; }

    public EcosystemPopulationChildSettlement Child =>
        LoadedLibrary.ChildSettlement;

    public EcosystemPopulationLoadedLibraryReference LoadedLibrary { get; }

    public WorkspaceLibraryAdmissionReceipt Admission { get; }

    public WorkspaceLibraryOccurrence Occurrence { get; }
}

/// <summary>
/// Historical evidence that one exact Focus Library from one Ecosystem load
/// was accepted by one Workspace admission.
/// </summary>
public sealed class EcosystemPopulationLibraryContributionWitness
{
    internal EcosystemPopulationLibraryContributionWitness(
        EcosystemPopulationLibraryAdmissionCorrespondence correspondence) =>
        Correspondence = correspondence;

    public EcosystemPopulationLibraryAdmissionCorrespondence Correspondence
    {
        get;
    }
}

/// <summary>
/// Resource-free result of composing one loader outcome with Workspace
/// admission.
/// </summary>
public sealed class EcosystemPopulationAdmissionResult
{
    internal EcosystemPopulationAdmissionResult(
        EcosystemPopulationLoadReceipt loadReceipt,
        IReadOnlyList<EcosystemPopulationChildAdmission> childAdmissions,
        IReadOnlyList<EcosystemPopulationLibraryAdmissionCorrespondence>
            libraries,
        IReadOnlyList<EcosystemPopulationLibraryContributionWitness>
            contributions)
    {
        LoadReceipt = loadReceipt;
        ChildAdmissions = Array.AsReadOnly([.. childAdmissions]);
        Libraries = Array.AsReadOnly([.. libraries]);
        Contributions = Array.AsReadOnly([.. contributions]);
    }

    public EcosystemPopulationLoadReceipt LoadReceipt { get; }

    public IReadOnlyList<EcosystemPopulationChildAdmission> ChildAdmissions
    {
        get;
    }

    public IReadOnlyList<EcosystemPopulationLibraryAdmissionCorrespondence>
        Libraries
    {
        get;
    }

    public IReadOnlyList<EcosystemPopulationLibraryContributionWitness>
        Contributions
    {
        get;
    }
}

/// <summary>
/// Unexpected admission-orchestration failure retaining all completed
/// dispositions that preceded it.
/// </summary>
public sealed class EcosystemPopulationAdmissionException : Exception
{
    internal EcosystemPopulationAdmissionException(
        EcosystemPopulationLoadReceipt loadReceipt,
        IReadOnlyList<EcosystemPopulationChildAdmission> childAdmissions,
        IReadOnlyList<EcosystemPopulationLibraryAdmissionCorrespondence>
            libraries,
        IReadOnlyList<EcosystemPopulationLibraryContributionWitness>
            contributions,
        Exception failure,
        Exception? retirementFailure)
        : base(
            "Ecosystem population admission failed unexpectedly.",
            retirementFailure is null
                ? failure
                : new AggregateException(failure, retirementFailure))
    {
        LoadReceipt = loadReceipt;
        ChildAdmissions = Array.AsReadOnly([.. childAdmissions]);
        Libraries = Array.AsReadOnly([.. libraries]);
        Contributions = Array.AsReadOnly([.. contributions]);
        RetirementFailure = retirementFailure;
    }

    public EcosystemPopulationLoadReceipt LoadReceipt { get; }

    public IReadOnlyList<EcosystemPopulationChildAdmission> ChildAdmissions
    {
        get;
    }

    public IReadOnlyList<EcosystemPopulationLibraryAdmissionCorrespondence>
        Libraries
    {
        get;
    }

    public IReadOnlyList<EcosystemPopulationLibraryContributionWitness>
        Contributions
    {
        get;
    }

    public Exception? RetirementFailure { get; }
}

/// <summary>
/// Composes one exact loader result with ordinary Workspace Library admission.
/// </summary>
public static class EcosystemPopulationAdmissionOperation
{
    public static ValueTask<EcosystemPopulationAdmissionResult> AdmitAsync(
        InspectionWorkspace workspace,
        EcosystemPopulationLoadOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            EcosystemPopulationLoadOutcome.Completed completed =>
                AdmitOwningAsync(
                    workspace,
                    outcome.Receipt,
                    completed.Owners),
            EcosystemPopulationLoadOutcome.Incomplete incomplete =>
                AdmitOwningAsync(
                    workspace,
                    outcome.Receipt,
                    incomplete.Owners),
            _ => ValueTask.FromResult(
                new EcosystemPopulationAdmissionResult(
                    outcome.Receipt,
                    [],
                    [],
                    [])),
        };
    }

    static async ValueTask<EcosystemPopulationAdmissionResult>
        AdmitOwningAsync(
            InspectionWorkspace workspace,
            EcosystemPopulationLoadReceipt loadReceipt,
            EcosystemPopulationOwnerBatch owners)
    {
        List<EcosystemPopulationChildAdmission> childAdmissions = [];
        List<EcosystemPopulationLibraryAdmissionCorrespondence> libraries =
            [];
        List<EcosystemPopulationLibraryContributionWitness> contributions =
            [];

        try
        {
            foreach (EcosystemPopulationChildSettlement child
                in loadReceipt.Children)
            {
                bool hasLibraries = owners.Libraries.Any(
                    library => ReferenceEquals(
                        library.ChildSettlement,
                        child));
                bool hasArtifactSession =
                    owners.ArtifactSessionChildren.Any(
                        candidate => ReferenceEquals(candidate, child));
                if (!hasLibraries && !hasArtifactSession)
                    continue;

                EcosystemPopulationChildAuthorityTransfer transfer =
                    owners.TakeChildAuthorities(child);
                if (!hasLibraries || !hasArtifactSession)
                {
                    Exception[] retirementFailures =
                        await RetireCapturingAsync(transfer);
                    childAdmissions.Add(
                        new EcosystemPopulationChildAdmission.Unsupported(
                            child,
                            transfer.Libraries,
                            hasLibraries
                                ? EcosystemPopulationChildAdmissionUnsupportedReason
                                    .LibrariesWithoutArtifactSession
                                : EcosystemPopulationChildAdmissionUnsupportedReason
                                    .ArtifactSessionWithoutLibraries,
                            retirementFailures));
                    continue;
                }

                WorkspaceLibraryAdmissionOutcome admission =
                    await AdmitChildAsync(
                        workspace,
                        loadReceipt,
                        transfer);
                childAdmissions.Add(
                    new EcosystemPopulationChildAdmission.Attempted(
                        child,
                        transfer.Libraries,
                        admission));
                if (admission
                    is not WorkspaceLibraryAdmissionOutcome.Accepted accepted)
                {
                    continue;
                }

                for (int index = 0;
                    index < transfer.Libraries.Count;
                    index++)
                {
                    var correspondence =
                        new EcosystemPopulationLibraryAdmissionCorrespondence(
                            loadReceipt,
                            transfer.Libraries[index],
                            accepted.Receipt,
                            accepted.Receipt.Occurrences[index]);
                    libraries.Add(correspondence);
                    if ((correspondence.LoadedLibrary.Roles
                            & EcosystemPopulationLibraryRole.Focus)
                        != 0)
                    {
                        contributions.Add(
                            new(
                                correspondence));
                    }
                }
            }

            await owners.DisposeAsync();
            return new(
                loadReceipt,
                childAdmissions,
                libraries,
                contributions);
        }
        catch (Exception failure)
        {
            Exception? retirementFailure =
                await CaptureRetirementFailureAsync(owners);
            throw new EcosystemPopulationAdmissionException(
                loadReceipt,
                childAdmissions,
                libraries,
                contributions,
                failure,
                retirementFailure);
        }
    }

    static async ValueTask<WorkspaceLibraryAdmissionOutcome>
        AdmitChildAsync(
            InspectionWorkspace workspace,
            EcosystemPopulationLoadReceipt loadReceipt,
            EcosystemPopulationChildAuthorityTransfer transfer)
    {
        try
        {
            WorkspaceLibraryAdmissionOutcome outcome =
                await workspace.AdmitLibraryBatchAsync(
                    loadReceipt.Request.RegistrationRevision,
                    transfer.ArtifactSession!,
                    transfer.Owners);
            transfer.MarkConsumed();
            return outcome;
        }
        catch (Exception failure)
        {
            Exception? retirementFailure =
                await CaptureRetirementFailureAsync(transfer);
            if (retirementFailure is not null)
            {
                throw new AggregateException(
                    "Workspace admission and local authority retirement both failed.",
                    failure,
                    retirementFailure);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    static async ValueTask<Exception?>
        CaptureRetirementFailureAsync(IAsyncDisposable resource)
    {
        try
        {
            await resource.DisposeAsync();
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    static async ValueTask<Exception[]> RetireCapturingAsync(
        IAsyncDisposable resource)
    {
        Exception? failure =
            await CaptureRetirementFailureAsync(resource);
        return failure switch
        {
            AggregateException aggregate =>
                [.. aggregate.Flatten().InnerExceptions],
            not null => [failure],
            _ => [],
        };
    }
}
