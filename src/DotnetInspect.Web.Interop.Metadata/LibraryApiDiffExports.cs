using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using DotnetInspect.Web;
using ILInspector.Metadata;
using NuGet.Versioning;
using QuerySpace;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
public static partial class MetadataExports
{
    const int MaxLibraryApiDiffRequestFieldCharacters = 4_096;
    const string BrowserBodyAnalysisUnavailable =
        "Browser/Wasm does not construct method-body comparison inputs.";

    static readonly BrowserManagedOperationBridge LibraryApiDiffOperations =
        new();
    static readonly InspectionCapabilityCatalog DiffAnalysisCapabilities =
        InspectionCapabilityCatalog.Create(
            [DiffAnalysisCatalog.ProductModule]);

    [JSExport]
    public static string CancelLibraryApiDiff(
        string operationId,
        string reason)
    {
        BrowserLibraryApiDiffCancellation result =
            ProjectCancellation(
                LibraryApiDiffOperations.RequestCancellation(
                    BrowserManagedOperationId.From(operationId),
                    BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default
                .BrowserLibraryApiDiffCancellation);
    }

    [JSExport]
    public static async Task<string> QueryLibraryApiDiff(
        string operationId,
        string requestJson)
    {
        BrowserLibraryApiDiffRequest? parsedRequest = null;
        ValidatedLibraryApiDiffRequest? validatedRequest = null;
        Exception? requestError = null;
        try
        {
            parsedRequest =
                JsonSerializer.Deserialize(
                    requestJson,
                    BrowserMetadataJsonContext.Default
                        .BrowserLibraryApiDiffRequest)
                ?? throw new ArgumentException(
                    "A Library API diff request is required.");
            validatedRequest = ValidateLibraryApiDiffRequest(parsedRequest);
        }
        catch (Exception error) when (
            error is ArgumentException
                or JsonException
                or BrowserLibraryApiDiffRequestException)
        {
            requestError = error;
        }

        BrowserLibraryApiDiffRequest? request = null;
        BrowserLibraryApiDiffResult result =
            await RunLibraryApiDiffOperationAsync(
                BrowserManagedOperationId.From(operationId),
                () => request,
                async token =>
                {
                    try
                    {
                        if (requestError is not null)
                        {
                            throw requestError;
                        }

                        request = parsedRequest
                            ?? throw new InvalidOperationException(
                                "The validated Library API diff request is unavailable.");
                        AnalysisSetValidationResult.Accepted selection =
                            validatedRequest?.Selection
                                ?? throw new InvalidOperationException(
                                    "The validated Diff analysis selection is unavailable.");
                        return new BrowserManagedOperationBodyResult<
                            BrowserLibraryApiDiffResult,
                            string,
                            string>.Succeeded(
                                await QueryLibraryApiDiffCore(
                                    request,
                                    selection,
                                    validatedRequest?.StringLiteralQuery,
                                    token));
                    }
                    catch (Exception error) when (
                        error is ArgumentException
                            or JsonException
                            or BrowserLibraryApiDiffRequestException)
                    {
                        return new BrowserManagedOperationBodyResult<
                            BrowserLibraryApiDiffResult,
                            string,
                            string>.Failed(
                                error.Message,
                                error.ToString());
                    }
                });
        return JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
    }

    internal static async Task<BrowserLibraryApiDiffResult>
        RunLibraryApiDiffOperationAsync(
            BrowserManagedOperationId operationId,
            Func<BrowserLibraryApiDiffRequest?> request,
            Func<
                CancellationToken,
                Task<
                    BrowserManagedOperationBodyResult<
                        BrowserLibraryApiDiffResult,
                        string,
                        string>>> body)
    {
        BrowserManagedOperationResult<
            BrowserLibraryApiDiffResult,
            string,
            string> result =
            await LibraryApiDiffOperations.RunAsync<
                BrowserLibraryApiDiffResult,
                string,
                string,
                object>(
                    operationId,
                    eventCallback: null,
                    (token, _) => body(token),
                    error => new(error.Message, error.ToString()));
        return result switch
        {
            BrowserManagedOperationResult<
                BrowserLibraryApiDiffResult,
                string,
                string>.Succeeded success =>
                    success.Value,
            BrowserManagedOperationResult<
                BrowserLibraryApiDiffResult,
                string,
                string>.Failed failure =>
                    new BrowserLibraryApiDiffResult(
                        BrowserLibraryApiDiffSchema.Version,
                        request(),
                        BrowserLibraryApiDiffResultKind.Failed,
                        Value: null,
                        Unavailable: null,
                        Rejected: null,
                        failure.FailureKind switch
                        {
                            BrowserManagedOperationFailureKind.Expected =>
                                BrowserLibraryApiDiffFailureKind.Expected,
                            BrowserManagedOperationFailureKind.Unexpected =>
                                BrowserLibraryApiDiffFailureKind.Unexpected,
                            _ => throw new ArgumentOutOfRangeException(
                                nameof(result)),
                        },
                        failure.Error,
                        failure.Diagnostic,
                        Reason: null),
            BrowserManagedOperationResult<
                BrowserLibraryApiDiffResult,
                string,
                string>.Canceled canceled =>
                    new BrowserLibraryApiDiffResult(
                        BrowserLibraryApiDiffSchema.Version,
                        request(),
                        BrowserLibraryApiDiffResultKind.Canceled,
                        Value: null,
                        Unavailable: null,
                        Rejected: null,
                        FailureKind: null,
                        Error: null,
                        Diagnostic: null,
                        BrowserManagedOperationCancelReasons.Format(
                            canceled.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown managed Library API diff outcome."),
        };
    }

    static async Task<BrowserLibraryApiDiffResult> QueryLibraryApiDiffCore(
        BrowserLibraryApiDiffRequest request,
        AnalysisSetValidationResult.Accepted selection,
        StringLiteralComparisonQueryPlan? stringLiteralQuery,
        CancellationToken cancellationToken)
    {
        await using BrowserScopeLease<BrowserInspectionScope> targetLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId,
                request.TargetVersion,
                request.TargetFramework,
                cancellationToken);
        BrowserInspectionScope targetScope = targetLease.Scope;
        BrowserPackageCoordinate targetCoordinate =
            targetScope.Coordinates[0];
        PackageCompileAsset targetAsset = RequireExactCompileAsset(
            targetCoordinate,
            request.CompileAssetId,
            "target");
        BrowserWorkspaceParticipant targetParticipant =
            targetScope.SurfaceParticipant(targetCoordinate, targetAsset);

        await using BrowserScopeLease<BrowserInspectionScope> currentLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                request.PackageId,
                request.CurrentVersion,
                request.TargetFramework,
                cancellationToken);
        BrowserInspectionScope currentScope = currentLease.Scope;
        BrowserPackageCoordinate currentCoordinate =
            currentScope.Coordinates[0];
        PackageCompileAsset currentAsset = RequireExactCompileAsset(
            currentCoordinate,
            request.CompileAssetId,
            "current");
        BrowserWorkspaceParticipant currentParticipant =
            currentScope.SurfaceParticipant(currentCoordinate, currentAsset);

        cancellationToken.ThrowIfCancellationRequested();
        if (request.Surface is not BrowserDiffAnalysisSurface.Member
            && stringLiteralQuery is null)
        {
            InspectionEnvelope<LibraryDiffSummaryOutcome> summary =
                targetScope.UseSurfaceParticipant(
                    targetParticipant,
                    (targetGroup, target) =>
                        currentScope.UseSurfaceParticipant(
                            currentParticipant,
                            (currentGroup, current) =>
                                LibraryDiffSummaryInspection.Execute(
                                    targetGroup,
                                    target,
                                    currentGroup,
                                    current,
                                    BrowserApiSurfacePolicy.Limits,
                                    new HashSet<string>(
                                        request.TypeNames,
                                        StringComparer.Ordinal),
                                    request.Surface
                                        is BrowserDiffAnalysisSurface.Type
                                            ? ApiDiffScope.Signature
                                                | ApiDiffScope.Attributes
                                            : ApiDiffScope.Signature)));
            cancellationToken.ThrowIfCancellationRequested();
            return BrowserLibraryApiDiffWireProjection.Project(
                request,
                summary,
                Context(targetParticipant),
                Context(currentParticipant));
        }

        (
            InspectionEnvelope<DiffAnalysisDocument> Detail,
            InspectionEnvelope<LibraryDiffSummaryOutcome>? Summary)
            inspections =
            targetScope.UseSurfaceParticipant(
                targetParticipant,
                (targetGroup, target) =>
                    currentScope.UseSurfaceParticipant(
                        currentParticipant,
                        (currentGroup, current) =>
                        {
                            var typeFilters =
                                new HashSet<string>(
                                    request.TypeNames,
                                    StringComparer.Ordinal);
                            InspectionEnvelope<
                                DiffAnalysisDocument> detail =
                                DiffAnalysisLibraryInspection.Execute(
                                targetGroup,
                                target,
                                currentGroup,
                                current,
                                ApiSurfaceScope.Public,
                                BrowserApiSurfacePolicy.Limits,
                                new DiffAnalysisLibraryInspectionRequest(
                                    request.PackageId,
                                    request.TargetVersion,
                                    request.CurrentVersion,
                                    DiffAnalysisCapabilities,
                                    selection,
                                    ViewsOf(request.Views),
                                    typeFilters,
                                    request.TypeNames,
                                    selection.Surface
                                        == AnalysisReportSurfaceKind.Member
                                            ? new HashSet<string>(
                                                request.MemberTargetIdentities,
                                                StringComparer.Ordinal)
                                            : null,
                                    BeforePaths: [],
                                    AfterPaths: [],
                                    PrepareBodySignals: null,
                                    PrepareImplementation: null,
                                    HostUnavailability:
                                        BrowserHostUnavailability(selection),
                                    StringLiteralQuery:
                                        stringLiteralQuery,
                                    ApiDiffScope:
                                        request.Surface
                                            is BrowserDiffAnalysisSurface.Member
                                                ? ApiDiffScope.Signature
                                                    | ApiDiffScope.Attributes
                                                : ApiDiffScope.Signature));
                            InspectionEnvelope<
                                LibraryDiffSummaryOutcome>? summary =
                                request.Surface
                                    is BrowserDiffAnalysisSurface.Member
                                        ? LibraryDiffSummaryInspection.Execute(
                                            targetGroup,
                                            target,
                                            currentGroup,
                                            current,
                                            BrowserApiSurfacePolicy.Limits,
                                            typeFilters,
                                            ApiDiffScope.Signature
                                                | ApiDiffScope.Attributes,
                                            new HashSet<string>(
                                                request
                                                    .MemberTargetIdentities,
                                                StringComparer.Ordinal))
                                        : null;
                            return (detail, summary);
                        }));
        cancellationToken.ThrowIfCancellationRequested();

        return BrowserLibraryApiDiffWireProjection.Project(
            request,
            inspections.Detail,
            Context(targetParticipant),
            Context(currentParticipant),
            inspections.Summary);
    }

    static BrowserLibraryApiDiffEndpointContext Context(
        BrowserWorkspaceParticipant participant) =>
        new(
            participant.Coordinate.PackageId,
            participant.Coordinate.Version,
            participant.Coordinate.Framework,
            participant.Asset.Id,
            participant.Asset.Path,
            participant.Asset.AssemblyName);

    static PackageCompileAsset RequireExactCompileAsset(
        BrowserPackageCoordinate coordinate,
        string compileAssetId,
        string role)
    {
        if (!coordinate.Selection.IsSelected)
        {
            throw new BrowserLibraryApiDiffRequestException(
                $"The {role} package endpoint has no selected compile library "
                    + $"({coordinate.Selection.Status}).");
        }

        return coordinate.Selection.FindAsset(compileAssetId)
            ?? throw new BrowserLibraryApiDiffRequestException(
                $"The exact compile asset '{compileAssetId}' is not selected "
                    + $"by the {role} package endpoint "
                    + $"{coordinate.PackageId} {coordinate.Version} "
                    + $"for framework '{coordinate.Framework}'.");
    }

    sealed record ValidatedLibraryApiDiffRequest(
        AnalysisSetValidationResult.Accepted Selection,
        StringLiteralComparisonQueryPlan? StringLiteralQuery);

    static ValidatedLibraryApiDiffRequest ValidateLibraryApiDiffRequest(
        BrowserLibraryApiDiffRequest request)
    {
        if (request.SchemaVersion != BrowserLibraryApiDiffSchema.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The Library API diff request schema version is unsupported.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CompileAssetId);
        RequireBoundedRequestField(request.PackageId, nameof(request.PackageId));
        RequireBoundedRequestField(
            request.CurrentVersion,
            nameof(request.CurrentVersion));
        RequireBoundedRequestField(
            request.TargetVersion,
            nameof(request.TargetVersion));
        RequireBoundedRequestField(
            request.TargetFramework,
            nameof(request.TargetFramework));
        RequireBoundedRequestField(
            request.CompileAssetId,
            nameof(request.CompileAssetId));
        _ = BrowserFrameworkText.Require(request.TargetFramework);
        if (!NuGetVersion.TryParse(request.CurrentVersion, out _)
            || !NuGetVersion.TryParse(request.TargetVersion, out _))
        {
            throw new ArgumentException(
                "Library API diff requires exact current and target package versions.",
                nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Analyses);
        ArgumentNullException.ThrowIfNull(request.TypeNames);
        ArgumentNullException.ThrowIfNull(request.MemberTargetIdentities);
        DiffAnalysisDocumentViews views = ViewsOf(request.Views);
        if (views == DiffAnalysisDocumentViews.None)
        {
            throw new BrowserLibraryApiDiffRequestException(
                "The Diff analysis view selection is unsupported.");
        }
        AnalysisReportSurfaceKind surface = SurfaceOf(request.Surface);
        int targetCount = surface switch
        {
            AnalysisReportSurfaceKind.Library
                when request.TypeNames.Length == 0
                    && request.MemberTargetIdentities.Length == 0 => 1,
            AnalysisReportSurfaceKind.Type
                when request.MemberTargetIdentities.Length == 0 =>
                    request.TypeNames.Length,
            AnalysisReportSurfaceKind.Member =>
                request.MemberTargetIdentities.Length,
            AnalysisReportSurfaceKind.Library or AnalysisReportSurfaceKind.Type =>
                throw new BrowserLibraryApiDiffRequestException(
                    "The Diff analysis target identities do not match the selected surface."),
            _ => throw new BrowserLibraryApiDiffRequestException(
                "The Diff analysis surface is unsupported in Browser Compare."),
        };
        AnalysisSetValidationResult validation =
            DiffAnalysisCapabilities.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                surface,
                targetCount,
                request.Analyses);
        if (validation is AnalysisSetValidationResult.Accepted accepted)
        {
            DiffAnalysisViewRejectionReason? viewRejection =
                DiffAnalysisViewAdmission.Validate(accepted, views);
            if (viewRejection is not null)
            {
                throw new BrowserLibraryApiDiffRequestException(
                    viewRejection switch
                    {
                        DiffAnalysisViewRejectionReason.ChangesRequireApi =>
                            "The Diff analysis view selection was rejected: "
                                + "Changes requires the 'api' analysis.",
                        DiffAnalysisViewRejectionReason
                            .TransitionsRequireSupportedSurface =>
                                "The Diff analysis view selection was rejected: "
                                    + "no selected analysis declares Transitions "
                                    + "at this report surface.",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(viewRejection)),
                    });
            }

            bool requiresPredicate =
                DiffAnalysisCatalog.RequiresStringLiteralQuery(accepted);
            if (requiresPredicate != (request.Predicate is not null))
            {
                throw new BrowserLibraryApiDiffRequestException(
                    requiresPredicate
                        ? "The string-literals analysis requires one Literal predicate."
                        : "The request supplied a predicate that no selected analysis consumes.");
            }
            StringLiteralComparisonQueryPlan? stringLiteralQuery = null;
            if (request.Predicate is { } predicate)
            {
                RequireBoundedRequestField(
                    predicate.Key,
                    nameof(BrowserDiffAnalysisPredicate.Key));
                RequireBoundedRequestField(
                    predicate.Value,
                    nameof(BrowserDiffAnalysisPredicate.Value));
                PortableQueryOperator @operator = predicate.Operator switch
                {
                    BrowserDiffAnalysisPredicateOperator.Contains =>
                        PortableQueryOperator.Contains,
                    BrowserDiffAnalysisPredicateOperator.StartsWith =>
                        PortableQueryOperator.StartsWith,
                    _ => throw new BrowserLibraryApiDiffRequestException(
                        "The Diff analysis predicate operator is unsupported."),
                };
                PortableQueryIntent intent =
                    StringLiteralComparisonQuery.CreateIntent(
                        @operator,
                        predicate.Value);
                if (predicate.Key
                    != StringLiteralComparisonQuery.LiteralKey)
                {
                    throw new BrowserLibraryApiDiffRequestException(
                        "String-literal Diff accepts only the exact predicate "
                            + "key 'Literal'.");
                }
                stringLiteralQuery =
                    StringLiteralComparisonQuery.ResolveIntent(intent) switch
                    {
                        StringLiteralComparisonQueryPlanResult.Accepted
                            resolved => resolved.Plan,
                        StringLiteralComparisonQueryPlanResult.Rejected
                            queryRejected => throw new BrowserLibraryApiDiffRequestException(
                                "The string-literal Diff predicate was rejected: "
                                    + queryRejected.Failure.Reason),
                        _ => throw new InvalidOperationException(
                            "Unknown string-literal query-plan result."),
                    };
            }
            return new(accepted, stringLiteralQuery);
        }

        var rejected = (AnalysisSetValidationResult.Rejected)validation;
        throw new BrowserLibraryApiDiffRequestException(
            "The Diff analysis selection was rejected: "
                + string.Join(
                    "; ",
                    rejected.Rejections.Select(rejection =>
                        $"{rejection.RequestedIdentity ?? "<set>"}: "
                            + $"{rejection.SetReason?.ToString()
                                ?? rejection.RequestReason?.ToString()
                                ?? "unsupported"}")));
    }

    static DiffAnalysisHostUnavailability[] BrowserHostUnavailability(
        AnalysisSetValidationResult.Accepted selection) =>
    [
        .. selection.Analyses
            .Where(analysis =>
                analysis.ParticipationFor(AnalysisOperationKind.Compare)
                    ?.For(selection.Surface)?.ProducerRoute is { } route
                && (route == DiffAnalysisCatalog.BodySignalRoute
                    || route == DiffAnalysisCatalog.RetainedResearchRoute))
            .Select(analysis => new DiffAnalysisHostUnavailability(
                analysis.Id,
                BrowserBodyAnalysisUnavailable)),
    ];

    static AnalysisReportSurfaceKind SurfaceOf(
        BrowserDiffAnalysisSurface surface) =>
        surface switch
        {
            BrowserDiffAnalysisSurface.Member =>
                AnalysisReportSurfaceKind.Member,
            BrowserDiffAnalysisSurface.Type =>
                AnalysisReportSurfaceKind.Type,
            BrowserDiffAnalysisSurface.Library =>
                AnalysisReportSurfaceKind.Library,
            _ => throw new BrowserLibraryApiDiffRequestException(
                "The Diff analysis surface is unsupported in Browser Compare."),
        };

    static DiffAnalysisDocumentViews ViewsOf(BrowserDiffAnalysisViews views)
    {
        if ((views & ~(
                BrowserDiffAnalysisViews.Changes
                | BrowserDiffAnalysisViews.Summary
                | BrowserDiffAnalysisViews.Transitions)) != 0)
        {
            throw new BrowserLibraryApiDiffRequestException(
                "The Diff analysis view selection is unsupported.");
        }
        return (DiffAnalysisDocumentViews)(int)views;
    }

    static void RequireBoundedRequestField(string value, string parameterName)
    {
        if (value.Length > MaxLibraryApiDiffRequestFieldCharacters)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Library API diff request fields are limited to "
                    + $"{MaxLibraryApiDiffRequestFieldCharacters} characters.");
        }
    }

    static BrowserLibraryApiDiffCancellation ProjectCancellation(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(
                    BrowserLibraryApiDiffCancellationKind.Requested,
                    BrowserManagedOperationCancelReasons.Format(
                        requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(
                    BrowserLibraryApiDiffCancellationKind.AlreadyRequested,
                    BrowserManagedOperationCancelReasons.Format(
                        requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new(
                    BrowserLibraryApiDiffCancellationKind.NotActive,
                    Reason: null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

    sealed class BrowserLibraryApiDiffRequestException(string message)
        : InvalidOperationException(message);
}
