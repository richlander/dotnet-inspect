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
    /// <summary>
    /// Runs an async acquisition step with network allowed, then re-engages the guard.
    /// Ensures a clear phase boundary: acquire (network) → process (offline).
    /// </summary>
    private static async Task<T> AcquireAsync<T>(Func<Task<T>> acquire)
    {
#if DEBUG
        DotnetInspector.Core.HttpClientFactory.AllowNetwork();
        try
        {
            return await acquire();
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.DenyNetwork();
        }
#else
        return await acquire();
#endif
    }

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

            // Phase 1: Acquire assemblies (network)
            var assemblyInfos = await AcquireAsync(() =>
                AssemblyCollector.CollectAsync(
                    context.HttpClient, options, tempDirs, logger, "inspect-depends"));

            // Phase 2: Scan types (offline)

            logger.Log($"Scanning {assemblyInfos.Count} libraries for type {options.TargetType}");

            var assemblyPaths = assemblyInfos.Select(a => a.Path).ToList();
            var tree = TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);

            if (tree == null)
            {
                Console.Error.WriteLine($"Type '{options.TargetType}' not found in the specified scope.");
                return 1;
            }

            if (tree.Count == 0)
            {
                Console.WriteLine($"{options.TargetType}: no type dependencies (derives from System.Object only).");
                return 0;
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

            // Phase 1: Resolve library path (network)
            var assemblyPath = await AcquireAsync(async () =>
            {
                if (File.Exists(libraryName))
                    return libraryName;

                if (PlatformResolver.IsPlatformCandidate(libraryName))
                {
                    var (resolved, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                        libraryName, context.HttpClient, logger.Log);
                    if (error == null && resolved != null)
                        return resolved;
                }

                // Try NuGet package
                logger.Log($"Resolving package: {libraryName}");
                var outcome = await PackageExtractor.ExtractPackageAsync(
                    context.HttpClient, libraryName, logger.Log,
                    sourceOptions: options.SourceOptions);
                if (!outcome.IsSuccess)
                    return (string?)null;

                tempDir = outcome.Result!.TempDir;
                var extractPath = outcome.Result!.ExtractPath;

                var dllFiles = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories)
                    .Where(f => f.Contains("/lib/") || f.Contains("\\lib\\"))
                    .OrderByDescending(f => f)
                    .ToArray();
                return dllFiles.Length > 0 ? dllFiles[0] : null;
            });

            if (assemblyPath == null)
            {
                Console.Error.WriteLine($"Error: Could not resolve '{libraryName}' as a file, platform library, or NuGet package.");
                return 1;
            }

            // Phase 2: Extract references and build tree (offline)

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

            // Phase 1: Acquire package and resolve transitive dependencies (network)
            logger.Log($"Resolving package: {packageRef}");

            string? version = null;
            List<DependencyNode>? depNodes = null;
            string? resolvedTfm = null;

            var success = await AcquireAsync(async () =>
            {
                var outcome = await PackageExtractor.ExtractPackageAsync(
                    context.HttpClient, packageRef, logger.Log,
                    sourceOptions: options.SourceOptions);
                if (!outcome.IsSuccess)
                {
                    Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                    return false;
                }

                tempDir = outcome.Result!.TempDir;
                var extractPath = outcome.Result!.ExtractPath;
                version = outcome.Result!.Version ?? "";

                string[] nuspecFiles = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
                if (nuspecFiles.Length == 0)
                    return true; // no deps — not a failure

                var nuspec = NuspecParser.Parse(nuspecFiles[0]);
                if (nuspec.DependencyGroups is not { Count: > 0 })
                    return true;

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
                        return false;
                    }
                }
                else
                {
                    group = nuspec.DependencyGroups
                        .OrderByDescending(g => TfmResolver.GetTfmPriority(g.TargetFramework))
                        .First();
                    resolvedTfm = group.TargetFramework;
                }

                if (group.Dependencies.Count == 0)
                    return true;

                var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
                    context.HttpClient, group.Dependencies, resolvedTfm, globalSeen, logger.Log);
                return true;
            });

            if (!success)
                return 1;

            // Phase 2: Render output (offline)
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
