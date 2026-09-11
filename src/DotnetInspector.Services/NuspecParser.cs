using System.Xml.Linq;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Services;

/// <summary>
/// Parses .nuspec files to extract package metadata.
/// </summary>
public static class NuspecParser
{
    /// <summary>
    /// Parses all metadata from a nuspec file.
    /// </summary>
    public static NuspecData Parse(string nuspecPath)
    {
        try
        {
            return ParseDocument(HardenedXml.LoadXDocument(nuspecPath));
        }
        catch (System.Xml.XmlException ex)
        {
            throw NuspecParseException.From(ex);
        }
    }

    /// <summary>
    /// Finds the first nuspec file directly under a package directory.
    /// </summary>
    public static string? FindNuspec(string packageDir)
    {
        var nuspecFiles = Directory.GetFiles(packageDir, "*.nuspec", SearchOption.TopDirectoryOnly);
        return nuspecFiles.Length == 0 ? null : nuspecFiles[0];
    }

    /// <summary>
    /// Finds and parses the first nuspec file directly under a package directory.
    /// </summary>
    public static NuspecData? FindAndParse(string packageDir)
    {
        var nuspecPath = FindNuspec(packageDir);
        return nuspecPath == null ? null : Parse(nuspecPath);
    }

    /// <summary>
    /// Parses nuspec metadata from raw XML content (e.g. a nuspec fetched directly from a feed
    /// without downloading the full package).
    /// </summary>
    public static NuspecData ParseContent(string nuspecXml)
    {
        try
        {
            if (nuspecXml.Length > 0 && nuspecXml[0] == '\uFEFF')
                nuspecXml = nuspecXml[1..];

            return ParseDocument(HardenedXml.ParseXDocument(nuspecXml));
        }
        catch (System.Xml.XmlException ex)
        {
            throw NuspecParseException.From(ex);
        }
    }

    /// <summary>
    /// Parses package metadata from a bounded stream with DTD processing prohibited and an
    /// explicit decoded-character budget.
    /// </summary>
    public static NuspecData Parse(
        Stream nuspecStream,
        long maxCharactersInDocument)
    {
        ArgumentNullException.ThrowIfNull(nuspecStream);
        try
        {
            return ParseDocument(
                HardenedXml.LoadXDocument(
                    nuspecStream,
                    maxCharactersInDocument));
        }
        catch (System.Xml.XmlException ex)
        {
            throw NuspecParseException.From(ex);
        }
    }

