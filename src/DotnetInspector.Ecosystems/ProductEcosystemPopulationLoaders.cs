using DotnetInspector.EcosystemLoading;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Ecosystems;

/// <summary>
/// One host-authorized capability that realizes an exact Platform population
/// declaration through PlatformHouse.
/// </summary>
public interface IEcosystemPlatformPopulationCapability
{
    EcosystemPopulationCapabilityPlanIdentity PlanIdentity { get; }

    ValueTask<PlatformPopulationArtifactMaterializationOutcome> RealizeAsync(
        PlatformLibraryPopulationDeclaration declaration,
        CancellationToken cancellationToken);
}

/// <summary>
/// Typed inputs shared by the initial Platform-backed product loaders.
/// </summary>
public abstract class PlatformEcosystemPopulationLoadInputs :
    IEcosystemPopulationLoadInputs
{
    private protected PlatformEcosystemPopulationLoadInputs(
        PlatformLibraryPopulationDeclaration declaration,
        EcosystemPopulationOperationPolicyIdentity operationPolicy,
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan,
        EcosystemPopulationWorkIdentity work,
        IEcosystemPlatformPopulationCapability? platformCapability)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(operationPolicy);
        ArgumentNullException.ThrowIfNull(capabilityPlan);
        ArgumentNullException.ThrowIfNull(work);
        if (platformCapability is not null
            && !ReferenceEquals(
                capabilityPlan,
                platformCapability.PlanIdentity))
        {
            throw new ArgumentException(
                "The retained capability-plan identity must be issued by the exact Platform capability.",
                nameof(capabilityPlan));
        }

        PlatformDeclaration = declaration;
        PlatformCapability = platformCapability;
        Snapshot = new(operationPolicy, capabilityPlan, work);
    }

    public PlatformLibraryPopulationDeclaration PlatformDeclaration { get; }
    public IEcosystemPlatformPopulationCapability? PlatformCapability
    {
        get;
    }
    public EcosystemPopulationLoadInputSnapshot Snapshot { get; }
}

/// <summary>Inputs for one .NET Runtime Ecosystem population load.</summary>
public sealed class RuntimeEcosystemPopulationLoadInputs :
    PlatformEcosystemPopulationLoadInputs
{
    public RuntimeEcosystemPopulationLoadInputs(
        EcosystemPopulationOperationPolicyIdentity operationPolicy,
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan,
        EcosystemPopulationWorkIdentity work,
        IEcosystemPlatformPopulationCapability? platformCapability)
        : base(
            ProductEcosystemPacks.RuntimePlatformPopulation,
            operationPolicy,
            capabilityPlan,
            work,
            platformCapability)
    {
    }
}

/// <summary>Inputs for one ASP.NET Core Ecosystem population load.</summary>
public sealed class AspNetCoreEcosystemPopulationLoadInputs :
    PlatformEcosystemPopulationLoadInputs
{
    public AspNetCoreEcosystemPopulationLoadInputs(
        EcosystemPopulationOperationPolicyIdentity operationPolicy,
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan,
        EcosystemPopulationWorkIdentity work,
        IEcosystemPlatformPopulationCapability? platformCapability)
        : base(
            ProductEcosystemPacks.AspNetCorePlatformPopulation,
            operationPolicy,
            capabilityPlan,
            work,
            platformCapability)
    {
    }
}

internal static class ProductEcosystemPopulationLoaders
{
    internal static EcosystemPopulationLoaderBinding Runtime { get; } =
        EcosystemPopulationLoaderBinding.Create<
            RuntimeEcosystemPopulationLoadInputs>(
            EcosystemPopulationLoaderId.Create("ecosystem-loader.runtime"),
            LoadRuntimeAsync);

    internal static EcosystemPopulationLoaderBinding AspNetCore { get; } =
        EcosystemPopulationLoaderBinding.Create<
            AspNetCoreEcosystemPopulationLoadInputs>(
            EcosystemPopulationLoaderId.Create("ecosystem-loader.aspnetcore"),
            LoadAspNetCoreAsync);

    static ValueTask<EcosystemPopulationLoaderReply> LoadRuntimeAsync(
        EcosystemPopulationLoadRequest<
            RuntimeEcosystemPopulationLoadInputs> request) =>
        LoadPlatformPopulationAsync(request);

    static ValueTask<EcosystemPopulationLoaderReply> LoadAspNetCoreAsync(
        EcosystemPopulationLoadRequest<
            AspNetCoreEcosystemPopulationLoadInputs> request) =>
        LoadPlatformPopulationAsync(request);

