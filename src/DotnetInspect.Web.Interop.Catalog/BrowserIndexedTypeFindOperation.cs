using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Web.Interop.Catalog;

internal sealed record BrowserIndexedTypeFindInspection(
    InspectionEnvelope<EcosystemFindIndexedIntegrationResult.Type> Find);

internal abstract record BrowserIndexedTypeFindExecutionResult
{
    private protected BrowserIndexedTypeFindExecutionResult() { }

    internal sealed record Completed(
        BrowserIndexedTypeFindInspection Result) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Rejected(string Reason) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Unavailable(string Reason) :
        BrowserIndexedTypeFindExecutionResult;

    internal sealed record Stale(string Reason) :
        BrowserIndexedTypeFindExecutionResult;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserIndexedTypeFindOperation
{
    readonly object _gate = new();
    PublicationState? _current;
    string? _realizationId;
    long _latestGeneration = -1;

    internal BrowserIndexedTypeFindExecutionResult Execute(
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        string realizationId,
        string text,
        int maximumRows,
        long resultGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);

        PublicationState publication;
        lock (_gate)
        {
            if (_realizationId != realizationId)
            {
                _realizationId = realizationId;
                _latestGeneration = -1;
                _current = null;
            }
            if (resultGeneration <= _latestGeneration)
            {
                return new BrowserIndexedTypeFindExecutionResult.Stale(
                    "A newer indexed Type Find result is already current.");
            }

            _latestGeneration = resultGeneration;
            publication = new(realizationId, resultGeneration);
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
            if (ReferenceEquals(_current, publication))
            {
                return new BrowserIndexedTypeFindExecutionResult.Completed(
                    new(find));
            }
        }

        return new BrowserIndexedTypeFindExecutionResult.Stale(
            "A newer indexed Type Find result replaced this operation.");
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _realizationId = null;
            _latestGeneration = -1;
            _current = null;
        }
    }

    sealed record PublicationState(
        string RealizationId,
        long Generation);
}
