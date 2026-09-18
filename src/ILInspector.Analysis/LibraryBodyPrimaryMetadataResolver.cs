using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Owns primary-image metadata judgments and the narrow per-method resolver
/// adapters consumed by body-analysis producers.
/// </summary>
internal sealed class LibraryBodyPrimaryMetadataResolver
{
    readonly MetadataReader _reader;
    readonly string _assemblyName;
    readonly string _moduleName;
    readonly AssemblyReferenceIdentity? _assemblyIdentity;
    readonly SameImageSignatureComparer _signatureComparer;
    readonly Guid _mvid;
    readonly MemorySafetyMetadataIndex _memorySafety;
    readonly Func<
        EntityHandle,
        GenericScope,
        MethodDefinitionHandle,
        MemberRef> _resolveMethod;
    readonly Func<
        EntityHandle,
        GenericScope,
        UnsafePresenceWorkBudget,
        MemberRef> _resolvePresenceMethod;
    readonly Func<TypeRef, MethodIdentity, bool>
        _genericParameterCanBeValueType;
    readonly Func<DecodedInstruction, bool>
        _isStableReceiverGetter;
    readonly Action? _asyncStateMachineTypesBuilt;
    readonly Lazy<Dictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>> _localTypeDefinitions;
    // Build owns the only async-state-machine classification cache and prewarms it before
    // parallel method analysis. OptimizationOpportunities_AsyncStateMachineTypesArePrewarmedBeforeParallelAnalysis
    // gates that the consumed cache, rather than a duplicate, is initialized exactly once.
    IReadOnlySet<TypeRef>? _asyncStateMachineTypes;
    IReadOnlySet<TypeDefinitionHandle>? _asyncStateMachineTypeHandles;

