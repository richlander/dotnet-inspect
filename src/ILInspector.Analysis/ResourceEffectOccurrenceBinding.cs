using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal abstract class ResourceEffectOccurrenceBindingResult
{
    private protected ResourceEffectOccurrenceBindingResult()
    {
    }

    internal sealed class Resolved(
        ResolvedResourceEffectBinding binding)
        : ResourceEffectOccurrenceBindingResult
    {
        internal ResolvedResourceEffectBinding Binding { get; } = binding;
    }

    internal sealed class Ambiguous(
        ResourceEffectOccurrenceBindingGap gap)
        : ResourceEffectOccurrenceBindingResult
    {
        internal ResourceEffectOccurrenceBindingGap Gap { get; } = gap;
    }

    internal sealed class Unsupported(
        ResourceEffectOccurrenceBindingGap gap)
        : ResourceEffectOccurrenceBindingResult
    {
        internal ResourceEffectOccurrenceBindingGap Gap { get; } = gap;
    }

    internal sealed class Incomplete(
        ResourceEffectOccurrenceBindingGap gap)
        : ResourceEffectOccurrenceBindingResult
    {
        internal ResourceEffectOccurrenceBindingGap Gap { get; } = gap;
    }
}

internal static class ResourceEffectOccurrenceBinder
{
    internal static ResourceEffectOccurrenceBindingResult Bind(
        ResourceEffect effect,
        ResourceEffectSelectorBinding.Resolved selector)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(selector);
        return new Binder(effect, selector).Bind();
    }

    sealed class Binder(
        ResourceEffect effect,
        ResourceEffectSelectorBinding.Resolved selector)
    {
        readonly Dictionary<
            ResourceEffectLocation,
            ResolvedResourceEffectLocation> _locations = [];
        readonly Dictionary<
            int,
            ResolvedResourceEffectCallbackContract> _callbacks = [];
        ResourceEffectOccurrenceBindingResult? _failure;

        internal ResourceEffectOccurrenceBindingResult Bind()
        {
            foreach (ResourceEffectLocation location in Locations(effect))
            {
                if (ResolveLocation(location) is null)
                    return _failure!;
            }

            ResolvedResourceEffectCallback? callback = null;
            if (effect is ResourceEffect.Callback callbackEffect)
            {
                ResolvedResourceEffectCallbackContract? contract =
                    ResolveCallback(callbackEffect.Scope.Index);
                if (contract is null)
                    return _failure!;
                callback = new ResolvedResourceEffectCallback(
                    contract,
                    callbackEffect.Execution,
                    callbackEffect.Cardinality);
            }
            else if (effect is ResourceEffect.Borrow
                {
                    Scope: ResourceBorrowScope.Callback callbackScope,
                })
            {
                if (ResolveCallback(callbackScope.Index) is null)
                    return _failure!;
            }

            ResolvedResourceEffectOutcome? outcome = null;
            if (effect is ResourceEffect.Outcome outcomeEffect)
            {
                outcome = ResolveOutcome(
                    outcomeEffect.Source,
                    outcomeEffect.Test);
                if (outcome is null)
                    return _failure!;
            }

            ResolvedResourceEffectCompletion? completion =
                ResolveCompletion(Completion(effect));
            if (_failure is not null)
                return _failure;

            ResolvedResourceEffectGuard? guard =
                ResolveGuard(Guard(effect));
            if (_failure is not null)
                return _failure;

            return new ResourceEffectOccurrenceBindingResult.Resolved(
                new ResolvedResourceEffectBinding(
                    [.. _locations.Values.Distinct()],
                    [.. _callbacks.Values],
                    _locations,
                    callback,
                    outcome,
                    completion,
                    guard));
        }

        ResolvedResourceEffectLocation? ResolveLocation(
            ResourceEffectLocation location)
        {
            if (_locations.TryGetValue(
                    location,
                    out ResolvedResourceEffectLocation? existing))
            {
                return existing;
            }

            ResolvedResourceEffectLocation? resolved = location switch
            {
                ResourceEffectLocation.Receiver =>
                    Boundary(
                        ResolvedResourceEffectBoundaryLocationKind.Receiver,
                        null,
                        selector.DirectCall.Call.Callee.DeclaringType),
                ResourceEffectLocation.Return =>
                    Boundary(
                        ResolvedResourceEffectBoundaryLocationKind.Return,
                        null,
                        selector.DirectCall.Call.Callee.ReturnType),
                ResourceEffectLocation.Constructed =>
                    Boundary(
                        ResolvedResourceEffectBoundaryLocationKind.Constructed,
                        null,
                        selector.DirectCall.Call.Callee.DeclaringType),
                ResourceEffectLocation.Parameter parameter =>
                    Parameter(parameter),
                ResourceEffectLocation.StructuralField field =>
                    StructuralField(field),
                ResourceEffectLocation.CallbackParameter callback =>
                    CallbackParameter(callback),
                ResourceEffectLocation.CallbackReturn callback =>
                    CallbackReturn(callback),
                ResourceEffectLocation.OperationSlot operation =>
                    OperationSlot(operation),
                ResourceEffectLocation.Field or
                    ResourceEffectLocation.Operation =>
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind
                            .BoundaryLocation,
                        location),
                _ => FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind
                        .BoundaryLocation,
                    location),
            };
            if (resolved is not null)
                _locations.Add(location, resolved);
            return resolved;
        }

        ResolvedResourceEffectLocation? Parameter(
            ResourceEffectLocation.Parameter parameter)
        {
            if (parameter.Index
                >= selector.DirectCall.Call.Callee.ParameterTypes.Length)
            {
                return FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind
                        .BoundaryLocation,
                    parameter);
            }
            return Boundary(
                ResolvedResourceEffectBoundaryLocationKind.Parameter,
                parameter.Index,
                selector.DirectCall.Call.Callee
                    .ParameterTypes[parameter.Index]);
        }

        ResolvedResourceEffectLocation? Boundary(
            ResolvedResourceEffectBoundaryLocationKind kind,
            int? parameterIndex,
            TypeRef type)
        {
            ResourceEffectSelectorBinder.MatchResult result =
                ResourceEffectSelectorBinder.TryResolveType(
                    type,
                    selector.DirectCall,
                    out ResolvedResourceEffectType resolved);
            if (result.Kind
                != ResourceEffectSelectorBinder.MatchKind.Match)
            {
                return Fail(
                    result.Kind,
                    ResourceEffectOccurrenceBindingGapKind.TypeDefinition);
            }

            return new ResolvedResourceEffectLocation.Boundary(
                kind,
                parameterIndex,
                resolved,
                $"boundary:{(int)kind}:"
                    + (parameterIndex?.ToString(
                        CultureInfo.InvariantCulture) ?? ""));
        }

        ResolvedResourceEffectLocation? StructuralField(
            ResourceEffectLocation.StructuralField field)
        {
            ResolvedResourceEffectLocation? root =
                ResolveLocation(field.Root);
            if (root is null)
                return null;
            TypeRef rootType = root.Type.Type;
            if (!ResourceEffectSelectorBinder.TryGetDefinition(
                    rootType,
                    selector.DirectCall,
                    out ResolvedTypeDefinition rootDefinition))
            {
                return FailIncomplete(
                    ResourceEffectOccurrenceBindingGapKind.TypeDefinition,
                    field);
            }

            try
            {
                using Stream stream =
                    rootDefinition.Assembly.Assembly.OpenRead();
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                {
                    return FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.TypeDefinition,
                        field);
                }
                MetadataReader reader = peReader.GetMetadataReader();
                if (!rootDefinition.Address.TryResolve(
                        reader,
                        out TypeDefinitionHandle typeHandle))
                {
                    return FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.TypeDefinition,
                        field);
                }

                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                int declaringTypeArity =
                    type.GetGenericParameters().Count;
                if (type.GetFields().Count
                    > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
                {
                    return FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.StructuralField,
                        field);
                }

                var matches =
                    new List<(
                        FieldDefinitionHandle Handle,
                        TypeRef Type,
                        bool IsStatic)>();
                bool unreadableCandidate = false;
                foreach (FieldDefinitionHandle handle in type.GetFields())
                {
                    FieldDefinition candidate =
                        reader.GetFieldDefinition(handle);
                    if (!string.Equals(
                            reader.GetString(candidate.Name),
                            field.Selector.MetadataName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    bool isStatic =
                        (candidate.Attributes & FieldAttributes.Static) != 0;
                    if (isStatic != field.Selector.IsStatic)
                        continue;
                    if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                            reader,
                            candidate.Signature,
                            SignatureBlobGuard.Kind.Field))
                    {
                        unreadableCandidate = true;
                        continue;
                    }
                    TypeRef candidateType = candidate.DecodeSignature(
                        TypeRefDecoder.Instance,
                        TypeScope(reader, type));
                    if (!GenericParametersAreInRange(
                            candidateType,
                            declaringTypeArity,
                            methodArity: 0))
                    {
                        unreadableCandidate = true;
                        continue;
                    }
                    candidateType = candidateType.Instantiate(
                        rootType.Kind == TypeRefKind.GenericInstance
                            ? rootType.TypeArguments
                            : [],
                        []);

                    ResourceEffectSelectorBinder.MatchResult declaring =
                        ResourceEffectSelectorBinder.MatchOccurrenceType(
                            field.Selector.DeclaringType,
                            rootType,
                            selector,
                            rootDefinition.Assembly.Assembly.Identity);
                    ResourceEffectSelectorBinder.MatchResult signature =
                        ResourceEffectSelectorBinder.MatchOccurrenceType(
                            field.Selector.ReturnType,
                            candidateType,
                            selector,
                            rootDefinition.Assembly.Assembly.Identity);
                    if (declaring.Kind
                            == ResourceEffectSelectorBinder.MatchKind.Match
                        && signature.Kind
                            == ResourceEffectSelectorBinder.MatchKind.Match)
                    {
                        matches.Add((handle, candidateType, isStatic));
                    }
                    else if (declaring.Kind
                            is not ResourceEffectSelectorBinder.MatchKind
                                .NoMatch
                        || signature.Kind
                            is not ResourceEffectSelectorBinder.MatchKind
                                .NoMatch)
                    {
                        ResourceEffectSelectorBinder.MatchKind failure =
                            declaring.Kind
                                == ResourceEffectSelectorBinder.MatchKind.Match
                                ? signature.Kind
                                : declaring.Kind;
                        if (failure
                            != ResourceEffectSelectorBinder.MatchKind.NoMatch)
                        {
                            return Fail(
                                failure,
                                ResourceEffectOccurrenceBindingGapKind
                                    .StructuralField,
                                field);
                        }
                    }
                }

                if (unreadableCandidate)
                {
                    return FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind.StructuralField,
                        field);
                }
                if (matches.Count > 1)
                {
                    return FailAmbiguous(
                        ResourceEffectOccurrenceBindingGapKind.StructuralField,
                        field);
                }
                if (matches.Count == 0)
                {
                    return FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind
                            .StructuralField,
                        field);
                }

                var match = matches[0];
                ResolvedResourceEffectType? fieldType =
                    ResolveSignatureType(
                        match.Type,
                        rootDefinition.Assembly.Assembly,
                        ResourceEffectOccurrenceBindingGapKind
                            .StructuralField,
                        field);
                if (fieldType is null)
                    return null;
                ResolvedResourceEffectTypeDefinition declaringType =
                    TypeDefinition(rootDefinition);
                var definition = new ResolvedResourceEffectField(
                    declaringType,
                    MetadataTokens.GetToken(match.Handle),
                    field.Selector.MetadataName,
                    match.IsStatic,
                    fieldType);
                return new ResolvedResourceEffectLocation.Field(
                    root,
                    definition,
                    $"{root.CanonicalKey}:field:"
                        + TypeDefinitionKey(declaringType)
                        + $":{definition.MetadataToken:X8}");
            }
            catch (Exception ex) when (MetadataUnavailable(ex))
            {
                return FailIncomplete(
                    ResourceEffectOccurrenceBindingGapKind.StructuralField,
                    field);
            }
            catch (Exception ex) when (MetadataUnsupported(ex))
            {
                return FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.StructuralField,
                    field);
            }
        }

        ResolvedResourceEffectLocation? CallbackParameter(
            ResourceEffectLocation.CallbackParameter callback)
        {
            ResolvedResourceEffectCallbackContract? contract =
                ResolveCallback(callback.CallbackIndex);
            if (contract is null)
                return null;
            if (callback.ParameterIndex >= contract.ParameterTypes.Length)
            {
                return FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.CallbackLocation,
                    callback);
            }
            ResolvedResourceEffectType type =
                contract.ParameterTypes[callback.ParameterIndex];
            return new ResolvedResourceEffectLocation.CallbackParameter(
                contract,
                callback.ParameterIndex,
                type,
                CallbackKey(contract)
                    + ":parameter:"
                    + callback.ParameterIndex.ToString(
                        CultureInfo.InvariantCulture));
        }

        ResolvedResourceEffectLocation? CallbackReturn(
            ResourceEffectLocation.CallbackReturn callback)
        {
            ResolvedResourceEffectCallbackContract? contract =
                ResolveCallback(callback.CallbackIndex);
            if (contract is null)
                return null;
            return new ResolvedResourceEffectLocation.CallbackReturn(
                contract,
                contract.ReturnType,
                CallbackKey(contract) + ":return");
        }

        ResolvedResourceEffectLocation? OperationSlot(
            ResourceEffectLocation.OperationSlot operation)
        {
            ResolvedResourceEffectLocation? source =
                ResolveLocation(operation.Source);
            if (source is null)
                return null;
            ResolvedResourceKindReference? kind =
                operation.Kind is null
                    ? null
                    : ResolveKind(operation.Kind);
            if (operation.Kind is not null && kind is null)
            {
                return FailIncomplete(
                    ResourceEffectOccurrenceBindingGapKind.BoundaryLocation,
                    operation);
            }
            return new ResolvedResourceEffectLocation.OperationSlot(
                source,
                kind,
                source.CanonicalKey
                    + ":operation:"
                    + (kind is null ? "all" : KindKey(kind)));
        }

        ResolvedResourceEffectCallbackContract? ResolveCallback(int index)
        {
            if (_callbacks.TryGetValue(
                    index,
                    out ResolvedResourceEffectCallbackContract? existing))
            {
                return existing;
            }
            if (index
                    >= selector.DirectCall.Call.Callee.ParameterTypes.Length
                || selector.DirectCall.Call.Callee.ParameterTypes[index].Kind
                    == TypeRefKind.ByRef)
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.CallbackContract);
                return null;
            }

            TypeRef delegateType =
                selector.DirectCall.Call.Callee.ParameterTypes[index];
            if (delegateType.Kind
                is not (
                    TypeRefKind.Definition
                    or TypeRefKind.GenericInstance
                    or TypeRefKind.GenericParameter
                    or TypeRefKind.MethodGenericParameter))
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.CallbackContract);
                return null;
            }
            if (!ResourceEffectSelectorBinder.TryGetDefinition(
                    delegateType,
                    selector.DirectCall,
                    out ResolvedTypeDefinition definition))
            {
                FailIncomplete(
                    ResourceEffectOccurrenceBindingGapKind.CallbackContract);
                return null;
            }

            try
            {
                using Stream stream =
                    definition.Assembly.Assembly.OpenRead();
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                MetadataReader reader = peReader.GetMetadataReader();
                if (!definition.Address.TryResolve(
                        reader,
                        out TypeDefinitionHandle typeHandle))
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                TypeRef? baseType = DecodeTypeHandle(
                    reader,
                    type.BaseType,
                    TypeScope(reader, type));
                if (baseType is null
                    || !FrameworkIdentity.IsCoreLibraryType(
                        baseType,
                        "System",
                        "MulticastDelegate"))
                {
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }

                MethodDefinitionHandle[] invoke =
                [
                    .. type.GetMethods().Where(handle =>
                        reader.GetString(
                            reader.GetMethodDefinition(handle).Name)
                        == "Invoke"),
                ];
                if (invoke.Length > 1)
                {
                    FailAmbiguous(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                if (invoke.Length == 0)
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }

                MethodDefinition method =
                    reader.GetMethodDefinition(invoke[0]);
                if (method.GetGenericParameters().Count != 0)
                {
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                        reader,
                        method.Signature,
                        SignatureBlobGuard.Kind.Method))
                {
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                MethodSignature<TypeRef> signature =
                    method.DecodeSignature(
                        TypeRefDecoder.Instance,
                        TypeScope(reader, type));
                const MethodAttributes requiredInvokeAttributes =
                    MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot
                    | MethodAttributes.Virtual;
                if ((method.Attributes
                        & (requiredInvokeAttributes
                            | MethodAttributes.Static))
                    != requiredInvokeAttributes
                    || (method.ImplAttributes
                        & MethodImplAttributes.CodeTypeMask)
                        != MethodImplAttributes.Runtime
                    || !signature.Header.IsInstance
                    || signature.Header.HasExplicitThis
                    || signature.Header.IsGeneric
                    || signature.GenericParameterCount != 0
                    || signature.Header.CallingConvention
                        != SignatureCallingConvention.Default)
                {
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                int delegateArity = type.GetGenericParameters().Count;
                ImmutableArray<TypeRef> typeArguments =
                    delegateType.Kind == TypeRefKind.GenericInstance
                        ? delegateType.TypeArguments
                        : [];
                if (typeArguments.Length != delegateArity
                    || !GenericParametersAreInRange(
                        signature.ReturnType,
                        delegateArity,
                        methodArity: 0)
                    || signature.ParameterTypes.Any(parameter =>
                        !GenericParametersAreInRange(
                            parameter,
                            delegateArity,
                            methodArity: 0)))
                {
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }
                ImmutableArray<TypeRef> parameters =
                [
                    .. signature.ParameterTypes.Select(parameter =>
                        parameter.Instantiate(typeArguments, [])),
                ];
                TypeRef returnType = signature.ReturnType.Instantiate(
                    typeArguments,
                    []);
                ResourceEffectSelectorBinder.MatchResult delegateResult =
                    ResourceEffectSelectorBinder.TryResolveType(
                        delegateType,
                        selector.DirectCall,
                        out ResolvedResourceEffectType resolvedDelegate);
                if (delegateResult.Kind
                    != ResourceEffectSelectorBinder.MatchKind.Match)
                {
                    Fail(
                        delegateResult.Kind,
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                    return null;
                }

                var resolvedParameters =
                    ImmutableArray.CreateBuilder<ResolvedResourceEffectType>(
                        parameters.Length);
                foreach (TypeRef parameter in parameters)
                {
                    ResolvedResourceEffectType? resolvedParameter =
                        ResolveSignatureType(
                            parameter,
                            definition.Assembly.Assembly,
                            ResourceEffectOccurrenceBindingGapKind
                                .CallbackContract);
                    if (resolvedParameter is null)
                        return null;
                    resolvedParameters.Add(resolvedParameter);
                }
                ResolvedResourceEffectType? resolvedReturn =
                    ResolveSignatureType(
                        returnType,
                        definition.Assembly.Assembly,
                        ResourceEffectOccurrenceBindingGapKind
                            .CallbackContract);
                if (resolvedReturn is null)
                    return null;

                var contract =
                    new ResolvedResourceEffectCallbackContract(
                        index,
                        resolvedDelegate,
                        TypeDefinition(definition),
                        MetadataTokens.GetToken(invoke[0]),
                        resolvedParameters.ToImmutable(),
                        resolvedReturn);
                _callbacks.Add(index, contract);
                return contract;
            }
            catch (Exception ex) when (MetadataUnavailable(ex))
            {
                FailIncomplete(
                    ResourceEffectOccurrenceBindingGapKind.CallbackContract);
                return null;
            }
            catch (Exception ex) when (MetadataUnsupported(ex))
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.CallbackContract);
                return null;
            }
        }

        ResolvedResourceEffectOutcome? ResolveOutcome(
            ResourceEffectLocation source,
            ResourceEffectOutcomeTest test)
        {
            ResolvedResourceEffectLocation? resolvedSource =
                ResolveLocation(source);
            if (resolvedSource is null)
                return null;
            ResolvedResourceEffectOutcomeTest? resolvedTest =
                ResolveOutcomeTest(resolvedSource, test);
            return resolvedTest is null
                ? null
                : new ResolvedResourceEffectOutcome(
                    resolvedSource,
                    resolvedTest);
        }

        ResolvedResourceEffectOutcomeTest? ResolveOutcomeTest(
            ResolvedResourceEffectLocation source,
            ResourceEffectOutcomeTest test)
        {
            if (test is ResourceEffectOutcomeTest.Enum enumValue)
                return ResolveEnumOutcomeTest(source, enumValue);

            if (test is not ResourceEffectOutcomeTest.ExactType exact)
            {
                return new ResolvedResourceEffectOutcomeTest(
                    test,
                    null,
                    null,
                    test switch
                    {
                        ResourceEffectOutcomeTest.Boolean value =>
                            $"bool:{value.Value}",
                        ResourceEffectOutcomeTest.Null => "null",
                        ResourceEffectOutcomeTest.NonNull => "non-null",
                        _ => test.GetType().Name,
                    });
            }

            MetadataTypeDefinitionName? name =
                OutcomeTypeName(exact.Selector);
            if (name is null)
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                return null;
            }

            ResolvedAssemblyReference assembly =
                selector.DeclarationOrigin.AssemblyReference;
            try
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Open(assembly);
                if (session.ModuleVersionId()
                    != selector.DeclarationOrigin.ModuleVersionId)
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                    return null;
                }
                TypeDeclarationResult result =
                    session.ProbeDeclaration(name);
                switch (result)
                {
                    case TypeDeclarationResult.Defined defined:
                    {
                        var resolved =
                            new ResolvedResourceEffectTypeDefinition(
                                assembly,
                                        selector.DeclarationOrigin.ModuleVersionId,
                                defined.Definition,
                                name);
                        return new ResolvedResourceEffectOutcomeTest(
                            test,
                            resolved,
                            null,
                            "type:" + TypeDefinitionKey(resolved));
                    }
                    case TypeDeclarationResult.Ambiguous:
                        FailAmbiguous(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    case TypeDeclarationResult.Rejected:
                        FailUnsupported(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    default:
                        FailIncomplete(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                }
            }
            catch (Exception ex) when (MetadataUnavailable(ex))
            {
                FailIncomplete(
                    ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                return null;
            }
            catch (Exception ex) when (MetadataUnsupported(ex))
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                return null;
            }

            ResolvedResourceEffectOutcomeTest? ResolveEnumOutcomeTest(
                ResolvedResourceEffectLocation source,
                ResourceEffectOutcomeTest.Enum test)
            {
                if (!ResourceEffectSelectorBinder.TryGetDefinition(
                        source.Type.Type,
                        selector.DirectCall,
                        out ResolvedTypeDefinition definition))
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                    return null;
                }

                string[] segments = test.Value.Split('.');
                string memberName = segments[^1];
                string typeName =
                    string.IsNullOrEmpty(definition.Type.Namespace)
                        ? string.Join(".", definition.Type.Segments)
                        : definition.Type.Namespace
                            + "."
                            + string.Join(".", definition.Type.Segments);
                if (segments.Length > 1
                    && !string.Equals(
                        string.Join(".", segments[..^1]),
                        typeName,
                        StringComparison.Ordinal))
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                    return null;
                }

                try
                {
                    using Stream stream =
                        definition.Assembly.Assembly.OpenRead();
                    using var peReader = new PEReader(stream);
                    if (!peReader.HasMetadata)
                    {
                        FailIncomplete(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }
                    MetadataReader reader = peReader.GetMetadataReader();
                    TypeDefinitionHandle handle =
                        definition.Address.TryResolve(
                            reader,
                            out TypeDefinitionHandle resolved)
                            ? resolved
                            : default;
                    if (handle.IsNil)
                    {
                        FailIncomplete(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }
                    TypeDefinition type = reader.GetTypeDefinition(handle);
                    TypeRef? baseType = DecodeTypeHandle(
                        reader,
                        type.BaseType,
                        TypeScope(reader, type));
                    if (baseType is null
                        || !FrameworkIdentity.IsCoreLibraryType(
                            baseType,
                            "System",
                            "Enum"))
                    {
                        FailUnsupported(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }

                    TypeRef? underlyingType = null;
                    List<FieldDefinitionHandle> matches = [];
                    foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                    {
                        FieldDefinition field =
                            reader.GetFieldDefinition(fieldHandle);
                        bool isStatic =
                            (field.Attributes & FieldAttributes.Static) != 0;
                        if (!isStatic)
                        {
                            if (underlyingType is not null
                                || reader.GetString(field.Name) != "value__"
                                || (field.Attributes
                                    & (FieldAttributes.SpecialName
                                        | FieldAttributes.RTSpecialName))
                                    != (FieldAttributes.SpecialName
                                        | FieldAttributes.RTSpecialName))
                            {
                                FailUnsupported(
                                    ResourceEffectOccurrenceBindingGapKind
                                        .OutcomeType);
                                return null;
                            }
                            if (!SignatureBlobGuard
                                .IsSafeAndCompleteToDecode(
                                    reader,
                                    field.Signature,
                                    SignatureBlobGuard.Kind.Field))
                            {
                                FailUnsupported(
                                    ResourceEffectOccurrenceBindingGapKind
                                        .OutcomeType);
                                return null;
                            }
                            underlyingType = field.DecodeSignature(
                                TypeRefDecoder.Instance,
                                TypeScope(reader, type));
                        }
                        if (reader.GetString(field.Name) == memberName)
                            matches.Add(fieldHandle);
                    }
                    if (underlyingType is null
                        || !IsEnumUnderlyingType(underlyingType))
                    {
                        FailUnsupported(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }
                    if (matches.Count == 0)
                    {
                        FailIncomplete(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }
                    if (matches.Count > 1)
                    {
                        FailAmbiguous(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }

                    FieldDefinitionHandle match = matches[0];
                    FieldDefinition literal = reader.GetFieldDefinition(match);
                    TypeRef? literalType =
                        SignatureBlobGuard.IsSafeAndCompleteToDecode(
                            reader,
                            literal.Signature,
                            SignatureBlobGuard.Kind.Field)
                            ? literal.DecodeSignature(
                                TypeRefDecoder.Instance,
                                TypeScope(reader, type))
                            : null;
                    if ((literal.Attributes
                        & (FieldAttributes.Literal | FieldAttributes.Static))
                        != (FieldAttributes.Literal | FieldAttributes.Static)
                        || literalType?.Resolution
                            is not
                            {
                                Origin:
                                    TypeReferenceOrigin.CurrentAssembly,
                                Type: { } literalTypeName,
                            }
                        || literalTypeName != definition.Type
                        || literal.GetDefaultValue().IsNil
                        || !TryReadEnumConstant(
                            reader,
                            literal.GetDefaultValue(),
                            out decimal constant,
                            out ConstantTypeCode constantType)
                        || !EnumConstantTypeMatches(
                            underlyingType,
                            constantType))
                    {
                        FailUnsupported(
                            ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                        return null;
                    }

                    ResolvedResourceEffectTypeDefinition enumType =
                        TypeDefinition(definition);
                    var enumConstant = new ResolvedResourceEffectEnumConstant(
                        enumType,
                        MetadataTokens.GetToken(match),
                        memberName,
                        constant);
                    return new ResolvedResourceEffectOutcomeTest(
                        test,
                        null,
                        enumConstant,
                        "enum:"
                        + TypeDefinitionKey(enumType)
                        + ":"
                        + constant.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex) when (MetadataUnavailable(ex))
                {
                    FailIncomplete(
                        ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                    return null;
                }
                catch (Exception ex) when (MetadataUnsupported(ex))
                {
                    FailUnsupported(
                        ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                    return null;
                }
            }

            static bool IsEnumUnderlyingType(TypeRef type) =>
                EnumConstantTypeCode(type) is not null;

            static bool EnumConstantTypeMatches(
                TypeRef type,
                ConstantTypeCode constantType) =>
                EnumConstantTypeCode(type) == constantType;

            static ConstantTypeCode? EnumConstantTypeCode(TypeRef type)
            {
                if (!FrameworkIdentity.IsCoreLibraryType(
                        type,
                        "System",
                        type.Name))
                {
                    return null;
                }
                return type.Name switch
                {
                    "SByte" => ConstantTypeCode.SByte,
                    "Byte" => ConstantTypeCode.Byte,
                    "Int16" => ConstantTypeCode.Int16,
                    "UInt16" => ConstantTypeCode.UInt16,
                    "Int32" => ConstantTypeCode.Int32,
                    "UInt32" => ConstantTypeCode.UInt32,
                    "Int64" => ConstantTypeCode.Int64,
                    "UInt64" => ConstantTypeCode.UInt64,
                    _ => null,
                };
            }

            static bool TryReadEnumConstant(
                MetadataReader reader,
                ConstantHandle handle,
                out decimal value,
                out ConstantTypeCode type)
            {
                Constant constant = reader.GetConstant(handle);
                BlobReader blob = reader.GetBlobReader(constant.Value);
                type = constant.TypeCode;
                value = constant.TypeCode switch
                {
                    ConstantTypeCode.SByte => blob.ReadSByte(),
                    ConstantTypeCode.Byte => blob.ReadByte(),
                    ConstantTypeCode.Int16 => blob.ReadInt16(),
                    ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                    ConstantTypeCode.Int32 => blob.ReadInt32(),
                    ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                    ConstantTypeCode.Int64 => blob.ReadInt64(),
                    ConstantTypeCode.UInt64 => blob.ReadUInt64(),
                    _ => 0,
                };
                return constant.TypeCode
                    is ConstantTypeCode.SByte
                    or ConstantTypeCode.Byte
                    or ConstantTypeCode.Int16
                    or ConstantTypeCode.UInt16
                    or ConstantTypeCode.Int32
                    or ConstantTypeCode.UInt32
                    or ConstantTypeCode.Int64
                    or ConstantTypeCode.UInt64;
            }
        }

        ResolvedResourceEffectCompletion? ResolveCompletion(
            ResourceEffectCompletion? completion)
        {
            if (completion is null)
                return null;
            if (completion is ResourceEffectCompletion.Outcome)
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.OutcomeType);
                return null;
            }
            if (completion is ResourceEffectCompletion.OutcomeCase outcome)
            {
                ResolvedResourceEffectOutcome? resolved =
                    ResolveOutcome(outcome.Source, outcome.Test);
                return resolved is null
                    ? null
                    : new ResolvedResourceEffectCompletion(
                        completion,
                        resolved,
                        "outcome:"
                            + resolved.Source.CanonicalKey
                            + ":"
                            + resolved.Test.CanonicalKey);
            }
            return new ResolvedResourceEffectCompletion(
                completion,
                null,
                completion.GetType().Name);
        }

        ResolvedResourceEffectGuard? ResolveGuard(
            ResourceEffectGuard? guard)
        {
            if (guard is null)
                return null;
            var exact = (ResourceEffectGuard.ExactRuntimeType)guard;
            ResolvedResourceEffectLocation? subject =
                ResolveLocation(exact.Subject);
            if (subject is null)
                return null;
            TypeRef? expected = exact.Expected switch
            {
                ResourceEffectSignatureLocation.Receiver =>
                    selector.DirectCall.Call.Callee.DeclaringType,
                ResourceEffectSignatureLocation.Return =>
                    selector.DirectCall.Call.Callee.ReturnType,
                ResourceEffectSignatureLocation.Parameter parameter
                    when parameter.Index
                        < selector.DirectCall.Call.Callee
                            .ParameterTypes.Length =>
                    selector.DirectCall.Call.Callee
                        .ParameterTypes[parameter.Index],
                _ => null,
            };
            if (expected is null)
            {
                FailUnsupported(
                    ResourceEffectOccurrenceBindingGapKind.BoundaryLocation);
                return null;
            }
            ResourceEffectSelectorBinder.MatchResult result =
                ResourceEffectSelectorBinder.TryResolveType(
                    expected,
                    selector.DirectCall,
                    out ResolvedResourceEffectType resolvedExpected);
            if (result.Kind
                != ResourceEffectSelectorBinder.MatchKind.Match)
            {
                Fail(
                    result.Kind,
                    ResourceEffectOccurrenceBindingGapKind.TypeDefinition);
                return null;
            }
            return new ResolvedResourceEffectGuard(
                subject,
                resolvedExpected);
        }

        ResolvedResourceKindReference? ResolveKind(
            ResourceKindReference declared) =>
            selector.ResourceKinds.SingleOrDefault(kind =>
                kind.Identity == declared.Identity
                && kind.Arguments.Length == declared.Arguments.Length
                && declared.Arguments.Select(variable =>
                        selector.GenericBindings.Single(binding =>
                            binding.Variable == variable).Value)
                    .SequenceEqual(kind.Arguments));

        ResolvedResourceEffectType? ResolveSignatureType(
            TypeRef type,
            ResolvedAssemblyReference definingAssembly,
            ResourceEffectOccurrenceBindingGapKind gapKind,
            ResourceEffectLocation? location = null)
        {
            ResourceEffectSelectorBinder.MatchResult result =
                ResourceEffectSelectorBinder.TryResolveType(
                    type,
                    selector.DirectCall,
                    out ResolvedResourceEffectType resolved);
            if (result.Kind
                == ResourceEffectSelectorBinder.MatchKind.Match)
            {
                return resolved;
            }
            if (result.Kind
                    != ResourceEffectSelectorBinder.MatchKind.Incomplete
                || !TryBuildExactSignatureType(
                    type,
                    definingAssembly,
                    selector.DirectCall,
                    out resolved))
            {
                Fail(result.Kind, gapKind, location);
                return null;
            }
            return resolved;
        }

        static bool GenericParametersAreInRange(
            TypeRef type,
            int typeArity,
            int methodArity)
        {
            if (type.Kind == TypeRefKind.GenericParameter)
            {
                return type.GenericParameterIndex >= 0
                    && type.GenericParameterIndex < typeArity;
            }
            if (type.Kind == TypeRefKind.MethodGenericParameter)
            {
                return type.GenericParameterIndex >= 0
                    && type.GenericParameterIndex < methodArity;
            }
            if (type.ElementType is not null
                && !GenericParametersAreInRange(
                    type.ElementType,
                    typeArity,
                    methodArity))
            {
                return false;
            }
            return type.TypeArguments.All(argument =>
                GenericParametersAreInRange(
                    argument,
                    typeArity,
                    methodArity));
        }

        static bool TryBuildExactSignatureType(
            TypeRef type,
            ResolvedAssemblyReference currentAssembly,
            DirectCallDefinitionResolution.Resolved directCall,
            out ResolvedResourceEffectType resolved)
        {
            if (type.Kind
                is TypeRefKind.Unsupported
                    or TypeRefKind.Pinned)
            {
                resolved = null!;
                return false;
            }
            ResourceEffectSelectorBinder.MatchResult callBound =
                ResourceEffectSelectorBinder.TryResolveType(
                    type,
                    directCall,
                    out resolved);
            if (callBound.Kind
                == ResourceEffectSelectorBinder.MatchKind.Match)
            {
                return true;
            }
            if (type.Kind
                is TypeRefKind.GenericParameter
                    or TypeRefKind.MethodGenericParameter
                || callBound.Kind
                    != ResourceEffectSelectorBinder.MatchKind.Incomplete)
            {
                resolved = null!;
                return false;
            }

            ResolvedResourceEffectType? element = null;
            if (type.ElementType is not null
                && !TryBuildExactSignatureType(
                    type.ElementType,
                    currentAssembly,
                    directCall,
                    out element))
            {
                resolved = null!;
                return false;
            }

            var arguments =
                ImmutableArray.CreateBuilder<ResolvedResourceEffectType>(
                    type.TypeArguments.Length);
            foreach (TypeRef argument in type.TypeArguments)
            {
                if (!TryBuildExactSignatureType(
                        argument,
                        currentAssembly,
                        directCall,
                        out ResolvedResourceEffectType resolvedArgument))
                {
                    resolved = null!;
                    return false;
                }
                arguments.Add(resolvedArgument);
            }

            TypeRef definition = type.Kind == TypeRefKind.GenericInstance
                ? type.ElementType!
                : type;
            if (definition.Assembly == TypeRef.CoreLibrary
                && !ResourceEffectSelectorBinder
                    .IsIntrinsicCoreLibrarySignatureType(definition)
                && definition.Resolution?.Origin
                    is not TypeReferenceOrigin.CurrentAssembly)
            {
                resolved = null!;
                return false;
            }

            AssemblyReferenceIdentity? assembly =
                type.Kind is TypeRefKind.Definition
                    or TypeRefKind.GenericInstance
                    ? SignatureAssembly(
                        definition,
                        currentAssembly.Identity)
                    : element?.DefiningAssembly;
            if (type.Kind
                    is TypeRefKind.Definition
                        or TypeRefKind.GenericInstance
                && assembly is null
                && (type.Kind == TypeRefKind.GenericInstance
                        ? type.ElementType!
                        : type).Assembly
                    != TypeRef.CoreLibrary)
            {
                resolved = null!;
                return false;
            }
            if (type.Kind
                    is TypeRefKind.Definition
                        or TypeRefKind.GenericInstance
                && assembly == currentAssembly.Identity
                && !DefinitionExists(definition, currentAssembly))
            {
                resolved = null!;
                return false;
            }

            resolved = new ResolvedResourceEffectType(
                type,
                assembly,
                null,
                null,
                element,
                arguments.ToImmutable());
            return true;
        }

        static bool DefinitionExists(
            TypeRef type,
            ResolvedAssemblyReference assembly)
        {
            MetadataTypeDefinitionName? name = type.Resolution?.Type;
            if (name is null)
                return false;
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assembly);
            return session.ProbeDeclaration(name)
                is TypeDeclarationResult.Defined;
        }

        static AssemblyReferenceIdentity? SignatureAssembly(
            TypeRef definition,
            AssemblyReferenceIdentity currentAssembly) =>
            definition.Resolution?.Origin switch
            {
                TypeReferenceOrigin.CurrentAssembly origin =>
                    origin.Assembly ?? currentAssembly,
                TypeReferenceOrigin.IntrinsicCoreLibrary => null,
                _ when definition.Assembly == TypeRef.CoreLibrary => null,
                _ => null,
            };

        ResolvedResourceEffectLocation? Fail(
            ResourceEffectSelectorBinder.MatchKind kind,
            ResourceEffectOccurrenceBindingGapKind gapKind,
            ResourceEffectLocation? location = null)
        {
            var gap = new ResourceEffectOccurrenceBindingGap(
                gapKind,
                location);
            _failure = kind switch
            {
                ResourceEffectSelectorBinder.MatchKind.Ambiguous =>
                    new ResourceEffectOccurrenceBindingResult.Ambiguous(gap),
                ResourceEffectSelectorBinder.MatchKind.Unsupported =>
                    new ResourceEffectOccurrenceBindingResult.Unsupported(gap),
                _ => new ResourceEffectOccurrenceBindingResult.Incomplete(gap),
            };
            return null;
        }

        ResolvedResourceEffectLocation? FailAmbiguous(
            ResourceEffectOccurrenceBindingGapKind kind,
            ResourceEffectLocation? location = null)
        {
            _failure = new ResourceEffectOccurrenceBindingResult.Ambiguous(
                new ResourceEffectOccurrenceBindingGap(kind, location));
            return null;
        }

        ResolvedResourceEffectLocation? FailUnsupported(
            ResourceEffectOccurrenceBindingGapKind kind,
            ResourceEffectLocation? location = null)
        {
            _failure = new ResourceEffectOccurrenceBindingResult.Unsupported(
                new ResourceEffectOccurrenceBindingGap(kind, location));
            return null;
        }

        ResolvedResourceEffectLocation? FailIncomplete(
            ResourceEffectOccurrenceBindingGapKind kind,
            ResourceEffectLocation? location = null)
        {
            _failure = new ResourceEffectOccurrenceBindingResult.Incomplete(
                new ResourceEffectOccurrenceBindingGap(kind, location));
            return null;
        }

        static IEnumerable<ResourceEffectLocation> Locations(
            ResourceEffect value) =>
            value switch
            {
                ResourceEffect.Authority item => [item.Target],
                ResourceEffect.Acquire item =>
                    Present(item.Target, item.Correspondence, item.Lender),
                ResourceEffect.Move item =>
                    Present(
                        item.Source,
                        item.Target,
                        CompletionSource(item.When)),
                ResourceEffect.Consume item => [item.Source, item.Target],
                ResourceEffect.Release item =>
                    Present(
                        item.Source,
                        item.Correspondence,
                        item.Observation,
                        CompletionSource(item.When)),
                ResourceEffect.Borrow item =>
                    Present(item.Source, item.Target, item.Lender),
                ResourceEffect.Derive item =>
                    Present(item.Source, item.Target, GuardSubject(item.Guard)),
                ResourceEffect.Pass item => [item.Source, item.Target],
                ResourceEffect.Independent item =>
                    [item.Source, item.Target],
                ResourceEffect.Callback item => [item.Delegate],
                ResourceEffect.Accept item =>
                    Present(
                        item.Source,
                        item.Target,
                        CompletionSource(item.When)),
                ResourceEffect.Operation item =>
                    Present(GuardSubject(item.Guard)),
                ResourceEffect.Outcome item => [item.Source],
                _ => [],
            };

        static IEnumerable<ResourceEffectLocation> Present(
            params ResourceEffectLocation?[] locations) =>
            locations.OfType<ResourceEffectLocation>();

        static ResourceEffectLocation? GuardSubject(
            ResourceEffectGuard? guard) =>
            guard is ResourceEffectGuard.ExactRuntimeType exact
                ? exact.Subject
                : null;

        static ResourceEffectLocation? CompletionSource(
            ResourceEffectCompletion completion) =>
            completion is ResourceEffectCompletion.OutcomeCase outcome
                ? outcome.Source
                : null;

        static ResourceEffectCompletion? Completion(
            ResourceEffect value) =>
            value switch
            {
                ResourceEffect.Acquire item => item.When,
                ResourceEffect.Move item => item.When,
                ResourceEffect.Release item => item.When,
                ResourceEffect.Accept item => item.When,
                _ => null,
            };

        static ResourceEffectGuard? Guard(ResourceEffect value) =>
            value switch
            {
                ResourceEffect.Derive item => item.Guard,
                ResourceEffect.Operation item => item.Guard,
                _ => null,
            };

        static GenericScope TypeScope(
            MetadataReader reader,
            TypeDefinition type) =>
            new(
                [
                    .. type.GetGenericParameters().Select(handle =>
                        reader.GetString(
                            reader.GetGenericParameter(handle).Name)),
                ],
                []);

        static TypeRef? DecodeTypeHandle(
            MetadataReader reader,
            EntityHandle handle,
            GenericScope scope) =>
            handle.Kind switch
            {
                HandleKind.TypeDefinition =>
                    TypeRefDecoder.Instance.GetTypeFromDefinition(
                        reader,
                        (TypeDefinitionHandle)handle,
                        0),
                HandleKind.TypeReference =>
                    TypeRefDecoder.Instance.GetTypeFromReference(
                        reader,
                        (TypeReferenceHandle)handle,
                        0),
                HandleKind.TypeSpecification
                    when SignatureBlobGuard.IsSafeAndCompleteToDecode(
                        reader,
                        reader.GetTypeSpecification(
                            (TypeSpecificationHandle)handle).Signature,
                        SignatureBlobGuard.Kind.TypeSpecification) =>
                    TypeRefDecoder.Instance.GetTypeFromSpecification(
                        reader,
                        scope,
                        (TypeSpecificationHandle)handle,
                        0),
                _ => null,
            };

        static ResolvedResourceEffectTypeDefinition TypeDefinition(
            ResolvedTypeDefinition definition) =>
            new(
                definition.Assembly.Assembly,
                definition.Address.ModuleVersionId,
                definition.Address.Definition,
                definition.Type);

        static MetadataTypeDefinitionName? OutcomeTypeName(string selector)
        {
            int separator = selector.LastIndexOf('.');
            if (separator <= 0 || separator == selector.Length - 1)
                return null;
            MetadataTypeDefinitionNameResult result =
                MetadataTypeDefinitionName.Create(
                    selector[..separator],
                    [selector[(separator + 1)..]]);
            return result is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : null;
        }

        static string TypeDefinitionKey(
            ResolvedResourceEffectTypeDefinition definition) =>
            MetadataReceiptEvidence.For(
                definition.AssemblyReference.Registration)
            + ":"
            + definition.ModuleVersionId.ToString("D")
            + ":"
            + definition.Token.Value.ToString("X8", CultureInfo.InvariantCulture);

        static string CallbackKey(
            ResolvedResourceEffectCallbackContract callback) =>
            callback.DelegateParameterIndex.ToString(
                CultureInfo.InvariantCulture)
            + ":"
            + TypeDefinitionKey(callback.DelegateDefinition)
            + ":"
            + callback.InvokeMetadataToken.ToString(
                "X8",
                CultureInfo.InvariantCulture);

        static string KindKey(ResolvedResourceKindReference kind) =>
            kind.Identity.Value
            + "<"
            + string.Join(
                ",",
                kind.Arguments.Select(argument =>
                    ResolvedResourceEffectCanonicalizer.Type(argument)))
            + ">";

        static bool MetadataUnavailable(Exception exception) =>
            exception is IOException
                or UnauthorizedAccessException
                or ObjectDisposedException;

        static bool MetadataUnsupported(Exception exception) =>
            exception is NotSupportedException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or ArgumentOutOfRangeException
                or OverflowException
                or IndexOutOfRangeException;
    }
}
