using System.Runtime.Versioning;
using DotnetInspector.Packages;

namespace InspectWeb.Engine;

/// <summary>
/// The product's compile-library outcome for one package coordinate, in a DTO-neutral shape.
/// </summary>
/// <remarks>
/// Several facades report this outcome, and each publishes it through its own wire record. The
/// classification and its message text stay here so the browser reports one answer regardless of
/// which capability asked.
/// </remarks>
internal enum BrowserCompileLibraryState
{
    Selected,
    NoCompileAssets,
    NoMatchingTargetFramework,
    EmptyCompileGroup,
    InvalidImplementationAssets,
}

internal sealed record BrowserCompileLibraryInfo(
    BrowserCompileLibraryState State,
    string? TargetFramework,
    string? Message);

[SupportedOSPlatform("browser")]
internal static class BrowserCompileLibraryProjection
{
    internal static BrowserCompileLibraryInfo Project(
        PackageCompileAssetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        string? framework = BrowserFrameworkText.Project(selection.TargetFramework);
        if (selection.IsSelected && framework is null)
        {
            throw new InvalidOperationException(
                "The selected compile-library framework cannot be represented safely.");
        }

        return new(
            selection.Status switch
            {
                PackageCompileAssetSelectionStatus.Selected =>
                    BrowserCompileLibraryState.Selected,
                PackageCompileAssetSelectionStatus.NoCompileAssets =>
                    BrowserCompileLibraryState.NoCompileAssets,
                PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                    BrowserCompileLibraryState.NoMatchingTargetFramework,
                PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                    BrowserCompileLibraryState.EmptyCompileGroup,
                PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                    BrowserCompileLibraryState.InvalidImplementationAssets,
                _ => throw new InvalidOperationException(
                    "Package compile-asset selection returned an unknown outcome."),
            },
            framework,
            selection.Status switch
            {
                PackageCompileAssetSelectionStatus.Selected => null,
                PackageCompileAssetSelectionStatus.NoCompileAssets =>
                    "The package contains no compile assets.",
                PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                    "No compatible target framework was selected.",
                PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                    "The selected target framework declares an empty compile group.",
                PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                    "The package has an invalid implementation-asset layout.",
                _ => throw new InvalidOperationException(
                    "Package compile-asset selection returned an unknown outcome."),
            });
    }

    internal static BrowserCompileLibraryInfo Selected(string framework) =>
        new(
            BrowserCompileLibraryState.Selected,
            BrowserFrameworkText.Require(framework),
            Message: null);
}

/// <summary>
/// The bounded framework identifiers the browser is willing to transport.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserFrameworkText
{
    internal static string[] Available(PackageCompileAssetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return
        [
            .. selection.AvailableTargetFrameworks
                .Select(Project)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    internal static string Active(BrowserPackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return Project(coordinate.Framework) ?? "";
    }

    internal static string Require(string framework) =>
        Project(framework)
        ?? throw new InvalidOperationException(
            "A framework identifier cannot be represented safely.");

    internal static string DependencyGroup(string framework) =>
        string.IsNullOrWhiteSpace(framework)
            ? "any"
            : Require(framework);

    internal static string? Project(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework)
            || framework.Length > 128)
        {
            return null;
        }

        foreach (char character in framework)
        {
            if (!(character is >= 'a' and <= 'z')
                && !(character is >= 'A' and <= 'Z')
                && !(character is >= '0' and <= '9')
                && character is not
                    ('.' or '-' or '+' or '_' or ',' or '=' or ' '))
            {
                return null;
            }
        }

        return framework;
    }
}

/// <summary>
/// The platform workspace's product-owned package identity. Facades report it verbatim.
/// </summary>
internal static class BrowserPlatformIdentity
{
    internal const string PackageName = "Microsoft.NETCore.App";

    internal static string AssemblyFileName(string assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assembly
            : $"{assembly}.dll";
    }
}
