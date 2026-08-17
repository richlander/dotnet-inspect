using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal readonly record struct ExplicitInterfaceTypeIdentity(
    string Key,
    string MetadataName,
    ImmutableArray<ExplicitInterfaceTypeIdentity> GenericArguments = default,
    bool IsDegraded = false);

internal readonly record struct ExplicitInterfaceSignatureContext(
    GenericContext? Names,
    ImmutableArray<ExplicitInterfaceTypeIdentity> TypeArguments)
{
    public static ExplicitInterfaceSignatureContext Open(GenericContext? names)
        => new(names, []);

    public ExplicitInterfaceSignatureContext WithTypeArguments(
        ImmutableArray<ExplicitInterfaceTypeIdentity> typeArguments)
        => new(Names, typeArguments);
}

internal sealed class ExplicitInterfaceTypeIdentityProvider
    : ISignatureTypeProvider<ExplicitInterfaceTypeIdentity, ExplicitInterfaceSignatureContext>
{
    public static ExplicitInterfaceTypeIdentityProvider Instance { get; } = new();

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
        return new ExplicitInterfaceTypeIdentity(name, name);
    }

    public ExplicitInterfaceTypeIdentity GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        string name = TypeResolver.GetTypeNameFromDefinition(reader, handle);
        return new ExplicitInterfaceTypeIdentity(
            $"[{CurrentModuleKey(reader)}]{name}",
            name);
    }

    public ExplicitInterfaceTypeIdentity GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        string name = TypeResolver.GetTypeNameFromReference(reader, handle);
        return new ExplicitInterfaceTypeIdentity(
            $"[{ResolutionScopeKey(reader, handle)}]{name}",
            name);
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
            $"{elementType.Key}[]",
            $"{elementType.MetadataName}[]",
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetArrayType(
        ExplicitInterfaceTypeIdentity elementType,
        ArrayShape shape)
    {
        string suffix = $"[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
        return new ExplicitInterfaceTypeIdentity(
            elementType.Key + suffix,
            elementType.MetadataName + suffix,
            IsDegraded: elementType.IsDegraded);
    }

    public ExplicitInterfaceTypeIdentity GetByReferenceType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            $"{elementType.Key}&",
            $"{elementType.MetadataName}&",
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetPointerType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            $"{elementType.Key}*",
            $"{elementType.MetadataName}*",
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetPinnedType(ExplicitInterfaceTypeIdentity elementType)
        => new(
            $"pinned {elementType.Key}",
            elementType.MetadataName,
            IsDegraded: elementType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetGenericInstantiation(
        ExplicitInterfaceTypeIdentity genericType,
        ImmutableArray<ExplicitInterfaceTypeIdentity> typeArguments)
        => new(
            ApplyGenericArguments(
                genericType.Key,
                typeArguments.Select(argument => argument.Key).ToArray()),
            ApplyGenericArguments(
                genericType.MetadataName,
                typeArguments.Select(argument => argument.MetadataName).ToArray()),
            typeArguments,
            genericType.IsDegraded || typeArguments.Any(argument => argument.IsDegraded));

    public ExplicitInterfaceTypeIdentity GetGenericMethodParameter(
        ExplicitInterfaceSignatureContext context,
        int index)
    {
        string name = context.Names is not null && index < context.Names.MethodParameters.Count
            ? context.Names.MethodParameters[index]
            : $"TM{index}";
        return new ExplicitInterfaceTypeIdentity($"!!{index}", name);
    }

    public ExplicitInterfaceTypeIdentity GetGenericTypeParameter(
        ExplicitInterfaceSignatureContext context,
        int index)
    {
        if (index < context.TypeArguments.Length)
            return context.TypeArguments[index];

        string name = context.Names is not null && index < context.Names.TypeParameters.Count
            ? context.Names.TypeParameters[index]
            : $"T{index}";
        return new ExplicitInterfaceTypeIdentity($"!{index}", name);
    }

    public ExplicitInterfaceTypeIdentity GetModifiedType(
        ExplicitInterfaceTypeIdentity modifier,
        ExplicitInterfaceTypeIdentity unmodifiedType,
        bool isRequired)
        => new(
            $"{(isRequired ? "modreq" : "modopt")}({modifier.Key}){unmodifiedType.Key}",
            unmodifiedType.MetadataName,
            IsDegraded: modifier.IsDegraded || unmodifiedType.IsDegraded);

    public ExplicitInterfaceTypeIdentity GetFunctionPointerType(
        MethodSignature<ExplicitInterfaceTypeIdentity> signature)
    {
        string key = $"method[{signature.Header.RawValue}:"
            + $"{signature.GenericParameterCount}:{signature.RequiredParameterCount}] "
            + $"{signature.ReturnType.Key} *("
            + string.Join(",", signature.ParameterTypes.Select(parameter => parameter.Key))
            + ")";
        string name = $"method {signature.ReturnType.MetadataName} *("
            + string.Join(",", signature.ParameterTypes.Select(parameter => parameter.MetadataName))
            + ")";
        return new ExplicitInterfaceTypeIdentity(
            key,
            name,
            IsDegraded: signature.ReturnType.IsDegraded
                || signature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    internal static ExplicitInterfaceTypeIdentity FromHandle(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => Instance.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => Instance.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => Instance.GetTypeFromSpecification(
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

    static string ResolutionScopeKey(MetadataReader reader, TypeReferenceHandle handle)
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
            HandleKind.AssemblyReference => AssemblyKey(
                AssemblyReferenceIdentity.From(reader, (AssemblyReferenceHandle)scope)),
            HandleKind.ModuleDefinition when reader.IsAssembly => AssemblyKey(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader)),
            HandleKind.ModuleDefinition => CurrentModuleKey(reader),
            HandleKind.ModuleReference => "module:"
                + reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)scope).Name),
            _ => scope.Kind.ToString()
        };
    }

    static string AssemblyKey(AssemblyReferenceIdentity identity)
        => $"{identity.Name}|{identity.Version}|{identity.Culture}|{identity.PublicKeyToken}";

    static string CurrentModuleKey(MetadataReader reader)
    {
        if (reader.IsAssembly)
            return AssemblyKey(AssemblyReferenceIdentity.FromAssemblyDefinition(reader));

        var module = reader.GetModuleDefinition();
        return "module:"
            + reader.GetString(module.Name)
            + "|"
            + reader.GetGuid(module.Mvid);
    }
}
