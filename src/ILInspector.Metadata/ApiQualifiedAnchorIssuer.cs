using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;
using InertText.Encoding;

namespace ILInspector.Metadata;

internal sealed class ApiQualifiedAnchorIssuer
{
    readonly MetadataReader _reader;
    readonly PEReader _image;
    readonly MetadataOperationContext _operation;
    readonly TypeIndexFactory _getTypeIndex;
    readonly Func<
        MetadataTypeDefinitionAddress,
        CancellationToken,
        MetadataTypeDeclarationResult> _postType;
    readonly Func<
        MetadataTypeDefinitionAddress,
        MetadataMethodAddress,
        CancellationToken,
        MetadataMethodDeclarationResult> _postMethod;
    readonly Func<
        MetadataAccessorDeclarationRequest,
        CancellationToken,
        MetadataAccessorDeclarationResult> _postAccessor;
    readonly Dictionary<
        MetadataNamedTypeIdentity,
        ApiQualifiedTypeDefinitionIdentity> _normalizedTypes = [];
    ApiQualifiedAssemblyFamily? _assemblyFamily;
    IApiQualifiedTypeDefinitionResolver? _resolver;
    CancellationToken _token;

    internal ApiQualifiedAnchorIssuer(
        PEReader image,
        MetadataReader reader,
        MetadataOperationContext operation,
        TypeIndexFactory getTypeIndex,
        Func<
            MetadataTypeDefinitionAddress,
            CancellationToken,
            MetadataTypeDeclarationResult> postType,
        Func<
            MetadataTypeDefinitionAddress,
            MetadataMethodAddress,
            CancellationToken,
            MetadataMethodDeclarationResult> postMethod,
        Func<
            MetadataAccessorDeclarationRequest,
            CancellationToken,
            MetadataAccessorDeclarationResult> postAccessor)
    {
        _image = image;
        _reader = reader;
        _operation = operation;
        _getTypeIndex = getTypeIndex;
        _postType = postType;
        _postMethod = postMethod;
        _postAccessor = postAccessor;
    }

    internal ApiQualifiedAnchorResult Issue(
        MetadataDeclarationLocation location,
        IApiQualifiedTypeDefinitionResolver? resolver,
        CancellationToken token)
    {
        _resolver = resolver;
        _token = token;

        try
        {
            token.ThrowIfCancellationRequested();
            _assemblyFamily = ReadAssemblyFamily();
            EntityHandle handle = ResolveLocation(location);
            ApiQualifiedAnchor anchor = handle.Kind switch
            {
                HandleKind.TypeDefinition => IssueType(
                    (TypeDefinitionHandle)handle),
                HandleKind.MethodDefinition => IssueMethod(
                    (MethodDefinitionHandle)handle),
                HandleKind.PropertyDefinition => IssueProperty(
                    (PropertyDefinitionHandle)handle),
                HandleKind.EventDefinition => IssueEvent(
                    (EventDefinitionHandle)handle),
                HandleKind.FieldDefinition => IssueField(
                    (FieldDefinitionHandle)handle),
                _ => throw Failed(
                    ApiQualifiedAnchorStage.AddressValidation,
                    ApiQualifiedAnchorFailureReason.InvalidAddress,
                    "The declaration location does not identify a supported API declaration."),
            };

            return new ApiQualifiedAnchorResult.Complete(
                location,
                anchor,
                _operation.Counters);
        }
        catch (ApiQualifiedAnchorRefusedException exception)
        {
            return new ApiQualifiedAnchorResult.Refused(
                exception.Refusal,
                _operation.Counters);
        }
        catch (ApiQualifiedAnchorFailedException exception)
        {
            return new ApiQualifiedAnchorResult.Failed(
                exception.Failure,
                _operation.Counters);
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            return new ApiQualifiedAnchorResult.Failed(
                new(
                    ApiQualifiedAnchorStage.Projection,
                    ApiQualifiedAnchorFailureReason.WorkLimitExceeded,
                    exception.Message),
                _operation.Counters);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return new ApiQualifiedAnchorResult.Failed(
                new(
                    ApiQualifiedAnchorStage.Projection,
                    ApiQualifiedAnchorFailureReason.MalformedMetadata,
                    exception.Message),
                _operation.Counters);
        }
    }

    ApiQualifiedAssemblyFamily ReadAssemblyFamily()
    {
        try
        {
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(_reader);
            return new(
                identity.Name,
                identity.Culture,
                identity.PublicKeyToken);
        }
        catch (BadImageFormatException exception)
        {
            throw Refused(
                ApiQualifiedAnchorStage.AssemblyFamily,
                ApiQualifiedAnchorRefusalReason.UnsupportedImage,
                exception.Message);
        }
    }

