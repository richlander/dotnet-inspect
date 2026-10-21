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
    static readonly BrowserManagedOperationBridge LibraryFindingDiffAnyOperations = new();

    // The structural bound on one synchronous Finding Diff Any step. The Worker is
    // single-threaded, so the operation yields to its event loop between steps
    // and foreground operations and cancellation run there.
    internal static TimeSpan LibraryFindingDiffAnyStepBudget { get; set; } = TimeSpan.FromMilliseconds(8);

    [JSExport]
    public static string CancelLibraryFindingDiffAny(string operationId, string reason)
    {
        BrowserLibraryApiDiffCancellation cancellation = ProjectCancellation(
            LibraryFindingDiffAnyOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            new BrowserLibraryFindingDiffAnyCancellation(cancellation.Kind, cancellation.Reason),
            BrowserMetadataJsonContext.Default.BrowserLibraryFindingDiffAnyCancellation);
    }

    /// <summary>
    /// Runs Finding Diff Any over one exact Library pair: every Type's API and Body
    /// states between the target and current implementation assemblies.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryLibraryFindingDiffAny(string operationId, string requestJson)
    {
        BrowserLibraryFindingDiffAnyRequest? request = null;
        Exception? requestError = null;
        try
        {
            request = JsonSerializer.Deserialize(
                    requestJson,
                    BrowserMetadataJsonContext.Default.BrowserLibraryFindingDiffAnyRequest)
                ?? throw new ArgumentException("A Library Finding Diff Any request is required.");
            ValidateLibraryFindingDiffAnyRequest(request);
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            requestError = error;
        }

        BrowserManagedOperationResult<BrowserLibraryFindingDiffAnyResult, string, string> result =
            await LibraryFindingDiffAnyOperations.RunAsync<BrowserLibraryFindingDiffAnyResult, string, string, object>(
                BrowserManagedOperationId.From(operationId),
                eventCallback: null,
                async (token, _) =>
                {
                    try
                    {
                        if (requestError is not null)
                            throw requestError;
                        return new BrowserManagedOperationBodyResult<BrowserLibraryFindingDiffAnyResult, string, string>
                            .Succeeded(await QueryLibraryFindingDiffAnyCore(request!, token));
                    }
                    catch (Exception error) when (
                        error is ArgumentException or JsonException or BrowserLibraryApiDiffRequestException)
                    {
                        return new BrowserManagedOperationBodyResult<BrowserLibraryFindingDiffAnyResult, string, string>
                            .Failed(error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));

        BrowserLibraryFindingDiffAnyResult projected = result switch
        {
            BrowserManagedOperationResult<BrowserLibraryFindingDiffAnyResult, string, string>.Succeeded success =>
                success.Value,
            BrowserManagedOperationResult<BrowserLibraryFindingDiffAnyResult, string, string>.Failed failure =>
                new(BrowserLibraryFindingDiffAnySchema.Version, request, BrowserLibraryFindingDiffAnyResultKind.Failed,
                    Value: null, failure.Error, failure.Diagnostic, Reason: null),
            BrowserManagedOperationResult<BrowserLibraryFindingDiffAnyResult, string, string>.Canceled canceled =>
                new(BrowserLibraryFindingDiffAnySchema.Version, request, BrowserLibraryFindingDiffAnyResultKind.Canceled,
                    Value: null, Error: null, Diagnostic: null,
                    BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed Library Finding Diff Any outcome."),
        };
        return JsonSerializer.Serialize(projected, BrowserMetadataJsonContext.Default.BrowserLibraryFindingDiffAnyResult);
    }

    static async Task<BrowserLibraryFindingDiffAnyResult> QueryLibraryFindingDiffAnyCore(
        BrowserLibraryFindingDiffAnyRequest request,
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

        // Finding Diff Any compares bodies, so both endpoints use their implementation
        // assemblies, not reference assemblies.
        BrowserWorkspaceParticipant target = targetScope.ImplementationParticipant(
            targetScope.SurfaceParticipant(targetCoordinate, targetAsset));
        BrowserWorkspaceParticipant current = currentScope.ImplementationParticipant(
            currentScope.SurfaceParticipant(currentCoordinate, currentAsset));

        var comparison = new FindingDiffAnyComparison(Project(request.Axes));
        AssemblyContextFindingDiffAnyOutcome outcome;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcome = targetScope.UseImplementationParticipant(target, (targetGroup, targetParticipant) =>
                currentScope.UseImplementationParticipant(current, (currentGroup, currentParticipant) =>
                    AssemblyContextFindingDiffAnyQuery.Execute(
                        targetGroup,
                        targetParticipant,
                        currentGroup,
                        currentParticipant,
                        comparison,
                        LibraryFindingDiffAnyStepBudget,
                        cancellationToken)));
            if (outcome is not AssemblyContextFindingDiffAnyOutcome.Pending)
                break;
            await Task.Yield();
        }
        cancellationToken.ThrowIfCancellationRequested();

        return outcome switch
        {
            AssemblyContextFindingDiffAnyOutcome.Compared compared => new(
                BrowserLibraryFindingDiffAnySchema.Version,
                request,
                BrowserLibraryFindingDiffAnyResultKind.Succeeded,
                new BrowserLibraryFindingDiffAnyValue(
                    target.Asset.Id,
                    current.Asset.Id,
                    compared.Result.Types.Length,
                    [
                        .. compared.Result.Types
                            .Where(type => IsReported(type.Api) || IsReported(type.Body))
                            .Select(type => new BrowserFindingDiffAnyType(
                                type.Identifier,
                                type.FullName,
                                Project(type.Api),
                                Project(type.Body))),
                    ]),
                Error: null,
                Diagnostic: null,
                Reason: null),
            AssemblyContextFindingDiffAnyOutcome.Rejected rejected => new(
                BrowserLibraryFindingDiffAnySchema.Version,
                request,
                BrowserLibraryFindingDiffAnyResultKind.Rejected,
                Value: null,
                Error: rejected.Reason,
                Diagnostic: null,
                Reason: null),
            _ => throw new InvalidOperationException("Unknown Finding Diff Any outcome."),
        };
    }

    static bool IsReported(FindingDiffAnyState state)
        => state is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate;

    static BrowserFindingDiffAnyState Project(FindingDiffAnyState state) => state switch
    {
        FindingDiffAnyState.Unchanged => BrowserFindingDiffAnyState.Unchanged,
        FindingDiffAnyState.Changed => BrowserFindingDiffAnyState.Changed,
        FindingDiffAnyState.Indeterminate => BrowserFindingDiffAnyState.Indeterminate,
        FindingDiffAnyState.NotCompared => BrowserFindingDiffAnyState.NotCompared,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    static FindingDiffAnyAxes Project(BrowserFindingDiffAnyAxes axes) => axes switch
    {
        BrowserFindingDiffAnyAxes.ApiAndBody => FindingDiffAnyAxes.ApiAndBody,
        BrowserFindingDiffAnyAxes.Api => FindingDiffAnyAxes.Api,
        _ => throw new ArgumentOutOfRangeException(nameof(axes)),
    };

    static void ValidateLibraryFindingDiffAnyRequest(BrowserLibraryFindingDiffAnyRequest request)
    {
        if (request.SchemaVersion != BrowserLibraryFindingDiffAnySchema.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The Library Finding Diff Any request schema version is unsupported.");
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
        if (!Enum.IsDefined(request.Axes))
            throw new ArgumentOutOfRangeException(nameof(request), "The Library Finding Diff Any axes are unsupported.");
        if (!NuGetVersion.TryParse(request.CurrentVersion, out _)
            || !NuGetVersion.TryParse(request.TargetVersion, out _))
        {
            throw new ArgumentException(
                "Library Finding Diff Any requires exact current and target package versions.");
        }
    }
}
