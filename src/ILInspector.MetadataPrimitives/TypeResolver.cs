using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves type names from metadata handles.
/// </summary>
public static class TypeResolver
{
    /// <summary>
    /// Gets the fully qualified type name from an entity handle.
    /// Handles TypeReference, TypeDefinition, and TypeSpecification.
    /// </summary>
    public static string? GetTypeName(MetadataReader reader, EntityHandle handle, GenericContext? context = null)
    {
        if (handle.IsNil)
            return null;

        return handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeNameFromReference(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => GetTypeNameFromDefinition(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeSpecification => DecodeTypeNameFromSpecification(
                reader,
                (TypeSpecificationHandle)handle,
                context).TryGetValue(out var name)
                    ? name
                    : null,
            _ => null
        };
    }

    /// <summary>
    /// Gets the type name from a TypeReference handle, qualifying a nested type
    /// through its declaring type (<c>Outer.Inner</c>) - a nested
    /// <see cref="TypeReference"/> carries an empty namespace and a leaf name with
    /// its enclosing type as the resolution scope, so a raw namespace+name would
    /// drop the qualifier (rendering <c>ImmutableArray`1+Builder</c> as a bare
    /// <c>Builder</c>). Mirrors <see cref="GetFullName(MetadataReader, TypeDefinition)"/>.
    /// </summary>
    public static string GetTypeNameFromReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{GetTypeNameFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope)}.{reader.GetString(typeRef.Name)}";
        return GetFullName(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
    }

    /// <summary>
    /// Gets the type name from a TypeDefinition handle.
    /// </summary>
    public static string GetTypeNameFromDefinition(MetadataReader reader, TypeDefinitionHandle handle)
        => GetFullName(reader, reader.GetTypeDefinition(handle));

