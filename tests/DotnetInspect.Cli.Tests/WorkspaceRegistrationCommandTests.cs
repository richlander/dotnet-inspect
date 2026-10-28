using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

// PR-fast: inert registration composition and canonical codecs; no acquired fixtures.
[Collection("Console")]
public sealed class WorkspaceRegistrationCommandTests
{
    [Fact]
    public async Task Registrations_ComposeCatalogEcosystemAndIndependentPrefix()
    {
        var created = await Run("ecosystem", "add", "runtime");
        Assert.Equal(0, created.ExitCode);
        Assert.Empty(created.Error);
        WorkspaceSharePacket original = Decode(created.Output);
        var runtime = Assert.IsType<WorkspaceRegistration.Ecosystem>(Assert.Single(original.Registrations));
        Assert.Equal("System", Assert.Single(runtime.Declaration.NamespaceRoots));
        Assert.Empty(runtime.Declaration.CorePackages);

        // Runtime already declares System. internally; the independent registration is distinct.
        var added = await Run("prefix", "add", "System.", "--packet", created.Output.Trim());
        Assert.Equal(0, added.ExitCode);
        Assert.Empty(added.Error);
        var registrations = Decode(added.Output).Registrations;
        Assert.Equal(2, registrations.Count);
        Assert.IsType<WorkspaceRegistration.Ecosystem>(registrations[0]);
        Assert.Equal("System.", Assert.IsType<WorkspaceRegistration.PackagePrefix>(registrations[1]).Prefix.Prefix);
        var removed = await Run("prefix", "remove", "System.", "--packet", added.Output.Trim());
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(created.Output, removed.Output);
        Assert.Equal(created.Output.Trim(), WorkspaceSharePacketCodec.Encode(original));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("ai")]
    public async Task PrefixOnlyWorkspace_CanAddAndRemoveAnEcosystem(string ecosystem)
    {
        var created = await Run("prefix", "add", "Microsoft.Extensions.");
        Assert.Equal(0, created.ExitCode);
        Assert.IsType<WorkspaceRegistration.PackagePrefix>(Assert.Single(Decode(created.Output).Registrations));
        var added = await Run("ecosystem", "add", "ecosystem." + ecosystem, "--packet", created.Output.Trim());
        Assert.Equal(0, added.ExitCode);
        var removed = await Run("ecosystem", "remove", ecosystem, "--packet", added.Output.Trim());
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(created.Output, removed.Output);
    }

    [Fact]
    public async Task Registrations_PreservePackageContextAndSources()
    {
        string input = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            """{"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}""",
            TestContext.Current.CancellationToken));
        var added = await Run("ecosystem", "add", "runtime", "--packet", input);
        Assert.Equal(0, added.ExitCode);
        WorkspaceSharePacket packet = Decode(added.Output);
        Assert.Equal(5, packet.FormatVersion);
        Assert.Equal(0, packet.FocusedTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
        Assert.Equal("System.Text.Json", Assert.Single(packet.Tabs).Source);
        var removed = await Run("ecosystem", "remove", "runtime", "--packet", added.Output.Trim());
        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(input, removed.Output.Trim());
    }

    [Theory]
    [InlineData("ecosystem", "absent")]
    [InlineData("ecosystem", "ecosystem.Bad")]
    [InlineData("prefix", "*")]
    public async Task Registrations_RejectInvalidInputs(string kind, string value)
    {
        var result = await Run(kind, "add", value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Theory]
    [InlineData("--namespace-root", "System")]
    [InlineData("--core-package", "System.Text.Json")]
    [InlineData("--from", "runtime")]
    public async Task EcosystemAdd_DoesNotExposeMetadataAuthoring(string option, string value)
    {
        var result = await Run("ecosystem", "add", "runtime", option, value);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(option, result.Error);
    }

    [Theory]
    [InlineData("ecosystem", "runtime")]
    [InlineData("prefix", "Microsoft.Extensions.")]
    public async Task Registrations_RejectDuplicatesAbsenceAndEmptyResult(string kind, string value)
    {
        var created = await Run(kind, "add", value);
        var duplicate = await Run(kind, "add", value, "--packet", created.Output.Trim());
        Assert.Equal(1, duplicate.ExitCode);
        Assert.Empty(duplicate.Output);
        Assert.Contains("already exists", duplicate.Error);
        var absent = await Run(kind, "remove", kind == "ecosystem" ? "absent" : "Absent.", "--packet", created.Output.Trim());
        Assert.Equal(1, absent.ExitCode);
        Assert.Empty(absent.Output);
        Assert.Contains("absent", absent.Error);
        var empty = await Run(kind, "remove", value, "--packet", created.Output.Trim());
        Assert.Equal(1, empty.ExitCode);
        Assert.Empty(empty.Output);
    }

    [Fact]
    public async Task EcosystemAdd_RetainsScannerProjectionRefusal()
    {
        var result = await Run("ecosystem", "add", "aspire");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("NonProjectable", result.Error);
        Assert.Contains("scanner", result.Error);
    }

    [Theory]
    [InlineData("ecosystem", "runtime")]
    [InlineData("prefix", "Microsoft.Extensions.")]
    public async Task Registrations_EmitWebsiteUrl(string kind, string value)
    {
        var packet = await Run(kind, "add", value);
        var url = await Run(kind, "add", value, "--share", "url");
        Assert.Equal(0, url.ExitCode);
        Assert.Contains("?w=" + packet.Output.Trim(), url.Output);
    }

    private static WorkspaceSharePacket Decode(string output) => WorkspaceSharePacketCodec.Decode(
        output.Trim(), TestContext.Current.CancellationToken);

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            DotnetInspector.Networking.HttpClientFactory.SetPackageSourceHandlerForTesting(
                _ => throw new InvalidOperationException("Workspace registration editing must not acquire content."));
            try
            {
                return await DotnetInspect.Cli.CommandLineBuilder.CreateRootCommand()
                    .Parse(["workspace", .. args]).InvokeAsync();
            }
            finally
            {
                DotnetInspector.Networking.HttpClientFactory.SetPackageSourceHandlerForTesting(null);
            }
        });
}
