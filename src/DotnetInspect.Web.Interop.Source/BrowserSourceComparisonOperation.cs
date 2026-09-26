using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Source;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGet.Versioning;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
[JsExportJsonInput(
    nameof(SourceExports.QueryMemberSourceComparison),
    "requestJson",
    typeof(BrowserSourceComparisonRequest))]
[JsExportJsonOutput(
    nameof(SourceExports.QueryMemberSourceComparison),
    typeof(BrowserSourceComparisonResult),
    deferParsing: true)]
public static partial class SourceExports
{
    [JSExport]
    public static string CancelMemberSourceComparison(string operationId, string reason)
    {
        BrowserTypeSourceCancellation result = BrowserTypeSourceCancellation.From(
            TypeSourceOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserTypeSourceCancellation.ParseReason(reason)));
        return JsonSerializer.Serialize(
            result, BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation);
    }

    [JSExport]
    public static async Task<string> QueryMemberSourceComparison(
        string operationId, string requestJson)
    {
        BrowserManagedOperationId id = BrowserManagedOperationId.From(operationId);
        BrowserManagedOperationResult<
            BrowserSourceComparisonProjectionResult,
            string,
            string> result =
            await TypeSourceOperations.RunAsync<
                BrowserSourceComparisonProjectionResult,
                string,
                string,
                object>(
                id, null,
                async (token, _) =>
                {
                    using BrowserSourceOperationLease operation =
                        await BrowserSourceOperationCoordinator.BeginAsync(
                            token, reason => TypeSourceOperations.RequestCancellation(id, reason));
                    try
                    {
                        BrowserSourceDiffProjection.AdmitRequest(requestJson);
                        BrowserSourceComparisonRequest request = JsonSerializer.Deserialize(
                            requestJson, BrowserSourceJsonContext.Default.BrowserSourceComparisonRequest)
                            ?? throw new ArgumentException("A Source comparison request is required.");
                        ValidateSourceComparisonRequest(request);
                        BrowserSourceComparison comparison =
                            await QueryMemberSourceComparisonCore(
                                request,
                                operation.CancellationToken);
                        return new BrowserManagedOperationBodyResult<
                            BrowserSourceComparisonProjectionResult,
                            string,
                            string>.Succeeded(
                                new BrowserSourceComparisonProjectionResult.Complete(
                                    comparison));
                    }
                    catch (BrowserSourceDiffCapacityException error)
                    {
                        return new BrowserManagedOperationBodyResult<
                            BrowserSourceComparisonProjectionResult,
                            string,
                            string>.Succeeded(
                                new BrowserSourceComparisonProjectionResult.TooComplex(
                                    error.Capacity));
                    }
                    catch (Exception error) when (error is ArgumentException or JsonException)
                    {
                        return new BrowserManagedOperationBodyResult<
                            BrowserSourceComparisonProjectionResult,
                            string,
                            string>.Failed(
                            error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));
        BrowserSourceComparisonResult wire = result switch
        {
            BrowserManagedOperationResult<
                BrowserSourceComparisonProjectionResult,
                string,
                string>.Succeeded
            {
                Value: BrowserSourceComparisonProjectionResult.Complete complete,
            } =>
                new(
                    1,
                    BrowserSourceComparisonResultKind.Succeeded,
                    complete.Comparison,
                    null,
                    null,
                    null,
                    null,
                    null),
            BrowserManagedOperationResult<
                BrowserSourceComparisonProjectionResult,
                string,
                string>.Succeeded
            {
                Value: BrowserSourceComparisonProjectionResult.TooComplex tooComplex,
            } =>
                new(
                    1,
                    BrowserSourceComparisonResultKind.TooComplex,
                    null,
                    null,
                    null,
                    null,
                    null,
                    tooComplex.Capacity),
            BrowserManagedOperationResult<
                BrowserSourceComparisonProjectionResult,
                string,
                string>.Failed failure =>
                new(1, BrowserSourceComparisonResultKind.Failed, null,
                    failure.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected => BrowserTypeSourceFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected => BrowserTypeSourceFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failure.Error, failure.Diagnostic, null, null),
            BrowserManagedOperationResult<
                BrowserSourceComparisonProjectionResult,
                string,
                string>.Canceled canceled =>
                new(
                    1,
                    BrowserSourceComparisonResultKind.Canceled,
                    null,
                    null,
                    null,
                    null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason),
                    null),
            _ => throw new InvalidOperationException("Unknown managed Source comparison outcome."),
        };
        return BrowserSourceDiffJson.Serialize(wire);
    }

    static async Task<BrowserSourceComparison> QueryMemberSourceComparisonCore(
        BrowserSourceComparisonRequest request, CancellationToken cancellationToken)
    {
        await using BrowserScopeLease<BrowserInspectionScope> beforeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.BeforeVersion, request.Framework, cancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> afterLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.AfterVersion, request.Framework, cancellationToken);
        BrowserInspectionScope beforeScope = beforeLease.Scope;
        BrowserInspectionScope afterScope = afterLease.Scope;
        SourceComparisonEndpointInput before = ResolveEndpoint(
            beforeScope, request.Assembly, request.Before);
        SourceComparisonEndpointInput after = ResolveEndpoint(
            afterScope, request.Assembly, request.After);
        var pairRequest = new AssemblyMemberSourcePairRequest(
            before.Request, after.Request);

        InspectionEnvelope<AssemblyMemberSourcePairResult> inspection =
            await beforeScope.UseImplementationParticipant(
                before.Participant,
                (beforeGroup, beforeParticipant) =>
                    afterScope.UseImplementationParticipant(
                        after.Participant,
                        (afterGroup, afterParticipant) =>
                            MemberSourcePairInspection.ExecuteAsync(
                                beforeGroup,
                                beforeParticipant,
                                afterGroup,
                                afterParticipant,
                                pairRequest,
                                BrowserSourceQueryContext.Create(),
                                cancellationToken,
                                BrowserSourceDiffProjection.AdmitEndpoints)));
        cancellationToken.ThrowIfCancellationRequested();
        return BrowserSourceComparisonProjection.Project(
            request,
            inspection.Content,
            before.Participant,
            after.Participant);
    }

    static SourceComparisonEndpointInput ResolveEndpoint(
        BrowserInspectionScope scope,
        string assembly,
        BrowserSourceComparisonEndpointRequest? request)
    {
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserWorkspaceParticipant surface = scope.SurfaceParticipant(
            coordinate, coordinate.CompileAsset(assembly));
        BrowserWorkspaceParticipant implementation =
            scope.ImplementationParticipant(surface);
        if (request is null)
            return new(implementation, null);

        MetadataTypeDefinitionName type =
            MetadataTypeDefinitionName.ParseSerialized(request.TypeIdentity)
                is MetadataTypeDefinitionNameResult.Valid valid
                    ? valid.Name
                    : throw new ArgumentException(
                        "Source comparison requires an exact metadata Type identity.");
        var anchor = new MemberAnchor(
            request.StableSelector,
            request.CanonicalSignature,
            request.Fingerprint,
            request.TypeFullName,
            request.MemberName);
        return new(
            implementation,
            new(type, anchor));
    }

    static void ValidateSourceComparisonRequest(BrowserSourceComparisonRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Assembly);
        if (!NuGetVersion.TryParse(request.BeforeVersion, out _)
            || !NuGetVersion.TryParse(request.AfterVersion, out _))
            throw new ArgumentException("Source comparison requires two exact package versions.");
        if (request.Before is null && request.After is null)
            throw new ArgumentException(
                "Source comparison requires at least one endpoint member.");
        ValidateEndpoint(request.Before);
        ValidateEndpoint(request.After);
    }

    static void ValidateEndpoint(
        BrowserSourceComparisonEndpointRequest? request)
    {
        if (request is null)
            return;
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TypeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StableSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CanonicalSignature);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TypeFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemberName);
    }

    sealed record SourceComparisonEndpointInput(
        BrowserWorkspaceParticipant Participant,
        AssemblyMemberSourcePairEndpointRequest? Request);
}
