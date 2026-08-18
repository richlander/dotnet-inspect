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
        => ForType(reader, typeDef, beforeMaterialize: null);

    /// <summary>
    /// Creates a context for a type definition while observing encoded generic-name
    /// work before those names are materialized.
    /// </summary>
    public static GenericContext ForType(
        MetadataReader reader,
        TypeDefinition typeDef,
        Action<int>? beforeMaterialize)
    {
        var typeParameters = ReadParameters(
            reader,
            typeDef.GetGenericParameters(),
            beforeMaterialize);
        return new GenericContext(typeParameters.Names, [], typeParameters.ValueTypeConstraints, []);
    }

    /// <summary>
    /// Creates a context for a method definition (type + method parameters).
    /// </summary>
    public static GenericContext ForMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition methodDef)
    {
        var typeParameters = ReadParameters(
            reader,
            typeDef.GetGenericParameters(),
            beforeMaterialize: null);
        var methodParameters = ReadParameters(
            reader,
            methodDef.GetGenericParameters(),
            beforeMaterialize: null);
        return new GenericContext(
            typeParameters.Names,
            methodParameters.Names,
            typeParameters.ValueTypeConstraints,
            methodParameters.ValueTypeConstraints);
    }

    /// <summary>
    /// Extends an existing type context with one method's generic parameters,
    /// observing encoded method-parameter names before materialization.
    /// </summary>
    public static GenericContext ForMethod(
        MetadataReader reader,
        GenericContext typeContext,
        MethodDefinition methodDef,
        Action<int>? beforeMaterialize)
    {
        ArgumentNullException.ThrowIfNull(typeContext);
        var methodParameters = ReadParameters(
            reader,
            methodDef.GetGenericParameters(),
            beforeMaterialize);
        return new GenericContext(
            typeContext.TypeParameters,
            methodParameters.Names,
            typeContext._typeValueTypeConstraints,
            methodParameters.ValueTypeConstraints);
    }

    static (List<string> Names, List<bool> ValueTypeConstraints) ReadParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        Action<int>? beforeMaterialize)
    {
        if (handles.Count > MetadataSafetyPolicy.MaxSignatureTypeNodes)
        {
            throw new BadImageFormatException(
                "The generic-parameter count exceeds the metadata safety limit.");
        }
        ValidateParameterIndices(reader, handles);

        var names = new List<string>(handles.Count);
        var valueTypeConstraints = new List<bool>(handles.Count);
        int totalNameLength = 0;
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            int remainingNameLength =
                MetadataSafetyPolicy.MaxStructuralSignatureChars
                - totalNameLength;
            int encodedNameLength = reader.GetBlobReader(parameter.Name).Length;
            if (encodedNameLength > remainingNameLength)
            {
                throw new BadImageFormatException(
                    "The generic-parameter names exceed the metadata safety limit.");
            }
            beforeMaterialize?.Invoke(encodedNameLength);
            string name = reader.GetString(parameter.Name);
            if (name.Length > remainingNameLength)
            {
                throw new BadImageFormatException(
                    "The generic-parameter names exceed the metadata safety limit.");
            }
            totalNameLength += name.Length;
            names.Add(name);
            valueTypeConstraints.Add(
                (parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0);
        }
        return (names, valueTypeConstraints);
    }

    public static void ValidateParameterIndices(
        MetadataReader reader,
        IEnumerable<GenericParameterHandle> handles,
        int expectedIndex = 0)
    {
        foreach (GenericParameterHandle handle in handles)
        {
            GenericParameter parameter = reader.GetGenericParameter(handle);
            if (parameter.Index != expectedIndex)
            {
                throw new BadImageFormatException(
                    "Generic parameter indices must be contiguous and ordered.");
            }
            expectedIndex++;
        }
    }

    static bool HasValueTypeConstraint(IReadOnlyList<bool> constraints, int index)
        => index >= 0 && index < constraints.Count && constraints[index];
}
