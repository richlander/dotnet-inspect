using System.Text.Json.Serialization;

namespace NuGetFetch.Plugins;

// Wire contract for the NuGet cross-platform plugin protocol (version 2.0.0).
//
// The authoritative definition is NuGet/NuGet.Client,
// src/NuGet.Core/NuGet.Protocol/Plugins/. Shapes below mirror the types in
// .../Plugins/Messages/ and the envelope written by .../Plugins/MessageConverter.cs.
//
// Three properties of that contract drive the design here:
//   - Every message is one line of compact JSON terminated by a newline (NDJSON),
//     UTF-8 with no BOM. There is no length prefix. See .../Plugins/Sender.cs.
//   - Property names are PascalCase and matched case-sensitively, and enums travel as
//     their member-name strings, never as integers. See MessageConverter.Read/Write
//     and PluginJsonContext's UseStringEnumConverter.
//   - Null-valued properties are omitted entirely, so a request whose fields are all
//     null serializes as an empty object rather than a set of explicit nulls.
//
// Versions and timeouts are declared as strings rather than SemanticVersion/TimeSpan
// because the wire format is fixed ("2.0.0", "00:00:30") and modelling them as strings
// keeps serialization independent of how any given runtime chooses to render those types.

/// <summary>
/// The protocol envelope. <typeparamref name="T"/> is the payload type for a specific method.
/// </summary>
/// <remarks>
/// Declaration order matches the property order NuGet writes. Order is not significant to
/// either side — both parse by name — but keeping it aligned makes captured traffic easier
/// to diff against traffic from the real NuGet client.
/// </remarks>
internal sealed record Envelope<T>(string RequestId, string Type, string Method, T? Payload);

internal sealed record HandshakeRequest(string ProtocolVersion, string MinimumProtocolVersion);

/// <summary>Handshake reply. <see cref="ProtocolVersion"/> is the negotiated version, and is absent when <see cref="ResponseCode"/> is not Success.</summary>
internal sealed record HandshakeResponse(string ResponseCode, string? ProtocolVersion);

/// <summary>Tells the plugin which process to watch, so it exits if the host dies rather than lingering.</summary>
internal sealed record MonitorNuGetProcessExitRequest(int ProcessId);

internal sealed record MonitorNuGetProcessExitResponse(string ResponseCode);

/// <summary>
/// Establishes client identity and the default per-request timeout.
/// </summary>
/// <remarks>
/// The timeout sent here is authoritative for the rest of the session: after the response
/// arrives NuGet applies the same value to its own connection options, so both ends agree.
/// </remarks>
internal sealed record InitializeRequest(string ClientVersion, string Culture, string RequestTimeout);

internal sealed record InitializeResponse(string ResponseCode);

/// <summary>
/// Asks what the plugin can do. Both fields are null for an authentication query, which is
/// what makes the question source-agnostic; NuGet only permits null here for 2.0.0 plugins.
/// </summary>
internal sealed record GetOperationClaimsRequest(string? PackageSourceRepository, string? ServiceIndex);

internal sealed record GetOperationClaimsResponse(List<string>? Claims);

internal sealed record SetLogLevelRequest(string LogLevel);

internal sealed record SetLogLevelResponse(string ResponseCode);

/// <summary>
/// The credential request.
/// </summary>
/// <param name="Uri">The package source needing credentials.</param>
/// <param name="IsRetry">
/// False on first ask. True after the returned credentials were rejected with a 401, which
/// instructs the plugin to obtain fresh credentials rather than serve its cache. The Azure
/// Artifacts provider warns that omitting this "MAY" yield invalid credentials.
/// </param>
/// <param name="IsNonInteractive">True when the plugin must not block for user input. Takes precedence over <paramref name="CanShowDialog"/>.</param>
/// <param name="CanShowDialog">Whether the plugin may present interactive UI.</param>
internal sealed record GetAuthenticationCredentialsRequest(
    string Uri,
    bool IsRetry,
    bool IsNonInteractive,
    bool CanShowDialog);

/// <summary>
/// The credential reply. A NotFound response code means the plugin does not serve this URI,
/// which is the normal answer from an Azure provider asked about an unrelated feed.
/// </summary>
/// <param name="AuthenticationTypes">
/// HTTP auth schemes the credentials apply to, e.g. "basic". Null means they apply to any scheme.
/// </param>
internal sealed record GetAuthenticationCredentialsResponse(
    string? Username,
    string? Password,
    string? Message,
    List<string>? AuthenticationTypes,
    string ResponseCode);

/// <summary>A log line the plugin wants surfaced. Suppressed by the plugin until SetLogLevel has been answered.</summary>
internal sealed record LogRequest(string LogLevel, string Message);

internal sealed record LogResponse(string ResponseCode);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Envelope<HandshakeRequest>))]
[JsonSerializable(typeof(Envelope<HandshakeResponse>))]
[JsonSerializable(typeof(Envelope<MonitorNuGetProcessExitRequest>))]
[JsonSerializable(typeof(Envelope<InitializeRequest>))]
[JsonSerializable(typeof(Envelope<GetOperationClaimsRequest>))]
[JsonSerializable(typeof(Envelope<SetLogLevelRequest>))]
[JsonSerializable(typeof(Envelope<GetAuthenticationCredentialsRequest>))]
[JsonSerializable(typeof(Envelope<LogResponse>))]
[JsonSerializable(typeof(Envelope<object>))]
[JsonSerializable(typeof(HandshakeRequest))]
[JsonSerializable(typeof(HandshakeResponse))]
[JsonSerializable(typeof(MonitorNuGetProcessExitResponse))]
[JsonSerializable(typeof(InitializeResponse))]
[JsonSerializable(typeof(GetOperationClaimsResponse))]
[JsonSerializable(typeof(SetLogLevelResponse))]
[JsonSerializable(typeof(GetAuthenticationCredentialsResponse))]
[JsonSerializable(typeof(LogRequest))]
internal sealed partial class PluginJsonContext : JsonSerializerContext;

/// <summary>Message type discriminators, spelled as they appear on the wire.</summary>
internal static class MessageTypes
{
    public const string Request = "Request";
    public const string Response = "Response";
    public const string Progress = "Progress";
    public const string Fault = "Fault";
    public const string Cancel = "Cancel";
}

/// <summary>Method names, spelled as they appear on the wire.</summary>
internal static class MessageMethods
{
    public const string Handshake = "Handshake";
    public const string Initialize = "Initialize";
    public const string MonitorNuGetProcessExit = "MonitorNuGetProcessExit";
    public const string GetOperationClaims = "GetOperationClaims";
    public const string SetLogLevel = "SetLogLevel";
    public const string GetAuthenticationCredentials = "GetAuthenticationCredentials";
    public const string Log = "Log";
    public const string Close = "Close";
}

/// <summary>Response codes, spelled as they appear on the wire.</summary>
internal static class ResponseCodes
{
    public const string Success = "Success";
    public const string Error = "Error";
    public const string NotFound = "NotFound";
}
