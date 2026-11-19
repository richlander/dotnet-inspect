using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Library;

[SupportedOSPlatform("browser")]
[JsExportJsonInput(
    nameof(LibraryExports.InspectLibrary),
    "requestJson",
    typeof(BrowserLibraryInspectionRequest))]
[JsExportJsonOutput(
    nameof(LibraryExports.InspectLibrary),
    typeof(BrowserLibraryDocumentInspection))]
public static partial class LibraryExports
{
    /// <summary>
    /// Inspects one exact package or Platform Library the Browser already
    /// realizes and returns the requested Library document facts.
    /// </summary>
    [JSExport]
    public static async Task<string> InspectLibrary(string requestJson)
    {
        BrowserLibraryInspectionRequest request =
            JsonSerializer.Deserialize(
                requestJson,
                BrowserLibraryJsonContext.Default.BrowserLibraryInspectionRequest)
            ?? throw new ArgumentException(
                "A Library inspection request is required.",
                nameof(requestJson));
        return JsonSerializer.Serialize(
            await BrowserLibraryDocumentOperation.ExecuteAsync(request)
                .ConfigureAwait(false),
            BrowserLibraryJsonContext.Default.BrowserLibraryDocumentInspection);
    }
}

/// <summary>
/// Lowers a <see cref="BrowserLibraryInspectionRequest"/> to one exact
/// Library and a <see cref="LibraryInspectionPlan"/>. Enablements use the
/// implementation-preferred participant; Name-only requests use the API
/// participant without requesting an implementation.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserLibraryDocumentOperation
{
    private const long MaxAssemblyImageBytes = 64L * 1024 * 1024;

    private static readonly AssemblyContextLibraryMaterializationLimits s_limits =
        new(MaxAssemblyImageBytes, MaxAssemblyImageBytes);

    internal static async Task<BrowserLibraryDocumentInspection> ExecuteAsync(
        BrowserLibraryInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Library);
        ArgumentNullException.ThrowIfNull(request.Plan);
        LibraryInspectionPlan plan = Plan(request.Plan);
        switch (request.Library)
        {
            case { Kind: BrowserLibrarySelectorKind.Package, Package: { } package }:
            {
                await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
                    await BrowserPackageWorkspace.OpenLibraryScopeAsync(
                        package.PackageId,
                        package.Version,
                        package.TargetFramework,
                        PackageLibraryInspectionDemandPlanner.Plan(
                            new PackageLibrarySelector(package.AssemblyId, PackageLibrarySelectionKind.AssetId),
                            request.Plan.Enablements
                                ? PackageLibraryInspectionRequirement.ImplementationFacts
                                : PackageLibraryInspectionRequirement.PublicApi),
                        cancellationToken).ConfigureAwait(false);
                return await InspectPackageAsync(
                        scopeLease.Scope,
                        package.AssemblyId,
                        plan,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            case { Kind: BrowserLibrarySelectorKind.Platform, Platform: { } platform }:
            {
                await using BrowserPlatformScopeResolution resolution =
                    await BrowserPlatformWorkspace.OpenAssemblyAsync(
                        platform.TargetFramework,
                        platform.PlatformVersion,
                        platform.AssemblyFileName,
                        platform.Pack,
                        cancellationToken).ConfigureAwait(false);
                return await InspectPlatformAsync(resolution, plan, cancellationToken)
                    .ConfigureAwait(false);
            }

            default:
                throw new ArgumentException(
                    "The Library selector must name exactly the case its Kind declares.",
                    nameof(request));
        }
    }

    internal static async Task<BrowserLibraryDocumentInspection> InspectPackageAsync(
        BrowserInspectionScope scope,
        string assemblyId,
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        if (!coordinate.Selection.IsSelected)
        {
            return Unavailable(
                $"{coordinate.PackageId} {coordinate.Version} has no selected "
                + $"compile library ({coordinate.Selection.Status}).");
        }

        // Enablements prefer an implementation; Name-only requests stay on
        // the selected API participant.
        BrowserWorkspaceParticipant participant =
            plan.Enablements is not null
                ? scope.LibraryParticipant(coordinate, assemblyId)
                : scope.SurfaceParticipant(
                    coordinate,
                    coordinate.CompileAsset(assemblyId));
        AssemblyContextLibraryRole role =
            scope.ImplementationParticipants.Contains(participant)
                ? AssemblyContextLibraryRole.Implementation
                : AssemblyContextLibraryRole.ApiOnly;
        return await InspectAsync(
                scope.UseMetadataParticipant(
                    participant,
                    (group, member) =>
                        AssemblyContextLibraryAdapter.MaterializeAsync(
                            group,
                            member,
                            role,
                            s_limits,
                            cancellationToken)),
                plan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Type Names are the Library's surface, the reference assembly when it
    // has one; Enablements and facts describe what runs, so they stay on the
    // implementation (docs/design/inspect-web-platform-scope-roles.md).
    internal static async Task<BrowserLibraryDocumentInspection> InspectPlatformAsync(
        BrowserPlatformScopeResolution resolution,
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Types is null
            || !resolution.Scope.HasReferenceSurface(resolution.Participant))
        {
            return await InspectImplementationAsync(resolution, plan, cancellationToken)
                .ConfigureAwait(false);
        }

        var namesPlan = new LibraryInspectionPlan(plan.Types, plan.Bounds);
        BrowserLibraryDocumentInspection names = await InspectAsync(
                resolution.Scope.MaterializeSurfaceAsync(
                    resolution.Participant,
                    s_limits,
                    cancellationToken),
                namesPlan,
                cancellationToken)
            .ConfigureAwait(false);
        if (plan.Enablements is null && plan.Image is null && plan.Description is null)
            return names;

        var implementationPlan = new LibraryInspectionPlan(
            types: null,
            plan.Bounds,
            plan.Enablements,
            plan.Image,
            plan.Description);
        BrowserLibraryDocumentInspection implementation =
            await InspectImplementationAsync(resolution, implementationPlan, cancellationToken)
                .ConfigureAwait(false);
        return implementation with
        {
            // A failed surface run never falls back to runtime names.
            TypeNames = names.TypeNames
                ?? new BrowserLibraryTypeNames(
                    "Unavailable",
                    names.Detail ?? $"The Library surface returned {names.Outcome}.",
                    []),
            Diagnostics = [.. implementation.Diagnostics, .. names.Diagnostics],
        };
    }

    private static Task<BrowserLibraryDocumentInspection> InspectImplementationAsync(
        BrowserPlatformScopeResolution resolution,
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken) =>
        InspectAsync(
            resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, member) =>
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        member,
                        AssemblyContextLibraryRole.Implementation,
                        s_limits,
                        cancellationToken)),
            plan,
            cancellationToken);

    internal static LibraryInspectionPlan Plan(BrowserLibraryInspectionPlan plan) =>
        new(
            types: plan.TypeNames ? NamePresentation().Types : null,
            BrowserApiSurfacePolicy.LibraryBounds,
            plan.Enablements ? new LibraryEnablementsRequest() : null,
            plan.Facts ? new LibraryImageFactsRequest() : null,
            plan.Facts ? new LibraryDescriptionFactsRequest() : null);

    internal static (
        LibraryTypePopulationRequest Types,
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> Hierarchy)
        NamePresentation() => BrowserLibraryTypeHierarchy.NamePlan();

    private static async Task<BrowserLibraryDocumentInspection> InspectAsync(
        ValueTask<AssemblyContextLibraryAdapterResult> materialization,
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        AssemblyContextLibraryInspectionRun<InspectionEnvelope<LibraryInspectionOutcome>> run =
            await AssemblyContextLibraryInspection.ExecuteAsync(
                    materialization,
                    (reference, owner) =>
                        AssemblyContextLibraryInspection.ExecuteOperation(
                            reference,
                            owner,
                            lease =>
                                LibraryInspectionOperation.Execute(
                                    new(reference, plan),
                                    lease,
                                    cancellationToken)))
                .ConfigureAwait(false);
        return BrowserLibraryDocumentProjection.Project(run);
    }

    private static BrowserLibraryDocumentInspection Unavailable(string detail) =>
        new(BrowserLibraryDocumentOutcome.Unavailable, detail, null, null, []);
}