    EntityHandle ResolveLocation(MetadataDeclarationLocation location)
    {
        if (location.ModuleVersionId
            != MetadataModuleIdentity.ReadVersionId(_reader))
        {
            throw Failed(
                ApiQualifiedAnchorStage.AddressValidation,
                ApiQualifiedAnchorFailureReason.InvalidAddress,
                "The declaration location belongs to a different metadata image.");
        }

        EntityHandle handle;
        try
        {
            handle = MetadataTokens.EntityHandle(location.MetadataToken);
        }
        catch (ArgumentException exception)
        {
            throw Failed(
                ApiQualifiedAnchorStage.AddressValidation,
                ApiQualifiedAnchorFailureReason.InvalidAddress,
                exception.Message);
        }

        HandleKind expected = location.Table switch
        {
            ApiDeclarationMetadataTable.TypeDefinition =>
                HandleKind.TypeDefinition,
            ApiDeclarationMetadataTable.MethodDefinition =>
                HandleKind.MethodDefinition,
            ApiDeclarationMetadataTable.PropertyDefinition =>
                HandleKind.PropertyDefinition,
            ApiDeclarationMetadataTable.EventDefinition =>
                HandleKind.EventDefinition,
            ApiDeclarationMetadataTable.FieldDefinition =>
                HandleKind.FieldDefinition,
            _ => default,
        };

        if (expected == default ||
            handle.Kind != expected ||
            MetadataTokens.GetRowNumber(handle)
                > GetTableRowCount(location.Table))
        {
            throw Failed(
                ApiQualifiedAnchorStage.AddressValidation,
                ApiQualifiedAnchorFailureReason.InvalidAddress,
                "The declaration metadata token is outside the requested table.");
        }

        return handle;
    }

    int GetTableRowCount(ApiDeclarationMetadataTable table)
        => _reader.GetTableRowCount(table switch
        {
            ApiDeclarationMetadataTable.TypeDefinition =>
                TableIndex.TypeDef,
            ApiDeclarationMetadataTable.MethodDefinition =>
                TableIndex.MethodDef,
            ApiDeclarationMetadataTable.PropertyDefinition =>
                TableIndex.Property,
            ApiDeclarationMetadataTable.EventDefinition =>
                TableIndex.Event,
            ApiDeclarationMetadataTable.FieldDefinition =>
                TableIndex.Field,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        });

    ApiQualifiedAnchor IssueType(TypeDefinitionHandle handle)
    {
        ApiQualifiedTypeDeclaration declaration = ReadTypeDeclaration(handle);
        return new ApiQualifiedAnchor.Type(
            ApiQualifiedAnchorFormat.V1,
            AssemblyFamily,
            declaration);
    }

    ApiQualifiedAnchor IssueMethod(MethodDefinitionHandle handle)
    {
        PreflightSignatureNamedTypes(handle);
        MethodDefinition definition = _reader.GetMethodDefinition(handle);
        TypeDefinitionHandle typeHandle = definition.GetDeclaringType();
        MetadataTypeDefinitionAddress typeAddress =
            MetadataTypeDefinitionAddress.FromHandle(
                _reader,
                typeHandle);
        MetadataMethodAddress methodAddress =
            MetadataMethodAddress.Create(_reader, handle);
        MetadataMethodDeclarationResult result =
            _postMethod(typeAddress, methodAddress, _token);
        MetadataMethodDeclarationEvidence evidence = result switch
        {
            MetadataMethodDeclarationResult.Posted posted =>
                posted.Evidence,
            MetadataMethodDeclarationResult.Rejected rejected =>
                throw DeclarationFailure(rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown method declaration result."),
        };

        ImmutableArray<ApiQualifiedTypeIdentity> parameterTypes =
            NormalizeTypes(evidence.Signature.ParameterTypes);
        if (parameterTypes.Length != evidence.Parameters.Length)
        {
            throw Failed(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorFailureReason.MalformedMetadata,
                "The method parameter evidence does not match its decoded signature.");
        }

        var parameters =
            ImmutableArray.CreateBuilder<ApiQualifiedParameter>(
                parameterTypes.Length);
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            ParameterAttributes attributes =
                evidence.Parameters[i].Attributes;
            parameters.Add(new(
                parameterTypes[i],
                (attributes & ParameterAttributes.In) != 0,
                (attributes & ParameterAttributes.Out) != 0));
        }

        string name = Decode(evidence.Name);
        bool isStatic =
            (evidence.Attributes & MethodAttributes.Static) != 0;
        ApiQualifiedMemberDeclaration.Method declaration = new(
            name,
            isStatic,
            evidence.Signature.GenericParameterCount,
            evidence.Signature.Header,
            evidence.Signature.RequiredParameterCount,
            NormalizeType(evidence.Signature.ReturnType),
            parameters.MoveToImmutable());
        MemberAnchor companion = CreateCompanionAnchor(
            (ref int remaining) =>
                ApiMemberIdentity.CreateMethodAnchorInfo(
                    _reader,
                    typeHandle,
                    definition,
                    ref remaining,
                    MethodCorrespondenceResolver.IsExtensionMethod(
                        _reader,
                        _reader.GetTypeDefinition(typeHandle),
                        definition))
                .Anchor);

        return CreateMember(
            typeHandle,
            declaration,
            companion);
    }

