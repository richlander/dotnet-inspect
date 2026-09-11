using System.Collections.Concurrent;
using System.Text.Json;
using NuGetFetch;
using NuGetFetch.PluginFixture;
using NuGetFetch.Plugins;

namespace NuGetFetch.Tests;

/// <summary>
/// Exercises the plugin wire protocol against a fake plugin that really speaks it.
/// </summary>
/// <remarks>
/// <para>
/// The fake is a managed child process rather than a mock object, so these tests cover the parts
/// most likely to be wrong in a hand-written protocol client: process launch and argument shape,
/// newline-delimited JSON framing, the symmetric handshake, and the exact property names and enum
/// spellings. A mock would have agreed with whatever we wrote.
/// </para>
/// <para>
/// The contract is defined by NuGet/NuGet.Client, src/NuGet.Core/NuGet.Protocol/Plugins/.
/// </para>
/// </remarks>
[Collection(ThreadPoolDeadlineCollection.Name)]
public sealed class PluginProtocolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("plugin-protocol").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task FullExchange_YieldsCredentials()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "VssSessionToken", password: "a-token");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Equal("VssSessionToken", credential.Username);
        Assert.Equal("a-token", credential.Password);
    }

    [Fact]
    public async Task InitializationFollowsTheProtocolSequence()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        await provider.GetCredentialsAsync(new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        string[] methods = [.. plugin.ReceivedRequests().Select(r => r.Method)];

        // The plugin will not answer credential requests until it has been initialized, and it
        // suppresses its own logging until SetLogLevel is answered.
        Assert.Equal(
            ["Handshake", "MonitorNuGetProcessExit", "Initialize", "GetOperationClaims", "SetLogLevel", "GetAuthenticationCredentials"],
            methods);
    }

    [Fact]
    public async Task OperationClaimsAreRequestedWithoutASource()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        await provider.GetCredentialsAsync(new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        JsonElement payload = plugin.ReceivedRequests().Single(r => r.Method == "GetOperationClaims").Payload;

        // Null source and service index are what make the question source-agnostic. Because null
        // properties are omitted, this must serialize as an empty object rather than explicit
        // nulls; the protocol only permits it for 2.0.0 plugins.
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.Empty(payload.EnumerateObject());
    }

    [Fact]
    public async Task CredentialRequestCarriesTheSourceAndInteractivityFlags()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p");
        var uri = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        await provider.GetCredentialsAsync(uri, isRetry: false, TestContext.Current.CancellationToken);

        JsonElement payload = plugin.ReceivedRequests().Single(r => r.Method == "GetAuthenticationCredentials").Payload;

        Assert.Equal(
            uri.OriginalString,
            payload.GetProperty("Uri").GetString());
        Assert.False(payload.GetProperty("IsRetry").GetBoolean());

        // Unattended by default, matching "dotnet restore" without --interactive: a tool that
        // may run in CI must not block on a sign-in prompt.
        Assert.True(payload.GetProperty("IsNonInteractive").GetBoolean());
        Assert.False(payload.GetProperty("CanShowDialog").GetBoolean());
    }

    [Theory]
    [InlineData(
        "https://feed.example/%7E/private/index.json?tenant=%61",
        "https://feed.example/~/private/index.json?tenant=a")]
    [InlineData(
        "https://feed.example/other/../v3/index.json",
        "https://feed.example/v3/index.json")]
    public async Task CredentialRequestPreservesOriginalSourceSpelling(
        string first,
        string second)
    {
        FakePlugin plugin = CreatePlugin(
            "raw-provider-query",
            username: "u",
            password: "p");

        await using var provider = new PluginCredentialProvider(
            null,
            [plugin.Executable]);
        await provider.GetCredentialsAsync(
            new Uri(first),
            isRetry: false,
            TestContext.Current.CancellationToken);
        await provider.GetCredentialsAsync(
            new Uri(second),
            isRetry: false,
            TestContext.Current.CancellationToken);

        string[] queries =
        [
            .. plugin.ReceivedRequests()
                .Where(request =>
                    request.Method == "GetAuthenticationCredentials")
                .Select(request =>
                    request.Payload.GetProperty("Uri").GetString()
                        ?? throw new InvalidOperationException(
                            "The plugin credential request omitted its URI.")),
        ];
        Assert.Equal([first, second], queries);
    }

    [Fact]
    public async Task IsRetryReachesThePlugin()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        await provider.GetCredentialsAsync(new Uri("https://feed.example/v3/index.json"), isRetry: true, TestContext.Current.CancellationToken);

        JsonElement payload = plugin.ReceivedRequests().Single(r => r.Method == "GetAuthenticationCredentials").Payload;

        // Without this the Azure Artifacts provider may keep serving a cached token that the
        // feed has already rejected.
        Assert.True(payload.GetProperty("IsRetry").GetBoolean());
    }

    [Fact]
    public async Task InteractivePluginsMayBeAllowedToPrompt()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]) { AllowInteractive = true };
        await provider.GetCredentialsAsync(new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        JsonElement payload = plugin.ReceivedRequests().Single(r => r.Method == "GetAuthenticationCredentials").Payload;

        Assert.False(payload.GetProperty("IsNonInteractive").GetBoolean());
        Assert.True(payload.GetProperty("CanShowDialog").GetBoolean());
    }

    [Fact]
    public async Task PluginWithoutTheAuthenticationClaim_IsNotAsked()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p", claims: "DownloadPackage");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        Assert.Null(credential);

        // Initialization stops at the claims check; a download-only plugin is never asked for
        // credentials it never offered to supply.
        Assert.DoesNotContain("GetAuthenticationCredentials", plugin.ReceivedRequests().Select(r => r.Method));
    }

    [Fact]
    public async Task NotFoundMeansThisPluginDoesNotServeTheSource()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p", responseCode: "NotFound");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        // An Azure provider asked about an unrelated feed answers NotFound. That is a normal
        // "ask someone else", not a failure.
        Assert.Null(credential);
    }

    [Fact]
    public async Task WhenOnePluginDeclines_TheNextIsTried()
    {
        FakePlugin declines = CreatePlugin("declines", username: "u", password: "p", responseCode: "NotFound");
        FakePlugin answers = CreatePlugin("answers", username: "right", password: "token");

        await using var provider = new PluginCredentialProvider(null, [declines.Executable, answers.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Equal("right", credential.Username);
    }

    [Fact]
    public async Task WhenOnePluginDiesDuringTheRequest_TheNextIsTried()
    {
        FakePlugin dies = CreatePlugin(
            "dies",
            username: "unused",
            password: "unused",
            exitOnCredentialRequest: true);
        FakePlugin answers = CreatePlugin("answers-after-death", username: "right", password: "token");

        await using var provider = new PluginCredentialProvider(null, [dies.Executable, answers.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Equal("right", credential.Username);
    }

    [Fact]
    public async Task AClosedCachedPluginConnectionIsRestartedOnTheNextRequest()
    {
        var credentialLineWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionClosed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLineWriter = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            RequestLineWritten = method =>
            {
                if (hooksEnabled.IsSet
                    && method == MessageMethods.GetAuthenticationCredentials)
                {
                    credentialLineWritten.TrySetResult();
                    releaseLineWriter.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(10),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
            ConnectionAdmissionClosed = _ => connectionClosed.TrySetResult(),
        };
        FakePlugin plugin = CreatePlugin(
            "restarts-after-death",
            username: "right",
            password: "token",
            exitOnFirstCredentialRequest: true);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable], hooks);

        hooksEnabled.Set();
        Task<PackageSourceCredential?> firstRequest = Task.Run(
            () => provider.GetCredentialsAsync(
                new Uri("https://first.example/v3/index.json"),
                false,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        await credentialLineWritten.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await connectionClosed.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        releaseLineWriter.TrySetResult();

        PackageSourceCredential? first = await firstRequest;
        PackageSourceCredential? second = await provider.GetCredentialsAsync(
            new Uri("https://second.example/v3/index.json"),
            false,
            TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Equal("right", second.Username);
    }

    [Fact]
    public async Task ARequestRacingTerminalPublicationRetriesOnAReplacementConnection()
    {
        var admissionClosed = new TaskCompletionSource<(bool ClosedPublished, bool GateHeld)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationStarting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAdmissionAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClosure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var trackSecondAdmission = new ManualResetEventSlim();
        int blockNextClosure = 1;
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            TerminalPublicationStarting = () =>
            {
                if (Interlocked.Exchange(ref blockNextClosure, 0) == 1)
                {
                    publicationStarting.TrySetResult();
                    releaseClosure.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(2),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
            ConnectionAdmissionClosed = observation =>
            {
                admissionClosed.TrySetResult(observation);
            },
            RequestAdmissionAttempted = () =>
            {
                if (trackSecondAdmission.IsSet)
                {
                    secondAdmissionAttempted.TrySetResult();
                }
            },
        };
        FakePlugin plugin = CreatePlugin(
            "retry-terminal-publication",
            username: "right",
            password: "token",
            exitOnFirstCredentialRequest: true);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable], hooks);
        Task<PackageSourceCredential?> first = Task.Run(
            () => provider.GetCredentialsAsync(
                new Uri("https://first.example/v3/index.json"),
                false,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        await publicationStarting.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        trackSecondAdmission.Set();
        Task<PackageSourceCredential?> second = Task.Run(
            () => provider.GetCredentialsAsync(
                new Uri("https://second.example/v3/index.json"),
                false,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        try
        {
            await secondAdmissionAttempted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseClosure.TrySetResult();
        }

        (bool closedPublished, bool gateHeld) = await admissionClosed.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.True(closedPublished);
        Assert.True(gateHeld);

        Assert.Null(await first);
        PackageSourceCredential? credential = await second.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.NotNull(credential);
        Assert.Equal("right", credential.Username);
    }

    [Fact]
    public async Task CancellationWhileReplacingAClosedConnectionRemainsCancellation()
    {
        var quiescenceAwaiting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuiescence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        int blockNextQuiescence = 1;
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            ConnectionQuiescenceAwaiting = () =>
            {
                if (hooksEnabled.IsSet
                    && Interlocked.Exchange(ref blockNextQuiescence, 0) == 1)
                {
                    quiescenceAwaiting.TrySetResult();
                    releaseQuiescence.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(2),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
        };
        FakePlugin plugin = CreatePlugin(
            "cancel-closed-replacement",
            username: "right",
            password: "token",
            exitOnFirstCredentialRequest: true);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable], hooks);
        Assert.Null(await provider.GetCredentialsAsync(
            new Uri("https://first.example/v3/index.json"),
            false,
            TestContext.Current.CancellationToken));

        hooksEnabled.Set();
        using var cancellation = new CancellationTokenSource();
        Task<PackageSourceCredential?> replacement = Task.Run(
            () => provider.GetCredentialsAsync(
                new Uri("https://second.example/v3/index.json"),
                false,
                cancellation.Token),
            TestContext.Current.CancellationToken);

        await quiescenceAwaiting.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();
        releaseQuiescence.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await replacement.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CredentialsRestrictedToOtherAuthSchemesAreNotUsed()
    {
        FakePlugin plugin = CreatePlugin("negotiate", username: "u", password: "p", authenticationTypes: "negotiate");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        // We can only send Basic. Using a credential the plugin scoped to another scheme would
        // put it on the wire in a form the plugin did not sanction.
        Assert.Null(credential);
    }

    [Fact]
    public async Task ASecondRequestReusesTheRunningPlugin()
    {
        FakePlugin plugin = CreatePlugin("basic", username: "u", password: "p");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        await provider.GetCredentialsAsync(new Uri("https://one.example/v3/index.json"), false, TestContext.Current.CancellationToken);
        await provider.GetCredentialsAsync(new Uri("https://two.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        var requests = plugin.ReceivedRequests();

        // Initialization happens once; only the credential request repeats. Re-launching would
        // pay a process start and a five-message handshake per source.
        Assert.Single(requests, r => r.Method == "Initialize");
        Assert.Equal(2, requests.Count(r => r.Method == "GetAuthenticationCredentials"));
    }

    [Fact]
    public async Task APluginThatCannotStartIsSkipped()
    {
        var missing = new PluginExecutable(Path.Combine(_root, "does-not-exist"), RequiresDotnetHost: false);

        await using var provider = new PluginCredentialProvider(null, [missing]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        // A broken plugin install must not take down every package operation on the machine.
        Assert.Null(credential);
    }

    [Fact]
    public async Task APluginThatWritesGarbageIsSkipped()
    {
        FakePlugin plugin = CreatePlugin(
            "garbage",
            username: "unused",
            password: "unused",
            writeGarbageAndWait: true);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        Assert.Null(credential);
    }

    [Fact]
    public async Task AProtocolMessageWithNonStringHeadersIsIgnoredRatherThanEndingTheConversation()
    {
        // Well-formed JSON carrying the right property names with the wrong types is a distinct
        // case from the unparseable noise above: it survives JsonDocument.Parse, so the guard for
        // that path never sees it. Reading such a field as a string throws
        // InvalidOperationException, which is not a JsonException, so an unguarded read escapes
        // the read loop, and the loop's finally faults every pending request. That would take the
        // plugin down for the life of the process on one malformed line.
        FakePlugin plugin = CreatePlugin(
            "mistyped",
            username: "u",
            password: "p",
            preambleMessage:
                """{"RequestId":123,"Type":"Response","Method":"GetAuthenticationCredentials"}""");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Equal("u", credential.Username);
        Assert.Equal("p", credential.Password);
    }

    [Theory]
    [InlineData(
        "mistyped-version",
        """{"ProtocolVersion":123,"MinimumProtocolVersion":"1.0.0"}""")]
    [InlineData(
        "missing-minimum",
        """{"ProtocolVersion":"2.0.0"}""")]
    [InlineData(
        "invalid-version",
        """{"ProtocolVersion":"current","MinimumProtocolVersion":"1.0.0"}""")]
    [InlineData(
        "reversed-range",
        """{"ProtocolVersion":"1.0.0","MinimumProtocolVersion":"2.0.0"}""")]
    [InlineData(
        "unsupported-version",
        """{"ProtocolVersion":"1.0.0","MinimumProtocolVersion":"1.0.0"}""")]
    public async Task InvalidOrUnsupportedInboundHandshakeReceivesAnErrorResponse(
        string name,
        string payload)
    {
        FakePlugin plugin = CreatePlugin(
            $"malformed-inbound-handshake-{name}",
            username: "u",
            password: "p",
            inboundHandshakePayload: payload);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken);

        Assert.NotNull(credential);

        ReceivedMessage response = plugin.WaitForMessage("fake-handshake", MessageTypes.Response);
        Assert.Equal(MessageMethods.Handshake, response.Method);
        Assert.Equal(
            ResponseCodes.Error,
            response.Payload.GetProperty("ResponseCode").GetString());
    }

    [Fact]
    public async Task CompatibleInboundHandshakeUsesProtocolTwo()
    {
        FakePlugin plugin = CreatePlugin(
            "compatible-inbound-handshake",
            username: "u",
            password: "p",
            inboundHandshakePayload:
                """{"ProtocolVersion":"2.0.0","MinimumProtocolVersion":"2.0.0"}""");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken);

        Assert.NotNull(credential);

        ReceivedMessage response = plugin.WaitForMessage("fake-handshake", MessageTypes.Response);
        Assert.Equal(MessageMethods.Handshake, response.Method);
        Assert.Equal(
            ResponseCodes.Success,
            response.Payload.GetProperty("ResponseCode").GetString());
        Assert.Equal(
            "2.0.0",
            response.Payload.GetProperty("ProtocolVersion").GetString());
    }

    [Theory]
    [InlineData(
        "unsupported-version",
        """{"ResponseCode":"Success","ProtocolVersion":"1.0.0"}""")]
    [InlineData(
        "missing-version",
        """{"ResponseCode":"Success"}""")]
    [InlineData(
        "invalid-version",
        """{"ResponseCode":"Success","ProtocolVersion":"current"}""")]
    public async Task InvalidOrUnsupportedOutboundHandshakeStopsInitialization(
        string name,
        string payload)
    {
        FakePlugin plugin = CreatePlugin(
            $"invalid-outbound-handshake-{name}",
            username: "u",
            password: "p",
            outboundHandshakePayload: payload);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken);

        Assert.Null(credential);
        Assert.Contains(
            plugin.ReceivedRequests(),
            request => request.Method == MessageMethods.Handshake);
        Assert.DoesNotContain(
            plugin.ReceivedRequests(),
            request => request.Method is not MessageMethods.Handshake and not MessageMethods.Close);
    }

    [Theory]
    [InlineData(
        "mistyped-level",
        """{"LogLevel":123,"Message":"ignored"}""")]
    [InlineData(
        "missing-message",
        """{"LogLevel":"Information"}""")]
    [InlineData(
        "empty-message",
        """{"LogLevel":"Information","Message":""}""")]
    [InlineData(
        "invalid-level",
        """{"LogLevel":"Bogus","Message":"ignored"}""")]
    public async Task MalformedInboundLogReceivesAnErrorResponse(
        string name,
        string payload)
    {
        FakePlugin plugin = CreatePlugin(
            $"malformed-inbound-log-{name}",
            username: "u",
            password: "p",
            inboundLogRequestId: "malformed-log",
            inboundLogPayload: payload,
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.EmitLog);

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken);

        Assert.NotNull(credential);

        ReceivedMessage response = plugin.WaitForMessage("malformed-log", MessageTypes.Response);
        Assert.Equal(MessageMethods.Log, response.Method);
        Assert.Equal(
            ResponseCodes.Error,
            response.Payload.GetProperty("ResponseCode").GetString());
    }

    [Fact]
    public async Task ValidInboundLogIsSurfacedAndAcknowledged()
    {
        var log = new List<string>();
        FakePlugin plugin = CreatePlugin(
            "valid-inbound-log",
            username: "u",
            password: "p",
            inboundLogRequestId: "valid-log",
            inboundLogPayload:
                """{"LogLevel":"Information","Message":"hello"}""",
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.EmitLog);

        await using var provider = new PluginCredentialProvider(log.Add, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Contains(
            "Credential plugin [Information]: hello",
            log);

        ReceivedMessage response = plugin.WaitForMessage("valid-log", MessageTypes.Response);
        Assert.Equal(MessageMethods.Log, response.Method);
        Assert.Equal(
            ResponseCodes.Success,
            response.Payload.GetProperty("ResponseCode").GetString());
    }

    [Fact]
    public async Task AConnectionDisposedBeforeARequestDegradesToNoCredentials()
    {
        FakePlugin plugin = CreatePlugin(
            "disposed-connection",
            username: "u",
            password: "p");
        PluginConnection? connection = await PluginConnection.StartAsync(
            plugin.Executable,
            log: null,
            TestContext.Current.CancellationToken);
        Assert.NotNull(connection);

        await connection.DisposeAsync();

        GetAuthenticationCredentialsResponse? response =
            await connection.GetCredentialsAsync(
                new Uri("https://feed.example/v3/index.json"),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false,
                TestContext.Current.CancellationToken);

        Assert.Null(response);
    }

    [Fact]
    public async Task ARequestAfterReceiverLossIsRejectedWithoutWaitingForItsTimeout()
    {
        FakePlugin plugin = CreatePlugin(
            "closed-admission",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.CloseOutput,
            exitOnCredentialRequest: true);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken));
        GetAuthenticationCredentialsResponse? response;

        await using (connection)
        {
            await connection.Closed.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            response = await connection
                .GetCredentialsAsync(
                    new Uri("https://feed.example/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    TestContext.Current.CancellationToken)
                .WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

            Assert.Null(response);
        }

        Assert.DoesNotContain(
            plugin.ReceivedRequests(),
            request => request.Method == MessageMethods.GetAuthenticationCredentials);
    }

    [Fact]
    public async Task ReceiverLossSettlesARequestAdmittedBeforeThePendingSnapshot()
    {
        FakePlugin plugin = CreatePlugin(
            "closed-with-pending-request",
            username: "u",
            password: "p",
            closeOutputOnCredentialRequest: true);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken));
        await using (connection)
        {
            GetAuthenticationCredentialsResponse? response = await connection
                .GetCredentialsAsync(
                    new Uri("https://feed.example/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    TestContext.Current.CancellationToken)
                .WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

            Assert.Null(response);
            Assert.Contains(
                plugin.ReceivedRequests(),
                request => request.Method == MessageMethods.GetAuthenticationCredentials);
        }
    }

    [Fact]
    public async Task AdmissionCannotRegisterDuringTheTerminalPendingSnapshot()
    {
        var admissionAccepted = new TaskCompletionSource<long>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAdmission = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requestRegistered = new TaskCompletionSource<(long GateEntry, bool GateHeld)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var settlementAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotCaptured = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            RequestAdmissionAccepted = gateEntry =>
            {
                if (hooksEnabled.IsSet)
                {
                    admissionAccepted.TrySetResult(gateEntry);
                    releaseAdmission.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(5),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
            RequestRegistered = observation =>
            {
                if (hooksEnabled.IsSet)
                {
                    requestRegistered.TrySetResult(observation);
                }
            },
            TerminalSettlementAttempted = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    settlementAttempted.TrySetResult();
                }
            },
            PendingSnapshotCaptured = gateHeld =>
            {
                if (hooksEnabled.IsSet)
                {
                    snapshotCaptured.TrySetResult(gateHeld);
                }
            },
        };
        FakePlugin plugin = CreatePlugin(
            "admission-during-terminal-snapshot",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior:
                PluginAfterSetLogLevelBehavior.WaitForCloseMarkerThenCloseOutput,
            exitOnCredentialRequest: true);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken,
                hooks));
        await using (connection)
        {
            hooksEnabled.Set();

            try
            {
                Task<GetAuthenticationCredentialsResponse?> request = Task.Run(
                    () => connection.GetCredentialsAsync(
                        new Uri("https://feed.example/v3/index.json"),
                        isRetry: false,
                        isNonInteractive: true,
                        canShowDialog: false,
                        TestContext.Current.CancellationToken),
                    TestContext.Current.CancellationToken);

                long acceptedGateEntry = await admissionAccepted.Task.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

                File.WriteAllText(plugin.RecordPath + ".close", string.Empty);

                await settlementAttempted.Task.WaitAsync(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken);

                Assert.False(snapshotCaptured.Task.IsCompleted);

                releaseAdmission.TrySetResult();

                (long registeredGateEntry, bool registrationHeldGate) =
                    await requestRegistered.Task.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);
                bool snapshotHeldGate = await snapshotCaptured.Task.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

                GetAuthenticationCredentialsResponse? response = await request.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

                Assert.Null(response);
                Assert.Equal(acceptedGateEntry, registeredGateEntry);
                Assert.True(registrationHeldGate);
                Assert.True(snapshotHeldGate);
            }
            finally
            {
                releaseAdmission.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task CanceledRequestAfterReceiverLossRemainsCancellation()
    {
        FakePlugin plugin = CreatePlugin(
            "canceled-after-receiver-loss",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.CloseOutput);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken));
        await using (connection)
        {
            await connection.Closed.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                connection.GetCredentialsAsync(
                    new Uri("https://feed.example/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    cancellation.Token));
        }

        Assert.DoesNotContain(
            plugin.ReceivedRequests(),
            recorded => recorded.Method == MessageMethods.GetAuthenticationCredentials);
    }

    [Fact]
    public async Task CancellationWhileWaitingForClosedAdmissionRemainsCancellation()
    {
        var admissionAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotCaptured = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSnapshot = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            RequestAdmissionAttempted = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    admissionAttempted.TrySetResult();
                }
            },
            PendingSnapshotCaptured = _ =>
            {
                if (hooksEnabled.IsSet)
                {
                    snapshotCaptured.TrySetResult();
                    releaseSnapshot.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(5),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
        };
        FakePlugin plugin = CreatePlugin(
            "canceled-while-waiting-for-closed-admission",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior:
                PluginAfterSetLogLevelBehavior.WaitForCloseMarkerThenCloseOutput,
            exitOnCredentialRequest: true);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken,
                hooks));
        await using (connection)
        {
            hooksEnabled.Set();

            try
            {
                File.WriteAllText(plugin.RecordPath + ".close", string.Empty);
                await snapshotCaptured.Task.WaitAsync(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken);

                using var cancellation = new CancellationTokenSource();
                Task<GetAuthenticationCredentialsResponse?> request = Task.Run(
                    () => connection.GetCredentialsAsync(
                        new Uri("https://feed.example/v3/index.json"),
                        isRetry: false,
                        isNonInteractive: true,
                        canShowDialog: false,
                        cancellation.Token),
                    TestContext.Current.CancellationToken);

                await admissionAttempted.Task.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);
                cancellation.Cancel();
                releaseSnapshot.TrySetResult();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await request.WaitAsync(
                        TimeSpan.FromSeconds(1),
                        TestContext.Current.CancellationToken));
            }
            finally
            {
                releaseSnapshot.TrySetResult();
            }
        }

        Assert.DoesNotContain(
            plugin.ReceivedRequests(),
            recorded => recorded.Method == MessageMethods.GetAuthenticationCredentials);
    }

    [Fact]
    public async Task AStalledWriterTimeoutTerminatesTheConnectionAndSettlesQueuedRequests()
    {
        using var timeout = new EnvironmentVariable(
            "NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS",
            "2");
        var log = new ConcurrentQueue<string>();
        FakePlugin plugin = CreatePlugin(
            "stalled-writer-timeout",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.Stall);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log.Enqueue,
                TestContext.Current.CancellationToken));
        await using (connection)
        {
            Task<GetAuthenticationCredentialsResponse?> active = connection.GetCredentialsAsync(
                CreateLargeFeedUri(),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false,
                TestContext.Current.CancellationToken);

            await Task.Delay(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

            Task<GetAuthenticationCredentialsResponse?> queued = connection.GetCredentialsAsync(
                new Uri("https://queued.example/v3/index.json"),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false,
                TestContext.Current.CancellationToken);

            GetAuthenticationCredentialsResponse?[] responses = await Task
                .WhenAll(active, queued)
                .WaitAsync(
                    TimeSpan.FromSeconds(6),
                    TestContext.Current.CancellationToken);

            Assert.All(responses, Assert.Null);
            Assert.Contains(log, message => message.Contains(
                "did not respond in time",
                StringComparison.Ordinal));
            Assert.Contains(log, message => message.Contains(
                "closed the connection",
                StringComparison.Ordinal));

            GetAuthenticationCredentialsResponse? later = await connection
                .GetCredentialsAsync(
                    new Uri("https://later.example/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    TestContext.Current.CancellationToken)
                .WaitAsync(
                    TimeSpan.FromSeconds(1),
                    TestContext.Current.CancellationToken);

            Assert.Null(later);
        }
    }

    [Fact]
    public async Task CallerCancellationOfAStalledWriterRemainsCancellation()
    {
        using var timeout = new EnvironmentVariable(
            "NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS",
            "10");
        var log = new ConcurrentQueue<string>();
        FakePlugin plugin = CreatePlugin(
            "stalled-writer-cancellation",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.Stall);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log.Enqueue,
                TestContext.Current.CancellationToken));
        await using (connection)
        {
            using var cancellation = new CancellationTokenSource();
            Task<GetAuthenticationCredentialsResponse?> active = connection.GetCredentialsAsync(
                CreateLargeFeedUri(),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false,
                cancellation.Token);

            await Task.Delay(100, TestContext.Current.CancellationToken);

            Task<GetAuthenticationCredentialsResponse?> queued = connection.GetCredentialsAsync(
                new Uri("https://queued.example/v3/index.json"),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false,
                TestContext.Current.CancellationToken);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await active.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

            GetAuthenticationCredentialsResponse? peer = await queued.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.Null(peer);
            Assert.DoesNotContain(log, message => message.Contains(
                "did not respond in time",
                StringComparison.Ordinal));
            Assert.Contains(log, message => message.Contains(
                "closed the connection",
                StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AResponseCannotLeaveItsRequestWriterStalled()
    {
        using var timeout = new EnvironmentVariable(
            "NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS",
            "10");
        FakePlugin plugin = CreatePlugin(
            "response-before-complete-write",
            username: "right",
            password: "token",
            afterSetLogLevelBehavior:
                PluginAfterSetLogLevelBehavior.RespondBeforeCredentialLineCompletes);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken));
        await using (connection)
        {
            GetAuthenticationCredentialsResponse? response = await connection
                .GetCredentialsAsync(
                    CreateLargeFeedUri(),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    TestContext.Current.CancellationToken)
                .WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);

            Assert.NotNull(response);
            Assert.Equal("right", response.Username);
            await connection.Closed.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task CallerCancellationWinsAConcurrentWriteFailure()
    {
        using var timeout = new EnvironmentVariable(
            "NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS",
            "10");
        var writeFailed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeStarting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            RequestWriteStarting = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    writeStarting.TrySetResult();
                    releaseWrite.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(2),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    throw new IOException("Injected write failure.");
                }
            },
            RequestWriteFailed = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    writeFailed.TrySetResult();
                    releaseFailure.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(2),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
        };
        FakePlugin plugin = CreatePlugin(
            "write-failure-cancellation",
            username: "u",
            password: "p");
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken,
                hooks));
        await using (connection)
        {
            using var cancellation = new CancellationTokenSource();
            hooksEnabled.Set();
            Task<GetAuthenticationCredentialsResponse?> request = Task.Run(
                () => connection.GetCredentialsAsync(
                    CreateLargeFeedUri(),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    cancellation.Token),
                TestContext.Current.CancellationToken);

            await writeStarting.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            releaseWrite.TrySetResult();

            await writeFailed.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            cancellation.Cancel();
            releaseFailure.TrySetResult();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await request.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ConnectionResourcesWaitForInterruptedRequestsToQuiesce()
    {
        using var timeout = new EnvironmentVariable(
            "NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS",
            "10");
        var writeInterrupted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInterruption = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var quiescenceAwaiting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resourcesDisposing = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            RequestWriteOverride = () => hooksEnabled.IsSet ? writeCompletes.Task : null,
            RequestWriteStarted = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    writeStarted.TrySetResult();
                }
            },
            RequestWriteInterrupted = () =>
            {
                writeInterrupted.TrySetResult();
                releaseInterruption.Task
                    .WaitAsync(
                        TimeSpan.FromSeconds(2),
                        TestContext.Current.CancellationToken)
                    .GetAwaiter()
                    .GetResult();
            },
            ConnectionQuiescenceAwaiting = () => quiescenceAwaiting.TrySetResult(),
            ConnectionResourcesDisposing = value => resourcesDisposing.TrySetResult(value),
        };
        FakePlugin plugin = CreatePlugin(
            "quiesce-before-dispose",
            username: "u",
            password: "p",
            afterSetLogLevelBehavior: PluginAfterSetLogLevelBehavior.Stall);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken,
                hooks));
        using var cancellation = new CancellationTokenSource();
        hooksEnabled.Set();
        Task<GetAuthenticationCredentialsResponse?> request = connection.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            isNonInteractive: true,
            canShowDialog: false,
            cancellation.Token);

        await writeStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await writeInterrupted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Task disposal = connection.DisposeAsync().AsTask();

        try
        {
            await quiescenceAwaiting.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.False(resourcesDisposing.Task.IsCompleted);
        }
        finally
        {
            writeCompletes.TrySetResult();
            releaseInterruption.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        await disposal.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.True(await resourcesDisposing.Task);
    }

    [Fact]
    public async Task ConnectionResourcesWaitForInboundResponseWritersToQuiesce()
    {
        var responseWriteStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponseWrite = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var quiescenceAwaiting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resourcesDisposing = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            ResponseWriteStarted = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    responseWriteStarted.TrySetResult();
                    releaseResponseWrite.Task
                        .WaitAsync(
                            TimeSpan.FromSeconds(10),
                            TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
            },
            ConnectionQuiescenceAwaiting = () => quiescenceAwaiting.TrySetResult(),
            ConnectionResourcesDisposing = value => resourcesDisposing.TrySetResult(value),
        };
        FakePlugin plugin = CreatePlugin(
            "quiesce-inbound-response",
            username: "u",
            password: "p",
            inboundLogRequestId: "quiesce-log",
            inboundLogPayload:
                """{"LogLevel":"Information","Message":"hello"}""",
            afterSetLogLevelBehavior:
                PluginAfterSetLogLevelBehavior.WaitForLogMarkerThenEmitLogAndStall);
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken,
                hooks));

        hooksEnabled.Set();
        File.WriteAllText(plugin.RecordPath + ".log", string.Empty);
        await responseWriteStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Task disposal = connection.DisposeAsync().AsTask();

        try
        {
            await quiescenceAwaiting.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.False(resourcesDisposing.Task.IsCompleted);
        }
        finally
        {
            releaseResponseWrite.TrySetResult();
        }

        await disposal.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.True(await resourcesDisposing.Task);
    }

    [Fact]
    public async Task AnUnfinishedInterruptedWriteRetainsConnectionResources()
    {
        using var timeout = new EnvironmentVariable(
            "NUGET_PLUGIN_REQUEST_TIMEOUT_IN_SECONDS",
            "10");
        var neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resourcesDisposing = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hooksEnabled = new ManualResetEventSlim();
        var hooks = new PluginConnection.PluginConnectionTestHooks
        {
            RequestWriteOverride = () => hooksEnabled.IsSet ? neverCompletes.Task : null,
            RequestWriteStarted = () =>
            {
                if (hooksEnabled.IsSet)
                {
                    writeStarted.TrySetResult();
                }
            },
            ConnectionResourcesDisposing = value => resourcesDisposing.TrySetResult(value),
        };
        FakePlugin plugin = CreatePlugin(
            "retain-unfinished-write-resources",
            username: "u",
            password: "p");
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken,
                hooks));
        using var cancellation = new CancellationTokenSource();
        hooksEnabled.Set();
        Task<GetAuthenticationCredentialsResponse?> request = connection.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"),
            isRetry: false,
            isNonInteractive: true,
            canShowDialog: false,
            cancellation.Token);

        await writeStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        await connection.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.False(await resourcesDisposing.Task);
    }

    [Fact]
    public async Task CallerCancellationContinuesToPropagate()
    {
        FakePlugin plugin = CreatePlugin("cancelled-request", username: "u", password: "p");
        PluginConnection connection = Assert.IsType<PluginConnection>(
            await PluginConnection.StartAsync(
                plugin.Executable,
                log: null,
                TestContext.Current.CancellationToken));
        await using (connection)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                connection.GetCredentialsAsync(
                    new Uri("https://feed.example/v3/index.json"),
                    isRetry: false,
                    isNonInteractive: true,
                    canShowDialog: false,
                    cancellation.Token));
        }
    }

    [Fact]
    public void RecoverableRequestFailuresExcludeCallerCancellationAndUnrelatedFaults()
    {
        Assert.True(PluginConnection.IsRecoverableRequestFailure(
            new TimeoutException()));
        Assert.True(PluginConnection.IsRecoverableRequestFailure(
            new JsonException()));
        Assert.True(PluginConnection.IsRecoverableRequestFailure(
            new IOException()));
        Assert.True(PluginConnection.IsRecoverableRequestFailure(
            new ObjectDisposedException("plugin pipe")));
        Assert.True(PluginConnection.IsRecoverableRequestFailure(
            new InvalidOperationException()));

        Assert.False(PluginConnection.IsRecoverableRequestFailure(
            new OperationCanceledException()));
        Assert.False(PluginConnection.IsRecoverableRequestFailure(
            new ArgumentException()));
    }

    private static Uri CreateLargeFeedUri() =>
        new("https://feed.example/" + new string('a', 4 * 1024 * 1024));

    private FakePlugin CreatePlugin(
        string name,
        string username,
        string password,
        string claims = "Authentication",
        string responseCode = "Success",
        string? authenticationTypes = null,
        string? preambleMessage = null,
        string? inboundHandshakePayload = null,
        string? outboundHandshakePayload = null,
        string? inboundLogRequestId = null,
        string? inboundLogPayload = null,
        bool writeGarbageAndWait = false,
        PluginAfterSetLogLevelBehavior afterSetLogLevelBehavior =
            PluginAfterSetLogLevelBehavior.Continue,
        bool closeOutputOnCredentialRequest = false,
        bool exitOnFirstCredentialRequest = false,
        bool exitOnCredentialRequest = false)
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        string record = Path.Combine(directory, "received.ndjson");
        var configuration = new PluginFixtureConfiguration
        {
            RecordPath = record,
            Username = username,
            Password = password,
            Claims = claims,
            ResponseCode = responseCode,
            AuthenticationType = authenticationTypes,
            PreambleMessage = preambleMessage,
            InboundHandshakePayload = inboundHandshakePayload
                ?? """{"ProtocolVersion":"2.0.0","MinimumProtocolVersion":"1.0.0"}""",
            OutboundHandshakePayload = outboundHandshakePayload
                ?? """{"ResponseCode":"Success","ProtocolVersion":"2.0.0"}""",
            InboundLogRequestId = inboundLogRequestId,
            InboundLogPayload = inboundLogPayload,
            WriteGarbageAndWait = writeGarbageAndWait,
            CredentialBehavior = closeOutputOnCredentialRequest
                ? PluginCredentialBehavior.CloseOutput
                : exitOnFirstCredentialRequest
                    ? PluginCredentialBehavior.ExitOnFirstRequest
                    : exitOnCredentialRequest
                        ? PluginCredentialBehavior.Exit
                        : PluginCredentialBehavior.Respond,
            AfterSetLogLevelBehavior = afterSetLogLevelBehavior,
        };

        string sourceBase = Path.Combine(
            FindPluginFixtureDirectory(),
            "NuGetFetch.PluginFixture");
        string targetBase = Path.Combine(directory, $"nuget-plugin-{name}");
        foreach (string suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
        {
            string source = sourceBase + suffix;
            Assert.True(File.Exists(source), $"Expected plugin fixture file '{source}'.");
            File.Copy(source, targetBase + suffix);
        }

        File.WriteAllText(
            targetBase + ".json",
            JsonSerializer.Serialize(configuration));

        return new FakePlugin(
            new PluginExecutable(targetBase + ".dll", RequiresDotnetHost: true),
            record);
    }

    private static string FindPluginFixtureDirectory()
    {
        string testOutput = Path.TrimEndingDirectorySeparator(
            AppContext.BaseDirectory);
        string configuration = Path.GetFileName(testOutput);
        string projectOutput = Directory.GetParent(testOutput)!.FullName;
        string binDirectory = Directory.GetParent(projectOutput)!.FullName;
        return Path.Combine(
            binDirectory,
            "NuGetFetch.PluginFixture",
            configuration);
    }

    private sealed record FakePlugin(PluginExecutable Executable, string RecordPath)
    {
        /// <summary>Requests the host sent, in order, as parsed from what the plugin actually received.</summary>
        public IReadOnlyList<(string Method, JsonElement Payload)> ReceivedRequests()
        {
            List<(string, JsonElement)> requests = [];

            foreach (string line in ReadAllLinesWithRetry())
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;

                if (root.GetProperty("Type").GetString() != "Request")
                {
                    continue;
                }

                JsonElement payload = root.TryGetProperty("Payload", out JsonElement value)
                    ? value.Clone()
                    : default;

                requests.Add((root.GetProperty("Method").GetString()!, payload));
            }

            return requests;
        }

        public ReceivedMessage WaitForMessage(string requestId, string type)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                foreach (string line in ReadAllLinesWithRetry())
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;

                    if (root.GetProperty("RequestId").GetString() == requestId
                        && root.GetProperty("Type").GetString() == type)
                    {
                        JsonElement payload = root.TryGetProperty("Payload", out JsonElement value)
                            ? value.Clone()
                            : default;

                        return new ReceivedMessage(
                            requestId,
                            type,
                            root.GetProperty("Method").GetString()!,
                            payload);
                    }
                }

                Thread.Sleep(25);
            }

            throw new Xunit.Sdk.XunitException(
                $"The plugin did not receive {type} message '{requestId}'.");
        }

        private string[] ReadAllLinesWithRetry()
        {
            // The plugin appends as it goes and we read while it may still be running.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(RecordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }

    private sealed record ReceivedMessage(
        string RequestId,
        string Type,
        string Method,
        JsonElement Payload);

    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariable(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
