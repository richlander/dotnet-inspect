using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.ILDiff;

public static partial class IlBodyDiff
{
    /// <summary>
    /// SPIKE (exp/unified-diff-exists, #10227, #9918): the complete IL diff's two
    /// identities as <see cref="SymbolKey"/> bytes instead of text, for one side.
    /// <list type="bullet">
    /// <item>The <b>method identity</b> that <c>IlAssemblyDiff</c> pairs bodies by
    /// (its <c>MethodKey</c> text: declaring Type without assembly, name, arity,
    /// instance or static, return and parameter Types as <c>MetadataIdentityName</c>
    /// spells them).</item>
    /// <item>The <b>operand identity</b> that canonical comparison compares tokens by
    /// (<c>MetadataOperandResolver</c>'s text under the scope normalization).</item>
    /// </list>
    /// Each key keeps exactly the distinctions its text spelling keeps (assembly
    /// scope tokens, signature header bits, the vararg sentinel, array shapes,
    /// stand-alone signatures as blob bytes, user strings by value), so two keys
    /// are equal exactly when the two texts are, except where a text spelling
    /// collides (a <c>.</c> or <c>+</c> inside a name, or ill-formed UTF-8, which
    /// decodes to U+FFFD): the keys are injective there and the text is not. Keys
    /// intern into a <see cref="SymbolKeyTable"/>; ids are valid only in it.
    /// </summary>
    internal sealed class SymbolicIdentity
    {
        const SignatureAttributes ThisBits = SignatureAttributes.Instance | SignatureAttributes.ExplicitThis;

        readonly MetadataReader _md;
        readonly SymbolKey.Builder _keys;
        readonly IlBodyDiffNormalization _normalization;
        readonly string? _currentAssembly;
        readonly OperandTypes _operandTypes;
        readonly MapTypes _mapTypes;
        readonly Dictionary<int, SymbolKey> _definitions = [];
        readonly Dictionary<int, SymbolKey> _referencesQualified = [];
        readonly Dictionary<int, SymbolKey> _referencesUnqualified = [];
        readonly Dictionary<int, SymbolKey> _specifications = [];
        readonly Dictionary<int, SymbolKey> _mapDefinitions = [];
        readonly Dictionary<int, SymbolKey> _mapReferences = [];
        readonly Dictionary<int, SymbolKey> _mapSpecifications = [];
        readonly Dictionary<int, SymbolKey> _declaringTypes = [];
        readonly Dictionary<BlobHandle, SymbolKey> _mapSignatures = [];
        readonly Dictionary<AssemblyReferenceHandle, (SymbolPart Part, byte[]? Exact)> _scopes = [];
        readonly Dictionary<long, int> _operands = [];
        readonly SymbolKey[] _primitives = new SymbolKey[256];

        public SymbolicIdentity(PEReader pe, MetadataReader md, IlBodyDiffNormalization normalization, SymbolKeyTable table)
        {
            if (normalization != (IlBodyDiffNormalization.NormalizeCurrentAssemblyScope | IlBodyDiffNormalization.NormalizePlatformAssemblyScope))
                throw new NotSupportedException("SPIKE: only the Body policy's scope normalization is modelled.");
            _md = md;
            _normalization = normalization;
            Table = table;
            _keys = new SymbolKey.Builder(new SymbolKey.Utf8Names(pe, md));
            _currentAssembly = md.IsAssembly ? md.GetString(md.GetAssemblyDefinition().Name) : null;
            _operandTypes = new OperandTypes(this);
            _mapTypes = new MapTypes(this);
        }

        public SymbolKeyTable Table { get; }

        public MetadataReader Reader => _md;

        /// <summary>Distinct operand tokens resolved (memo misses), for attribution.</summary>
        public long OperandMisses { get; private set; }

        // ------------------------------------------------------------------ method identity

