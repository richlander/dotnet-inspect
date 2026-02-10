using DotnetInspector.Options;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Data passed from PlatformCommand to the formatter for assembly listing.
/// </summary>
public record FrameworkAssemblyData(string FrameworkName, string Version, List<AssemblyEntry> Assemblies);

/// <summary>
/// A single assembly entry with an optional public type count.
/// </summary>
public record AssemblyEntry(string Name, int? PublicTypeCount = null);

/// <summary>
/// Formats platform command results for display.
/// </summary>
public static class PlatformOutputFormatter
{
    public static string FormatFrameworks(List<FrameworkInfo> frameworks, Verbosity verbosity, string packsDir)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, "Installed Frameworks");

        if (verbosity == Verbosity.Detailed)
        {
            var latestVersion = frameworks.Max(f => f.LatestVersion) ?? "";
            var majorVersion = latestVersion.Contains('.') ? latestVersion[..latestVersion.IndexOf('.')] + ".0" : latestVersion;
            var majorVersions = frameworks
                .SelectMany(f => f.AllVersions)
                .Select(v => v.Contains('.') ? v[..v.IndexOf('.')] : v)
                .Distinct()
                .Count();
            var dotnetRoot = Path.GetDirectoryName(packsDir) ?? packsDir;

            writer.WriteCompactFields(
                new MarkoutField("Latest", majorVersion),
                new MarkoutField("Patch", latestVersion),
                new MarkoutField("Majors", majorVersions.ToString()),
                new MarkoutField("Runtimes", frameworks.Count.ToString()),
                new MarkoutField("Location", dotnetRoot));
        }

        var headers = new[] { "Framework", "Version", "Libraries" };
        var rows = frameworks.Select(f => new[] { f.ShortName, f.LatestVersion, f.AssemblyCount.ToString() });
        writer.WriteTable(headers, rows);

        return writer.ToString();
    }

    public static string FormatVersions(List<FrameworkInfo> frameworks, int? limit)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, "Installed Versions");

        foreach (var framework in frameworks)
        {
            writer.WriteHeading(2, framework.ShortName);

            var versions = framework.AllVersions;
            if (limit.HasValue)
            {
                versions = versions.Take(limit.Value).ToList();
            }

            writer.WriteArray(versions);

            if (limit.HasValue && framework.AllVersions.Count > limit.Value)
            {
                writer.WriteParagraph($"... *and {framework.AllVersions.Count - limit.Value} more*");
            }
        }

        return writer.ToString();
    }

    public static string FormatAssemblies(List<FrameworkAssemblyData> frameworkData, bool includeTypes,
        int? limit, string packsDir, bool multipleFrameworks)
    {
        var writer = new MarkoutWriter();

        if (multipleFrameworks)
        {
            writer.WriteHeading(1, "Platform Libraries");
            writer.WriteField("Packs Directory", packsDir);
        }

        foreach (var data in frameworkData)
        {
            writer.WriteHeading(2, $"{data.FrameworkName} ({data.Version})");

            var displayAssemblies = data.Assemblies.AsEnumerable();
            if (limit.HasValue)
            {
                displayAssemblies = displayAssemblies.Take(limit.Value);
            }

            if (includeTypes)
            {
                var headers = new[] { "Library", "Types" };
                var rows = displayAssemblies.Select(a => new[] { a.Name, (a.PublicTypeCount ?? 0).ToString() });
                writer.WriteTable(headers, rows);
            }
            else
            {
                var headers = new[] { "Library" };
                var rows = displayAssemblies.Select(a => new[] { a.Name });
                writer.WriteTable(headers, rows);
            }

            if (limit.HasValue && data.Assemblies.Count > limit.Value)
            {
                writer.WriteParagraph($"... *and {data.Assemblies.Count - limit.Value} more libraries*");
            }
        }

        return writer.ToString();
    }
}
