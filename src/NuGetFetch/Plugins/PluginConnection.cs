using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NuGetFetch.Plugins;

/// <summary>
/// A live connection to a NuGet credential plugin: the child process, the newline-delimited
/// JSON conversation carried over its stdin/stdout, and the request/response correlation.
/// </summary>
/// <remarks>
/// Modelled on NuGet/NuGet.Client, src/NuGet.Core/NuGet.Protocol/Plugins/ — chiefly
/// PluginFactory (process launch), Sender/StandardOutputReceiver (framing) and
/// MessageDispatcher (correlation). Only the credential-provider slice of the protocol is
/// implemented; package-download claims are ignored.
/// </remarks>
internal sealed class PluginConnection : IAsyncDisposable
{
    private const string ProtocolVersion = "2.0.0";
    private const string MinimumProtocolVersion = "1.0.0";

    // NuGet's defaults, from Plugins/ProtocolConstants.cs and Plugins/PluginConstants.cs.
    // Note the handshake timeout is 30s in code even though the published docs say 5s.
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitWaitAfterClose = TimeSpan.FromSeconds(1);

    private readonly Process _process;
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TimeSpan _requestTimeout;
    private readonly Action<string>? _log;
    private Task? _readLoop;
    private bool _disposed;

    private PluginConnection(Process process, TimeSpan requestTimeout, Action<string>? log)
    {
        _process = process;
        _requestTimeout = requestTimeout;
        _log = log;
    }

    /// <summary>
    /// Launches a plugin and drives it through the full initialization sequence, returning null
    /// if it does not claim the Authentication operation or fails any step.
    /// </summary>
    /// <remarks>
    /// A null return is an ordinary outcome, not an error: a plugin that only claims
    /// DownloadPackage is simply not our concern, and a plugin that cannot start should not
    /// take the whole request down with it.
    /// </remarks>
    public static async Task<PluginConnection?> StartAsync(
        PluginExecutable plugin,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        TimeSpan requestTimeout = ReadTimeoutOverride("NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS", DefaultRequestTimeout);
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            // NuGet deliberately leaves stderr alone; the protocol never uses it, and
            // redirecting an unread pipe risks blocking a chatty plugin.
            RedirectStandardError = false,
            StandardOutputEncoding = utf8NoBom,
            StandardInputEncoding = utf8NoBom,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (plugin.RequiresDotnetHost)
        {
            // Honour DOTNET_HOST_PATH so the plugin runs under the same host as this process,
            // which is what NuGet does and what CI images rely on.
            startInfo.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            startInfo.ArgumentList.Add(plugin.Path);
        }
        else
        {
            startInfo.FileName = plugin.Path;
        }

        startInfo.ArgumentList.Add("-Plugin");

        Process? process;

        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Credential plugin failed to start ({plugin.Path}): {ex.Message}");
            return null;
        }

        if (process is null)
        {
            return null;
        }

        var connection = new PluginConnection(process, requestTimeout, log);
        connection._readLoop = Task.Run(connection.ReadLoopAsync, CancellationToken.None);