        /// <summary>
        /// The id of the method identity <c>IlAssemblyDiff</c> pairs by, or false with the
        /// failure that makes the text identity fail.
        /// </summary>
        public bool TryMethodIdentity(MethodDefinitionHandle handle, out int id)
        {
            int mark = _keys.Begin();
            try
            {
                MethodDefinition method = _md.GetMethodDefinition(handle);
                SymbolKey declaring = DeclaringType(method.GetDeclaringType());
                if (!_mapSignatures.TryGetValue(method.Signature, out SymbolKey signature))
                {
                    if (!GuardedProviderDecode.TryMethod(_md, method, _mapTypes, null, out MethodSignature<SymbolKey> decoded))
                    {
                        _keys.Truncate(mark);
                        id = -1;
                        return false;
                    }
                    int inner = _keys.Begin();
                    _keys.Int(decoded.GenericParameterCount).Int(decoded.Header.IsInstance ? 1 : 0)
                        .Mark(SymbolPart.Return).Key(decoded.ReturnType);
                    foreach (SymbolKey parameter in decoded.ParameterTypes)
                        _keys.Key(parameter);
                    _mapSignatures[method.Signature] = signature = _keys.End(inner);
                }
                _keys.Mark(SymbolPart.Method).Key(declaring).Name(method.Name).Key(signature);
                id = Table.Intern(_keys.Peek(mark));
                _keys.Truncate(mark);
                return true;
            }
            catch (Exception ex) when (IsIdentityFailure(ex))
            {
                _keys.Truncate(mark);
                id = -1;
                return false;
            }
        }

        static bool IsIdentityFailure(Exception ex)
            => ex is BadImageFormatException or ArgumentException or InvalidOperationException or IdentityRejected;

        /// <summary>A Type identity that <c>MetadataIdentityName</c> rejects.</summary>
        sealed class IdentityRejected(string message) : Exception(message);