    ApiQualifiedAnchor IssueProperty(PropertyDefinitionHandle handle)
    {
        PreflightSignatureNamedTypes(handle);
        PropertyDefinition definition =
            _reader.GetPropertyDefinition(handle);
        TypeDefinitionHandle typeHandle = definition.GetDeclaringType();
        MetadataAccessorDeclarationEvidence evidence =
            ReadAccessorEvidence(
                typeHandle,
                MetadataAccessorDeclarationAddress.Create(
                    _reader,
                    handle));
        MetadataAccessorRootDeclarationEvidence.Property root =
            evidence.Root as
                MetadataAccessorRootDeclarationEvidence.Property
            ?? throw Failed(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorFailureReason.MalformedMetadata,
                "The property declaration produced non-property evidence.");

        ImmutableArray<ApiQualifiedTypeIdentity> parameterTypes =
            NormalizeTypes(root.Signature.IndexParameterTypes);
        ImmutableArray<ApiQualifiedParameter> parameters =
            ReadPropertyParameters(evidence, parameterTypes);
        bool isStatic = ReadAccessorStaticness(evidence);
        ApiQualifiedMemberDeclaration.Property declaration = new(
            Decode(root.Name),
            isStatic,
            root.Signature.GenericParameterCount,
            root.Signature.Header,
            root.Signature.RequiredParameterCount,
            NormalizeType(root.Signature.ValueType),
            parameters);
        MemberAnchor companion = CreateCompanionAnchor(
            (ref int remaining) =>
                ApiMemberIdentity.CreatePropertyAnchor(
                    _reader,
                    typeHandle,
                    definition,
                    ref remaining));

        return CreateMember(
            typeHandle,
            declaration,
            companion);
    }

    ApiQualifiedAnchor IssueEvent(EventDefinitionHandle handle)
    {
        EventDefinition definition = _reader.GetEventDefinition(handle);
        TypeDefinitionHandle typeHandle = definition.GetDeclaringType();
        MetadataAccessorDeclarationEvidence evidence =
            ReadAccessorEvidence(
                typeHandle,
                MetadataAccessorDeclarationAddress.Create(
                    _reader,
                    handle));
        MetadataAccessorRootDeclarationEvidence.Event root =
            evidence.Root as MetadataAccessorRootDeclarationEvidence.Event
            ?? throw Failed(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorFailureReason.MalformedMetadata,
                "The event declaration produced non-event evidence.");

        ApiQualifiedMemberDeclaration.Event declaration = new(
            Decode(root.Name),
            ReadAccessorStaticness(evidence),
            NormalizeType(root.EventType));
        MemberAnchor companion = CreateCompanionAnchor(
            (ref int remaining) =>
                ApiMemberIdentity.CreateEventAnchor(
                    _reader,
                    typeHandle,
                    definition,
                    ref remaining));

        return CreateMember(
            typeHandle,
            declaration,
            companion);
    }

