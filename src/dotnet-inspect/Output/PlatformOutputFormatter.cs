using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using Markout;

namespace DotnetInspector.Output;

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

        var headers = new[] { "Framework", "Version", "Assemblies" };
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

    public static string FormatAssemblies(List<FrameworkInfo> frameworks, string? requestedVersion,
        bool includeTypes, int? limit, string packsDir, bool multipleFrameworks,
        Func<string, int> countPublicTypes, VerboseLogger logger)
    {
        var writer = new MarkoutWriter();

        if (multipleFrameworks)
        {
            writer.WriteHeading(1, "Platform Assemblies");
            writer.WriteField("Packs Directory", packsDir);
        }

        foreach (var framework in frameworks)
        {
            var version = requestedVersion ?? framework.LatestVersion;
            var refPath = PlatformResolver.GetRefAssemblyPath(framework.Path, version);

            if (refPath == null)
            {
                logger.Log($"Could not find ref path for {framework.ShortName} {version}");
                continue;
            }

            var assemblies = PlatformResolver.GetAssemblies(refPath);

            writer.WriteHeading(2, $"{framework.ShortName} ({version})");

            var displayAssemblies = assemblies.AsEnumerable();
            if (limit.HasValue)
            {
                displayAssemblies = displayAssemblies.Take(limit.Value);
            }

            if (includeTypes)
            {
                var headers = new[] { "Assembly", "Types" };
                var rows = displayAssemblies.Select(a => new[] { a.Name, countPublicTypes(a.Path).ToString() });
                writer.WriteTable(headers, rows);
            }
            else
            {
                var headers = new[] { "Assembly" };
                var rows = displayAssemblies.Select(a => new[] { a.Name });
                writer.WriteTable(headers, rows);
            }

            if (limit.HasValue && assemblies.Count > limit.Value)
            {
                writer.WriteParagraph($"... *and {assemblies.Count - limit.Value} more assemblies*");
            }
        }

        return writer.ToString();
    }
}