    internal LibraryBodyPrimaryMetadataResolver(
        MetadataReader reader,
        string assemblyName,
        Guid mvid,
        Func<
            EntityHandle,
            GenericScope,
            MethodDefinitionHandle,
            MemberRef> resolveMethod,
        Func<
            EntityHandle,
            GenericScope,
            UnsafePresenceWorkBudget,
            MemberRef> resolvePresenceMethod,
        Func<TypeRef, MethodIdentity, bool>
            genericParameterCanBeValueType,
        Func<DecodedInstruction, bool>
            isStableReceiverGetter,
        Action? asyncStateMachineTypesBuilt)
    {
        _reader = reader;
        _assemblyName = assemblyName;
        _moduleName = reader.GetString(
            reader.GetModuleDefinition().Name);
        _assemblyIdentity = reader.IsAssembly
            ? AssemblyReferenceIdentity
                .FromAssemblyDefinition(reader)
            : null;
        _signatureComparer = new(_assemblyIdentity, _moduleName);
        _mvid = mvid;
        _resolveMethod = resolveMethod;
        _resolvePresenceMethod =
            resolvePresenceMethod;
        _genericParameterCanBeValueType =
            genericParameterCanBeValueType;
        _isStableReceiverGetter =
            isStableReceiverGetter;
        _asyncStateMachineTypesBuilt =
            asyncStateMachineTypesBuilt;
        _memorySafety = MemorySafetyMetadataIndex.Create(reader);
        _localTypeDefinitions = new(
            BuildLocalTypeDefinitions,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal MemorySafetyRulesResult MemorySafetyRules =>
        _memorySafety.Rules;

    internal string AssemblyName => _assemblyName;

    internal string ModuleName => _moduleName;

    internal Guid Mvid => _mvid;

    internal ILibraryMethodAnalysisResolver CreateMethodAnalysisResolver(
        GenericScope scope,
        MethodIdentity caller,
        MethodInstructions instructions) =>
        new MethodAnalysisResolver(
            this,
            scope,
            caller,
            instructions);

    internal IMethodCallResolver CreateCallResolver(
        GenericScope scope,
        MethodIdentity caller) =>
        new CallResolver(this, scope, caller);

    internal CallerUnsafeMode? ResolveSameImageCallerUnsafeMode(
        int operandToken,
        MemberRef member,
        UnsafePresenceWorkBudget workBudget)
    {
        int definitionToken = PeelToDefinitionToken(operandToken);
        EntityHandle handle =
            MetadataTokens.EntityHandle(definitionToken);
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinitionHandle method =
                (MethodDefinitionHandle)handle;
            EnsureExactGenericParameters(
                method,
                workBudget);
            return CallerUnsafeModeFromContract(
                _memorySafety.GetMemberContract(
                    method));
        }
        if (handle.Kind == HandleKind.MemberReference)
        {
            EntityHandle parent =
                _reader.GetMemberReference(
                    (MemberReferenceHandle)handle)
                    .Parent;
            if (parent.Kind == HandleKind.MethodDefinition)
            {
                MethodDefinitionHandle method =
                    (MethodDefinitionHandle)parent;
                EnsureExactGenericParameters(
                    method,
                    workBudget);
                return CallerUnsafeModeFromContract(
                    _memorySafety.GetMemberContract(
                        method));
            }
        }

        if (!CanCanonicalizeCurrentModuleReference(
                member.DeclaringType))
        {
            return null;
        }

        var decoder = new TypeRefDecoder(
            workBudget.ReserveCorrespondenceBytes);
        if (!TryResolveSameImageDeclaringType(
                definitionToken,
                member.DeclaringType,
                decoder,
                workBudget,
                out TypeDefinitionHandle typeHandle))
        {
            return null;
        }
        if (member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
            && member.DeclaringType.TypeArguments.Length
                != _reader
                    .GetTypeDefinition(typeHandle)
                    .GetGenericParameters()
                    .Count)
        {
            throw new BadImageFormatException(
                "Constructed declaring-type arity does not match "
                    + "the resolved local type definition.");
        }

        MethodDefinitionHandle target =
            ResolveSameImageMethodDefinition(
                typeHandle,
                member,
                decoder,
                workBudget);
        return target.IsNil
            ? null
            : CallerUnsafeModeFromContract(
                _memorySafety.GetMemberContract(
                    target));
    }

    internal bool MayResolveSameImageCall(
        int operandToken,
        UnsafePresenceWorkBudget workBudget)
    {
        EntityHandle handle =
            MetadataTokens.EntityHandle(operandToken);
        if (handle.Kind == HandleKind.MethodDefinition)
            return true;
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            MethodSpecification specification =
                _reader.GetMethodSpecification(
                    (MethodSpecificationHandle)handle);
            return MayResolveSameImageCall(
                MetadataTokens.GetToken(
                    specification.Method),
                workBudget);
        }
        if (handle.Kind != HandleKind.MemberReference)
            return false;

        EntityHandle parent =
            _reader.GetMemberReference(
                (MemberReferenceHandle)handle)
                .Parent;
        return parent.Kind switch
        {
            HandleKind.TypeDefinition
                or HandleKind.MethodDefinition => true,
            HandleKind.TypeSpecification => true,
            HandleKind.TypeReference =>
                MayResolveSameImageTypeReference(
                    (TypeReferenceHandle)parent,
                    workBudget),
            HandleKind.ModuleReference =>
                ReadPresenceString(
                    _reader.GetModuleReference(
                        (ModuleReferenceHandle)parent).Name,
                    workBudget)
                    .Equals(
                        _moduleName,
                        StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    internal MemberRef ResolveMethod(
        int token,
        GenericScope scope,
        MethodDefinitionHandle caller) =>
        _resolveMethod(
            MetadataTokens.EntityHandle(token),
            scope,
            caller);

    internal MemberRef ResolvePresenceMethod(
        int token,
        GenericScope scope,
        UnsafePresenceWorkBudget workBudget) =>
        _resolvePresenceMethod(
            MetadataTokens.EntityHandle(token),
            scope,
            workBudget);

    internal bool IsAllocatingValueTypeBox(int token, GenericScope scope) =>
        IsAllocatingValueTypeBox(token, ResolveTypeToken(token, scope));

    // True when a `newobj` of this operand constructs a value type. Combines a name-based
    // FRAMEWORK fast path with an authoritative metadata resolution of the constructor's
    // declaring type (TypeDef base chain, or TypeSpec signature blob for constructed
    // generics) — the latter is what classifies in-assembly and cross-assembly structs.
    internal bool IsNonHeapNewObj(int operandToken, TypeRef declaringType)
    {
        if (IsNonHeapConstructionByName(declaringType))
            return true;
        try
        {
            var handle = MetadataTokens.EntityHandle(operandToken);
            EntityHandle typeHandle = handle.Kind switch
            {
                HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => default,
            };
            if (typeHandle.Kind == HandleKind.TypeDefinition)
                return IsValueTypeDefinition((TypeDefinitionHandle)typeHandle);
            if (typeHandle.Kind == HandleKind.TypeSpecification)
                return IsValueTypeSpec((TypeSpecificationHandle)typeHandle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
        return false;
    }

    // Name-based recognition of FRAMEWORK value types whose `newobj` resolves to a bare
    // TypeRef the token dispatch cannot follow (a non-generic framework struct like DateTime
    // or Guid lives in an assembly this one does not load). The common generic framework
    // value types (Span/ReadOnlySpan/Memory/Nullable/ValueTuple`n) are constructed through a
    // TypeSpec and are resolved authoritatively by the signature blob, so they are listed
    // here only as a fast path. In-assembly and cross-assembly value types are NOT matched by
    // name — that is the operand-token metadata path's job — because a display name omits
    // assembly identity and would misclassify an external reference type that shares a
    // namespace+name with an in-assembly struct (#1804 review).
    static bool IsNonHeapConstructionByName(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (definition.Kind != TypeRefKind.Definition || !definition.TrustedFrameworkAssembly)
            return false;
        if (definition.Namespace == "System" && definition.Name is
                "Span`1" or "ReadOnlySpan`1" or "Memory`1" or "ReadOnlyMemory`1" or "Nullable`1"
                or "ValueTuple" or "ValueTuple`1" or "ValueTuple`2" or "ValueTuple`3" or "ValueTuple`4"
                or "ValueTuple`5" or "ValueTuple`6" or "ValueTuple`7" or "ValueTuple`8")
            return true;
        return IsWellKnownValueType(definition.Namespace, definition.Name);
    }

    internal MethodIdentity CreateMethodIdentity(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle, MethodDefinition methodDef, GenericScope scope)
    {
        ImmutableArray<TypeRef> parameterTypes;
        TypeRef returnType;
        byte signatureHeader;
        int requiredParameterCount;
        GenericParameterHandleCollection genericParameters =
            methodDef.GetGenericParameters();
        int genericArity = genericParameters.Count;
        bool hasInvalidGenericParameterDeclaration = false;
        if (SignatureBlobGuard.IsSafeToDecode(_reader, methodDef.Signature, SignatureBlobGuard.Kind.Method))
        {
            var signature = methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
            parameterTypes = signature.ParameterTypes;
            returnType = signature.ReturnType;
            signatureHeader = signature.Header.RawValue;
            requiredParameterCount = signature.RequiredParameterCount;
            hasInvalidGenericParameterDeclaration =
                !MemberResolver.HasExactGenericParameters(
                    _reader,
                    genericParameters,
                    signature.GenericParameterCount);
        }
        else
        {
            parameterTypes = [];
            returnType = TypeRef.Unsupported("method signature nesting depth exceeded");
            signatureHeader = 0;
            requiredParameterCount = -1;
            hasInvalidGenericParameterDeclaration =
                !HasExactGenericParameterDeclaration(methodDef);
        }
        return CreateMethodIdentity(
            typeHandle,
            methodHandle,
            methodDef,
            TypeRefDecoder.Instance.GetTypeFromDefinition(
                _reader,
                typeHandle,
                0),
            _reader.GetString(methodDef.Name),
            parameterTypes,
            returnType,
            signatureHeader,
            requiredParameterCount,
            genericArity,
            GenericParameterNames(methodDef),
            hasInvalidGenericParameterDeclaration);
    }

    internal MethodIdentity CreatePresenceMethodIdentity(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        GenericScope scope,
        UnsafePresenceWorkBudget workBudget)
    {
        workBudget.ReserveCorrespondenceBytes(
            _reader.GetBlobReader(method.Signature).Length);
        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            throw new BadImageFormatException(
                "A method signature exceeds the safe decoding "
                    + "limits.");
        }

        var decoder = new TypeRefDecoder(
            workBudget.ReserveCorrespondenceBytes);
        MethodSignature<TypeRef> signature =
            method.DecodeSignature(
                decoder,
                scope);
        return CreateMethodIdentity(
            typeHandle,
            methodHandle,
            method,
            decoder.GetTypeFromDefinition(
                _reader,
                typeHandle,
                0),
            ReadPresenceString(
                method.Name,
                workBudget),
            signature.ParameterTypes,
            signature.ReturnType,
            signature.Header.RawValue,
            signature.RequiredParameterCount,
            scope.MethodParameters.Length,
            scope.MethodParameters,
            hasInvalidGenericParameterDeclaration: false);
    }

    MethodIdentity CreateMethodIdentity(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        TypeRef declaringType,
        string methodName,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        byte signatureHeader,
        int requiredParameterCount,
        int genericArity,
        ImmutableArray<string> genericParameterNames,
        bool hasInvalidGenericParameterDeclaration)
        => new(
            _assemblyName,
            _mvid,
            declaringType,
            methodName,
            parameterTypes,
            returnType,
            MetadataTokens.GetToken(methodHandle),
            (method.Attributes & MethodAttributes.Static) != 0,
            IsExtensionMethod(typeHandle, method),
            CallerUnsafeModeFromContract(
                _memorySafety.GetMemberContract(methodHandle)),
            genericArity,
            genericParameterNames)
        {
            SignatureHeader = signatureHeader,
            RequiredParameterCount = requiredParameterCount,
            HasInvalidGenericParameterDeclaration =
                hasInvalidGenericParameterDeclaration,
            IsVirtualDispatchOpen =
                DispatchCanTargetOverride(
                    _reader.GetTypeDefinition(typeHandle),
                    method),
        };

    internal static bool DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        (method.Attributes & MethodAttributes.Virtual) != 0
        && (method.Attributes & MethodAttributes.Final) == 0
        && (declaringType.Attributes & TypeAttributes.Sealed) == 0;

    ImmutableArray<string> GenericParameterNames(MethodDefinition methodDef)
    {
        var handles = methodDef.GetGenericParameters();
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(_reader.GetString(_reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    bool IsExtensionMethod(TypeDefinitionHandle typeHandle, MethodDefinition methodDef)
    {
        var type = _reader.GetTypeDefinition(typeHandle);
        return (type.Attributes & TypeAttributes.Abstract) != 0
            && (type.Attributes & TypeAttributes.Sealed) != 0
            && (methodDef.Attributes & MethodAttributes.Static) != 0
            && AttributeReader.HasExtensionAttribute(_reader, type.GetCustomAttributes())
            && AttributeReader.HasExtensionAttribute(_reader, methodDef.GetCustomAttributes());
    }

    static CallerUnsafeMode CallerUnsafeModeFromContract(
        MemorySafetyMemberContractResult contract)
        => contract switch
        {
            MemorySafetyMemberContractResult.None =>
                CallerUnsafeMode.None,
            MemorySafetyMemberContractResult.Implicit =>
                CallerUnsafeMode.Implicit,
            MemorySafetyMemberContractResult.Explicit =>
                CallerUnsafeMode.Explicit,
            MemorySafetyMemberContractResult.Unavailable =>
                CallerUnsafeMode.Unavailable,
            _ => CallerUnsafeMode.Unavailable,
        };

    bool HasAttributeNamed(CustomAttributeHandleCollection attributes, string simpleName, params string[] namespaces)
    {
        foreach (var handle in attributes)
        {
            var (ns, name) = AttributeTypeName(_reader.GetCustomAttribute(handle).Constructor);
            if (name == simpleName && (namespaces.Length == 0 || Array.IndexOf(namespaces, ns) >= 0))
                return true;
        }
        return false;
    }

    // True when the member/type is marked [System.CodeDom.Compiler.GeneratedCode] —
    // the universal source-generator signal (System.Text.Json, regex, etc.). Such code
    // has ordinary names (so the compiler-generated name heuristics miss it) but is not
    // an actionable source-shape optimization target.
    internal bool HasGeneratedCodeAttribute(CustomAttributeHandleCollection attributes)
        => HasAttributeNamed(attributes, "GeneratedCodeAttribute", "System.CodeDom.Compiler");

    // True when the method is marked [System.Runtime.CompilerServices.CompilerGenerated]
    // — record synthesized members (EqualityContract/PrintMembers/Equals/GetHashCode/
    // ToString), lambdas, iterators, and async state machines. These have ordinary names
    // (e.g. get_EqualityContract) that the angle-bracket name heuristics miss, yet none
    // are user-actionable source-shape rewrite targets, so exclude them from collection.
    internal bool HasCompilerGeneratedAttribute(CustomAttributeHandleCollection attributes)
        => HasAttributeNamed(attributes, "CompilerGeneratedAttribute", "System.Runtime.CompilerServices");

    internal bool IsCompilerGeneratedTypeOrEnclosing(
        TypeDefinitionHandle handle)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
            .TryWalkTypeDefinitionDeclaringChain(
                _reader,
                handle,
                chain,
                out int count,
                out _,
                out _))
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            if (HasCompilerGeneratedAttribute(
                    _reader.GetTypeDefinition(
                        chain[i]).GetCustomAttributes()))
            {
                return true;
            }
        }
        return false;
    }

    (string Namespace, string Name) AttributeTypeName(EntityHandle constructor)
    {
        if (constructor.Kind == HandleKind.MemberReference
            && _reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent)
        {
            var typeRef = _reader.GetTypeReference((TypeReferenceHandle)parent);
            return (_reader.GetString(typeRef.Namespace), _reader.GetString(typeRef.Name));
        }
        if (constructor.Kind == HandleKind.MethodDefinition)
        {
            var declType = _reader.GetTypeDefinition(_reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
            return (_reader.GetString(declType.Namespace), _reader.GetString(declType.Name));
        }
        return ("", "");
    }

    // Metadata- and IL-dependent judgments for one method's body analyses.
    // The builder owns the metadata reader, the caller's generic scope, and the raw
    // IL bytes; topic producers see only these narrow answers, so they cannot open a
    // second decode or metadata traversal path.
    sealed class MethodAnalysisResolver(
        LibraryBodyPrimaryMetadataResolver owner,
        GenericScope scope,
        MethodIdentity caller,
        MethodInstructions instructions)
        : ILibraryMethodAnalysisResolver
    {
        public TypeRef ResolveType(int token)
            => owner.ResolveTypeToken(token, scope);

        public MemberRef ResolveMember(int token)
            => owner.ResolveMethod(
                token,
                scope,
                (MethodDefinitionHandle)
                    MetadataTokens.EntityHandle(
                        caller.MetadataToken));

        public NewObjectConstructionKind ClassifyConstruction(
            int operandToken,
            TypeRef declaringType)
        {
            if (!owner.IsNonHeapNewObj(operandToken, declaringType))
                return NewObjectConstructionKind.Heap;
            return owner.IsUnresolvedExternalValueTypeConstruction(
                operandToken,
                declaringType)
                    ? NewObjectConstructionKind.UnresolvedExternalValueType
                    : NewObjectConstructionKind.NonHeap;
        }

        public bool IsDelegateConstructor(int operandToken, MemberRef constructor)
            => owner.IsDelegateConstructorToken(operandToken, constructor);

        public bool IsAllocatingValueTypeBox(int operandToken, TypeRef boxed)
            => owner.IsAllocatingValueTypeBox(operandToken, boxed);

        public bool GenericParameterCanBeValueType(
            TypeRef genericParameter) =>
            owner._genericParameterCanBeValueType(
                genericParameter,
                caller);

        public bool IsStableReceiverGetter(
            DecodedInstruction instruction) =>
            owner._isStableReceiverGetter(instruction);

        public bool IsAsyncStateMachineType(TypeRef? type)
            => owner.IsAsyncStateMachineType(type);

        public bool IsInAssemblyReferenceType(int typeToken)
            => owner.IsInAssemblyReferenceTypeElement(typeToken);

        public (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(int fieldToken)
            => owner.ResolveFieldOwner(fieldToken, scope);

        public ReachingDefinitionsResult AnalyzeReachingDefinitions()
            => ReachingDefinitions.Analyze(
                instructions,
                ArgumentSlotCount(caller));
    }

    // Metadata-dependent call-site facts for one method. MethodCallAnalysis owns
    // the body traversal and projection while this resolver retains reader/scope
    // ownership and the established malformed-metadata behavior.
    sealed class CallResolver(
        LibraryBodyPrimaryMetadataResolver owner,
        GenericScope scope,
        MethodIdentity caller)
        : IMethodCallResolver
    {
        public MemberRef ResolveMember(int token)
            => owner.ResolveMethod(
                token,
                scope,
                (MethodDefinitionHandle)
                    MetadataTokens.EntityHandle(
                        caller.MetadataToken));

        public MemberRef ResolveIndirectCall(int signatureToken)
            => owner.ResolveCalliMember(signatureToken, scope);

        public int DefinitionToken(int operandToken)
            => owner.PeelToDefinitionToken(operandToken);

        public TypeRef ResolveType(int token)
            => owner.ResolveTypeToken(token, scope);

        public (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(
            int fieldToken)
            => owner.ResolveFieldOwner(fieldToken, scope);

        public FieldIdentity? ResolveFieldIdentity(int fieldToken)
            => owner.ResolveFieldIdentity(fieldToken, scope);

        public string? ResolveUserString(int token)
        {
            if ((token & unchecked((int)0xFF000000))
                    != 0x70000000)
            {
                return null;
            }

            try
            {
                return owner._reader.GetUserString(
                    MetadataTokens.UserStringHandle(
                        token & 0x00FFFFFF));
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentException
                    or InvalidOperationException)
            {
                return null;
            }
        }
    }

    // A value-type `newobj` whose operand is an unresolvable external TypeRef is still
    // recorded (as a non-heap annotation) when the type is a recognized framework value
    // type by name, so the row is not silently dropped.
    bool IsUnresolvedExternalValueTypeConstruction(
        int operandToken,
        TypeRef type)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(operandToken);
            var parent = handle.Kind switch
            {
                HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => default,
            };
            return parent.Kind == HandleKind.TypeReference
                && IsNonHeapConstructionByName(type);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    // The declaring type and name behind a field-store operand. Returns (null, null)
    // when the operand is not a resolvable field, leaving the escape-kind judgment to
    // the allocation analysis that asked.
    (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(int fieldToken, GenericScope callerScope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(fieldToken);
            switch (handle.Kind)
            {
                case HandleKind.FieldDefinition:
                    var field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                    return (
                        TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, field.GetDeclaringType(), 0),
                        _reader.GetString(field.Name));
                case HandleKind.MemberReference:
                    return (
                        ResolveMemberReferenceParentType(handle, callerScope),
                        _reader.GetString(_reader.GetMemberReference((MemberReferenceHandle)handle).Name));
                default:
                    return (null, null);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return (null, null);
        }
    }

    FieldIdentity? ResolveFieldIdentity(
        int fieldToken,
        GenericScope callerScope)
    {
        try
        {
            EntityHandle handle = MetadataTokens.EntityHandle(fieldToken);
            (TypeRef? declaringType, string? name) =
                ResolveFieldOwner(fieldToken, callerScope);
            FieldIdentity? fallback =
                FieldIdentity.TryCreate(declaringType, name);
            if (fallback is null)
                return null;

            if (handle.Kind == HandleKind.FieldDefinition)
            {
                return FieldIdentity.CreateLocal(
                    declaringType!,
                    name!,
                    fieldToken);
            }
            if (handle.Kind != HandleKind.MemberReference)
                return fallback;

            MemberReference member =
                _reader.GetMemberReference((MemberReferenceHandle)handle);
            TypeDefinitionHandle parent;
            if (member.Parent.Kind == HandleKind.TypeDefinition)
            {
                parent = (TypeDefinitionHandle)member.Parent;
            }
            else if (CouldReferenceCurrentModule(declaringType!))
            {
                if (!CanCanonicalizeCurrentModuleReference(
                        declaringType!)
                    || !TryResolveLocalTypeDefinition(
                        declaringType!,
                        out parent))
                {
                    return null;
                }
            }
            else
            {
                return fallback;
            }

            FieldDefinitionHandle[] matches =
            [
                .. _reader
                    .GetTypeDefinition(parent)
                    .GetFields()
                    .Where(field =>
                        FieldMatchesMemberReference(
                            member,
                            field,
                            name!)),
            ];
            return matches is [var match]
                ? FieldIdentity.CreateLocal(
                    declaringType!,
                    name!,
                    MetadataTokens.GetToken(match))
                : null;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or IndexOutOfRangeException)
        {
            return null;
        }
    }

    bool CouldReferenceCurrentModule(TypeRef type)
    {
        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;
        return definition.Resolution?.Origin switch
        {
            TypeReferenceOrigin.CurrentAssembly => true,
            TypeReferenceOrigin.AssemblyReference assembly =>
                _assemblyIdentity is not null
                && assembly.Assembly.Name.Equals(
                    _assemblyName,
                    StringComparison.OrdinalIgnoreCase),
            TypeReferenceOrigin.ModuleReference module =>
                module.ModuleName.Equals(
                    _moduleName,
                    StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    bool CanCanonicalizeCurrentModuleReference(TypeRef type)
    {
        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;
        return definition.Resolution?.Origin switch
        {
            TypeReferenceOrigin.CurrentAssembly => true,
            TypeReferenceOrigin.AssemblyReference assembly =>
                _assemblyIdentity is not null
                && assembly.Assembly.IsEquivalentTo(
                    _assemblyIdentity),
            TypeReferenceOrigin.ModuleReference module =>
                module.ModuleName.Equals(
                    _moduleName,
                    StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    string ReadPresenceString(
        StringHandle handle,
        UnsafePresenceWorkBudget workBudget)
    {
        workBudget.ReserveCorrespondenceBytes(
            _reader.GetBlobReader(handle).Length);
        return MetadataSafetyPolicy.ReadStructuralString(
            _reader,
            handle);
    }

    bool TryResolveLocalTypeDefinition(
        TypeRef type,
        out TypeDefinitionHandle handle)
    {
        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;
        if (definition.Resolution is not { Type: var name }
            || !_localTypeDefinitions.Value.TryGetValue(
                name,
                out handle)
            || handle.IsNil)
        {
            handle = default;
            return false;
        }

        return true;
    }

    Dictionary<MetadataTypeDefinitionName, TypeDefinitionHandle>
        BuildLocalTypeDefinitions()
    {
        var definitions = new Dictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>();
        foreach (TypeDefinitionHandle handle in _reader.TypeDefinitions)
        {
            TypeRef type = TypeRefDecoder.Instance.GetTypeFromDefinition(
                _reader,
                handle,
                0);
            if (type.Resolution is not { Type: var name })
                continue;

            if (!definitions.TryAdd(name, handle))
                definitions[name] = default;
        }

        return definitions;
    }

    bool TryResolveSameImageDeclaringType(
        int definitionToken,
        TypeRef declaringType,
        TypeRefDecoder decoder,
        UnsafePresenceWorkBudget workBudget,
        out TypeDefinitionHandle typeHandle)
    {
        EntityHandle handle =
            MetadataTokens.EntityHandle(definitionToken);
        if (handle.Kind == HandleKind.MemberReference)
        {
            EntityHandle parent =
                _reader.GetMemberReference(
                    (MemberReferenceHandle)handle)
                    .Parent;
            if (parent.Kind == HandleKind.TypeDefinition)
            {
                typeHandle = (TypeDefinitionHandle)parent;
                return true;
            }
            if (parent.Kind == HandleKind.MethodDefinition)
            {
                typeHandle = _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)parent)
                    .GetDeclaringType();
                return true;
            }
        }

        TypeRef definition =
            declaringType.Kind == TypeRefKind.GenericInstance
                ? declaringType.ElementType ?? declaringType
                : declaringType;
        if (definition.Resolution is not { Type: var targetName })
        {
            typeHandle = default;
            return false;
        }

        Dictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle> definitions =
                workBudget.GetOrCreateLocalTypeDefinitions(
                    () => BuildPresenceLocalTypeDefinitions(
                        decoder,
                        workBudget));
        if (!definitions.TryGetValue(
                targetName,
                out typeHandle))
        {
            return false;
        }
        if (typeHandle.IsNil)
        {
            throw new InvalidDataException(
                "Unsafe evidence presence is incomplete because a local "
                    + "type reference has ambiguous declaring-type "
                    + "correspondence.");
        }
        return true;
    }

    Dictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle> BuildPresenceLocalTypeDefinitions(
            TypeRefDecoder decoder,
            UnsafePresenceWorkBudget workBudget)
    {
        var definitions = new Dictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>();
        foreach (TypeDefinitionHandle candidateHandle
            in _reader.TypeDefinitions)
        {
            workBudget.ReserveCorrespondenceRow();
            TypeRef candidate =
                decoder.GetTypeFromDefinition(
                    _reader,
                    candidateHandle,
                    0);
            if (candidate.Resolution is not { Type: var candidateName })
                continue;

            if (!definitions.TryAdd(
                    candidateName,
                    candidateHandle))
            {
                definitions[candidateName] = default;
            }
        }

        return definitions;
    }

    MethodDefinitionHandle ResolveSameImageMethodDefinition(
        TypeDefinitionHandle typeHandle,
        MemberRef member,
        TypeRefDecoder decoder,
        UnsafePresenceWorkBudget workBudget)
    {
        TypeDefinition typeDefinition =
            _reader.GetTypeDefinition(typeHandle);
        MethodDefinitionHandle resolved = default;
        foreach (MethodDefinitionHandle methodHandle
            in typeDefinition.GetMethods())
        {
            workBudget.ReserveCorrespondenceRow();
            MethodDefinition methodDefinition =
                _reader.GetMethodDefinition(methodHandle);
            workBudget.ReserveCorrespondenceBytes(
                _reader.GetBlobReader(
                    methodDefinition.Name)
                    .Length);
            if (!_reader.StringComparer.Equals(
                    methodDefinition.Name,
                    member.Name))
            {
                continue;
            }

            workBudget.ReserveCorrespondenceBytes(
                _reader.GetBlobReader(
                    methodDefinition.Signature)
                    .Length);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    methodDefinition.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                throw new BadImageFormatException(
                    "A same-image target signature exceeds the safe decoding limits.");
            }

            MethodSignature<TypeRef> signature =
                methodDefinition.DecodeSignature(
                    decoder,
                    GenericScope.Empty);
            int typeParameterCount =
                typeDefinition.GetGenericParameters()
                    .Count;
            GenericParameterHandleCollection
                methodGenericParameters =
                    methodDefinition.GetGenericParameters();
            int methodParameterCount =
                methodGenericParameters.Count;
            if (!MemberResolver.HasExactGenericParameters(
                    _reader,
                    methodGenericParameters,
                    signature.GenericParameterCount,
                    workBudget.ReserveCorrespondenceRow)
                || SignatureTypeFacts.IsMalformed(
                    signature.ReturnType,
                    typeParameterCount,
                    methodParameterCount)
                || signature.ParameterTypes.Any(
                    parameter =>
                        SignatureTypeFacts.IsMalformed(
                            parameter,
                            typeParameterCount,
                            methodParameterCount)))
            {
                throw new BadImageFormatException(
                    "A same-image target signature contains an "
                        + "unsupported or malformed type.");
            }
            if (!MethodDefinitionMap.SignatureMatches(
                    signature.ParameterTypes,
                    signature.ReturnType,
                    typeParameterCount,
                    methodParameterCount,
                    (methodDefinition.Attributes
                        & MethodAttributes.Static) != 0,
                    signature.Header.RawValue,
                    signature.RequiredParameterCount,
                    member.OpenSignatureParameters,
                    member.OpenSignatureReturn,
                    member.GenericArity,
                    member.HasThis,
                    member.SignatureHeader,
                    member.RequiredParameterCount,
                    _signatureComparer.Matches))
            {
                continue;
            }
            if (!resolved.IsNil)
            {
                throw new InvalidDataException(
                    "Unsafe evidence presence is incomplete because a local "
                        + "member reference has ambiguous MethodDef "
                        + "correspondence.");
            }
            resolved = methodHandle;
        }

        return resolved;
    }

    bool MayResolveSameImageTypeReference(
        TypeReferenceHandle handle,
        UnsafePresenceWorkBudget workBudget)
    {
        TypeRef type = new TypeRefDecoder(
            workBudget.ReserveCorrespondenceBytes)
            .GetTypeFromReference(
                _reader,
                handle,
                0);
        if (type.Kind != TypeRefKind.Unsupported)
            return CanCanonicalizeCurrentModuleReference(type);
        if (!RawTypeReferenceMayResolveToCurrentModule(
                handle,
                workBudget))
        {
            return false;
        }
        throw new BadImageFormatException(
            "A same-image declaring type contains unsupported "
                + "or malformed metadata.");
    }

    bool RawTypeReferenceMayResolveToCurrentModule(
        TypeReferenceHandle handle,
        UnsafePresenceWorkBudget workBudget)
    {
        var visited =
            new HashSet<TypeReferenceHandle>();
        EntityHandle current = handle;
        while (current.Kind == HandleKind.TypeReference)
        {
            TypeReferenceHandle currentHandle =
                (TypeReferenceHandle)current;
            if (!visited.Add(currentHandle))
            {
                throw new BadImageFormatException(
                    "A type reference scope contains a cycle.");
            }
            workBudget.ReserveCorrespondenceRow();
            current = _reader
                .GetTypeReference(currentHandle)
                .ResolutionScope;
        }

        return current.Kind switch
        {
            HandleKind.ModuleDefinition => true,
            HandleKind.ModuleReference =>
                ReadPresenceString(
                    _reader.GetModuleReference(
                        (ModuleReferenceHandle)current).Name,
                    workBudget)
                    .Equals(
                        _moduleName,
                        StringComparison.OrdinalIgnoreCase),
            HandleKind.AssemblyReference =>
                IsCurrentAssemblyReference(
                    (AssemblyReferenceHandle)current,
                    workBudget),
            _ => false,
        };
    }

    bool IsCurrentAssemblyReference(
        AssemblyReferenceHandle handle,
        UnsafePresenceWorkBudget workBudget)
    {
        workBudget.ReserveCorrespondenceRow();
        System.Reflection.Metadata.AssemblyReference reference =
            _reader.GetAssemblyReference(handle);
        workBudget.ReserveCorrespondenceBytes(
            _reader.GetBlobReader(reference.Name).Length);
        if (!reference.Culture.IsNil)
        {
            workBudget.ReserveCorrespondenceBytes(
                _reader.GetBlobReader(
                    reference.Culture).Length);
        }
        if (!reference.PublicKeyOrToken.IsNil)
        {
            workBudget.ReserveCorrespondenceBytes(
                _reader.GetBlobReader(
                    reference.PublicKeyOrToken).Length);
        }
        return _assemblyIdentity is not null
            && AssemblyReferenceIdentity
                .From(_reader, handle)
                .IsEquivalentTo(_assemblyIdentity);
    }

    bool FieldMatchesMemberReference(
        MemberReference member,
        FieldDefinitionHandle fieldHandle,
        string name)
    {
        FieldDefinition field =
            _reader.GetFieldDefinition(fieldHandle);
        if (_reader.GetString(field.Name) != name
            || !SignatureBlobGuard.IsSafeAndCompleteToDecode(
                _reader,
                member.Signature,
                SignatureBlobGuard.Kind.Field)
            || !SignatureBlobGuard.IsSafeAndCompleteToDecode(
                _reader,
                field.Signature,
                SignatureBlobGuard.Kind.Field))
        {
            return false;
        }

        BlobReader left = _reader.GetBlobReader(member.Signature);
        BlobReader right = _reader.GetBlobReader(field.Signature);
        if (left.Length != right.Length)
            return false;
        while (left.RemainingBytes > 0)
        {
            if (left.ReadByte() != right.ReadByte())
                return false;
        }
        return true;
    }

    bool IsDelegateConstructorToken(int operandToken, MemberRef constructor)
    {
        if (constructor.Kind != MemberKind.Constructor
            || constructor.ParameterTypes.Length != 2
            || !constructor.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Object"))
            || !constructor.ParameterTypes[1].Equals(TypeRef.CoreLib("System", "IntPtr")))
        {
            return false;
        }

        var definition = constructor.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? constructor.DeclaringType.ElementType ?? constructor.DeclaringType
            : constructor.DeclaringType;
        if (definition.TrustedFrameworkAssembly
            && definition.Assembly == TypeRef.CoreLibrary
            && definition.Namespace == "System"
            && (definition.Name.StartsWith("Func`", StringComparison.Ordinal)
                || definition.Name.StartsWith("Action`", StringComparison.Ordinal)
                || definition.Name == "Action"))
        {
            return true;
        }

        try
        {
            var handle = MetadataTokens.EntityHandle(operandToken);
            EntityHandle parent = handle.Kind switch
            {
                HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => default,
            };
            return parent.Kind == HandleKind.TypeDefinition
                && TypeDerivesFromMulticastDelegate((TypeDefinitionHandle)parent);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    bool TypeDerivesFromMulticastDelegate(TypeDefinitionHandle handle)
    {
        var visited = new HashSet<TypeDefinitionHandle>();
        var current = handle;
        while (visited.Add(current))
        {
            var baseHandle = _reader.GetTypeDefinition(current).BaseType;
            switch (baseHandle.Kind)
            {
                case HandleKind.TypeReference:
                    var baseRef = _reader.GetTypeReference((TypeReferenceHandle)baseHandle);
                    return _reader.GetString(baseRef.Namespace) == "System"
                        && _reader.GetString(baseRef.Name) == "MulticastDelegate";
                case HandleKind.TypeDefinition:
                    current = (TypeDefinitionHandle)baseHandle;
                    continue;
                default:
                    return false;
            }
        }
        return false;
    }

    internal string? CalliReturnDetail(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                return null;
            var standalone = _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    standalone.Signature,
                    SignatureBlobGuard.Kind.StandaloneMethod))
                return null;
            var signature = standalone.DecodeMethodSignature(TypeRefDecoder.Instance, scope);
            return signature.ReturnType.ToDisplayString();
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    bool IsAsyncStateMachineType(TypeRef? type)
    {
        if (type is null)
            return false;
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        return AsyncStateMachineTypes().Contains(definition);
    }

    internal IReadOnlySet<TypeRef> AsyncStateMachineTypes()
    {
        EnsureAsyncStateMachineTypes();
        return _asyncStateMachineTypes!;
    }

    internal IReadOnlySet<TypeDefinitionHandle>
        AsyncStateMachineTypeHandles()
    {
        EnsureAsyncStateMachineTypes();
        return _asyncStateMachineTypeHandles!;
    }

    void EnsureAsyncStateMachineTypes()
    {
        if (_asyncStateMachineTypes is not null)
            return;

        _asyncStateMachineTypesBuilt?.Invoke();
        var types = new HashSet<TypeRef>();
        var handles = new HashSet<TypeDefinitionHandle>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDef = _reader.GetTypeDefinition(typeHandle);
            var type = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0);
            if (!type.Name.Contains(">d__", StringComparison.Ordinal))
                continue;
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = _reader.GetInterfaceImplementation(implementationHandle);
                var interfaceType = TypeFromEntity(implementation.Interface);
                var definition = interfaceType.Kind == TypeRefKind.GenericInstance
                    ? interfaceType.ElementType ?? interfaceType
                    : interfaceType;
                if (FrameworkIdentity.IsCoreLibraryType(definition, "System.Runtime.CompilerServices", "IAsyncStateMachine"))
                {
                    types.Add(type);
                    handles.Add(typeHandle);
                    break;
                }
            }
        }
        _asyncStateMachineTypeHandles = handles;
        _asyncStateMachineTypes = types;
    }

    TypeRef TypeFromEntity(EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, new GenericScope([], []), (TypeSpecificationHandle)handle, 0),
                _ => TypeRef.Unsupported("interface implementation"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return TypeRef.Unsupported("interface implementation");
        }
    }

    // True only when a `box` operand is positively identified as a value type that
    // unconditionally allocates. ECMA-335 allows `box` on reference types (no allocation),
    // generic parameters (compiler-mandated / JIT-specialized), and `Nullable<T>` (no
    // allocation when null) — all excluded to avoid false positives. In-assembly types are
    // resolved authoritatively via their base type; external types are accepted only from a
    // curated set of well-known framework value types.
    bool IsAllocatingValueTypeBox(int token, TypeRef boxed)
    {
        // Nullable<T> boxing allocates only when HasValue; conservatively exclude.
        var leaf = boxed.Kind == TypeRefKind.GenericInstance ? boxed.ElementType ?? boxed : boxed;
        if (leaf.Kind == TypeRefKind.Definition && leaf.Namespace == "System" && leaf.Name == "Nullable`1")
            return false;

        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.TypeDefinition)
                return IsValueTypeDefinition((TypeDefinitionHandle)handle);
            // A constructed generic type (e.g. Box<int>) is a TypeSpec whose signature blob
            // directly encodes value-type-ness (ELEMENT_TYPE_VALUETYPE vs ELEMENT_TYPE_CLASS),
            // so we don't need to resolve the definition. Covers in-assembly and external
            // generic structs alike; Nullable<T> is already excluded above.
            if (handle.Kind == HandleKind.TypeSpecification)
                return IsValueTypeSpec((TypeSpecificationHandle)handle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }

        return leaf.Kind == TypeRefKind.Definition
            && leaf.TrustedFrameworkAssembly
            && IsWellKnownValueType(leaf.Namespace, leaf.Name);
    }

    // Reads a TypeSpec signature blob to decide value-type-ness directly from metadata. The
    // signature is an ELEMENT_TYPE_* stream; a generic instance is GENERICINST followed by
    // VALUETYPE (0x11) or CLASS (0x12), and a bare value/class spec starts with that byte.
    bool IsValueTypeSpec(TypeSpecificationHandle handle)
    {
        const byte ElementTypeValueType = 0x11;
        const byte ElementTypeGenericInst = 0x15;
        var blob = _reader.GetBlobReader(_reader.GetTypeSpecification(handle).Signature);
        if (blob.RemainingBytes == 0)
            return false;
        byte code = blob.ReadByte();
        if (code == ElementTypeGenericInst)
        {
            if (blob.RemainingBytes == 0)
                return false;
            code = blob.ReadByte();
        }
        // VALUETYPE (0x11) is a value type; CLASS (0x12) and everything else is not.
        return code == ElementTypeValueType;
    }

    // Authoritative in-assembly check: a value type extends System.ValueType or System.Enum.
    bool IsValueTypeDefinition(TypeDefinitionHandle handle)
    {
        var baseHandle = _reader.GetTypeDefinition(handle).BaseType;
        if (baseHandle.IsNil)
            return false;
        var (ns, name) = baseHandle.Kind switch
        {
            HandleKind.TypeReference => (_reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseHandle).Namespace),
                _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseHandle).Name)),
            HandleKind.TypeDefinition => (_reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle).Namespace),
                _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle).Name)),
            _ => ("", ""),
        };
        return ns == "System" && name is "ValueType" or "Enum";
    }

    static bool IsWellKnownValueType(string ns, string name)
        => (ns == "System" && name is "Boolean" or "Byte" or "SByte" or "Char"
                or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64"
                or "Single" or "Double" or "IntPtr" or "UIntPtr" or "Decimal"
                or "Half" or "Int128" or "UInt128"
                or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Guid")
           || (ns == "System.Numerics" && name is "BigInteger" or "Complex")
           || (ns == "System" && name.StartsWith("ValueTuple", StringComparison.Ordinal))
           || (ns == "System.Collections.Generic" && name == "KeyValuePair`2");

    // Resolves a metadata type token (TypeDef/TypeRef/TypeSpec) to a TypeRef, used to
    // inspect a newarr element type. Returns Unsupported on any malformed/unknown token.
    TypeRef ResolveTypeToken(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, scope, (TypeSpecificationHandle)handle, 0),
                _ => TypeRef.Unsupported("newarr element"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return TypeRef.Unsupported("newarr element");
        }
    }

    bool IsInAssemblyReferenceTypeElement(int elementToken)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(elementToken);
            return handle.Kind == HandleKind.TypeDefinition
                && !IsValueTypeDefinition((TypeDefinitionHandle)handle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    TypeRef? ResolveMemberReferenceParentType(EntityHandle handle, GenericScope callerScope)
    {
        var parent = _reader.GetMemberReference((MemberReferenceHandle)handle).Parent;
        return parent.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)parent, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)parent, 0),
            HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, callerScope, (TypeSpecificationHandle)parent, 0),
            _ => null,
        };
    }

    static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    MemberRef ResolveCalliMember(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                return MemberRef.Unsupported("calli signature unavailable");
            var standalone = _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    standalone.Signature,
                    SignatureBlobGuard.Kind.StandaloneMethod))
            {
                return MemberRef.Unsupported("calli signature unavailable");
            }

            var signature = standalone.DecodeMethodSignature(TypeRefDecoder.Instance, scope);
            return new MemberRef(
                TypeRef.Unsupported("function pointer"),
                "calli",
                signature.ParameterTypes,
                signature.ReturnType,
                MemberKind.FunctionPointer)
            {
                HasThis = signature.Header.IsInstance,
                SignatureHeader = signature.Header.RawValue,
                RequiredParameterCount =
                    signature.RequiredParameterCount,
                GenericArity = signature.GenericParameterCount,
                OpenParameterTypes = signature.ParameterTypes,
                OpenReturnType = signature.ReturnType,
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            return MemberRef.Unsupported("calli signature unavailable");
        }
    }

    // Recover an exact local MethodDef from metadata shapes that carry one.
    int PeelToDefinitionToken(int token)
        => MemberResolver.DefinitionToken(_reader, token);

    internal GenericScope CreateScope(TypeDefinition typeDef, MethodDefinition methodDef)
        => new(GenericParameterNames(typeDef.GetGenericParameters()), GenericParameterNames(methodDef.GetGenericParameters()));

    internal GenericScope CreatePresenceScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition,
        UnsafePresenceWorkBudget workBudget)
    {
        GenericScope scope =
            new(
                GenericParameterNames(
                    typeDefinition.GetGenericParameters(),
                    workBudget),
                GenericParameterNames(
                    methodDefinition.GetGenericParameters(),
                    workBudget));
        EnsureExactGenericParameters(
            methodDefinition,
            scope,
            workBudget);
        return scope;
    }

    void EnsureExactGenericParameters(
        MethodDefinitionHandle methodHandle,
        UnsafePresenceWorkBudget workBudget)
    {
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        EnsureExactGenericParameters(
            method,
            GenericScope.Empty,
            workBudget);
    }

    void EnsureExactGenericParameters(
        MethodDefinition method,
        GenericScope scope,
        UnsafePresenceWorkBudget workBudget)
    {
        if (!HasExactGenericParameters(
                method,
                scope,
                workBudget))
        {
            throw new BadImageFormatException(
                "Method generic parameter declarations do not "
                    + "match the signature.");
        }
    }

    bool HasExactGenericParameters(
        MethodDefinition method,
        GenericScope scope,
        UnsafePresenceWorkBudget workBudget)
    {
        workBudget.ReserveCorrespondenceBytes(
            _reader.GetBlobReader(method.Signature).Length);
        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            throw new BadImageFormatException(
                "A method signature exceeds the safe decoding "
                    + "limits.");
        }

        MethodSignature<TypeRef> signature =
            method.DecodeSignature(
                new TypeRefDecoder(
                    workBudget.ReserveCorrespondenceBytes),
                scope);
        return MemberResolver.HasExactGenericParameters(
            _reader,
            method.GetGenericParameters(),
            signature.GenericParameterCount,
            workBudget.ReserveCorrespondenceRow);
    }

    bool HasExactGenericParameterDeclaration(
        MethodDefinition method)
    {
        try
        {
            BlobReader signature =
                _reader.GetBlobReader(method.Signature);
            SignatureHeader header =
                signature.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method)
                return false;

            int signatureGenericParameterCount =
                header.IsGeneric
                    ? signature.ReadCompressedInteger()
                    : 0;
            return signatureGenericParameterCount >= 0
                && MemberResolver.HasExactGenericParameters(
                    _reader,
                    method.GetGenericParameters(),
                    signatureGenericParameterCount);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            return false;
        }
    }

    ImmutableArray<string> GenericParameterNames(GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(_reader.GetString(_reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    ImmutableArray<string> GenericParameterNames(
        GenericParameterHandleCollection handles,
        UnsafePresenceWorkBudget workBudget)
    {
        if (handles.Count == 0)
            return [];
        var names =
            ImmutableArray.CreateBuilder<string>(
                handles.Count);
        foreach (GenericParameterHandle handle in handles)
        {
            workBudget.ReserveCorrespondenceRow();
            names.Add(
                ReadPresenceString(
                    _reader.GetGenericParameter(handle).Name,
                    workBudget));
        }
        return names.MoveToImmutable();
    }

}