    ApiQualifiedAnchor IssueField(FieldDefinitionHandle handle)
    {
        PreflightSignatureNamedTypes(handle);
        FieldDefinition definition = _reader.GetFieldDefinition(handle);
        TypeDefinitionHandle typeHandle = definition.GetDeclaringType();
        TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
        MetadataTypeIdentityDecodeResult decoded =
            MetadataTypeIdentityDecoder.DecodeField(
                _reader,
                type,
                definition,
                _operation);
        MetadataTypeIdentity fieldType = decoded switch
        {
            MetadataTypeIdentityDecodeResult.Decoded success =>
                success.Identity,
            MetadataTypeIdentityDecodeResult.Rejected rejected =>
                throw Failed(
                    ApiQualifiedAnchorStage.DeclarationEvidence,
                    ApiQualifiedAnchorFailureReason.MalformedMetadata,
                    rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown field signature result."),
        };

        ApiQualifiedMemberDeclaration.Field declaration = new(
            ReadName(definition.Name),
            (definition.Attributes & FieldAttributes.Static) != 0,
            NormalizeType(fieldType));
        MemberAnchor companion = CreateCompanionAnchor(
            (ref int remaining) =>
                ApiMemberIdentity.CreateFieldAnchor(
                    _reader,
                    typeHandle,
                    definition,
                    ref remaining));

        return CreateMember(
            typeHandle,
            declaration,
            companion);
    }

    ApiQualifiedAnchor.Member CreateMember(
        TypeDefinitionHandle typeHandle,
        ApiQualifiedMemberDeclaration declaration,
        MemberAnchor companion)
    {
        ApiQualifiedTypeDeclaration declaringType =
            ReadTypeDeclaration(typeHandle);
        return new(
            ApiQualifiedAnchorFormat.V1,
            AssemblyFamily,
            declaringType,
            declaration,
            companion);
    }

    MetadataAccessorDeclarationEvidence ReadAccessorEvidence(
        TypeDefinitionHandle typeHandle,
        MetadataAccessorDeclarationAddress declaration)
    {
        MetadataTypeDefinitionAddress typeAddress =
            MetadataTypeDefinitionAddress.FromHandle(
                _reader,
                typeHandle);
        MetadataAccessorDeclarationResult result = _postAccessor(
            new(typeAddress, declaration),
            _token);
        return result switch
        {
            MetadataAccessorDeclarationResult.Posted posted =>
                posted.Evidence,
            MetadataAccessorDeclarationResult.Rejected rejected =>
                throw Failed(
                    ApiQualifiedAnchorStage.DeclarationEvidence,
                    rejected.Failure.Reason
                        == MetadataAccessorDeclarationFailureReason
                            .BudgetExceeded
                        ? ApiQualifiedAnchorFailureReason.WorkLimitExceeded
                        : ApiQualifiedAnchorFailureReason.MalformedMetadata,
                    rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Unknown accessor declaration result."),
        };
    }

    ImmutableArray<ApiQualifiedParameter> ReadPropertyParameters(
        MetadataAccessorDeclarationEvidence evidence,
        ImmutableArray<ApiQualifiedTypeIdentity> types)
    {
        var flags = new (bool IsIn, bool IsOut)?[types.Length];
        bool found = types.Length == 0;

        foreach (MetadataAccessorSemanticsOccurrence occurrence
            in evidence.Accessors)
        {
            int expected = occurrence.Role switch
            {
                MetadataAccessorSemanticsRole.Getter => types.Length,
                MetadataAccessorSemanticsRole.Setter => types.Length + 1,
                _ => -1,
            };
            if (expected < 0)
            {
                continue;
            }

            if (occurrence.Correspondence.Role.Status
                    != MetadataAccessorRoleCorrespondenceStatus.Exact ||
                occurrence.Method.Parameters.Length != expected)
            {
                throw Refused(
                    ApiQualifiedAnchorStage.DeclarationEvidence,
                    ApiQualifiedAnchorRefusalReason.UnsupportedShape,
                    "The property accessor parameters do not exactly correspond to the property signature.");
            }

            found = true;
            for (int i = 0; i < types.Length; i++)
            {
                ParameterAttributes attributes =
                    occurrence.Method.Parameters[i].Attributes;
                var current = (
                    IsIn: (attributes & ParameterAttributes.In) != 0,
                    IsOut: (attributes & ParameterAttributes.Out) != 0);
                if (flags[i] is { } prior && prior != current)
                {
                    throw Failed(
                        ApiQualifiedAnchorStage.DeclarationEvidence,
                        ApiQualifiedAnchorFailureReason.MalformedMetadata,
                        "Property accessors disagree on index-parameter In/Out flags.");
                }

                flags[i] = current;
            }
        }

        if (!found || flags.Any(static value => value is null))
        {
            throw Refused(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorRefusalReason.UnsupportedShape,
                "Property index-parameter flags require an exact ordinary getter or setter.");
        }

        var parameters =
            ImmutableArray.CreateBuilder<ApiQualifiedParameter>(
                types.Length);
        for (int i = 0; i < types.Length; i++)
        {
            (bool isIn, bool isOut) = flags[i]!.Value;
            parameters.Add(new(types[i], isIn, isOut));
        }

        return parameters.MoveToImmutable();
    }

    static bool ReadAccessorStaticness(
        MetadataAccessorDeclarationEvidence evidence)
    {
        bool? isStatic = null;
        foreach (MetadataAccessorSemanticsOccurrence accessor
            in evidence.Accessors)
        {
            bool definesStaticness =
                evidence.Declaration.Kind switch
                {
                    MetadataAccessorDeclarationKind.Property =>
                        accessor.Role
                            is MetadataAccessorSemanticsRole.Getter
                            or MetadataAccessorSemanticsRole.Setter,
                    MetadataAccessorDeclarationKind.Event =>
                        accessor.Role
                            is MetadataAccessorSemanticsRole.AddOn
                            or MetadataAccessorSemanticsRole.RemoveOn,
                    _ => false,
                };
            if (!definesStaticness)
            {
                continue;
            }

            if (accessor.Correspondence.Role.Status
                != MetadataAccessorRoleCorrespondenceStatus.Exact)
            {
                throw Refused(
                    ApiQualifiedAnchorStage.DeclarationEvidence,
                    ApiQualifiedAnchorRefusalReason.UnsupportedShape,
                    "A defining accessor does not exactly correspond to its declaration.");
            }

            bool current =
                (accessor.Method.Attributes & MethodAttributes.Static) != 0;
            if (isStatic is not null && isStatic != current)
            {
                throw Failed(
                    ApiQualifiedAnchorStage.DeclarationEvidence,
                    ApiQualifiedAnchorFailureReason.MalformedMetadata,
                    "Accessors disagree on declaration staticness.");
            }

            isStatic = current;
        }

        return isStatic
            ?? throw Refused(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorRefusalReason.UnsupportedShape,
                "Declaration staticness requires a defining accessor method.");
    }

    ApiQualifiedTypeDeclaration ReadTypeDeclaration(
        TypeDefinitionHandle handle)
    {
        PreflightLocalDefinition(handle);
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(_reader, handle);
        MetadataTypeDeclarationResult result =
            _postType(address, _token);
        MetadataTypeDeclarationEvidence evidence = result switch
        {
            MetadataTypeDeclarationResult.Posted posted =>
                posted.Evidence,
            MetadataTypeDeclarationResult.Rejected rejected =>
                throw DeclarationFailure(rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown type declaration result."),
        };

        return new(
            NormalizeLocalDefinition(evidence.DefinitionIdentity, handle),
            evidence.Category);
    }

    void PreflightSignatureNamedTypes(EntityHandle handle)
    {
        SignatureOccurrenceDecodeResult decoded =
            SignatureOccurrenceDecoder.Decode(
                _image,
                handle,
                metrics: null,
                limits: null,
                _operation);
        if (decoded
            is not SignatureOccurrenceDecodeResult.Decoded complete)
        {
            return;
        }

        foreach (SignatureNamedTypeOccurrence occurrence
            in complete.Occurrences)
        {
            switch (occurrence.Reference.Scope)
            {
                case MetadataTypeReferenceScope.CurrentAssembly:
                    EnsureUniqueLocalName(occurrence.Reference.Type);
                    break;
                case MetadataTypeReferenceScope.ModuleReference:
                    throw Refused(
                        ApiQualifiedAnchorStage.NamedTypeResolution,
                        ApiQualifiedAnchorRefusalReason.ModuleScopedType,
                        "A module-scoped named type has no portable assembly family.");
            }
        }
    }

    void PreflightLocalDefinition(TypeDefinitionHandle handle)
    {
        MetadataTypeDefinitionNameReadResult result =
            MetadataTypeDefinitionNameReader.Read(
                _reader,
                handle,
                beforeMaterialize: amount =>
                    _operation.Charge(
                        MetadataOperationDimension.StructuredNodes,
                        amount),
                chargeChain: amount =>
                    _operation.Charge(
                        MetadataOperationDimension.RelationshipEdges,
                        amount),
                chargeCharacters: amount =>
                    _operation.Charge(
                        MetadataOperationDimension.RetainedText,
                        amount));
        MetadataTypeDefinitionName name = result switch
        {
            MetadataTypeDefinitionNameReadResult.Read read =>
                read.Name,
            MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                throw Failed(
                    ApiQualifiedAnchorStage.DeclarationEvidence,
                    ApiQualifiedAnchorFailureReason.MalformedMetadata,
                    rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Unknown type-name read result."),
        };
        TypeDefinitionHandle resolved = EnsureUniqueLocalName(name);
        if (resolved != handle)
        {
            throw Failed(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorFailureReason.InconsistentResolution,
                "The local type name identifies a different TypeDef.");
        }
    }

    TypeDefinitionHandle EnsureUniqueLocalName(
        MetadataTypeDefinitionName name)
    {
        MetadataTypeDefinitionIndex index = GetTypeDefinitionIndex();
        bool found = index.TryGetDefinition(
            name,
            out TypeDefinitionHandle handle,
            out bool ambiguous);
        if (ambiguous)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.AmbiguousType,
                "A local named type resolves to multiple TypeDefs.");
        }

        if (!found)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.UnresolvedType,
                "A local named type has no exact TypeDef.");
        }

        return handle;
    }

