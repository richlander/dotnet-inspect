using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserSpotlightTypeFindEventKind>))]
public enum BrowserSpotlightTypeFindEventKind
{
    Finding,
}

public sealed record BrowserSpotlightTypeFindCandidate(
    string Identity,
    string? Action,
    string? Reason,
    string Name,
    string Namespace,
    string Library,
    string Source,
    string DeclarationKind);

public sealed record BrowserSpotlightTypeFindEvent(
    BrowserSpotlightTypeFindEventKind Kind,
    BrowserSpotlightTypeFindCandidate Candidate);

public sealed record BrowserSpotlightTypeFindFailure(
    string Source,
    string Message);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserSpotlightTypeFindCompletion>))]
public enum BrowserSpotlightTypeFindCompletion
{
    Exhausted,
    LimitReached,
    Partial,
}

public sealed record BrowserSpotlightTypeFindInspection(
    BrowserSpotlightTypeFindCandidate[] Candidates,
    BrowserSpotlightTypeFindCompletion Completion,
    BrowserSpotlightTypeFindFailure[] Failures,
    string[] Unsearched);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserSpotlightTypeFindResultKind>))]
public enum BrowserSpotlightTypeFindResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserSpotlightTypeFindFailureKind>))]
public enum BrowserSpotlightTypeFindFailureKind
{
    Expected,
    Unexpected,
}

