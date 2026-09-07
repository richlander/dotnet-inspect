using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using InspectWeb.Engine;
using InspectWeb.Engine.SourceFacade;
using NuGet.Versioning;

[SupportedOSPlatform("browser")]
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
        BrowserManagedOperationResult<BrowserSourceComparison, string, string> result =
            await TypeSourceOperations.RunAsync<BrowserSourceComparison, string, string, object>(
                id, null,
                async (token, _) =>
                {
                    using BrowserSourceOperationLease operation =
                        await BrowserSourceOperationCoordinator.BeginAsync(
                            token, reason => TypeSourceOperations.RequestCancellation(id, reason));
                    try
                    {
                        BrowserSourceComparisonRequest request = JsonSerializer.Deserialize(
                            requestJson, BrowserSourceJsonContext.Default.BrowserSourceComparisonRequest)
                            ?? throw new ArgumentException("A Source comparison request is required.");
                        ValidateSourceComparisonRequest(request);
                        return new BrowserManagedOperationBodyResult<BrowserSourceComparison, string, string>.Succeeded(
                            await QueryMemberSourceComparisonCore(request, operation.CancellationToken));
                    }
                    catch (SourceComparisonUnavailableException error)
                    {
                        return new BrowserManagedOperationBodyResult<BrowserSourceComparison, string, string>.Failed(
                            error.Message, error.ToString());
                    }
                    catch (Exception error) when (error is ArgumentException or JsonException)
                    {
                        return new BrowserManagedOperationBodyResult<BrowserSourceComparison, string, string>.Failed(
                            error.Message, error.ToString());
                    }
                },
                error => new(error.Message, error.ToString()));
        BrowserSourceComparisonResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserSourceComparison, string, string>.Succeeded success =>
                new(1, BrowserSourceComparisonResultKind.Succeeded, success.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserSourceComparison, string, string>.Failed failure =>
                new(1, BrowserSourceComparisonResultKind.Failed, null,
                    failure.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected => BrowserTypeSourceFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected => BrowserTypeSourceFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failure.Error, failure.Diagnostic, null),
            BrowserManagedOperationResult<BrowserSourceComparison, string, string>.Canceled canceled =>
                new(1, BrowserSourceComparisonResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed Source comparison outcome."),
        };
        return JsonSerializer.Serialize(
            wire, BrowserSourceJsonContext.Default.BrowserSourceComparisonResult);
    }

    static async Task<BrowserSourceComparison> QueryMemberSourceComparisonCore(
        BrowserSourceComparisonRequest request, CancellationToken cancellationToken)
    {
        await using BrowserMemberResolution.ScopedResolution before =
            await BrowserMemberResolution.ImplementationMemberAsync(
                request.PackageId, request.BeforeVersion, request.Framework, request.Assembly,
                request.TypeIdentity, request.MemberName, request.SelectorKey, request.MetadataToken,
                cancellationToken);
        if (before.Member.Member.MetadataToken != before.Member.BodyToken)
        {
            throw new SourceComparisonUnavailableException(
                "Source comparison requires a whole method declaration, not an accessor body.");
        }

        AssemblyMemberSourceRequest selected = AssemblyMemberSourceRequest.From(
            before.Member.Type, before.Member.Member);
        var pairRequest = new AssemblyMemberSourcePairRequest(selected.Type, selected.Member);

        await using BrowserScopeLease<BrowserInspectionScope> afterLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId, request.AfterVersion, request.Framework, cancellationToken);
        BrowserInspectionScope afterScope = afterLease.Scope;
        BrowserPackageCoordinate afterCoordinate = afterScope.Coordinates[0];
        BrowserWorkspaceParticipant after = afterScope.ImplementationParticipant(
            afterScope.SurfaceParticipant(
                afterCoordinate, afterCoordinate.CompileAsset(request.Assembly)));

        AssemblyMemberSourcePairResult pair = await before.Scope.UseImplementationParticipant(
            before.ImplementationParticipant,
            (beforeGroup, beforeParticipant) => afterScope.UseImplementationParticipant(
                after,
                (afterGroup, afterParticipant) => AssemblyContextMemberSourcePairQuery.ExecuteAsync(
                    beforeGroup, beforeParticipant, afterGroup, afterParticipant,
                    pairRequest, CreateSourceContext(), cancellationToken)));
        cancellationToken.ThrowIfCancellationRequested();
        return BrowserSourceComparisonProjection.Project(
            request, pair, before.ImplementationParticipant, after);
    }

    static void ValidateSourceComparisonRequest(BrowserSourceComparisonRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TypeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SelectorKey);
        if (!NuGetVersion.TryParse(request.BeforeVersion, out _)
            || !NuGetVersion.TryParse(request.AfterVersion, out _))
            throw new ArgumentException("Source comparison requires two exact package versions.");
        if ((request.MetadataToken & unchecked((int)0xff000000)) != 0x06000000
            || (request.MetadataToken & 0x00ffffff) == 0)
            throw new ArgumentException("Source comparison requires a selected MethodDef.");
    }

    sealed class SourceComparisonUnavailableException(string message)
        : InvalidOperationException(message);
}