    /// <summary>
    /// Gets the type name from a TypeSpecification handle (generic instantiations).
    /// </summary>
    public static string GetTypeNameFromSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context = null)
        => DecodeTypeNameFromSpecification(reader, handle, context).GetValueOrThrow();

    /// <summary>
    /// Gets the guarded decode outcome for a TypeSpecification handle (generic instantiations).
    /// </summary>
    public static SignatureDecodeResult<string> DecodeTypeNameFromSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context = null)
        => GuardedSignatureDecoder.DecodeTypeSpecification(reader, handle, context);

    /// <summary>
    /// Resolves a direct type definition or the generic type underlying a guarded
    /// <see cref="HandleKind.TypeSpecification"/> when that definition belongs to
    /// the current assembly. Type references and non-type signatures return
    /// <see langword="false"/>.
    /// </summary>
    public static bool TryGetSameAssemblyTypeDefinition(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context,
        out TypeDefinitionHandle definition)
    {
        definition = default;
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            definition = (TypeDefinitionHandle)handle;
            return true;
        }
        if (handle.Kind != HandleKind.TypeSpecification)
            return false;

        try
        {
            if (!TypeSpecGuard.TryEnter(reader, (TypeSpecificationHandle)handle, out var scope))
                return false;
            using (scope)
            {
                var origin = reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(TypeDefinitionOriginProvider.Instance, context);
                if (origin.IsDegraded || origin.Handle.Kind != HandleKind.TypeDefinition)
                    return false;
                definition = (TypeDefinitionHandle)origin.Handle;
                return true;
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the full name of a type definition (Namespace.Name), qualifying a
    /// nested type through its declaring type (Outer.Inner).
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaringType = typeDef.GetDeclaringType();
        if (!declaringType.IsNil)
            return $"{GetFullName(reader, reader.GetTypeDefinition(declaringType))}.{reader.GetString(typeDef.Name)}";
        return GetFullName(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
    }

    /// <summary>
    /// Gets the full name of a type (Namespace.Name) from an ApiType-like structure.
    /// </summary>
    public static string GetFullName(string? ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// Renders a generic instantiation by substituting the supplied type arguments
    /// at each <c>`N</c> arity marker in the open type name, preserving the
    /// surrounding text - crucially any trailing nested-type segment such as the
    /// <c>.Enumerator</c> in <c>Dictionary`2.Enumerator</c>. Arguments are consumed
    /// in order across arity markers (so <c>Outer`1.Inner`1</c> with two arguments
    /// becomes <c>Outer&lt;A&gt;.Inner&lt;B&gt;</c>). When the name carries no arity
    /// marker the arguments are appended once, matching the simple
    /// <c>Name&lt;args&gt;</c> form.
    /// </summary>
    public static string ApplyGenericArguments(string genericTypeName, IReadOnlyList<string> typeArguments)
    {
        if (!genericTypeName.Contains('`'))
        {
            return typeArguments.Count == 0
                ? genericTypeName
                : $"{genericTypeName}<{string.Join(", ", typeArguments)}>";
        }

        var result = new System.Text.StringBuilder(genericTypeName.Length + 16);
        var argIndex = 0;
        for (var i = 0; i < genericTypeName.Length; i++)
        {
            if (genericTypeName[i] != '`')
            {
                result.Append(genericTypeName[i]);
                continue;
            }

            var digitStart = i + 1;
            var digitEnd = digitStart;
            while (digitEnd < genericTypeName.Length && char.IsDigit(genericTypeName[digitEnd]))
                digitEnd++;

            if (digitEnd == digitStart
                || !int.TryParse(genericTypeName.AsSpan(digitStart, digitEnd - digitStart), out var arity)
                || arity <= 0)
            {
                result.Append('`');
                continue;
            }

            var take = Math.Min(arity, typeArguments.Count - argIndex);
            result.Append('<');
            for (var k = 0; k < take; k++)
            {
                if (k > 0)
                    result.Append(", ");
                result.Append(typeArguments[argIndex + k]);
            }
            result.Append('>');
            argIndex += take;
            i = digitEnd - 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats raw metadata type names for display by replacing CLR generic arity suffixes
    /// with readable type parameter placeholders.
    /// </summary>
    public static string FormatDisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !typeName.Contains('`'))
            return typeName;

        var result = new System.Text.StringBuilder(typeName.Length + 8);
        for (var i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] != '`')
            {
                result.Append(typeName[i]);
                continue;
            }

            var digitStart = i + 1;
            var digitEnd = digitStart;
            while (digitEnd < typeName.Length && char.IsDigit(typeName[digitEnd]))
                digitEnd++;

            if (digitEnd == digitStart || !int.TryParse(typeName.AsSpan(digitStart, digitEnd - digitStart), out var arity) || arity <= 0)
            {
                result.Append(typeName[i]);
                continue;
            }

            result.Append('<');
            for (var parameterIndex = 1; parameterIndex <= arity; parameterIndex++)
            {
                if (parameterIndex > 1)
                    result.Append(", ");
                result.Append(arity == 1 ? "T" : $"T{parameterIndex}");
            }
            result.Append('>');
            i = digitEnd - 1;
        }

        return result.ToString();
    }

    readonly record struct TypeDefinitionOrigin(EntityHandle Handle, bool IsDegraded);

    sealed class TypeDefinitionOriginProvider : ISignatureTypeProvider<TypeDefinitionOrigin, GenericContext?>
    {
        public static TypeDefinitionOriginProvider Instance { get; } = new();

        public TypeDefinitionOrigin GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
            => new(handle, IsDegraded: false);

        public TypeDefinitionOrigin GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
            => new(handle, IsDegraded: false);

        public TypeDefinitionOrigin GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return new(default, IsDegraded: true);
            using (scope)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            }
        }

        public TypeDefinitionOrigin GetGenericInstantiation(
            TypeDefinitionOrigin genericType,
            ImmutableArray<TypeDefinitionOrigin> typeArguments)
            => new(
                genericType.Handle,
                genericType.IsDegraded || typeArguments.Any(argument => argument.IsDegraded));

        public TypeDefinitionOrigin GetModifiedType(
            TypeDefinitionOrigin modifier,
            TypeDefinitionOrigin unmodifiedType,
            bool isRequired)
            => new(unmodifiedType.Handle, modifier.IsDegraded || unmodifiedType.IsDegraded);

        public TypeDefinitionOrigin GetPinnedType(TypeDefinitionOrigin elementType)
            => elementType;

        public TypeDefinitionOrigin GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
        public TypeDefinitionOrigin GetSZArrayType(TypeDefinitionOrigin elementType) => new(default, elementType.IsDegraded);
        public TypeDefinitionOrigin GetArrayType(TypeDefinitionOrigin elementType, ArrayShape shape) => new(default, elementType.IsDegraded);
        public TypeDefinitionOrigin GetByReferenceType(TypeDefinitionOrigin elementType) => new(default, elementType.IsDegraded);
        public TypeDefinitionOrigin GetPointerType(TypeDefinitionOrigin elementType) => new(default, elementType.IsDegraded);
        public TypeDefinitionOrigin GetGenericMethodParameter(GenericContext? context, int index) => default;
        public TypeDefinitionOrigin GetGenericTypeParameter(GenericContext? context, int index) => default;
        public TypeDefinitionOrigin GetFunctionPointerType(MethodSignature<TypeDefinitionOrigin> signature)
            => new(
                default,
                signature.ReturnType.IsDegraded
                    || signature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }
}
