using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// A node in a type dependency tree (base classes and interfaces).
/// </summary>
public record TypeDependencyNode(string TypeName, List<TypeDependencyNode> Children);

/// <summary>
/// Walks the inheritance and interface implementation graph upward from a type.
/// This is the inverse of <see cref="TypeHierarchyScanner.FindImplementers"/> —
/// it shows what a type depends on, not what depends on it.
/// </summary>
public static class TypeDependencyScanner
{
    /// <summary>
    /// Builds the dependency tree for a type found in the given assemblies.
    /// Returns the direct base types and interfaces as root-level nodes,
    /// each with their own recursive dependencies.
    /// </summary>
    public static List<TypeDependencyNode> BuildDependencyTree(
        string targetType,
        IReadOnlyList<string> assemblyPaths)
    {
        var typeIndex = new Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)>(
            StringComparer.OrdinalIgnoreCase);
        var peReaders = new List<PEReader>();

        try
        {
            foreach (var path in assemblyPaths)
            {
                try
                {
                    var stream = File.OpenRead(path);
                    var peReader = new PEReader(stream);
                    peReaders.Add(peReader);

                    if (!peReader.HasMetadata)
                        continue;

                    var mdReader = peReader.GetMetadataReader();
                    foreach (var typeDefHandle in mdReader.TypeDefinitions)
                    {
                        var typeDef = mdReader.GetTypeDefinition(typeDefHandle);
                        if (!typeDef.IsPublic)
                            continue;

                        var name = mdReader.GetString(typeDef.Name);
                        if (name.StartsWith("<") || name.StartsWith("__"))
                            continue;

                        var ns = mdReader.GetString(typeDef.Namespace);
                        var fullName = TypeResolver.GetFullName(ns, name);

                        // Index by ECMA name for lookup
                        typeIndex.TryAdd(fullName, (peReader, mdReader, typeDef));
                    }
                }
                catch
                {
                    // Skip assemblies that can't be read
                }
            }

            // Find the target type
            var normalizedTarget = TypeMatcher.Normalize(targetType);
            var matchKey = typeIndex.Keys.FirstOrDefault(k => TypeMatcher.Matches(k, normalizedTarget));
            if (matchKey == null)
                return [];

            var match = typeIndex[matchKey];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return BuildNode(match.MdReader, match.TypeDef, typeIndex, seen);
        }
        finally
        {
            foreach (var pr in peReaders)
                pr.Dispose();
        }
    }

    /// <summary>
    /// Builds the child nodes for a type. Computes the "minimal" direct
    /// dependencies by removing interfaces that are transitively inherited
    /// through other direct interfaces. De-duplicates across the tree.
    /// </summary>
    private static List<TypeDependencyNode> BuildNode(
        MetadataReader reader,
        TypeDefinition typeDef,
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        HashSet<string> seen)
    {
        var context = GenericContext.ForType(reader, typeDef);

        // Gather all declared dependencies (base type + interfaces)
        var allDeps = new List<string>();

        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType, context);
            if (baseTypeName != null && !IsSystemRoot(baseTypeName))
                allDeps.Add(baseTypeName);
        }

        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(reader, iface.Interface, context);
            if (ifaceName != null)
                allDeps.Add(ifaceName);
        }

        if (allDeps.Count == 0)
            return [];

        // Compute transitive closure for each dep to find which are redundant
        var transitivelyReachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in allDeps)
        {
            CollectTransitive(dep, typeIndex, transitivelyReachable, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        // A dep is "direct" if it's not transitively reachable through another dep
        var directDeps = allDeps
            .Where(d => !transitivelyReachable.Contains(TypeMatcher.Normalize(d)))
            .ToList();

        // Build tree nodes for direct deps only
        var results = new List<TypeDependencyNode>();
        foreach (var dep in directDeps)
        {
            var normalized = TypeMatcher.Normalize(dep);
            if (!seen.Add(normalized))
            {
                // Already shown at a shallower level — include as leaf
                results.Add(new TypeDependencyNode(dep, []));
                continue;
            }

            var children = ResolveChildren(dep, typeIndex, seen);
            results.Add(new TypeDependencyNode(dep, children));
        }

        return results;
    }

    /// <summary>
    /// Collects all interfaces transitively reachable from a type's dependencies
    /// (not including the type itself).
    /// </summary>
    private static void CollectTransitive(
        string typeName,
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        HashSet<string> result,
        HashSet<string> visited)
    {
        var normalized = TypeMatcher.Normalize(typeName);
        if (!visited.Add(normalized))
            return;

        var matchKey = typeIndex.Keys.FirstOrDefault(k => TypeMatcher.Matches(k, normalized));
        if (matchKey == null)
            return;

        var (_, mdReader, typeDef) = typeIndex[matchKey];
        var context = GenericContext.ForType(mdReader, typeDef);

        // Base type
        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(mdReader, typeDef.BaseType, context);
            if (baseTypeName != null && !IsSystemRoot(baseTypeName))
            {
                result.Add(TypeMatcher.Normalize(baseTypeName));
                CollectTransitive(baseTypeName, typeIndex, result, visited);
            }
        }

        // Interfaces
        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = mdReader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(mdReader, iface.Interface, context);
            if (ifaceName != null)
            {
                result.Add(TypeMatcher.Normalize(ifaceName));
                CollectTransitive(ifaceName, typeIndex, result, visited);
            }
        }
    }

    private static List<TypeDependencyNode> ResolveChildren(
        string typeName,
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        HashSet<string> seen)
    {
        var normalizedName = TypeMatcher.Normalize(typeName);

        var matchKey = typeIndex.Keys.FirstOrDefault(k => TypeMatcher.Matches(k, normalizedName));
        if (matchKey == null)
            return [];

        var match = typeIndex[matchKey];
        return BuildNode(match.MdReader, match.TypeDef, typeIndex, seen);
    }

    private static bool IsSystemRoot(string typeName)
    {
        return typeName is "System.Object" or "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate";
    }
}
