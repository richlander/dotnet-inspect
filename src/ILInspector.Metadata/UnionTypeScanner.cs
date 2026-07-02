using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

public record UnionTypeInfo(
    string TypeName,
    string Kind,
    bool ImplementsIUnion,
    List<string> CaseTypes);

/// <summary>
/// Scans assemblies for C# union-shaped types.
/// </summary>
public static class UnionTypeScanner
{
    private const string IUnionTypeName = "System.Runtime.CompilerServices.IUnion";

    public static List<UnionTypeInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        List<UnionTypeInfo> results = [];

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!AttributeReader.HasAttribute(reader, typeDef.GetCustomAttributes(), KnownAttributeNames.UnionAttribute))
                continue;

            var context = GenericContext.ForType(reader, typeDef);
            results.Add(new UnionTypeInfo(
                reader.GetFullTypeName(typeDef),
                GetTypeKind(reader, typeDef),
                ImplementsIUnion(reader, typeDef, context),
                GetUnionCaseTypes(reader, typeDef).ToList()));
        }

        return results;
    }

    private static bool ImplementsIUnion(MetadataReader reader, TypeDefinition typeDef, GenericContext context)
    {
        foreach (var interfaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(interfaceHandle);
            if (TypeResolver.GetTypeName(reader, iface.Interface, context) == IUnionTypeName)
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetUnionCaseTypes(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != ".ctor")
                continue;
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                continue;
            if ((method.Attributes & MethodAttributes.Static) != 0)
                continue;

            MethodSignature<string> signature;
            try
            {
                signature = method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch
            {
                continue;
            }

            if (signature.ParameterTypes.Length == 1)
                yield return signature.ParameterTypes[0];
        }
    }

    private static string GetTypeKind(MetadataReader reader, TypeDefinition typeDef)
    {
        var attrs = typeDef.Attributes;
        if ((attrs & TypeAttributes.Interface) != 0)
            return "interface";
        if (TypeResolver.GetTypeName(reader, typeDef.BaseType) == "System.ValueType")
            return "struct";
        return "class";
    }
}