    ApiQualifiedTypeIdentity NormalizeType(MetadataTypeIdentity type)
    {
        _token.ThrowIfCancellationRequested();
        _operation.Charge(MetadataOperationDimension.StructuredNodes);

        return type switch
        {
            MetadataTypeIdentity.Primitive primitive =>
                new ApiQualifiedTypeIdentity.Primitive(primitive.Name),
            MetadataTypeIdentity.Named named =>
                new ApiQualifiedTypeIdentity.Named(
                    NormalizeNamedDefinition(named.Definition),
                    named.IsValueType),
            MetadataTypeIdentity.GenericInstance instance =>
                new ApiQualifiedTypeIdentity.GenericInstance(
                    NormalizeNamedDefinition(instance.Definition),
                    instance.IsValueType,
                    NormalizeTypes(instance.Arguments)),
            MetadataTypeIdentity.GenericParameter parameter =>
                new ApiQualifiedTypeIdentity.GenericParameter(
                    parameter.IsMethodParameter,
                    parameter.Index),
            MetadataTypeIdentity.SzArray array =>
                new ApiQualifiedTypeIdentity.SzArray(
                    NormalizeType(array.Element)),
            MetadataTypeIdentity.Array array =>
                new ApiQualifiedTypeIdentity.MdArray(
                    NormalizeType(array.Element),
                    array.Rank,
                    array.Sizes,
                    array.LowerBounds),
            MetadataTypeIdentity.Pointer pointer =>
                new ApiQualifiedTypeIdentity.Pointer(
                    NormalizeType(pointer.Element)),
            MetadataTypeIdentity.ByReference byRef =>
                new ApiQualifiedTypeIdentity.ByRef(
                    NormalizeType(byRef.Element)),
            MetadataTypeIdentity.FunctionPointer pointer =>
                new ApiQualifiedTypeIdentity.FunctionPointer(
                    NormalizeSignature(pointer.Signature)),
            MetadataTypeIdentity.Modified modified =>
                new ApiQualifiedTypeIdentity.Modified(
                    NormalizeType(modified.Modifier),
                    NormalizeType(modified.Type),
                    modified.IsRequired),
            MetadataTypeIdentity.Pinned pinned =>
                throw Refused(
                    ApiQualifiedAnchorStage.Projection,
                    ApiQualifiedAnchorRefusalReason.UnsupportedShape,
                    $"Pinned types are not portable API declaration types ({pinned.Type.GetType().Name})."),
            _ => throw new InvalidOperationException(
                "Unknown metadata type identity."),
        };
    }

