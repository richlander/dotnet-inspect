using System.Text.Json;
using DotnetInspector.Packages;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Parses .deps.json files for runtime dependencies.
/// </summary>
public static class DepsJsonParser
{
    public static void Parse(string depsPath, InspectionResult result)
    {
        try
        {
            string json = File.ReadAllText(depsPath);
            using var doc = JsonDocument.Parse(json);

            // Get runtime target
            if (doc.RootElement.TryGetProperty("runtimeTarget", out var runtimeTarget))
            {
                if (runtimeTarget.TryGetProperty("name", out var name))
                {
                    string targetName = name.GetString() ?? "";
                    // Format: .NETCoreApp,Version=v8.0/win-x64 or .NETCoreApp,Version=v8.0
                    if (targetName.Contains('/'))
                    {
                        string rid = targetName.Split('/')[1];
                        result.RuntimeTargetRid = rid;
                    }
                }
            }

            // Get runtime dependencies
            if (doc.RootElement.TryGetProperty("libraries", out var libraries))
            {
                foreach (var lib in libraries.EnumerateObject())
                {
                    string[] parts = lib.Name.Split('/');
                    if (parts.Length == 2)
                    {
                        if (lib.Value.TryGetProperty("type", out var typeElem))
                        {
                            string type = typeElem.GetString() ?? "";
                            if (type == "package")
                            {
                                result.RuntimeDependencies ??= [];
                                result.RuntimeDependencies.Add(new PackageDependency
                                {
                                    Id = parts[0],
                                    Version = parts[1]
                                });
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }
}
