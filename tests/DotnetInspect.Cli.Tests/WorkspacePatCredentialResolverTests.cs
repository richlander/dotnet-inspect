using System.Text;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class WorkspacePatCredentialResolverTests
{
    private const string PrivateEndpoint =
        "https://nuget.pkg.github.com/example/index.json";

    [Fact]
    public async Task CredentialInputsAndBoundaries_AreEnforced()
    {
        await EnvironmentBinding_ProducesEphemeralCredential();
        await EnvironmentBinding_PreservesExactValue();
        await FileBinding_RemovesOneLineEndingAndLeavesFileUntouched();
        await AuthenticationRequiredWithoutBinding_UsesProviderFallback();
        await BindingForUnrequiredSource_IsRejected();
        await InvalidUtf8File_IsRejectedWithoutEchoingPayload();
        await RedirectedStandardInput_ProducesEphemeralCredential();
        await TerminalStandardInput_IsRejectedWithoutOpeningIt();
        await OversizedFile_IsRejectedWithoutEchoingPayload();
        await MissingEnvironmentVariable_DoesNotEchoASecret();
        await DuplicateBindings_AreRejectedBeforeReadingSecrets();
        await SameOriginCannotMixExplicitCredentialAndProviderFallback();
        await CredentialProviderScope_DoesNotUpgradeAnonymousOrigin();
        await WorkspacePackageRequests_FollowBoundedRedirects();
    }

    private static async Task EnvironmentBinding_ProducesEphemeralCredential()
    {
        string variable =
            "DOTNET_INSPECT_PAT_TEST_" + Guid.NewGuid().ToString("N");
        const string secret = "unit-test-pat";
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            PackageSource[] sources =
                await WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        Binding(
                            WorkspacePatInputKind.Environment,
                            variable),
                    ],
                    TestContext.Current.CancellationToken);

            PackageSource source = Assert.Single(sources);
            Assert.Equal("example-user", source.Credential!.Username);
            Assert.Equal(secret, source.Credential.Password);
            Assert.DoesNotContain(secret, source.Credential.ToString());
        }

        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static async Task EnvironmentBinding_PreservesExactValue()
    {
        string variable =
            "DOTNET_INSPECT_PAT_TEST_" + Guid.NewGuid().ToString("N");
        const string secret = "line-sensitive-pat\n";
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            PackageSource[] sources =
                await WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        Binding(
                            WorkspacePatInputKind.Environment,
                            variable),
                    ],
                    TestContext.Current.CancellationToken);

            Assert.Equal(secret, Assert.Single(sources).Credential!.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static async Task FileBinding_RemovesOneLineEndingAndLeavesFileUntouched()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-pat-{Guid.NewGuid():N}.txt");
        byte[] payload = Encoding.UTF8.GetBytes("file-pat\r\n");
        await File.WriteAllBytesAsync(
            path,
            payload,
            TestContext.Current.CancellationToken);
        try
        {
            PackageSource[] sources =
                await WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        Binding(
                            WorkspacePatInputKind.File,
                            path),
                    ],
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                "file-pat",
                Assert.Single(sources).Credential!.Password);
            Assert.Equal(
                payload,
                await File.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task AuthenticationRequiredWithoutBinding_UsesProviderFallback()
    {
        PackageSource[] sources =
            await WorkspacePatCredentialResolver.ResolveAsync(
                [PatSource()],
                [],
                TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(sources).Credential);
    }

    private static async Task BindingForUnrequiredSource_IsRejected()
    {
        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [
                        new WorkspacePackageSourceDefinition(
                            "https://api.nuget.org/v3/index.json"),
                    ],
                    [
                        new WorkspacePatBindingInput(
                            PrivateEndpoint,
                            "example-user",
                            WorkspacePatInputKind.Environment,
                            "IGNORED"),
                    ],
                    TestContext.Current.CancellationToken));

        Assert.Contains("not an authentication-required source", exception.Message);
    }

    private static async Task InvalidUtf8File_IsRejectedWithoutEchoingPayload()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-pat-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(
            path,
            [0xFF, 0xFE],
            TestContext.Current.CancellationToken);
        try
        {
            WorkspacePatBindingException exception =
                await Assert.ThrowsAsync<WorkspacePatBindingException>(
                    () => WorkspacePatCredentialResolver.ResolveAsync(
                        [PatSource()],
                        [
                            Binding(
                                WorkspacePatInputKind.File,
                                path),
                        ],
                        TestContext.Current.CancellationToken));

            Assert.Contains("not valid UTF-8", exception.Message);
            Assert.DoesNotContain("FF", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task RedirectedStandardInput_ProducesEphemeralCredential()
    {
        await using var input = new MemoryStream(
            Encoding.UTF8.GetBytes("stdin-pat\n"));

        PackageSource[] sources =
            await WorkspacePatCredentialResolver.ResolveAsync(
                [PatSource()],
                [
                    Binding(
                        WorkspacePatInputKind.StandardInput,
                        value: null),
                ],
                isInputRedirected: true,
                () => input,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "stdin-pat",
            Assert.Single(sources).Credential!.Password);
    }

    private static async Task TerminalStandardInput_IsRejectedWithoutOpeningIt()
    {
        bool opened = false;

        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        Binding(
                            WorkspacePatInputKind.StandardInput,
                            value: null),
                    ],
                    isInputRedirected: false,
                    () =>
                    {
                        opened = true;
                        return Stream.Null;
                    },
                    TestContext.Current.CancellationToken));

        Assert.Contains("stdin is a terminal", exception.Message);
        Assert.False(opened);
    }

    private static async Task OversizedFile_IsRejectedWithoutEchoingPayload()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-pat-{Guid.NewGuid():N}.txt");
        const char secretMarker = 'Z';
        await File.WriteAllBytesAsync(
            path,
            Enumerable.Repeat(
                (byte)secretMarker,
                WorkspacePatCredentialResolver.MaxSecretUtf8Bytes + 1)
                .ToArray(),
            TestContext.Current.CancellationToken);
        try
        {
            WorkspacePatBindingException exception =
                await Assert.ThrowsAsync<WorkspacePatBindingException>(
                    () => WorkspacePatCredentialResolver.ResolveAsync(
                        [PatSource()],
                        [
                            Binding(
                                WorkspacePatInputKind.File,
                                path),
                        ],
                        TestContext.Current.CancellationToken));

            Assert.Contains("exceeds", exception.Message);
            Assert.DoesNotContain(
                new string(secretMarker, 16),
                exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task MissingEnvironmentVariable_DoesNotEchoASecret()
    {
        string variable =
            "DOTNET_INSPECT_MISSING_PAT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, null);

        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        Binding(
                            WorkspacePatInputKind.Environment,
                            variable),
                    ],
                    TestContext.Current.CancellationToken));

        Assert.Contains("is not set", exception.Message);
    }

    private static async Task DuplicateBindings_AreRejectedBeforeReadingSecrets()
    {
        bool opened = false;

        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        Binding(
                            WorkspacePatInputKind.StandardInput,
                            value: null),
                        Binding(
                            WorkspacePatInputKind.StandardInput,
                            value: null),
                    ],
                    isInputRedirected: true,
                    () =>
                    {
                        opened = true;
                        return Stream.Null;
                    },
                    TestContext.Current.CancellationToken));

        Assert.Contains("supplied more than once", exception.Message);
        Assert.False(opened);
    }

    private static async Task
        SameOriginCannotMixExplicitCredentialAndProviderFallback()
    {
        bool opened = false;
        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [
                        new WorkspacePackageSourceDefinition(
                            "https://private.example/first/index.json",
                            WorkspacePackageSourceAuthentication
                                .AuthenticationRequired),
                        new WorkspacePackageSourceDefinition(
                            "https://private.example/second/index.json",
                            WorkspacePackageSourceAuthentication
                                .AuthenticationRequired),
                    ],
                    [
                        new WorkspacePatBindingInput(
                            "https://private.example/first/index.json",
                            "example-user",
                            WorkspacePatInputKind.StandardInput,
                            Value: null),
                    ],
                    isInputRedirected: true,
                    () =>
                    {
                        opened = true;
                        return Stream.Null;
                    },
                    TestContext.Current.CancellationToken));

        Assert.Contains("mixes explicit PAT bindings", exception.Message);
        Assert.False(opened);
    }

    private static async Task CredentialProviderScope_DoesNotUpgradeAnonymousOrigin()
    {
        var inner = new RecordingCredentialSource();
        var scoped = new WorkspaceCredentialSource(
            inner,
            [new Uri("https://private.example/v3/index.json")]);

        Assert.Null(await scoped.GetCredentialsAsync(
            new Uri("https://public.example/v3/index.json"),
            isRetry: false,
            TestContext.Current.CancellationToken));
        Assert.Empty(inner.Requests);

        PackageSourceCredential? credential =
            await scoped.GetCredentialsAsync(
                new Uri("https://private.example/flat/package/index.json"),
                isRetry: false,
                TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.Equal(
            ["https://private.example/flat/package/index.json"],
            inner.Requests);
    }

    private static async Task WorkspacePackageRequests_FollowBoundedRedirects()
    {
        var transport = new RedirectTransportHandler();
        using HttpMessageHandler handler =
            WorkspacePatCredentialResolver.CreatePackageRequestHandler(
                transport,
                credentialSource: null);
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://feed.example/v3/index.json");
        using HttpResponseMessage response = await invoker.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                "https://feed.example/v3/index.json",
                "https://feed.example/v3/redirected-index.json",
            ],
            transport.Requests);
    }

    private static WorkspacePackageSourceDefinition PatSource() =>
        new(
            PrivateEndpoint,
            WorkspacePackageSourceAuthentication.AuthenticationRequired);

    private static WorkspacePatBindingInput Binding(
        WorkspacePatInputKind kind,
        string? value) =>
        new(
            PrivateEndpoint,
            "example-user",
            kind,
            value);

    private sealed class RecordingCredentialSource : ICredentialSource
    {
        internal List<string> Requests { get; } = [];

        public bool HasCredentialSources => true;

        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult<PackageSourceCredential?>(
                new("provider", "secret"));
        }
    }

    private sealed class RedirectTransportHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (Requests.Count == 1)
            {
                var redirect = new HttpResponseMessage(
                    System.Net.HttpStatusCode.Found);
                redirect.Headers.Location =
                    new Uri("redirected-index.json", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
