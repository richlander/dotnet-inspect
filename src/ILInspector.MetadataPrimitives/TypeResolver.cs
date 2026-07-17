using System.Reflection.Metadata;
using System.Text;

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
        try
        {
            var typeRef = reader.GetTypeReference(handle);
            if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
                return GetFullName(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, handle);
        }

        return ResolveTypeNameFromReference(reader, handle).GetValueOrThrow();
    }

    /// <summary>
    /// Resolves a TypeReference name through a bounded, cycle-aware
    /// resolution-scope walk.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveTypeNameFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        try
        {
            var typeRef = reader.GetTypeReference(handle);
            if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
            {
                return CompleteLeafName(
                    reader,
                    typeRef.Namespace,
                    typeRef.Name,
                    handle,
                    consumedNodes: 1);
            }
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }

        return FormatChain(
            reader,
            MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(reader, handle),
            current =>
            {
                var typeRef = reader.GetTypeReference(current);
                return (typeRef.Namespace, typeRef.Name);
            },
            static current => current);
    }

    /// <summary>
    /// Gets the type name from a TypeDefinition handle.
    /// </summary>
    public static string GetTypeNameFromDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        try
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.GetDeclaringType().IsNil)
                return GetFullName(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, handle);
        }

        return ResolveTypeNameFromDefinition(reader, handle).GetValueOrThrow();
    }

    /// <summary>
    /// Resolves a TypeDefinition name through a bounded, cycle-aware
    /// declaring-type walk.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveTypeNameFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        try
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.GetDeclaringType().IsNil)
            {
                return CompleteLeafName(
                    reader,
                    typeDef.Namespace,
                    typeDef.Name,
                    handle,
                    consumedNodes: 1);
            }
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }

        return FormatChain(
            reader,
            MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(reader, handle),
            current =>
            {
                var typeDef = reader.GetTypeDefinition(current);
                return (typeDef.Namespace, typeDef.Name);
            },
            static current => current);
    }

    /// <summary>
    /// Resolves an ExportedType name through a bounded, cycle-aware
    /// implementation walk.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveTypeNameFromExportedType(
        MetadataReader reader,
        ExportedTypeHandle handle)
    {
        try
        {
            var exportedType = reader.GetExportedType(handle);
            if (exportedType.Implementation.Kind != HandleKind.ExportedType)
            {
                return CompleteLeafName(
                    reader,
                    exportedType.Namespace,
                    exportedType.Name,
                    handle,
                    consumedNodes: 1);
            }
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }

        return FormatChain(
            reader,
            MetadataRelationshipTraversal.WalkExportedTypeImplementationChain(reader, handle),
            current =>
            {
                var exportedType = reader.GetExportedType(current);
                return (exportedType.Namespace, exportedType.Name);
            },
            static current => current);
    }

    /// <summary>
    /// Gets an ExportedType name or throws at a caller-owned failure boundary.
    /// </summary>
    public static string GetTypeNameFromExportedType(
        MetadataReader reader,
        ExportedTypeHandle handle)
    {
        try
        {
            var exportedType = reader.GetExportedType(handle);
            if (exportedType.Implementation.Kind != HandleKind.ExportedType)
                return GetFullName(reader.GetString(exportedType.Namespace), reader.GetString(exportedType.Name));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, handle);
        }

        return ResolveTypeNameFromExportedType(reader, handle).GetValueOrThrow();
    }

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
    /// Gets the full name of a type definition (Namespace.Name), qualifying a
    /// nested type through its declaring type (Outer.Inner).
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeDefinition typeDef)
        => ResolveFullName(reader, typeDef).GetValueOrThrow();

    /// <summary>
    /// Resolves the full name of a TypeDefinition value whose handle is not
    /// available to the caller.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveFullName(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        try
        {
            var declaringType = typeDef.GetDeclaringType();
            if (declaringType.IsNil)
            {
                return CompleteLeafName(
                    reader,
                    typeDef.Namespace,
                    typeDef.Name,
                    default,
                    consumedNodes: 1);
            }

            return AppendLeaf(
                reader,
                ResolveTypeNameFromDefinition(reader, declaringType),
                typeDef.Name,
                declaringType);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
    }

    /// <summary>
    /// Gets the full name of a type (Namespace.Name) from an ApiType-like structure.
    /// </summary>
    public static string GetFullName(string? ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// Resolves the full name of a TypeReference value whose handle is not
    /// available to the caller.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveFullName(
        MetadataReader reader,
        TypeReference typeRef)
    {
        try
        {
            var resolutionScope = typeRef.ResolutionScope;
            if (resolutionScope.Kind != HandleKind.TypeReference)
            {
                return CompleteLeafName(
                    reader,
                    typeRef.Namespace,
                    typeRef.Name,
                    resolutionScope,
                    consumedNodes: 1);
            }

            return AppendLeaf(
                reader,
                ResolveTypeNameFromReference(reader, (TypeReferenceHandle)resolutionScope),
                typeRef.Name,
                resolutionScope);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
    }

    /// <summary>
    /// Resolves the full name of an ExportedType value whose handle is not
    /// available to the caller.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveFullName(
        MetadataReader reader,
        ExportedType exportedType)
    {
        try
        {
            var implementation = exportedType.Implementation;
            if (implementation.Kind != HandleKind.ExportedType)
            {
                return CompleteLeafName(
                    reader,
                    exportedType.Namespace,
                    exportedType.Name,
                    implementation,
                    consumedNodes: 1);
            }

            return AppendLeaf(
                reader,
                ResolveTypeNameFromExportedType(reader, (ExportedTypeHandle)implementation),
                exportedType.Name,
                implementation);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
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

    static RelationshipTraversalResult<string> FormatChain<THandle>(
        MetadataReader reader,
        RelationshipTraversalResult<RelationshipChain<THandle>> traversal,
        Func<THandle, (StringHandle Namespace, StringHandle Name)> getName,
        Func<THandle, EntityHandle> getSubject)
        where THandle : struct
    {
        if (traversal is RelationshipTraversalResult<RelationshipChain<THandle>>.Rejected rejected)
            return Reject<string>(rejected.Rejection);

        var chain = ((RelationshipTraversalResult<RelationshipChain<THandle>>.Completed)traversal).Value;
        var builder = new StringBuilder();
        for (int i = 0; i < chain.Handles.Length; i++)
        {
            var handle = chain.Handles[i];
            try
            {
                var (namespaceHandle, nameHandle) = getName(handle);
                if (i == 0)
                {
                    string ns = reader.GetString(namespaceHandle);
                    if (ns.Length > 0)
                    {
                        builder.Append(ns);
                        builder.Append('.');
                    }
                }
                else
                {
                    builder.Append('.');
                }

                builder.Append(reader.GetString(nameHandle));
            }
            catch (BadImageFormatException ex)
            {
                return Malformed<string>(ex, getSubject(handle), consumedNodes: i + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed<string>(ex, getSubject(handle), consumedNodes: i + 1);
            }
        }

        return new RelationshipTraversalResult<string>.Completed(
            builder.ToString(),
            chain.Handles.Length);
    }

    static RelationshipTraversalResult<string> AppendLeaf(
        MetadataReader reader,
        RelationshipTraversalResult<string> declaringName,
        StringHandle leafName,
        EntityHandle subject)
    {
        if (declaringName is RelationshipTraversalResult<string>.Rejected rejected)
            return Reject<string>(rejected.Rejection);

        var completed = (RelationshipTraversalResult<string>.Completed)declaringName;
        if (completed.ConsumedNodes >= MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            return new RelationshipTraversalResult<string>.Rejected(
                new RelationshipTraversalRejection(
                    RelationshipTraversalRejectionKind.NodeBudget,
                    $"The metadata relationship exceeds "
                    + $"{MetadataSafetyPolicy.MaxRelationshipNodes} nodes.",
                    subject,
                    completed.ConsumedNodes));
        }

        try
        {
            return new RelationshipTraversalResult<string>.Completed(
                $"{completed.Value}.{reader.GetString(leafName)}",
                completed.ConsumedNodes + 1);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, subject, completed.ConsumedNodes + 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, subject, completed.ConsumedNodes + 1);
        }
    }

    static RelationshipTraversalResult<string> CompleteLeafName(
        MetadataReader reader,
        StringHandle namespaceHandle,
        StringHandle nameHandle,
        EntityHandle subject,
        int consumedNodes)
    {
        try
        {
            return new RelationshipTraversalResult<string>.Completed(
                GetFullName(reader.GetString(namespaceHandle), reader.GetString(nameHandle)),
                consumedNodes);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, subject, consumedNodes);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, subject, consumedNodes);
        }
    }

    static RelationshipTraversalResult<T> Malformed<T>(
        Exception exception,
        EntityHandle subject,
        int consumedNodes)
        where T : notnull
        => new RelationshipTraversalResult<T>.Rejected(
            new RelationshipTraversalRejection(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                exception.Message,
                subject,
                consumedNodes));

    static RelationshipTraversalResult<T> Reject<T>(
        RelationshipTraversalRejection rejection)
        where T : notnull
        => new RelationshipTraversalResult<T>.Rejected(rejection);

    static string ThrowMalformed(
        Exception exception,
        EntityHandle subject)
        => Malformed<string>(exception, subject, consumedNodes: 1).GetValueOrThrow();
}
