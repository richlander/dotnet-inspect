using System.Reflection.Metadata;

namespace DotnetInspector.Metadata;

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

    /// <summary>
    /// Creates a context for a type definition (type parameters only).
    /// </summary>
    public static GenericContext ForType(MetadataReader reader, TypeDefinition typeDef)
    {
        var typeParams = typeDef.GetGenericParameters()
            .Select(h => reader.GetGenericParameterName(h))
            .ToList();
        return new GenericContext(typeParams, []);
    }

    /// <summary>
    /// Creates a context for a method definition (type + method parameters).
    /// </summary>
    public static GenericContext ForMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition methodDef)
    {
        var typeParams = typeDef.GetGenericParameters()
            .Select(h => reader.GetGenericParameterName(h))
            .ToList();
        var methodParams = methodDef.GetGenericParameters()
            .Select(h => reader.GetGenericParameterName(h))
            .ToList();
        return new GenericContext(typeParams, methodParams);
    }
}