    ApiQualifiedMethodSignature NormalizeSignature(
        MetadataMethodSignatureIdentity signature)
        => new(
            signature.Header,
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            NormalizeType(signature.ReturnType),
            NormalizeTypes(signature.ParameterTypes));

    ImmutableArray<ApiQualifiedTypeIdentity> NormalizeTypes(
        ImmutableArray<MetadataTypeIdentity> types)
    {
        var builder =
            ImmutableArray.CreateBuilder<ApiQualifiedTypeIdentity>(
                types.Length);
        foreach (MetadataTypeIdentity type in types)
        {
            builder.Add(NormalizeType(type));
        }

        return builder.MoveToImmutable();
    }

    ApiQualifiedTypeDefinitionIdentity NormalizeNamedDefinition(
        MetadataNamedTypeIdentity definition)
    {
        if (_normalizedTypes.TryGetValue(definition, out var cached))
        {
            return cached;
        }

        ApiQualifiedTypeDefinitionIdentity normalized =
            definition.Scope.Kind switch
            {
                MetadataTypeScopeKind.CurrentModule =>
                    ResolveLocalDefinition(definition),
                MetadataTypeScopeKind.AssemblyReference =>
                    ResolveExternalDefinition(definition),
                MetadataTypeScopeKind.ModuleReference =>
                    throw Refused(
                        ApiQualifiedAnchorStage.NamedTypeResolution,
                        ApiQualifiedAnchorRefusalReason.ModuleScopedType,
                        "A module-scoped named type has no portable assembly family."),
                _ => throw Failed(
                    ApiQualifiedAnchorStage.NamedTypeResolution,
                    ApiQualifiedAnchorFailureReason.MalformedMetadata,
                    "The named type has an unknown metadata scope."),
            };
        _normalizedTypes.Add(definition, normalized);
        return normalized;
    }

