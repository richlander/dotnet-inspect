using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;
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

            var assemblyInfos = await AssemblyCollector.CollectAsync(
                context.HttpClient, options, tempDirs, logger, "inspect-depends");


            logger.Log($"Scanning {assemblyInfos.Count} libraries for type {options.TargetType}");

            var assemblyPaths = assemblyInfos.Select(a => a.Path).ToList();
            var tree = TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);

            if (tree == null)
            {
                Console.Error.WriteLine($"Type '{options.TargetType}' not found in the specified scope.");
                return 1;
            }

            // Include System.Object for types with no other dependencies
            if (tree.Count == 0)
                tree = [new TypeDependencyNode("System.Object", [])];

            if (options.JsonOutput)
            {
                JsonOutputHelper.Write(tree,
                    DependsJsonContext.Default.ListTypeDependencyNode,
                    DependsCompactJsonContext.Default.ListTypeDependencyNode,
                    options.CompactJson);
            }
            else
            {
                var writer = new MarkoutWriter(Console.Out);
                writer.WriteTreeNode(options.TargetType);
                writer.WriteTree(ToTreeNodes(tree));
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
            string? assemblyName = null;
            string? assemblyVersion = null;
            string? tfm = null;

            string? assemblyPath;
            if (File.Exists(libraryName))
            {
                assemblyPath = libraryName;
            }
            else if (PlatformResolver.IsPlatformCandidate(libraryName))
            {
                var (resolved, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                    libraryName, context.HttpClient, logger.Log);
                assemblyPath = (error == null) ? resolved : null;
            }
            else
            {
                // Try NuGet package
                logger.Log($"Resolving package: {libraryName}");
                var outcome = await PackageExtractor.ExtractPackageAsync(
                    context.HttpClient, libraryName, logger.Log,
                    sourceOptions: options.SourceOptions);
                if (outcome.IsSuccess)
                {
                    tempDir = outcome.Result!.TempDir;
                    var extractPath = outcome.Result!.ExtractPath;
                    var dllFiles = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories)
                        .Where(f => f.Contains("/lib/") || f.Contains("\\lib\\"))
                        .OrderByDescending(f => f)
                        .ToArray();
                    assemblyPath = dllFiles.Length > 0 ? dllFiles[0] : null;
                }
                else
                {
                    assemblyPath = null;
                }
            }

            if (assemblyPath == null)
            {
                Console.Error.WriteLine($"Error: Could not resolve '{libraryName}' as a file, platform library, or NuGet package.");
                return 1;
            }


            // Extract references and build transitive tree
            var (refs, company) = AssemblyInspector.ExtractReferencesAndCompany(assemblyPath);
            assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            // Get assembly version/TFM via quick metadata read
            using (var service = SourceLinkService.Open(assemblyPath, logger.Log))
            {
                var info = service.Context.ExtractAssemblyInfo(includeReferences: false);
                assemblyVersion = info?.AssemblyVersion;
                tfm = info?.TargetFramework;
            }

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

            var view = new AssemblyDependenciesView
            {
                Title = assemblyName,
                AssemblyName = assemblyName,
                Version = assemblyVersion,
                Tfm = tfm,
                Dependencies = treeNodes
            };
            MarkoutSerializer.Serialize(view, Console.Out, AssemblyDependenciesContext.Default);
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

            string? resolvedTfm = null;
            List<DependencyNode>? depNodes = null;

            string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length > 0)
            {
                var nuspec = NuspecParser.Parse(nuspecFiles[0]);
                if (nuspec.DependencyGroups is { Count: > 0 })
                {
                    resolvedTfm = options.Tfm;
                    DependencyGroup? group;
                    if (!string.IsNullOrEmpty(resolvedTfm))
                    {
                        group = DependencyResolutionService.FindBestMatchingTfmGroup(nuspec.DependencyGroups, resolvedTfm);
                        if (group == null)
                        {
                            Console.Error.WriteLine($"Error: No dependencies found for TFM '{resolvedTfm}'.");
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
                        resolvedTfm = group.TargetFramework;
                    }

                    if (group.Dependencies.Count > 0)
                    {
                        var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
                            context.HttpClient, group.Dependencies, resolvedTfm, globalSeen, logger.Log);
                    }
                }
            }

            if (depNodes == null)
            {
                var emptyView = new EmptyDepsView
                {
                    Title = $"{packageName} ({version})",
                    Description = $"No additional dependencies for {resolvedTfm ?? "any TFM"}."
                };
                Console.WriteLine(new MarkoutContext().Serialize(emptyView));
                return 0;
            }

            var view = new PackageDependenciesView
            {
                Title = $"{packageName} ({version})",
                Package = packageName,
                Version = version!,
                Tfm = resolvedTfm!,
                Dependencies = ToDependencyTreeNodes(depNodes)
            };
            MarkoutSerializer.Serialize(view, Console.Out, PackageDependenciesContext.Default);
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
                ? new TreeNode(n.TypeName, ToTreeNodes(n.Children))
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
                ? new TreeNode(label, ToDependencyTreeNodes(n.Children))
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

            target.Add(children.Count > 0 ? new TreeNode(label, children) : new TreeNode(label));
        }
    }
}
