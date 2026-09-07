using System.Collections.Immutable;
using System.Text.Json;
using System.Xml;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

public sealed partial class AssemblyDependencyResolver
{
    /// <summary>
    /// Acquires every DLL entry emitted by the enabled discovery tiers, without
    /// filename coalescing or target-name exclusion. This does not perform binding
    /// or enumerate request-driven installed-platform fallback.
    /// </summary>
    public AssemblyDependencyDiscoveryResult CaptureDiscoveryInventory(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string targetPath = Path.GetFullPath(_options.TargetAssemblyPath);
        var entries = ImmutableArray.CreateBuilder<AssemblyDependencyDiscoveryEntry>();
        var failures = ImmutableArray.CreateBuilder<AssemblyDependencyDiscoveryFailure>();
        bool acquisitionFailed = false;

        CollectDependencies(
            deduplicate: false,
            capture: dependency =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provenance = ResolutionProvenance(dependency);
                AssemblyDependencyAcquisition acquisition =
                    DescriptorResult(dependency.Path, provenance).Acquisition;
                entries.Add(new(
                    dependency,
                    provenance,
                    string.Equals(
                        dependency.Path,
                        targetPath,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal),
                    acquisition));
                acquisitionFailed |= acquisition is
                    AssemblyDependencyAcquisition.Rejected
                    or AssemblyDependencyAcquisition.Unavailable;
            },
            discoveryFailure: failures.Add,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return acquisitionFailed || failures.Count != 0
            ? new AssemblyDependencyDiscoveryResult.Failed(
                Version, entries.ToImmutable(), failures.ToImmutable())
            : new AssemblyDependencyDiscoveryResult.Captured(
                Version, entries.ToImmutable());
    }

    static bool DiscoveryFileExists(string path, bool strict)
    {
        if (!strict)
            return File.Exists(path);
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    static bool DiscoveryDirectoryExists(string path, bool strict)
    {
        if (!strict)
            return Directory.Exists(path);
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    static bool IsDiscoveryFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException
            or JsonException or XmlException;

    static AssemblyDependencyDiscoveryFailure DiscoveryFailure(
        AssemblyDependencyProvenance tier, string? location, Exception exception) =>
        new(tier, location, exception is JsonException or XmlException
            ? AssemblyDependencyDiscoveryFailureKind.InvalidDocument
            : AssemblyDependencyDiscoveryFailureKind.Unreadable);
}
