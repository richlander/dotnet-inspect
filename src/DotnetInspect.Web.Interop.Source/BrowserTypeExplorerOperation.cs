using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
[JsExportJsonInput(
    nameof(SourceExports.QueryTypeExplorer),
    "requestJson",
    typeof(BrowserTypeExplorerRequest))]
[JsExportJsonOutput(
    nameof(SourceExports.QueryTypeExplorer),
    typeof(BrowserTypeExplorerResult))]
public static partial class SourceExports
{
    [JSExport]
    public static string CancelTypeExplorerQuery(
        string operationId,
        string reason)
    {
        BrowserTypeSourceCancellation result =
            BrowserTypeSourceCancellation.From(
                TypeSourceOperations.RequestCancellation(
                    BrowserManagedOperationId.From(operationId),
                    BrowserTypeSourceCancellation.ParseReason(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserSourceJsonContext.Default
                .BrowserTypeSourceCancellation);
    }

    [JSExport]
    public static async Task<string> QueryTypeExplorer(
        string operationId,
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string styleOptionsJson,
        string requestJson)
    {
        BrowserManagedOperationId id =
            BrowserManagedOperationId.From(operationId);
        BrowserManagedOperationResult<
            BrowserTypeExplorerInspection,
            string,
            string> result =
            await TypeSourceOperations.RunAsync<
                BrowserTypeExplorerInspection,
                string,
                string,
                object>(
                id,
                eventCallback: null,
                async (token, _) =>
                {
                    using BrowserSourceOperationLease operation =
                        await BrowserSourceOperationCoordinator.BeginAsync(
                            token,
                            reason => TypeSourceOperations
                                .RequestCancellation(id, reason));
                    try
                    {
                        BrowserTypeExplorerRequest request =
                            JsonSerializer.Deserialize(
                                requestJson,
                                BrowserSourceJsonContext.Default
                                    .BrowserTypeExplorerRequest)
                            ?? throw new ArgumentException(
                                "A Type Explorer projection request is required.",
                                nameof(requestJson));
                        return new BrowserManagedOperationBodyResult<
                            BrowserTypeExplorerInspection,
                            string,
                            string>.Succeeded(
                                await QueryTypeExplorerCore(
                                    packageId,
                                    version,
                                    targetFramework,
                                    assemblyName,
                                    typeIdentity,
                                    styleOptionsJson,
                                    request,
                                    token));
                    }
                    catch (TypeSourceUnavailableException error)
                    {
                        return new BrowserManagedOperationBodyResult<
                            BrowserTypeExplorerInspection,
                            string,
                            string>.Failed(
                                error.Message,
                                error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));

        BrowserTypeExplorerResult wire = result switch
        {
            BrowserManagedOperationResult<
                BrowserTypeExplorerInspection,
                string,
                string>.Succeeded success =>
                new(
                    1,
                    BrowserTypeSourceResultKind.Succeeded,
                    success.Value,
                    null,
                    null,
                    null,
                    null),
            BrowserManagedOperationResult<
                BrowserTypeExplorerInspection,
                string,
                string>.Failed failure =>
                new(
                    1,
                    BrowserTypeSourceResultKind.Failed,
                    null,
                    failure.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected =>
                            BrowserTypeSourceFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected =>
                            BrowserTypeSourceFailureKind.Unexpected,
                        _ => throw new InvalidOperationException(
                            "Unknown Type Explorer failure kind."),
                    },
                    failure.Error,
                    failure.Diagnostic,
                    null),
            BrowserManagedOperationResult<
                BrowserTypeExplorerInspection,
                string,
                string>.Canceled canceled =>
                new(
                    1,
                    BrowserTypeSourceResultKind.Canceled,
                    null,
                    null,
                    null,
                    null,
                    BrowserTypeSourceCancellation.FormatReason(
                        canceled.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown managed Type Explorer outcome."),
        };
        return JsonSerializer.Serialize(
            wire,
            BrowserSourceJsonContext.Default.BrowserTypeExplorerResult);
    }

    private static async Task<BrowserTypeExplorerInspection>
        QueryTypeExplorerCore(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeIdentity,
            string styleOptionsJson,
            BrowserTypeExplorerRequest request,
            CancellationToken cancellationToken)
    {
        (
            BrowserScopeLease<BrowserInspectionScope> scopeLease,
            BrowserWorkspaceParticipant participant,
            ApiType type
        ) = await SourceTypeAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            cancellationToken);
        await using (scopeLease)
        {
            BrowserInspectionScope scope = scopeLease.Scope;
            var sourceRequest = AssemblyTypeSourceRequest.From(
                type,
                BrowserStyleOptions.Resolve(styleOptionsJson));
            InspectionEnvelope<CSharpTypeDocumentOutcome> inspection =
                await scope.UseImplementationParticipant(
                    participant,
                    (group, member) =>
                        TypeDocumentInspection.ExecuteAsync(
                            group,
                            member,
                            sourceRequest,
                            BrowserSourceQueryContext.Create(),
                            cancellationToken: cancellationToken));
            return BrowserTypeExplorerAdapter.From(
                inspection,
                request);
        }
    }
}
