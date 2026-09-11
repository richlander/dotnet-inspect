using DotnetInspector.Packages;
using NuGet.Versioning;

namespace DotnetInspector.Services;

/// <summary>
/// Reads a platform prune inventory from the reference packs installed on this machine.
/// </summary>
/// <remarks>
/// <para>
/// The upstream prune data ships inside each reference pack as <c>data/PackageOverrides.txt</c>,
/// so a machine with an SDK already has it. Reading from there keeps the answer offline and exact:
/// no acquisition, and the pack version is the one actually installed rather than a projection.
/// </para>
/// <para>
/// A shipped projection covering targets that are not installed is separate work. This source
/// answers only for what is present, and says so rather than approximating.
/// </para>
/// </remarks>
public static class InstalledPlatformPruneSource
{
    /// <summary>The shared-framework family each reference pack publishes entries for.</summary>
    static readonly Dictionary<string, string> FamilyByRefPack =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.NETCore.App.Ref"] = "Microsoft.NETCore.App",
            ["Microsoft.AspNetCore.App.Ref"] = "Microsoft.AspNetCore.App",
            ["NETStandard.Library.Ref"] = "NETStandard.Library",
        };

    /// <summary>The outcome of reading one installed family's inventory.</summary>
    /// <param name="Inventory">The inventory, or null when it could not be read.</param>
    /// <param name="Error">Why it could not be read, or null on success.</param>
    public sealed record Result(PlatformPruneInventory? Inventory, string? Error);

    /// <summary>
    /// Reads the inventory for one framework specification, such as <c>runtime</c> or
    /// <c>aspnetcore@10.0.11</c>.
    /// </summary>
    /// <remarks>
    /// A pack that publishes no <c>data/PackageOverrides.txt</c> subsumes nothing, which is a
    /// real answer rather than a failure: the empty inventory says so directly.
    /// </remarks>
    public static Result Read(string frameworkSpec, string? packsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameworkSpec);

        (string? refAssemblyPath, string? version, string? error) =
            PlatformResolver.ResolveFramework(frameworkSpec, packsDirectory);
        if (error is not null || refAssemblyPath is null || version is null)
        {
            return new Result(null, error ?? $"Could not resolve platform framework '{frameworkSpec}'.");
        }

        // ResolveFramework answers with the reference assembly directory,
        // <packs>/<Pack>.Ref/<version>/ref/<tfm>. The prune data sits beside `ref` under the
        // version directory, and the pack name two levels above names the shared framework.
        string targetFramework = Path.GetFileName(refAssemblyPath);
        if (Directory.GetParent(refAssemblyPath)?.Parent is not { } versionDirectory
            || versionDirectory.Parent is not { } packDirectory)
        {
            return new Result(null, $"Unexpected reference pack layout at '{refAssemblyPath}'.");
        }

        if (!FamilyByRefPack.TryGetValue(packDirectory.Name, out string? family))
        {
            return new Result(
                null,
                $"Reference pack '{packDirectory.Name}' publishes no known shared framework.");
        }

        if (!NuGetVersion.TryParse(version, out NuGetVersion? packVersion))
        {
            return new Result(
                null,
                $"'{packDirectory.Name}' has an unparsable installed version '{version}'.");
        }

        var target = new PlatformPruneTarget(family, targetFramework, packVersion);
        string overrides = Path.Combine(versionDirectory.FullName, "data", "PackageOverrides.txt");
        if (!File.Exists(overrides))
        {
            // The pack ships no prune data, so this target subsumes nothing. That is an answer.
            return new Result(PlatformPruneInventory.None(targetFramework), null);
        }

        try
        {
            return new Result(
                PlatformPruneInventory.FromExactFamily(target, File.ReadLines(overrides)),
                null);
        }
        catch (Exception exception) when (exception is FormatException or IOException)
        {
            return new Result(null, $"Could not read '{overrides}': {exception.Message}");
        }
    }
}
