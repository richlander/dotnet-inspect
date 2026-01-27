using System.Reflection.Metadata;

namespace DotnetInspector;

/// <summary>
/// Context for resolving generic type parameter names during signature decoding.
/// </summary>
public class GenericContext
{
    public IReadOnlyList<string> TypeParameters { get; }
    public IReadOnlyList<string> MethodParameters { get; }

    public GenericContext(IReadOnlyList<string> typeParameters, IReadOnlyList<string> methodParameters)
    {
        TypeParameters = typeParameters;
        MethodParameters = methodParameters;
    }

    public static GenericContext ForType(MetadataReader reader, TypeDefinition typeDef)
    {
        var typeParams = typeDef.GetGenericParameters()
            .Select(h => reader.GetString(reader.GetGenericParameter(h).Name))
            .ToList();
        return new GenericContext(typeParams, []);
    }

    public static GenericContext ForMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition methodDef)
    {
        var typeParams = typeDef.GetGenericParameters()
            .Select(h => reader.GetString(reader.GetGenericParameter(h).Name))
            .ToList();
        var methodParams = methodDef.GetGenericParameters()
            .Select(h => reader.GetString(reader.GetGenericParameter(h).Name))
            .ToList();
        return new GenericContext(typeParams, methodParams);
    }
}
