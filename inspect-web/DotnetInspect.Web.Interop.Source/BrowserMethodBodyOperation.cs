using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Source;
using DotnetInspect.Web.Interop.Source.Operations;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
[JsExportJsonInput(
    nameof(SourceExports.QueryMethodBodyComparison),
    "requestJson",
    typeof(BrowserMethodBodyComparisonRequest))]
public static partial class SourceExports
{
    [JSExport]
    public static string CancelMethodBodyComparison(string operationId, string reason)
    {
        BrowserTypeSourceCancellation result = BrowserTypeSourceCancellation.From(
            TypeSourceOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserTypeSourceCancellation.ParseReason(reason)));
        return JsonSerializer.Serialize(
            result, BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation);
    }

    [JSExport]
    public static async Task<string> QueryMethodBodyComparisonTargets(
        string operationId, string packageId, string version, string targetFramework,
        string assemblyName, string typeIdentity, string memberName, string selectorKey,
        int metadataToken)
    {
        var result = await RunMethodBodyOperation(operationId, _ =>
            MethodBodyOperations.WithParticipantAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                (group, participant) =>
                {
                    ApiSurface surface = MethodBodyOperations.Select(() =>
                        BrowserMemberResolution.ImplementationSurface(group, participant));
                    CallGraphMemberResolution before = MethodBodyOperations.Select(() =>
                        BrowserMemberResolution.ResolveImplementationMember(
                            surface, typeIdentity, memberName, selectorKey, metadataToken));
                    MetadataMethodAddress address = MethodBodyOperations.RequireAddress(
                        group, participant, before.BodyToken);
                    BrowserMethodBodySelection[] methods =
                        MethodBodyOperations.Inventory(surface);
                    BrowserMethodBodySelection selection = methods.SingleOrDefault(
                        method => method.MetadataToken == before.BodyToken)
                        ?? throw new MethodBodyUnavailableException(
                            "SelectionUnavailable: the selected implementation body has no inventory identity.");
                    return new BrowserMethodBodyTargets(
                        packageId, version, targetFramework, assemblyName,
                        address.ModuleVersionId.ToString("D"), selection, methods);
                }));
        BrowserMethodBodyTargetsResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserMethodBodyTargets, string, string>.Succeeded success =>
                new(1, BrowserMethodBodyResultKind.Succeeded, success.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserMethodBodyTargets, string, string>.Failed failure =>
                new(1, BrowserMethodBodyResultKind.Failed, null, MethodBodyFailureKind(failure.FailureKind),
                    failure.Error, failure.Diagnostic, null),
            BrowserManagedOperationResult<BrowserMethodBodyTargets, string, string>.Canceled canceled =>
                new(1, BrowserMethodBodyResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed method-body outcome."),
        };
        return JsonSerializer.Serialize(wire, BrowserSourceJsonContext.Default.BrowserMethodBodyTargetsResult);
    }

    [JSExport]
    public static async Task<string> QueryMethodBodyComparison(string operationId, string requestJson)
    {
        var result = await RunMethodBodyOperation(
            operationId,
            token => MethodBodyComparisonOperations.RunMethodBodyComparison(
                requestJson,
                token));
        BrowserMethodBodyComparisonResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserMethodBodyComparison, string, string>.Succeeded success =>
                new(1, BrowserMethodBodyResultKind.Succeeded, success.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserMethodBodyComparison, string, string>.Failed failure =>
                new(1, BrowserMethodBodyResultKind.Failed, null, MethodBodyFailureKind(failure.FailureKind),
                    failure.Error, failure.Diagnostic, null),
            BrowserManagedOperationResult<BrowserMethodBodyComparison, string, string>.Canceled canceled =>
                new(1, BrowserMethodBodyResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed method-body outcome."),
        };
        return JsonSerializer.Serialize(wire, BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonResult);
    }

    static Task<BrowserManagedOperationResult<T, string, string>> RunMethodBodyOperation<T>(
        string operationId, Func<CancellationToken, Task<T>> query)
    {
        BrowserManagedOperationId id = BrowserManagedOperationId.From(operationId);
        return TypeSourceOperations.RunAsync<T, string, string, object>(
            id, null,
            async (token, _) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return new BrowserManagedOperationBodyResult<T, string, string>.Succeeded(
                        await query(token).ConfigureAwait(false));
                }
                catch (MethodBodyUnavailableException error)
                {
                    return new BrowserManagedOperationBodyResult<T, string, string>.Failed(
                        error.Message, error.ToString());
                }
            },
            error => new(error.Message, error.ToString()));
    }

    static BrowserTypeSourceFailureKind MethodBodyFailureKind(BrowserManagedOperationFailureKind kind) =>
        kind switch
        {
            BrowserManagedOperationFailureKind.Expected => BrowserTypeSourceFailureKind.Expected,
            BrowserManagedOperationFailureKind.Unexpected => BrowserTypeSourceFailureKind.Unexpected,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

}
