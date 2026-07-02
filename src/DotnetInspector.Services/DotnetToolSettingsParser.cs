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

        foreach (var level1 in Directory.GetDirectories(toolsDir))
        {
            path = Path.Combine(level1, SettingsFileName);
            if (File.Exists(path))
                return path;

            foreach (var level2 in Directory.GetDirectories(level1))
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
            return ParseDocument(HardenedXml.ParseXDocument(xml));
        }
        catch
        {
            return null;
        }
    }

    private static DotnetToolSettingsData? ParseDocument(XDocument doc)
    {
        var root = doc.Root;
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
