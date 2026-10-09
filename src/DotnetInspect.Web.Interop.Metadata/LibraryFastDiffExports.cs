using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspect.Web;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspect.Web.Interop.Metadata;

public static partial class MetadataExports
{
    static readonly BrowserManagedOperationBridge LibraryFastDiffOperations = new();

    [JSExport]
    public static string CancelLibraryFastDiff(string operationId, string reason)
    {
        BrowserLibraryApiDiffCancellation cancellation = ProjectCancellation(
            LibraryFastDiffOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            new BrowserLibraryFastDiffCancellation(cancellation.Kind, cancellation.Reason),
            BrowserMetadataJsonContext.Default.BrowserLibraryFastDiffCancellation);
    }

    /// <summary>
    /// Runs Fast Diff over one exact Library pair: every Type's API and Body
    /// states between the target and current implementation assemblies.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryLibraryFastDiff(string operationId, string requestJson)
    {
        BrowserLibraryFastDiffRequest? request = null;
        Exception? requestError = null;
        try
        {
            request = JsonSerializer.Deserialize(
                    requestJson,
                    BrowserMetadataJsonContext.Default.BrowserLibraryFastDiffRequest)
                ?? throw new ArgumentException("A Library Fast Diff request is required.");
            ValidateLibraryFastDiffRequest(request);
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            requestError = error;
        }

        BrowserManagedOperationResult<BrowserLibraryFastDiffResult, string, string> result =
            await LibraryFastDiffOperations.RunAsync<BrowserLibraryFastDiffResult, string, string, object>(
                BrowserManagedOperationId.From(operationId),
                eventCallback: null,
                async (token, _) =>
                {
                    try
                    {
                        if (requestError is not null)
                            throw requestError;
                        return new BrowserManagedOperationBodyResult<BrowserLibraryFastDiffResult, string, string>
                            .Succeeded(await QueryLibraryFastDiffCore(request!, token));
                    }
                    catch (Exception error) when (
                        error is ArgumentException or JsonException or BrowserLibraryApiDiffRequestException)
                    {
                        return new BrowserManagedOperationBodyResult<BrowserLibraryFastDiffResult, string, string>
                            .Failed(error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));

        BrowserLibraryFastDiffResult projected = result switch
        {
            BrowserManagedOperationResult<BrowserLibraryFastDiffResult, string, string>.Succeeded success =>
                success.Value,
            BrowserManagedOperationResult<BrowserLibraryFastDiffResult, string, string>.Failed failure =>
                new(BrowserLibraryFastDiffSchema.Version, request, BrowserLibraryFastDiffResultKind.Failed,
                    Value: null, failure.Error, failure.Diagnostic, Reason: null),
            BrowserManagedOperationResult<BrowserLibraryFastDiffResult, string, string>.Canceled canceled =>
                new(BrowserLibraryFastDiffSchema.Version, request, BrowserLibraryFastDiffResultKind.Canceled,
                    Value: null, Error: null, Diagnostic: null,
                    BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed Library Fast Diff outcome."),
        };
        return JsonSerializer.Serialize(projected, BrowserMetadataJsonContext.Default.BrowserLibraryFastDiffResult);
    }

    static async Task<BrowserLibraryFastDiffResult> QueryLibraryFastDiffCore(
        BrowserLibraryFastDiffRequest request,
        CancellationToken cancellationToken)
    {
        await using BrowserScopeLease<BrowserInspectionScope> targetLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.TargetVersion, request.TargetFramework, cancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> currentLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.CurrentVersion, request.TargetFramework, cancellationToken);
        BrowserInspectionScope targetScope = targetLease.Scope;
        BrowserInspectionScope currentScope = currentLease.Scope;
        BrowserPackageCoordinate targetCoordinate = targetScope.Coordinates[0];
        BrowserPackageCoordinate currentCoordinate = currentScope.Coordinates[0];
        PackageCompileAsset currentAsset =
            RequireExactCompileAsset(currentCoordinate, request.CompileAssetId, "current");
        PackageCompileAsset targetAsset = targetCoordinate.Selection.FindComparisonAsset(currentAsset)
            ?? throw new BrowserLibraryApiDiffRequestException(
                $"The target package endpoint {targetCoordinate.PackageId} {targetCoordinate.Version} "
                    + $"has no unique selected compile counterpart for '{currentAsset.Id}'.");

        // Fast Diff compares bodies, so both endpoints use their implementation
        // assemblies, not reference assemblies.
        BrowserWorkspaceParticipant target = targetScope.ImplementationParticipant(
            targetScope.SurfaceParticipant(targetCoordinate, targetAsset));
        BrowserWorkspaceParticipant current = currentScope.ImplementationParticipant(
            currentScope.SurfaceParticipant(currentCoordinate, currentAsset));

        cancellationToken.ThrowIfCancellationRequested();
        AssemblyContextFastDiffOutcome outcome =
            targetScope.UseImplementationParticipant(target, (targetGroup, targetParticipant) =>
                currentScope.UseImplementationParticipant(current, (currentGroup, currentParticipant) =>
                    AssemblyContextFastDiffQuery.Execute(
                        targetGroup, targetParticipant, currentGroup, currentParticipant, cancellationToken)));
        cancellationToken.ThrowIfCancellationRequested();

        return outcome switch
        {
            AssemblyContextFastDiffOutcome.Compared compared => new(
                BrowserLibraryFastDiffSchema.Version,
                request,
                BrowserLibraryFastDiffResultKind.Succeeded,
                new BrowserLibraryFastDiffValue(
                    target.Asset.Id,
                    current.Asset.Id,
                    compared.Result.Types.Length,
                    [
                        .. compared.Result.Types
                            .Where(type => type.Api != FastDiffState.Unchanged
                                || type.Body != FastDiffState.Unchanged)
                            .Select(type => new BrowserFastDiffType(
                                type.Identifier,
                                type.FullName,
                                Project(type.Api),
                                Project(type.Body))),
                    ]),
                Error: null,
                Diagnostic: null,
                Reason: null),
            AssemblyContextFastDiffOutcome.Rejected rejected => new(
                BrowserLibraryFastDiffSchema.Version,
                request,
                BrowserLibraryFastDiffResultKind.Rejected,
                Value: null,
                Error: rejected.Reason,
                Diagnostic: null,
                Reason: null),
            _ => throw new InvalidOperationException("Unknown Fast Diff outcome."),
        };
    }

    static BrowserFastDiffState Project(FastDiffState state) => state switch
    {
        FastDiffState.Unchanged => BrowserFastDiffState.Unchanged,
        FastDiffState.Changed => BrowserFastDiffState.Changed,
        FastDiffState.Indeterminate => BrowserFastDiffState.Indeterminate,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    static void ValidateLibraryFastDiffRequest(BrowserLibraryFastDiffRequest request)
    {
        if (request.SchemaVersion != BrowserLibraryFastDiffSchema.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The Library Fast Diff request schema version is unsupported.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CompileAssetId);
        RequireBoundedRequestField(request.PackageId, nameof(request.PackageId));
        RequireBoundedRequestField(request.CurrentVersion, nameof(request.CurrentVersion));
        RequireBoundedRequestField(request.TargetVersion, nameof(request.TargetVersion));
        RequireBoundedRequestField(request.TargetFramework, nameof(request.TargetFramework));
        RequireBoundedRequestField(request.CompileAssetId, nameof(request.CompileAssetId));
        _ = BrowserFrameworkText.Require(request.TargetFramework);
        if (!NuGetVersion.TryParse(request.CurrentVersion, out _)
            || !NuGetVersion.TryParse(request.TargetVersion, out _))
        {
            throw new ArgumentException(
                "Library Fast Diff requires exact current and target package versions.");
        }
    }
}
