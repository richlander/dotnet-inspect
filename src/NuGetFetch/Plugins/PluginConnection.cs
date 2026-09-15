using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private const string MinimumProtocolVersion = ProtocolVersion;
    private static readonly string[] LogLevels =
        ["Debug", "Verbose", "Information", "Minimal", "Warning", "Error"];

    // NuGet's defaults, from Plugins/ProtocolConstants.cs and Plugins/PluginConstants.cs.
    // Note the handshake timeout is 30s in code even though the published docs say 5s.
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitWaitAfterClose = TimeSpan.FromSeconds(1);

    private readonly Process _process;
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly object _pendingGate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _closed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _quiesced =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PluginConnectionTestHooks? _testHooks;
    private readonly TimeSpan _requestTimeout;
    private readonly Action<string>? _log;
    private Task? _readLoop;
    private long _pendingGateEntry;
    private int _activeOperations;
    private bool _hasUnobservedWrite;
    private bool _acceptingRequests = true;
    private bool _terminated;
    private bool _disposed;

    private PluginConnection(
        Process process,
        TimeSpan requestTimeout,
        Action<string>? log,
        PluginConnectionTestHooks? testHooks)
    {
        _process = process;
        _requestTimeout = requestTimeout;
        _log = log;
        _testHooks = testHooks;
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
        CancellationToken cancellationToken,
        PluginConnectionTestHooks? testHooks = null)
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

        process.StandardInput.AutoFlush = true;
        var connection = new PluginConnection(process, requestTimeout, log, testHooks);
        connection._readLoop = Task.Run(connection.ReadLoopAsync, CancellationToken.None);

        try
        {
            if (await connection.HandshakeAndInitializeAsync(requestTimeout, cancellationToken).ConfigureAwait(false))
            {
                return connection;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
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

        if (handshake?.ResponseCode != ResponseCodes.Success
            || !string.Equals(handshake.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal))
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
        GetCredentialsAsync(
            uri,
            isRetry,
            isNonInteractive,
            canShowDialog,
            retryConnectionClosed: false,
            cancellationToken);

    internal Task<GetAuthenticationCredentialsResponse?> GetCredentialsForProviderAsync(
        Uri uri,
        bool isRetry,
        bool isNonInteractive,
        bool canShowDialog,
        CancellationToken cancellationToken) =>
        GetCredentialsAsync(
            uri,
            isRetry,
            isNonInteractive,
            canShowDialog,
            retryConnectionClosed: true,
            cancellationToken);

    private Task<GetAuthenticationCredentialsResponse?> GetCredentialsAsync(
        Uri uri,
        bool isRetry,
        bool isNonInteractive,
        bool canShowDialog,
        bool retryConnectionClosed,
        CancellationToken cancellationToken) =>
        SendAsync(
            MessageMethods.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsRequest(
                uri.OriginalString,
                isRetry,
                isNonInteractive,
                canShowDialog),
            PluginJsonContext.Default.EnvelopeGetAuthenticationCredentialsRequest,
            PluginJsonContext.Default.GetAuthenticationCredentialsResponse,
            cancellationToken,
            retryConnectionClosed);

    internal Task Closed => _closed.Task;

    internal Task Quiesced => _quiesced.Task;

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(
        string method,
        TRequest payload,
        JsonTypeInfo<Envelope<TRequest>> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken,
        bool retryConnectionClosed = false)
        where TResponse : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        string requestId = Guid.NewGuid().ToString();
        var pending = new PendingRequest(_requestTimeout, cancellationToken);
        bool registered = false;

        try
        {
            _testHooks?.RequestAdmissionAttempted?.Invoke();

            using (PendingGateLease gate = EnterPendingGate())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_acceptingRequests)
                {
                    if (retryConnectionClosed)
                    {
                        throw new ConnectionClosedBeforeRequestException();
                    }

                    throw new IOException("Credential plugin closed the connection.");
                }

                _testHooks?.RequestAdmissionAccepted?.Invoke(gate.Entry);
                _pending[requestId] = pending;
                _activeOperations++;
                registered = true;
                _testHooks?.RequestRegistered?.Invoke(
                    (gate.Entry, Monitor.IsEntered(_pendingGate)));
            }

            await WriteRequestAsync(
                new Envelope<TRequest>(requestId, MessageTypes.Request, method, payload),
                requestType,
                pending).ConfigureAwait(false);

            JsonElement? responsePayload = await pending.Completion.ConfigureAwait(false);

            return responsePayload?.Deserialize(responseType);
        }
        catch (ConnectionClosedBeforeRequestException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableRequestFailure(ex))
        {
            if (retryConnectionClosed && pending.ShouldRetryClosedConnection)
            {
                throw new ConnectionClosedBeforeRequestException(ex);
            }

            _log?.Invoke($"Credential plugin request '{method}' failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (registered)
            {
                using (EnterPendingGate())
                {
                    _pending.TryRemove(requestId, out _);
                    CompleteActiveOperationUnderLock();
                }
            }

            pending.Dispose();
        }
    }

    internal static bool IsRecoverableRequestFailure(Exception exception) =>
        exception switch
        {
            TimeoutException or JsonException or IOException => true,
            // ObjectDisposedException is an InvalidOperationException, but keeping it explicit
            // documents the closed-pipe race this boundary is intended to absorb.
            ObjectDisposedException => true,
            InvalidOperationException => true,
            _ => false,
        };

    internal sealed class ConnectionClosedBeforeRequestException : Exception
    {
        public ConnectionClosedBeforeRequestException()
            : base("Credential plugin closed before the request could be sent.")
        {
        }

        public ConnectionClosedBeforeRequestException(Exception innerException)
            : base("Credential plugin closed before the request could be sent.", innerException)
        {
        }
    }

    private async Task WriteRequestAsync<T>(
        Envelope<T> envelope,
        JsonTypeInfo<Envelope<T>> type,
        PendingRequest pending)
    {
        string json = JsonSerializer.Serialize(envelope, type);
        bool lockTaken = false;
        bool releaseWriteLock = true;
        bool completionWon = false;

        try
        {
            await _writeLock.WaitAsync(pending.WriteCancellation).ConfigureAwait(false);
            lockTaken = true;

            using (EnterPendingGate())
            {
                if (!_acceptingRequests)
                {
                    pending.TrySetConnectionClosed();
                    throw new IOException("Credential plugin closed the connection.");
                }
            }

            _testHooks?.RequestWriteStarting?.Invoke();
            Task write = _testHooks?.RequestWriteOverride?.Invoke()
                ?? WriteLineAsync(
                    json,
                    pending.WriteCancellation,
                    () =>
                    {
                        pending.MarkWriteCompleted();
                        _testHooks?.RequestLineWritten?.Invoke(envelope.Method);
                    });
            _testHooks?.RequestWriteStarted?.Invoke();
            Task completed = await Task.WhenAny(write, pending.Completion).ConfigureAwait(false);

            if (!ReferenceEquals(completed, write))
            {
                completionWon = true;
                TerminateConnection();
                _testHooks?.RequestWriteInterrupted?.Invoke();
                releaseWriteLock = await ObserveTerminatedWriteAsync(write).ConfigureAwait(false);

                if (!releaseWriteLock)
                {
                    MarkUnobservedWrite();
                }
            }
            else
            {
                await write.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _testHooks?.RequestWriteFailed?.Invoke();
            pending.TrySetException(ex);

            if (lockTaken)
            {
                TerminateConnection();
            }

            await pending.Completion.ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken && releaseWriteLock)
            {
                _writeLock.Release();
            }
        }

        if (completionWon)
        {
            _ = await pending.Completion.ConfigureAwait(false);
        }
    }

    private async Task WriteAsync<T>(
        Envelope<T> envelope,
        JsonTypeInfo<Envelope<T>> type,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        bool allowClosedConnection = false)
    {
        string json = JsonSerializer.Serialize(envelope, type);
        using CancellationTokenSource? timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using CancellationTokenSource? linkedCancellation = timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        CancellationToken writeCancellation = linkedCancellation?.Token ?? cancellationToken;
        bool lockTaken = false;
        bool releaseWriteLock = true;

        // Writes are serialized so two concurrent requests cannot interleave halves of a line.
        try
        {
            await _writeLock.WaitAsync(writeCancellation).ConfigureAwait(false);
            lockTaken = true;

            using (EnterPendingGate())
            {
                if (_terminated || (!_acceptingRequests && !allowClosedConnection))
                {
                    throw new IOException("Credential plugin closed the connection.");
                }
            }

            Task write = WriteLineAsync(json, writeCancellation);
            _testHooks?.ResponseWriteStarted?.Invoke();
            Task cancellation = Task.Delay(Timeout.InfiniteTimeSpan, writeCancellation);
            Task completed = await Task.WhenAny(write, cancellation).ConfigureAwait(false);

            if (!ReferenceEquals(completed, write))
            {
                TerminateConnection();
                releaseWriteLock = await ObserveTerminatedWriteAsync(write).ConfigureAwait(false);

                if (!releaseWriteLock)
                {
                    MarkUnobservedWrite();
                }

                writeCancellation.ThrowIfCancellationRequested();
            }

            await write.ConfigureAwait(false);
        }
        catch
        {
            if (lockTaken || timeoutCancellation?.IsCancellationRequested is true)
            {
                TerminateConnection();
            }

            throw;
        }
        finally
        {
            if (lockTaken && releaseWriteLock)
            {
                _writeLock.Release();
            }
        }
    }

    private async Task WriteLineAsync(
        string json,
        CancellationToken cancellationToken,
        Action? lineWritten = null)
    {
        await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        lineWritten?.Invoke();
    }

    private static async Task<bool> ObserveTerminatedWriteAsync(Task write)
    {
        try
        {
            await write.WaitAsync(ExitWaitAfterClose).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception ex) when (IsRecoverableRequestFailure(ex)
            || ex is OperationCanceledException)
        {
            return true;
        }
    }

    private void MarkUnobservedWrite()
    {
        using (EnterPendingGate())
        {
            _hasUnobservedWrite = true;
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
            _ = SettleClosedConnection();
        }
    }

    private bool SettleClosedConnection(bool terminated = false)
    {
        PendingRequest[] pending;
        bool canSendClose;

        _testHooks?.TerminalSettlementAttempted?.Invoke();

        using (EnterPendingGate())
        {
            canSendClose = _acceptingRequests && _pending.IsEmpty;
            _terminated |= terminated;
            _testHooks?.TerminalPublicationStarting?.Invoke();
            _closed.TrySetResult();
            _acceptingRequests = false;
            _testHooks?.ConnectionAdmissionClosed?.Invoke(
                (_closed.Task.IsCompleted, Monitor.IsEntered(_pendingGate)));
            pending = [.. _pending.Values];
            _testHooks?.PendingSnapshotCaptured?.Invoke(Monitor.IsEntered(_pendingGate));
            _pending.Clear();

            if (_activeOperations == 0)
            {
                _quiesced.TrySetResult();
            }
        }

        foreach (PendingRequest request in pending)
        {
            request.TrySetConnectionClosed();
        }

        return canSendClose;
    }

    private void TerminateConnection()
    {
        _ = SettleClosedConnection(terminated: true);

        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposal already completed the same terminal transition.
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or NotSupportedException
            or AggregateException
            or System.ComponentModel.Win32Exception)
        {
            // The process exited concurrently or does not support tree termination.
        }
    }

    private PendingGateLease EnterPendingGate() => new(this);

    private readonly struct PendingGateLease : IDisposable
    {
        private readonly PluginConnection _owner;

        public PendingGateLease(PluginConnection owner)
        {
            _owner = owner;
            Monitor.Enter(owner._pendingGate);
            Entry = ++owner._pendingGateEntry;
        }

        public long Entry { get; }

        public void Dispose() => Monitor.Exit(_owner._pendingGate);
    }

    internal sealed class PluginConnectionTestHooks
    {
        public Action? RequestAdmissionAttempted { get; init; }

        public Action<long>? RequestAdmissionAccepted { get; init; }

        public Action<(long GateEntry, bool GateHeld)>? RequestRegistered { get; init; }

        public Action? TerminalSettlementAttempted { get; init; }

        public Action? TerminalPublicationStarting { get; init; }

        public Action<bool>? PendingSnapshotCaptured { get; init; }

        public Action<(bool ClosedPublished, bool GateHeld)>? ConnectionAdmissionClosed { get; init; }

        public Action? RequestWriteStarted { get; init; }

        public Action? RequestWriteStarting { get; init; }

        public Action? RequestWriteInterrupted { get; init; }

        public Action? RequestWriteFailed { get; init; }

        public Action? ResponseWriteStarted { get; init; }

        public Action<string>? RequestLineWritten { get; init; }

        public Func<Task?>? RequestWriteOverride { get; init; }

        public Action<bool>? ConnectionResourcesDisposing { get; init; }

        public Action? ConnectionQuiescenceAwaiting { get; init; }
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
                    completed.TrySetResult(payload);
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
                    faulted.TrySetException(
                        new IOException($"Credential plugin faulted on '{method}'."));
                }

                break;

            case MessageTypes.Request:
                if (TryBeginInboundOperation())
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleInboundRequestAsync(requestId, method, payload).ConfigureAwait(false);
                        }
                        finally
                        {
                            CompleteActiveOperation();
                        }
                    });
                }

                break;
        }
    }

    private bool TryBeginInboundOperation()
    {
        using (EnterPendingGate())
        {
            if (!_acceptingRequests)
            {
                return false;
            }

            _activeOperations++;
            return true;
        }
    }

    private void CompleteActiveOperation()
    {
        using (EnterPendingGate())
        {
            CompleteActiveOperationUnderLock();
        }
    }

    private void CompleteActiveOperationUnderLock()
    {
        _activeOperations--;

        if (!_acceptingRequests && _activeOperations == 0)
        {
            _quiesced.TrySetResult();
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
                        _shutdown.Token,
                        _requestTimeout).ConfigureAwait(false);
                    break;

                case MessageMethods.Log:
                    LogRequest? log = DeserializeInboundPayload(
                        payload,
                        PluginJsonContext.Default.LogRequest,
                        MessageMethods.Log);

                    bool validLog = log is not null
                        && LogLevels.Contains(log.LogLevel, StringComparer.Ordinal)
                        && !string.IsNullOrEmpty(log.Message);

                    if (validLog && _log is not null)
                    {
                        _log($"Credential plugin [{log!.LogLevel}]: {log.Message}");
                    }

                    await WriteAsync(
                        new Envelope<LogResponse>(
                            requestId,
                            MessageTypes.Response,
                            MessageMethods.Log,
                            new LogResponse(
                                !validLog
                                    ? ResponseCodes.Error
                                    : ResponseCodes.Success)),
                        PluginJsonContext.Default.EnvelopeLogResponse,
                        _shutdown.Token,
                        _requestTimeout).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Connection is going away.
        }
    }

    private HandshakeResponse NegotiateHandshake(JsonElement? payload)
    {
        HandshakeRequest? request = DeserializeInboundPayload(
            payload,
            PluginJsonContext.Default.HandshakeRequest,
            MessageMethods.Handshake);

        if (request is null
            || !TryParseProtocolVersion(request.ProtocolVersion, out Version? pluginVersion)
            || !TryParseProtocolVersion(request.MinimumProtocolVersion, out Version? pluginMinimum)
            || pluginMinimum > pluginVersion
            || !TryParseProtocolVersion(ProtocolVersion, out Version? hostVersion)
            || !TryParseProtocolVersion(MinimumProtocolVersion, out Version? hostMinimum))
        {
            return new HandshakeResponse(ResponseCodes.Error, null);
        }

        Version negotiated = pluginVersion < hostVersion ? pluginVersion : hostVersion;

        return negotiated >= pluginMinimum && negotiated >= hostMinimum
            ? new HandshakeResponse(ResponseCodes.Success, negotiated.ToString(3))
            : new HandshakeResponse(ResponseCodes.Error, null);
    }

    private static bool TryParseProtocolVersion(
        string? value,
        [NotNullWhen(true)] out Version? version)
    {
        if (value is not null
            && Version.TryParse(value, out version)
            && version.Build >= 0
            && version.Revision < 0
            && string.Equals(version.ToString(3), value, StringComparison.Ordinal))
        {
            return true;
        }

        version = null;
        return false;
    }

    private T? DeserializeInboundPayload<T>(
        JsonElement? payload,
        JsonTypeInfo<T> type,
        string method)
        where T : class
    {
        try
        {
            return payload?.Deserialize(type);
        }
        catch (JsonException)
        {
            _log?.Invoke($"Credential plugin sent a malformed '{method}' payload.");
            return null;
        }
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
        bool canSendClose = SettleClosedConnection();

        try
        {
            // Close carries no payload and expects no response; NuGet sends it, waits briefly,
            // then kills the process regardless.
            if (canSendClose)
            {
                using var closeTimeout = new CancellationTokenSource(CloseTimeout);
                await WriteAsync(
                    new Envelope<object>(Guid.NewGuid().ToString(), MessageTypes.Request, MessageMethods.Close, null),
                    PluginJsonContext.Default.EnvelopeObject,
                    closeTimeout.Token,
                    allowClosedConnection: true).ConfigureAwait(false);
            }
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
                await _process.WaitForExitAsync().ConfigureAwait(false);
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

        _testHooks?.ConnectionQuiescenceAwaiting?.Invoke();
        await Quiesced.ConfigureAwait(false);
        bool resourcesCanDispose;

        using (EnterPendingGate())
        {
            resourcesCanDispose = _activeOperations == 0 && !_hasUnobservedWrite;
        }

        _testHooks?.ConnectionResourcesDisposing?.Invoke(resourcesCanDispose);

        if (!resourcesCanDispose)
        {
            // A transport write remained blocked even after process termination. Retain every
            // resource it may still touch; the terminal connection is never reused.
            return;
        }

        _process.Dispose();
        _writeLock.Dispose();
        _shutdown.Dispose();
    }

    private sealed class PendingRequest : IDisposable
    {
        private const int Active = 0;
        private const int Completed = 1;
        private const int TimedOut = 2;
        private const int CallerCanceled = 3;
        private const int ConnectionClosed = 4;

        private readonly TimeSpan _timeout;
        private readonly Timer _timer;
        private readonly CancellationToken _callerCancellation;
        private readonly CancellationTokenRegistration _callerRegistration;
        private readonly CancellationTokenSource _writeCancellation = new();
        private readonly TaskCompletionSource<JsonElement?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCompleted;
        private int _state;

        public PendingRequest(TimeSpan timeout, CancellationToken callerCancellation)
        {
            _timeout = timeout;
            _callerCancellation = callerCancellation;
            _timer = new Timer(
                static state => ((PendingRequest)state!).OnTimeout(),
                this,
                timeout,
                Timeout.InfiniteTimeSpan);
            _callerRegistration = callerCancellation.Register(
                static state => ((PendingRequest)state!).CancelByCaller(),
                this);
        }

        public Task<JsonElement?> Completion => _completion.Task;

        public bool ShouldRetryClosedConnection =>
            Volatile.Read(ref _state) == ConnectionClosed
            && Volatile.Read(ref _writeCompleted) == 0;

        public CancellationToken WriteCancellation => _writeCancellation.Token;

        public void MarkWriteCompleted() => Volatile.Write(ref _writeCompleted, 1);

        public void Extend()
        {
            if (Volatile.Read(ref _state) != Active)
            {
                return;
            }

            try
            {
                _timer.Change(_timeout, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Request already finished.
            }
        }

        public void TrySetResult(JsonElement? payload)
        {
            if (TryTransition(Completed))
            {
                SignalWriteCancellation();
                _completion.TrySetResult(payload);
            }
        }

        public void TrySetException(Exception exception)
        {
            if (TryTransition(Completed))
            {
                SignalWriteCancellation();
                _completion.TrySetException(exception);
            }
        }

        public void TrySetConnectionClosed()
        {
            if (TryTransition(ConnectionClosed))
            {
                SignalWriteCancellation();
                _completion.TrySetException(
                    new IOException("Credential plugin closed the connection."));
            }
        }

        private bool TryTransition(int state) =>
            Interlocked.CompareExchange(ref _state, state, Active) == Active;

        private void OnTimeout()
        {
            if (TryTransition(TimedOut))
            {
                SignalWriteCancellation();
                _completion.TrySetException(
                    new TimeoutException("Credential plugin did not respond in time."));
            }
        }

        private void CancelByCaller()
        {
            if (TryTransition(CallerCanceled))
            {
                SignalWriteCancellation();
                _completion.TrySetCanceled(_callerCancellation);
            }
        }

        private void SignalWriteCancellation()
        {
            try
            {
                _writeCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request finished while preemption was being delivered.
            }
        }

        public void Dispose()
        {
            _callerRegistration.Dispose();
            _timer.Dispose();
            _writeCancellation.Dispose();
        }
    }
}