public sealed record BrowserSpotlightTypeFindResult(
    BrowserSpotlightTypeFindResultKind Kind,
    BrowserSpotlightTypeFindInspection? Inspection,
    BrowserSpotlightTypeFindFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserSpotlightTypeFindResult From(
        BrowserManagedOperationResult<
            BrowserSpotlightTypeFindInspection,
            string,
            string> result) =>
        result switch
        {
            BrowserManagedOperationResult<
                BrowserSpotlightTypeFindInspection,
                string,
                string>.Succeeded succeeded =>
                new(
                    BrowserSpotlightTypeFindResultKind.Succeeded,
                    succeeded.Value,
                    FailureKind: null,
                    Error: null,
                    Diagnostic: null,
                    Reason: null),
            BrowserManagedOperationResult<
                BrowserSpotlightTypeFindInspection,
                string,
                string>.Failed failed =>
                new(
                    BrowserSpotlightTypeFindResultKind.Failed,
                    Inspection: null,
                    failed.FailureKind
                        is BrowserManagedOperationFailureKind.Expected
                            ? BrowserSpotlightTypeFindFailureKind.Expected
                            : BrowserSpotlightTypeFindFailureKind.Unexpected,
                    failed.Error,
                    failed.Diagnostic,
                    Reason: null),
            BrowserManagedOperationResult<
                BrowserSpotlightTypeFindInspection,
                string,
                string>.Canceled canceled =>
                new(
                    BrowserSpotlightTypeFindResultKind.Canceled,
                    Inspection: null,
                    FailureKind: null,
                    Error: null,
                    Diagnostic: null,
                    BrowserManagedOperationCancelReasons.Format(
                        canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

public sealed record BrowserSpotlightTypeFindCancellation(
    string Status,
    string? Reason)
{
    internal static BrowserSpotlightTypeFindCancellation From(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(
                    "Requested",
                    BrowserManagedOperationCancelReasons.Format(
                        requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(
                    "AlreadyRequested",
                    BrowserManagedOperationCancelReasons.Format(
                        requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new("NotActive", Reason: null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    static readonly BrowserManagedOperationBridge SpotlightTypeFindOperations =
        new();

    [JSExport]
    public static string CancelSpotlightTypeFind(
        string operationId,
        string reason)
    {
        BrowserSpotlightTypeFindCancellation result =
            BrowserSpotlightTypeFindCancellation.From(
                SpotlightTypeFindOperations.RequestCancellation(
                    BrowserManagedOperationId.From(operationId),
                    BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserSpotlightTypeFindJsonContext.Default
                .BrowserSpotlightTypeFindCancellation);
    }

    [JSExport]
    public static async Task<string> FindSpotlightTypes(
        string operationId,
        string retainedDefinitionId,
        string realizationId,
        int resultGeneration,
        string text,
        int maximumRows,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        BrowserManagedOperationResult<
            BrowserSpotlightTypeFindInspection,
            string,
            string> operation =
            await SpotlightTypeFindOperations.RunAsync<
                BrowserSpotlightTypeFindInspection,
                string,
                string,
                BrowserSpotlightTypeFindEvent>(
                    BrowserManagedOperationId.From(operationId),
                    findEvent => eventSink.SetProperty(
                        "event",
                        JsonSerializer.Serialize(
                            findEvent,
                            BrowserSpotlightTypeFindJsonContext.Default
                                .BrowserSpotlightTypeFindEvent)),
                    async (token, events) =>
                    {
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            return new BrowserManagedOperationBodyResult<
                                BrowserSpotlightTypeFindInspection,
                                string,
                                string>.Failed(
                                    "Type Find text must not be empty.",
                                    "The Browser supplied an empty Find question.");
                        }
                        if (maximumRows <= 0)
                        {
                            return new BrowserManagedOperationBodyResult<
                                BrowserSpotlightTypeFindInspection,
                                string,
                                string>.Failed(
                                    "Type Find maximum rows must be positive.",
                                    "The Browser supplied an invalid Find bound.");
                        }

                        BrowserEcosystemFindIndexAssetLoad assets =
                            await BrowserEcosystemFindIndexAssets.LoadAsync()
                                .ConfigureAwait(false);
                        var sink = new EventSink(events);
                        var session = new FindSemanticResultSession(
                            maximumRows,
                            sink);
                        BrowserSpotlightTypeFindExecution execution =
                            await BrowserRetainedWorkspaceActivationService
                                .FindSpotlightTypesAsync(
                                    retainedDefinitionId,
                                    realizationId,
                                    assets,
                                    text,
                                    maximumRows,
                                    resultGeneration,
                                    session,
                                    token)
                                .ConfigureAwait(false);
                        FindSemanticResult completed = session.Complete(
                            execution.Completion,
                            execution.Failures,
                            execution.Unsearched);
                        BrowserSpotlightTypeFindInspection inspection =
                            ProjectSpotlightTypeFindInspection(
                                completed,
                                execution);
                        return new BrowserManagedOperationBodyResult<
                            BrowserSpotlightTypeFindInspection,
                            string,
                            string>.Succeeded(inspection);
                    },
                    exception => new(
                        exception.Message,
                        exception.ToString()))
                .ConfigureAwait(false);

        return JsonSerializer.Serialize(
            BrowserSpotlightTypeFindResult.From(operation),
            BrowserSpotlightTypeFindJsonContext.Default
                .BrowserSpotlightTypeFindResult);
    }

    internal static BrowserSpotlightTypeFindInspection
        ProjectSpotlightTypeFindInspection(
        FindSemanticResult completed,
        BrowserSpotlightTypeFindExecution execution)
    {
        var activations =
            new Dictionary<string, BrowserSpotlightTypeFindActivation>(
                StringComparer.Ordinal);
        if (execution.Indexed
            is BrowserIndexedTypeFindExecutionResult.Completed indexed)
        {
            foreach (BrowserIndexedTypeFindActivationAssociation activation
                in indexed.Result.Activations)
            {
                if (activation.Identity is { } identity)
                {
                    activations.TryAdd(
                        identity,
                        new(
                            activation.Status,
                            activation.Action,
                            activation.Reason));
                }
            }
        }
        if (execution.Resident
            is BrowserTypeFindExecutionResult.Completed resident)
        {
            foreach (BrowserTypeFindCandidateActivation activation
                in resident.Result.Activations)
            {
                if (activation.Identity is { } identity)
                {
                    activations.TryAdd(
                        identity,
                        new(
                            activation.Status,
                            activation.Action,
                            activation.Reason));
                }
            }
        }

        BrowserSpotlightTypeFindCandidate[] candidates =
        [
            .. completed.Findings.Select(finding =>
            {
                BrowserSpotlightTypeFindCandidate projected =
                    ProjectSpotlightTypeFindFinding(finding);
                return activations.TryGetValue(
                        projected.Identity,
                        out BrowserSpotlightTypeFindActivation? activation)
                    ? projected with
                    {
                        Action = activation.Status
                                is BrowserTypeFindActivationStatus.Available
                            ? activation.Action
                            : null,
                        Reason = activation.Status
                                is BrowserTypeFindActivationStatus.Available
                            ? null
                            : activation.Reason
                                ?? $"Type activation is {activation.Status.ToString().ToLowerInvariant()}.",
                    }
                    : projected with
                    {
                        Reason =
                            "Type activation correspondence is unavailable.",
                    };
            }),
        ];
        return new(
            candidates,
            completed.Completion switch
            {
                FindSemanticResultCompletion.Exhausted =>
                    BrowserSpotlightTypeFindCompletion.Exhausted,
                FindSemanticResultCompletion.RowLimitReached
                    or FindSemanticResultCompletion.CandidateLimitReached =>
                    BrowserSpotlightTypeFindCompletion.LimitReached,
                _ => BrowserSpotlightTypeFindCompletion.Partial,
            },
            [
                .. completed.Failures.Select(failure =>
                    new BrowserSpotlightTypeFindFailure(
                        failure.Source,
                        failure.Message)),
            ],
            [.. completed.Unsearched]);
    }

    internal static BrowserSpotlightTypeFindCandidate
        ProjectSpotlightTypeFindFinding(FindSemanticFinding finding)
    {
        if (finding is not FindSemanticFinding.Type type)
        {
            throw new InvalidOperationException(
                "Browser Type Find received a non-Type finding.");
        }
        TypeFindSemanticMatch match = type.Match;
        ExactLibrarySourceCoordinate source =
            match.Declaration.Source.Coordinate;
        string identity = BrowserSpotlightTypeIdentity.TryCreate(
                source,
                match.Declaration.Name,
                match.Declaration.ModuleVersionId)
            ?? throw new InvalidOperationException(
                "Browser Type Find could not project stable identity.");
        return new(
            identity,
            Action: null,
            Reason: null,
            match.TypeName,
            match.Namespace,
            source.LibraryIdentity.Identity.Name,
            SourceLabel(source),
            match.DeclarationKind.ToString());
    }

    static string SourceLabel(ExactLibrarySourceCoordinate source) =>
        source switch
        {
            ExactLibrarySourceCoordinate.Package package =>
                $"{package.PackageCoordinate.PackageId}@"
                    + package.PackageCoordinate.Version,
            ExactLibrarySourceCoordinate.Platform platform =>
                platform.Population.Family.ToString(),
            ExactLibrarySourceCoordinate.Project => "Project",
            ExactLibrarySourceCoordinate.Local => "Local",
            _ => throw new InvalidOperationException(
                "Browser Type Find returned an unsupported source."),
        };

    sealed class EventSink(
        IBrowserManagedOperationEvents<BrowserSpotlightTypeFindEvent> events) :
        IFindSemanticFindingSink
    {
        public void Publish(FindSemanticFinding finding) =>
            events.Report(new(
                BrowserSpotlightTypeFindEventKind.Finding,
                ProjectSpotlightTypeFindFinding(finding)));
    }

    sealed record BrowserSpotlightTypeFindActivation(
        BrowserTypeFindActivationStatus Status,
        string? Action,
        string? Reason);
}

internal sealed record BrowserSpotlightTypeFindExecution(
    BrowserIndexedTypeFindExecutionResult? Indexed,
    BrowserTypeFindExecutionResult? Resident,
    FindSemanticResultCompletion Completion,
    ImmutableArray<FindSemanticResultFailure> Failures,
    ImmutableArray<string> Unsearched);

internal static partial class BrowserRetainedWorkspaceActivationService
{
    internal static async ValueTask<BrowserSpotlightTypeFindExecution>
        FindSpotlightTypesAsync(
            string retainedDefinitionId,
            string realizationId,
            BrowserEcosystemFindIndexAssetLoad assets,
            string text,
            int maximumRows,
            long resultGeneration,
            FindSemanticResultSession resultSession,
            CancellationToken cancellationToken) =>
        await FindSpotlightTypesAsync(
                Owner,
                retainedDefinitionId,
                realizationId,
                assets,
                text,
                maximumRows,
                resultGeneration,
                resultSession,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async ValueTask<BrowserSpotlightTypeFindExecution>
        FindSpotlightTypesAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            string realizationId,
            BrowserEcosystemFindIndexAssetLoad assets,
            string text,
            int maximumRows,
            long resultGeneration,
            FindSemanticResultSession resultSession,
            CancellationToken cancellationToken)
    {
        var failures =
            ImmutableArray.CreateBuilder<FindSemanticResultFailure>();
        BrowserIndexedTypeFindExecutionResult? indexed = null;
        if (assets is BrowserEcosystemFindIndexAssetLoad.Available available)
        {
            indexed = await FindIndexedTypesAsync(
                    owner,
                    retainedDefinitionId,
                    realizationId,
                    available.Artifacts,
                    text,
                    maximumRows,
                    resultGeneration,
                    resultSession,
                    cancellationToken)
                .ConfigureAwait(false);
            AddFailure("checked-in index", indexed, failures);
        }
        else
        {
            failures.Add(new(
                "checked-in index",
                ((BrowserEcosystemFindIndexAssetLoad.Invalid)assets).Reason));
        }

        BrowserTypeFindExecutionResult? resident = null;
        if (resultSession.RemainingCapacity != 0)
        {
            resident = await owner.FindTypesAsync(
                    retainedDefinitionId,
                    realizationId,
                    text,
                    resultGeneration,
                    resultSession,
                    cancellationToken)
                .ConfigureAwait(false);
            AddFailure("retained Workspace", resident, failures);
        }

        FindSemanticResultCompletion completion =
            resultSession.RemainingCapacity == 0
                ? FindSemanticResultCompletion.RowLimitReached
                : failures.Count > 0
                    ? FindSemanticResultCompletion.Partial
                    : FindSemanticResultCompletion.Exhausted;
        ImmutableArray<string> unsearched =
            resultSession.RemainingCapacity == 0 && resident is null
                ? ["retained Workspace"]
                : [];
        return new(
            indexed,
            resident,
            completion,
            failures.DrainToImmutable(),
            unsearched);
    }

    static void AddFailure(
        string source,
        BrowserIndexedTypeFindExecutionResult result,
        ImmutableArray<FindSemanticResultFailure>.Builder failures)
    {
        string? reason = result switch
        {
            BrowserIndexedTypeFindExecutionResult.Completed => null,
            BrowserIndexedTypeFindExecutionResult.Rejected rejected =>
                rejected.Reason,
            BrowserIndexedTypeFindExecutionResult.Unavailable unavailable =>
                unavailable.Reason,
            BrowserIndexedTypeFindExecutionResult.Stale stale =>
                stale.Reason,
            _ => "Indexed Type Find returned an unsupported result.",
        };
        if (reason is not null)
            failures.Add(new(source, reason));
    }

    static void AddFailure(
        string source,
        BrowserTypeFindExecutionResult result,
        ImmutableArray<FindSemanticResultFailure>.Builder failures)
    {
        string? reason = result switch
        {
            BrowserTypeFindExecutionResult.Completed completed =>
                ResidentFailure(completed),
            BrowserTypeFindExecutionResult.Rejected rejected =>
                rejected.Reason,
            BrowserTypeFindExecutionResult.Unavailable unavailable =>
                unavailable.Reason,
            BrowserTypeFindExecutionResult.Stale stale =>
                stale.Reason,
            _ => "Type Find returned an unsupported result.",
        };
        if (reason is not null)
            failures.Add(new(source, reason));
    }

    static string? ResidentFailure(
        BrowserTypeFindExecutionResult.Completed completed) =>
        completed.Result.Find.Content switch
        {
            TypeDeclarationLocatorSectionResult.Rejected rejected =>
                $"Type Find was rejected: {rejected.RejectionKind}.",
            TypeDeclarationLocatorSectionResult.Evaluated evaluated
                when !evaluated.IsSuccess
                    || evaluated.Contexts.Any(context =>
                        !context.IsRealized || !context.Failures.IsEmpty)
                    || evaluated.Members.Any(member =>
                        !member.IsComplete) =>
                "Type Find completed with incomplete retained Workspace coverage.",
            _ => null,
        };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserSpotlightTypeFindEvent))]
[JsonSerializable(typeof(BrowserSpotlightTypeFindResult))]
[JsonSerializable(typeof(BrowserSpotlightTypeFindCancellation))]
internal sealed partial class BrowserSpotlightTypeFindJsonContext :
    JsonSerializerContext;
