using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Context for resolving generic type parameter names and constraint flags during
/// signature decoding.
/// </summary>
public class GenericContext
{
    readonly IReadOnlyList<bool> _typeValueTypeConstraints;
    readonly IReadOnlyList<bool> _methodValueTypeConstraints;

    public IReadOnlyList<string> TypeParameters { get; }
    public IReadOnlyList<string> MethodParameters { get; }

    public GenericContext(IReadOnlyList<string> typeParameters, IReadOnlyList<string> methodParameters)
        : this(typeParameters, methodParameters, [], [])
    {
    }

    GenericContext(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodParameters,
        IReadOnlyList<bool> typeValueTypeConstraints,
        IReadOnlyList<bool> methodValueTypeConstraints)
    {
        TypeParameters = typeParameters;
        MethodParameters = methodParameters;
        _typeValueTypeConstraints = typeValueTypeConstraints;
        _methodValueTypeConstraints = methodValueTypeConstraints;
    }

    /// <summary>
    /// Whether the indexed type parameter carries the metadata value-type constraint
    /// flag. This decides whether a nullable annotation may be applied to the parameter itself:
    /// for <c>where T : struct</c>, source <c>T?</c> is represented structurally as
    /// <c>System.Nullable&lt;T&gt;</c>, never as an annotation on <c>T</c>.
    /// </summary>
    public bool HasTypeParameterValueTypeConstraint(int index)
        => HasValueTypeConstraint(_typeValueTypeConstraints, index);

    /// <summary>
    /// Whether the indexed method parameter carries the metadata value-type constraint
    /// flag. See <see cref="HasTypeParameterValueTypeConstraint"/>.
    /// </summary>
    public bool HasMethodParameterValueTypeConstraint(int index)
        => HasValueTypeConstraint(_methodValueTypeConstraints, index);

    /// <summary>
    /// Creates a context for a type definition (type parameters only).
    /// </summary>
    public static GenericContext ForType(MetadataReader reader, TypeDefinition typeDef)
    {
        var typeParameters = ReadParameters(reader, typeDef.GetGenericParameters());
        return new GenericContext(typeParameters.Names, [], typeParameters.ValueTypeConstraints, []);
    }

    /// <summary>
    /// Creates a context for a method definition (type + method parameters).
    /// </summary>
    public static GenericContext ForMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition methodDef)
    {
        var typeParameters = ReadParameters(reader, typeDef.GetGenericParameters());
        var methodParameters = ReadParameters(reader, methodDef.GetGenericParameters());
        return new GenericContext(
            typeParameters.Names,
            methodParameters.Names,
            typeParameters.ValueTypeConstraints,
            methodParameters.ValueTypeConstraints);
    }

    static (List<string> Names, List<bool> ValueTypeConstraints) ReadParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles)
    {
        List<string> names = [];
        List<bool> valueTypeConstraints = [];
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            names.Add(reader.GetString(parameter.Name));
            valueTypeConstraints.Add(
                (parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0);
        }
        return (names, valueTypeConstraints);
    }

    static bool HasValueTypeConstraint(IReadOnlyList<bool> constraints, int index)
        => index >= 0 && index < constraints.Count && constraints[index];
}
