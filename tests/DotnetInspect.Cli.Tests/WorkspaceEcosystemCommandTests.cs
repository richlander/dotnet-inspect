using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

// PR-fast: declaration and canonical-codec operations only; no acquired fixtures.
[Collection("Console")]
public sealed class WorkspaceEcosystemCommandTests
{
    [Fact]
    public async Task EcosystemAuthoring_CreatesComposesReplacesAndRemovesDeclarations()
    {
        var created = await Run("add", "ecosystem.runtime", "--from", "runtime");
        Assert.Equal(0, created.ExitCode);
        Assert.Empty(created.Error);
        WorkspaceSharePacket original = Decode(created.Output);
        Assert.Empty(original.Tabs);
        var runtime = Assert.IsType<WorkspaceRegistration.Ecosystem>(Assert.Single(original.Registrations));
        Assert.Equal("System", Assert.Single(runtime.Declaration.NamespaceRoots));

        var added = await Run("add", "ecosystem.json", "--packet", created.Output.Trim(),
            "--namespace-root", "System.Text.Json", "--core-package", "System.Text.Json",
            "--package-prefix", "System.Text.Json", "--platform", "DotNetRuntime",
            "--library", "System.Text.Json@10.0.0/System.Text.Json@10.0.0.0");
        Assert.Equal(0, added.ExitCode);
        Assert.Empty(added.Error);
        var registration = Assert.IsType<WorkspaceRegistration.Ecosystem>(Decode(added.Output).Registrations[1]);
        Assert.Collection(registration.Declaration.Populations,
            value => Assert.IsType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(value),
            value => Assert.IsType<WorkspaceEcosystemPopulationDeclaration.Platform>(value),
            value => Assert.IsType<WorkspaceEcosystemPopulationDeclaration.ExactLibrary>(value));

        var updated = await Run("update", "ecosystem.json", "--packet", added.Output.Trim(),
            "--namespace-root", "System.Text.Json.Serialization");
        Assert.Equal(0, updated.ExitCode);
        var replacement = Assert.IsType<WorkspaceRegistration.Ecosystem>(Decode(updated.Output).Registrations[1]);
        Assert.Equal("System.Text.Json.Serialization", Assert.Single(replacement.Declaration.NamespaceRoots));
        Assert.Empty(replacement.Declaration.CorePackages);
        Assert.Empty(replacement.Declaration.Populations);

        var removed = await Run("remove", "ecosystem.json", "--packet", updated.Output.Trim());
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(created.Output, removed.Output);
        Assert.Equal(created.Output.Trim(), WorkspaceSharePacketCodec.Encode(original));
    }

    [Fact]
    public async Task EcosystemAuthoring_PreservesPackageContextAndSources()
    {
        const string json = """
            {"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[["p","Microsoft.Extensions."]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """;
        string input = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            json, TestContext.Current.CancellationToken));
        var added = await Run("add", "ecosystem.runtime", "--from", "runtime", "--packet", input);
        Assert.Equal(0, added.ExitCode);
        WorkspaceSharePacket packet = Decode(added.Output);
        Assert.Equal(5, packet.FormatVersion);
        Assert.Equal(0, packet.FocusedTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
        Assert.Equal("System.Text.Json", Assert.Single(packet.Tabs).Source);
        Assert.Equal(2, packet.Registrations.Count);
        var removed = await Run("remove", "ecosystem.runtime", "--packet", added.Output.Trim());
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(input, removed.Output.Trim());
    }

    [Theory]
    [InlineData("ecosystem.Bad", "--namespace-root", "System")]
    [InlineData("ecosystem.json", "--namespace-root", "System.*")]
    [InlineData("ecosystem.json", "--core-package", "System.Text.Json@10.0.0")]
    [InlineData("ecosystem.json", "--package-prefix", "*")]
    [InlineData("ecosystem.json", "--from", "absent")]
    public async Task EcosystemAuthoring_RejectsInvalidContributions(string id, string option, string value)
    {
        var result = await Run("add", id, option, value);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task EcosystemAuthoring_UnknownPlatformUsesOrdinaryParserFailure()
    {
        var result = await Run("add", "ecosystem.json", "--platform", "Unknown");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unknown", result.Error);
    }

    [Fact]
    public async Task EcosystemAuthoring_RejectsDuplicateAbsentAndNonportableDeclarations()
    {
        var created = await Run("add", "ecosystem.runtime", "--from", "runtime");
        var duplicate = await Run("add", "ecosystem.runtime", "--from", "runtime", "--packet", created.Output.Trim());
        var absent = await Run("update", "ecosystem.absent", "--namespace-root", "Absent", "--packet", created.Output.Trim());
        var scanner = await Run("add", "ecosystem.aspire", "--from", "aspire");
        foreach (var result in new[] { duplicate, absent, scanner })
        {
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
        }
        Assert.Contains("already exists", duplicate.Error);
        Assert.Contains("absent", absent.Error);
        Assert.Contains("NonProjectable", scanner.Error);
        Assert.Contains("scanner", scanner.Error);
    }

    [Fact]
    public async Task EcosystemAuthoring_FromOverridesOnlySpecifiedContributionGroups()
    {
        var result = await Run("add", "ecosystem.custom", "--from", "runtime",
            "--namespace-root=Custom", "--core-package=System.Text.Json",
            "--package-prefix=System.Text.Json", "--platform=AspNetCore");
        Assert.Equal(0, result.ExitCode);
        var registration = Assert.IsType<WorkspaceRegistration.Ecosystem>(Assert.Single(Decode(result.Output).Registrations));
        Assert.Equal("Custom", Assert.Single(registration.Declaration.NamespaceRoots));
        Assert.Equal("System.Text.Json", Assert.Single(registration.Declaration.CorePackages).PackageId);
        Assert.Collection(registration.Declaration.Populations,
            value => Assert.IsType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(value),
            value => Assert.IsType<WorkspaceEcosystemPopulationDeclaration.Platform>(value));
    }

    [Fact]
    public async Task EcosystemAuthoring_EmitsWebsiteUrlFromSamePacket()
    {
        var packet = await Run("add", "ecosystem.runtime", "--from", "runtime");
        var url = await Run("add", "ecosystem.runtime", "--from", "runtime", "--share", "url");
        Assert.Equal(0, url.ExitCode);
        Assert.Contains("?w=" + packet.Output.Trim(), url.Output);
    }

    private static WorkspaceSharePacket Decode(string output) => WorkspaceSharePacketCodec.Decode(
        output.Trim(), TestContext.Current.CancellationToken);

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            // Any attempt to construct a package-source transport fails the test.
            DotnetInspector.Networking.HttpClientFactory.SetPackageSourceHandlerForTesting(
                _ => throw new InvalidOperationException("Ecosystem authoring must not acquire content."));
            try
            {
                return await DotnetInspect.Cli.CommandLineBuilder.CreateRootCommand()
                    .Parse(["workspace", "ecosystem", .. args]).InvokeAsync();
            }
            finally
            {
                DotnetInspector.Networking.HttpClientFactory.SetPackageSourceHandlerForTesting(null);
            }
        });
}
