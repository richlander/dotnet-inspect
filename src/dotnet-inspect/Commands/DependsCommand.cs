using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Commands;

/// <summary>
/// Walks dependency graphs upward: type hierarchies, library references, or package dependencies.
/// </summary>
public class DependsCommand
{
    /// <summary>
    /// Returned when the target type was not found. The caller can fall back to library mode.
    /// </summary>
    internal const int TypeNotFoundExitCode = 2;

    public static async Task<int> ExecuteTypeDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            var result = await DependencyGraphService.BuildTypeDependencyTreeAsync(
                context.HttpClient, options, logger);

            if (!result.Found)
            {
                return TypeNotFoundExitCode;
            }

            if (result.Tree.Count == 0)
            {
                Console.Error.WriteLine($"Type '{result.MatchedType}' has no type dependencies beyond System.Object.");
                return 0;
            }

            if (options.JsonOutput)
            {
                JsonOutputHelper.Write(result.Tree,
                    DependsJsonContext.Default.ListTypeDependencyNode,
                    DependsCompactJsonContext.Default.ListTypeDependencyNode,
                    options.CompactJson);
            }
            else
            {
                var rootName = options.TargetType.Contains('<') ? options.TargetType : result.MatchedType!;
                var treeNodes = ToTreeNodes(result.Tree);

                if (options.MermaidOutput)
                {
                    WriteMermaidTree(rootName, treeNodes);
                }
                else if (options.EmbeddedMermaid)
                {
                    WriteEmbeddedMermaidTree(rootName, treeNodes);
                }
                else
                {
                    var view = new PackageDependenciesView
                    {
                        Title = rootName,
                        Dependencies = treeNodes
                    };
                    WriteMarkdown(view, options.Rows);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ExecuteLibraryDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            var libraryName = options.LibraryName!;
            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                context.HttpClient, libraryName, options.SourceOptions, logger);
            if (result is LibraryDependencyGraphResult.Error error)
            {
                Console.Error.WriteLine($"Error: {error.Message}");
                if (error.HintInput != null)
                    NamespacePrefixHints.WriteIfLikelyNamespacePrefix(error.HintInput);
                return 1;
            }
            if (result is LibraryDependencyGraphResult.Empty empty)
            {
                Console.Error.WriteLine($"No assembly references found in '{empty.AssemblyName}'.");
                return 0;
            }

            var graph = (LibraryDependencyGraphResult.Graph)result;
            var treeNodes = BuildNestedDependencyTree(graph.References);

            if (options.MermaidOutput)
            {
                WriteMermaidTree(graph.AssemblyName, treeNodes);
            }
            else if (options.EmbeddedMermaid)
            {
                WriteEmbeddedMermaidTree(graph.AssemblyName, treeNodes);
            }
            else
            {
                var view = new PackageDependenciesView
                {
                    Title = graph.AssemblyName,
                    Dependencies = treeNodes
                };
                WriteMarkdown(view, options.Rows);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ExecutePackageDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            var packageRef = options.PackageName!;
            var result = await DependencyGraphService.BuildPackageDependencyTreeAsync(
                context.HttpClient, packageRef, options.Tfm, options.SourceOptions, logger);
            if (result is PackageDependencyGraphResult.Error error)
            {
                Console.Error.WriteLine($"Error: {error.Message}");
                if (error.Detail != null)
                    Console.Error.WriteLine(error.Detail);
                return 1;
            }
            if (result is PackageDependencyGraphResult.Empty empty)
            {
                Console.Error.WriteLine(empty.Message);
                return 0;
            }

            var graph = (PackageDependencyGraphResult.Graph)result;
            var treeNodes = ToDependencyTreeNodes(graph.Dependencies);

            if (options.MermaidOutput)
            {
                WriteMermaidTree(graph.Title, treeNodes);
            }
            else if (options.EmbeddedMermaid)
            {
                WriteEmbeddedMermaidTree(graph.Title, treeNodes);
            }
            else
            {
                var view = new PackageDependenciesView
                {
                    Title = graph.Title,
                    Dependencies = treeNodes
                };
                WriteMarkdown(view, options.Rows);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static List<TreeNode> ToTreeNodes(List<TypeDependencyNode> nodes)
    {
        return nodes.Select(n =>
            n.Children.Count > 0
                ? new TreeNode(n.TypeName) { Children = ToTreeNodes(n.Children) }
                : new TreeNode(n.TypeName)
        ).ToList();
    }

    private static List<TreeNode> ToDependencyTreeNodes(List<DependencyNode> nodes)
    {
        return nodes.Select(n =>
        {
            var label = !string.IsNullOrEmpty(n.Author)
                ? $"{n.PackageId} {n.Version} [{n.Author}]"
                : $"{n.PackageId} {n.Version}";
            return n.Children.Count > 0
                ? new TreeNode(label) { Children = ToDependencyTreeNodes(n.Children) }
                : new TreeNode(label);
        }).ToList();
    }

    private static List<TreeNode> BuildNestedDependencyTree(List<AssemblyReferenceNode> nodes)
    {
        List<TreeNode> result = [];
        int i = 0;
        BuildNestedNodes(nodes, ref i, 0, result);
        return result;
    }

    private static void BuildNestedNodes(List<AssemblyReferenceNode> nodes, ref int index, int currentDepth, List<TreeNode> target)
    {
        while (index < nodes.Count && nodes[index].Depth == currentDepth)
        {
            var node = nodes[index];
            var label = !string.IsNullOrEmpty(node.Company)
                ? $"{node.Name} {node.Version} [{node.Company}]"
                : $"{node.Name} {node.Version}";
            index++;

            List<TreeNode> children = [];
            if (index < nodes.Count && nodes[index].Depth > currentDepth)
            {
                BuildNestedNodes(nodes, ref index, currentDepth + 1, children);
            }

            target.Add(children.Count > 0 ? new TreeNode(label) { Children = children } : new TreeNode(label));
        }
    }

    /// <summary>
    /// Writes standalone mermaid output using the MermaidFormatter.
    /// </summary>
    private static void WriteMermaidTree(string title, List<TreeNode> treeNodes)
    {
        var writer = MarkoutWriter.Create(Console.Out, new MermaidFormatter());
        writer.WriteHeading(1, title);
        writer.WriteTree([.. treeNodes]);
        writer.Flush();
    }

    /// <summary>
    /// Writes mermaid embedded in a markdown document (```mermaid code block).
    /// </summary>
    private static void WriteEmbeddedMermaidTree(string title, List<TreeNode> treeNodes)
    {
        var mdWriter = MarkoutWriter.Create(Console.Out, new MarkdownFormatter());
        mdWriter.WriteHeading(1, title);

        // Render the mermaid content to a string
        var mermaidWriter = MarkoutWriter.Create(new MermaidFormatter());
        mermaidWriter.WriteTree([.. treeNodes]);
        var mermaidContent = mermaidWriter.ToString();

        mdWriter.WriteCodeStart("mermaid");
        Console.Out.Write(mermaidContent);
        if (!mermaidContent.EndsWith('\n'))
            Console.Out.WriteLine();
        mdWriter.WriteCodeEnd();
        mdWriter.Flush();
    }

    private static void WriteMarkdown(PackageDependenciesView view, int? rows)
    {
        var markdown = MarkoutSerializer.Serialize(view, PackageDependenciesContext.Default).TrimEnd();
        Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, rows));
    }
}
