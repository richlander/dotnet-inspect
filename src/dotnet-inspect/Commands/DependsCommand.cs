using ILInspector.CSharp;
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

    private static string DescribeTypeDependencyRejection(
        TypeDependencyRejection rejection) =>
        rejection.Kind switch
        {
            TypeDependencyRejectionKind.UnsupportedMetadataFormat =>
                "unsupported metadata format",
            TypeDependencyRejectionKind.MalformedMetadataRoot
                when rejection.MetadataRootReason is { } reason =>
                $"malformed metadata root ({reason})",
            TypeDependencyRejectionKind.InvalidImage => "invalid image",
            _ => throw new ArgumentException(
                "The rejection is not a metadata-format failure.",
                nameof(rejection)),
        };

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

            // A rejected candidate scopes to itself, so it is reported beside
            // whatever the surviving candidates produced.
            PackageCommand.WriteLibraryInspectionFailures(
                result.Rejections.Select(
                    rejection =>
                        (
                            rejection.AssemblyPath,
                            DescribeTypeDependencyRejection(rejection))));

            if (!result.Found)
            {
                return TypeNotFoundExitCode;
            }

            if (result.Tree.Count == 0)
            {
                if (options.Count)
                {
                    WriteCount(0);
                    return 0;
                }

                CommandError.WriteLine(
                    $"Type '{ContainLabel(result.MatchedType ?? options.TargetType)}' has no type dependencies beyond System.Object.");
                return 0;
            }

            var visibleNodes = TreeRowWindow.Apply(
                result.Tree,
                options.Rows,
                node => node.Children,
                (node, children) => node with { Children = children });
            var treeNodes = ToTreeNodes(visibleNodes);
            if (options.Count)
            {
                WriteCount(
                    TreeRowWindow.Count(result.Tree, node => node.Children),
                    options.Rows);
            }
            else if (options.JsonOutput)
            {
                JsonOutputHelper.Write(visibleNodes,
                    DependsJsonContext.Default.ListTypeDependencyNode,
                    DependsCompactJsonContext.Default.ListTypeDependencyNode,
                    options.CompactJson);
            }
            else
            {
                // The root label sits at the head of the same tree, so it needs
                // the same containment as its children.
                var rootName = ContainLabel(
                    options.TargetType.Contains('<') ? options.TargetType : result.MatchedType!);

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
                    WriteMarkdown(view);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
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
                CommandError.Write($"{error.Message}");
                if (error.HintInput != null)
                    NamespacePrefixHints.WriteIfLikelyNamespacePrefix(error.HintInput);
                return 1;
            }
            if (result is LibraryDependencyGraphResult.Empty empty)
            {
                if (options.Count)
                {
                    WriteCount(0);
                    return 0;
                }

                CommandError.WriteLine($"No assembly references found in '{empty.AssemblyName}'.");
                return 0;
            }

            var graph = (LibraryDependencyGraphResult.Graph)result;
            var treeNodes = BuildNestedDependencyTree(graph.References);
            var visibleTreeNodes = TreeRowWindow.Apply(
                treeNodes,
                options.Rows,
                node => node.Children ?? [],
                (node, children) => new TreeNode(node.Text) { Children = children });

            if (options.Count)
            {
                WriteCount(graph.References.Count, options.Rows);
            }
            else if (options.MermaidOutput)
            {
                WriteMermaidTree(graph.AssemblyName, visibleTreeNodes);
            }
            else if (options.EmbeddedMermaid)
            {
                WriteEmbeddedMermaidTree(ContainLabel(graph.AssemblyName), visibleTreeNodes);
            }
            else
            {
                var view = new PackageDependenciesView
                {
                    Title = ContainLabel(graph.AssemblyName),
                    Dependencies = visibleTreeNodes
                };
                WriteMarkdown(view);
            }
            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
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
                // Detail lists the TFMs a package actually offers, which comes
                // straight out of its .nuspec. It used to go out as its own
                // unindented line, so a hostile targetFramework attribute
                // containing a line separator forged a diagnostic under it.
                CommandError.Write(error.Message, error.Detail is null ? [] : [error.Detail]);
                return 1;
            }
            if (result is PackageDependencyGraphResult.Empty empty)
            {
                if (options.Count)
                {
                    WriteCount(0);
                    return 0;
                }

                CommandError.WriteLine(empty.Message);
                return 0;
            }

            var graph = (PackageDependencyGraphResult.Graph)result;
            var visibleDependencies = TreeRowWindow.Apply(
                graph.Dependencies,
                options.Rows,
                node => node.Children,
                (node, children) => node with { Children = children });
            var treeNodes = ToDependencyTreeNodes(visibleDependencies);

            if (options.Count)
            {
                WriteCount(
                    TreeRowWindow.Count(graph.Dependencies, node => node.Children),
                    options.Rows);
            }
            else if (options.MermaidOutput)
            {
                WriteMermaidTree(ContainLabel(graph.Title), treeNodes);
            }
            else if (options.EmbeddedMermaid)
            {
                WriteEmbeddedMermaidTree(ContainLabel(graph.Title), treeNodes);
            }
            else
            {
                var view = new PackageDependenciesView
                {
                    Title = ContainLabel(graph.Title),
                    Dependencies = treeNodes
                };
                WriteMarkdown(view);
            }
            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    /// <summary>
    /// A tree label is written straight into the terminal beside a box-drawing
    /// gutter, so an ESC or a bidi override in a metadata name rewrites the
    /// shape of the tree itself (issue #3319). Containment goes here, at
    /// construction, so every renderer of the node inherits it.
    /// </summary>
    private static string ContainLabel(string label)
        => CSharpIdentifier.ContainRenderedText(label);

    private static List<TreeNode> ToTreeNodes(List<TypeDependencyNode> nodes)
    {
        return nodes.Select(n =>
            n.Children.Count > 0
                ? new TreeNode(ContainLabel(n.TypeName)) { Children = ToTreeNodes(n.Children) }
                : new TreeNode(ContainLabel(n.TypeName))
        ).ToList();
    }

    private static void WriteCount(int count, RowWindow? rows = null)
    {
        if (rows is { IsUnlimited: false } window)
        {
            var (start, end) = window.Resolve(count);
            count = end - start;
        }

        CountOutput.WriteCount(count);
    }

    private static List<TreeNode> ToDependencyTreeNodes(List<DependencyNode> nodes)
    {
        return nodes.Select(n =>
        {
            var label = !string.IsNullOrEmpty(n.Author)
                ? $"{n.PackageId} {n.Version} [{n.Author}]"
                : $"{n.PackageId} {n.Version}";
            label = ContainLabel(label);
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
            var label = LibraryInspectionView.ReferenceTreeText(node);
            label = ContainLabel(label);
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

    private static void WriteMarkdown(PackageDependenciesView view)
    {
        OutputFormatter.WriteWindowedMarkdown(Console.Out, rows: null,
            opts => MarkoutSerializer.Serialize(view, PackageDependenciesContext.Default, opts));
    }
}
