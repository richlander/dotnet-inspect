using DotnetInspector.Ecosystems;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    // One exact nuget.org package per authored DependsOn edge. Each child
    // package must report its parent ecosystem in Ecosystem Dependencies.
    private static readonly Dictionary<string, string> s_dependsOnEvidencePackages = new()
    {
        ["ecosystem.microsoft-extensions"] = "Microsoft.Extensions.Logging@9.0.0",
        ["ecosystem.aspnetcore"] = "Microsoft.AspNetCore.Authentication.JwtBearer@9.0.0",
        ["ecosystem.aspire"] = "Aspire.Hosting@9.0.0",
        ["ecosystem.ai"] = "Microsoft.Extensions.AI@9.5.0",
        ["ecosystem.blazor"] = "Microsoft.AspNetCore.Components.WebAssembly@9.0.0",
        ["ecosystem.maui"] = "Microsoft.Maui.Core@9.0.0",
    };

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task EveryDependsOnEdgeIsGroundedInARealChildPackage()
    {
        EcosystemPackDescriptor[] packs = [.. EcosystemPackCatalog.Discover()];
        Dictionary<string, string> titles =
            packs.ToDictionary(pack => pack.Id.Value, pack => pack.Title);
        EcosystemPackDescriptor[] children =
            [.. packs.Where(pack => pack.DependsOn is not null)];

        Assert.Equal(
            children.Select(pack => pack.Id.Value).Order(StringComparer.Ordinal),
            s_dependsOnEvidencePackages.Keys.Order(StringComparer.Ordinal));
        foreach (EcosystemPackDescriptor child in children)
        {
            string package = s_dependsOnEvidencePackages[child.Id.Value];
            var (exit, output, error) = await RunAppAsync(
                "package",
                package,
                "-S",
                "Package Info");

            Assert.True(exit == 0, $"{package}: exit {exit}.{Environment.NewLine}{error}");
            string line = Assert.Single(
                output.Split('\n'),
                line => line.StartsWith("| Ecosystem Dependencies |", StringComparison.Ordinal));
            string[] reported =
            [
                .. line["| Ecosystem Dependencies |".Length..]
                    .TrimEnd(' ', '|', '\r')
                    .Split(',', StringSplitOptions.TrimEntries),
            ];
            Assert.Contains(titles[child.DependsOn!.Value], reported);
        }
    }
}
