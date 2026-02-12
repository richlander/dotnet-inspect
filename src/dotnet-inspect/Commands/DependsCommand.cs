using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Walks dependency graphs upward: type hierarchies, library references, or package dependencies.
/// </summary>
public class DependsCommand
{
    public static async Task<int> ExecuteTypeDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        List<string> tempDirs = [];

        try
        {
            // Safety fallback — apply curated scope if nothing specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to curated scope");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames,
                    Packages = [.. options.Packages, .. CommandLineBuilder.CuratedScopePackages]
                };
            }

            // Collect all assembly paths from various sources
            var assemblyInfos = await AssemblyCollector.CollectAsync(
                context.HttpClient, options, tempDirs, logger, "inspect-depends");

            logger.Log($"Scanning {assemblyInfos.Count} libraries for type {options.TargetType}");

            var assemblyPaths = assemblyInfos.Select(a => a.Path).ToList();
            var tree = TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);

            if (tree.Count == 0)
            {
                Console.Error.WriteLine($"Type '{options.TargetType}' not found in the specified scope.");
                return 1;
            }

            if (options.JsonOutput)
            {
                JsonOutputHelper.Write(tree,
                    DependsJsonContext.Default.ListTypeDependencyNode,
                    DependsCompactJsonContext.Default.ListTypeDependencyNode,
                    options.CompactJson);
            }
            else
            {
                var view = new TypeDependenciesView
                {
                    Title = options.TargetType,
                    Dependencies = ToTreeNodes(tree)
                };
                MarkoutSerializer.Serialize(view, Console.Out, TypeDependenciesContext.Default);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    private static List<TreeNode> ToTreeNodes(List<TypeDependencyNode> nodes)
    {
        return nodes.Select(n =>
            n.Children.Count > 0
                ? new TreeNode(n.TypeName, ToTreeNodes(n.Children))
                : new TreeNode(n.TypeName)
        ).ToList();
    }
}
