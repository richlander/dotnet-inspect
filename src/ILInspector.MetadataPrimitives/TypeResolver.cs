using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves type names from metadata handles.
/// </summary>
public static class TypeResolver
{
    /// <summary>
    /// Strictly resolves a type name while preserving absence and typed
    /// relationship or signature rejection as distinct outcomes.
    /// </summary>
    public static MetadataTypeNameResult ResolveTypeName(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context = null)
    {
        if (handle.IsNil)
            return new MetadataTypeNameResult.Absent();

        return handle.Kind switch
        {
            HandleKind.TypeReference => FromRelationship(
                ResolveTypeNameFromReference(reader, (TypeReferenceHandle)handle)),
            HandleKind.TypeDefinition => FromRelationship(
                ResolveTypeNameFromDefinition(reader, (TypeDefinitionHandle)handle)),
            HandleKind.TypeSpecification => FromSignature(
                DecodeTypeNameFromSpecification(
                    reader,
                    (TypeSpecificationHandle)handle,
                    context),
                (TypeSpecificationHandle)handle),
            _ => new MetadataTypeNameResult.Absent(),
        };
    }

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
    {
        TypeDefinitionHandle declaringType;
        try
        {
            declaringType = typeDef.GetDeclaringType();
            if (declaringType.IsNil)
                return GetFullName(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, default, consumedNodes: 0);
        }

        return AppendLeaf(
            reader,
            ResolveTypeNameFromDefinition(reader, declaringType),
            typeDef.Name,
            declaringType).GetValueOrThrow();
    }

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
    /// Gets the full name of a type reference, qualifying a nested type through
    /// its resolution-scope chain.
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeReference typeRef)
    {
        EntityHandle resolutionScope;
        try
        {
            resolutionScope = typeRef.ResolutionScope;
            if (resolutionScope.Kind != HandleKind.TypeReference)
                return GetFullName(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, default, consumedNodes: 0);
        }

        return AppendLeaf(
            reader,
            ResolveTypeNameFromReference(reader, (TypeReferenceHandle)resolutionScope),
            typeRef.Name,
            resolutionScope).GetValueOrThrow();
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
    /// Gets the full name of an exported type, qualifying a nested type through
    /// its implementation chain.
    /// </summary>
    public static string GetFullName(MetadataReader reader, ExportedType exportedType)
    {
        EntityHandle implementation;
        try
        {
            implementation = exportedType.Implementation;
            if (implementation.Kind != HandleKind.ExportedType)
                return GetFullName(reader.GetString(exportedType.Namespace), reader.GetString(exportedType.Name));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, default, consumedNodes: 0);
        }

        return AppendLeaf(
            reader,
            ResolveTypeNameFromExportedType(reader, (ExportedTypeHandle)implementation),
            exportedType.Name,
            implementation).GetValueOrThrow();
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
        ArgumentNullException.ThrowIfNull(genericTypeName);
        ArgumentNullException.ThrowIfNull(typeArguments);
        if (!genericTypeName.Contains('`'))
        {
            return typeArguments.Count == 0
                ? genericTypeName
                : $"{genericTypeName}<{string.Join(", ", typeArguments)}>";
        }

        int argIndex = 0;
        return RewriteAritySegments(
            genericTypeName,
            (int arity, StringBuilder builder) =>
            {
                int take = Math.Min(arity, typeArguments.Count - argIndex);
                builder.Append('<');
                for (int k = 0; k < take; k++)
                {
                    if (k > 0)
                        builder.Append(", ");
                    builder.Append(typeArguments[argIndex + k]);
                }
                builder.Append('>');
                argIndex += take;
                return true;
            });
    }

    /// <summary>
    /// Formats raw metadata type names for display by replacing CLR generic arity suffixes
    /// with readable type parameter placeholders.
    /// </summary>
    public static string FormatDisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !typeName.Contains('`'))
            return typeName;

        return RewriteAritySegments(
            typeName,
            static (int arity, StringBuilder builder) =>
            {
                if (arity > MaxDisplayedPlaceholders)
                    return false;

                builder.Append('<');
                for (int parameterIndex = 1; parameterIndex <= arity; parameterIndex++)
                {
                    if (parameterIndex > 1)
                        builder.Append(", ");
                    builder.Append(arity == 1 ? "T" : $"T{parameterIndex}");
                }
                builder.Append('>');
                return true;
            });
    }

    /// <summary>
    /// The most type-parameter placeholders <see cref="FormatDisplayName"/> will
    /// synthesize for one arity marker. A canonical arity reaches
    /// <see cref="MetadataNameArity.MaxArity"/>, and expanding that many
    /// placeholders would turn a short hostile name into a megabyte of display
    /// text, so a larger arity keeps its raw <c>`N</c> spelling: the name stays
    /// visible and bounded instead of being rendered or silently dropped.
    /// </summary>
    internal const int MaxDisplayedPlaceholders = 64;

    /// <summary>
    /// Rewrites every canonical generic-arity suffix in a flattened type name,
    /// leaving all other text — including a backtick that is not a canonical
    /// suffix — exactly as it was. Suffix recognition is
    /// <see cref="MetadataNameArity"/>'s, applied to each <c>.</c>/<c>+</c>
    /// component, so an arbitrary digit run (<c>Bomb`2147483647</c>), a
    /// non-ASCII digit, a signed or padded count, or digits followed by more text
    /// is text, not arity. <paramref name="render"/> returns false to decline a
    /// marker, which restores its raw spelling.
    /// </summary>
    static string RewriteAritySegments(string name, Func<int, StringBuilder, bool> render)
    {
        var result = new StringBuilder(name.Length + 16);
        foreach (MetadataNameComponent component in MetadataNameArity.EnumerateComponents(name))
        {
            ReadOnlySpan<char> text = name.AsSpan(component.Start, component.Length);
            ReadOnlySpan<char> decoration = text[TypeDecorationStart(text)..];
            ReadOnlySpan<char> metadataName = text[..^decoration.Length];

            if (MetadataNameArity.TryReadSuffix(metadataName, out int arity, out int simpleNameLength))
            {
                result.Append(metadataName[..simpleNameLength]);
                int beforeRender = result.Length;
                if (render(arity, result))
                {
                    result.Append(decoration);
                }
                else
                {
                    result.Length = beforeRender;
                    result.Append(text[simpleNameLength..]);
                }
            }
            else
            {
                result.Append(text);
            }

            if (component.Delimiter is { } delimiter)
                result.Append(delimiter);
        }

        return result.ToString();
    }

    /// <summary>
    /// Where a component's metadata name ends and signature decoration —
    /// array, pointer, by-ref, or nullable syntax — begins. Decoration is
    /// display syntax rather than name text, so it is set aside before the arity
    /// grammar is applied and restored after the arguments
    /// (<c>List`1[]</c> renders <c>List&lt;T&gt;[]</c>).
    /// </summary>
    /// <remarks>
    /// Decoration is a suffix, so it is measured from the end. Scanning from the
    /// start would cut a compiler-generated name at its leading
    /// <c>&lt;&gt;</c> — <c>&lt;&gt;c__DisplayClass0_0`1</c> is a name, not a
    /// decorated one — and leave its arity marker unexpanded in emitted C#.
    /// </remarks>
    static int TypeDecorationStart(ReadOnlySpan<char> component)
    {
        int start = component.Length;
        while (start > 0 && component[start - 1] is '[' or ']' or '*' or '&' or '?' or ',')
            start--;

        return start;
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

    static MetadataTypeNameResult FromRelationship(
        RelationshipTraversalResult<string> result)
        => result switch
        {
            RelationshipTraversalResult<string>.Completed completed =>
                new MetadataTypeNameResult.Resolved(completed.Value),
            RelationshipTraversalResult<string>.Rejected rejected =>
                new MetadataTypeNameResult.Rejected(
                    MetadataTypeNameFailure.From(rejected.Rejection)),
            _ => throw new InvalidOperationException(
                "Unknown metadata relationship traversal result."),
        };

    static MetadataTypeNameResult FromSignature(
        SignatureDecodeResult<string> result,
        TypeSpecificationHandle subject)
        => result switch
        {
            SignatureDecodeResult<string>.Decoded decoded =>
                new MetadataTypeNameResult.Resolved(decoded.Value),
            SignatureDecodeResult<string>.Rejected rejected =>
                new MetadataTypeNameResult.Rejected(
                    MetadataTypeNameFailure.From(rejected.Rejection, subject)),
            _ => throw new InvalidOperationException(
                "Unknown metadata signature decode result."),
        };

    static string ThrowMalformed(
        Exception exception,
        EntityHandle subject,
        int consumedNodes = 1)
        => Malformed<string>(exception, subject, consumedNodes).GetValueOrThrow();
}
