using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Research;
using Analysis = ILInspector.Analysis;

using InspectWeb.Engine;
using InspectWeb.Engine.MetadataFacade;

/// <summary>
/// API and metadata projection over a package or platform coordinate the shared workspace already
/// owns. This facade acquires no artifact of its own.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class MetadataExports
{
    /// <summary>
    /// One type's metadata projection, produced by
    /// <see cref="AssemblyContextTypeProjectionQuery"/> over the participant that owns the type.
    /// The query owns the metadata source and resolves references through the group's binding
    /// policy; nothing here opens a source or reads an image.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryTypeProjection(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId)
    {
        BrowserTypeMetadata type = await TypeProjectionAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeId);
        return JsonSerializer.Serialize(
            type,
            BrowserMetadataJsonContext.Default.BrowserTypeMetadata);
    }

    static async Task<BrowserTypeMetadata> TypeProjectionAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId)
    {
        (BrowserScopeLease<BrowserInspectionScope> lease,
            BrowserWorkspaceParticipant participant) =
            await BrowserMemberResolution.SurfaceParticipantAsync(
                packageId,
                version,
                targetFramework,
                assemblyName);
        await using BrowserScopeLease<BrowserInspectionScope> surfaceLease = lease;
        BrowserInspectionScope scope = lease.Scope;

        ResearchViews.TypeProjectionResult projection = BrowserSurfaceProjection.Require(
            scope.UseSurfaceParticipant(
                participant,
                (group, member) => AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextTypeProjectionRequest(typeId))),
            $"Type projection for '{typeId}'");

        return new BrowserTypeMetadata(
                projection.Identity.FullName,
                projection.Identity.Namespace,
                projection.Identity.Name,
                projection.Identity.Kind,
                [.. projection.Identity.Modifiers],
                projection.Identity.Accessibility,
                projection.Identity.Assembly,
                projection.BaseType,
                [.. projection.Interfaces],
                [.. projection.DerivedTypes],
                [
                    .. projection.TypeParameters.Select(parameter => new BrowserTypeParameter(
                        parameter.Name,
                        parameter.Variance,
                        [.. parameter.Constraints])),
                ],
                [.. projection.Attributes],
                projection.EnumUnderlyingType,
                projection.Composition is { } composition
                    ? new BrowserTypeComposition(
                        composition.Methods,
                        composition.Properties,
                        composition.Fields,
                        composition.Events,
                        composition.Constructors,
                        composition.Operators,
                        composition.ExplicitInterfaceImplementations,
                        composition.ExtensionMethods,
                        composition.Static,
                        composition.Unsafe,
                        composition.Async,
                        composition.Virtual,
                        composition.Abstract,
                        composition.Override,
                        composition.Extension,
                        composition.Obsolete,
                        composition.Total)
                    : null,
                [
                    .. projection.Graph?.Nodes.Select(node => new BrowserTypeGraphNode(
                        node.Id,
                        node.DisplayName,
                        node.Role.ToString().ToLowerInvariant())) ?? [],
                ],
                [
                    .. projection.Graph?.Edges.Select(edge => new BrowserTypeGraphEdge(
                        edge.FromId,
                        edge.ToId,
                        edge.Kind.ToString().ToLowerInvariant())) ?? [],
                ],
                [
                    .. projection.InspectionFailures.Select(
                        failure => $"{failure.Operation}: {failure.Detail}"),
                ]);
    }

    /// <summary>
    /// The exact API member selected by a call-graph target. Package surfaces keep public types
    /// lean by omitting their non-public members; a graph click is the explicit gesture that
    /// projects one such member from the already bounded implementation surface.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryGraphMemberSurface(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        BrowserGraphMemberSurface surface = await GraphMemberSurfaceAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);
        return JsonSerializer.Serialize(
            surface,
            BrowserMetadataJsonContext.Default.BrowserGraphMemberSurface);
    }

    static async Task<BrowserGraphMemberSurface> GraphMemberSurfaceAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        BrowserWorkspaceParticipant surfaceParticipant = resolved.SurfaceParticipant;
        Analysis.CallGraphMemberResolution resolution = resolved.Member;
        var textBudget = new BrowserSurfaceProjection.BrowserSurfaceTextBudget(
            BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
        textBudget.BeginParticipant();
        BrowserTypeSurfaceInfo projectedType =
            BrowserSurfaceProjection.Type(
                resolution.Type,
                surfaceParticipant.Asset.AssemblyName,
                surfaceParticipant.Asset.Id,
                surfaceParticipant.Assembly.Identity.Name,
                textBudget,
                qualifyId: true,
                selectedMembers: [resolution.Member]);
        BrowserTypeSurface type = BrowserMetadataWireProjection.Project(projectedType);
        BrowserMemberSurface member = type.Api.Single();
        BrowserMemberBodySelector selectedBody =
            member.BodySelectors.SingleOrDefault(
                body => body.Token == resolution.BodyToken)
            ?? throw new InvalidOperationException(
                $"The projected member '{member.Name}' does not retain "
                + $"body 0x{resolution.BodyToken:X8}.");
        textBudget.CommitParticipant();
        return new BrowserGraphMemberSurface(type, selectedBody);
    }
}
