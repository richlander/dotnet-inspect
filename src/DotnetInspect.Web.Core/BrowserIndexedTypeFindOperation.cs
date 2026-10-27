using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Web;

internal sealed record BrowserIndexedTypeFindOperationResult(
    InspectionEnvelope<EcosystemFindIndexedIntegrationResult.Type> Find);

internal abstract record BrowserIndexedTypeFindExecutionResult
{
    private protected BrowserIndexedTypeFindExecutionResult() { }

    internal sealed record Completed(
        BrowserIndexedTypeFindOperationResult Result) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Rejected(string Reason) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Unavailable(string Reason) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Stale(string Reason) :
        BrowserIndexedTypeFindExecutionResult;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserIndexedTypeFindOperation : IDisposable
{
    readonly object _gate = new();
    PublicationState? _current;
    long _latestGeneration = -1;
    bool _disposed;

    internal BrowserIndexedTypeFindExecutionResult Execute(
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        string text,
        int maximumRows,
        long resultGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);

        PublicationState publication;
        lock (_gate)
        {
            if (_disposed)
            {
                return new BrowserIndexedTypeFindExecutionResult.Unavailable(
                    "Indexed Type Find is closed.");
            }
            if (resultGeneration <= _latestGeneration)
            {
                return new BrowserIndexedTypeFindExecutionResult.Stale(
                    "A newer indexed Type Find result is already current.");
            }

            _latestGeneration = resultGeneration;
            publication = new(resultGeneration);
            _current = publication;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new BrowserIndexedTypeFindExecutionResult.Rejected(
                "Indexed Type Find text must not be empty.");
        }
        if (maximumRows <= 0)
        {
            return new BrowserIndexedTypeFindExecutionResult.Rejected(
                "Indexed Type Find maximum rows must be positive.");
        }

        ImmutableArray<WorkspaceRegistration.Ecosystem> registrations =
        [
            .. operation.Definition.Plan.Registrations
                .OfType<WorkspaceRegistration.Ecosystem>(),
        ];
        var admittedArtifacts =
            ImmutableArray.CreateBuilder<EcosystemFindIndexArtifact>();
        var demands = ImmutableArray.CreateBuilder<
            EcosystemFindIndexedIntegrationSourceIdentity>();
        foreach (EcosystemFindIndexArtifact artifact in artifacts)
        {
            EcosystemWorkspaceRegistrationSelectionResult selection =
                EcosystemPackCatalog.SelectWorkspaceRegistration(
                    artifact.Ecosystem);
            if (selection
                is not EcosystemWorkspaceRegistrationSelectionResult.Known
                    known)
            {
                continue;
            }
            WorkspaceRegistration.Ecosystem? registration =
                registrations.SingleOrDefault(candidate =>
                    candidate.Declaration.Id == known.Declaration.Id);
            if (registration is null)
                continue;

            int demandCount = demands.Count;
            foreach (PackagePrefixDeclaration prefix in artifact.Prefixes)
            {
                if (registration.Declaration.Populations.Any(population =>
                    population
                        is WorkspaceEcosystemPopulationDeclaration.PackagePrefix
                            declared
                    && declared.Prefix == prefix))
                {
                    demands.Add(new(artifact.Ecosystem, prefix));
                }
            }
            if (demands.Count > demandCount)
                admittedArtifacts.Add(artifact);
        }

        InspectionEnvelope<EcosystemFindIndexedIntegrationResult.Type> find =
            EcosystemFindIndexedTypeInspection.Execute(
                admittedArtifacts,
                demands,
                TypeFindQuestion.Create(
                    [text],
                    FindVisibility.Public,
                    maximumRows),
                maximumRows,
                cancellationToken: cancellationToken);
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(_current, publication))
            {
                return new BrowserIndexedTypeFindExecutionResult.Completed(
                    new(find));
            }
        }

        return new BrowserIndexedTypeFindExecutionResult.Stale(
            "A newer indexed Type Find result replaced this operation.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _current = null;
        }
    }

    sealed record PublicationState(long Generation);
}

internal sealed partial class BrowserRetainedWorkspaceActivationOwner
{
    BrowserIndexedTypeFindOperation _indexedTypeFind = null!;

    internal async ValueTask<BrowserIndexedTypeFindExecutionResult>
        FindIndexedTypesAsync(
            string retainedDefinitionId,
            string realizationId,
            IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
            string text,
            int maximumRows,
            long resultGeneration,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentNullException.ThrowIfNull(artifacts);

        BrowserRetainedWorkspacePosting posting;
        BrowserIndexedTypeFindOperation indexedTypeFind;
        lock (_gate)
        {
            if (_active is not { } active
                || active.RetainedDefinitionId != retainedDefinitionId
                || active.RealizationId != realizationId)
            {
                return new BrowserIndexedTypeFindExecutionResult.Unavailable(
                    "The requested retained Workspace realization is not active.");
            }
            posting = active;
            indexedTypeFind = _indexedTypeFind;
        }

        WorkspaceRealizationOperationAdmission admission =
            await EnterOperationAsync(
                    retainedDefinitionId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new BrowserIndexedTypeFindExecutionResult.Unavailable(
                "The active Workspace realization is unavailable.");
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        lock (_gate)
        {
            if (!ReferenceEquals(_active, posting)
                || !ReferenceEquals(_indexedTypeFind, indexedTypeFind))
            {
                return new BrowserIndexedTypeFindExecutionResult.Stale(
                    "The active Workspace realization changed.");
            }
        }

        return indexedTypeFind.Execute(
            operation,
            artifacts,
            text,
            maximumRows,
            resultGeneration,
            cancellationToken);
    }

    void InitializeIndexedTypeFind() =>
        _indexedTypeFind = new BrowserIndexedTypeFindOperation();

    void DisposeIndexedTypeFind() =>
        _indexedTypeFind.Dispose();
}
