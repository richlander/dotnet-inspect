using System.Xml.Linq;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Lightweight nuspec parser that extracts only dependency groups and author.
/// Used by DependencyResolutionService for transitive resolution without
/// depending on the full NuspecParser in the app layer.
/// </summary>
public static class NuspecDependencyParser
{
    /// <summary>
    /// Parses dependency groups and author from a nuspec file.
    /// </summary>
    public static (List<DependencyGroup>? DependencyGroups, string? Author) Parse(string nuspecPath)
    {
        XDocument doc = XDocument.Load(nuspecPath);
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var metadata = doc.Root?.Element(ns + "metadata");
        if (metadata == null) return (null, null);

        string? author = metadata.Element(ns + "authors")?.Value;
        List<DependencyGroup>? dependencyGroups = null;

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
                    dependencyGroups ??= [];
                    dependencyGroups.Add(depGroup);
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
                dependencyGroups ??= [];
                dependencyGroups.Add(depGroup);
            }
        }

        return (dependencyGroups, author);
    }
}
