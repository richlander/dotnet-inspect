using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
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
        List<string> tempDirs = [];

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

            // Collect all assembly paths from various sources
            var assemblyInfos = await AssemblyCollector.CollectAsync(
                context.HttpClient, options, tempDirs, logger, "inspect-depends");

            logger.Log($"Scanning {assemblyInfos.Count} libraries for type {options.TargetType}");

            var assemblyPaths = assemblyInfos.Select(a => a.Path).ToList();
            var result = TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);

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
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    public static async Task<int> ExecuteLibraryDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            var libraryName = options.LibraryName!;
            string? assemblyPath = null;
            string? assemblyName = null;

            // Resolve library: local file → platform → package
            if (File.Exists(libraryName))
            {
                assemblyPath = libraryName;
            }
            else if (PlatformResolver.IsPlatformCandidate(libraryName))
            {
                var (resolved, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                    libraryName, context.HttpClient, logger.Log);
                if (error == null && resolved != null)
                    assemblyPath = resolved;
            }

            if (assemblyPath == null)
            {
                // Try NuGet package
                logger.Log($"Resolving package: {libraryName}");
                var outcome = await PackageExtractor.ExtractPackageAsync(
                    context.HttpClient, libraryName, logger.Log,
                    sourceOptions: options.SourceOptions);
                if (!outcome.IsSuccess)
                {
                    Console.Error.WriteLine($"Error: Could not resolve '{libraryName}' as a file, platform library, or NuGet package.");
                    return 1;
                }
                tempDir = outcome.Result!.TempDir;
                var extractPath = outcome.Result!.ExtractPath;

                // Find the primary DLL in the package
                var dllFiles = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories)
                    .Where(f => f.Contains("/lib/") || f.Contains("\\lib\\"))
                    .OrderByDescending(f => f) // prefer latest TFM
                    .ToArray();
                if (dllFiles.Length == 0)
                {
                    Console.Error.WriteLine($"Error: No libraries found in package '{libraryName}'.");
                    return 1;
                }
                assemblyPath = dllFiles[0];
            }

            // Extract references and build transitive tree
            var (refs, company) = AssemblyInspector.ExtractReferencesAndCompany(assemblyPath);
            assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            if (refs.Count == 0)
            {
                Console.Error.WriteLine($"No assembly references found in '{assemblyName}'.");
                return 0;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assemblyName };
            var sourceDir = Path.GetDirectoryName(assemblyPath);
            var refNodes = LibraryMetadataService.BuildTransitiveReferences(
                refs, sourceDir, visited, logger, deduplicate: true);

            var treeNodes = BuildNestedDependencyTree(refNodes);

            if (options.MermaidOutput)
            {
                WriteMermaidTree(assemblyName, treeNodes);
            }
            else if (options.EmbeddedMermaid)
            {
                WriteEmbeddedMermaidTree(assemblyName, treeNodes);
            }
            else
            {
                var view = new PackageDependenciesView
                {
                    Title = assemblyName,
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
        finally
        {
            if (tempDir != null)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    public static async Task<int> ExecutePackageDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            var packageRef = options.PackageName!;
            var (packageName, _) = PackageExtractor.ParsePackageReference(packageRef);

            logger.Log($"Resolving package: {packageRef}");
            var outcome = await PackageExtractor.ExtractPackageAsync(
                context.HttpClient, packageRef, logger.Log,
                sourceOptions: options.SourceOptions);
            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                return 1;
            }
            tempDir = outcome.Result!.TempDir;
            var extractPath = outcome.Result!.ExtractPath;
            var version = outcome.Result!.Version ?? "";

            // Parse nuspec for dependency groups
            string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length == 0)
            {
                Console.Error.WriteLine("No dependencies declared in package.");
                return 0;
            }

            var nuspec = NuspecParser.Parse(nuspecFiles[0]);
            if (nuspec.DependencyGroups is not { Count: > 0 })
            {
                Console.Error.WriteLine("No dependencies declared in package.");
                return 0;
            }

            // Pick TFM: explicit --tfm, or highest available
            var tfm = options.Tfm;
            DependencyGroup? group;
            if (!string.IsNullOrEmpty(tfm))
            {
                group = DependencyResolutionService.FindBestMatchingTfmGroup(nuspec.DependencyGroups, tfm);
                if (group == null)
                {
                    Console.Error.WriteLine($"Error: No dependencies found for TFM '{tfm}'.");
                    Console.Error.WriteLine("Available TFMs: " + string.Join(", ",
                        nuspec.DependencyGroups.Select(g => g.TargetFramework)));
                    return 1;
                }
            }
            else
            {
                group = nuspec.DependencyGroups
                    .OrderByDescending(g => TfmResolver.GetTfmPriority(g.TargetFramework))
                    .First();
                tfm = group.TargetFramework;
            }

            if (group.Dependencies.Count == 0)
            {
                Console.Error.WriteLine($"No additional dependencies for {tfm}.");
                return 0;
            }

            // Resolve transitive dependencies
            var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
                context.HttpClient, group.Dependencies, tfm, globalSeen, logger.Log);

            var title = $"{packageName} ({version})";
            var treeNodes = ToDependencyTreeNodes(depNodes);

            if (options.MermaidOutput)
            {
                WriteMermaidTree(title, treeNodes);
            }
            else if (options.EmbeddedMermaid)
            {
                WriteEmbeddedMermaidTree(title, treeNodes);
            }
            else
            {
                var view = new PackageDependenciesView
                {
                    Title = title,
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
        finally
        {
            if (tempDir != null)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
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
