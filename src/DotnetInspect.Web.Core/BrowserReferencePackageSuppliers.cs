using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web;

internal sealed record BrowserReferencePackageSupplierCoordinate(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken,
    string? PackageId,
    string? PackageVersion);

[SupportedOSPlatform("browser")]
internal static class BrowserReferencePackageSuppliers
{
    internal static async Task<BrowserReferencePackageSupplierCoordinate[]>
        ResolveAsync(
            BrowserInspectionScope scope,
            BrowserPackageCoordinate retained,
            string assemblyId,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline,
            Func<BrowserPackageCoordinate, Task<PackageDependencyEvidenceRoot>>
                dependencyEvidence)
    {
        PackageCompileAsset asset = retained.CompileAsset(assemblyId);
        BrowserWorkspaceParticipant participant =
            scope.SurfaceParticipant(retained, asset);
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>> rows =
            scope.UseSurfaceParticipant(
                participant,
                AssemblyContextReferencesQuery.ExecuteParticipantRows);
        if (rows is not AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceRow>>.Available available)
            return [];

        AssemblyReferenceRow[] references =
        [
            .. available.Value
                .OrderBy(reference => reference.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.Version,
                    StringComparer.Ordinal)
                .ThenBy(reference => reference.Culture,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.PublicKeyToken,
                    StringComparer.OrdinalIgnoreCase),
        ];
        if (references.Length == 0)
            return [];

        PackageDependencyEvidenceRoot root =
            await dependencyEvidence(retained)
                .ConfigureAwait(false);
        IPackageSourceClient source = BrowserPackageWorkspace.Gallery;
        await using PackageSourceSettlementLease sourceLease =
            PackageSourceSettlementService.IssueLease(
                authority => ReferenceEquals(
                    authority.Association,
                    source.Source.Association)
                    ? source
                    : throw new InvalidOperationException(
                        "Reference supplier routing requested another package source."));
        TimeSpan timeout =
            BrowserPackageWorkspace.SourceSettlementOperationTimeout(
                deadline.Remaining);
        PackageDependencyTraversalOutcome traversal;
        using (PackageSourceOperationLease traversalOperation =
            sourceLease.IssueOperationLease(
                deadline.Token,
                timeout,
                timeout))
        {
            traversal = await PackageDependencyTraversalQuery.ExecuteAsync(
                new PackageDependencyTraversalRequest(
                    [new PackageDependencyTraversalRootOccurrence(
                        root,
                        PackageDependencyTraversalExpansionAuthority
                            .RecursiveSources,
                        PackageDependencyTraversalRootRecurrenceAuthority
                            .ExactCoordinate)],
                    new TraversalTargetFrameworkPolicy(retained.Framework),
                    new PackageDependencyTraversalCandidateAdapter(
                        BrowserPackageWorkspace.PackageSourceAuthorization),
                    new AuthorizedPackageDependencyManifestSource(),
                    new PackageDependencyTraversalWorkBudget(1_024, 16_384)),
                traversalOperation).ConfigureAwait(false);
        }

        (AssemblyReferenceResolutionGenerationReceipt generation,
            MemberCallGraphFocalScopeReceipt focalScope) =
            await scope.CaptureAssemblyReferenceSupplierScopeAsync()
                .ConfigureAwait(false);
        PackageHouseOperation operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Realize,
            requestTimeout: timeout,
            operationTimeout: timeout);
        var executions =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationExecution>();
        for (int edgeIndex = 0; edgeIndex < traversal.Edges.Length; edgeIndex++)
        {
            if (!traversal.RootReachability[0].IsEdgeAdmitted(
                    edgeIndex,
                    out _)
                || traversal.Edges[edgeIndex].Authority
                    != PackageDependencyTraversalEdgeEmissionAuthority
                        .ResolvedCandidate)
                continue;
            executions.Add(PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex,
                    operation,
                    PackageHouseTargetContext.Exact(retained.Framework))));
        }

        PackageAssemblyReferenceSupplierAssociation association =
            PackageAssemblyReferenceSupplierAssociation.Create(new(
                generation,
                focalScope,
                traversal,
                rootOccurrenceIndex: 0,
                executions.ToImmutable()));
        BrowserPackageWorkspace.BrowserSessionPackageStore store =
            BrowserPackageWorkspace.PackageStoreFor(source);
        var house = new PackageHouse(
            BrowserPackageWorkspace.PackageSourceAuthorization,
            new PackagePayloadAcquisitionPlan(
                (authority, _) => ReferenceEquals(
                    authority.Association,
                    source.Source.Association)
                    ? store
                    : throw new InvalidOperationException(
                        "Reference supplier routing requested another package store."),
                BrowserPackageWorkspace.PackageLimits,
                new BrowserPackageWorkspace.BrowserPackageOperationTransferPolicy(
                    store, deadline),
                access: PackagePayloadAccess.Ranged));
        using PackageSourceOperationLease supplierOperation =
            sourceLease.IssueOperationLease(
                deadline.Token,
                operation.RequestTimeout,
                operation.OperationTimeout);
        var suppliers =
            new BrowserReferencePackageSupplierCoordinate[references.Length];
        for (int index = 0; index < references.Length; index++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            AssemblyReferenceRow reference = references[index];
            AssemblyReferenceIdentity identity = new(
                reference.Name,
                Version.TryParse(reference.Version, out Version? parsed)
                    ? parsed
                    : null,
                reference.Culture,
                reference.PublicKeyToken);
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(identity),
                AssemblyBindingOrigin.FromAssembly(participant.Assembly),
                AssemblyResolutionScope.Any);
            PackageAssemblyReferenceSupplierOutcome outcome =
                await association.ResolveAsync(
                    request,
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    supplierOperation).ConfigureAwait(false);
            PackageSourceCoordinate? supplier = outcome
                is PackageAssemblyReferenceSupplierOutcome.Selected selected
                    ? selected.Selection.Evidence.Route.Candidate.Coordinate
                    : null;
            suppliers[index] = new(
                reference.Name,
                reference.Version,
                reference.Culture,
                reference.PublicKeyToken,
                supplier?.PackageId,
                supplier?.Version);
        }
        return suppliers;
    }
}
