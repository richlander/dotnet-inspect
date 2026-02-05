using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// Result of a type hierarchy scan.
/// </summary>
/// <param name="TypeName">Full name of the implementing/derived type</param>
/// <param name="Namespace">Namespace of the type</param>
/// <param name="Kind">Type kind (class, struct, interface)</param>
/// <param name="RelationshipKind">How this type relates to the target (Implements, Extends)</param>
/// <param name="AssemblyName">Assembly containing the type</param>
public record TypeRelationship(
    string TypeName,
    string? Namespace,
    string Kind,
    RelationshipKind RelationshipKind,
    string? AssemblyName = null);

/// <summary>
/// The kind of relationship between types.
/// </summary>
public enum RelationshipKind
{
    /// <summary>Type implements the target interface.</summary>
    Implements,
    /// <summary>Type extends (inherits from) the target class.</summary>
    Extends
}

/// <summary>
/// Scans assemblies to find type hierarchy relationships.
/// Finds types that implement interfaces or extend base classes.
/// </summary>
public static class TypeHierarchyScanner
{
    /// <summary>
    /// Finds all types in an assembly that implement or extend the target type.
    /// </summary>
    /// <param name="peReader">PE reader for the assembly</param>
    /// <param name="targetType">Interface or base class to search for</param>
    /// <param name="includeHidden">Include EditorBrowsable.Never and Obsolete types</param>
    /// <returns>Types that implement/extend the target</returns>
    public static IEnumerable<TypeRelationship> FindImplementers(
        PEReader peReader,
        string targetType,
        bool includeHidden = false)
    {
        if (!peReader.HasMetadata)
            yield break;

        var reader = peReader.GetMetadataReader();
        var normalizedTarget = TypeMatcher.Normalize(targetType);

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var attributes = typeDef.Attributes;

            // Only public types
            bool isPublic = (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public ||
                           (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;
            if (!isPublic)
                continue;

            string typeName = reader.GetString(typeDef.Name);

            // Skip compiler-generated
            if (typeName.StartsWith("<") || typeName.StartsWith("__"))
                continue;

            // Skip hidden unless requested
            if (!includeHidden && AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                continue;

            string? ns = reader.GetString(typeDef.Namespace);
            string fullName = TypeResolver.GetFullName(ns, typeName);
            string kind = GetTypeKind(reader, typeDef);

            // Check base type
            if (!typeDef.BaseType.IsNil)
            {
                var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType);
                if (baseTypeName != null && TypeMatcher.Matches(baseTypeName, normalizedTarget))
                {
                    yield return new TypeRelationship(fullName, ns, kind, RelationshipKind.Extends);
                    continue; // Don't also report as implements
                }
            }

            // Check interfaces
            var context = GenericContext.ForType(reader, typeDef);
            foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
            {
                var iface = reader.GetInterfaceImplementation(ifaceHandle);
                var ifaceName = TypeResolver.GetTypeName(reader, iface.Interface, context);
                
                if (ifaceName != null && TypeMatcher.Matches(ifaceName, normalizedTarget))
                {
                    yield return new TypeRelationship(fullName, ns, kind, RelationshipKind.Implements);
                    break; // Only report once per type
                }
            }
        }
    }

    /// <summary>
    /// Scans a stream containing a PE image for implementers of a target type.
    /// </summary>
    public static IEnumerable<TypeRelationship> FindImplementers(
        Stream peStream,
        string targetType,
        bool includeHidden = false)
    {
        using var peReader = new PEReader(peStream, PEStreamOptions.LeaveOpen);
        foreach (var result in FindImplementers(peReader, targetType, includeHidden))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Gets all interfaces implemented by a type (direct only, not inherited).
    /// </summary>
    public static IEnumerable<string> GetInterfaces(MetadataReader reader, TypeDefinition typeDef)
    {
        var context = GenericContext.ForType(reader, typeDef);
        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(reader, iface.Interface, context);
            if (ifaceName != null)
                yield return ifaceName;
        }
    }

    /// <summary>
    /// Gets the base type of a type definition.
    /// </summary>
    public static string? GetBaseType(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.BaseType.IsNil)
            return null;
        return TypeResolver.GetTypeName(reader, typeDef.BaseType);
    }

    private static string GetTypeKind(MetadataReader reader, TypeDefinition typeDef)
    {
        var attrs = typeDef.Attributes;

        if ((attrs & TypeAttributes.Interface) != 0)
            return "interface";

        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType);
            return baseTypeName switch
            {
                "System.Enum" => "enum",
                "System.ValueType" => "struct",
                "System.Delegate" or "System.MulticastDelegate" => "delegate",
                _ => "class"
            };
        }

        return "class";
    }
}
