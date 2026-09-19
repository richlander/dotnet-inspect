using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

internal static class CliSourceSelectorFactory
{
    internal static SourceSelector.PackageSource CreatePackageSource(
        string value)
    {
        if (value.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return new SourceSelector.PackageArchive(value);

        var (name, version) = PackageReferenceParser.Parse(value);
        return new SourceSelector.PackageReference(name, version);
    }
}
