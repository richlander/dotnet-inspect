using System.Xml.Linq;
using DotnetInspector.Core;

namespace DotnetInspector.Services;

/// <summary>
/// Locates and parses a NuGet tool package's <c>DotnetToolSettings.xml</c> manifest.
/// Sibling to <see cref="NuspecParser"/> / <c>DepsJsonParser</c>: the CLI orchestration
/// layer maps the returned data onto its own inspection models.
/// </summary>
public static class DotnetToolSettingsParser
{
    private const string SettingsFileName = "DotnetToolSettings.xml";

    /// <summary>
    /// Searches for <c>DotnetToolSettings.xml</c> at the root of <paramref name="toolsDir"/>
    /// or up to two levels deep (<c>tools/</c>, <c>tools/{tfm}/</c>, <c>tools/{tfm}/{rid}/</c>).
    /// Returns the manifest path, or <see langword="null"/> if none is found.
    /// </summary>
    public static string? FindSettings(string toolsDir)
    {
        var path = Path.Combine(toolsDir, SettingsFileName);
        if (File.Exists(path))
            return path;

        foreach (var level1 in Directory.GetDirectories(toolsDir)
                     .Order(StringComparer.Ordinal))
        {
            path = Path.Combine(level1, SettingsFileName);
            if (File.Exists(path))
                return path;

            foreach (var level2 in Directory.GetDirectories(level1)
                         .Order(StringComparer.Ordinal))
            {
                path = Path.Combine(level2, SettingsFileName);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Locates and parses the manifest under <paramref name="toolsDir"/>, or
    /// <see langword="null"/> if none is found or it cannot be parsed.
    /// </summary>
    public static DotnetToolSettingsData? FindAndParse(string toolsDir)
    {
        var settingsFile = FindSettings(toolsDir);
        return settingsFile == null ? null : Parse(settingsFile);
    }

    /// <summary>
    /// Locates and projects every candidate manifest. Returns <see langword="true"/>
    /// for absence or one complete, agreeing projection; otherwise returns
    /// <see langword="false"/> while preserving the first deterministic projection
    /// in <paramref name="data"/> when one was readable.
    /// </summary>
    public static bool TryProject(
        string toolsDir,
        out DotnetToolSettingsData? data)
    {
        data = null;
        try
        {
            string[] candidates = FindSettingsCandidates(toolsDir);
            if (candidates.Length == 0)
                return true;

            DotnetToolSettingsData? selected = Parse(candidates[0]);
            if (selected is null)
                return false;

            for (int i = 1; i < candidates.Length; i++)
            {
                DotnetToolSettingsData? candidate = Parse(candidates[i]);
                if (!Equivalent(selected, candidate))
                {
                    data = selected;
                    return false;
                }
            }

            data = selected;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or System.Xml.XmlException
                or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a <c>DotnetToolSettings.xml</c> file, or <see langword="null"/> on error or an
    /// unrecognized manifest version.
    /// </summary>
    public static DotnetToolSettingsData? Parse(string settingsFile)
    {
        try
        {
            return ParseDocument(HardenedXml.LoadXDocument(settingsFile));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses <c>DotnetToolSettings.xml</c> from raw XML content, or <see langword="null"/> on
    /// error or an unrecognized manifest version.
    /// </summary>
    public static DotnetToolSettingsData? ParseContent(string xml)
    {
        try
        {
            return ParseContentOrThrow(xml);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses <c>DotnetToolSettings.xml</c> from raw XML content, or
    /// <see langword="null"/> for an unsupported document.
    /// </summary>
    /// <exception cref="System.Xml.XmlException">
    /// The content is not a well-formed XML document.
    /// </exception>
    public static DotnetToolSettingsData? ParseContentOrThrow(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return ParseDocument(HardenedXml.ParseXDocument(xml));
    }

    private static DotnetToolSettingsData? ParseDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root?.Name != "DotNetCliTool")
            return null;

        var version = root?.Attribute("Version")?.Value;

        return version switch
        {
            "2" => new DotnetToolSettingsData(
                version,
                "DotNetCliTool Version=\"2\" (RID-specific)",
                IsRidSpecificPointerPackage: true,
                ParseCommands(root),
                ParseRidPackages(root)),
            "1" or null => new DotnetToolSettingsData(
                version,
                "DotNetCliTool Version=\"1\" (portable)",
                IsRidSpecificPointerPackage: false,
                ParseCommands(root),
                RuntimeIdentifierPackages: null),
            _ => null,
        };
    }

    private static string[] FindSettingsCandidates(string toolsDir)
    {
        var candidates = new List<string>();
        string root = Path.Combine(toolsDir, SettingsFileName);
        if (File.Exists(root))
            candidates.Add(root);

        foreach (string level1 in Directory.GetDirectories(toolsDir)
                     .Order(StringComparer.Ordinal))
        {
            string path = Path.Combine(level1, SettingsFileName);
            if (File.Exists(path))
                candidates.Add(path);

            foreach (string level2 in Directory.GetDirectories(level1)
                         .Order(StringComparer.Ordinal))
            {
                path = Path.Combine(level2, SettingsFileName);
                if (File.Exists(path))
                    candidates.Add(path);
            }
        }

        return [.. candidates];
    }

    private static bool Equivalent(
        DotnetToolSettingsData left,
        DotnetToolSettingsData? right)
        => right is not null
            && left.Version == right.Version
            && left.ToolFormat == right.ToolFormat
            && left.IsRidSpecificPointerPackage
                == right.IsRidSpecificPointerPackage
            && SequenceEqual(left.Commands, right.Commands)
            && SequenceEqual(
                left.RuntimeIdentifierPackages,
                right.RuntimeIdentifierPackages);

    private static bool SequenceEqual<T>(
        IReadOnlyList<T>? left,
        IReadOnlyList<T>? right)
        where T : IEquatable<T>
        => left is null
            ? right is null
            : right is not null && left.SequenceEqual(right);

    private static List<string>? ParseCommands(XElement? root)
    {
        var commands = root?.Element("Commands")?.Elements("Command");
        if (commands == null)
            return null;

        return commands
            .Select(c => c.Attribute("Name")?.Value)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }

    private static List<ToolRidPackage>? ParseRidPackages(XElement? root)
    {
        var ridPackages = root?.Element("RuntimeIdentifierPackages")?.Elements("RuntimeIdentifierPackage");
        if (ridPackages == null)
            return null;

        return ridPackages
            .Select(r => new ToolRidPackage(
                r.Attribute("RuntimeIdentifier")?.Value ?? "",
                r.Attribute("Id")?.Value ?? ""))
            .ToList();
    }
}
