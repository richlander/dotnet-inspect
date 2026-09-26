using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;
using PackageAdmission = DotnetInspect.Web.BrowserRetainedWorkspaceAdmissionResult<DotnetInspect.Web.BrowserRetainedWorkspacePackagePresentation>;
using PlatformAdmission = DotnetInspect.Web.BrowserRetainedWorkspaceAdmissionResult<DotnetInspect.Web.BrowserRetainedWorkspacePlatformPresentation>;

namespace DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    [JSExport]
    public static async Task<string> PrepareRetainedWorkspaceDefinition(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
    {
        BrowserRetainedWorkspacePreparationResult result =
            await BrowserRetainedWorkspaceActivationService.PrepareAsync(
                    retainedDefinitionId,
                    label,
                    canonicalLocation,
                    canonicalPacket)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspacePreparationResult);
    }

    [JSExport]
    public static async Task<string>
        PrepareRetainedWorkspaceDefinitionWithCredentials(
            string retainedDefinitionId,
            string label,
            string canonicalLocation,
            string canonicalPacket,
            string packageSourceCredentialsJson)
    {
        Dictionary<
            string,
            BrowserRetainedWorkspacePackageSourceCredential>
            packageSourceCredentials;
        try
        {
            BrowserRetainedWorkspaceActivationService
                .ValidatePackageSourceCredentialsJson(
                    packageSourceCredentialsJson);
            packageSourceCredentials = JsonSerializer.Deserialize(
                    packageSourceCredentialsJson,
                    BrowserCatalogJsonContext.Default
                        .DictionaryStringBrowserRetainedWorkspacePackageSourceCredential)
                ?? throw new JsonException();
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(
                BrowserRetainedWorkspaceActivationService
                    .InvalidCredentialPreparation(ex.Message),
                BrowserCatalogJsonContext.Default
                    .BrowserRetainedWorkspacePreparationResult);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(
                BrowserRetainedWorkspaceActivationService
                    .InvalidCredentialPreparation(
                        BrowserRetainedWorkspaceActivationService
                            .InvalidPackageSourceCredentialsMessage),
                BrowserCatalogJsonContext.Default
                    .BrowserRetainedWorkspacePreparationResult);
        }

        BrowserRetainedWorkspacePreparationResult result =
            await BrowserRetainedWorkspaceActivationService.PrepareAsync(
                    retainedDefinitionId,
                    label,
                    canonicalLocation,
                    canonicalPacket,
                    packageSourceCredentials)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspacePreparationResult);
    }

    [JSExport]
    public static async Task<string> CommitRetainedWorkspaceActivation(
        string receipt)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.CommitAsync(
                    receipt)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static async Task<string> CancelRetainedWorkspaceActivation(
        string receipt)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.CancelAsync(
                    receipt)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static string CompleteRetainedWorkspaceActivation(
        string receipt,
        bool succeeded,
        string? failure)
    {
        BrowserRetainedWorkspaceConsumerCompletionResult result =
            BrowserRetainedWorkspaceActivationService.CompleteActivation(
                receipt,
                succeeded,
                failure);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceConsumerCompletionResult);
    }

    [JSExport]
    public static async Task<string> AdmitRetainedWorkspacePackage(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        int typeOffset = 0)
    {
        BrowserRetainedWorkspacePackageAdmissionResult result =
            await BrowserRetainedWorkspaceActivationService.AdmitPackageAsync(
                retainedDefinitionId,
                realizationId,
                navigationId,
                typeOffset).ConfigureAwait(false);
        result = BrowserRetainedWorkspaceDetailWireProjection.Admit(result);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePackageAdmissionResult);
    }

    [JSExport]
    public static async Task<string> AdmitRetainedWorkspacePlatform(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        int typeOffset = 0)
    {
        BrowserRetainedWorkspacePlatformAdmissionResult result =
            await BrowserRetainedWorkspaceActivationService.AdmitPlatformAsync(
                retainedDefinitionId,
                realizationId,
                navigationId,
                typeOffset).ConfigureAwait(false);
        result = BrowserRetainedWorkspaceDetailWireProjection.Admit(result);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePlatformAdmissionResult);
    }

    [JSExport]
    public static async Task<string> ActivateRetainedWorkspaceDefinition(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.ActivateAsync(
                    retainedDefinitionId,
                    label,
                    canonicalLocation,
                    canonicalPacket)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static async Task<string>
        ActivateRetainedWorkspaceDefinitionWithCredentials(
            string retainedDefinitionId,
            string label,
            string canonicalLocation,
            string canonicalPacket,
            string packageSourceCredentialsJson)
    {
        Dictionary<
            string,
            BrowserRetainedWorkspacePackageSourceCredential>
            packageSourceCredentials;
        try
        {
            BrowserRetainedWorkspaceActivationService
                .ValidatePackageSourceCredentialsJson(
                    packageSourceCredentialsJson);
            packageSourceCredentials = JsonSerializer.Deserialize(
                    packageSourceCredentialsJson,
                    BrowserCatalogJsonContext.Default
                        .DictionaryStringBrowserRetainedWorkspacePackageSourceCredential)
                ?? throw new JsonException();
        }
        catch (ArgumentException ex)
        {
            return JsonSerializer.Serialize(
                BrowserRetainedWorkspaceActivationService
                    .InvalidCredentialActivation(ex.Message),
                BrowserCatalogJsonContext.Default
                    .BrowserRetainedWorkspaceActivationResult);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(
                BrowserRetainedWorkspaceActivationService
                    .InvalidCredentialActivation(
                        BrowserRetainedWorkspaceActivationService
                            .InvalidPackageSourceCredentialsMessage),
                BrowserCatalogJsonContext.Default
                    .BrowserRetainedWorkspaceActivationResult);
        }

        BrowserRetainedWorkspaceActivationResult result =
            await BrowserRetainedWorkspaceActivationService.ActivateAsync(
                    retainedDefinitionId,
                    label,
                    canonicalLocation,
                    canonicalPacket,
                    packageSourceCredentials)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceActivationResult);
    }

    [JSExport]
    public static string DescribeWorkspacePackageSources(
        string canonicalPacket)
    {
        BrowserWorkspacePackageSourceRequirementsResult result =
            BrowserRetainedWorkspaceActivationService
                .DescribePackageSources(canonicalPacket);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserWorkspacePackageSourceRequirementsResult);
    }

    [JSExport]
    public static async Task<string> DeactivateRetainedWorkspaceDefinition(
        string retainedDefinitionId)
    {
        BrowserRetainedWorkspaceDeactivationResult result =
            await BrowserRetainedWorkspaceActivationService.DeactivateAsync(
                    retainedDefinitionId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceDeactivationResult);
    }

    [JSExport]
    public static string CompleteRetainedWorkspaceDeactivation(
        string receipt,
        bool succeeded,
        string? failure)
    {
        BrowserRetainedWorkspaceConsumerCompletionResult result =
            BrowserRetainedWorkspaceActivationService.CompleteDeactivation(
                receipt,
                succeeded,
                failure);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceConsumerCompletionResult);
    }

    [JSExport]
    public static async Task<string> ObserveRetainedWorkspaceSettlement(
        string settlementId)
    {
        BrowserRetainedWorkspaceSettlementResult result =
            await BrowserRetainedWorkspaceActivationService
                .ObserveSettlementAsync(settlementId)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default
                .BrowserRetainedWorkspaceSettlementResult);
    }

    [JSExport]
    public static string RecordRetainedWorkspaceNavigationPosting(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService
            .RecordConsumerPosting(
                realizationId,
                RequirePublicationOrdinal(publicationOrdinal),
                new(session, revision, intent, epoch));

    [JSExport]
    public static bool ValidateRetainedWorkspaceNavigationAuthority(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService
            .ValidateNavigationAuthority(
                realizationId,
                RequirePublicationOrdinal(publicationOrdinal),
                new(session, revision, intent, epoch));

    [JSExport]
    public static string AcknowledgeRetainedWorkspaceNavigation(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService.Acknowledge(
            realizationId,
            RequirePublicationOrdinal(publicationOrdinal),
            new(session, revision, intent, epoch));

    [JSExport]
    public static string AbandonRetainedWorkspaceNavigation(
        string realizationId,
        double publicationOrdinal,
        string session,
        string revision,
        string intent,
        string epoch) =>
        BrowserRetainedWorkspaceActivationService.Abandon(
            realizationId,
            RequirePublicationOrdinal(publicationOrdinal),
            new(session, revision, intent, epoch));

    static long RequirePublicationOrdinal(double value)
    {
        const double maximumSafeInteger = 9_007_199_254_740_991d;
        if (!double.IsFinite(value)
            || value < 0
            || value > maximumSafeInteger
            || Math.Truncate(value) != value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The publication ordinal must be a non-negative safe integer.");
        }

        return checked((long)value);
    }
}

[SupportedOSPlatform("browser")]
internal static class BrowserRetainedWorkspaceActivationService
{
    internal const int PackageSourceCredentialsJsonLengthLimit = 2 * 1024 * 1024;
    internal const string InvalidPackageSourceCredentialsMessage =
        "Workspace credential bindings must be one JSON object of "
        + "source endpoints to username/PAT objects.";

    static readonly object Gate = new();
    static readonly Dictionary<
        string,
        BrowserRetainedWorkspaceActivationSession> Sessions =
        new(StringComparer.Ordinal);
    internal static BrowserRetainedWorkspaceActivationOwner Owner =>
        BrowserRetainedWorkspaceActivationRegistry.Owner;

    internal static async Task<BrowserRetainedWorkspacePackageAdmissionResult>
        AdmitPackageAsync(
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            int typeOffset)
    {
        PackageAdmission result =
            await Owner.AdmitPackageAsync(
                retainedDefinitionId,
                realizationId,
                navigationId).ConfigureAwait(false);
        return result switch
        {
            PackageAdmission.Admitted admitted when
                ValidOffset(typeOffset, admitted.Presentation.Surface) =>
                new("admitted", Package(admitted.Presentation, typeOffset), null),
            PackageAdmission.Admitted =>
                new("unavailable", null, "The requested Type offset is outside the retained Package inventory."),
            PackageAdmission.Superseded => new("superseded", null, null),
            PackageAdmission.Unavailable unavailable =>
                new("unavailable", null, unavailable.Message),
            _ => throw new InvalidOperationException(
                "Retained Package admission returned an unsupported result."),
        };
    }

    internal static async Task<BrowserRetainedWorkspacePlatformAdmissionResult>
        AdmitPlatformAsync(
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            int typeOffset)
    {
        PlatformAdmission result = await Owner.AdmitPlatformAsync(
            retainedDefinitionId, realizationId, navigationId).ConfigureAwait(false);
        return result switch
        {
            PlatformAdmission.Admitted admitted when
                ValidOffset(typeOffset, admitted.Presentation.Surface) =>
                new("admitted", Platform(admitted.Presentation, typeOffset), null),
            PlatformAdmission.Admitted =>
                new("unavailable", null, "The requested Type offset is outside the retained Platform inventory."),
            PlatformAdmission.Superseded => new("superseded", null, null),
            PlatformAdmission.Unavailable unavailable =>
                new("unavailable", null, unavailable.Message),
            _ => throw new InvalidOperationException(
                "Retained Platform admission returned an unsupported result."),
        };
    }

    internal static async Task<BrowserRetainedWorkspacePreparationResult>
        PrepareAsync(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
        => await PrepareAsync(
            retainedDefinitionId,
            label,
            canonicalLocation,
            canonicalPacket,
            packageSourceCredentials: new Dictionary<
                string,
                BrowserRetainedWorkspacePackageSourceCredential>(
                    StringComparer.Ordinal)).ConfigureAwait(false);

    internal static async Task<BrowserRetainedWorkspacePreparationResult>
        PrepareAsync(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket,
        IReadOnlyDictionary<
            string,
            BrowserRetainedWorkspacePackageSourceCredential>
            packageSourceCredentials)
    {
        BrowserRetainedWorkspaceActivationRequest request;
        try
        {
            request = new(
                retainedDefinitionId,
                label,
                canonicalLocation,
                canonicalPacket,
                BindPackageSourceCredentials(packageSourceCredentials));
        }
        catch (ArgumentException ex)
        {
            return new(
                "failed",
                null,
                null,
                null,
                new("InvalidRequest", ex.Message));
        }

        BrowserRetainedWorkspaceActivationSession session =
            Owner.BeginActivation(request);
        lock (Gate)
            Sessions.Add(session.Receipt, session);

        DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
            preparation;
        try
        {
            preparation = await session.Preparation.ConfigureAwait(false);
        }
        catch
        {
            lock (Gate)
                Sessions.Remove(session.Receipt);
            throw;
        }

        if (preparation
            is not DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                .Prepared)
        {
            lock (Gate)
                Sessions.Remove(session.Receipt);
        }

        return preparation switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .Prepared prepared =>
                new(
                    "prepared",
                    session.Receipt,
                    PreparedPosting(prepared.Posting),
                    null,
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .NoEffect noEffect =>
                new(
                    "noEffect",
                    null,
                    null,
                    Posting(noEffect.Posting),
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .Superseded =>
                new("superseded", null, null, null, null),
            DotnetInspect.Web.BrowserRetainedWorkspacePreparationResult
                    .Failed failed =>
                new(
                    "failed",
                    null,
                    null,
                    null,
                    new(
                        failed.Failure.GetType().Name,
                        failed.Failure.Message)),
            _ => throw new InvalidOperationException(
                "Retained Workspace preparation returned an unsupported result."),
        };
    }

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        CommitAsync(string receipt)
    {
        BrowserRetainedWorkspaceActivationSession? session =
            FindSession(receipt);
        if (session is null || !session.Commit())
        {
            return new(
                "failed",
                null,
                new(
                    "InvalidRequest",
                    "The retained Workspace activation receipt cannot commit."));
        }

        DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult activation;
        try
        {
            activation = await session.Activation.ConfigureAwait(false);
        }
        catch
        {
            lock (Gate)
                Sessions.Remove(receipt);
            throw;
        }
        if (activation
            is not DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                .Activated)
        {
            lock (Gate)
                Sessions.Remove(receipt);
        }
        return Activation(activation);
    }

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        CancelAsync(string receipt)
    {
        BrowserRetainedWorkspaceActivationSession? session =
            FindSession(receipt);
        if (session is null)
        {
            return new(
                "failed",
                null,
                new(
                    "InvalidRequest",
                    "The retained Workspace activation receipt is unavailable."));
        }
        bool cancelled = session.Cancel();
        DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult activation;
        try
        {
            activation = await session.Activation.ConfigureAwait(false);
        }
        catch
        {
            lock (Gate)
                Sessions.Remove(receipt);
            throw;
        }
        if (!cancelled
            && activation
                is DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Activated)
        {
            return new(
                "failed",
                null,
                new(
                    "InvalidRequest",
                    "The retained Workspace activation receipt is already committing."));
        }

        try
        {
            return Activation(activation);
        }
        finally
        {
            lock (Gate)
                Sessions.Remove(receipt);
        }
    }

    internal static BrowserRetainedWorkspaceConsumerCompletionResult
        CompleteActivation(
            string receipt,
            bool succeeded,
            string? failure)
    {
        BrowserRetainedWorkspaceActivationSession? session =
            FindSession(receipt);
        if (session is null)
        {
            return new(
                "unavailable",
                null,
                null,
                "The retained Workspace activation receipt is unavailable.");
        }

        DotnetInspect.Web.BrowserRetainedWorkspaceConsumerCompletionResult
            completion = session.Complete(succeeded, failure);
        if (completion
            is DotnetInspect.Web
                .BrowserRetainedWorkspaceConsumerCompletionResult.Completed)
        {
            lock (Gate)
                Sessions.Remove(receipt);
        }
        return Completion(completion);
    }

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        ActivateAsync(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
        => await ActivateAsync(
            retainedDefinitionId,
            label,
            canonicalLocation,
            canonicalPacket,
            packageSourceCredentials: new Dictionary<
                string,
                BrowserRetainedWorkspacePackageSourceCredential>(
                    StringComparer.Ordinal)).ConfigureAwait(false);

    internal static async Task<BrowserRetainedWorkspaceActivationResult>
        ActivateAsync(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket,
        IReadOnlyDictionary<
            string,
            BrowserRetainedWorkspacePackageSourceCredential>
            packageSourceCredentials)
    {
        BrowserRetainedWorkspaceActivationRequest request;
        try
        {
            request = new(
                retainedDefinitionId,
                label,
                canonicalLocation,
                canonicalPacket,
                BindPackageSourceCredentials(packageSourceCredentials));
        }
        catch (ArgumentException ex)
        {
            return new(
                "failed",
                null,
                new("InvalidRequest", ex.Message));
        }

        DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
            activation = await Owner.ActivateAsync(request)
                .ConfigureAwait(false);
        return Activation(activation);
    }

    internal static BrowserWorkspacePackageSourceRequirementsResult
        DescribePackageSources(string canonicalPacket)
    {
        try
        {
            WorkspaceSharePacket packet =
                WorkspaceSharePacketCodec.Decode(canonicalPacket);
            return new(
                true,
                [
                    .. packet.PackageSources.Select(source =>
                        new BrowserWorkspacePackageSourceRequirement(
                            source.Endpoint,
                            source.Authentication
                                switch
                                {
                                    WorkspacePackageSourceAuthentication.Anonymous =>
                                        BrowserWorkspacePackageSourceAuthentication
                                        .Anonymous,
                                    WorkspacePackageSourceAuthentication.AuthenticationRequired =>
                                        BrowserWorkspacePackageSourceAuthentication
                                        .AuthenticationRequired,
                                    _ => throw new InvalidOperationException(
                                        "Unsupported Workspace package-source authentication."),
                                })),
                ],
                null);
        }
        catch (WorkspaceSharePacketException ex)
        {
            return new(
                false,
                [],
                new(ex.Kind.ToString(), "packet", ex.Message));
        }
    }

    internal static BrowserRetainedWorkspacePreparationResult
        InvalidCredentialPreparation(
        string message) =>
        new(
            "failed",
            null,
            null,
            null,
            new("InvalidRequest", message));

    internal static BrowserRetainedWorkspaceActivationResult
        InvalidCredentialActivation(
        string message) =>
        new(
            "failed",
            null,
            new("InvalidRequest", message));

    internal static void ValidatePackageSourceCredentialsJson(string json)
    {
        if (json.Length > PackageSourceCredentialsJsonLengthLimit)
        {
            throw new ArgumentException(
                "Workspace credential bindings exceed the browser transport limit.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 5,
                });
        }
        catch (JsonException)
        {
            throw new ArgumentException(
                InvalidPackageSourceCredentialsMessage);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException(InvalidPackageSourceCredentialsMessage);

            var endpoints = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property
                in document.RootElement.EnumerateObject())
            {
                if (endpoints.Count >= WorkspaceSharePacketCodec.MaxPackageSources)
                {
                    throw new ArgumentException(
                        "Workspace credential bindings contain too many source entries.");
                }
                if (!endpoints.Add(property.Name))
                {
                    throw new ArgumentException(
                        $"Workspace credential binding '{property.Name}' is duplicated.");
                }

                JsonProperty[] fields =
                    property.Value.ValueKind == JsonValueKind.Object
                        ? [.. property.Value.EnumerateObject()]
                        : [];
                if (property.Value.ValueKind != JsonValueKind.Object
                    || fields.Length != 2
                    || fields.Count(
                        child => child.NameEquals("username")) != 1
                    || fields.Count(child => child.NameEquals("pat")) != 1
                    || !property.Value.TryGetProperty(
                        "username",
                        out JsonElement usernameElement)
                    || usernameElement.ValueKind != JsonValueKind.String
                    || !property.Value.TryGetProperty(
                        "pat",
                        out JsonElement patElement)
                    || patElement.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException(
                        $"Workspace credential binding '{property.Name}' must "
                            + "contain only string 'username' and 'pat' properties.");
                }
            }
        }
    }

    internal static IReadOnlyDictionary<string, NuGetFetch.PackageSourceCredential>
        BindPackageSourceCredentials(
        IReadOnlyDictionary<
            string,
            BrowserRetainedWorkspacePackageSourceCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var result = new Dictionary<
            string,
            NuGetFetch.PackageSourceCredential>(StringComparer.Ordinal);
        foreach ((
            string rawEndpoint,
            BrowserRetainedWorkspacePackageSourceCredential credential)
            in credentials)
        {
            if (result.Count >= WorkspaceSharePacketCodec.MaxPackageSources)
            {
                throw new ArgumentException(
                    "Workspace credential bindings contain too many source entries.");
            }
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.Username))
            {
                throw new ArgumentException(
                    $"Workspace credential binding '{rawEndpoint}' has "
                        + "an invalid username.");
            }
            if (string.IsNullOrEmpty(credential.Pat)
                || credential.Pat.Length > 64 * 1024)
            {
                throw new ArgumentException(
                    $"Workspace credential binding '{rawEndpoint}' has "
                        + "an invalid PAT length.");
            }
            string endpoint;
            try
            {
                endpoint = new WorkspacePackageSourceDefinition(
                    rawEndpoint,
                    WorkspacePackageSourceAuthentication
                        .AuthenticationRequired)
                    .Endpoint;
            }
            catch (ArgumentException)
            {
                throw new ArgumentException(
                    $"Workspace credential binding endpoint "
                        + $"'{rawEndpoint}' is invalid.");
            }
            if (!result.TryAdd(
                    endpoint,
                    new NuGetFetch.PackageSourceCredential(
                        credential.Username,
                        credential.Pat)))
            {
                throw new ArgumentException(
                    $"Workspace credential binding '{endpoint}' is duplicated.");
            }
        }
        return result;
    }

    static BrowserRetainedWorkspaceActivationResult Activation(
        DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult activation) =>
        activation switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Activated activated =>
                new("activated", Posting(activated.Posting), null),
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .NoEffect noEffect =>
                new("noEffect", Posting(noEffect.Posting), null),
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Superseded =>
                new("superseded", null, null),
            DotnetInspect.Web.BrowserRetainedWorkspaceActivationResult
                    .Failed failed =>
                new(
                    "failed",
                    null,
                    new(
                        failed.Failure.GetType().Name,
                        failed.Failure.Message)),
            _ => throw new InvalidOperationException(
                "Retained Workspace activation returned an unsupported result."),
        };

    internal static async Task<BrowserRetainedWorkspaceDeactivationResult>
        DeactivateAsync(
        string retainedDefinitionId)
    {
        DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult outcome =
            await Owner.BeginDeactivationAsync(retainedDefinitionId)
                .ConfigureAwait(false);
        BrowserRetainedWorkspaceDeactivationResult result = outcome switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .Deactivated deactivated =>
                new(
                    "deactivated",
                    deactivated.CompletionReceipt,
                    Settlement(deactivated.Settlement),
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .CleanupFailed failed =>
                new(
                    "cleanupFailed",
                    failed.CompletionReceipt,
                    Settlement(failed.Settlement),
                    failed.NavigationFailure
                        ?? "The active Workspace could not be settled."),
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .NoEffect =>
                new("noEffect", null, null, null),
            DotnetInspect.Web.BrowserRetainedWorkspaceDeactivationResult
                    .Rejected rejected =>
                new("rejected", null, null, rejected.Message),
            _ => throw new InvalidOperationException(
                "Retained Workspace deactivation returned an unsupported result."),
        };
        return result;
    }

    internal static BrowserRetainedWorkspaceConsumerCompletionResult
        CompleteDeactivation(
            string receipt,
            bool succeeded,
            string? failure) =>
        Completion(
            Owner.CompleteConsumerDeactivation(
                receipt,
                succeeded,
                failure));

    internal static string RecordConsumerPosting(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        AuthorityResult(
            Owner.RecordConsumerPosting(
                realizationId,
                publicationOrdinal,
                authority));

    internal static bool ValidateNavigationAuthority(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        Owner.ValidateNavigationAuthority(
            realizationId,
            publicationOrdinal,
            authority);

    internal static string Acknowledge(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        AuthorityResult(
            Owner.Acknowledge(
                realizationId,
                publicationOrdinal,
                authority));

    internal static string Abandon(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        AuthorityResult(
            Owner.Abandon(
                realizationId,
                publicationOrdinal,
                authority));

    internal static async Task<BrowserRetainedWorkspaceSettlementResult>
        ObserveSettlementAsync(
        string settlementId)
    {
        DotnetInspect.Web.BrowserRetainedWorkspaceSettlementResult outcome =
            await Owner.ObserveSettlementAsync(settlementId)
                .ConfigureAwait(false);
        BrowserRetainedWorkspaceSettlementResult result = outcome switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceSettlementResult
                    .Settled settled =>
                new("settled", Settlement(settled.Settlement)),
            DotnetInspect.Web.BrowserRetainedWorkspaceSettlementResult.Unknown =>
                new("unknown", null),
            _ => throw new InvalidOperationException(
                "Retained Workspace settlement returned an unsupported result."),
        };
        return result;
    }

    internal static async Task ResetForTestsAsync()
    {
        lock (Gate)
            Sessions.Clear();
        await BrowserRetainedWorkspaceActivationRegistry
            .ResetForTestsAsync()
            .ConfigureAwait(false);
    }

    static BrowserRetainedWorkspaceActivationSession? FindSession(
        string receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt))
            return null;
        lock (Gate)
            return Sessions.GetValueOrDefault(receipt);
    }

    static BrowserRetainedWorkspaceConsumerCompletionResult Completion(
        DotnetInspect.Web.BrowserRetainedWorkspaceConsumerCompletionResult
            completion) =>
        completion switch
        {
            DotnetInspect.Web.BrowserRetainedWorkspaceConsumerCompletionResult
                    .Completed completed =>
                new(
                    "completed",
                    completed.Succeeded,
                    completed.Failure,
                    null),
            DotnetInspect.Web.BrowserRetainedWorkspaceConsumerCompletionResult
                    .Unavailable unavailable =>
                new("unavailable", null, null, unavailable.Message),
            _ => throw new InvalidOperationException(
                "Retained Workspace consumer completion returned an unsupported result."),
        };

    static BrowserRetainedWorkspacePreparedPosting PreparedPosting(
        BrowserRetainedWorkspacePostingDraft posting)
    {
        string canonicalPacket = CanonicalPacket(posting.Projection);
        return new(
            posting.RetainedDefinitionId,
            posting.Label,
            posting.CanonicalLocation,
            canonicalPacket,
            Definition(
                canonicalPacket,
                posting.Definition),
            BrowserCatalogWireProjection.Project(
                posting.Navigation.Result.Consumer),
            [
                .. posting.Packages.Select(
                    static package =>
                        new BrowserRetainedWorkspacePackageInventory(
                            package.NavigationId,
                            package.ContextIndex,
                            package.ConsumerPackageSubjectId,
                            Summary(package.Surface))),
            ],
            [
                .. posting.Platforms.Select(
                    static platform =>
                        new BrowserRetainedWorkspacePlatformInventory(
                            platform.NavigationId,
                            platform.ContextIndex,
                            platform.Family,
                            platform.RuntimeIdentifier,
                            Summary(platform.Surface))),
            ]);
    }

    static BrowserRetainedWorkspacePosting Posting(
        DotnetInspect.Web.BrowserRetainedWorkspacePosting posting) =>
        new(
            posting.RetainedDefinitionId,
            posting.Label,
            posting.CanonicalLocation,
            posting.CanonicalPacket
                ?? throw new InvalidOperationException(
                    "The packet activation export cannot project a non-packet retained definition."),
            posting.RealizationId,
            posting.PublicationOrdinal,
            Definition(
                posting.CanonicalPacket
                    ?? throw new InvalidOperationException(
                        "The packet activation export requires a canonical packet."),
                posting.Definition),
            BrowserCatalogWireProjection.Project(posting.Navigation),
            [
                .. posting.Packages.Select(
                    static package => new BrowserRetainedWorkspacePackageInventory(
                        package.NavigationId,
                        package.ContextIndex,
                        package.ConsumerPackageSubjectId,
                        Summary(package.Surface))),
            ],
            [
                .. posting.Platforms.Select(
                    static platform => new BrowserRetainedWorkspacePlatformInventory(
                        platform.NavigationId,
                        platform.ContextIndex,
                        platform.Family,
                        platform.RuntimeIdentifier,
                        Summary(platform.Surface))),
            ],
            posting.Predecessor is null
                ? null
                : new(
                    posting.Predecessor.SettlementId,
                    posting.Predecessor.Retirement.Reason.ToString()),
            posting.Cleanup is null
                ? null
                : new(posting.Cleanup.Message));

    static BrowserRetainedWorkspacePackage Package(
        BrowserRetainedWorkspacePackagePresentation package,
        int typeOffset) =>
        new(
            package.NavigationId,
            package.ContextIndex,
            package.ConsumerPackageSubjectId,
            PageSurface(package.Surface, typeOffset),
            TypePage(package.Surface, typeOffset));

    static BrowserRetainedWorkspacePlatform Platform(
        BrowserRetainedWorkspacePlatformPresentation platform,
        int typeOffset) =>
        new(
            platform.NavigationId,
            platform.ContextIndex,
            platform.Family,
            platform.RuntimeIdentifier,
            PageSurface(platform.Surface, typeOffset),
            TypePage(platform.Surface, typeOffset));

    static bool ValidOffset(int offset, BrowserPackageSurfaceInfo surface) =>
        offset >= 0 && offset <= surface.Types.Length;

    static BrowserRetainedWorkspaceTypePage TypePage(
        BrowserPackageSurfaceInfo surface, int offset)
    {
        int end = offset + Math.Min(100, surface.Types.Length - offset);
        return new(offset, surface.Types.Length, end < surface.Types.Length ? end : null);
    }

    static BrowserPackageSurface PageSurface(
        BrowserPackageSurfaceInfo surface, int offset)
    {
        int count = Math.Min(100, surface.Types.Length - offset);
        return BrowserCatalogWireProjection.Project(surface with
        {
            Types = surface.Types[offset..(offset + count)],
        });
    }

    static BrowserRetainedWorkspaceSurfaceSummary Summary(
        BrowserPackageSurfaceInfo surface) =>
        new(
            surface.CompileLibrary.TargetFramework,
            surface.Assemblies.Length,
            surface.Types.Length,
            surface.TotalMembers,
            surface.Documents.Count,
            surface.InspectionErrors.Length > 0 || surface.InspectionError is not null);

    static string CanonicalPacket(
        CompleteRestorationProjection projection) =>
        projection is CompleteRestorationProjection.Projectable projectable
            ? projectable.CanonicalPacket
            : throw new InvalidOperationException(
                "The packet activation export cannot project a non-packet retained definition.");

    static BrowserRetainedWorkspaceDefinitionState Definition(
        string canonicalPacket,
        CommittedScenarioDefinitionSet definition)
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            canonicalPacket);
        CommittedNavigationDefinition navigation =
            definition.Navigation
            ?? throw new InvalidOperationException(
                "A restored Workspace requires its Navigation definition.");
        WorkspaceDefinition workspace =
            definition.Workspace
            ?? throw new InvalidOperationException(
                "A restored Workspace requires its Workspace definition.");
        return new(
            [
                .. packet.Tabs.Select((tab, index) => new BrowserWorkspaceShareTab(
                    navigation.Tabs[index].Id,
                    tab.SourceKind == WorkspaceShareSourceKind.Package
                        ? "package" : "group",
                    tab.Source,
                    tab.Version,
                    tab.Framework,
                    tab.RuntimeIdentifier)),
            ],
            [
                .. packet.Contexts.Select((context, index) =>
                    new BrowserWorkspaceShareContext(
                        workspace.Contexts[index].Name,
                        [
                            .. context.TabIndexes.Select(
                                tabIndex => navigation.Tabs[tabIndex].Id),
                        ])),
            ],
            [.. workspace.Registrations.Select(Registration)],
            navigation.Focus,
            definition.Scenario.Context);
    }

    static BrowserRetainedWorkspaceRegistration Registration(
        WorkspaceRegistration registration) =>
        registration switch
        {
            WorkspaceRegistration.ExactLibrary exact =>
                new("exactLibrary", ExactLibrary(exact.Coordinate), null, null),
            WorkspaceRegistration.PackagePrefix prefix =>
                new("packagePrefix", null, prefix.Prefix.Prefix, null),
            WorkspaceRegistration.Ecosystem ecosystem =>
                new("ecosystem", null, null, new(
                    ecosystem.Declaration.Id.Value,
                    [.. ecosystem.Declaration.NamespaceRoots],
                    [.. ecosystem.Declaration.CorePackages.Select(
                        static package => package.PackageId)],
                    [.. ecosystem.Declaration.Populations.Select(EcosystemPopulation)])),
            _ => throw new InvalidOperationException(
                "The completed packet contains an unsupported Workspace registration."),
        };

    static BrowserRetainedWorkspaceEcosystemPopulation EcosystemPopulation(
        WorkspaceEcosystemPopulationDeclaration population) =>
        population switch
        {
            WorkspaceEcosystemPopulationDeclaration.ExactLibrary exact =>
                new("exactLibrary", ExactLibrary(exact.Coordinate), null, null),
            WorkspaceEcosystemPopulationDeclaration.Platform platform =>
                new("platform", null, platform.Population.Family.ToString(), null),
            WorkspaceEcosystemPopulationDeclaration.PackagePrefix prefix =>
                new("packagePrefix", null, null, prefix.Prefix.Prefix),
            _ => throw new InvalidOperationException(
                "The completed packet contains an unsupported Ecosystem population."),
        };

    static BrowserRetainedWorkspaceExactLibrary ExactLibrary(
        ExactLibrarySourceCoordinate coordinate)
    {
        ILInspector.Metadata.AssemblyReferenceIdentity identity =
            coordinate.LibraryIdentity.Identity;
        var library = new BrowserRetainedWorkspaceLibraryIdentity(
            identity.Name,
            identity.Version?.ToString(4)
                ?? throw new InvalidOperationException(
                    "A completed exact Library registration requires an assembly version."),
            identity.Culture,
            identity.PublicKeyToken);
        return coordinate switch
        {
            ExactLibrarySourceCoordinate.Package package =>
                new("package", library,
                    package.PackageCoordinate.PackageId,
                    package.PackageCoordinate.Version,
                    null),
            ExactLibrarySourceCoordinate.Platform platform =>
                new("platform", library, null, null,
                    platform.Population.Family.ToString()),
            _ => throw new InvalidOperationException(
                "The completed packet contains an unsupported exact Library source."),
        };
    }

    static BrowserRetainedWorkspaceSettlement Settlement(
        WorkspaceRealizationSettlement settlement) =>
        new(
            settlement.Succeeded,
            settlement.Reason.ToString(),
            settlement.Failure?.Message);

    static string AuthorityResult(NavigationAuthorityResult result) =>
        result switch
        {
            NavigationAuthorityResult.Accepted => "accepted",
            NavigationAuthorityResult.InvalidAuthority => "invalidAuthority",
            NavigationAuthorityResult.PostingRequired =>
                "postingRequired",
            _ => throw new InvalidOperationException(
                "Navigation authority settlement returned an unknown result."),
        };
}
