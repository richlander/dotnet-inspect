using DotnetInspector.Metadata;

namespace DotnetInspector.Metadata.Tests;

/// <summary>
/// Tests for TypeDependencyScanner — walks type hierarchies upward.
/// Uses platform ref assemblies for realistic metadata.
/// </summary>
public class TypeDependencyScannerTests
{
    private static readonly string[] RefAssemblies = GetRefAssemblyPaths();

    private static string[] GetRefAssemblyPaths()
    {
        // Find the ref pack directory for the current runtime
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location))
            ?? "/usr/lib/dotnet/shared/Microsoft.NETCore.App";

        // Walk up to the dotnet root and find ref packs
        var root = Path.GetFullPath(Path.Combine(dotnetRoot, "..", ".."));
        var refDir = Directory.GetDirectories(Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref"))
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (refDir == null)
            return [];

        var netDir = Directory.GetDirectories(Path.Combine(refDir, "ref"))
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (netDir == null)
            return [];

        return Directory.GetFiles(netDir, "*.dll");
    }

    [Fact]
    public void Stream_HasExpectedDependencies()
    {
        var tree = TypeDependencyScanner.BuildDependencyTree("Stream", RefAssemblies);

        Assert.NotEmpty(tree);

        var names = tree.Select(n => n.TypeName).ToList();
        Assert.Contains(names, n => n == "System.IDisposable");
        Assert.Contains(names, n => n == "System.IAsyncDisposable");
        // Stream extends MarshalByRefObject
        Assert.Contains(names, n => n == "System.MarshalByRefObject");
    }

    [Fact]
    public void IDisposable_HasNoDependencies()
    {
        // IDisposable is a leaf — no base interfaces
        var tree = TypeDependencyScanner.BuildDependencyTree("IDisposable", RefAssemblies);

        Assert.Empty(tree);
    }

    [Fact]
    public void UnknownType_ReturnsEmpty()
    {
        var tree = TypeDependencyScanner.BuildDependencyTree("NonExistentType12345", RefAssemblies);

        Assert.Empty(tree);
    }

    [Fact]
    public void EmptyAssemblyList_ReturnsEmpty()
    {
        var tree = TypeDependencyScanner.BuildDependencyTree("Stream", []);

        Assert.Empty(tree);
    }

    [Fact]
    public void IComparable_Generic_HasNoDependencies()
    {
        // IComparable<T> is a leaf interface
        var tree = TypeDependencyScanner.BuildDependencyTree("IComparable", RefAssemblies);

        Assert.Empty(tree);
    }

    [Fact]
    public void Tree_DeduplicatesExpansions()
    {
        // INumber<TSelf> has many transitive deps;
        // each type should be expanded (shown with children) at most once
        var tree = TypeDependencyScanner.BuildDependencyTree("INumber", RefAssemblies);

        Assert.NotEmpty(tree);

        // Collect all expanded nodes (those with children)
        var expandedNames = new List<string>();
        CollectExpandedNames(tree, expandedNames);

        // Each expanded name should appear exactly once
        var duplicates = expandedNames
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void ConcreteType_ResolvesTypeArguments()
    {
        // Int128 implements interfaces with concrete type arguments
        var tree = TypeDependencyScanner.BuildDependencyTree("Int128", RefAssemblies);

        Assert.NotEmpty(tree);

        var allNames = new List<string>();
        CollectNames(tree, allNames);

        // Should have concrete type args like System.Int128, not just TSelf
        Assert.Contains(allNames, n => n.Contains("System.Int128"));
    }

    [Fact]
    public void DirectDepsOnly_ExcludesTransitive()
    {
        // INumber<TSelf> directly inherits from INumberBase<TSelf>
        // INumberBase<TSelf> inherits from IAdditionOperators, etc.
        // The root nodes of INumber should NOT include IAdditionOperators
        // (it should only appear as a child of INumberBase)
        var tree = TypeDependencyScanner.BuildDependencyTree("INumber", RefAssemblies);

        Assert.NotEmpty(tree);

        var directNames = tree.Select(n => TypeMatcher.Normalize(n.TypeName)).ToList();

        // INumberBase should be a direct dep of INumber
        Assert.Contains(directNames, n => n.EndsWith("INumberBase"));

        // IAdditionOperators is a transitive dep (through INumberBase), not direct
        Assert.DoesNotContain(directNames, n => n.EndsWith("IAdditionOperators"));
    }

    [Fact]
    public void SystemRoot_IsExcluded()
    {
        // Object, ValueType, Enum should never appear in the tree
        var tree = TypeDependencyScanner.BuildDependencyTree("Int128", RefAssemblies);

        var allNames = new List<string>();
        CollectNames(tree, allNames);

        Assert.DoesNotContain(allNames, n => n == "System.Object");
        Assert.DoesNotContain(allNames, n => n == "System.ValueType");
        Assert.DoesNotContain(allNames, n => n == "System.Enum");
    }

    private static void CollectNames(List<TypeDependencyNode> nodes, List<string> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node.TypeName);
            CollectNames(node.Children, result);
        }
    }

    private static void CollectExpandedNames(List<TypeDependencyNode> nodes, List<string> result)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count > 0)
            {
                result.Add(TypeMatcher.Normalize(node.TypeName));
                CollectExpandedNames(node.Children, result);
            }
        }
    }
}
