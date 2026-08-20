using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;

using ILInspector.Instructions;
using ILInspector.Metadata;

using MethodReferenceKey =
    ILInspector.Analysis.LibraryBodyMethodReferenceResolver.MethodReferenceKey;
using MethodReferenceKeyComparer =
    ILInspector.Analysis.LibraryBodyMethodReferenceResolver.MethodReferenceKeyComparer;

namespace ILInspector.Analysis;

/// <summary>
/// Owns assembly-scoped lifted-method source-owner correlation, including
/// top-level execution and async state-machine authentication.
/// </summary>
internal sealed class LibraryBodyLiftedSourceOwnerResolver
{
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver;
    readonly LibraryBodyMethodReferenceResolver
        _methodReferenceResolver;
    readonly Action<MethodDefinitionHandle>? _methodBodyReferenceIndexed;
    readonly Action? _typeDefinitionIndexBuilt;
    readonly ConcurrentDictionary<
        TypeDefinitionHandle,
        Lazy<IReadOnlyDictionary<string, ImmutableArray<MethodDefinitionHandle>>>>
        _methodsByName = new();
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<MethodBodyReferenceEvidence>>
        _methodBodyReferences = new();
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<TopLevelExecutionMethod?>>
        _topLevelExecutionMethods = new();
    readonly ConcurrentDictionary<
        LiftedOwnerGroupKey,
        Lazy<LiftedOwnerGroupEvidence>>
        _liftedOwnerGroups = new();
    readonly Lazy<IReadOnlyDictionary<
        LiftedOwnerGroupKey,
        ImmutableArray<MethodDefinitionHandle>>>
        _liftedMethodsByOwner;
    readonly ConcurrentDictionary<
        string,
        Lazy<TypeDefinitionHandle?>>
        _serializedAsyncStateMachineTypes =
            new(StringComparer.Ordinal);
    readonly Lazy<MetadataTypeDefinitionIndex> _typeDefinitionIndex;

