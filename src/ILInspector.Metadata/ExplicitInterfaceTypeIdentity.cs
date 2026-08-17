using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

internal readonly record struct ExplicitInterfaceTypeIdentity(
    string Key,
    string MetadataName,
    string? AggregateAliasName = null,
    ImmutableArray<ExplicitInterfaceTypeIdentity> GenericArguments = default,
    int GenericArity = 0,
    bool IsDegraded = false);

internal readonly record struct ExplicitInterfaceSignatureContext(
    GenericContext? Names,
    ImmutableArray<ExplicitInterfaceTypeIdentity> TypeArguments,
    int TypeParameterCount)
{
    public static ExplicitInterfaceSignatureContext Open(GenericContext? names)
        => new(names, [], names?.TypeParameters.Count ?? 0);

    public ExplicitInterfaceSignatureContext WithTypeArguments(
        ImmutableArray<ExplicitInterfaceTypeIdentity> typeArguments,
        int typeParameterCount)
        => new(
            Names,
            typeArguments.IsDefault ? [] : typeArguments,
            typeParameterCount);
}

internal sealed class ExplicitInterfaceTypeIdentityProvider
    : ISignatureTypeProvider<ExplicitInterfaceTypeIdentity, ExplicitInterfaceSignatureContext>
{
    internal const int MaxAssemblyIdentityBlobBytes = 4096;
    readonly Dictionary<AssemblyReferenceHandle, string> assemblyScopeKeys = [];
    string? currentModuleKey;

    public ExplicitInterfaceTypeIdentity GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        string name = typeCode switch
        {
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => typeCode.ToString()
        };
        string? alias = typeCode switch
        {
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",
            _ => null
        };
        return new ExplicitInterfaceTypeIdentity(
            Node("primitive", ((int)typeCode).ToString()),
            name,
            alias);
    }

    public ExplicitInterfaceTypeIdentity GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        var read = MetadataTypeDefinitionNameReader.Read(reader, handle);
        if (read is not MetadataTypeDefinitionNameReadResult.Read result)
            throw new BadImageFormatException("The interface type definition name is malformed.");

        string name = TypeResolver.GetTypeNameFromDefinition(reader, handle);
        return new ExplicitInterfaceTypeIdentity(
            Node(
                "named",
                rawTypeKind.ToString(),
                CurrentModuleKey(reader),
                StructuredNameKey(result.Name.Namespace, result.Name.Segments)),
            name,
            GenericArity: reader.GetTypeDefinition(handle).GetGenericParameters().Count);
    }

    public ExplicitInterfaceTypeIdentity GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        var structuredName = ReferenceName(reader, handle);
        string name = TypeResolver.GetTypeNameFromReference(reader, handle);
        return new ExplicitInterfaceTypeIdentity(
            Node(
                "named",
                rawTypeKind.ToString(),
                ResolutionScopeKey(reader, handle),
                structuredName.Key),
            name,
            GenericArity: structuredName.GenericArity);
    }

    public ExplicitInterfaceTypeIdentity GetTypeFromSpecification(
        MetadataReader reader,
        ExplicitInterfaceSignatureContext context,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
        => GuardedProviderDecode.TypeSpec(
            reader,
            handle,
            this,
            context,
            new ExplicitInterfaceTypeIdentity(
                "<invalid>",
                "<invalid>",
                IsDegraded: true));

    public ExplicitInterfaceTypeIdentity GetSZArrayType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            Node("szarray", elementType.Key),
            $"{elementType.MetadataName}[]",
            elementType.AggregateAliasName is { } alias ? $"{alias}[]" : null,
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetArrayType(
        ExplicitInterfaceTypeIdentity elementType,
        ArrayShape shape)
    {
        string suffix = shape.Rank == 1
            ? "[*]"
            : $"[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
        return new ExplicitInterfaceTypeIdentity(
            Node(
                "array",
                shape.Rank.ToString(),
                string.Join(",", shape.Sizes),
                string.Join(",", shape.LowerBounds),
                elementType.Key),
            elementType.MetadataName + suffix,
            elementType.AggregateAliasName is { } alias ? alias + suffix : null,
            IsDegraded: elementType.IsDegraded);
    }

    public ExplicitInterfaceTypeIdentity GetByReferenceType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            Node("byref", elementType.Key),
            $"{elementType.MetadataName}&",
            elementType.AggregateAliasName is { } alias ? $"{alias}&" : null,
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetPointerType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            Node("pointer", elementType.Key),
            $"{elementType.MetadataName}*",
            elementType.AggregateAliasName is { } alias ? $"{alias}*" : null,
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetPinnedType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            Node("pinned", elementType.Key),
            elementType.MetadataName,
            elementType.AggregateAliasName,
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetGenericInstantiation(
        ExplicitInterfaceTypeIdentity genericType,
        ImmutableArray<ExplicitInterfaceTypeIdentity> typeArguments)
    {
        string? aggregateAlias = (genericType.MetadataName == "System.Nullable"
            || genericType.MetadataName.StartsWith(
                "System.Nullable<",
                StringComparison.Ordinal)
            || genericType.MetadataName.StartsWith(
                "System.Nullable`",
                StringComparison.Ordinal))
            && typeArguments is [var nullableArgument]
                ? $"{nullableArgument.AggregateAliasName ?? nullableArgument.MetadataName}?"
                : typeArguments.Any(argument => argument.AggregateAliasName is not null)
                    ? ApplyGenericArguments(
                        genericType.MetadataName,
                        typeArguments
                            .Select(argument => argument.AggregateAliasName ?? argument.MetadataName)
                            .ToArray())
                    : null;
        return new(
            Node(
                "generic",
                [genericType.Key, .. typeArguments.Select(argument => argument.Key)]),
            ApplyGenericArguments(
                genericType.MetadataName,
                typeArguments.Select(argument => argument.MetadataName).ToArray()),
            aggregateAlias,
            typeArguments,
            genericType.GenericArity,
            genericType.IsDegraded || typeArguments.Any(argument => argument.IsDegraded));
    }

    public ExplicitInterfaceTypeIdentity GetGenericMethodParameter(
        ExplicitInterfaceSignatureContext context,
        int index)
    {
        string name = context.Names is not null && index < context.Names.MethodParameters.Count
            ? context.Names.MethodParameters[index]
            : $"TM{index}";
        return new ExplicitInterfaceTypeIdentity(Node("mvar", index.ToString()), name);
    }

    public ExplicitInterfaceTypeIdentity GetGenericTypeParameter(
        ExplicitInterfaceSignatureContext context,
        int index)
    {
        if (!context.TypeArguments.IsDefault && index < context.TypeArguments.Length)
            return context.TypeArguments[index];

        string name = context.Names is not null && index < context.Names.TypeParameters.Count
            ? context.Names.TypeParameters[index]
            : $"T{index}";
        return index < context.TypeParameterCount
            ? new ExplicitInterfaceTypeIdentity(Node("var", index.ToString()), name)
            : new ExplicitInterfaceTypeIdentity(
                Node("invalid-var", index.ToString()),
                name,
                IsDegraded: true);
    }

    public ExplicitInterfaceTypeIdentity GetModifiedType(
        ExplicitInterfaceTypeIdentity modifier,
        ExplicitInterfaceTypeIdentity unmodifiedType,
        bool isRequired)
        => new(
            Node(isRequired ? "modreq" : "modopt", modifier.Key, unmodifiedType.Key),
            unmodifiedType.MetadataName,
            unmodifiedType.AggregateAliasName,
            IsDegraded: modifier.IsDegraded || unmodifiedType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetFunctionPointerType(
        MethodSignature<ExplicitInterfaceTypeIdentity> signature)
    {
        string key = Node(
            "fnptr",
            [
                signature.Header.RawValue.ToString(),
                signature.GenericParameterCount.ToString(),
                signature.RequiredParameterCount.ToString(),
                signature.ReturnType.Key,
                .. signature.ParameterTypes.Select(parameter => parameter.Key)
            ]);
        string name = $"method {signature.ReturnType.MetadataName} *("
            + string.Join(",", signature.ParameterTypes.Select(parameter => parameter.MetadataName))
            + ")";
        return new ExplicitInterfaceTypeIdentity(
            key,
            name,
            IsDegraded: signature.ReturnType.IsDegraded
                || signature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    internal ExplicitInterfaceTypeIdentity FromHandle(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(
                reader,
                ExplicitInterfaceSignatureContext.Open(context),
                (TypeSpecificationHandle)handle,
                rawTypeKind: 0),
            _ => new ExplicitInterfaceTypeIdentity(
                "<invalid>",
                "<invalid>",
                IsDegraded: true)
        };

    static string ApplyGenericArguments(string typeName, IReadOnlyList<string> typeArguments)
        => TypeResolver.ApplyGenericArguments(typeName, typeArguments)
            .Replace(", ", ",", StringComparison.Ordinal);

    string ResolutionScopeKey(MetadataReader reader, TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                chain,
                out int consumed,
                out _,
                out var rejection)
            || consumed == 0)
        {
            throw new BadImageFormatException(
                rejection?.Detail
                    ?? "The interface type has an invalid resolution-scope chain.");
        }

        EntityHandle scope = reader.GetTypeReference(chain[0]).ResolutionScope;
        return scope.Kind switch
        {
            HandleKind.AssemblyReference => AssemblyScopeKey(
                reader,
                (AssemblyReferenceHandle)scope),
            HandleKind.ModuleDefinition => CurrentModuleKey(reader),
            HandleKind.ModuleReference => Node(
                "module",
                BoundedString(
                    reader,
                    reader.GetModuleReference((ModuleReferenceHandle)scope).Name)),
            _ => Node("scope", scope.Kind.ToString())
        };
    }

    internal static string AssemblyKey(AssemblyReferenceIdentity identity)
        => Node(
            "assembly",
            identity.Name.ToUpperInvariant(),
            identity.Version?.ToString() ?? "",
            NormalizeCulture(identity.Culture).ToUpperInvariant(),
            (identity.PublicKeyToken ?? "").ToUpperInvariant());

    string CurrentModuleKey(MetadataReader reader)
    {
        if (currentModuleKey is not null)
            return currentModuleKey;

        if (reader.IsAssembly)
        {
            ValidateAssemblyIdentity(reader, reader.GetAssemblyDefinition());
            return currentModuleKey = AssemblyKey(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
        }

        var module = reader.GetModuleDefinition();
        return currentModuleKey = Node(
            "module",
            BoundedString(reader, module.Name),
            reader.GetGuid(module.Mvid).ToString());
    }

    string AssemblyScopeKey(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        if (assemblyScopeKeys.TryGetValue(handle, out string? key))
            return key;

        var reference = reader.GetAssemblyReference(handle);
        ValidateAssemblyIdentity(reader, reference);
        key = AssemblyKey(AssemblyReferenceIdentity.From(reader, handle));
        assemblyScopeKeys.Add(handle, key);
        return key;
    }

    static void ValidateAssemblyIdentity(
        MetadataReader reader,
        System.Reflection.Metadata.AssemblyReference reference)
    {
        _ = BoundedString(reader, reference.Name);
        if (!reference.Culture.IsNil)
            _ = BoundedString(reader, reference.Culture);
        ValidateAssemblyIdentityBlob(reader, reference.PublicKeyOrToken);
    }

    static void ValidateAssemblyIdentity(MetadataReader reader, AssemblyDefinition definition)
    {
        _ = BoundedString(reader, definition.Name);
        if (!definition.Culture.IsNil)
            _ = BoundedString(reader, definition.Culture);
        ValidateAssemblyIdentityBlob(reader, definition.PublicKey);
    }

    static void ValidateAssemblyIdentityBlob(MetadataReader reader, BlobHandle handle)
    {
        if (!handle.IsNil
            && reader.GetBlobReader(handle).Length > MaxAssemblyIdentityBlobBytes)
        {
            throw new BadImageFormatException(
                "The assembly identity key blob exceeds the inspection limit.");
        }
    }

    static string BoundedString(MetadataReader reader, StringHandle handle)
    {
        if (reader.GetBlobReader(handle).Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
            throw new BadImageFormatException("The metadata identity name exceeds the inspection limit.");
        return reader.GetString(handle);
    }

    static (string Key, int GenericArity) ReferenceName(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                chain,
                out int consumed,
                out _,
                out var rejection)
            || consumed == 0)
        {
            throw new BadImageFormatException(
                rejection?.Detail
                    ?? "The interface type has an invalid resolution-scope chain.");
        }

        var outer = reader.GetTypeReference(chain[0]);
        string @namespace = outer.Namespace.IsNil
            ? ""
            : BoundedString(reader, outer.Namespace);
        string[] segments = new string[consumed];
        int totalCharacters = @namespace.Length;
        int genericArity = 0;
        for (int i = 0; i < consumed; i++)
        {
            segments[i] = BoundedString(reader, reader.GetTypeReference(chain[i]).Name);
            totalCharacters += segments[i].Length + 1;
            genericArity += GenericArity(segments[i]);
            if (totalCharacters > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                throw new BadImageFormatException(
                    "The interface type name exceeds the inspection limit.");
            }
        }
        return (
            StructuredNameKey(@namespace, segments),
            genericArity);
    }

    static string StructuredNameKey(
        string @namespace,
        IEnumerable<string> segments)
        => Node("type-name", [@namespace, .. segments]);

    static int GenericArity(string name)
    {
        int separator = name.LastIndexOf('`');
        return separator >= 0
            && int.TryParse(name[(separator + 1)..], out int arity)
                ? arity
                : 0;
    }

    static string NormalizeCulture(string? value)
        => string.IsNullOrEmpty(value)
            || value.Equals("neutral", StringComparison.OrdinalIgnoreCase)
                ? ""
                : value;

    static string Node(string tag, params string[] parts)
    {
        var builder = new StringBuilder(tag);
        foreach (string part in parts)
        {
            builder.Append('|');
            builder.Append(part.Length);
            builder.Append(':');
            builder.Append(part);
        }
        return builder.ToString();
    }
}
