using DotnetInspector.Queries.Definitions;

using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspacePackageSourceBindingTests
{
    private const string PrivateEndpoint =
        "https://nuget.pkg.github.com/example/index.json";
    private const string OtherPrivateEndpoint =
        "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json";

    [Fact]
    public void Create_EmptyDeclarationsPreserveAnEmptyPlan()
    {
        var bound = Assert.IsType<WorkspacePackageSourceBindingResult.Bound>(
            WorkspacePackageSourceBinding.Create(
                [],
                new Dictionary<
                    string,
                    PackageSourceCredential>(StringComparer.Ordinal)));

        Assert.Empty(bound.Plan.Sources);
        Assert.Empty(bound.Plan.UnboundAuthenticationRequirements);
    }

    [Fact]
    public void Create_BindsInDeclarationOrderAndReportsUnboundRequirements()
    {
        WorkspacePackageSourceDefinition[] definitions =
        [
            new("https://api.nuget.org/v3/index.json"),
            new(
                PrivateEndpoint,
                WorkspacePackageSourceAuthentication.AuthenticationRequired),
            new(
                OtherPrivateEndpoint,
                WorkspacePackageSourceAuthentication.AuthenticationRequired),
        ];
        var credentials = new Dictionary<
            string,
            PackageSourceCredential>(StringComparer.Ordinal)
        {
            [PrivateEndpoint] = new("example-user", "session-only-secret"),
        };

        var bound = Assert.IsType<WorkspacePackageSourceBindingResult.Bound>(
            WorkspacePackageSourceBinding.Create(definitions, credentials));

        Assert.Equal(
            definitions.Select(static source => source.Endpoint),
            bound.Plan.Sources.Select(static source => source.Url));
        Assert.Null(bound.Plan.Sources[0].Credential);
        Assert.Equal(
            "example-user",
            bound.Plan.Sources[1].Credential?.Username);
        Assert.Equal(
            "session-only-secret",
            bound.Plan.Sources[1].Credential?.Password);
        Assert.Null(bound.Plan.Sources[2].Credential);
        Assert.Equal(
            OtherPrivateEndpoint,
            Assert.Single(bound.Plan.UnboundAuthenticationRequirements)
                .Endpoint);
    }

    [Fact]
    public void Create_RejectsCredentialForAnonymousSource()
    {
        var rejected =
            Assert.IsType<WorkspacePackageSourceBindingResult.Rejected>(
                WorkspacePackageSourceBinding.Create(
                    [new WorkspacePackageSourceDefinition(PrivateEndpoint)],
                    new Dictionary<
                        string,
                        PackageSourceCredential>(StringComparer.Ordinal)
                    {
                        [PrivateEndpoint] =
                            new("example-user", "session-only-secret"),
                    }));

        Assert.Contains(
            "not an authentication-required source",
            rejected.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsPartialExplicitBindingOnOneOrigin()
    {
        const string secondEndpoint =
            "https://nuget.pkg.github.com/example/secondary/index.json";
        var rejected =
            Assert.IsType<WorkspacePackageSourceBindingResult.Rejected>(
                WorkspacePackageSourceBinding.Create(
                    [
                        new(
                            PrivateEndpoint,
                            WorkspacePackageSourceAuthentication
                                .AuthenticationRequired),
                        new(
                            secondEndpoint,
                            WorkspacePackageSourceAuthentication
                                .AuthenticationRequired),
                    ],
                    new Dictionary<
                        string,
                        PackageSourceCredential>(StringComparer.Ordinal)
                    {
                        [PrivateEndpoint] =
                            new("example-user", "session-only-secret"),
                    }));

        Assert.Contains(
            "Bind every endpoint on that origin or none",
            rejected.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "session-only-secret", "username")]
    [InlineData("example-user", "", "password")]
    public void Create_RejectsIncompleteCredential(
        string username,
        string password,
        string expected)
    {
        var rejected =
            Assert.IsType<WorkspacePackageSourceBindingResult.Rejected>(
                WorkspacePackageSourceBinding.Create(
                    [
                        new(
                            PrivateEndpoint,
                            WorkspacePackageSourceAuthentication
                                .AuthenticationRequired),
                    ],
                    new Dictionary<
                        string,
                        PackageSourceCredential>(StringComparer.Ordinal)
                    {
                        [PrivateEndpoint] = new(username, password),
                    }));

        Assert.Contains(expected, rejected.Message, StringComparison.Ordinal);
    }
}
