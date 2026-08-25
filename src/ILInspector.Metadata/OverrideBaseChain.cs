using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// One same-image base class reached from a derived type, together with the
/// exact generic arguments that instantiate it, expressed in the derived
/// type's own generic scope.
/// </summary>
/// <param name="Definition">The base <c>TypeDef</c> in this image.</param>
/// <param name="TypeArguments">
/// The instantiation of <paramref name="Definition"/>'s type parameters, or
/// <see langword="null"/> when no substitution applies because the base is
/// reached without a <c>TypeSpec</c> (the identity case).
/// </param>
internal readonly record struct OverrideBaseInstantiation(
    TypeDefinitionHandle Definition,
    ImmutableArray<TypeNode>? TypeArguments);

/// <summary>
/// Bounded same-image base-class and interface traversal used by override-slot
/// authentication. A compiler encodes <c>Derived&lt;T&gt; : Base&lt;T&gt;</c>
/// and <c>Derived : Base&lt;string&gt;</c> as a <c>TypeSpec</c> base, so a walk
/// that only follows <c>TypeDef</c> bases stops at the first constructed
/// generic base and cannot see the slot the derived type actually overrides,
/// nor the ancestor a covariant return actually reaches.
/// Every step here keeps the exact <c>TypeDef</c> identity and the exact
/// generic arguments; no step matches a rendered name.
/// </summary>
internal static class OverrideBaseChain
{
    /// <summary>
    /// Walks the same-image base chain of <paramref name="derivedHandle"/>,
    /// stopping at the first base that leaves this image, is not a constructed
    /// generic instantiation of a same-image definition, or cannot be decoded.
    /// The chain is bounded by
    /// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/>.
    /// </summary>
    internal static List<OverrideBaseInstantiation> SameAssemblyBases(
        MetadataReader reader,
        TypeDefinitionHandle derivedHandle)
    {
        var chain = new List<OverrideBaseInstantiation>();
        var visited = new HashSet<TypeDefinitionHandle> { derivedHandle };
        TypeDefinitionHandle current = derivedHandle;
        ImmutableArray<TypeNode>? currentArguments = null;

        while (chain.Count < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            TypeDefinition currentType;
            EntityHandle baseHandle;
            try
            {
                currentType = reader.GetTypeDefinition(current);
                baseHandle = currentType.BaseType;
            }
            catch (Exception exception)
                when (exception is BadImageFormatException
                    or ArgumentException
                    or InvalidOperationException)
            {
                return chain;
            }

            TypeDefinitionHandle nextHandle;
            ImmutableArray<TypeNode>? nextArguments;
            if (baseHandle.Kind == HandleKind.TypeDefinition)
            {
                nextHandle = (TypeDefinitionHandle)baseHandle;
                if (!IsNonGenericDefinition(reader, nextHandle))
                    return chain;
                nextArguments = null;
            }
            else if (baseHandle.Kind == HandleKind.TypeSpecification)
            {
                if (!TryReadConstructedBase(
                        reader,
                        (TypeSpecificationHandle)baseHandle,
                        currentType,
                        currentArguments,
                        out nextHandle,
                        out ImmutableArray<TypeNode> decodedArguments))
                {
                    return chain;
                }

                nextArguments = decodedArguments;
            }
            else
            {
                return chain;
            }

            if (!visited.Add(nextHandle))
                return chain;

            chain.Add(new OverrideBaseInstantiation(nextHandle, nextArguments));
            current = nextHandle;
            currentArguments = nextArguments;
        }

        return chain;
    }

