using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class FindWorkspacePacketTests
{
    private const string RuntimeRegistration =
        """["e","ecosystem.runtime",["System"],[],[["t","DotNetRuntime"],["p","System."]]]""";

    private static string Packet(string registrations) => Encode(
        $$$"""{"f":3,"t":[],"g":[],"r":[{{{registrations}}}],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""");

    private static string Encode(string json) => WorkspaceSharePacketCodec.Encode(
        WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken));

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    [Theory]
    [InlineData("--package", "System.Text.Json@10.0.0")]
    [InlineData("--library", "Missing.dll")]
    [InlineData("--platform", "runtime")]
    [InlineData("--ecosystem", "runtime")]
    [InlineData("--project", "Missing.csproj")]
    [InlineData("--bin", "missing-bin")]
    [InlineData("--package-prefix", "System.")]
    [InlineData("--tfm", "net10.0")]
    [InlineData("--extensions", null)]
    [InlineData("--aspnetcore", null)]
    public async Task CompetingSelectorFailsBeforePacketOrSourceAcquisition(string selector, string? value)
    {
        string[] source = value is null ? [selector] : [selector, value];
        var result = await Run(["find", "JsonSerializer", "--workspace", "invalid", .. source]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("cannot be combined", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-packet")]
    [InlineData("https://example.invalid/?packet=not-a-packet")]
    public async Task InvalidPacketFailsVisibly(string value)
    {
        var result = await Run("find", "JsonSerializer", "--workspace", value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""["p","System."]""")]
    [InlineData("""["l",["t","DotNetRuntime",["System.Runtime","11.0.0.0",null,"b03f5f7f11d50a3a"]]]""")]
    [InlineData("""["e","ecosystem.custom",[],[],[["l",["t","DotNetRuntime",["System.Runtime","11.0.0.0",null,"b03f5f7f11d50a3a"]]]]]""")]
    public async Task UnsupportedRegistrationsCannotFallBackToPlatform(string registrations)
    {
        var result = await Run("find", "JsonSerializer", "--workspace", Packet(registrations), "--count");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("registration-only Ecosystem Workspace", result.Error);
    }

    [Theory]
    [InlineData("""{"f":3,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData("""{"f":5,"s":[["https://example.invalid/v3/index.json"]],"t":[],"g":[],"r":[["e","ecosystem.runtime",[],[],[["t","DotNetRuntime"]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    public async Task UnsupportedContentOrSourcesFailBeforeAcquisition(string json)
    {
        var result = await Run("find", "JsonSerializer", "--workspace", Encode(json), "--count");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("registration-only Ecosystem Workspace", result.Error);
    }

    [Fact]
    public async Task MixedPlatformAndCorePackagesFailVisibly()
    {
        string packet = Packet("""["e","ecosystem.custom",[],["system.text.json"],[["t","DotNetRuntime"]]]""");
        var result = await Run("find", "JsonSerializer", "--workspace", packet, "--count");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("mixed Platform and core-package", result.Error);
    }

    [Fact]
    public async Task ChangedCatalogDeclarationIsNotReloadedByIdentity()
    {
        // The catalog's Runtime has a Platform population. This packet deliberately does not.
        string packet = Packet("""["e","ecosystem.runtime",["System"],[],[["p","System."]]]""");
        var result = await Run("find", "System.Text.Json.JsonSerializer", "--workspace", packet, "--count");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("no searchable population", result.Error);
    }

    [Fact]
    public async Task PacketOrderAndDeclarationsSurviveAdmission()
    {
        string packet = Packet("""["e","ecosystem.custom",["Example"],["example.core"],[["p","Example."]]],["e","ecosystem.runtime",[],[],[["t","DotNetRuntime"]]]""");
        var options = new FindOptions { WorkspacePacket = packet };
        Assert.False(options.UsesImplicitPlatform);
        Assert.True(FindWorkspacePacketAdmission.TryAdmit(options, TestContext.Current.CancellationToken,
            out FindOptions admitted, out string? error), error);
        Assert.Equal(["ecosystem.custom", "ecosystem.runtime"], admitted.Ecosystems!.Select(id => id.Value));
        Assert.Equal("example.core", admitted.WorkspaceEcosystems![0].Declaration.CorePackages[0].PackageId);
        var result = await Run("find", "JsonSerializer", "--workspace", packet, "-D", "--take", "1");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Results", result.Output);
    }

    [Fact]
    public void TipsRetainWorkspaceAndMemberLens()
    {
        string packet = Packet(RuntimeRegistration);
        var tips = FindOptionsParser.BuildTips(new FindOptions { WorkspacePacket = packet, Members = true }, ".Serialize");
        Assert.NotEmpty(tips);
        Assert.All(tips, tip =>
        {
            Assert.Equal("find", tip.Subcommand);
            Assert.Contains($"--workspace {packet}", tip.Args);
            Assert.Contains("--members", tip.Args);
            Assert.DoesNotContain("--platform", tip.Args);
        });
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--markdown")]
    public async Task PrefixTakeStillRejectsNonStreamingTerminals(string terminal)
    {
        var result = await Run("find", "JsonSerializer", "--workspace", Packet(RuntimeRegistration),
            "--take", "1", terminal);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--take", result.Error);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    [InlineData("--table")]
    [InlineData("--markdown")]
    [InlineData("--count")]
    public async Task AuthoredEcosystemWorkspaceMatchesOrdinaryFind(string terminal)
    {
        var authored = await Run("workspace", "--register-ecosystem", "runtime", "--share", "packet");
        Assert.Equal(0, authored.ExitCode);
        string packet = authored.Output.Trim();
        string[] closing = terminal == "--count" ? [terminal] : [terminal, "-n", "1"];
        var baseline = await Run(["find", "JsonSerializer", "--type", "JsonSerializer", "--ecosystem", "runtime", .. closing]);
        var result = await Run(["find", "JsonSerializer", "--type", "JsonSerializer", "--workspace", packet, .. closing]);
        Assert.Equal(0, baseline.ExitCode);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(baseline.Output, result.Output);
        Assert.Equal(baseline.Error, result.Error);
        if (terminal == "--count")
            Assert.Equal("1", result.Output.Trim());
        else
            Assert.Contains("JsonSerializer", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnknownCatalogIdentityUsesDeclaredPlatform()
    {
        string packet = Packet(RuntimeRegistration.Replace("ecosystem.runtime", "ecosystem.custom"));
        var result = await Run("find", "JsonSerializer", "--type", "JsonSerializer", "--workspace", packet, "--json", "-n", "1");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("System.Text.Json.JsonSerializer", result.Output);
        Assert.Contains("ecosystem.custom", result.Output);
        Assert.Equal(packet, WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.Decode(packet,
            TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedQueryIsNotExecutedAsFindScope(bool ecosystem)
    {
        string packet = Encode(
            $$$"""{"f":3,"t":[],"g":[],"r":[{{{(ecosystem ? RuntimeRegistration : "")}}}],"a":null,"x":null,"q":[["package-query/v1",{"t":[["prefix","eq","Microsoft.Extensions."],["prerelease","eq","stable"]],"b":[["candidates",200]]}]],"v":[{"t":null,"u":{"k":"workspace"},"q":[0]}]}""");
        var result = await Run("find", "JsonSerializer", "--workspace", packet, "--count");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(ecosystem ? "cannot be mixed" : "registration-only Ecosystem Workspace", result.Error);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task MemberLensAndRowNarrowingMatchOrdinaryFind()
    {
        string packet = Packet(RuntimeRegistration);
        var baseline = await Run("find", ".Serialize", "--ecosystem", "runtime", "--json", "-n", "1");
        var result = await Run("find", ".Serialize", "--workspace", packet, "--json", "-n", "1");
        Assert.Equal(0, baseline.ExitCode);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(baseline.Output, result.Output);
        Assert.Contains("Serialize", result.Output);
    }
}
