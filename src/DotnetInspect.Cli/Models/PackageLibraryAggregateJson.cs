namespace DotnetInspect.Cli.Models;

internal sealed record PackageLibraryAggregateJson(
    string Package,
    string? PackageVersion,
    PackageLibraryAggregateSectionJson[] Sections);

internal sealed record PackageLibraryAggregateSectionJson(
    string Name,
    Dictionary<string, string>[] Rows);
