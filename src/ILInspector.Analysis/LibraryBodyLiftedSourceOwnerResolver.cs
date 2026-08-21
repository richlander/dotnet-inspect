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
    readonly Func<EntityHandle, TypeRef> _typeFromEntity;
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
        TypeDefinitionHandle,
        IReadOnlyDictionary<
            MethodReferenceKey,
            LiftedDefinitionReference>>>
        _liftedDefinitionsByOwnerType;
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
        Func<EntityHandle, TypeRef> typeFromEntity,
        Action<MethodDefinitionHandle>? methodBodyReferenceIndexed = null,
        Action? typeDefinitionIndexBuilt = null)
    {
        _reader = reader;
        _peReader = peReader;
        _primaryMetadataResolver = primaryMetadataResolver;
        _methodReferenceResolver = methodReferenceResolver;
        _typeFromEntity = typeFromEntity;
        _methodBodyReferenceIndexed = methodBodyReferenceIndexed;
        _typeDefinitionIndexBuilt = typeDefinitionIndexBuilt;
        _typeDefinitionIndex = new(
            BuildTypeDefinitionIndex,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _liftedDefinitionsByOwnerType = new(
            BuildLiftedDefinitionsByOwnerType,
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
        string liftedName = _reader.GetString(liftedMethod.Name);
        int close = Math.Max(
            liftedName.LastIndexOf(
                ">g__",
                StringComparison.Ordinal),
            liftedName.LastIndexOf(
                ">b__",
                StringComparison.Ordinal));
        if (liftedName.Length < 4
            || liftedName[0] != '<'
            || close <= 1
            || close + 4 >= liftedName.Length)
        {
            return false;
        }

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                _reader,
                liftedMethod.GetDeclaringType(),
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

        TypeDefinitionHandle ownerType = chain[ownerIndex];
        TypeDefinition ownerDefinition = _reader.GetTypeDefinition(ownerType);
        string ownerName = liftedName[1..close];
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
            && !directlySelectedBody
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
            if (topLevel)
                executionHandle = execution.Method;
            else if (TryGetStateMachineExecutionMethod(
                    _reader.GetMethodDefinition(ownerHandle),
                    out MethodDefinitionHandle stateMachineExecution))
            {
                executionHandle = stateMachineExecution;
            }
            evidence.AddOwner(
                ownerHandle,
                topLevel,
                OwnerFamilyReferences(
                    group.OwnerType,
                    executionHandle));
        }
        return evidence;
    }

    MethodBodyReferenceEvidence OwnerFamilyReferences(
        TypeDefinitionHandle ownerType,
        MethodDefinitionHandle executionHandle)
    {
        var calledDefinitions = new HashSet<int>();
        var referencedDefinitions = new HashSet<int>();
        var referencedMembers = new HashSet<MethodReferenceKey>(
            MethodReferenceKeyComparer.Instance);
        var pending = new Queue<MethodDefinitionHandle>();
        var scheduled = new HashSet<MethodDefinitionHandle>();
        var authenticatedStateMachineTypes =
            new HashSet<TypeDefinitionHandle>();

        void Enqueue(MethodDefinitionHandle method)
        {
            if (!scheduled.Add(method))
                return;
            if (scheduled.Count
                > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw new BadImageFormatException(
                    "The lifted owner family exceeds the relationship limit.");
            }
            pending.Enqueue(method);
        }

        void EnqueueLifted(MethodDefinitionHandle lifted)
        {
            Enqueue(lifted);
            if (TryGetStateMachineExecutionMethod(
                    _reader.GetMethodDefinition(lifted),
                    out MethodDefinitionHandle stateMachineExecution))
            {
                authenticatedStateMachineTypes.Add(
                    _reader.GetMethodDefinition(
                        stateMachineExecution).GetDeclaringType());
                Enqueue(stateMachineExecution);
            }
        }

        Enqueue(executionHandle);
        if (IsAuthenticatedStateMachineExecution(
                executionHandle,
                out TypeDefinitionHandle executionType))
        {
            authenticatedStateMachineTypes.Add(executionType);
        }
        while (pending.Count > 0)
        {
            MethodDefinitionHandle current = pending.Dequeue();
            MethodDefinition currentMethod =
                _reader.GetMethodDefinition(current);
            TypeDefinitionHandle currentType =
                currentMethod.GetDeclaringType();
            MethodBodyReferenceEvidence references =
                MethodBodyReferences(current);
            references.ThrowIfReferenceIncomplete();
            calledDefinitions.UnionWith(
                references.CalledDefinitions);
            referencedDefinitions.UnionWith(
                references.ReferencedDefinitions);
            referencedMembers.UnionWith(
                references.ReferencedMembers);
            foreach (int token
                in references.ReferencedDefinitions)
            {
                EntityHandle handle =
                    MetadataTokens.EntityHandle(token);
                if (handle.Kind
                        != HandleKind.MethodDefinition)
                {
                    continue;
                }

                var referenced =
                    (MethodDefinitionHandle)handle;
                MethodDefinition referencedMethod =
                    _reader.GetMethodDefinition(referenced);
                if (IsLiftedWithinOwnerType(
                        referenced,
                        ownerType))
                {
                    EnqueueLifted(referenced);
                    continue;
                }
                if (authenticatedStateMachineTypes.Contains(
                        currentType)
                    && referencedMethod.GetDeclaringType()
                        == currentType)
                {
                    Enqueue(referenced);
                }
            }
            IReadOnlyDictionary<
                MethodReferenceKey,
                LiftedDefinitionReference> liftedDefinitions =
                    LiftedDefinitionsByOwnerType(ownerType);
            foreach (MethodReferenceKey member
                in references.ReferencedMembers)
            {
                if (!liftedDefinitions.TryGetValue(
                        member,
                        out LiftedDefinitionReference lifted)
                    || lifted.Ambiguous)
                {
                    continue;
                }

                EnqueueLifted(lifted.Method);
            }
        }

        return new(
            calledDefinitions,
            referencedDefinitions,
            referencedMembers,
            null,
            null);
    }

    bool IsAuthenticatedStateMachineExecution(
        MethodDefinitionHandle method,
        out TypeDefinitionHandle stateMachineType)
    {
        stateMachineType =
            _reader.GetMethodDefinition(
                method).GetDeclaringType();
        TypeDefinition type =
            _reader.GetTypeDefinition(stateMachineType);
        return ImplementsAsyncStateMachine(type)
                && IsStateMachineMoveNext(
                    method,
                    iterator: false)
            || ImplementsIteratorStateMachine(type)
                && IsStateMachineMoveNext(
                    method,
                    iterator: true);
    }

    IReadOnlyDictionary<
        MethodReferenceKey,
        LiftedDefinitionReference> LiftedDefinitionsByOwnerType(
            TypeDefinitionHandle ownerType)
        => _liftedDefinitionsByOwnerType.Value.TryGetValue(
                ownerType,
                out IReadOnlyDictionary<
                    MethodReferenceKey,
                    LiftedDefinitionReference>? definitions)
            ? definitions
            : new Dictionary<
                MethodReferenceKey,
                LiftedDefinitionReference>(
                    MethodReferenceKeyComparer.Instance);

    IReadOnlyDictionary<
        TypeDefinitionHandle,
        IReadOnlyDictionary<
            MethodReferenceKey,
            LiftedDefinitionReference>>
        BuildLiftedDefinitionsByOwnerType()
    {
        var definitions = new Dictionary<
            TypeDefinitionHandle,
            Dictionary<
                MethodReferenceKey,
                LiftedDefinitionReference>>();
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        foreach (TypeDefinitionHandle typeHandle
            in _reader.TypeDefinitions)
        {
            foreach (MethodDefinitionHandle methodHandle
                in _reader.GetTypeDefinition(typeHandle).GetMethods())
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(methodHandle);
                if (!CompilerGeneratedNames.IsLocalFunctionOrLambda(
                        _reader.GetString(method.Name)))
                {
                    continue;
                }

                MethodReferenceKey key =
                    _methodReferenceResolver.CreateIdentity(
                        _reader.GetString(method.Name),
                        _typeFromEntity(typeHandle),
                        method.Signature);
                if (!MetadataRelationshipTraversal
                    .TryWalkTypeDefinitionDeclaringChain(
                        _reader,
                        method.GetDeclaringType(),
                        chain,
                        out int count,
                        out _,
                        out _))
                {
                    continue;
                }
                for (int i = 0; i < count; i++)
                {
                    if (!definitions.TryGetValue(
                            chain[i],
                            out Dictionary<
                                MethodReferenceKey,
                                LiftedDefinitionReference>? byReference))
                    {
                        byReference = new(
                            MethodReferenceKeyComparer.Instance);
                        definitions.Add(chain[i], byReference);
                    }
                    if (byReference.TryGetValue(
                            key,
                            out LiftedDefinitionReference existing)
                        && existing.Method != methodHandle)
                    {
                        byReference[key] =
                            existing with { Ambiguous = true };
                        continue;
                    }
                    byReference.TryAdd(
                        key,
                        new(methodHandle, Ambiguous: false));
                }
            }
        }
        return definitions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<
                MethodReferenceKey,
                LiftedDefinitionReference>)pair.Value);
    }

    bool IsLiftedWithinOwnerType(
        MethodDefinitionHandle methodHandle,
        TypeDefinitionHandle ownerType)
    {
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        if (!CompilerGeneratedNames.IsLocalFunctionOrLambda(
                _reader.GetString(method.Name)))
        {
            return false;
        }

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        return MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    _reader,
                    method.GetDeclaringType(),
                    chain,
                    out int count,
                    out _,
                    out _)
            && chain[..count].Contains(ownerType);
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

        if (!TryGetStateMachineExecutionMethod(
                ownerMethod,
                out MethodDefinitionHandle moveNextHandle))
        {
            return null;
        }

        return new(
            _reader.GetMethodDefinition(
                moveNextHandle).GetDeclaringType(),
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

    bool TryGetStateMachineExecutionMethod(
        MethodDefinition ownerMethod,
        out MethodDefinitionHandle executionMethod)
    {
        executionMethod = default;
        if (!TryGetStateMachineType(
                ownerMethod,
                out TypeDefinitionHandle stateMachineHandle,
                out bool iterator))
        {
            return false;
        }

        foreach (MethodDefinitionHandle methodHandle
            in _reader.GetTypeDefinition(
                stateMachineHandle).GetMethods())
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(methodHandle);
            if (!_reader.StringComparer.Equals(
                    method.Name,
                    "MoveNext")
                || !IsStateMachineMoveNext(
                    methodHandle,
                    iterator))
            {
                continue;
            }
            if (!executionMethod.IsNil)
                return false;
            executionMethod = methodHandle;
        }
        return !executionMethod.IsNil;
    }

    bool TryGetStateMachineType(
        MethodDefinition ownerMethod,
        out TypeDefinitionHandle stateMachineHandle,
        out bool iterator)
    {
        stateMachineHandle = default;
        iterator = false;
        string? stateMachineName = null;
        string? stateMachineAttribute = null;
        foreach (CustomAttributeHandle attributeHandle
            in ownerMethod.GetCustomAttributes())
        {
            CustomAttribute attribute =
                _reader.GetCustomAttribute(attributeHandle);
            string? attributeName =
                AttributeDecoder.GetAttributeTypeName(
                    _reader,
                    attribute.Constructor);
            if (attributeName is not (
                    KnownAttributeNames.AsyncStateMachineAttribute
                    or KnownAttributeNames.AsyncIteratorStateMachineAttribute
                    or KnownAttributeNames.IteratorStateMachineAttribute)
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
            stateMachineAttribute = attributeName;
        }
        if (stateMachineName is null
            || stateMachineAttribute is null)
            return false;

        TypeDefinitionHandle? resolved =
            _serializedAsyncStateMachineTypes.GetOrAdd(
                stateMachineName,
                name => new Lazy<TypeDefinitionHandle?>(
                    () => ResolveSerializedAsyncStateMachineType(name),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (resolved is not { } handle)
            return false;
        iterator = stateMachineAttribute
            == KnownAttributeNames.IteratorStateMachineAttribute;
        if (iterator
            ? !ImplementsIteratorStateMachine(
                _reader.GetTypeDefinition(handle))
            : !ImplementsAsyncStateMachine(
                _reader.GetTypeDefinition(handle)))
        {
            return false;
        }
        stateMachineHandle = handle;
        return true;
    }

    bool ImplementsAsyncStateMachine(
        TypeDefinition type)
    {
        foreach (InterfaceImplementationHandle handle
            in type.GetInterfaceImplementations())
        {
            TypeRef interfaceType = _typeFromEntity(
                _reader.GetInterfaceImplementation(
                    handle).Interface);
            TypeRef definition =
                interfaceType.Kind == TypeRefKind.GenericInstance
                    ? interfaceType.ElementType ?? interfaceType
                    : interfaceType;
            if (FrameworkIdentity.IsCoreLibraryType(
                    definition,
                    "System.Runtime.CompilerServices",
                    "IAsyncStateMachine"))
            {
                return true;
            }
        }
        return false;
    }

    bool ImplementsIteratorStateMachine(
        TypeDefinition type)
    {
        foreach (InterfaceImplementationHandle handle
            in type.GetInterfaceImplementations())
        {
            TypeRef interfaceType = _typeFromEntity(
                _reader.GetInterfaceImplementation(
                    handle).Interface);
            TypeRef definition =
                interfaceType.Kind == TypeRefKind.GenericInstance
                    ? interfaceType.ElementType ?? interfaceType
                    : interfaceType;
            if (FrameworkIdentity.IsCoreLibraryType(
                    definition,
                    "System.Collections",
                    "IEnumerator"))
            {
                return true;
            }
        }
        return false;
    }

    bool IsStateMachineMoveNext(
        MethodDefinitionHandle handle,
        bool iterator)
    {
        MethodDefinition method =
            _reader.GetMethodDefinition(handle);
        if (method.RelativeVirtualAddress == 0
            || (method.Attributes
                    & MethodAttributes.PinvokeImpl) != 0
            || (method.ImplAttributes
                    & (MethodImplAttributes.CodeTypeMask
                        | MethodImplAttributes.ManagedMask
                        | MethodImplAttributes.InternalCall))
                != MethodImplAttributes.IL)
        {
            return false;
        }

        MemberRef member = MemberResolver.ResolveMethod(
            _reader,
            handle,
            GenericScope.Empty);
        return member.HasThis
            && member.GenericArity == 0
            && member.ParameterTypes.Length == 0
            && member.SignatureHeader == 0x20
            && member.RequiredParameterCount == 0
            && FrameworkIdentity.IsCoreLibraryType(
                member.ReturnType,
                "System",
                iterator ? "Boolean" : "Void");
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

        return handle;
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

    readonly record struct LiftedDefinitionReference(
        MethodDefinitionHandle Method,
        bool Ambiguous);

    sealed class LiftedOwnerGroupEvidence
    {
        readonly Dictionary<int, LiftedOwnerReference>
            _definitionOwners = [];
        readonly Dictionary<MethodReferenceKey, LiftedOwnerReference>
            _memberOwners = new(MethodReferenceKeyComparer.Instance);
        readonly Dictionary<MethodDefinitionHandle, bool>
            _topLevelOwners = [];

        public void AddOwner(
            MethodDefinitionHandle owner,
            bool topLevel,
            MethodBodyReferenceEvidence references)
        {
            references.ThrowIfReferenceIncomplete();
            _topLevelOwners[owner] = topLevel;
            foreach (int token in references.ReferencedDefinitions)
                Add(_definitionOwners, token, owner);
            foreach (MethodReferenceKey member in references.ReferencedMembers)
                Add(_memberOwners, member, owner);
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
