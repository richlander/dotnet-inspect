using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class RouterEcosystemExplanationTests
{
    private static readonly PlatformPruneInventory Net10 =
        PlatformPruneInventory.FromExactFamily(
            "Microsoft.NETCore.App", "net10.0", "10.0.12",
            ["System.Linq|4.3.0", "System.Text.Json|10.0.12"]);

    [Theory]
    [InlineData("System.Linq", "4.3.0", "pruned")]
    [InlineData("System.Text.Json", "9.0.0", "pruned")]
    [InlineData("System.Text.Json", "11.0.0", "not pruned")]
    public void ExactPackageExplainsSupplyWithoutChangingItsSubject(
        string id, string version, string expected)
    {
        var coordinate = new PackageCoordinate(id, version);
        string? note = RouterEcosystemExplanation.FormatPackage(
            $"{id}@{version}",
            EcosystemPackCatalog.IsEcosystemPackage(coordinate, "net10.0", Net10),
            "net10.0");
        Assert.Equal($"Routing to NuGet package '{id}@{version}' · .NET Runtime; {expected} for traversal target net10.0.", note);
    }

    [Fact]
    public void AssetSelectionTfmDoesNotChangePruningTarget()
    {
        string? Note(string tfm) => RouterEcosystemExplanation.FormatPackage(
            "System.Text.Json@9.0.0",
            EcosystemPackCatalog.IsEcosystemPackage(
                new("System.Text.Json", "9.0.0", tfm), "net10.0", Net10),
            "net10.0");
        Assert.Equal(Note("net8.0"), Note("net9.0"));
        Assert.Contains("pruned for traversal target net10.0", Note("net8.0"));
    }

    [Fact]
    public void UnavailableInventoryDoesNotClaimPrunedOrNotPruned()
    {
        string? note = RouterEcosystemExplanation.FormatPackage(
            "System.Text.Json@9.0.0",
            EcosystemPackCatalog.IsEcosystemPackage(new("System.Text.Json", "9.0.0"), "net10.0", null),
            "net10.0");
        Assert.Equal("Routing to NuGet package 'System.Text.Json@9.0.0' · .NET Runtime; pruning unavailable for traversal target net10.0.", note);
    }

    [Fact]
    public void VersionlessPackagePreservesUncertainty()
    {
        string? note = RouterEcosystemExplanation.FormatPackage(
            "System.Text.Json",
            EcosystemPackCatalog.IsEcosystemPackage(new("System.Text.Json"), "net10.0", Net10),
            "net10.0");
        Assert.Contains("pruning undetermined", note);
        Assert.DoesNotContain("; pruned", note);
        Assert.DoesNotContain("; not pruned", note);
    }

    [Fact]
    public void UnrecognizedPackageDoesNotAddDiagnosticNoise()
    {
        Assert.Null(RouterEcosystemExplanation.FormatPackage(
            "Newtonsoft.Json@13.0.3",
            EcosystemPackCatalog.IsEcosystemPackage(new("Newtonsoft.Json", "13.0.3"), "net10.0", Net10),
            "net10.0"));
    }

    [Fact]
    public async Task PlatformRoutePreservesJsonPayloadAndExplainsOnlyOnStderr()
    {
        async Task<(int ExitCode, string Output, string Error)> Invoke(params string[] args)
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return await ConsoleCapture.RunAsync(() =>
                CommandLineBuilder.InvokeAsync(root.Parse(processed), processed));
        }
        var implicitResult = await Invoke("System.Text.Json", "--json");
        var explicitResult = await Invoke("library", "System.Text.Json", "--json");
        Assert.Equal(0, implicitResult.ExitCode);
        Assert.Equal(0, explicitResult.ExitCode);
        Assert.Equal(explicitResult.Output, implicitResult.Output);
        Assert.Contains("Routing to platform library 'System.Text.Json' · .NET Runtime.", implicitResult.Error);
        Assert.DoesNotContain("Routing to", explicitResult.Error);
        Assert.DoesNotContain("pruned", implicitResult.Error);
        using var payload = System.Text.Json.JsonDocument.Parse(implicitResult.Output);
    }

    [Theory]
    [InlineData("--package", "System.Text.Json@9.0.0", 0)]
    [InlineData("--platform", "System.Text.Json", 0)]
    [InlineData("--framework", "runtime@10.0", 0)]
    [InlineData("--workspace", "not-a-packet", 0)]
    [InlineData("--version", "10.0", 1)]
    public async Task ExplicitSourceOverrideDoesNotReceivePlatformDefaultNote(string option, string value, int expectedExitCode)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["System.Text.Json", option, value, "--help"], root);
        var result = await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(root.Parse(args), args));
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.DoesNotContain("Routing to platform library", result.Error);
    }

    [Fact]
    public async Task ExplicitCoordinateStillRoutesToPackageWithoutPrompt()
    {
        using var decisions = RouterDecisionLog.Begin();
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["System.Text.Json@9.0.0", "--help"], root);
        var result = await ConsoleCapture.RunAsync(() =>
            CommandLineBuilder.InvokeAsync(root.Parse(args), args));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(decisions.Decisions, d => d.Stage == "router-rewrite"
            && d.Detail.Contains("-> package System.Text.Json@9.0.0"));
        Assert.Contains("Routing to NuGet package 'System.Text.Json@9.0.0' · .NET Runtime", result.Error);
        Assert.DoesNotContain("Choose", result.Output + result.Error);
    }
}