    ApiQualifiedTypeDefinitionIdentity ResolveLocalDefinition(
        MetadataNamedTypeIdentity definition)
    {
        MetadataTypeDefinitionIndex index = GetTypeDefinitionIndex();
        MetadataTypeDefinitionName name = DecodeDefinitionName(definition);
        bool found = index.TryGetDefinition(
            name,
            out TypeDefinitionHandle handle,
            out bool ambiguous);
        if (ambiguous)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.AmbiguousType,
                "A current-module named type resolves to multiple local TypeDefs.");
        }

        if (!found)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.UnresolvedType,
                "A current-module named type has no exact local TypeDef.");
        }

        return NormalizeLocalDefinition(definition, handle);
    }

    ApiQualifiedTypeDefinitionIdentity NormalizeLocalDefinition(
        MetadataNamedTypeIdentity definition,
        TypeDefinitionHandle handle)
    {
        MetadataTypeDefinitionIndex index = GetTypeDefinitionIndex();
        MetadataTypeDefinitionName name = DecodeDefinitionName(definition);
        bool found = index.TryGetDefinition(
            name,
            out TypeDefinitionHandle actual,
            out bool ambiguous);
        if (ambiguous)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.AmbiguousType,
                "A local type declaration has an ambiguous exact metadata name.");
        }

        if (!found)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.UnresolvedType,
                "A local type declaration has no exact indexed definition.");
        }

        if (actual != handle)
        {
            throw Failed(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorFailureReason.InconsistentResolution,
                "A local named-type resolution returned a different exact TypeDef name.");
        }

        return new(
            AssemblyFamily,
            definition.Namespace,
            definition.Segments,
            definition.IntroducedGenericParameterCounts);
    }

    MetadataTypeDefinitionIndex GetTypeDefinitionIndex()
    {
        try
        {
            return _getTypeIndex(
                beforeAccess: () => { },
                beforeRelationshipFollow: _ =>
                    _operation.Charge(
                        MetadataOperationDimension.RelationshipEdges),
                beforeCreateNode: () =>
                    _operation.Charge(
                        MetadataOperationDimension.StructuredNodes),
                beforeRetainText: amount =>
                    _operation.Charge(
                        MetadataOperationDimension.RetainedText,
                        amount));
        }
        catch (MetadataTypeDefinitionIndexFailureException exception)
        {
            throw Failed(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                exception.Kind
                    == MetadataTypeDefinitionIndexFailureKind
                        .BudgetExceeded
                    ? ApiQualifiedAnchorFailureReason.WorkLimitExceeded
                    : ApiQualifiedAnchorFailureReason.MalformedMetadata,
                exception.Message);
        }
    }

    ApiQualifiedTypeDefinitionIdentity ResolveExternalDefinition(
        MetadataNamedTypeIdentity definition)
    {
        if (_resolver is null)
        {
            throw Refused(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorRefusalReason.ResolverRequired,
                "An external named type requires an owner-authorized definition resolver.");
        }

        _operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
        ApiQualifiedTypeDefinitionResolution result =
            _resolver.Resolve(definition, _token);
        ApiQualifiedTypeDefinitionIdentity resolved = result switch
        {
            ApiQualifiedTypeDefinitionResolution.Resolved success =>
                success.Definition,
            ApiQualifiedTypeDefinitionResolution.Refused refused =>
                throw Refused(
                    ApiQualifiedAnchorStage.NamedTypeResolution,
                    refused.Reason,
                    refused.Detail),
            ApiQualifiedTypeDefinitionResolution.Failed failed =>
                throw Failed(
                    ApiQualifiedAnchorStage.NamedTypeResolution,
                    ApiQualifiedAnchorFailureReason.InconsistentResolution,
                    failed.Detail),
            _ => throw Failed(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorFailureReason.InconsistentResolution,
                "The type-definition resolver returned an unknown result."),
        };

        if (!DefinitionNameEquals(definition, resolved))
        {
            throw Failed(
                ApiQualifiedAnchorStage.NamedTypeResolution,
                ApiQualifiedAnchorFailureReason.InconsistentResolution,
                "The type-definition resolver changed the exact type name or generic arity.");
        }

        return resolved;
    }

    static bool DefinitionNameEquals(
        MetadataNamedTypeIdentity source,
        ApiQualifiedTypeDefinitionIdentity resolved)
        => source.Namespace == resolved.Namespace &&
            source.Segments.AsSpan().SequenceEqual(
                resolved.Segments.AsSpan()) &&
            source.IntroducedGenericParameterCounts.AsSpan()
                .SequenceEqual(
                    resolved.IntroducedGenericParameterCounts.AsSpan());

    static MetadataTypeDefinitionName DecodeDefinitionName(
        MetadataNamedTypeIdentity identity)
    {
        string @namespace = Decode(identity.Namespace);
        var segments =
            ImmutableArray.CreateBuilder<string>(
                identity.Segments.Length);
        foreach (InertText.InertString segment in identity.Segments)
        {
            segments.Add(Decode(segment));
        }

        return MetadataTypeDefinitionName.Create(
            @namespace,
            segments.MoveToImmutable()) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid =>
                valid.Name,
            MetadataTypeDefinitionNameResult.Rejected =>
                throw new BadImageFormatException(
                    "A contained named-type identity is not a valid exact metadata name."),
            _ => throw new InvalidOperationException(
                "Unknown type-name creation result."),
        };
    }

    static string Decode(InertText.InertString value)
        => VisualEncoder.TryDecode(
            value.ToString(),
            out string? decoded)
            ? decoded
            : throw new BadImageFormatException(
                "A contained metadata name could not be decoded.");

    string ReadName(StringHandle handle)
    {
        int bytes = _reader.GetBlobReader(handle).Length;
        if (bytes > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw Failed(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorFailureReason.WorkLimitExceeded,
                "A field name exceeds the structural string limit.");
        }

        _operation.Charge(
            MetadataOperationDimension.RetainedText,
            bytes);
        return MetadataSafetyPolicy.ReadStructuralString(_reader, handle);
    }

    delegate MemberAnchor CompanionFactory(ref int remaining);

    internal delegate MetadataTypeDefinitionIndex TypeIndexFactory(
        Action beforeAccess,
        Action<TypeDefinitionHandle> beforeRelationshipFollow,
        Action beforeCreateNode,
        Action<int> beforeRetainText);

    MemberAnchor CreateCompanionAnchor(CompanionFactory factory)
    {
        int remaining = MetadataSafetyPolicy.MaxAnchorSignatureWorkChars;
        MemberAnchor anchor = factory(ref remaining);
        int spent =
            MetadataSafetyPolicy.MaxAnchorSignatureWorkChars - remaining;
        _operation.Charge(
            MetadataOperationDimension.StructuredNodes,
            Math.Max(spent, 1));
        return anchor;
    }

    ApiQualifiedAssemblyFamily AssemblyFamily
        => _assemblyFamily
            ?? throw new InvalidOperationException(
                "The assembly family has not been read.");

    static ApiQualifiedAnchorFailedException DeclarationFailure(
        MetadataMethodDeclarationFailure failure)
        => Failed(
            ApiQualifiedAnchorStage.DeclarationEvidence,
            failure.Reason == MetadataMethodDeclarationFailureReason
                .BudgetExceeded
                ? ApiQualifiedAnchorFailureReason.WorkLimitExceeded
                : ApiQualifiedAnchorFailureReason.MalformedMetadata,
            failure.Detail);

    static ApiQualifiedAnchorFailedException DeclarationFailure(
        MetadataTypeDeclarationFailure failure)
        => failure.Reason
            == MetadataTypeDeclarationFailureReason.UnsupportedShape
            ? throw Refused(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                ApiQualifiedAnchorRefusalReason.UnsupportedShape,
                failure.Detail)
            : Failed(
                ApiQualifiedAnchorStage.DeclarationEvidence,
                failure.Reason == MetadataTypeDeclarationFailureReason
                    .BudgetExceeded
                    ? ApiQualifiedAnchorFailureReason.WorkLimitExceeded
                    : ApiQualifiedAnchorFailureReason.MalformedMetadata,
                failure.Detail);

    static ApiQualifiedAnchorRefusedException Refused(
        ApiQualifiedAnchorStage stage,
        ApiQualifiedAnchorRefusalReason reason,
        string detail)
        => new(new(stage, reason, detail));

    static ApiQualifiedAnchorFailedException Failed(
        ApiQualifiedAnchorStage stage,
        ApiQualifiedAnchorFailureReason reason,
        string detail)
        => new(new(stage, reason, detail));

    sealed class ApiQualifiedAnchorRefusedException(
        ApiQualifiedAnchorRefusal refusal) : Exception(refusal.Detail)
    {
        internal ApiQualifiedAnchorRefusal Refusal { get; } = refusal;
    }

    sealed class ApiQualifiedAnchorFailedException(
        ApiQualifiedAnchorFailure failure) : Exception(failure.Detail)
    {
        internal ApiQualifiedAnchorFailure Failure { get; } = failure;
    }
}
