using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.ILDiff;

/// <summary>
/// One side of an IL census comparison: an image's reader and the operand
/// identities its cursors yield under one policy
/// (<c>docs/design/finding-census.md#the-cursor</c>). A metadata token
/// operand's identity is its row's Symbol Key, spelled through the shared
/// <see cref="MetadataSymbolSpeller{TDomain,TRow,TRows}"/> into this side's
/// population and memoized by token for the side's life, so a token met again
/// costs a lookup and every cursor over the image spells a row once. Under
/// Finding Diff Any's Body policy the bytes are the expansions of the keys
/// that comparison memoizes, which embed named Types as Ref parts of its
/// table. The text a view shows is a projection
/// (<see cref="IlOperandIdentity.Value"/>), formatted from the side only where
/// it is read and never compared.
/// </summary>
/// <remarks>
/// The policy enters a spelling in two places the speller leaves to its
/// consumer: the scope of a Type (<see cref="IlBodyDiffNormalization.NormalizeAssemblyScope"/>
/// omits every scope; the default declares it; the current and platform
/// mechanics substitute a name for the scopes they cover) and the name of a
/// definition (<see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>
/// writes the correspondence's ordinal-free name for a Type an operand or a
/// declaring Type names and for every method or field definition, while a
/// Type a signature names keeps its stored name, as the formatted operand
/// did). A side is used from one thread at a time.
/// </remarks>
public sealed class IlCensusSide
{
    readonly MetadataReader _reader;
    readonly IlBodyDiffNormalization _policy;
    readonly CompilerGeneratedOrdinalCorrespondence _correspondence;
    // Two populations, as Finding Diff Any keeps: Type-level rows and
    // primitives in one, member rows in the other, so a Type resolved while a
    // member key is open never finds its builder open. Neither is reset while
    // the side lives; a key stays where it was spelled.
    readonly SymbolKeyBuilder<IlOperandPart> _types;
    readonly SymbolKeyBuilder<IlOperandPart> _members;
    readonly MetadataSymbolSpeller<IlOperandPart, StagedSymbolKey<IlOperandPart>, Rows> _speller;
    // Rows memoized by token, and primitives under tokens no row uses. A Type
    // definition a signature names keeps its stored name while an operand or a
    // declaring Type takes the correspondence's, so those are two memos when a
    // correspondence folds names and one otherwise.
    readonly Dictionary<int, StagedSymbolKey<IlOperandPart>> _rows = [];
    readonly Dictionary<int, StagedSymbolKey<IlOperandPart>> _referenced;
    readonly Dictionary<(OperandKind Kind, int Token), IlOperandIdentity> _operands = [];
    // The Type rows that share a plain key with another, found once per image
    // on the first operand that names a Type definition or one of its members.
    DuplicateTypeRows? _duplicateTypeRows;
    IlBodyDiff.OperandText? _text;
    string? _assemblyName;
    bool _rewriting;

    const int PrimitiveTokenBase = -1;

    IlCensusSide(MetadataReader reader, IlBodyDiffNormalization policy, CompilerGeneratedOrdinalCorrespondence correspondence)
    {
        _reader = reader;
        _policy = policy;
        _correspondence = correspondence;
        var names = new MetadataStringHeap(reader);
        // Small to start: a side created for one method stays small, and an
        // assembly-wide side grows as its memo does.
        _types = new SymbolKeyBuilder<IlOperandPart>(1024) { Names = names };
        _members = new SymbolKeyBuilder<IlOperandPart>(1024) { Names = names };
        _speller = new MetadataSymbolSpeller<IlOperandPart, StagedSymbolKey<IlOperandPart>, Rows>(
            reader,
            names,
            (policy & IlBodyDiffNormalization.NormalizeAssemblyScope) != 0 ? MetadataSymbolScope.Omitted : MetadataSymbolScope.Declared,
            new Rows(this),
            scratchCapacity: 1024);
        _referenced = ReferenceEquals(correspondence, CompilerGeneratedOrdinalCorrespondence.Empty) ? _rows : [];
    }

