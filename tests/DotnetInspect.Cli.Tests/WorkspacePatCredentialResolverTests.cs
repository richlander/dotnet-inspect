using System.Text;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class WorkspacePatCredentialResolverTests
{
    [Fact]
    public async Task EnvironmentBinding_ProducesEphemeralCredential()
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
                        new WorkspacePatBindingInput(
                            "github",
                            WorkspacePatInputKind.Environment,
                            variable),
                    ],
                    TestContext.Current.CancellationToken);

            PackageSource source = Assert.Single(sources);
            Assert.Equal("example", source.Credential!.Username);
            Assert.Equal(secret, source.Credential.Password);
            Assert.DoesNotContain(secret, source.Credential.ToString());
        }

        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task EnvironmentBinding_PreservesExactValue()
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
                        new WorkspacePatBindingInput(
                            "github",
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

    [Fact]
    public async Task FileBinding_RemovesOneLineEndingAndLeavesFileUntouched()
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
                        new WorkspacePatBindingInput(
                            "github",
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

    [Fact]
    public async Task RequiredPatWithoutBinding_FailsBeforeSourceConstruction()
    {
        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [],
                    TestContext.Current.CancellationToken));

        Assert.Contains("requires a PAT", exception.Message);
    }

    [Fact]
    public async Task BindingForUnrequiredSource_IsRejected()
    {
        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [
                        new WorkspacePackageSourceDefinition(
                            "nuget",
                            "https://api.nuget.org/v3/index.json"),
                    ],
                    [
                        new WorkspacePatBindingInput(
                            "github",
                            WorkspacePatInputKind.Environment,
                            "IGNORED"),
                    ],
                    TestContext.Current.CancellationToken));

        Assert.Contains("not a required PAT source", exception.Message);
    }

    [Fact]
    public async Task InvalidUtf8File_IsRejectedWithoutEchoingPayload()
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
                            new WorkspacePatBindingInput(
                                "github",
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

    [Fact]
    public async Task RedirectedStandardInput_ProducesEphemeralCredential()
    {
        await using var input = new MemoryStream(
            Encoding.UTF8.GetBytes("stdin-pat\n"));

        PackageSource[] sources =
            await WorkspacePatCredentialResolver.ResolveAsync(
                [PatSource()],
                [
                    new WorkspacePatBindingInput(
                        "github",
                        WorkspacePatInputKind.StandardInput,
                        Value: null),
                ],
                isInputRedirected: true,
                () => input,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "stdin-pat",
            Assert.Single(sources).Credential!.Password);
    }

    [Fact]
    public async Task TerminalStandardInput_IsRejectedWithoutOpeningIt()
    {
        bool opened = false;

        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        new WorkspacePatBindingInput(
                            "github",
                            WorkspacePatInputKind.StandardInput,
                            Value: null),
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

    [Fact]
    public async Task OversizedFile_IsRejectedWithoutEchoingPayload()
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
                            new WorkspacePatBindingInput(
                                "github",
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

    [Fact]
    public async Task MissingEnvironmentVariable_DoesNotEchoASecret()
    {
        string variable =
            "DOTNET_INSPECT_MISSING_PAT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, null);

        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        new WorkspacePatBindingInput(
                            "github",
                            WorkspacePatInputKind.Environment,
                            variable),
                    ],
                    TestContext.Current.CancellationToken));

        Assert.Contains("is not set", exception.Message);
    }

    [Fact]
    public async Task DuplicateBindings_AreRejectedBeforeReadingSecrets()
    {
        bool opened = false;

        WorkspacePatBindingException exception =
            await Assert.ThrowsAsync<WorkspacePatBindingException>(
                () => WorkspacePatCredentialResolver.ResolveAsync(
                    [PatSource()],
                    [
                        new WorkspacePatBindingInput(
                            "github",
                            WorkspacePatInputKind.StandardInput,
                            Value: null),
                        new WorkspacePatBindingInput(
                            "github",
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

        Assert.Contains("supplied more than once", exception.Message);
        Assert.False(opened);
    }

    private static WorkspacePackageSourceDefinition PatSource() =>
        new(
            "github",
            "https://nuget.pkg.github.com/example/index.json",
            WorkspacePackageSourceAuthentication.BasicPat,
            "example");
}
