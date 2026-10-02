using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
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
/// Library and a <see cref="LibraryInspectionPlan"/>. The package route uses
/// the implementation-preferred participant, so a package with a <c>ref/</c>
/// folder is judged on its implementation without another read.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserLibraryDocumentOperation
{
    private const long MaxAssemblyImageBytes = 64L * 1024 * 1024;

    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: BrowserApiSurfacePolicy.MaxTypes,
            maxMembers: BrowserApiSurfacePolicy.MaxMembers,
            maxInspectionFailures: BrowserApiSurfacePolicy.MaxInspectionFailures,
            maxTypeForwarders: BrowserApiSurfacePolicy.MaxTypeForwarders,
            maxMetadataRows: BrowserApiSurfacePolicy.MaxMetadataRows,
            maxRetainedTextCharacters: BrowserApiSurfacePolicy.MaxRetainedTextCharacters);

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
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        package.PackageId,
                        package.Version,
                        package.TargetFramework).ConfigureAwait(false);
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

        // Implementation-preferred: a reference-only package falls back to
        // its compile assembly, which the enablements owner then reports as a
        // reference assembly.
        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyId);
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

    internal static Task<BrowserLibraryDocumentInspection> InspectPlatformAsync(
        BrowserPlatformScopeResolution resolution,
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return InspectAsync(
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
    }

    // Facts-only: the Browser lowers no Type population yet, so the plan
    // requests none and the operation runs no declaration inventory.
    internal static LibraryInspectionPlan Plan(BrowserLibraryInspectionPlan plan) =>
        new(
            types: null,
            s_bounds,
            plan.Enablements ? new LibraryEnablementsRequest() : null);

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