        try
        {
            if (await connection.HandshakeAndInitializeAsync(requestTimeout, cancellationToken).ConfigureAwait(false))
            {
                return connection;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Credential plugin initialization failed ({plugin.Path}): {ex.Message}");
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private async Task<bool> HandshakeAndInitializeAsync(TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        // The handshake is symmetric: the plugin sends its own request at the same time we send
        // ours. The read loop is already running, so its request is answered while we await the
        // reply to ours; neither side waits on the other to go first.
        HandshakeResponse? handshake = await SendAsync(
            MessageMethods.Handshake,
            new HandshakeRequest(ProtocolVersion, MinimumProtocolVersion),
            PluginJsonContext.Default.EnvelopeHandshakeRequest,
            PluginJsonContext.Default.HandshakeResponse,
            cancellationToken).ConfigureAwait(false);

        if (handshake?.ResponseCode != ResponseCodes.Success)
        {
            _log?.Invoke("Credential plugin declined the protocol handshake.");
            return false;
        }

        // Best-effort: the plugin exits with us rather than lingering if we are killed.
        _ = await SendAsync(
            MessageMethods.MonitorNuGetProcessExit,
            new MonitorNuGetProcessExitRequest(Environment.ProcessId),
            PluginJsonContext.Default.EnvelopeMonitorNuGetProcessExitRequest,
            PluginJsonContext.Default.MonitorNuGetProcessExitResponse,
            cancellationToken).ConfigureAwait(false);

        string culture = System.Globalization.CultureInfo.CurrentCulture.Name;
        InitializeResponse? initialize = await SendAsync(
            MessageMethods.Initialize,
            new InitializeRequest(
                ClientVersion: NuGetClientVersion,
                Culture: string.IsNullOrEmpty(culture) ? "en" : culture,
                RequestTimeout: requestTimeout.ToString("c")),
            PluginJsonContext.Default.EnvelopeInitializeRequest,
            PluginJsonContext.Default.InitializeResponse,
            cancellationToken).ConfigureAwait(false);

        if (initialize?.ResponseCode != ResponseCodes.Success)
        {
            _log?.Invoke("Credential plugin initialization was rejected.");
            return false;
        }

        // Null source and service index make this a source-agnostic question, which the
        // protocol only allows for 2.0.0 plugins. Both fields drop out of the JSON.
        GetOperationClaimsResponse? claims = await SendAsync(
            MessageMethods.GetOperationClaims,
            new GetOperationClaimsRequest(null, null),
            PluginJsonContext.Default.EnvelopeGetOperationClaimsRequest,
            PluginJsonContext.Default.GetOperationClaimsResponse,
            cancellationToken).ConfigureAwait(false);

        if (claims?.Claims is null || !claims.Claims.Contains("Authentication", StringComparer.Ordinal))
        {
            return false;
        }

        // Until this is answered the plugin suppresses its own Log requests entirely.
        _ = await SendAsync(
            MessageMethods.SetLogLevel,
            new SetLogLevelRequest("Information"),
            PluginJsonContext.Default.EnvelopeSetLogLevelRequest,
            PluginJsonContext.Default.SetLogLevelResponse,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Asks the plugin for credentials for <paramref name="uri"/>.
    /// </summary>
    public Task<GetAuthenticationCredentialsResponse?> GetCredentialsAsync(
        Uri uri,
        bool isRetry,
        bool isNonInteractive,
        bool canShowDialog,
        CancellationToken cancellationToken) =>
        SendAsync(
            MessageMethods.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsRequest(uri.ToString(), isRetry, isNonInteractive, canShowDialog),
            PluginJsonContext.Default.EnvelopeGetAuthenticationCredentialsRequest,
            PluginJsonContext.Default.GetAuthenticationCredentialsResponse,
            cancellationToken);

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(
        string method,
        TRequest payload,
        JsonTypeInfo<Envelope<TRequest>> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        string requestId = Guid.NewGuid().ToString();
        var pending = new PendingRequest(_requestTimeout);
        _pending[requestId] = pending;

        try
        {
            await WriteAsync(
                new Envelope<TRequest>(requestId, MessageTypes.Request, method, payload),
                requestType,
                cancellationToken).ConfigureAwait(false);

            JsonElement? responsePayload = await pending.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return responsePayload?.Deserialize(responseType);
        }
        catch (Exception ex) when (ex is TimeoutException or JsonException or IOException)
        {
            _log?.Invoke($"Credential plugin request '{method}' failed: {ex.Message}");
            return null;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
            pending.Dispose();
        }
    }

    private async Task WriteAsync<T>(Envelope<T> envelope, JsonTypeInfo<Envelope<T>> type, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(envelope, type);

        // Writes are serialized so two concurrent requests cannot interleave halves of a line.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                string? line = await _process.StandardOutput
                    .ReadLineAsync(_shutdown.Token)
                    .ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                if (line.Length > 0)
                {
                    HandleLine(line);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Normal shutdown races.
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Credential plugin read loop ended: {ex.Message}");
        }
        finally
        {
            // The pipe is gone, so nothing outstanding can ever be answered.
            foreach (PendingRequest pending in _pending.Values)
            {
                pending.Completion.TrySetException(new IOException("Credential plugin closed the connection."));
            }
        }
    }

    /// <summary>
    /// Reads a protocol header field, requiring it to actually be a string.
    /// </summary>
    /// <remarks>
    /// The value comes from another process, so its JSON type cannot be assumed. Calling
    /// <see cref="JsonElement.GetString"/> on a number or object throws
    /// <see cref="InvalidOperationException"/>, which is not a <see cref="JsonException"/> and so
    /// would escape the caller's parse guard, end the read loop, and fail every pending request
    /// for the life of the process. Checking the kind keeps a malformed message as cheap as any
    /// other non-protocol noise: it is ignored.
    /// </remarks>
    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        if (root.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private void HandleLine(string line)
    {
        string requestId;
        string type;
        string method;
        JsonElement? payload;

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !TryReadString(root, "RequestId", out requestId)
                || !TryReadString(root, "Type", out type)
                || !TryReadString(root, "Method", out method))
            {
                return;
            }

            // Clone: the element must outlive the JsonDocument it was parsed from.
            payload = root.TryGetProperty("Payload", out JsonElement payloadElement)
                ? payloadElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            // A plugin writing non-protocol noise to stdout should not kill the conversation.
            return;
        }

        switch (type)
        {
            case MessageTypes.Response:
                if (_pending.TryGetValue(requestId, out PendingRequest? completed))
                {
                    completed.Completion.TrySetResult(payload);
                }

                break;

            case MessageTypes.Progress:
                // The plugin is still working. NuGet restarts the clock rather than counting
                // total elapsed time, so a slow interactive sign-in does not trip the timeout.
                if (_pending.TryGetValue(requestId, out PendingRequest? inFlight))
                {
                    inFlight.Extend();
                }

                break;

            case MessageTypes.Fault:
                if (_pending.TryGetValue(requestId, out PendingRequest? faulted))
                {
                    faulted.Completion.TrySetException(
                        new IOException($"Credential plugin faulted on '{method}'."));
                }

                break;

            case MessageTypes.Request:
                _ = Task.Run(() => HandleInboundRequestAsync(requestId, method, payload));
                break;
        }
    }

    private async Task HandleInboundRequestAsync(string requestId, string method, JsonElement? payload)
    {
        try
        {
            switch (method)
            {
                case MessageMethods.Handshake:
                    // Answering this is mandatory; the plugin will not proceed without it.
                    await WriteAsync(
                        new Envelope<HandshakeResponse>(
                            requestId,
                            MessageTypes.Response,
                            MessageMethods.Handshake,
                            NegotiateHandshake(payload)),
                        PluginJsonContext.Default.EnvelopeHandshakeResponse,
                        _shutdown.Token).ConfigureAwait(false);
                    break;

                case MessageMethods.Log:
                    LogRequest? log = payload?.Deserialize(PluginJsonContext.Default.LogRequest);

                    if (log is not null && _log is not null)
                    {
                        _log($"Credential plugin [{log.LogLevel}]: {log.Message}");
                    }

                    await WriteAsync(
                        new Envelope<LogResponse>(
                            requestId,
                            MessageTypes.Response,
                            MessageMethods.Log,
                            new LogResponse(ResponseCodes.Success)),
                        PluginJsonContext.Default.EnvelopeLogResponse,
                        _shutdown.Token).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Connection is going away.
        }
    }

    private static HandshakeResponse NegotiateHandshake(JsonElement? payload)
    {
        HandshakeRequest? request = payload?.Deserialize(PluginJsonContext.Default.HandshakeRequest);

        // We speak exactly one version, so compatibility reduces to the plugin's floor not
        // exceeding it. NuGet's rule is the general form of the same test.
        if (request is null || !string.Equals(request.MinimumProtocolVersion, MinimumProtocolVersion, StringComparison.Ordinal))
        {
            return string.Equals(request?.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal)
                ? new HandshakeResponse(ResponseCodes.Success, ProtocolVersion)
                : new HandshakeResponse(ResponseCodes.Error, null);
        }

        return new HandshakeResponse(ResponseCodes.Success, ProtocolVersion);
    }

    private static TimeSpan ReadTimeoutOverride(string variable, TimeSpan fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;

    private static string NuGetClientVersion => "6.12.1";

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Close carries no payload and expects no response; NuGet sends it, waits briefly,
            // then kills the process regardless.
            using var closeTimeout = new CancellationTokenSource(CloseTimeout);
            await WriteAsync(
                new Envelope<object>(Guid.NewGuid().ToString(), MessageTypes.Request, MessageMethods.Close, null),
                PluginJsonContext.Default.EnvelopeObject,
                closeTimeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // A plugin that has already gone is fine.
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);

        try
        {
            if (!_process.HasExited && !_process.WaitForExit((int)ExitWaitAfterClose.TotalMilliseconds))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already exited.
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch
            {
                // Best effort.
            }
        }

        _process.Dispose();
        _writeLock.Dispose();
        _shutdown.Dispose();
    }

    private sealed class PendingRequest : IDisposable
    {
        private readonly TimeSpan _timeout;
        private readonly Timer _timer;

        public PendingRequest(TimeSpan timeout)
        {
            _timeout = timeout;
            Completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _timer = new Timer(
                static state => ((PendingRequest)state!).Completion.TrySetException(
                    new TimeoutException("Credential plugin did not respond in time.")),
                this,
                timeout,
                Timeout.InfiniteTimeSpan);
        }

        public TaskCompletionSource<JsonElement?> Completion { get; }

        public void Extend()
        {
            try
            {
                _timer.Change(_timeout, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Request already finished.
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
