using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Where a caller allows an assembly identity to resolve from.
/// <see cref="Platform"/> asserts the reference is a runtime/framework assembly
/// (its public-key token is a platform key), so the locator may resolve it only
/// from platform/framework sources — never a confusable local copy.
/// <see cref="Any"/> places no source constraint (sibling/package/platform).
/// </summary>
public enum AssemblyResolutionScope
{
    Any,
    Platform,
}

/// <summary>
/// Locates the file for an assembly by simple name. Policy lives with the
/// caller (sibling directory, ref pack, shared framework, package graph);
/// the resolver below supplies only the metadata mechanism. Returns null
/// when the assembly cannot be located. <paramref name="scope"/> lets the
/// caller constrain platform identities so the locator refuses confusable
/// impostors and fast-paths to framework sources.
/// </summary>
public delegate string? AssemblyLocator(string assemblyName, AssemblyResolutionScope scope);

/// <summary>Where a type is actually defined after following forwarders.</summary>
public sealed record TypeLocation(string AssemblyPath, string FullTypeName);

/// <summary>
/// Type-forwarder resolution: the mechanism (ExportedTypes traversal, hop
/// limit, cycle detection) lives here; locating assembly files is the
/// caller's policy via <see cref="AssemblyLocator"/> — the same split as
/// MetadataLoadContext's MetadataAssemblyResolver and ILSpy's
/// IAssemblyResolver.
/// </summary>
public static class TypeForwardResolver
{
    /// <summary>
    /// Resolves the assembly that defines <paramref name="fullTypeName"/>,
    /// starting at <paramref name="assemblyPath"/> and following type
    /// forwarders through assemblies supplied by <paramref name="locateAssembly"/>.
    /// Returns null if the chain dead-ends, exceeds <paramref name="maxHops"/>,
    /// or revisits an assembly.
    /// </summary>
    public static TypeLocation? LocateType(
        string assemblyPath, string fullTypeName, AssemblyLocator locateAssembly,
        int maxHops = 8, AssemblyResolutionScope scope = AssemblyResolutionScope.Any)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = assemblyPath;

        for (int hop = 0; hop <= maxHops; hop++)
        {
            if (!visited.Add(Path.GetFullPath(current)))
                return null;

            using var stream = File.OpenRead(current);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;
            var reader = peReader.GetMetadataReader();

            if (FindTypeDefinition(reader, fullTypeName) is { IsNil: false })
                return new TypeLocation(current, fullTypeName);

            if (ForwardTargetAssembly(reader, fullTypeName) is not { } targetAssembly)
                return null;
            if (locateAssembly(targetAssembly, scope) is not { } next || !File.Exists(next))
                return null;
            current = next;
        }
        return null;
    }

    /// <summary>True if the assembly contains a type definition matching <paramref name="typeName"/> (pattern-aware via <see cref="TypeMatcher"/>).</summary>
    public static bool DefinesType(string assemblyPath, string typeName)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return false;
            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                if (TypeMatcher.Matches(FullName(reader, typeDef.Namespace, typeDef.Name), typeName))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    /// <summary>True if the assembly forwards a type matching <paramref name="typeName"/> (pattern-aware via <see cref="TypeMatcher"/>).</summary>
    public static bool ForwardsType(string assemblyPath, string typeName)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return false;
            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.ExportedTypes)
            {
                var exported = reader.GetExportedType(handle);
                if (!exported.IsForwarder)
                    continue;
                if (TypeMatcher.Matches(FullName(reader, exported.Namespace, exported.Name), typeName))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    /// <summary>The assembly-reference simple name a forwarder for <paramref name="fullTypeName"/> points at, or null (exact-name match, one hop).</summary>
    public static string? ForwardTargetAssembly(MetadataReader reader, string fullTypeName)
    {
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (!exported.IsForwarder)
                continue;
            if (FullName(reader, exported.Namespace, exported.Name) != fullTypeName)
                continue;
            if (exported.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
                return reader.GetString(assembly.Name);
            }
        }
        return null;
    }

    static TypeDefinitionHandle FindTypeDefinition(MetadataReader reader, string fullTypeName)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (FullName(reader, typeDef.Namespace, typeDef.Name) == fullTypeName)
                return handle;
        }
        return default;
    }

    static string FullName(MetadataReader reader, StringHandle ns, StringHandle name)
    {
        string nsText = reader.GetString(ns);
        string nameText = reader.GetString(name);
        return nsText.Length == 0 ? nameText : $"{nsText}.{nameText}";
    }
}
