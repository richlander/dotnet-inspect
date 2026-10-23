using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// The consumer's side of a <see cref="MetadataSymbolSpeller{TDomain,TRow,TRows}"/>:
/// where rows are spelled and memoized. The declaring Type of a definition,
/// a member reference's parent, and a method specification's method are
/// resolved before the referring key opens; a Type a signature names is
/// resolved when it is embedded, while the referring key is open, so the
/// consumer spells Type-level rows into a builder other than the one a member
/// key is written in. <see cref="Embed"/> copies a resolved row's bytes as a
/// nested Key part into an open key.
/// </summary>
public interface IMetadataRowKeys<TDomain, TRow>
    where TDomain : unmanaged, Enum
{
    /// <summary>The consumer's key for a row, spelled or found in its memo.</summary>
    TRow Resolve(EntityHandle row, MetadataRowRole role);

    /// <summary>The consumer's key for a primitive type, which signatures name far more often than any row.</summary>
    TRow Primitive(PrimitiveTypeCode typeCode);

    /// <summary>Embeds a resolved row's key as a nested Key part of the key open in <paramref name="into"/>.</summary>
    void Embed(SymbolKeyBuilder<TDomain> into, TRow row);
}

/// <summary>
/// The one speller of metadata rows as Symbol Keys
/// (<c>docs/design/symbol-key.md#metadata-rows</c>): a Type definition's
/// namespace, declaring chain, and names; a Type reference's chain and, under
/// <see cref="MetadataSymbolScope.Declared"/>, its resolution scope; a method,
/// field, member reference, method specification, or standalone signature with
/// its decoded signature; and the signature composites (arrays, pointers,
/// generic instances, modifiers, function pointers, parameters) spelled in a
/// scratch population and embedded by their consumer. Rows a spelling refers
/// to are resolved through <typeparamref name="TRow"/> by the consumer, which
/// owns their storage and memo, so two consumers over one image spell one row
/// with the same bytes. Guards bound every signature and specification decode;
/// a rejected shape is a <see cref="BadImageFormatException"/>. The consumer is
/// a struct type parameter, so the instantiation is specialized and its
/// resolve and embed are direct calls on the hot path; a class argument would
/// share code and dispatch through the interface.
/// </summary>
public sealed class MetadataSymbolSpeller<TDomain, TRow, TRows>
    where TDomain : unmanaged, Enum
    where TRows : struct, IMetadataRowKeys<TDomain, TRow>
{
    readonly MetadataSymbolScope _scope;
    readonly TRows _rows;
    readonly Provider _provider;
    MetadataReader _reader;
    int _depth;

    public MetadataSymbolSpeller(
        MetadataReader reader,
        MetadataStringHeap names,
        MetadataSymbolScope scope,
        TRows rows)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(names);
        _reader = reader;
        _scope = scope;
        _rows = rows;
        Scratch = new SymbolKeyBuilder<TDomain>(4 * 1024) { Names = names };
        _provider = new Provider(this);
        // The domain declares every marker this speller writes under its scope,
        // checked once here so each write is unchecked.
        foreach (MetadataSymbolPart part in Enum.GetValues<MetadataSymbolPart>())
        {
            if (part == MetadataSymbolPart.Scope && scope != MetadataSymbolScope.Declared)
                continue;
            if (!Scratch.Declares((byte)part))
                throw new SymbolKeyStateException($"Domain {typeof(TDomain).Name} does not declare the shared marker {part} (0x{(byte)part:X2}).");
        }
    }

    static SymbolKeyBuilder<TDomain> Mark(SymbolKeyBuilder<TDomain> into, MetadataSymbolPart part) => into.MarkValidated((byte)part);

    /// <summary>
    /// Signature composites built while one row is spelled; discarded when the
    /// outermost spelling completes.
    /// </summary>
    public SymbolKeyBuilder<TDomain> Scratch { get; }

    /// <summary>Continues over another reader of the same image.</summary>
    public void Bind(MetadataReader reader, MetadataStringHeap names)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(names);
        _reader = reader;
        Scratch.Names = names;
    }

    /// <summary>Discards an abandoned spelling's scratch state.</summary>
    public void Abandon()
    {
        Scratch.Reset();
        _depth = 0;
    }

    /// <summary>Releases the scratch storage; the speller is not used after.</summary>
    public void Release() => Scratch.Release();

    public StagedSymbolKey<TDomain> TypeDefinition(SymbolKeyBuilder<TDomain> into, TypeDefinitionHandle handle)
    {
        Enter();
        try
        {
            Span<TypeDefinitionHandle> chain = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(_reader, handle, chain, out int length, out _, out _)
                || length == 0)
            {
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            }
            int mark = into.Begin();
            Mark(into, MetadataSymbolPart.Type);
            TypeDefinition root = _reader.GetTypeDefinition(chain[0]);
            if (!root.Namespace.IsNil)
                Mark(into, MetadataSymbolPart.Namespace).Name(root.Namespace);
            for (int i = 0; i < length; i++)
                into.Name(_reader.GetTypeDefinition(chain[i]).Name);
            if (_scope == MetadataSymbolScope.Declared)
                CurrentScope(into);
            return into.End(mark);
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> TypeReference(SymbolKeyBuilder<TDomain> into, TypeReferenceHandle handle)
    {
        Enter();
        try
        {
            Span<TypeReferenceHandle> chain = stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    _reader, handle, chain, out int length, out EntityHandle terminal, out var rejection)
                || length == 0)
            {
                throw new BadImageFormatException(
                    rejection?.Detail ?? "The Type reference has an invalid resolution-scope chain.");
            }
            int mark = into.Begin();
            Mark(into, MetadataSymbolPart.Type);
            TypeReference root = _reader.GetTypeReference(chain[0]);
            if (!root.Namespace.IsNil)
                Mark(into, MetadataSymbolPart.Namespace).Name(root.Namespace);
            for (int i = 0; i < length; i++)
                into.Name(_reader.GetTypeReference(chain[i]).Name);
            if (_scope == MetadataSymbolScope.Declared)
                ResolutionScope(into, terminal);
            return into.End(mark);
        }
        finally
        {
            Exit();
        }
    }

    // The scope kinds, as the first part after the Scope marker, so a module and
    // an assembly of one name spell differently.
    const int AssemblyScope = 0;
    const int ModuleScope = 1;
    const int CurrentAssemblyScope = 2;

    // The declared scope of a Type reference: its assembly reference's identity
    // as parts, its module reference's name, or the current assembly's name.
    void ResolutionScope(SymbolKeyBuilder<TDomain> into, EntityHandle terminal)
    {
        switch (terminal.Kind)
        {
            case HandleKind.AssemblyReference:
            {
                AssemblyReference reference = _reader.GetAssemblyReference((AssemblyReferenceHandle)terminal);
                Mark(into, MetadataSymbolPart.Scope).Int(AssemblyScope).Name(reference.Name)
                    .Int(reference.Version.Major).Int(reference.Version.Minor)
                    .Int(reference.Version.Build).Int(reference.Version.Revision)
                    .Int(reference.Culture.IsNil ? 0 : 1);
                if (!reference.Culture.IsNil)
                    into.Name(reference.Culture);
                into.Blob(_reader.GetBlobReader(reference.PublicKeyOrToken));
                return;
            }
            case HandleKind.ModuleReference:
                Mark(into, MetadataSymbolPart.Scope).Int(ModuleScope)
                    .Name(_reader.GetModuleReference((ModuleReferenceHandle)terminal).Name);
                return;
            default:
                CurrentScope(into);
                return;
        }
    }

    // The current assembly's name, or the module's when the image is no assembly.
    void CurrentScope(SymbolKeyBuilder<TDomain> into)
    {
        Mark(into, MetadataSymbolPart.Scope).Int(CurrentAssemblyScope);
        if (_reader.IsAssembly)
            into.Name(_reader.GetAssemblyDefinition().Name);
        else
            into.Name(_reader.GetModuleDefinition().Name);
    }

    /// <summary>
    /// Spells a Type specification: a composite copied into <paramref name="into"/>
    /// (true), or a plain row the specification merely names, returned as the
    /// consumer's key for that row (false), so the consumer memoizes the
    /// specification as that row rather than as a copy of it.
    /// </summary>
    public bool TryTypeSpecification(
        SymbolKeyBuilder<TDomain> into,
        TypeSpecificationHandle handle,
        out StagedSymbolKey<TDomain> key,
        out TRow alias)
    {
        Enter();
        try
        {
            if (!TypeSpecGuard.TryEnter(_reader, handle, out TypeSpecGuard.Scope scope))
                throw new BadImageFormatException("The Type specification exceeds the decode budget.");
            using (scope)
            {
                TypeSpecification specification = _reader.GetTypeSpecification(handle);
                Guard(specification.Signature, SignatureBlobGuard.Kind.TypeSpecification);
                MetadataSignatureKey decoded = specification.DecodeSignature(_provider, null);
                if (decoded.IsRow)
                {
                    alias = _rows.Resolve(decoded.Row, MetadataRowRole.Referenced);
                    key = default;
                    return false;
                }
                if (decoded.IsPrimitive)
                {
                    alias = _rows.Primitive(decoded.PrimitiveType);
                    key = default;
                    return false;
                }
                alias = default!;
                key = into.Copy(ScratchKey(decoded));
                return true;
            }
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> Method(SymbolKeyBuilder<TDomain> into, MethodDefinitionHandle handle)
    {
        Enter();
        try
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);
            Guard(method.Signature, SignatureBlobGuard.Kind.Method);
            MethodSignature<MetadataSignatureKey> signature = method.DecodeSignature(_provider, null);
            TRow declaring = _rows.Resolve(method.GetDeclaringType(), MetadataRowRole.Declaring);
            return MethodKey(into, declaring, method.Name, signature);
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> Field(SymbolKeyBuilder<TDomain> into, FieldDefinitionHandle handle)
    {
        Enter();
        try
        {
            FieldDefinition field = _reader.GetFieldDefinition(handle);
            Guard(field.Signature, SignatureBlobGuard.Kind.Field);
            MetadataSignatureKey type = field.DecodeSignature(_provider, null);
            TRow declaring = _rows.Resolve(field.GetDeclaringType(), MetadataRowRole.Declaring);
            return FieldKey(into, declaring, field.Name, type);
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> MemberReference(SymbolKeyBuilder<TDomain> into, MemberReferenceHandle handle)
    {
        Enter();
        try
        {
            MemberReference member = _reader.GetMemberReference(handle);
            TRow parent = _rows.Resolve(member.Parent, MetadataRowRole.Referenced);
            if (member.GetKind() == MemberReferenceKind.Field)
            {
                Guard(member.Signature, SignatureBlobGuard.Kind.Field);
                return FieldKey(into, parent, member.Name, member.DecodeFieldSignature(_provider, null));
            }
            Guard(member.Signature, SignatureBlobGuard.Kind.Method);
            return MethodKey(into, parent, member.Name, member.DecodeMethodSignature(_provider, null));
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> MethodSpecification(SymbolKeyBuilder<TDomain> into, MethodSpecificationHandle handle)
    {
        Enter();
        try
        {
            MethodSpecification specification = _reader.GetMethodSpecification(handle);
            Guard(specification.Signature, SignatureBlobGuard.Kind.MethodSpecification);
            TRow method = _rows.Resolve(specification.Method, MetadataRowRole.Referenced);
            ImmutableArray<MetadataSignatureKey> arguments = specification.DecodeSignature(_provider, null);
            int mark = into.Begin();
            Mark(into, MetadataSymbolPart.MethodSpecification);
            _rows.Embed(into, method);
            foreach (MetadataSignatureKey argument in arguments)
                Embed(into, argument);
            return into.End(mark);
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> StandaloneSignature(SymbolKeyBuilder<TDomain> into, StandaloneSignatureHandle handle)
    {
        Enter();
        try
        {
            StandaloneSignature signature = _reader.GetStandaloneSignature(handle);
            int mark;
            if (signature.GetKind() == StandaloneSignatureKind.LocalVariables)
            {
                Guard(signature.Signature, SignatureBlobGuard.Kind.LocalVariables);
                ImmutableArray<MetadataSignatureKey> locals = signature.DecodeLocalSignature(_provider, null);
                mark = into.Begin();
                Mark(into, MetadataSymbolPart.LocalSignature);
                foreach (MetadataSignatureKey local in locals)
                    Embed(into, local);
                return into.End(mark);
            }
            Guard(signature.Signature, SignatureBlobGuard.Kind.StandaloneMethod);
            MethodSignature<MetadataSignatureKey> method = signature.DecodeMethodSignature(_provider, null);
            mark = into.Begin();
            Mark(into, MetadataSymbolPart.StandaloneSignature).Int(method.Header.RawValue);
            Signature(into, method);
            return into.End(mark);
        }
        finally
        {
            Exit();
        }
    }

    public StagedSymbolKey<TDomain> Primitive(SymbolKeyBuilder<TDomain> into, PrimitiveTypeCode typeCode)
    {
        int mark = into.Begin();
        return Mark(into, MetadataSymbolPart.Primitive).Int((byte)typeCode).End(mark);
    }

    /// <summary>A property's decoded signature, guarded; spelled into a consumer's key through <see cref="Signature"/>.</summary>
    public MethodSignature<MetadataSignatureKey> DecodeProperty(PropertyDefinition property)
    {
        Enter();
        try
        {
            Guard(property.Signature, SignatureBlobGuard.Kind.Property);
            return property.DecodeSignature(_provider, null);
        }
        finally
        {
            ExitKeepingScratch();
        }
    }

    /// <summary>
    /// Writes a decoded signature's parameter types, the Vararg marker at the
    /// sentinel, and the Return marker and type, into the open key.
    /// </summary>
    public void Signature(SymbolKeyBuilder<TDomain> into, MethodSignature<MetadataSignatureKey> signature)
    {
        ImmutableArray<MetadataSignatureKey> types = signature.ParameterTypes;
        for (int i = 0; i < types.Length; i++)
        {
            if (i == signature.RequiredParameterCount)
                Mark(into, MetadataSymbolPart.Vararg);
            Embed(into, types[i]);
        }
        Mark(into, MetadataSymbolPart.Return);
        Embed(into, signature.ReturnType);
    }

    /// <summary>Embeds a signature type, a row or primitive the consumer resolves or a scratch composite, as a nested Key part.</summary>
    public void Embed(SymbolKeyBuilder<TDomain> into, MetadataSignatureKey key)
    {
        if (key.IsRow)
            _rows.Embed(into, _rows.Resolve(key.Row, MetadataRowRole.Referenced));
        else if (key.IsPrimitive)
            _rows.Embed(into, _rows.Primitive(key.PrimitiveType));
        else
            into.Key(ScratchKey(key));
    }

    /// <summary>
    /// Ends a spelling whose decoded signature the consumer embeds afterwards:
    /// the scratch population lives until the consumer calls <see cref="Discard"/>.
    /// </summary>
    public void Discard()
    {
        if (_depth == 0)
            Scratch.Reset();
    }

    StagedSymbolKey<TDomain> MethodKey(SymbolKeyBuilder<TDomain> into, TRow parent, StringHandle name, MethodSignature<MetadataSignatureKey> signature)
    {
        int mark = into.Begin();
        Mark(into, MetadataSymbolPart.Method);
        _rows.Embed(into, parent);
        into.Name(name).Int(signature.GenericParameterCount).Int(signature.Header.RawValue);
        Signature(into, signature);
        return into.End(mark);
    }

    StagedSymbolKey<TDomain> FieldKey(SymbolKeyBuilder<TDomain> into, TRow parent, StringHandle name, MetadataSignatureKey type)
    {
        int mark = into.Begin();
        Mark(into, MetadataSymbolPart.Field);
        _rows.Embed(into, parent);
        into.Name(name);
        Embed(into, type);
        return into.End(mark);
    }

    StagedSymbolKey<TDomain> ScratchKey(MetadataSignatureKey key)
    {
        if (key.Generation != Scratch.Generation)
            throw new SymbolKeyStateException("The signature composite outlived its spelling.");
        return Scratch.Resolve(new SymbolKeySlot(key.Offset, key.Length));
    }

    void Guard(BlobHandle signature, SignatureBlobGuard.Kind kind)
    {
        if (!SignatureBlobGuard.IsSafeToDecode(_reader, signature, kind))
            throw new BadImageFormatException("The signature blob is too deep to decode.");
    }

    void Enter() => _depth++;

    void Exit()
    {
        if (--_depth == 0)
            Scratch.Reset();
    }

    // A decode whose result the consumer still embeds leaves scratch alive;
    // the consumer discards it.
    void ExitKeepingScratch() => _depth--;

    static MetadataSignatureKey Row(EntityHandle row) => new(row);

    sealed class Provider(MetadataSymbolSpeller<TDomain, TRow, TRows> speller) : ISignatureTypeProvider<MetadataSignatureKey, object?>
    {
        SymbolKeyBuilder<TDomain> Keys => speller.Scratch;

        MetadataSignatureKey Staged(StagedSymbolKey<TDomain> key) => new(key.Generation, key.Offset, key.Length);

        public MetadataSignatureKey GetArrayType(MetadataSignatureKey elementType, ArrayShape shape)
        {
            int mark = Keys.Begin();
            Mark(Keys, MetadataSymbolPart.Array);
            speller.Embed(Keys, elementType);
            Keys.Int(shape.Rank).Int(shape.Sizes.Length);
            foreach (int size in shape.Sizes)
                Keys.Int(size);
            Keys.Int(shape.LowerBounds.Length);
            foreach (int bound in shape.LowerBounds)
                Keys.Int(bound);
            return Staged(Keys.End(mark));
        }

        public MetadataSignatureKey GetByReferenceType(MetadataSignatureKey elementType) => Wrap(MetadataSymbolPart.ByReference, elementType);

        public MetadataSignatureKey GetFunctionPointerType(MethodSignature<MetadataSignatureKey> signature)
        {
            int mark = Keys.Begin();
            Mark(Keys, MetadataSymbolPart.FunctionPointer).Int(signature.Header.RawValue);
            speller.Signature(Keys, signature);
            return Staged(Keys.End(mark));
        }

        public MetadataSignatureKey GetGenericInstantiation(MetadataSignatureKey genericType, ImmutableArray<MetadataSignatureKey> typeArguments)
        {
            int mark = Keys.Begin();
            Mark(Keys, MetadataSymbolPart.GenericInstance);
            speller.Embed(Keys, genericType);
            foreach (MetadataSignatureKey argument in typeArguments)
                speller.Embed(Keys, argument);
            return Staged(Keys.End(mark));
        }

        public MetadataSignatureKey GetGenericMethodParameter(object? genericContext, int index)
        {
            int mark = Keys.Begin();
            return Staged(Mark(Keys, MetadataSymbolPart.MethodParameter).Int(index).End(mark));
        }

        public MetadataSignatureKey GetGenericTypeParameter(object? genericContext, int index)
        {
            int mark = Keys.Begin();
            return Staged(Mark(Keys, MetadataSymbolPart.TypeParameter).Int(index).End(mark));
        }

        public MetadataSignatureKey GetModifiedType(MetadataSignatureKey modifier, MetadataSignatureKey unmodifiedType, bool isRequired)
        {
            int mark = Keys.Begin();
            Mark(Keys, isRequired ? MetadataSymbolPart.RequiredModifier : MetadataSymbolPart.OptionalModifier);
            speller.Embed(Keys, modifier);
            speller.Embed(Keys, unmodifiedType);
            return Staged(Keys.End(mark));
        }

        public MetadataSignatureKey GetPinnedType(MetadataSignatureKey elementType) => Wrap(MetadataSymbolPart.Pinned, elementType);

        public MetadataSignatureKey GetPointerType(MetadataSignatureKey elementType) => Wrap(MetadataSymbolPart.Pointer, elementType);

        public MetadataSignatureKey GetPrimitiveType(PrimitiveTypeCode typeCode) => new(typeCode);

        public MetadataSignatureKey GetSZArrayType(MetadataSignatureKey elementType) => Wrap(MetadataSymbolPart.SZArray, elementType);

        public MetadataSignatureKey GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Row(handle);

        public MetadataSignatureKey GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Row(handle);

        public MetadataSignatureKey GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => Row(handle);

        MetadataSignatureKey Wrap(MetadataSymbolPart part, MetadataSignatureKey elementType)
        {
            int mark = Keys.Begin();
            Mark(Keys, part);
            speller.Embed(Keys, elementType);
            return Staged(Keys.End(mark));
        }
    }
}
