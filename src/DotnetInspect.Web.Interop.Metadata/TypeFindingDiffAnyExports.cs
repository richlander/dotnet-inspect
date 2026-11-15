using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

public static partial class MetadataExports
{
    static readonly BrowserManagedOperationBridge TypeFindingDiffAnyOperations = new();
    internal static TimeSpan TypeFindingDiffAnyStepBudget { get; set; } = TimeSpan.FromMilliseconds(8);

    [JSExport]
    public static string CancelTypeFindingDiffAny(string operationId, string reason)
        => JsonSerializer.Serialize(ProjectCancellation(TypeFindingDiffAnyOperations.RequestCancellation(
            BrowserManagedOperationId.From(operationId), BrowserManagedOperationCancelReasons.Parse(reason))),
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffCancellation);

    [JSExport]
    public static async Task<string> QueryTypeFindingDiffAny(string operationId, string requestJson)
    {
        BrowserTypeFindingDiffAnyRequest? request = null;
        Exception? requestError = null;
        try
        {
            request = JsonSerializer.Deserialize(requestJson, BrowserMetadataJsonContext.Default.BrowserTypeFindingDiffAnyRequest)
                ?? throw new ArgumentException("A Type Finding Diff Any request is required.");
            if (request.SchemaVersion != BrowserTypeFindingDiffAnySchema.Version)
                throw new ArgumentException("The Type Finding Diff Any schema version is unsupported.");
            ArgumentNullException.ThrowIfNull(request.Comparison);
            ValidateLibraryFindingDiffAnyRequest(request.Comparison);
            if (request.Comparison.Axes != BrowserFindingDiffAnyAxes.ApiAndBody)
                throw new ArgumentException("Type Member cues require both axes over implementation images.");
            ArgumentException.ThrowIfNullOrWhiteSpace(request.TypeIdentifier);
            RequireBoundedRequestField(request.TypeIdentifier, nameof(request.TypeIdentifier));
            if (AssemblyContextTypeResolutionQuery.ParseTypeIdentity(request.TypeIdentifier)
                is not AssemblyContextTypeResolutionTypeIdentityResult.Valid)
                throw new ArgumentException("An exact escaped Type definition identity is required.");
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            requestError = error;
        }
        var result = await TypeFindingDiffAnyOperations.RunAsync<BrowserTypeFindingDiffAnyResult, string, string, object>(
            BrowserManagedOperationId.From(operationId), eventCallback: null,
            async (token, _) =>
            {
                if (requestError is not null) throw requestError;
                BrowserTypeFindingDiffAnyResult value = await WithFindingDiffAnyScopes(
                    request!.Comparison,
                    (_, target, current, _, cancellationToken) => CompareTypeFindingDiffAny(request, target, current, cancellationToken),
                    (comparison, endpoint, version, missing) =>
                    {
                        BrowserLibraryFindingDiffAnyResult unavailable = MissingCounterpart(comparison, endpoint, version, missing);
                        return new BrowserTypeFindingDiffAnyResult(BrowserTypeFindingDiffAnySchema.Version, request,
                            unavailable.Kind, null, unavailable.Error, unavailable.Diagnostic, unavailable.Reason, unavailable.MissingCounterpart);
                    }, token);
                return new BrowserManagedOperationBodyResult<BrowserTypeFindingDiffAnyResult, string, string>.Succeeded(value);
            }, error => new(error.Message, error.ToString()));
        BrowserTypeFindingDiffAnyResult projected = result switch
        {
            BrowserManagedOperationResult<BrowserTypeFindingDiffAnyResult, string, string>.Succeeded succeeded => succeeded.Value,
            BrowserManagedOperationResult<BrowserTypeFindingDiffAnyResult, string, string>.Failed failed =>
                new(BrowserTypeFindingDiffAnySchema.Version, request, BrowserLibraryFindingDiffAnyResultKind.Failed,
                    null, failed.Error, failed.Diagnostic, null),
            BrowserManagedOperationResult<BrowserTypeFindingDiffAnyResult, string, string>.Canceled canceled =>
                new(BrowserTypeFindingDiffAnySchema.Version, request, BrowserLibraryFindingDiffAnyResultKind.Canceled,
                    null, null, null, BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed Type Finding Diff Any outcome.")
        };
        return JsonSerializer.Serialize(projected, BrowserMetadataJsonContext.Default.BrowserTypeFindingDiffAnyResult);
    }

    static async Task<BrowserTypeFindingDiffAnyResult> CompareTypeFindingDiffAny(
        BrowserTypeFindingDiffAnyRequest request, BrowserInspectionScope targetScope,
        BrowserInspectionScope currentScope, CancellationToken cancellationToken)
    {
        BrowserPackageCoordinate currentCoordinate = currentScope.Coordinates[0], targetCoordinate = targetScope.Coordinates[0];
        var currentAsset = RequireExactCompileAsset(currentCoordinate, request.Comparison.CompileAssetId, "current");
        var targetAsset = targetCoordinate.Selection.FindComparisonAsset(currentAsset)
            ?? throw new BrowserLibraryApiDiffRequestException("The target has no unique selected compile counterpart.");
        BrowserWorkspaceParticipant target = targetScope.ImplementationParticipant(targetScope.SurfaceParticipant(targetCoordinate, targetAsset));
        BrowserWorkspaceParticipant current = currentScope.ImplementationParticipant(currentScope.SurfaceParticipant(currentCoordinate, currentAsset));
        Task<AssemblyContextFindingDiffAnyTypeOutcome> pending = targetScope.UseImplementationParticipant(target,
            (targetGroup, targetParticipant) => currentScope.UseImplementationParticipant(current,
                (currentGroup, currentParticipant) => AssemblyContextFindingDiffAnyTypeQuery.ExecuteAsync(
                    targetGroup, targetParticipant, currentGroup, currentParticipant, request.TypeIdentifier,
                    TypeFindingDiffAnyStepBudget, cancellationToken)));
        AssemblyContextFindingDiffAnyTypeOutcome outcome = await pending;
        return outcome switch
        {
            AssemblyContextFindingDiffAnyTypeOutcome.Compared compared => new(
                BrowserTypeFindingDiffAnySchema.Version, request, BrowserLibraryFindingDiffAnyResultKind.Succeeded,
                new(request.TypeIdentifier, target.Asset.Id, current.Asset.Id,
                    Project(compared.Result.Api), Project(compared.Result.Body),
                    Project(compared.Result.ResidualApi), Project(compared.Result.ResidualBody),
                    [.. compared.Result.Members.Select(member => new BrowserFindingDiffAnyMember(
                        member.BeforeFingerprint, member.AfterFingerprint, Project(member.Api), Project(member.Body)))]),
                null, null, null),
            AssemblyContextFindingDiffAnyTypeOutcome.Rejected rejected => new(
                BrowserTypeFindingDiffAnySchema.Version, request, BrowserLibraryFindingDiffAnyResultKind.Rejected,
                null, rejected.Reason, null, null),
            _ => throw new InvalidOperationException("Unknown Type Finding Diff Any outcome.")
        };
    }
}