    static async ValueTask<EcosystemPopulationLoaderReply>
        LoadPlatformPopulationAsync<TInputs>(
            EcosystemPopulationLoadRequest<TInputs> request)
        where TInputs : PlatformEcosystemPopulationLoadInputs
    {
        if (request.Snapshot.Demand
            is not EcosystemPopulationDemand.WholePopulation)
        {
            return request.Unavailable(
                [],
                [
                    new(
                        "ecosystem-loader.platform-demand-unavailable",
                        "The Platform-backed product loader supports whole-population demand only."),
                ]);
        }

        IEcosystemPlatformPopulationCapability? capability =
            request.Inputs.PlatformCapability;
        if (capability is null)
        {
            return request.Unavailable(
                [],
                [
                    new(
                        "ecosystem-loader.platform-capability-unavailable",
                        "The host did not authorize a Platform population capability for this load."),
                ]);
        }

        EcosystemPopulationChildRequestIdentity childRequest =
            request.ChildRequest("platform-population.request");
        EcosystemPopulationChildReceiptIdentity childReceipt =
            request.ChildReceipt(
                childRequest,
                "platform-population.receipt");
        PlatformPopulationArtifactMaterializationOutcome platform =
            await capability.RealizeAsync(
                request.Inputs.PlatformDeclaration,
                request.CancellationToken)
            ?? throw new InvalidOperationException(
                "The Platform population capability returned no outcome.");
        platform = await PlatformHousePopulationArtifactMaterializer
            .RequireRequestFamilyAsync(
                platform,
                request.Inputs.PlatformDeclaration.Family,
                $"{request.Binding.Id.Value}.family-mismatch");

        return platform switch
        {
            PlatformPopulationArtifactMaterializationOutcome.Completed
                completed =>
                Completed(
                    request,
                    childRequest,
                    childReceipt,
                    completed),
            PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal =>
                Terminal(
                    request,
                    childRequest,
                    childReceipt,
                    terminal),
            _ => throw new InvalidOperationException(
                "Unknown Platform population materialization outcome."),
        };
    }

    static EcosystemPopulationLoaderReply Completed<TInputs>(
        EcosystemPopulationLoadRequest<TInputs> request,
        EcosystemPopulationChildRequestIdentity childRequest,
        EcosystemPopulationChildReceiptIdentity childReceipt,
        PlatformPopulationArtifactMaterializationOutcome.Completed platform)
        where TInputs : PlatformEcosystemPopulationLoadInputs
    {
        EcosystemPopulationCompletedChild child =
            request.PlatformCompletedChild(
                childRequest,
                childReceipt,
                platform.Population,
                platform.Artifacts);
        return request.Completed(
            request.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    $"{request.Binding.Id.Value}.platform-complete"),
                EcosystemPopulationCompletionKind.Satisfied),
            [child]);
    }

    static EcosystemPopulationLoaderReply Terminal<TInputs>(
        EcosystemPopulationLoadRequest<TInputs> request,
        EcosystemPopulationChildRequestIdentity childRequest,
        EcosystemPopulationChildReceiptIdentity childReceipt,
        PlatformPopulationArtifactMaterializationOutcome.Terminal platform)
        where TInputs : PlatformEcosystemPopulationLoadInputs
    {
        EcosystemPopulationChildSettlement child =
            request.PlatformTerminalChild(
                childRequest,
                childReceipt,
                platform.TerminalRealization);
        EcosystemPopulationLoadDiagnostic diagnostic = new(
            child.Kind switch
            {
                EcosystemPopulationChildSettlementKind.Unavailable =>
                    "ecosystem-loader.platform-unavailable",
                EcosystemPopulationChildSettlementKind.Incomplete =>
                    "ecosystem-loader.platform-incomplete",
                EcosystemPopulationChildSettlementKind.Rejected =>
                    "ecosystem-loader.platform-rejected",
                EcosystemPopulationChildSettlementKind.Failed =>
                    "ecosystem-loader.platform-failed",
                _ => throw new InvalidOperationException(
                    "Unknown terminal Platform population settlement."),
            },
            "PlatformHouse did not complete the requested Ecosystem population.");
        return child.Kind switch
        {
            EcosystemPopulationChildSettlementKind.Unavailable =>
                request.Unavailable([child], [diagnostic]),
            EcosystemPopulationChildSettlementKind.Incomplete =>
                request.Incomplete([child], [], [diagnostic]),
            EcosystemPopulationChildSettlementKind.Rejected =>
                request.Rejected([child], [diagnostic]),
            EcosystemPopulationChildSettlementKind.Failed =>
                request.Failed([child], [diagnostic]),
            _ => throw new InvalidOperationException(
                "Unknown terminal Platform population settlement."),
        };
    }
}