    internal LibraryBodyLiftedSourceOwnerResolver(
        MetadataReader reader,
        PEReader peReader,
        LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
        LibraryBodyMethodReferenceResolver methodReferenceResolver,
        Action<MethodDefinitionHandle>? methodBodyReferenceIndexed = null,
        Action? typeDefinitionIndexBuilt = null)
    {
        _reader = reader;
        _peReader = peReader;
        _primaryMetadataResolver = primaryMetadataResolver;
        _methodReferenceResolver = methodReferenceResolver;
        _methodBodyReferenceIndexed = methodBodyReferenceIndexed;
        _typeDefinitionIndexBuilt = typeDefinitionIndexBuilt;
        _typeDefinitionIndex = new(
            BuildTypeDefinitionIndex,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _liftedMethodsByOwner = new(
            BuildLiftedMethodsByOwner,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal bool TryResolve(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out MethodIdentity? sourceOwner,
        out bool sourceGenerated,
        IReadOnlySet<int>? ownerMethodScope = null,
        Func<TypeRef, bool>? ownerTypeScope = null,
        bool directlySelectedBody = false)
    {
        sourceOwner = null;
        sourceGenerated = false;
        if (!TryGetLiftedOwnerGroup(
                liftedMethod,
                out LiftedOwnerGroupKey group))
            return false;

        TypeDefinitionHandle ownerType = group.OwnerType;
        TypeDefinition ownerDefinition = _reader.GetTypeDefinition(ownerType);
        string ownerName = group.OwnerName;
        int liftedToken =
            MetadataTokens.GetToken(liftedHandle);
        if (ownerMethodScope is not null
            && !directlySelectedBody
            && (!MethodsByName(ownerType).TryGetValue(
                    ownerName,
                    out ImmutableArray<MethodDefinitionHandle> scopedOwners)
                || !scopedOwners.Any(handle =>
                    ownerMethodScope.Contains(
                        MetadataTokens.GetToken(handle)))))
        {
            return false;
        }
        if (ownerTypeScope is not null
            && !ownerTypeScope(
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    _reader,
                    ownerType,
                    0)))
        {
            return false;
        }
        MethodReferenceKey member =
            _methodReferenceResolver.CreateIdentity(
                liftedIdentity.Name,
                liftedIdentity.DeclaringType,
                liftedMethod.Signature);
        LiftedOwnerGroupEvidence ownerGroup =
            LiftedOwnerGroup(
                ownerType,
                ownerName,
                directlySelectedBody
                    ? null
                    : ownerMethodScope);
        if (!ownerGroup.TryResolve(
                liftedToken,
                member,
                out MethodDefinitionHandle ownerHandle,
                out bool ownerIsTopLevelEntryPoint))
        {
            return false;
        }

        var definition = _reader.GetMethodDefinition(ownerHandle);
        sourceGenerated =
            _primaryMetadataResolver.HasGeneratedCodeAttribute(
                definition.GetCustomAttributes())
            || !ownerIsTopLevelEntryPoint
                && (_primaryMetadataResolver.HasCompilerGeneratedAttribute(
                        definition.GetCustomAttributes())
                    || IsCompilerGeneratedSourceTypeOrEnclosing(ownerType));
        sourceOwner = _primaryMetadataResolver.CreateMethodIdentity(
            ownerType,
            ownerHandle,
            definition,
            _primaryMetadataResolver.CreateScope(
                ownerDefinition,
                definition));
        return true;
    }

    LiftedOwnerGroupEvidence LiftedOwnerGroup(
        TypeDefinitionHandle ownerType,
        string ownerName,
        IReadOnlySet<int>? ownerMethodScope)
    {
        var key = new LiftedOwnerGroupKey(ownerType, ownerName);
        if (ownerMethodScope is not null)
        {
            return BuildLiftedOwnerGroup(
                key,
                ownerMethodScope);
        }
        return _liftedOwnerGroups.GetOrAdd(
            key,
            group => new Lazy<LiftedOwnerGroupEvidence>(
                () => BuildLiftedOwnerGroup(
                    group,
                    ownerMethodScope: null),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    LiftedOwnerGroupEvidence BuildLiftedOwnerGroup(
        LiftedOwnerGroupKey group,
        IReadOnlySet<int>? ownerMethodScope)
    {
        var evidence = new LiftedOwnerGroupEvidence();
        if (!MethodsByName(group.OwnerType).TryGetValue(
                group.OwnerName,
                out ImmutableArray<MethodDefinitionHandle> owners))
        {
            return evidence;
        }

        if (!_liftedMethodsByOwner.Value.TryGetValue(
                group,
                out ImmutableArray<MethodDefinitionHandle> liftedMethods))
        {
            return evidence;
        }
        if (liftedMethods.Length
            > MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            throw new BadImageFormatException(
                "Lifted-method ownership exceeds the metadata "
                + "relationship node budget.");
        }

        var candidates = new LiftedMethodCandidates(
            _reader,
            _methodReferenceResolver,
            liftedMethods);
        var reachableOwners =
            new Dictionary<
                MethodDefinitionHandle,
                HashSet<MethodDefinitionHandle>>();
        var pending = new Queue<(
            MethodDefinitionHandle Body,
            MethodDefinitionHandle Owner)>();
        var topLevelOwners =
            new Dictionary<MethodDefinitionHandle, bool>();

        foreach (MethodDefinitionHandle ownerHandle in owners)
        {
            if (ownerMethodScope is not null
                && !ownerMethodScope.Contains(
                    MetadataTokens.GetToken(ownerHandle)))
            {
                continue;
            }
            MethodDefinitionHandle executionHandle = ownerHandle;
            TopLevelExecutionMethod execution = default;
            bool topLevel = group.OwnerName == "<Main>$"
                && TryGetTopLevelExecutionMethod(
                    ownerHandle,
                    out execution);
            topLevelOwners.Add(ownerHandle, topLevel);
            if (topLevel)
            {
                AddReachableLiftedMethods(
                    ownerHandle,
                    MethodBodyReferences(execution.Method),
                    candidates,
                    reachableOwners,
                    pending);
                continue;
            }

            AddReachableLiftedMethods(
                ownerHandle,
                MethodBodyReferences(executionHandle),
                candidates,
                reachableOwners,
                pending);
            MethodDefinition ownerMethod =
                _reader.GetMethodDefinition(ownerHandle);
            if (TryGetAsyncStateMachineMoveNext(
                    ownerMethod,
                    out MethodDefinitionHandle moveNextHandle))
            {
                AddReachableLiftedMethods(
                    ownerHandle,
                    MethodBodyReferences(moveNextHandle),
                    candidates,
                    reachableOwners,
                    pending);
            }
        }

        int visited = 0;
        while (pending.TryDequeue(
            out (
                MethodDefinitionHandle Body,
                MethodDefinitionHandle Owner) current))
        {
            if (++visited > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw new BadImageFormatException(
                    "Lifted-method ownership exceeds the metadata "
                    + "relationship node budget.");
            }

            AddReachableLiftedMethods(
                current.Owner,
                MethodBodyReferences(current.Body),
                candidates,
                reachableOwners,
                pending);
            MethodDefinition bodyMethod =
                _reader.GetMethodDefinition(current.Body);
            if (TryGetAsyncStateMachineMoveNext(
                    bodyMethod,
                    out MethodDefinitionHandle moveNextHandle))
            {
                AddReachableLiftedMethods(
                    current.Owner,
                    MethodBodyReferences(moveNextHandle),
                    candidates,
                    reachableOwners,
                    pending);
            }
        }

        foreach ((
            MethodDefinitionHandle body,
            HashSet<MethodDefinitionHandle> bodyOwners)
            in reachableOwners)
        {
            LiftedMethodCandidate candidate = candidates.ByHandle[body];
            foreach (MethodDefinitionHandle owner in bodyOwners)
            {
                evidence.Add(
                    candidate,
                    owner,
                    topLevelOwners[owner]);
            }
        }
        return evidence;
    }

    void AddReachableLiftedMethods(
        MethodDefinitionHandle owner,
        MethodBodyReferenceEvidence references,
        LiftedMethodCandidates candidates,
        Dictionary<
            MethodDefinitionHandle,
            HashSet<MethodDefinitionHandle>> reachableOwners,
        Queue<(
            MethodDefinitionHandle Body,
            MethodDefinitionHandle Owner)> pending)
    {
        references.ThrowIfReferenceIncomplete();
        var referenced = new HashSet<MethodDefinitionHandle>();
        foreach (int token in references.ReferencedDefinitions)
        {
            if (candidates.ByDefinitionToken.TryGetValue(
                    token,
                    out MethodDefinitionHandle body))
            {
                referenced.Add(body);
            }
        }
        foreach (MethodReferenceKey member
            in references.ReferencedMembers)
        {
            if (!candidates.ByMember.TryGetValue(
                    member,
                    out ImmutableArray<MethodDefinitionHandle> bodies))
            {
                continue;
            }
            referenced.UnionWith(bodies);
        }

        foreach (MethodDefinitionHandle body in referenced)
        {
            if (!reachableOwners.TryGetValue(
                    body,
                    out HashSet<MethodDefinitionHandle>? bodyOwners))
            {
                bodyOwners = [];
                reachableOwners.Add(body, bodyOwners);
            }
            if (bodyOwners.Add(owner))
                pending.Enqueue((body, owner));
        }
    }

    IReadOnlyDictionary<
        LiftedOwnerGroupKey,
        ImmutableArray<MethodDefinitionHandle>>
        BuildLiftedMethodsByOwner()
    {
        var builders = new Dictionary<
            LiftedOwnerGroupKey,
            ImmutableArray<MethodDefinitionHandle>.Builder>();
        foreach (MethodDefinitionHandle methodHandle
            in _reader.MethodDefinitions)
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(methodHandle);
            if (!TryGetLiftedOwnerGroup(
                    method,
                    out LiftedOwnerGroupKey group))
            {
                continue;
            }
            if (!builders.TryGetValue(group, out var methods))
            {
                methods = ImmutableArray.CreateBuilder<
                    MethodDefinitionHandle>();
                builders.Add(group, methods);
            }
            methods.Add(methodHandle);
        }

        return builders.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutable());
    }

    bool TryGetLiftedOwnerGroup(
        MethodDefinition method,
        out LiftedOwnerGroupKey group)
    {
        group = default;
        string name = _reader.GetString(method.Name);
        int close = name.IndexOf(">g__", StringComparison.Ordinal);
        if (close < 0)
            close = name.IndexOf(">b__", StringComparison.Ordinal);
        if (name.Length < 4
            || name[0] != '<'
            || close <= 1
            || close + 4 >= name.Length)
        {
            return false;
        }

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                _reader,
                method.GetDeclaringType(),
                chain,
                out int count,
                out _,
                out _))
        {
            return false;
        }

        int ownerIndex = count - 1;
        while (ownerIndex > 0
            && _reader.GetString(
                    _reader.GetTypeDefinition(chain[ownerIndex]).Name)
                .StartsWith("<>", StringComparison.Ordinal))
        {
            ownerIndex--;
        }

        group = new(chain[ownerIndex], name[1..close]);
        return true;
    }

    IReadOnlyDictionary<string, ImmutableArray<MethodDefinitionHandle>>
        MethodsByName(TypeDefinitionHandle typeHandle)
        => _methodsByName.GetOrAdd(
            typeHandle,
            handle => new Lazy<
                IReadOnlyDictionary<
                    string,
                    ImmutableArray<MethodDefinitionHandle>>>(
                () => BuildMethodsByName(handle),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    IReadOnlyDictionary<string, ImmutableArray<MethodDefinitionHandle>>
        BuildMethodsByName(TypeDefinitionHandle typeHandle)
    {
        var builders = new Dictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>.Builder>(
                StringComparer.Ordinal);
        foreach (MethodDefinitionHandle methodHandle
            in _reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            string name = _reader.GetString(
                _reader.GetMethodDefinition(methodHandle).Name);
            if (!builders.TryGetValue(name, out var methods))
            {
                methods = ImmutableArray.CreateBuilder<
                    MethodDefinitionHandle>();
                builders.Add(name, methods);
            }
            methods.Add(methodHandle);
        }

        var result = new Dictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>>(
                builders.Count,
                StringComparer.Ordinal);
        foreach (var pair in builders)
            result.Add(pair.Key, pair.Value.ToImmutable());
        return result;
    }

    bool TryGetTopLevelExecutionMethod(
        MethodDefinitionHandle ownerHandle,
        out TopLevelExecutionMethod execution)
    {
        TopLevelExecutionMethod? result =
            _topLevelExecutionMethods.GetOrAdd(
                ownerHandle,
                handle => new Lazy<TopLevelExecutionMethod?>(
                    () => ResolveTopLevelExecutionMethod(handle),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        execution = result.GetValueOrDefault();
        return result.HasValue;
    }

    TopLevelExecutionMethod? ResolveTopLevelExecutionMethod(
        MethodDefinitionHandle ownerHandle)
    {
        MethodDefinition ownerMethod =
            _reader.GetMethodDefinition(ownerHandle);
        CorHeader? corHeader = _peReader.PEHeaders.CorHeader;
        if (corHeader is null
            || (corHeader.Flags & CorFlags.NativeEntryPoint) != 0
            || corHeader.EntryPointTokenOrRelativeVirtualAddress == 0)
        {
            return null;
        }

        EntityHandle entryPoint;
        try
        {
            entryPoint = MetadataTokens.EntityHandle(
                corHeader.EntryPointTokenOrRelativeVirtualAddress);
        }
        catch (ArgumentException)
        {
            return null;
        }
        if (entryPoint.Kind != HandleKind.MethodDefinition)
            return null;

        var entryPointHandle = (MethodDefinitionHandle)entryPoint;
        if (entryPointHandle == ownerHandle)
        {
            return new(
                ownerMethod.GetDeclaringType(),
                ownerHandle);
        }

        MethodDefinition entryPointMethod =
            _reader.GetMethodDefinition(entryPointHandle);
        if (!MethodBodyReferences(entryPointHandle).CallsDefinition(
                MetadataTokens.GetToken(ownerHandle)))
        {
            return null;
        }

        if ((ownerMethod.ImplAttributes & MethodImplAttributes.Async) != 0)
        {
            return new(
                ownerMethod.GetDeclaringType(),
                ownerHandle);
        }

        if (!TryGetAsyncStateMachineMoveNext(
                ownerMethod,
                out MethodDefinitionHandle moveNextHandle))
        {
            return null;
        }
        return new(
            _reader.GetMethodDefinition(moveNextHandle).GetDeclaringType(),
            moveNextHandle);
    }

    MethodBodyReferenceEvidence MethodBodyReferences(
        MethodDefinitionHandle methodHandle)
        => _methodBodyReferences.GetOrAdd(
            methodHandle,
            handle => new Lazy<MethodBodyReferenceEvidence>(
                () => BuildMethodBodyReferences(handle),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    MethodBodyReferenceEvidence BuildMethodBodyReferences(
        MethodDefinitionHandle methodHandle)
    {
        _methodBodyReferenceIndexed?.Invoke(methodHandle);
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        if (method.RelativeVirtualAddress == 0)
        {
            return new(
                new HashSet<int>(),
                new HashSet<int>(),
                new HashSet<MethodReferenceKey>(
                    MethodReferenceKeyComparer.Instance),
                null,
                null);
        }

        MethodBodyBlock body =
            _peReader.GetMethodBody(method.RelativeVirtualAddress);
        var calledDefinitions = new HashSet<int>();
        var referencedDefinitions = new HashSet<int>();
        var referencedMembers = new HashSet<MethodReferenceKey>(
            MethodReferenceKeyComparer.Instance);
        var validDefinitionOperands = new HashSet<int>();
        var invalidDefinitionOperands =
            new Dictionary<int, ExceptionDispatchInfo>();
        ExceptionDispatchInfo? callFailure = null;
        ExceptionDispatchInfo? referenceFailure = null;
        TypeDefinition ownerType = _reader.GetTypeDefinition(
            method.GetDeclaringType());
        GenericScope scope =
            _primaryMetadataResolver.CreateScope(ownerType, method);
        foreach (var instruction in LibraryMethodAnalysisRunner.DecodeBody(
            body.GetILBytes() ?? [],
            body.ExceptionRegions).Instructions)
        {
            bool call = instruction.OpCode
                is ILOpCode.Call or ILOpCode.Callvirt;
            if (!call
                && instruction.OpCode is not (
                    ILOpCode.Ldftn or ILOpCode.Ldvirtftn))
            {
                continue;
            }

            int operandToken =
                MethodInstructionFacts.OperandInt32(instruction);
            if (invalidDefinitionOperands.TryGetValue(
                    operandToken,
                    out ExceptionDispatchInfo? definitionFailure))
            {
                if (call)
                    callFailure ??= definitionFailure;
                continue;
            }
            try
            {
                if (validDefinitionOperands.Add(operandToken))
                {
                    EntityHandle operand =
                        MetadataTokens.EntityHandle(operandToken);
                    if (operand.Kind
                        == HandleKind.MethodSpecification)
                    {
                        _ = _methodReferenceResolver.ResolveMethod(
                            operand,
                            scope,
                            methodHandle);
                    }
                }
                int definitionToken =
                    PeelToDefinitionToken(operandToken);
                referencedDefinitions.Add(definitionToken);
                if (call)
                    calledDefinitions.Add(definitionToken);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                var failure = ExceptionDispatchInfo.Capture(ex);
                invalidDefinitionOperands.Add(operandToken, failure);
                referenceFailure ??= failure;
                if (call)
                    callFailure ??= failure;
                continue;
            }

            try
            {
                EntityHandle handle =
                    MetadataTokens.EntityHandle(operandToken);
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    handle = _reader.GetMethodSpecification(
                        (MethodSpecificationHandle)handle).Method;
                }
                if (handle.Kind != HandleKind.MemberReference)
                    continue;

                referencedMembers.Add(
                    _methodReferenceResolver.ResolveIdentity(
                        (MemberReferenceHandle)handle,
                        scope,
                        methodHandle));
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                referenceFailure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }
        return new(
            calledDefinitions,
            referencedDefinitions,
            referencedMembers,
            callFailure,
            referenceFailure);
    }

    bool TryGetAsyncStateMachineType(
        MethodDefinition ownerMethod,
        out TypeDefinitionHandle stateMachineHandle)
    {
        stateMachineHandle = default;
        string? stateMachineName = null;
        foreach (CustomAttributeHandle attributeHandle
            in ownerMethod.GetCustomAttributes())
        {
            CustomAttribute attribute =
                _reader.GetCustomAttribute(attributeHandle);
            if (AttributeDecoder.GetAttributeTypeName(
                    _reader,
                    attribute.Constructor)
                    is not string attributeName
                || attributeName
                    != KnownAttributeNames.AsyncStateMachineAttribute
                || !LibraryBodyAsyncSourceResolver
                    .IsTrustedAsyncStateMachineAttribute(
                        _reader,
                        attribute.Constructor,
                        attributeName)
                || AttributeDecoder.TryDecodePreservingSerializedTypeNames(
                        _reader,
                        attribute)
                    is not { FixedArguments.Length: 1 } decoded
                || decoded.FixedArguments[0].Value is not string typeName)
            {
                continue;
            }
            if (stateMachineName is not null)
                return false;
            stateMachineName = typeName;
        }
        if (stateMachineName is null)
            return false;

        TypeDefinitionHandle? resolved =
            _serializedAsyncStateMachineTypes.GetOrAdd(
                stateMachineName,
                name => new Lazy<TypeDefinitionHandle?>(
                    () => ResolveSerializedAsyncStateMachineType(name),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (resolved is not { } handle)
            return false;
        stateMachineHandle = handle;
        return true;
    }

    bool TryGetAsyncStateMachineMoveNext(
        MethodDefinition ownerMethod,
        out MethodDefinitionHandle moveNextHandle)
    {
        moveNextHandle = default;
        if (!TryGetAsyncStateMachineType(
                ownerMethod,
                out TypeDefinitionHandle stateMachineHandle))
        {
            return false;
        }

        TypeDefinition stateMachine =
            _reader.GetTypeDefinition(stateMachineHandle);
        foreach (MethodDefinitionHandle methodHandle
            in stateMachine.GetMethods())
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(methodHandle);
            if (!_reader.StringComparer.Equals(method.Name, "MoveNext"))
                continue;
            if (!moveNextHandle.IsNil)
            {
                moveNextHandle = default;
                return false;
            }
            moveNextHandle = methodHandle;
        }
        return !moveNextHandle.IsNil;
    }

    TypeDefinitionHandle? ResolveSerializedAsyncStateMachineType(
        string stateMachineName)
    {
        if (MetadataTypeDefinitionName.ParseSerialized(stateMachineName)
                is not MetadataTypeDefinitionNameResult.Valid valid
            || !_typeDefinitionIndex.Value.TryGetUniqueDefinition(
                    valid.Name,
                    out TypeDefinitionHandle handle))
        {
            return null;
        }

        return _primaryMetadataResolver
                .AsyncStateMachineTypeHandles()
                .Contains(handle)
            ? handle
            : null;
    }

    MetadataTypeDefinitionIndex BuildTypeDefinitionIndex()
    {
        _typeDefinitionIndexBuilt?.Invoke();
        return MetadataTypeDefinitionIndex.Create(_reader);
    }

    bool IsCompilerGeneratedSourceTypeOrEnclosing(
        TypeDefinitionHandle handle)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
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
            if (_primaryMetadataResolver.HasCompilerGeneratedAttribute(
                    _reader.GetTypeDefinition(
                        chain[i]).GetCustomAttributes()))
            {
                return true;
            }
        }
        return false;
    }

    int PeelToDefinitionToken(int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var spec = _reader.GetMethodSpecification(
                (MethodSpecificationHandle)handle);
            if (spec.Method.Kind == HandleKind.MethodDefinition)
                return MetadataTokens.GetToken(spec.Method);
        }
        return token;
    }

    readonly record struct LiftedOwnerGroupKey(
        TypeDefinitionHandle OwnerType,
        string OwnerName);

    readonly record struct TopLevelExecutionMethod(
        TypeDefinitionHandle Type,
        MethodDefinitionHandle Method);

    readonly record struct LiftedMethodCandidate(
        MethodDefinitionHandle Handle,
        int DefinitionToken,
        MethodReferenceKey Member);

    sealed class LiftedMethodCandidates
    {
        public LiftedMethodCandidates(
            MetadataReader reader,
            LibraryBodyMethodReferenceResolver methodReferenceResolver,
            ImmutableArray<MethodDefinitionHandle> handles)
        {
            var byHandle = new Dictionary<
                MethodDefinitionHandle,
                LiftedMethodCandidate>();
            var byDefinitionToken =
                new Dictionary<int, MethodDefinitionHandle>();
            var memberBuilders = new Dictionary<
                MethodReferenceKey,
                ImmutableArray<MethodDefinitionHandle>.Builder>(
                    MethodReferenceKeyComparer.Instance);
            foreach (MethodDefinitionHandle handle in handles)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                int token = MetadataTokens.GetToken(handle);
                TypeRef declaringType =
                    TypeRefDecoder.Instance.GetTypeFromDefinition(
                        reader,
                        method.GetDeclaringType(),
                        0);
                MethodReferenceKey member =
                    methodReferenceResolver.CreateIdentity(
                        reader.GetString(method.Name),
                        declaringType,
                        method.Signature);
                var candidate = new LiftedMethodCandidate(
                    handle,
                    token,
                    member);
                byHandle.Add(handle, candidate);
                byDefinitionToken.Add(token, handle);
                if (!memberBuilders.TryGetValue(member, out var members))
                {
                    members = ImmutableArray.CreateBuilder<
                        MethodDefinitionHandle>();
                    memberBuilders.Add(member, members);
                }
                members.Add(handle);
            }

            ByHandle = byHandle;
            ByDefinitionToken = byDefinitionToken;
            ByMember = memberBuilders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutable(),
                MethodReferenceKeyComparer.Instance);
        }

        public IReadOnlyDictionary<
            MethodDefinitionHandle,
            LiftedMethodCandidate> ByHandle { get; }

        public IReadOnlyDictionary<
            int,
            MethodDefinitionHandle> ByDefinitionToken { get; }

        public IReadOnlyDictionary<
            MethodReferenceKey,
            ImmutableArray<MethodDefinitionHandle>> ByMember { get; }
    }

    sealed record MethodBodyReferenceEvidence(
        IReadOnlySet<int> CalledDefinitions,
        IReadOnlySet<int> ReferencedDefinitions,
        IReadOnlySet<MethodReferenceKey> ReferencedMembers,
        ExceptionDispatchInfo? CallFailure,
        ExceptionDispatchInfo? ReferenceFailure)
    {
        public bool CallsDefinition(int token)
        {
            if (CalledDefinitions.Contains(token))
                return true;
            CallFailure?.Throw();
            return false;
        }

        public void ThrowIfReferenceIncomplete() =>
            ReferenceFailure?.Throw();
    }

    readonly record struct LiftedOwnerReference(
        MethodDefinitionHandle Owner,
        bool Ambiguous)
    {
        public LiftedOwnerReference Add(MethodDefinitionHandle owner)
            => Owner == owner
                ? this
                : new(Owner, Ambiguous: true);
    }

    sealed class LiftedOwnerGroupEvidence
    {
        readonly Dictionary<int, LiftedOwnerReference>
            _definitionOwners = [];
        readonly Dictionary<MethodReferenceKey, LiftedOwnerReference>
            _memberOwners = new(MethodReferenceKeyComparer.Instance);
        readonly Dictionary<MethodDefinitionHandle, bool>
            _topLevelOwners = [];

        public void Add(
            LiftedMethodCandidate candidate,
            MethodDefinitionHandle owner,
            bool topLevel)
        {
            _topLevelOwners[owner] = topLevel;
            Add(_definitionOwners, candidate.DefinitionToken, owner);
            Add(_memberOwners, candidate.Member, owner);
        }

        public bool TryResolve(
            int definitionToken,
            MethodReferenceKey member,
            out MethodDefinitionHandle owner,
            out bool topLevel)
        {
            owner = default;
            topLevel = false;
            bool found = false;
            if (_definitionOwners.TryGetValue(
                    definitionToken,
                    out LiftedOwnerReference definition))
            {
                if (definition.Ambiguous)
                    return false;
                owner = definition.Owner;
                found = true;
            }
            if (_memberOwners.TryGetValue(
                    member,
                    out LiftedOwnerReference memberReference))
            {
                if (memberReference.Ambiguous
                    || found && owner != memberReference.Owner)
                {
                    return false;
                }
                owner = memberReference.Owner;
                found = true;
            }
            return found
                && _topLevelOwners.TryGetValue(owner, out topLevel);
        }

        static void Add<TKey>(
            Dictionary<TKey, LiftedOwnerReference> owners,
            TKey key,
            MethodDefinitionHandle owner)
            where TKey : notnull
        {
            if (owners.TryGetValue(key, out LiftedOwnerReference existing))
                owners[key] = existing.Add(owner);
            else
                owners.Add(key, new(owner, Ambiguous: false));
        }
    }
}
