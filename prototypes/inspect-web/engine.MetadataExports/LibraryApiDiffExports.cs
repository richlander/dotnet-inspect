using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using InspectWeb.Engine;
using InspectWeb.Engine.MetadataFacade;

/// <summary>
/// One page-keyed Library API Diff query and its matching keyed cancellation (#6423). The
/// comparison target is <c>Before</c>; the currently inspected Library is <c>After</c>. Both
/// package coordinates are resolved and their requested Library is resolved independently through
/// <see cref="BrowserPackageWorkspace"/>/<see cref="BrowserInspectionScope.LibraryParticipant"/>,
/// both scope leases are held across the comparison query and its presentation projection, and
/// <see cref="AssemblyContextApiComparisonQuery"/> and
/// <see cref="LibraryApiDiffPresentationAdapter"/> each run exactly once.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class MetadataExports
{
    static readonly BrowserManagedOperationBridge LibraryApiDiffOperations = new();

    [JSExport]
    public static string CancelLibraryApiDiff(string operationId, string reason)
    {
        BrowserLibraryApiDiffCancellation result = BrowserLibraryApiDiffCancellation.From(
            LibraryApiDiffOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserLibraryApiDiffCancellation.ParseReason(reason)));
        return JsonSerializer.Serialize(
            result, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffCancellation);
    }

    [JSExport]
    public static async Task<string> QueryLibraryApiDiff(string operationId, string requestJson)
    {
        BrowserManagedOperationId id = BrowserManagedOperationId.From(operationId);
        BrowserManagedOperationResult<BrowserLibraryApiDiff, string, string> result =
            await LibraryApiDiffOperations.RunAsync<BrowserLibraryApiDiff, string, string, object>(
                id,
                null,
                async (token, _) =>
                {
                    try
                    {
                        BrowserLibraryApiDiffRequest request = JsonSerializer.Deserialize(
                            requestJson,
                            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffRequest)
                            ?? throw new ArgumentException(
                                "A Library API diff request is required.");
                        ValidateRequest(request);
                        return new BrowserManagedOperationBodyResult<
                            BrowserLibraryApiDiff, string, string>.Succeeded(
                                await QueryLibraryApiDiffCoreAsync(request, token));
                    }
                    catch (Exception error) when (
                        error is ArgumentException
                        or InvalidOperationException
                        or JsonException
                        or FormatException)
                    {
                        return new BrowserManagedOperationBodyResult<
                            BrowserLibraryApiDiff, string, string>.Failed(
                                error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));
        BrowserLibraryApiDiffResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserLibraryApiDiff, string, string>.Succeeded success =>
                new(1, BrowserLibraryApiDiffResultKind.Succeeded, success.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserLibraryApiDiff, string, string>.Failed failure =>
                new(
                    1,
                    BrowserLibraryApiDiffResultKind.Failed,
                    null,
                    failure.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected =>
                            BrowserLibraryApiDiffFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected =>
                            BrowserLibraryApiDiffFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failure.Error,
                    failure.Diagnostic,
                    null),
            BrowserManagedOperationResult<BrowserLibraryApiDiff, string, string>.Canceled canceled =>
                new(
                    1,
                    BrowserLibraryApiDiffResultKind.Canceled,
                    null,
                    null,
                    null,
                    null,
                    BrowserLibraryApiDiffCancellation.FormatReason(canceled.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown managed Library API diff outcome."),
        };
        return JsonSerializer.Serialize(
            wire, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
    }

    static async Task<BrowserLibraryApiDiff> QueryLibraryApiDiffCoreAsync(
        BrowserLibraryApiDiffRequest request, CancellationToken cancellationToken)
    {
        // Resolve the exact selected After asset first, then use its product-owned assembly
        // identity to find the logical Before counterpart. Package versions may move the same
        // Library between ref/ and lib/ assets, so the current asset ID is not a cross-version
        // identity. Each participant still comes from its own independently leased scope.
        await using BrowserScopeLease<BrowserInspectionScope> afterLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.Version, request.Framework, cancellationToken);
        BrowserInspectionScope afterScope = afterLease.Scope;
        BrowserPackageCoordinate afterCoordinate = afterScope.Coordinates[0];
        BrowserWorkspaceParticipant afterParticipant =
            afterScope.LibraryParticipant(afterCoordinate, request.Assembly);

        await using BrowserScopeLease<BrowserInspectionScope> beforeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.ComparisonVersion, request.Framework, cancellationToken);
        BrowserInspectionScope beforeScope = beforeLease.Scope;
        BrowserPackageCoordinate beforeCoordinate = beforeScope.Coordinates[0];
        BrowserWorkspaceParticipant beforeParticipant =
            beforeScope.LibraryParticipant(
                beforeCoordinate, afterParticipant.Assembly.Identity.Name);

        // Both leases stay held (the two `await using` scopes above) across the comparison query
        // and the presentation projection below.
        AssemblyContextApiComparisonResult comparison = beforeScope.UseMetadataParticipant(
            beforeParticipant,
            (beforeGroup, beforeSelected) => afterScope.UseMetadataParticipant(
                afterParticipant,
                (afterGroup, afterSelected) => AssemblyContextApiComparisonQuery.Execute(
                    beforeGroup,
                    beforeSelected,
                    afterGroup,
                    afterSelected,
                    ApiSurfaceScope.Public,
                    BrowserApiSurfacePolicy.Limits)));
        cancellationToken.ThrowIfCancellationRequested();

        LibraryApiDiffPresentationResult presentation =
            LibraryApiDiffPresentationAdapter.Create(comparison);
        return BrowserLibraryApiDiffWireProjection.Project(request, presentation);
    }

    static void ValidateRequest(BrowserLibraryApiDiffRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ComparisonVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Assembly);
    }
}