    /// <summary>
    /// True when every base link above <paramref name="derivedHandle"/> stays
    /// inside this image until the chain terminates at the authenticated
    /// <c>System.Object</c> of a recognized core library. A base that leaves
    /// the image as anything else, an undecodable or non-constructed generic
    /// base, a cycle, and a chain longer than
    /// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/> all fail closed,
    /// because each leaves room for an unseen base to own the slot.
    /// </summary>
    internal static bool ReachesAuthenticatedObjectRoot(
        MetadataReader reader,
        TypeDefinitionHandle derivedHandle)
    {
        var visited = new HashSet<TypeDefinitionHandle> { derivedHandle };
        TypeDefinitionHandle current = derivedHandle;

        for (int step = 0;
            step < MetadataSafetyPolicy.MaxRelationshipNodes;
            step++)
        {
            EntityHandle baseHandle;
            try
            {
                baseHandle = reader.GetTypeDefinition(current).BaseType;
            }
            catch (Exception exception)
                when (exception is BadImageFormatException
                    or ArgumentException
                    or InvalidOperationException)
            {
                return false;
            }

            if (baseHandle.IsNil)
                return false;

            if ((baseHandle.Kind is HandleKind.TypeReference
                    or HandleKind.TypeDefinition)
                && ApiSurfaceExtractor.IsSystemObjectType(reader, baseHandle))
            {
                return true;
            }

            TypeDefinitionHandle next;
            if (baseHandle.Kind == HandleKind.TypeDefinition)
            {
                next = (TypeDefinitionHandle)baseHandle;
            }
            else if (baseHandle.Kind == HandleKind.TypeSpecification
                && TryReadGenericInstantiationHeader(
                    reader,
                    (TypeSpecificationHandle)baseHandle,
                    out TypeDefinitionHandle instantiated,
                    out _))
            {
                next = instantiated;
            }
            else
            {
                return false;
            }

            if (!visited.Add(next))
                return false;

            current = next;
        }

        return false;
    }

    /// <summary>
    /// Appends the direct same-image supertypes of one instantiated type --
    /// its base class and every interface it implements -- to
    /// <paramref name="supertypes"/>. Each result keeps the exact generic
    /// arguments that instantiate it, rewritten through
    /// <paramref name="type"/>'s own arguments, so the whole set is expressed
    /// in the scope of the type the caller started from.
    ///
    /// Fails closed by omission. A supertype that leaves this image, a
    /// <c>TypeDef</c> row naming a generic definition whose arguments the row
    /// cannot carry, a <c>TypeSpec</c> that is not a generic instantiation of
    /// a same-image definition, and any undecodable, degraded, or over-budget
    /// row are all left out, so no caller can prove ancestry through evidence
    /// this image does not carry. Gated by
    /// <c>SameAssemblyOverrideSlot_AuthenticatesCovariantReturnThroughConstructedGenericAncestry</c>,
    /// <c>SameAssemblyOverrideSlot_AuthenticatesCovariantInterfaceReturnThroughConstructedGenericAncestry</c>,
    /// and
    /// <c>SameAssemblyOverrideSlot_DeclinesConstructedGenericAncestryWithDifferentArgument</c>.
    /// </summary>
    internal static void AddDirectSameAssemblySupertypes(
        MetadataReader reader,
        OverrideBaseInstantiation type,
        List<OverrideBaseInstantiation> supertypes)
    {
        TypeDefinition definition;
        EntityHandle baseHandle;
        try
        {
            definition = reader.GetTypeDefinition(type.Definition);
            baseHandle = definition.BaseType;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return;
        }

        if (TryReadSupertype(
                reader,
                definition,
                type.TypeArguments,
                baseHandle,
                out OverrideBaseInstantiation baseType))
        {
            supertypes.Add(baseType);
        }

        try
        {
            foreach (InterfaceImplementationHandle implementationHandle
                in definition.GetInterfaceImplementations())
            {
                EntityHandle interfaceHandle = reader
                    .GetInterfaceImplementation(implementationHandle)
                    .Interface;
                if (TryReadSupertype(
                        reader,
                        definition,
                        type.TypeArguments,
                        interfaceHandle,
                        out OverrideBaseInstantiation implemented))
                {
                    supertypes.Add(implemented);
                }
            }
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
        }
    }

    static bool TryReadSupertype(
        MetadataReader reader,
        TypeDefinition declaringType,
        ImmutableArray<TypeNode>? substitution,
        EntityHandle handle,
        out OverrideBaseInstantiation supertype)
    {
        supertype = default;
        if (handle.IsNil)
            return false;

        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var definition = (TypeDefinitionHandle)handle;
            if (!IsNonGenericDefinition(reader, definition))
                return false;

            supertype = new OverrideBaseInstantiation(definition, null);
            return true;
        }

