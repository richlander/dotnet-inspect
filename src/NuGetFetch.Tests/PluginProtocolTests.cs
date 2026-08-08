using System.Text.Json;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace NuGetFetch.Tests;

/// <summary>
/// Exercises the plugin wire protocol against a fake plugin that really speaks it.
/// </summary>
/// <remarks>
/// <para>
/// The fake is a shell script rather than a mock object, so these tests cover the parts most
/// likely to be wrong in a hand-written protocol client: process launch and argument shape,
/// newline-delimited JSON framing, the symmetric handshake, and the exact property names and
/// enum spellings. A mock would have agreed with whatever we wrote.
/// </para>
/// <para>
/// The contract is defined by NuGet/NuGet.Client, src/NuGet.Core/NuGet.Protocol/Plugins/.
/// </para>
/// </remarks>
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

        Assert.Equal(uri.ToString(), payload.GetProperty("Uri").GetString());
        Assert.False(payload.GetProperty("IsRetry").GetBoolean());

        // Unattended by default, matching "dotnet restore" without --interactive: a tool that
        // may run in CI must not block on a sign-in prompt.
        Assert.True(payload.GetProperty("IsNonInteractive").GetBoolean());
        Assert.False(payload.GetProperty("CanShowDialog").GetBoolean());
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
        FakePlugin plugin = CreateRawPlugin("garbage", "printf 'not json at all\\n'\nsleep 2\n");

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
            preamble: """emit '{"RequestId":123,"Type":"Response","Method":"GetAuthenticationCredentials"}'""");

        await using var provider = new PluginCredentialProvider(null, [plugin.Executable]);
        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri("https://feed.example/v3/index.json"), false, TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Equal("u", credential.Username);
        Assert.Equal("p", credential.Password);
    }

    private FakePlugin CreatePlugin(
        string name,
        string username,
        string password,
        string claims = "Authentication",
        string responseCode = "Success",
        string? authenticationTypes = null,
        string? preamble = null)
    {
        // Values are embedded in a double-quoted bash string, so every JSON quote needs a
        // backslash in the emitted script.
        static string Quoted(string value) => "\\\"" + value + "\\\"";

        string types = authenticationTypes is null ? "null" : "[" + Quoted(authenticationTypes) + "]";

        string credentialPayload = responseCode == "Success"
            ? "{" + Quoted("Username") + ":" + Quoted(username)
                + "," + Quoted("Password") + ":" + Quoted(password)
                + "," + Quoted("AuthenticationTypes") + ":" + types
                + "," + Quoted("ResponseCode") + ":" + Quoted("Success") + "}"
            : "{" + Quoted("ResponseCode") + ":" + Quoted(responseCode) + "}";

        // A non-interpolated raw string with tokens: the script is dense with braces, and
        // interpolation holes would be indistinguishable from JSON.
        string body = """
            __PREAMBLE__
            # Open with our own handshake, as a real plugin does. The host must answer it.
            emit '{"RequestId":"fake-handshake","Type":"Request","Method":"Handshake","Payload":{"ProtocolVersion":"2.0.0","MinimumProtocolVersion":"1.0.0"}}'

            while IFS= read -r line; do
              printf '%s\n' "$line" >> "$RECORD"
              type=$(field "$line" Type)
              [ "$type" = "Request" ] || continue
              id=$(field "$line" RequestId)
              method=$(field "$line" Method)
              case "$method" in
                Handshake)
                  emit "{\"RequestId\":\"$id\",\"Type\":\"Response\",\"Method\":\"Handshake\",\"Payload\":{\"ResponseCode\":\"Success\",\"ProtocolVersion\":\"2.0.0\"}}" ;;
                MonitorNuGetProcessExit|Initialize|SetLogLevel)
                  emit "{\"RequestId\":\"$id\",\"Type\":\"Response\",\"Method\":\"$method\",\"Payload\":{\"ResponseCode\":\"Success\"}}" ;;
                GetOperationClaims)
                  emit "{\"RequestId\":\"$id\",\"Type\":\"Response\",\"Method\":\"GetOperationClaims\",\"Payload\":{\"Claims\":[__CLAIMS__]}}" ;;
                GetAuthenticationCredentials)
                  emit "{\"RequestId\":\"$id\",\"Type\":\"Response\",\"Method\":\"GetAuthenticationCredentials\",\"Payload\":__CREDENTIAL__}" ;;
                Close)
                  exit 0 ;;
              esac
            done
            """
            .Replace("__CLAIMS__", Quoted(claims))
            .Replace("__CREDENTIAL__", credentialPayload)
            .Replace("__PREAMBLE__", preamble ?? string.Empty);

        return CreateRawPlugin(name, body);
    }

    private FakePlugin CreateRawPlugin(string name, string body)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The fake plugin is a shell script; CI for this suite is Linux.");

        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        string record = Path.Combine(directory, "received.ndjson");
        string script = Path.Combine(directory, $"nuget-plugin-{name}");

        string contents = $$"""
            #!/usr/bin/env bash
            set -u
            RECORD="{{record}}"
            : > "$RECORD"

            emit() { printf '%s\n' "$1"; }

            field() { printf '%s' "$1" | sed -n "s/.*\"$2\":\"\([^\"]*\)\".*/\1/p"; }

            {{body}}
            """;

        File.WriteAllText(script, contents);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new FakePlugin(new PluginExecutable(script, RequiresDotnetHost: false), record);
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
}
