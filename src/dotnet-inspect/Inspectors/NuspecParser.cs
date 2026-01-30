using System.Xml.Linq;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Parses .nuspec files to extract package metadata.
/// </summary>
public static class NuspecParser
{
    public static void Parse(string nuspecPath, InspectionResult result)
    {
        XDocument doc = XDocument.Load(nuspecPath);
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var metadata = doc.Root?.Element(ns + "metadata");
        if (metadata == null) return;

        result.PackageName = metadata.Element(ns + "id")?.Value ?? result.PackageName;
        result.Version = metadata.Element(ns + "version")?.Value ?? result.Version;
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
                // Extract expression from nuget.org license URLs like https://licenses.nuget.org/MIT
                if (licenseUrl.StartsWith("https://licenses.nuget.org/"))
                {
                    result.License = licenseUrl.Replace("https://licenses.nuget.org/", "");
                }
            }
        }

        // Check if it's marked as a tool
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
    }
}