    private static NuspecData ParseDocument(XDocument doc)
    {
        var result = new NuspecData();

        XElement root = doc.Root
            ?? throw new InvalidDataException(
                "The package manifest is missing its document root.");
        if (!IsPackageRoot(root))
        {
            throw new InvalidDataException(
                "The package manifest has an invalid document root.");
        }

        XElement[] metadataCandidates =
        [
            .. root.Elements().Where(element =>
                element.Name.LocalName.Equals(
                    "metadata",
                    StringComparison.Ordinal)),
        ];
        XElement[] nuspecMetadataCandidates =
        [
            .. metadataCandidates.Where(element =>
                IsNuspecNamespace(element.Name.Namespace)),
        ];
        XElement[] metadataElements =
        [
            .. nuspecMetadataCandidates.Where(element =>
                IsCompatibleMetadataNamespace(
                    root.Name.Namespace,
                    element.Name.Namespace)),
        ];
        if (nuspecMetadataCandidates.Length != metadataElements.Length)
        {
            throw new InvalidDataException(
                "The package manifest metadata namespace does not match its document root.");
        }

        if (metadataElements.Length > 1)
        {
            throw new InvalidDataException(
                "The package manifest contains multiple metadata elements.");
        }

        XElement? metadata = metadataElements.SingleOrDefault();
        XNamespace ns = metadata?.Name.Namespace ?? root.Name.Namespace;
        result.ManifestVersion = GetManifestVersion(ns);

        if (metadata == null) return result;

        result.PackageName = metadata.Element(ns + "id")?.Value;
        result.Version = metadata.Element(ns + "version")?.Value;
        result.Description = metadata.Element(ns + "description")?.Value is { } description
            ? new InertString(TextPolicy.Prose, description)
            : null;
        result.Authors = metadata.Element(ns + "authors")?.Value;
        var repositoryElement = metadata.Element(ns + "repository");
        result.Repository = repositoryElement?.Attribute("url")?.Value;
        result.RepositoryType = repositoryElement?.Attribute("type")?.Value;
        result.RepositoryCommit = repositoryElement?.Attribute("commit")?.Value;

        // Parse license (prefer expression over file or URL)
        var licenseElement = metadata.Element(ns + "license");
        if (licenseElement != null)
        {
            string? licenseType = licenseElement.Attribute("type")?.Value;
            if (licenseType?.Equals("expression", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.License = licenseElement.Value;
            }
            else if (licenseType?.Equals("file", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.License = $"(file: {licenseElement.Value})";
            }
        }
        // Fallback to deprecated licenseUrl if no license element
        if (string.IsNullOrEmpty(result.License))
        {
            var licenseUrl = metadata.Element(ns + "licenseUrl")?.Value;
            result.LicenseUrl = licenseUrl;
            if (!string.IsNullOrEmpty(licenseUrl) && !licenseUrl.Contains("LICENSE"))
            {
                if (licenseUrl.StartsWith("https://licenses.nuget.org/"))
                {
                    result.License = licenseUrl.Replace("https://licenses.nuget.org/", "");
                }
            }
        }
        else
        {
            result.LicenseUrl = metadata.Element(ns + "licenseUrl")?.Value;
        }

        // Parse package types
        var packageTypes = metadata.Element(ns + "packageTypes");
        if (packageTypes != null)
        {
            foreach (var pt in packageTypes.Elements(ns + "packageType"))
            {
                string? typeName = pt.Attribute("name")?.Value;
                if (typeName != null)
                {
                    result.PackageTypes ??= [];
                    result.PackageTypes.Add(typeName);
                    if (typeName.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsToolPackage = true;
                    }
                }
            }
        }

        // Parse embedded and legacy presentation assets.
        result.IconFile = metadata.Element(ns + "icon")?.Value;
        result.IconUrl = metadata.Element(ns + "iconUrl")?.Value;
        result.ReadmeFile = metadata.Element(ns + "readme")?.Value;

        // Parse dependencies
        var dependencies = metadata.Element(ns + "dependencies");
        if (dependencies != null)
        {
            DependencyGroup? ungrouped = null;
            foreach (var element in dependencies.Elements())
            {
                if (element.Name == ns + "group")
                {
                    string? tfm = element.Attribute("targetFramework")?.Value;
                    var group = new DependencyGroup
                    {
                        TargetFramework = tfm ?? "any",
                    };
                    foreach (var dependency in element.Elements(
                        ns + "dependency"))
                    {
                        group.Dependencies.Add(ParseDependency(dependency));
                    }

                    result.DependencyGroups ??= [];
                    result.DependencyGroups.Add(group);
                    ungrouped = null;
                }
                else if (element.Name == ns + "dependency")
                {
                    if (ungrouped is null)
                    {
                        ungrouped = new DependencyGroup
                        {
                            TargetFramework = "any",
                            IsImplicitManifestGroup = true,
                        };
                        result.DependencyGroups ??= [];
                        result.DependencyGroups.Add(ungrouped);
                    }

                    ungrouped.Dependencies.Add(ParseDependency(element));
                }
            }
        }

        return result;
    }

    private static PackageDependency ParseDependency(XElement dependency)
        => new()
        {
            Id = dependency.Attribute("id")?.Value ?? "",
            Version = dependency.Attribute("version")?.Value ?? "",
        };

    private static string GetManifestVersion(XNamespace ns)
    {
        var uri = ns.NamespaceName;
        if (string.IsNullOrWhiteSpace(uri))
            return "nuspec";

        const string prefix = "http://schemas.microsoft.com/packaging/";
        const string suffix = "/nuspec.xsd";
        return uri[prefix.Length..^suffix.Length];
    }

    internal static bool IsPackageRoot(XElement root) =>
        root.Name.LocalName.Equals("package", StringComparison.Ordinal)
        && IsNuspecNamespace(root.Name.Namespace);

    private static bool IsNuspecNamespace(XNamespace ns)
    {
        var uri = ns.NamespaceName;
        if (uri.Length == 0)
            return true;

        const string prefix = "http://schemas.microsoft.com/packaging/";
        const string suffix = "/nuspec.xsd";
        return uri.Length > prefix.Length + suffix.Length
            && uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && uri.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompatibleMetadataNamespace(
        XNamespace rootNamespace,
        XNamespace metadataNamespace) =>
        string.IsNullOrEmpty(rootNamespace.NamespaceName)
            || rootNamespace == metadataNamespace;
}
