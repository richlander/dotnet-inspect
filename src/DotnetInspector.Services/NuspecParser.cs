using System.Xml.Linq;
using DotnetInspector.Packages;

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
        var result = new NuspecData();

        XDocument doc = XDocument.Load(nuspecPath);
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var metadata = doc.Root?.Element(ns + "metadata");
        if (metadata == null) return result;

        result.PackageName = metadata.Element(ns + "id")?.Value;
        result.Version = metadata.Element(ns + "version")?.Value;
        result.Description = metadata.Element(ns + "description")?.Value;
        result.Authors = metadata.Element(ns + "authors")?.Value;
        result.Repository = metadata.Element(ns + "repository")?.Attribute("url")?.Value;

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
            if (!string.IsNullOrEmpty(licenseUrl) && !licenseUrl.Contains("LICENSE"))
            {
                if (licenseUrl.StartsWith("https://licenses.nuget.org/"))
                {
                    result.License = licenseUrl.Replace("https://licenses.nuget.org/", "");
                }
            }
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

        // Parse readme file path
        result.ReadmeFile = metadata.Element(ns + "readme")?.Value;

        // Parse dependencies
        var dependencies = metadata.Element(ns + "dependencies");
        if (dependencies != null)
        {
            foreach (var group in dependencies.Elements(ns + "group"))
            {
                string? tfm = group.Attribute("targetFramework")?.Value;
                var depGroup = new DependencyGroup { TargetFramework = tfm ?? "any" };

                foreach (var dep in group.Elements(ns + "dependency"))
                {
                    depGroup.Dependencies.Add(new PackageDependency
                    {
                        Id = dep.Attribute("id")?.Value ?? "",
                        Version = dep.Attribute("version")?.Value ?? ""
                    });
                }

                if (depGroup.Dependencies.Count > 0)
                {
                    result.DependencyGroups ??= [];
                    result.DependencyGroups.Add(depGroup);
                }
            }

            // Handle dependencies without groups
            var ungroupedDeps = dependencies.Elements(ns + "dependency").ToList();
            if (ungroupedDeps.Count > 0)
            {
                var depGroup = new DependencyGroup { TargetFramework = "any" };
                foreach (var dep in ungroupedDeps)
                {
                    depGroup.Dependencies.Add(new PackageDependency
                    {
                        Id = dep.Attribute("id")?.Value ?? "",
                        Version = dep.Attribute("version")?.Value ?? ""
                    });
                }
                result.DependencyGroups ??= [];
                result.DependencyGroups.Add(depGroup);
            }
        }

        return result;
    }
}