    /// <summary>
    /// A side over one image under a policy one side can apply alone.
    /// <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>
    /// needs both readers, so a comparison under it takes its sides from
    /// <see cref="IlBodyDiff.Compare(MetadataReader, MethodBodyBlock, MetadataReader, MethodBodyBlock, IlBodyDiffNormalization)"/>.
    /// </summary>
    public static IlCensusSide Create(MetadataReader reader, IlBodyDiffNormalization policy)
    {
        ArgumentNullException.ThrowIfNull(reader);
        IlBodyDiff.RequireSingleSidePolicy(policy);
        return new IlCensusSide(reader, policy, CompilerGeneratedOrdinalCorrespondence.Empty);
    }

    /// <summary>
    /// The two sides of one comparison under one policy, with the
    /// generated-ordinal correspondence when the policy composes
    /// <see cref="IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals"/>;
    /// a consumer comparing many bodies of one image pair creates its sides
    /// here once.
    /// </summary>
    public static (IlCensusSide Old, IlCensusSide New) CreatePair(
        MetadataReader oldReader,
        MetadataReader newReader,
        IlBodyDiffNormalization policy)
    {
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newReader);
        if ((policy & ~IlBodyDiff.SupportedNormalizations) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy,
                "Unsupported IL body diff normalization.");
        }

        var (oldCorrespondence, newCorrespondence) =
            (policy & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0
                ? CompilerGeneratedOrdinalCorrespondence.Build(oldReader, newReader, policy)
                : (CompilerGeneratedOrdinalCorrespondence.Empty, CompilerGeneratedOrdinalCorrespondence.Empty);
        return (new IlCensusSide(oldReader, policy, oldCorrespondence), new IlCensusSide(newReader, policy, newCorrespondence));
    }

    /// <summary>The image's reader.</summary>
    public MetadataReader Reader => _reader;

    /// <summary>The policy every identity of this side is spelled under.</summary>
    public IlBodyDiffNormalization Policy => _policy;

    /// <summary>Resolves a metadata token operand to its identity, memoized by operand kind and token.</summary>
    internal bool TryResolve(DecodedInstruction instruction, out IlOperandIdentity? operand, out string? failure)
    {
        (OperandKind, int) key = (instruction.Operand, (int)instruction.OperandValue);
        if (_operands.TryGetValue(key, out operand))
        {
            failure = null;
            return true;
        }

        try
        {
            operand = Identity(instruction.Operand, (int)instruction.OperandValue);
            _operands[key] = operand;
            failure = null;
            return true;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            Abandon();
            operand = null;
            failure = $"metadata token operand at IL_{instruction.Offset:X4} could not be resolved: {ex.Message}";
            return false;
        }
    }

    /// <summary>Resolves a catch clause's Type as a type operand is resolved.</summary>
    internal bool TryResolveCatchType(int token, int tryOffset, out IlOperandIdentity? operand, out string? failure)
    {
        (OperandKind, int) key = (OperandKind.InlineType, token);
        if (_operands.TryGetValue(key, out operand))
        {
            failure = null;
            return true;
        }

        try
        {
            operand = Identity(OperandKind.InlineType, token);
            _operands[key] = operand;
            failure = null;
            return true;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            Abandon();
            operand = null;
            failure = $"catch Type of the clause at IL_{tryOffset:X4} could not be resolved: {ex.Message}";
            return false;
        }
    }

    IlBodyDiff.OperandText Text => _text ??= new IlBodyDiff.OperandText(_reader, _policy, _correspondence);

    /// <summary>The formatted spelling of a token operand, for the view that shows it.</summary>
    internal string Format(OperandKind kind, int token)
    {
        try
        {
            return Text.Format(kind, token);
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            // The key spelled; a formatting the text path rejects shows the token.
            return $"0x{token:X8}";
        }
    }

    IlOperandIdentity Identity(OperandKind kind, int token)
    {
        // A user string is a value: its text is its identity, with no fallback.
        if (kind == OperandKind.InlineString)
            return new IlOperandIdentity(IlOperandIdentityKind.Token, Text.Format(kind, token));
        EntityHandle row = MetadataTokens.EntityHandle(token);
        Expect(kind, row);
        RequireNotCompilerControlled(row);
        RequireNotDuplicateTypeRow(row);
        StagedSymbolKey<IlOperandPart> key = kind == OperandKind.InlineSig
            ? CalliSignature((StandaloneSignatureHandle)row)
            : Row(row, rewrite: true);
        return new IlOperandIdentity(key, this, kind, token);
    }

    // The operand kinds admit the rows Finding Diff Any's walk admits
    // (RequireOperandKind): a kind the opcode takes, a member reference whose
    // parent is a Type (Roslyn's vararg call site has a method parent), and a
    // method specification of a definition or of such a reference. A
    // compiler-controlled (PrivateScope) member, which ECMA-335 lets share a
    // name and signature, is refused where a reference names it (an operand,
    // or a row a spelling resolves), as Finding Diff Any's ReferenceKey
    // refuses it; the definition itself is spelled for pairing (Definition).
    // An operand that names one of two Type rows sharing a key, or a member
    // of one, is refused as ReferenceKey refuses it: the rows cannot be told
    // apart, so a reference to either is undecided.
    void Expect(OperandKind kind, EntityHandle row)
    {
        bool admitted = kind switch
        {
            OperandKind.InlineMethod => row.Kind is HandleKind.MethodDefinition
                || IsMemberReference(row, MemberReferenceKind.Method)
                || IsMethodSpecification(row),
            OperandKind.InlineField => row.Kind is HandleKind.FieldDefinition
                || IsMemberReference(row, MemberReferenceKind.Field),
            OperandKind.InlineType => IsType(row),
            OperandKind.InlineTok => IsType(row)
                || row.Kind is HandleKind.MethodDefinition or HandleKind.FieldDefinition
                || IsMemberReference(row, kind: null)
                || IsMethodSpecification(row),
            OperandKind.InlineSig => row.Kind == HandleKind.StandaloneSignature,
            _ => throw new InvalidOperationException($"Operand kind {kind} is not a metadata token."),
        };
        if (!admitted)
            throw new BadImageFormatException($"An operand names a {row.Kind} canonical comparison does not resolve.");
    }

    static bool IsType(EntityHandle row)
        => row.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification;

    bool IsMemberReference(EntityHandle row, MemberReferenceKind? kind)
    {
        if (row.Kind != HandleKind.MemberReference)
            return false;
        MemberReference member = _reader.GetMemberReference((MemberReferenceHandle)row);
        return (kind is null || member.GetKind() == kind) && IsType(member.Parent);
    }

    bool IsMethodSpecification(EntityHandle row)
    {
        if (row.Kind != HandleKind.MethodSpecification)
            return false;
        EntityHandle method = _reader.GetMethodSpecification((MethodSpecificationHandle)row).Method;
        return method.Kind == HandleKind.MethodDefinition || IsMemberReference(method, MemberReferenceKind.Method);
    }

    // A calli signature compares by its bytes, as Finding Diff Any compares it
    // (SignatureBytesEqual) and as the formatted operand did; a renumbered
    // token inside the blob is a difference.
    StagedSymbolKey<IlOperandPart> CalliSignature(StandaloneSignatureHandle handle)
    {
        int token = MetadataTokens.GetToken(handle);
        if (_rows.TryGetValue(token, out StagedSymbolKey<IlOperandPart> key))
            return key;
        BlobReader blob = _reader.GetBlobReader(_reader.GetStandaloneSignature(handle).Signature);
        int mark = _members.Begin();
        return Memoize(_rows, token, _members.Mark(IlOperandPart.CalliSignature).Blob(blob).End(mark));
    }

    StagedSymbolKey<IlOperandPart> Row(EntityHandle row, bool rewrite)
    {
        int token = MetadataTokens.GetToken(row);
        Dictionary<int, StagedSymbolKey<IlOperandPart>> memo = rewrite || row.Kind != HandleKind.TypeDefinition ? _rows : _referenced;
        if (memo.TryGetValue(token, out StagedSymbolKey<IlOperandPart> key))
            return key;
        switch (row.Kind)
        {
            case HandleKind.TypeDefinition:
                return Memoize(memo, token, TypeDefinition((TypeDefinitionHandle)row, rewrite));
            case HandleKind.TypeReference:
                return Memoize(memo, token, _speller.TypeReference(_types, (TypeReferenceHandle)row));
            case HandleKind.TypeSpecification:
                // A specification that names a plain row is that row's key.
                if (_speller.TryTypeSpecification(_types, (TypeSpecificationHandle)row, out var spelled, out StagedSymbolKey<IlOperandPart> alias))
                    return Memoize(memo, token, spelled);
                memo[token] = alias;
                return alias;
            case HandleKind.MethodDefinition:
                return Memoize(memo, token, _speller.Method(_members, (MethodDefinitionHandle)row));
            case HandleKind.FieldDefinition:
                return Memoize(memo, token, _speller.Field(_members, (FieldDefinitionHandle)row));
            case HandleKind.MemberReference:
                return Memoize(memo, token, _speller.MemberReference(_members, (MemberReferenceHandle)row));
            case HandleKind.MethodSpecification:
                return Memoize(memo, token, _speller.MethodSpecification(_members, (MethodSpecificationHandle)row));
            default:
                throw new BadImageFormatException($"Unsupported metadata token 0x{token:X8} in an IL operand.");
        }
    }

    void RequireNotDuplicateTypeRow(EntityHandle row)
    {
        TypeDefinitionHandle type = row.Kind switch
        {
            HandleKind.TypeDefinition => (TypeDefinitionHandle)row,
            HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)row).GetDeclaringType(),
            HandleKind.FieldDefinition => _reader.GetFieldDefinition((FieldDefinitionHandle)row).GetDeclaringType(),
            _ => default,
        };
        if (type.IsNil)
            return;
        _duplicateTypeRows ??= DuplicateTypeRows.Of(_reader);
        if (_duplicateTypeRows.Contains(type))
            throw new BadImageFormatException("A reference names one of two Type rows that share a key.");
    }

    /// <summary>
    /// The Type rows of one image that share a plain key with another, over
    /// the Types the walk's unit table admits: a Type with an invalid
    /// declaring chain, a compiler-generated Type in its chain, or a name that
    /// is not well-formed UTF-8 is not a unit there and so never in its
    /// duplicate set. Two Types share a key exactly when their root
    /// namespaces and their declaring-chain names agree byte for byte, so the
    /// set is found by comparing stored names, spelling nothing, once per
    /// image and kept per metadata reader as the generated-ordinal index is,
    /// so a host that creates a side per call pays it once.
    /// </summary>
    sealed class DuplicateTypeRows
    {
        static readonly ConditionalWeakTable<MetadataReader, DuplicateTypeRows> s_cache = new();
        static readonly DuplicateTypeRows s_none = new([]);

        readonly HashSet<int> _tokens;

        DuplicateTypeRows(HashSet<int> tokens) => _tokens = tokens;

        public static DuplicateTypeRows Of(MetadataReader reader) => s_cache.GetValue(reader, Compute);

        public bool Contains(TypeDefinitionHandle type) => _tokens.Count != 0 && _tokens.Contains(MetadataTokens.GetToken(type));

        static DuplicateTypeRows Compute(MetadataReader reader)
        {
            var names = new MetadataStringHeap(reader);
            var comparer = new PlainTypeNameComparer(reader, names);
            var seen = new Dictionary<TypeDefinitionHandle, TypeDefinitionHandle>(comparer);
            HashSet<int>? duplicates = null;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                // A row whose names or chain cannot be read is a malformed
                // unit in the walk's table, never a duplicate; it fails the
                // operands that name it, not this scan.
                try
                {
                    if (!comparer.IsUnit(handle))
                        continue;
                    if (seen.TryAdd(handle, handle))
                        continue;
                    duplicates ??= [];
                    duplicates.Add(MetadataTokens.GetToken(handle));
                    duplicates.Add(MetadataTokens.GetToken(seen[handle]));
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    continue;
                }
            }
            return duplicates is null ? s_none : new DuplicateTypeRows(duplicates);
        }
    }

    /// <summary>
    /// Equality of two Type definitions by the plain key the speller would
    /// write for them: the root namespace, if any, and the names along the
    /// declaring chain, each compared as stored bytes.
    /// </summary>
    sealed class PlainTypeNameComparer(MetadataReader reader, MetadataStringHeap names) : IEqualityComparer<TypeDefinitionHandle>
    {
        public bool IsUnit(TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!Walk(handle, chain, out int length))
                return false;
            TypeDefinition root = reader.GetTypeDefinition(chain[0]);
            if (!root.Namespace.IsNil && !names.IsWellFormed(root.Namespace))
                return false;
            for (int i = 0; i < length; i++)
            {
                StringHandle name = reader.GetTypeDefinition(chain[i]).Name;
                if (reader.StringComparer.StartsWith(name, "<") || !names.IsWellFormed(name))
                    return false;
            }
            return true;
        }

        public bool Equals(TypeDefinitionHandle x, TypeDefinitionHandle y)
        {
            if (x == y)
                return true;
            Span<TypeDefinitionHandle> chainX = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            Span<TypeDefinitionHandle> chainY = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!Walk(x, chainX, out int lengthX) || !Walk(y, chainY, out int lengthY) || lengthX != lengthY)
                return false;
            TypeDefinition rootX = reader.GetTypeDefinition(chainX[0]);
            TypeDefinition rootY = reader.GetTypeDefinition(chainY[0]);
            if (rootX.Namespace.IsNil != rootY.Namespace.IsNil)
                return false;
            if (!rootX.Namespace.IsNil && !names.Bytes(rootX.Namespace).SequenceEqual(names.Bytes(rootY.Namespace)))
                return false;
            for (int i = 0; i < lengthX; i++)
            {
                if (!names.Bytes(reader.GetTypeDefinition(chainX[i]).Name).SequenceEqual(names.Bytes(reader.GetTypeDefinition(chainY[i]).Name)))
                    return false;
            }
            return true;
        }

        public int GetHashCode(TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            var hash = new HashCode();
            if (!Walk(handle, chain, out int length))
                return 0;
            TypeDefinition root = reader.GetTypeDefinition(chain[0]);
            if (!root.Namespace.IsNil)
                hash.AddBytes(names.Bytes(root.Namespace));
            for (int i = 0; i < length; i++)
            {
                ReadOnlySpan<byte> name = names.Bytes(reader.GetTypeDefinition(chain[i]).Name);
                hash.Add(name.Length);
                hash.AddBytes(name);
            }
            return hash.ToHashCode();
        }

        bool Walk(TypeDefinitionHandle handle, Span<TypeDefinitionHandle> chain, out int length)
            => MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(reader, handle, chain, out length, out _, out _) && length > 0;
    }

    // A reference to a compiler-controlled member cannot say which of the
    // members sharing its name and signature it names; Finding Diff Any's
    // ReferenceKey refuses it with these words.
    void RequireNotCompilerControlled(EntityHandle row)
    {
        switch (row.Kind)
        {
            case HandleKind.MethodDefinition
                when (_reader.GetMethodDefinition((MethodDefinitionHandle)row).Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.PrivateScope:
                throw new BadImageFormatException("A reference names a compiler-controlled method.");
            case HandleKind.FieldDefinition
                when (_reader.GetFieldDefinition((FieldDefinitionHandle)row).Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.PrivateScope:
                throw new BadImageFormatException("A reference names a compiler-controlled field.");
        }
    }

    /// <summary>
    /// A method definition's key, the identity <see cref="IlAssemblyDiff"/>
    /// pairs bodies by: the same spelling a call operand naming the method
    /// memoizes, so a map built through the comparison's side spells each
    /// method once for both. A compiler-controlled definition is spelled (two
    /// of them pair by occurrence); only a reference to one is refused. A
    /// definition the speller rejects is a failure of that method alone: the
    /// spelling it left open is abandoned, and memoized keys stay valid.
    /// </summary>
    internal bool TryDefinition(MethodDefinitionHandle handle, out StagedSymbolKey<IlOperandPart> key, out string? failure)
    {
        try
        {
            key = Row(handle, rewrite: true);
            failure = null;
            return true;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            Abandon();
            key = default;
            failure = ex.Message;
            return false;
        }
    }

    StagedSymbolKey<IlOperandPart> TypeDefinition(TypeDefinitionHandle handle, bool rewrite)
    {
        _rewriting = rewrite;
        try
        {
            return _speller.TypeDefinition(_types, handle);
        }
        finally
        {
            _rewriting = false;
        }
    }

    StagedSymbolKey<IlOperandPart> Primitive(PrimitiveTypeCode typeCode)
    {
        int token = PrimitiveTokenBase - (byte)typeCode;
        if (_rows.TryGetValue(token, out StagedSymbolKey<IlOperandPart> key))
            return key;
        return Memoize(_rows, token, _speller.Primitive(_types, typeCode));
    }

    static StagedSymbolKey<IlOperandPart> Memoize(Dictionary<int, StagedSymbolKey<IlOperandPart>> memo, int token, StagedSymbolKey<IlOperandPart> key)
    {
        memo[token] = key;
        return key;
    }

    // A definition's compared name under the correspondence: a method or field
    // always, a Type only where the operand or a declaring Type names it.
    bool TryElidedName(EntityHandle definition, out string name)
    {
        if (ReferenceEquals(_correspondence, CompilerGeneratedOrdinalCorrespondence.Empty))
        {
            name = "";
            return false;
        }
        switch (definition.Kind)
        {
            case HandleKind.TypeDefinition when _rewriting:
                return _correspondence.TryGetTypeName((TypeDefinitionHandle)definition, out name);
            case HandleKind.MethodDefinition:
                return _correspondence.TryGetMethodName((MethodDefinitionHandle)definition, out name);
            case HandleKind.FieldDefinition:
                return _correspondence.TryGetFieldName((FieldDefinitionHandle)definition, out name);
            default:
                name = "";
                return false;
        }
    }

    // The scope name the current or platform mechanic substitutes, by the one
    // rule the formatted operand and the generated-ordinal correspondence
    // share (IlBodyDiff.AssemblyScopeToken): the current assembly, and a
    // reference whose name is the current assembly's, under the current
    // mechanic; a reference to a platform assembly under the platform one. A
    // module reader has no current name, so its references keep their
    // identities.
    ReadOnlySpan<byte> ScopeName(EntityHandle scope)
    {
        const IlBodyDiffNormalization Mechanics =
            IlBodyDiffNormalization.NormalizeCurrentAssemblyScope | IlBodyDiffNormalization.NormalizePlatformAssemblyScope;
        if ((_policy & Mechanics) == 0)
            return default;
        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
            {
                if (!_reader.IsAssembly)
                    return default;
                string name = _reader.GetString(_reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                return IlBodyDiff.AssemblyScopeToken(name, AssemblyName(), _policy) switch
                {
                    "<current>" => "<current>"u8,
                    "<platform>" => "<platform>"u8,
                    _ => default,
                };
            }
            case HandleKind.AssemblyDefinition:
            case HandleKind.ModuleDefinition:
                return (_policy & IlBodyDiffNormalization.NormalizeCurrentAssemblyScope) != 0 ? "<current>"u8 : default;
            default:
                return default;
        }
    }

    string AssemblyName() => _assemblyName ??= _reader.GetString(_reader.GetAssemblyDefinition().Name);

    // Discards a spelling a malformed row left open; memoized keys stay valid.
    void Abandon()
    {
        _types.Abandon();
        _members.Abandon();
        _speller.Abandon();
        _rewriting = false;
    }

    // A builder or speller used out of its lifetime is a defect, not malformed
    // input, and is never classified as the image's.
    static bool IsMalformed(Exception ex)
        => ex is BadImageFormatException or ArgumentException or InvalidOperationException
            && ex is not SymbolKeyStateException;

    /// <summary>
    /// The side as the speller's consumer: a struct, so the speller's
    /// instantiation is specialized and these are direct calls.
    /// </summary>
    readonly struct Rows(IlCensusSide side) : IMetadataRowKeys<IlOperandPart, StagedSymbolKey<IlOperandPart>>
    {
        public StagedSymbolKey<IlOperandPart> Resolve(EntityHandle row, MetadataRowRole role)
        {
            side.RequireNotCompilerControlled(row);
            return side.Row(row, rewrite: role != MetadataRowRole.Referenced);
        }

        public StagedSymbolKey<IlOperandPart> Primitive(PrimitiveTypeCode typeCode) => side.Primitive(typeCode);

        public void Embed(SymbolKeyBuilder<IlOperandPart> into, StagedSymbolKey<IlOperandPart> row) => into.Key(row);

        public void Name(SymbolKeyBuilder<IlOperandPart> into, EntityHandle definition, StringHandle name)
        {
            if (side.TryElidedName(definition, out string elided))
                into.Name(elided.AsSpan());
            else
                into.Name(name);
        }

        public ReadOnlySpan<byte> ScopeName(EntityHandle scope) => side.ScopeName(scope);
    }
}