        if (handle.Kind != HandleKind.TypeSpecification
            || !TryReadConstructedBase(
                reader,
                (TypeSpecificationHandle)handle,
                declaringType,
                substitution,
                out TypeDefinitionHandle instantiated,
                out ImmutableArray<TypeNode> arguments))
        {
            return false;
        }

        supertype = new OverrideBaseInstantiation(instantiated, arguments);
        return true;
    }

    /// <summary>
    /// Decodes a constructed-generic base or interface <c>TypeSpec</c> written
    /// in <paramref name="declaringType"/>'s generic scope into the same-image
    /// definition it instantiates and the exact arguments, rewritten through
    /// <paramref name="substitution"/> so the result is expressed in the
    /// original derived type's scope.
    /// </summary>
    internal static bool TryReadConstructedBase(
        MetadataReader reader,
        TypeSpecificationHandle specificationHandle,
        TypeDefinition declaringType,
        ImmutableArray<TypeNode>? substitution,
        out TypeDefinitionHandle definition,
        out ImmutableArray<TypeNode> arguments)
    {
        definition = default;
        arguments = [];
        if (!TryReadGenericInstantiationHeader(
                reader,
                specificationHandle,
                out TypeDefinitionHandle instantiated,
                out int argumentCount))
        {
            return false;
        }

        GenericParameterHandleCollection parameters;
        try
        {
            parameters = reader
                .GetTypeDefinition(instantiated)
                .GetGenericParameters();
            GenericContext.ValidateParameterIndices(reader, parameters);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return false;
        }

        if (parameters.Count != argumentCount)
            return false;

        if (!TryDecodeConstructedBase(
                reader,
                specificationHandle,
                declaringType,
                substitution,
                out GenericTypeNode? decoded)
            || decoded.Arguments.Length != argumentCount
            || decoded.IsDegraded)
        {
            return false;
        }

        definition = instantiated;
        arguments = decoded.Arguments;
        return true;
    }

    /// <summary>
    /// Reads the <c>GENERICINST</c> header of a <c>TypeSpec</c> and returns the
    /// exact same-image <c>TypeDef</c> it instantiates. A value-type
    /// instantiation, an external definition, or any other shape is refused;
    /// the definition identity never comes from a name.
    /// </summary>
    static bool TryReadGenericInstantiationHeader(
        MetadataReader reader,
        TypeSpecificationHandle specificationHandle,
        out TypeDefinitionHandle definition,
        out int argumentCount)
    {
        const int GenericTypeInstance = 0x15;
        const int ElementTypeClass = 0x12;
        definition = default;
        argumentCount = 0;
        try
        {
            BlobHandle signature =
                reader.GetTypeSpecification(specificationHandle).Signature;
            if (!SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    signature,
                    SignatureBlobGuard.Kind.TypeSpecification))
            {
                return false;
            }

            BlobReader blob = reader.GetBlobReader(signature);
            if (blob.ReadCompressedInteger() != GenericTypeInstance
                || blob.ReadCompressedInteger() != ElementTypeClass)
            {
                return false;
            }

            EntityHandle definitionHandle = blob.ReadTypeHandle();
            if (definitionHandle.Kind != HandleKind.TypeDefinition
                || definitionHandle.IsNil)
            {
                return false;
            }

            int count = blob.ReadCompressedInteger();
            if (count <= 0
                || count > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                return false;
            }

            definition = (TypeDefinitionHandle)definitionHandle;
            argumentCount = count;
            return true;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return false;
        }
    }

    static bool TryDecodeConstructedBase(
        MetadataReader reader,
        TypeSpecificationHandle specificationHandle,
        TypeDefinition declaringType,
        ImmutableArray<TypeNode>? substitution,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out GenericTypeNode? decoded)
    {
        decoded = null;
        try
        {
            GenericContext context =
                GenericContext.ForType(reader, declaringType);
            TypeNode node = GuardedProviderDecode.TypeSpec(
                reader,
                specificationHandle,
                SubstitutedTypeParameterProvider.Create(substitution),
                context,
                (TypeNode)new DegradedTypeNode());
            decoded = node as GenericTypeNode;
            return decoded is not null;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return false;
        }
    }

    static bool IsNonGenericDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        try
        {
            return reader
                .GetTypeDefinition(handle)
                .GetGenericParameters()
                .Count == 0;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>
/// Decodes a signature written in a generic type definition's scope while
/// replacing every type-parameter position with the exact argument that
/// instantiates it. Method type parameters, named types, and every composite
/// shape pass through to the wrapped scoped <see cref="TypeNodeProvider"/>, so
/// the substituted tree carries the same exact scoped identities as an
/// unsubstituted decode.
/// </summary>
internal sealed class SubstitutedTypeParameterProvider
    : ISignatureTypeProvider<TypeNode, GenericContext?>
{
    readonly TypeNodeProvider _inner;
    readonly ImmutableArray<TypeNode>? _typeArguments;

    SubstitutedTypeParameterProvider(
        ImmutableArray<TypeNode>? typeArguments)
    {
        _inner = new TypeNodeProvider(
            scopeNamedTypeIdentity: true,
            requireScopedNamedTypeIdentity: true);
        _typeArguments = typeArguments;
    }

    /// <summary>
    /// Creates a provider that substitutes <paramref name="typeArguments"/> for
    /// type-parameter positions. A <see langword="null"/> argument list decodes
    /// type parameters unchanged (the identity substitution).
    /// </summary>
    internal static SubstitutedTypeParameterProvider Create(
        ImmutableArray<TypeNode>? typeArguments)
        => new(typeArguments);

    public TypeNode GetGenericTypeParameter(
        GenericContext? context,
        int index)
    {
        if (_typeArguments is not { } arguments)
            return _inner.GetGenericTypeParameter(context, index);

        return (uint)index < (uint)arguments.Length
            ? arguments[index]
            : new DegradedTypeNode();
    }

    public TypeNode GetTypeFromSpecification(
        MetadataReader reader,
        GenericContext? context,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return new DegradedTypeNode();
        using (scope)
        {
            return reader
                .GetTypeSpecification(handle)
                .DecodeSignature(this, context);
        }
    }

    public TypeNode GetGenericMethodParameter(
        GenericContext? context,
        int index)
        => _inner.GetGenericMethodParameter(context, index);

    public TypeNode GetPrimitiveType(PrimitiveTypeCode typeCode)
        => _inner.GetPrimitiveType(typeCode);

    public TypeNode GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
        => _inner.GetTypeFromDefinition(reader, handle, rawTypeKind);

    public TypeNode GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
        => _inner.GetTypeFromReference(reader, handle, rawTypeKind);

    public TypeNode GetSZArrayType(TypeNode elementType)
        => _inner.GetSZArrayType(elementType);

    public TypeNode GetArrayType(TypeNode elementType, ArrayShape shape)
        => _inner.GetArrayType(elementType, shape);

    public TypeNode GetByReferenceType(TypeNode elementType)
        => _inner.GetByReferenceType(elementType);

    public TypeNode GetPointerType(TypeNode elementType)
        => _inner.GetPointerType(elementType);

    public TypeNode GetGenericInstantiation(
        TypeNode genericType,
        ImmutableArray<TypeNode> typeArguments)
        => _inner.GetGenericInstantiation(genericType, typeArguments);

    public TypeNode GetFunctionPointerType(
        MethodSignature<TypeNode> signature)
        => _inner.GetFunctionPointerType(signature);

    public TypeNode GetModifiedType(
        TypeNode modifier,
        TypeNode unmodifiedType,
        bool isRequired)
        => _inner.GetModifiedType(modifier, unmodifiedType, isRequired);

    public TypeNode GetPinnedType(TypeNode elementType)
        => _inner.GetPinnedType(elementType);
}
