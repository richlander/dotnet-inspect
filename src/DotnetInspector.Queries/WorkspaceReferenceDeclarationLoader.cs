using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Explicit reference-population admission. Acquisition uses the supplied source;
/// subsequent declaration queries use only Workspace-owned images and sessions.
/// </summary>
public static class WorkspaceReferenceDeclarationLoader
{
    /// <summary>Consumes one operation to acquire an externally established exact target.</summary>
    public static Task<WorkspaceDeclarationContext> LoadAsync(
        InspectionWorkspace workspace,
        PackagePlatformSource source,
        PackageReferencePackCoordinate coordinate,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageSourceOperationLease operation,
        AssemblyContextGroupOptions? options = null) =>
        LoadCoreAsync(workspace, source, coordinate, null, population, work, operation, options);

    /// <summary>Consumes the source-issued selection unchanged, without choosing another target.</summary>
    public static Task<WorkspaceDeclarationContext> LoadAsync(
        InspectionWorkspace workspace,
        PackagePlatformSource source,
        PackagePlatformTargetSelection selection,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageSourceOperationLease operation,
        AssemblyContextGroupOptions? options = null) =>
        LoadCoreAsync(workspace, source, selection?.Coordinate, selection, population, work, operation, options);

    /// <summary>Admits already-realized source bytes, including a House adapter's source value.</summary>
    public static WorkspaceDeclarationContext Admit(
        InspectionWorkspace workspace,
        PackageReferenceRealization realization,
        AssemblyContextGroupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(realization);
        options ??= new();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        int order = workspace.BeginDeclarationContext();
        return Complete(workspace, order, realization, options, cancellationToken);
    }

    static async Task<WorkspaceDeclarationContext> LoadCoreAsync(
        InspectionWorkspace workspace,
        PackagePlatformSource source,
        PackageReferencePackCoordinate? coordinate,
        PackagePlatformTargetSelection? selection,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageSourceOperationLease operation,
        AssemblyContextGroupOptions? options)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transferred = false;
        try
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(population);
            ArgumentNullException.ThrowIfNull(work);
            options ??= new();
            options.Validate();
            CancellationToken cancellationToken = operation.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            int order = workspace.BeginDeclarationContext();
            transferred = true;
            PackagePlatformSourceOutcome<PackageReferenceRealization> result = await (
                selection is null
                    ? source.RealizeAsync(coordinate, population, work, operation)
                    : source.RealizeAsync(selection, population, work, operation)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded success)
                return Complete(workspace, order, success.Value, options, cancellationToken);
            var failure = (PackagePlatformSourceOutcome<PackageReferenceRealization>.NotSucceeded)result;
            WorkspaceReferenceSourceFailureKind kind = failure switch
            {
                PackagePlatformSourceOutcome<PackageReferenceRealization>.Unavailable =>
                    WorkspaceReferenceSourceFailureKind.Unavailable,
                PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected =>
                    WorkspaceReferenceSourceFailureKind.Rejected,
                PackagePlatformSourceOutcome<PackageReferenceRealization>.Incomplete =>
                    WorkspaceReferenceSourceFailureKind.Incomplete,
                PackagePlatformSourceOutcome<PackageReferenceRealization>.Failed =>
                    WorkspaceReferenceSourceFailureKind.Failed,
                _ => throw new InvalidOperationException("Unknown Platform reference source outcome."),
            };
            return workspace.PublishDeclarationContext(new(
                new(workspace.Identity, order,
                    new WorkspaceDeclarationRequest.PlatformReference(coordinate, population),
                    isRealized: false, [],
                    [new WorkspaceDeclarationFailure.ReferenceSource(
                        failure.Generation, kind, failure.Diagnostic)]),
                group: null));
        }
        finally
        {
            if (!transferred)
                operation.Dispose();
        }
    }

    static WorkspaceDeclarationContext Complete(
        InspectionWorkspace workspace,
        int order,
        PackageReferenceRealization realization,
        AssemblyContextGroupOptions options,
        CancellationToken cancellationToken)
    {
        var evidence = new WorkspaceReferenceSourceEvidence(realization);
        var request = new WorkspaceDeclarationRequest.PlatformReference(
            realization.Coordinate, realization.Population);
        PlatformFamilyTarget target = realization.Coordinate.Target;
        AssemblyResolutionProvenance provenance = AssemblyResolutionProvenance.Platform(
            target.Family switch
            {
                PlatformFamily.DotNetRuntime => "Microsoft.NETCore.App",
                PlatformFamily.AspNetCore => "Microsoft.AspNetCore.App",
                _ => throw new InvalidOperationException("Unknown Platform family."),
            },
            target.Version.Value, "NuGet reference pack");
        ImmutableArray<ResolvedAssemblyReference> assemblies = [
            .. realization.Libraries.Select(library =>
                ResolvedAssemblyReference.Create(library.Identity,
                    path: null, library.OpenRead, provenance))];
        RetainedAssemblyContextGroup retained = RetainedAssemblyContextGroup.Create(
            workspace, assemblies, options, cancellationToken);
        if (retained is RetainedAssemblyContextGroup.Rejected rejected)
        {
            return workspace.PublishDeclarationContext(new(
                new(workspace.Identity, order, request, isRealized: false, [],
                    [new WorkspaceDeclarationFailure.ReferenceImage(evidence,
                        realization.Libraries[rejected.AssemblyIndex].Path, rejected.Failure)]),
                group: null));
        }
        AssemblyContextGroup group = ((RetainedAssemblyContextGroup.Ready)retained).Group;
        var members = ImmutableArray.CreateBuilder<WorkspaceDeclarationMember>(assemblies.Length);
        for (int index = 0; index < assemblies.Length; index++)
        {
            ResolvedAssemblyReference assembly = group.Participants[index].Assembly;
            members.Add(new(new(workspace.Identity, order, index),
                new ExactLibrarySourceCoordinate.Platform(new(target.Family), new(assembly.Identity)),
                assembly.Identity,
                new WorkspaceDeclarationOrigin.PlatformReference(evidence, realization.Libraries[index].Path),
                assembly.Provenance));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return workspace.PublishDeclarationContext(new(
            new(workspace.Identity, order, request, isRealized: true, members.MoveToImmutable(), []),
            group));
    }
}