        /// <summary><c>MetadataIdentityName.TypeDefinition(includeAssembly: false)</c>.</summary>
        SymbolKey DeclaringType(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_declaringTypes.TryGetValue(token, out SymbolKey key))
                return key;
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.Type);
            DefinitionName(handle, map: true);
            return _declaringTypes[token] = _keys.End(mark);
        }

        /// <summary>The root namespace and every name of a Type definition's declaring chain.</summary>
        void DefinitionName(TypeDefinitionHandle handle, bool map)
        {
            Span<TypeDefinitionHandle> chain = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(_md, handle, chain, out int length, out _, out var rejection)
                || length == 0)
            {
                string detail = rejection?.Detail ?? "The Type has an invalid declaring chain.";
                throw map ? new IdentityRejected(detail) : new BadImageFormatException(detail);
            }
            TypeDefinition root = _md.GetTypeDefinition(chain[0]);
            Namespace(root.Namespace);
            for (int i = 0; i < length; i++)
                _keys.Name(_md.GetTypeDefinition(chain[i]).Name);
        }

        void Namespace(StringHandle handle)
        {
            if (!handle.IsNil && _keys.Names.Length(handle) > 0)
                _keys.Mark(SymbolPart.Namespace).Name(handle);
        }

        // ------------------------------------------------------------------ operand identity

        /// <summary>
        /// The id of a token operand's canonical identity, memoised per token. Fails exactly
        /// where <c>MetadataOperandResolver.TryResolve</c> fails.
        /// </summary>
        public bool TryOperand(DecodedInstruction instruction, out int id, out string? failure)
        {
            long memo = instruction.Operand == OperandKind.InlineString
                ? (1L << 40) | (uint)instruction.OperandValue
                : (uint)instruction.OperandValue;
            if (_operands.TryGetValue(memo, out id))
            {
                failure = null;
                return true;
            }
            OperandMisses++;
            int mark = _keys.Begin();
            try
            {
                Operand(instruction);
                id = Table.Intern(_keys.Peek(mark));
                _keys.Truncate(mark);
                _operands[memo] = id;
                failure = null;
                return true;
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                _keys.Truncate(mark);
                id = -1;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} could not be resolved: {ex.Message}";
                return false;
            }
        }

        void Operand(DecodedInstruction instruction)
        {
            int token = (int)instruction.OperandValue;
            switch (instruction.Operand)
            {
                case OperandKind.InlineString:
                {
                    string value = _md.GetUserString(MetadataTokens.UserStringHandle(token));
                    _keys.Mark(SymbolPart.UserString).Payload(MemoryMarshal.AsBytes(value.AsSpan()));
                    break;
                }
                case OperandKind.InlineMethod:
                    MethodOperand(MetadataTokens.EntityHandle(token));
                    break;
                case OperandKind.InlineField:
                    FieldOperand(MetadataTokens.EntityHandle(token));
                    break;
                case OperandKind.InlineType:
                    _keys.Key(TypeOperand(MetadataTokens.EntityHandle(token)));
                    break;
                case OperandKind.InlineTok:
                {
                    var handle = MetadataTokens.EntityHandle(token);
                    switch (handle.Kind)
                    {
                        case HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                            _keys.Key(TypeOperand(handle));
                            break;
                        case HandleKind.MethodDefinition or HandleKind.MethodSpecification:
                            MethodOperand(handle);
                            break;
                        case HandleKind.MemberReference
                            when _md.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Method:
                            MethodOperand(handle);
                            break;
                        case HandleKind.MemberReference or HandleKind.FieldDefinition:
                            FieldOperand(handle);
                            break;
                        default:
                            throw new BadImageFormatException($"Unsupported ldtoken handle kind {handle.Kind}.");
                    }
                    break;
                }
                case OperandKind.InlineSig:
                {
                    var handle = MetadataTokens.EntityHandle(token);
                    if (handle.Kind != HandleKind.StandaloneSignature)
                        throw new BadImageFormatException($"Expected standalone signature token, got {handle.Kind}.");
                    // Canonical comparison compares a stand-alone signature by its blob bytes (#9801).
                    var signature = _md.GetStandaloneSignature((StandaloneSignatureHandle)handle);
                    _keys.Mark(SymbolPart.SignatureBlob).Blob(_md.GetBlobReader(signature.Signature));
                    break;
                }
                default:
                    throw new InvalidOperationException($"Operand kind {instruction.Operand} is not a metadata token.");
            }
        }

        void MethodOperand(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var method = _md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var signature = GuardedProviderDecode.TryMethod(_md, method, _operandTypes, null, out MethodSignature<SymbolKey> decoded)
                        ? decoded
                        : Fallback(method.Signature);
                    Call(signature, Definition(method.GetDeclaringType()), method.Name, default);
                    break;
                }
                case HandleKind.MemberReference:
                {
                    var member = _md.GetMemberReference((MemberReferenceHandle)handle);
                    if (member.GetKind() != MemberReferenceKind.Method)
                        throw new BadImageFormatException("Expected method member reference.");
                    var signature = GuardedProviderDecode.TryMemberRefMethod(_md, member, _operandTypes, null, out MethodSignature<SymbolKey> decoded)
                        ? decoded
                        : Fallback(member.Signature);
                    Call(signature, MemberParent(member.Parent), member.Name, default);
                    break;
                }
                case HandleKind.MethodSpecification:
                {
                    var specification = _md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    ImmutableArray<SymbolKey> arguments = GuardedProviderDecode.TryMethodSpec(_md, specification, _operandTypes, null, out ImmutableArray<SymbolKey> decoded)
                        ? decoded
                        : [Rejected(specification.Signature)];
                    switch (specification.Method.Kind)
                    {
                        case HandleKind.MethodDefinition:
                        {
                            var method = _md.GetMethodDefinition((MethodDefinitionHandle)specification.Method);
                            var signature = GuardedProviderDecode.TryMethod(_md, method, _operandTypes, null, out MethodSignature<SymbolKey> s)
                                ? s
                                : Fallback(method.Signature);
                            Call(signature, Definition(method.GetDeclaringType()), method.Name, arguments);
                            break;
                        }
                        case HandleKind.MemberReference:
                        {
                            var member = _md.GetMemberReference((MemberReferenceHandle)specification.Method);
                            if (member.GetKind() != MemberReferenceKind.Method)
                                throw new BadImageFormatException("Expected method member reference.");
                            var signature = GuardedProviderDecode.TryMemberRefMethod(_md, member, _operandTypes, null, out MethodSignature<SymbolKey> s)
                                ? s
                                : Fallback(member.Signature);
                            Call(signature, MemberParent(member.Parent), member.Name, arguments);
                            break;
                        }
                        default:
                            throw new BadImageFormatException($"Unsupported method specification target {specification.Method.Kind}.");
                    }
                    break;
                }
                default:
                    throw new BadImageFormatException($"Expected method token, got {handle.Kind}.");
            }
        }

        MethodSignature<SymbolKey> Fallback(BlobHandle blob)
            => GuardedProviderDecode.FallbackSignature(Rejected(blob));

        /// <summary>
        /// <c>FormatCall</c>: this and explicit-this bits, calling convention, generic arity,
        /// parent, name, method arguments, return, and parameters with the vararg sentinel.
        /// </summary>
        void Call(MethodSignature<SymbolKey> signature, SymbolKey parent, StringHandle name, ImmutableArray<SymbolKey> arguments)
        {
            _keys.Mark(SymbolPart.MethodIdentity)
                .Int((int)(signature.Header.Attributes & ThisBits) | (int)signature.Header.CallingConvention)
                .Int(signature.GenericParameterCount)
                .Key(parent)
                .Name(name);
            if (!arguments.IsDefault)
            {
                _keys.Mark(SymbolPart.MethodSpecification).Int(arguments.Length);
                foreach (SymbolKey argument in arguments)
                    _keys.Key(argument);
            }
            Parameters(signature, vararg: true);
        }

        void Parameters(MethodSignature<SymbolKey> signature, bool vararg)
        {
            _keys.Mark(SymbolPart.Return).Key(signature.ReturnType);
            ImmutableArray<SymbolKey> types = signature.ParameterTypes;
            int required = vararg && signature.Header.CallingConvention == SignatureCallingConvention.VarArgs
                ? Math.Clamp(signature.RequiredParameterCount, 0, types.Length)
                : -1;
            for (int i = 0; i < types.Length; i++)
            {
                if (i == required)
                    _keys.Mark(SymbolPart.Vararg);
                _keys.Key(types[i]);
            }
            if (required == types.Length)
                _keys.Mark(SymbolPart.Vararg);
        }

        void FieldOperand(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.FieldDefinition:
                {
                    var field = _md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    SymbolKey type = GuardedProviderDecode.TryField(_md, field, _operandTypes, null, out SymbolKey decoded)
                        ? decoded
                        : Rejected(field.Signature);
                    _keys.Mark(SymbolPart.FieldIdentity).Key(type).Key(Definition(field.GetDeclaringType())).Name(field.Name);
                    break;
                }
                case HandleKind.MemberReference:
                {
                    var member = _md.GetMemberReference((MemberReferenceHandle)handle);
                    if (member.GetKind() != MemberReferenceKind.Field)
                        throw new BadImageFormatException("Expected field member reference.");
                    SymbolKey type = GuardedProviderDecode.TryMemberRefField(_md, member, _operandTypes, null, out SymbolKey decoded)
                        ? decoded
                        : Rejected(member.Signature);
                    _keys.Mark(SymbolPart.FieldIdentity).Key(type).Key(MemberParent(member.Parent)).Name(member.Name);
                    break;
                }
                default:
                    throw new BadImageFormatException($"Expected field token, got {handle.Kind}.");
            }
        }

        SymbolKey MemberParent(EntityHandle parent)
            => parent.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => TypeOperand(parent),
                _ => throw new BadImageFormatException($"Unsupported member parent {parent.Kind}."),
            };

        /// <summary><c>FormatType</c>: a reference outside any assembly takes the current scope.</summary>
        SymbolKey TypeOperand(EntityHandle handle)
            => handle.Kind switch
            {
                HandleKind.TypeDefinition => Definition((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => Reference((TypeReferenceHandle)handle, qualify: true),
                HandleKind.TypeSpecification => Specification((TypeSpecificationHandle)handle),
                _ => throw new BadImageFormatException($"Unsupported type handle {handle.Kind}."),
            };

        /// <summary><c>[&lt;current&gt;]</c> and the definition's name, in operand and signature context alike.</summary>
        SymbolKey Definition(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_definitions.TryGetValue(token, out SymbolKey key))
                return key;
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.Type).Mark(SymbolPart.CurrentScope);
            DefinitionName(handle, map: false);
            return _definitions[token] = _keys.End(mark);
        }

        /// <summary>
        /// <c>FormatTypeReference</c>: the scope token, then each level's namespace and name.
        /// A reference that resolves outside an assembly reference spells the current scope in
        /// operand context and no scope in signature context.
        /// </summary>
        SymbolKey Reference(TypeReferenceHandle handle, bool qualify)
        {
            int token = MetadataTokens.GetToken(handle);
            var cache = qualify ? _referencesQualified : _referencesUnqualified;
            if (cache.TryGetValue(token, out SymbolKey key))
                return key;
            Span<TypeReferenceHandle> chain = stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(_md, handle, chain, out int length, out EntityHandle terminal, out var rejection)
                || length == 0)
            {
                throw new BadImageFormatException(rejection?.Detail ?? "The Type reference has an invalid resolution-scope chain.");
            }
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.Type);
            if (terminal.Kind == HandleKind.AssemblyReference)
            {
                var (part, exact) = Scope((AssemblyReferenceHandle)terminal);
                _keys.Mark(part);
                if (exact is not null)
                    _keys.Payload(exact);
            }
            else if (qualify)
            {
                _keys.Mark(SymbolPart.CurrentScope);
            }
            for (int i = 0; i < length; i++)
            {
                TypeReference level = _md.GetTypeReference(chain[i]);
                Namespace(level.Namespace);
                _keys.Name(level.Name);
            }
            return cache[token] = _keys.End(mark);
        }

        /// <summary>The scope token <c>NormalizeAssemblyIdentity</c> gives an assembly reference.</summary>
        (SymbolPart Part, byte[]? Exact) Scope(AssemblyReferenceHandle handle)
        {
            if (_scopes.TryGetValue(handle, out var scope))
                return scope;
            string identity = AssemblyReferenceIdentity(_md, handle);
            string? token = _currentAssembly is null ? null : AssemblyScopeToken(identity, _currentAssembly, _normalization);
            scope = token switch
            {
                "<current>" => (SymbolPart.CurrentScope, null),
                "<platform>" => (SymbolPart.PlatformScope, null),
                _ => (SymbolPart.ExactScope, Encoding.UTF8.GetBytes(identity)),
            };
            return _scopes[handle] = scope;
        }

        SymbolKey Specification(TypeSpecificationHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_specifications.TryGetValue(token, out SymbolKey key))
                return key;
            key = GuardedProviderDecode.TryTypeSpec(_md, handle, _operandTypes, null, out SymbolKey decoded)
                ? decoded
                : Rejected(_md.GetTypeSpecification(handle).Signature);
            return _specifications[token] = key;
        }

        /// <summary>A signature the guard rejects: <c>RejectedIdentity</c>, a digest of its bytes.</summary>
        SymbolKey Rejected(BlobHandle blob)
        {
            int mark = _keys.Begin();
            return _keys.Mark(SymbolPart.Rejected).Blob(_md.GetBlobReader(blob)).End(mark);
        }

        SymbolKey Primitive(PrimitiveTypeCode code)
        {
            ref SymbolKey slot = ref _primitives[(byte)code];
            if (slot.IsDefault)
            {
                int mark = _keys.Begin();
                slot = _keys.Mark(SymbolPart.Primitive).Int((int)code).End(mark);
            }
            return slot;
        }

        /// <summary>
        /// <c>ArrayShapeText.TryFormat</c>'s spelling per dimension, so two shapes are equal
        /// exactly when their text is; a shape it rejects keeps its raw values, as
        /// <c>RejectedArrayShapeIdentity</c> and the failure spellings do.
        /// </summary>
        SymbolKey Array(SymbolKey element, ArrayShape shape)
        {
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.Array).Key(element);
            if (ArrayShapeText.TryFormat("", shape, out string text))
            {
                _keys.Mark(SymbolPart.ArrayDimension).Payload(Encoding.UTF8.GetBytes(text));
            }
            else
            {
                _keys.Mark(SymbolPart.Rejected).Int(shape.Rank).Int(shape.Sizes.Length);
                foreach (int size in shape.Sizes)
                    _keys.Int(size);
                _keys.Int(shape.LowerBounds.Length);
                foreach (int bound in shape.LowerBounds)
                    _keys.Int(bound);
            }
            return _keys.End(mark);
        }

        SymbolKey Wrap(SymbolPart part, SymbolKey inner)
        {
            int mark = _keys.Begin();
            return _keys.Mark(part).Key(inner).End(mark);
        }

        SymbolKey Instantiation(SymbolKey generic, ImmutableArray<SymbolKey> arguments)
        {
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.GenericInstance).Key(generic).Int(arguments.Length);
            foreach (SymbolKey argument in arguments)
                _keys.Key(argument);
            return _keys.End(mark);
        }

        SymbolKey Parameter(SymbolPart part, int index)
        {
            int mark = _keys.Begin();
            return _keys.Mark(part).Int(index).End(mark);
        }

        SymbolKey Modified(SymbolKey modifier, SymbolKey unmodified, bool required)
        {
            int mark = _keys.Begin();
            return _keys.Mark(required ? SymbolPart.RequiredModifier : SymbolPart.OptionalModifier)
                .Key(modifier).Key(unmodified).End(mark);
        }

        /// <summary>
        /// The operand profile's function pointer keeps the header bits and vararg sentinel;
        /// the method map's keeps only the return and parameter Types, as their texts do.
        /// </summary>
        SymbolKey FunctionPointer(MethodSignature<SymbolKey> signature, bool operand)
        {
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.FunctionPointer);
            if (operand)
                _keys.Int((int)(signature.Header.Attributes & ThisBits) | (int)signature.Header.CallingConvention);
            Parameters(signature, vararg: operand);
            return _keys.End(mark);
        }

        /// <summary><c>MetadataIdentityName.TypeDefinition(includeAssembly: true)</c>.</summary>
        SymbolKey MapDefinition(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_mapDefinitions.TryGetValue(token, out SymbolKey key))
                return key;
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.Type).Mark(SymbolPart.Scope);
            if (_md.IsAssembly)
                _keys.Name(_md.GetAssemblyDefinition().Name);
            else
                _keys.Payload([]);
            DefinitionName(handle, map: true);
            return _mapDefinitions[token] = _keys.End(mark);
        }

        /// <summary>
        /// <c>MetadataIdentityName.TypeReference</c>: the referenced assembly's name (never
        /// its version), the root namespace, and each level's name.
        /// </summary>
        SymbolKey MapReference(TypeReferenceHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_mapReferences.TryGetValue(token, out SymbolKey key))
                return key;
            Span<TypeReferenceHandle> chain = stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(_md, handle, chain, out int length, out EntityHandle terminal, out var rejection)
                || length == 0)
            {
                throw new IdentityRejected(rejection?.Detail ?? "The Type reference has an invalid resolution-scope chain.");
            }
            int mark = _keys.Begin();
            _keys.Mark(SymbolPart.Type);
            if (terminal.Kind == HandleKind.AssemblyReference)
                _keys.Mark(SymbolPart.Scope).Name(_md.GetAssemblyReference((AssemblyReferenceHandle)terminal).Name);
            Namespace(_md.GetTypeReference(chain[0]).Namespace);
            for (int i = 0; i < length; i++)
                _keys.Name(_md.GetTypeReference(chain[i]).Name);
            return _mapReferences[token] = _keys.End(mark);
        }

        SymbolKey MapSpecification(TypeSpecificationHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_mapSpecifications.TryGetValue(token, out SymbolKey key))
                return key;
            key = GuardedProviderDecode.TryTypeSpec(_md, handle, _mapTypes, null, out SymbolKey decoded)
                ? decoded
                : Rejected(_md.GetTypeSpecification(handle).Signature);
            return _mapSpecifications[token] = key;
        }

        abstract class Types(SymbolicIdentity owner) : ISignatureTypeProvider<SymbolKey, object?>
        {
            protected SymbolicIdentity Owner { get; } = owner;

            public SymbolKey GetPrimitiveType(PrimitiveTypeCode typeCode) => Owner.Primitive(typeCode);

            public abstract SymbolKey GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind);

            public abstract SymbolKey GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind);

            public abstract SymbolKey GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind);

            public abstract SymbolKey GetFunctionPointerType(MethodSignature<SymbolKey> signature);

            public SymbolKey GetSZArrayType(SymbolKey elementType) => Owner.Wrap(SymbolPart.SZArray, elementType);

            public SymbolKey GetArrayType(SymbolKey elementType, ArrayShape shape) => Owner.Array(elementType, shape);

            public SymbolKey GetByReferenceType(SymbolKey elementType) => Owner.Wrap(SymbolPart.ByReference, elementType);

            public SymbolKey GetPointerType(SymbolKey elementType) => Owner.Wrap(SymbolPart.Pointer, elementType);

            public SymbolKey GetPinnedType(SymbolKey elementType) => Owner.Wrap(SymbolPart.Pinned, elementType);

            public SymbolKey GetGenericInstantiation(SymbolKey genericType, ImmutableArray<SymbolKey> typeArguments)
                => Owner.Instantiation(genericType, typeArguments);

            public SymbolKey GetGenericTypeParameter(object? genericContext, int index) => Owner.Parameter(SymbolPart.TypeParameter, index);

            public SymbolKey GetGenericMethodParameter(object? genericContext, int index) => Owner.Parameter(SymbolPart.MethodParameter, index);

            public SymbolKey GetModifiedType(SymbolKey modifier, SymbolKey unmodifiedType, bool isRequired)
                => Owner.Modified(modifier, unmodifiedType, isRequired);
        }

        /// <summary>The operand resolver's signature context (<c>SignatureIdentityProvider</c> in IlBodyDiff).</summary>
        sealed class OperandTypes(SymbolicIdentity owner) : Types(owner)
        {
            public override SymbolKey GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
                => Owner.Definition(handle);

            public override SymbolKey GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
                => Owner.Reference(handle, qualify: false);

            public override SymbolKey GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => Owner.Specification(handle);

            public override SymbolKey GetFunctionPointerType(MethodSignature<SymbolKey> signature)
                => Owner.FunctionPointer(signature, operand: true);
        }

        /// <summary>The method map's signature context (<c>SignatureIdentityProvider</c> in IlAssemblyDiff).</summary>
        sealed class MapTypes(SymbolicIdentity owner) : Types(owner)
        {
            public override SymbolKey GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
                => Owner.MapDefinition(handle);

            public override SymbolKey GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
                => Owner.MapReference(handle);

            public override SymbolKey GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => Owner.MapSpecification(handle);

            public override SymbolKey GetFunctionPointerType(MethodSignature<SymbolKey> signature)
                => Owner.FunctionPointer(signature, operand: false);
        }
    }
}
