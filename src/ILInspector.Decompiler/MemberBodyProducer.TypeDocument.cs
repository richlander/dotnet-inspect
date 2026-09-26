using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

public static partial class MemberBodyProducer
{
    internal static CSharpTypeDocumentOutcome ProduceTypeDocument(
        ApiType requestedType,
        MetadataSource source,
        bool pdbSupplied,
        PrinterOptions? printerOptions,
        CSharpTypeDocumentProductionTracker tracker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tracker);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveServiceType(
                source.Reader,
                requestedType,
                out TypeDefinitionHandle typeHandle))
        {
            return new CSharpTypeDocumentOutcome.Rejected(
                "The requested Type identity does not resolve in the supplied assembly.",
                tracker.Attempted);
        }

        ThrowIfMemorySafetyModeUnavailable(source);
        ApiSurface completeSurface =
            source.ExtractResolutionAwareApiSurface(
                includeAll: true,
                typesOnly: false,
                includeCompilerGenerated: true);
        cancellationToken.ThrowIfCancellationRequested();
        ApiType? type = completeSurface.Types.SingleOrDefault(candidate =>
            candidate.MetadataToken == MetadataTokens.GetToken(typeHandle)
            && candidate.DefinitionName == requestedType.DefinitionName);
        if (type is null)
        {
            return new CSharpTypeDocumentOutcome.Unavailable(
                "Complete same-reader API extraction did not retain the selected Type.",
                tracker.Attempted);
        }

        MetadataReader reader = source.Reader;
        TypeDefinition definition = reader.GetTypeDefinition(typeHandle);
        var directArtifactTokens = definition.GetFields()
            .Select(static handle => MetadataTokens.GetToken(handle))
            .Concat(definition.GetMethods().Select(
                static handle => MetadataTokens.GetToken(handle)))
            .Concat(definition.GetProperties().Select(
                static handle => MetadataTokens.GetToken(handle)))
            .Concat(definition.GetEvents().Select(
                static handle => MetadataTokens.GetToken(handle)))
            .ToHashSet();
        var logicalMembers = SelectLogicalMembers(
            type,
            directArtifactTokens);
        foreach (ApiMember member in logicalMembers)
        {
            if (member.SetterToken is { } setterToken
                && member.SignatureModel is { } signature
                && MetadataDeclarationQuery.IsInitOnlySetter(
                    reader,
                    definition,
                    reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(
                        setterToken & 0x00FFFFFF))))
            {
                foreach (ApiAccessor accessor in signature.Accessors.Where(
                    accessor => accessor.Kind == "set"))
                {
                    accessor.Kind = "init";
                }
            }
        }
        var declarationIdByToken = logicalMembers
            .Select((member, index) => (Token: DeclarationToken(member), index))
            .Where(static entry => entry.Token is not null)
            .ToDictionary(static entry => entry.Token!.Value, static entry => entry.index);
        var accessorOwners = BuildAccessorOwners(logicalMembers);
        var backingOwners = BuildBackingOwners(logicalMembers);

        var artifacts = new List<ArtifactBuild>();
        int anchorWorkRemaining =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;
        foreach (FieldDefinitionHandle handle in definition.GetFields())
        {
            cancellationToken.ThrowIfCancellationRequested();
            FieldDefinition field = reader.GetFieldDefinition(handle);
            int token = MetadataTokens.GetToken(handle);
            MemberAnchor anchor = ApiMemberIdentity.CreateFieldAnchor(
                reader,
                typeHandle,
                field,
                ref anchorWorkRemaining);
            var representation = FieldRepresentation(
                reader, type, field, token, declarationIdByToken, backingOwners);
            if (representation is null)
            {
                return new CSharpTypeDocumentOutcome.Unavailable(
                    $"FieldDef 0x{token:X8} has no proven C# representation.",
                    tracker.Attempted);
            }
            artifacts.Add(new(
                token,
                CSharpTypeArtifactKind.Field,
                anchor,
                Origin(reader, field.GetCustomAttributes()),
                representation));
        }
        foreach (MethodDefinitionHandle handle in definition.GetMethods())
        {
            cancellationToken.ThrowIfCancellationRequested();
            MethodDefinition method = reader.GetMethodDefinition(handle);
            int token = MetadataTokens.GetToken(handle);
            MemberAnchor anchor = ApiMemberIdentity.CreateMethodAnchor(
                reader,
                typeHandle,
                method);
            var representation = MethodRepresentation(
                type, token, declarationIdByToken, accessorOwners);
            if (representation is null)
            {
                return new CSharpTypeDocumentOutcome.Unavailable(
                    $"MethodDef 0x{token:X8} has no proven C# representation.",
                    tracker.Attempted);
            }
            artifacts.Add(new(
                token,
                CSharpTypeArtifactKind.Method,
                anchor,
                Origin(reader, method.GetCustomAttributes()),
                representation));
        }
        foreach (PropertyDefinitionHandle handle in definition.GetProperties())
        {
            cancellationToken.ThrowIfCancellationRequested();
            PropertyDefinition property = reader.GetPropertyDefinition(handle);
            int token = MetadataTokens.GetToken(handle);
            MemberAnchor anchor = ApiMemberIdentity.CreatePropertyAnchor(
                reader,
                typeHandle,
                property,
                ref anchorWorkRemaining);
            if (!declarationIdByToken.TryGetValue(token, out int declarationId))
            {
                return new CSharpTypeDocumentOutcome.Unavailable(
                    $"PropertyDef 0x{token:X8} has no complete logical declaration.",
                    tracker.Attempted);
            }
            artifacts.Add(new(
                token,
                CSharpTypeArtifactKind.Property,
                anchor,
                Origin(reader, property.GetCustomAttributes()),
                new(
                    CSharpTypeArtifactRepresentationKind.Declaration,
                    CSharpTypeArtifactRole.Declaration,
                    declarationId)));
        }
        foreach (EventDefinitionHandle handle in definition.GetEvents())
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventDefinition @event = reader.GetEventDefinition(handle);
            int token = MetadataTokens.GetToken(handle);
            MemberAnchor anchor = ApiMemberIdentity.CreateEventAnchor(
                reader,
                typeHandle,
                @event,
                ref anchorWorkRemaining);
            if (!declarationIdByToken.TryGetValue(token, out int declarationId))
            {
                return new CSharpTypeDocumentOutcome.Unavailable(
                    $"EventDef 0x{token:X8} has no complete logical declaration.",
                    tracker.Attempted);
            }
            artifacts.Add(new(
                token,
                CSharpTypeArtifactKind.Event,
                anchor,
                Origin(reader, @event.GetCustomAttributes()),
                new(
                    CSharpTypeArtifactRepresentationKind.Declaration,
                    CSharpTypeArtifactRole.Declaration,
                    declarationId)));
        }
        artifacts.Sort(static (left, right) => left.Token.CompareTo(right.Token));

        var artifactIdByToken = artifacts
            .Select((artifact, id) => (artifact.Token, id))
            .ToDictionary(static entry => entry.Token, static entry => entry.id);
        var bodyBuilds = new List<BodyBuild>();
        foreach (MethodDefinitionHandle handle in definition.GetMethods())
        {
            cancellationToken.ThrowIfCancellationRequested();
            MethodDefinition method = reader.GetMethodDefinition(handle);
            int token = MetadataTokens.GetToken(handle);
            CSharpTypeBodyRole role = accessorOwners.TryGetValue(
                token,
                out AccessorOwner? accessor)
                ? BodyRole(accessor.Role)
                : CSharpTypeBodyRole.Method;
            string fingerprint =
                CSharpBodyDiff.ComputePhysicalMethodFingerprint(source, handle);
            if (method.RelativeVirtualAddress == 0)
            {
                bodyBuilds.Add(new(
                    token,
                    role,
                    HasManagedBody: false,
                    CSharpTypeBodyOutcome.NoBody,
                    Fidelity: null,
                    fingerprint,
                    [],
                    Result: null));
                continue;
            }

            if (!tracker.TryBegin())
            {
                bodyBuilds.Add(new(
                    token,
                    role,
                    HasManagedBody: true,
                    CSharpTypeBodyOutcome.Unavailable,
                    Fidelity: null,
                    fingerprint,
                    [
                        new(
                            DiagnosticIds.CompositionBudgetExceeded,
                            $"C# Type document exhausted its {tracker.Maximum} body-projection budget."),
                    ],
                    Result: null));
                continue;
            }

            SelectedPropertyAccessorSource? propertySource =
                accessor is { Declaration.Kind: "property" }
                    ? SelectedPropertyAccessorSource.Create(source, handle)
                    : null;
            MemberBodyProductionResult produced = ProduceBody(
                source,
                MetadataMethodAddress.Create(reader, handle),
                propertySource,
                printerOptions,
                propagateUnexpectedFailures: true);
            cancellationToken.ThrowIfCancellationRequested();
            bodyBuilds.Add(new(
                token,
                role,
                HasManagedBody: true,
                produced.Status switch
                {
                    MemberBodyProductionStatus.Complete =>
                        CSharpTypeBodyOutcome.Available,
                    MemberBodyProductionStatus.Absent =>
                        CSharpTypeBodyOutcome.Unavailable,
                    _ => CSharpTypeBodyOutcome.Failed,
                },
                produced.Status == MemberBodyProductionStatus.Complete
                    ? produced.Projection.Fidelity
                    : produced.Status == MemberBodyProductionStatus.Failed
                        ? DecompilationFidelity.Failed
                        : null,
                fingerprint,
                [.. produced.Projection.Diagnostics],
                produced));
        }
        for (int index = 0; index < bodyBuilds.Count; index++)
        {
            bodyBuilds[index] = ReprintBodyWithQualifiedTypeNames(
                bodyBuilds[index],
                printerOptions);
        }

        var bodyIdByToken = bodyBuilds
            .Select((body, id) => (body.Token, id))
            .ToDictionary(static entry => entry.Token, static entry => entry.id);
        if (!CanOmitUnavailableConstructorInitializer(type, reader, definition)
            && logicalMembers.Any(member =>
                member.Kind == "constructor"
                && !member.IsStatic
                && member.MetadataToken is { } token
                && bodyBuilds.Single(body => body.Token == token) is
                    { Outcome: not CSharpTypeBodyOutcome.Available } body
                && body.Result?.Body?.ConstructorInitializer is null))
        {
            return new CSharpTypeDocumentOutcome.Unavailable(
                "A constructor body is unavailable and its required constructor initializer cannot be proven.",
                tracker.Attempted);
        }
        var loweredOwners = new Dictionary<int, int>();
        foreach (BodyBuild body in bodyBuilds)
        {
            if (body.Result?.RaisedFunction is not { } function)
                continue;
            foreach (LocalFunctionStatement local in function.Descendants.OfType<LocalFunctionStatement>())
            {
                if (local.SourceMethodAddress is { } address
                    && address.BelongsTo(reader)
                    && directArtifactTokens.Contains(MetadataTokens.GetToken(address.Handle)))
                {
                    int helperToken = MetadataTokens.GetToken(address.Handle);
                    if (loweredOwners.TryGetValue(helperToken, out int owner)
                        && owner != body.Token)
                    {
                        return new CSharpTypeDocumentOutcome.Unavailable(
                            "A lowered helper has more than one primary body representation.",
                            tracker.Attempted);
                    }
                    loweredOwners[helperToken] = body.Token;
                }
            }
        }
        if (loweredOwners.Count > 0)
        {
            var oldMembers = logicalMembers;
            logicalMembers = [.. oldMembers.Where(member =>
                DeclarationToken(member) is not { } token
                || !loweredOwners.ContainsKey(token))];
            var newIds = logicalMembers.Select((member, id) => (member, id))
                .ToDictionary(entry => DeclarationToken(entry.member)!.Value, entry => entry.id);
            for (int i = 0; i < artifacts.Count; i++)
            {
                ArtifactBuild artifact = artifacts[i];
                if (loweredOwners.TryGetValue(artifact.Token, out int bodyToken))
                {
                    artifacts[i] = artifact with
                    {
                        Representation = new(
                            CSharpTypeArtifactRepresentationKind.PhysicalBody,
                            CSharpTypeArtifactRole.LoweredImplementationHelper,
                            artifact.Token),
                    };
                }
                else if (artifact.Representation.Kind == CSharpTypeArtifactRepresentationKind.Declaration)
                {
                    int oldId = artifact.Representation.TargetId!.Value;
                    artifacts[i] = artifact with
                    {
                        Representation = artifact.Representation with
                        {
                            TargetId = newIds[DeclarationToken(oldMembers[oldId])!.Value],
                        },
                    };
                }
            }
        }
        InitializerCollection? initializerCollection = CollectInitializers(
            logicalMembers,
            bodyBuilds,
            bodyIdByToken,
            out string? initializerFailure);
        if (initializerCollection is null)
        {
            return new CSharpTypeDocumentOutcome.Unavailable(
                initializerFailure
                    ?? "Constructor initializers do not have one unambiguous declaration value.",
                tracker.Attempted);
        }
        var initializerByField = initializerCollection.Initializers;
        List<ApiMember> orderedMembers = OrderMembersForInitializerExecution(
            logicalMembers,
            initializerCollection.DeclarationOrder);
        if (!logicalMembers.SequenceEqual(orderedMembers))
        {
            var oldMembers = logicalMembers;
            logicalMembers = orderedMembers;
            var newIds = logicalMembers
                .Select((member, id) => (member, id))
                .ToDictionary(
                    entry => DeclarationToken(entry.member)!.Value,
                    entry => entry.id);
            for (int i = 0; i < artifacts.Count; i++)
            {
                ArtifactBuild artifact = artifacts[i];
                if (artifact.Representation.Kind
                    != CSharpTypeArtifactRepresentationKind.Declaration)
                {
                    continue;
                }
                int oldId = artifact.Representation.TargetId!.Value;
                artifacts[i] = artifact with
                {
                    Representation = artifact.Representation with
                    {
                        TargetId = newIds[
                            DeclarationToken(oldMembers[oldId])!.Value],
                    },
                };
            }
        }
        var memberRequests = BuildMemberRequests(
            source,
            reader,
            typeHandle,
            type,
            logicalMembers,
            bodyBuilds,
            initializerByField,
            printerOptions);
        var containingTypes = ContainingTypes(completeSurface, type);
        CSharpStructuredTypePlan plan;
        try
        {
            plan = CSharpStructuredTypePlanProducer.Produce(
                new(
                    type,
                    memberRequests,
                    containingTypes));
        }
        catch (NotSupportedException ex)
        {
            return new CSharpTypeDocumentOutcome.Unavailable(
                $"CSharp could not issue a complete structured render plan: {ex.Message}",
                tracker.Attempted);
        }

        var physicalArtifacts = artifacts
            .Select((artifact, id) => new CSharpTypePhysicalArtifact(
                id,
                artifact.Anchor,
                artifact.Token,
                artifact.Kind,
                artifact.Origin,
                ResolveRepresentationTargets(
                    artifact.Representation,
                    bodyIdByToken)))
            .ToImmutableArray();
        var physicalBodies = bodyBuilds
            .Select((body, id) => new CSharpTypePhysicalBody(
                id,
                new(
                    source.ModuleVersionId,
                    MetadataTokens.MethodDefinitionHandle(
                        body.Token & 0x00FFFFFF)),
                artifactIdByToken[body.Token],
                body.Role,
                body.HasManagedBody,
                body.Outcome,
                body.Fidelity,
                body.Fingerprint,
                body.Diagnostics))
            .ToImmutableArray();
        ImmutableArray<CSharpTypeDeclaration> declarations =
            BuildDeclarations(
                reader,
                typeHandle,
                plan,
                logicalMembers,
                bodyIdByToken,
                initializerByField);
        CSharpTypeFrame frame = new(
            ConvertParts(
                plan.PrefixParts,
                bodyIdByToken,
                initializer: null),
            plan.DeclarationSeparator,
            plan.Suffix);
        MetadataTypeDefinitionName typeName =
            type.DefinitionName
            ?? throw new InvalidOperationException(
                "Complete same-reader extraction omitted the selected Type identity.");
        CSharpTypeDocument document = CSharpTypeDocument.Create(
            typeName,
            MetadataTypeDefinitionAddress.FromToken(
                source.ModuleVersionId,
                MetadataTokens.GetToken(typeHandle)),
            new(
                CSharpTypeSourceKind.Decompiled,
                source.AssemblyName,
                pdbSupplied,
                source.Symbols,
                RenderingPolicy(printerOptions)),
            frame,
            physicalArtifacts,
            physicalBodies,
            declarations,
            CSharpTypeDocumentationCapability.Unavailable);
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<int> failures =
        [
            .. document.Bodies
                .Where(static body =>
                    body.Outcome is CSharpTypeBodyOutcome.Unavailable
                        or CSharpTypeBodyOutcome.Failed)
                .Select(static body => body.Id),
        ];
        return failures.Length == 0
            ? new CSharpTypeDocumentOutcome.Available(document)
            : new CSharpTypeDocumentOutcome.Incomplete(document, failures);
    }

    static List<ApiMember> SelectLogicalMembers(
        ApiType type,
        IReadOnlySet<int> directArtifactTokens)
    {
        if (type.Kind == "delegate")
        {
            return
            [
                type.Members.Single(member =>
                    member.Name == "Invoke"
                    && member.MetadataToken is not null),
            ];
        }

        var accessorTokens = type.Members
            .SelectMany(static member => new[]
            {
                member.GetterToken,
                member.SetterToken,
                member.AdderToken,
                member.RemoverToken,
            })
            .OfType<int>()
            .ToHashSet();
        return
        [
            .. type.Members.Where(member =>
                DeclarationToken(member) is { } declarationToken
                && directArtifactTokens.Contains(declarationToken)
                && (member.MetadataToken is not { } methodToken
                    || !accessorTokens.Contains(methodToken)))
                .OrderBy(static member =>
                    member.Kind == "field" ? 0 : 1),
        ];
    }

    static int? DeclarationToken(ApiMember member)
        => member.DeclarationMetadataToken ?? member.MetadataToken;

    static List<ApiMember> OrderMembersForInitializerExecution(
        IReadOnlyList<ApiMember> members,
        ImmutableArray<int> initializerOrder)
    {
        if (initializerOrder.IsDefaultOrEmpty)
            return [.. members];

        var initializerIndex = initializerOrder
            .Select((token, index) => (token, index))
            .ToDictionary(static entry => entry.token, static entry => entry.index);
        return
        [
            .. members
                .OrderBy(member =>
                    DeclarationToken(member) is { } token
                    && initializerIndex.ContainsKey(token)
                        ? 0
                        : member.Kind == "field"
                            ? 1
                            : 2)
                .ThenBy(member =>
                    DeclarationToken(member) is { } token
                    && initializerIndex.TryGetValue(token, out int index)
                        ? index
                        : 0),
        ];
    }

    static Dictionary<int, AccessorOwner> BuildAccessorOwners(
        IReadOnlyList<ApiMember> members)
    {
        var owners = new Dictionary<int, AccessorOwner>();
        foreach (ApiMember member in members)
        {
            Add(member.GetterToken, CSharpStructuredBodyRole.Getter);
            Add(
                member.SetterToken,
                member.SignatureModel?.Accessors.Any(
                    static accessor => accessor.Kind == "init") == true
                    ? CSharpStructuredBodyRole.Init
                    : CSharpStructuredBodyRole.Setter);
            Add(member.AdderToken, CSharpStructuredBodyRole.Adder);
            Add(member.RemoverToken, CSharpStructuredBodyRole.Remover);

            void Add(int? token, CSharpStructuredBodyRole role)
            {
                if (token is { } value)
                    owners.Add(value, new(member, role));
            }
        }
        return owners;
    }

    static Dictionary<int, (ApiMember Declaration, int DeclarationId)>
        BuildBackingOwners(IReadOnlyList<ApiMember> members)
    {
        var owners =
            new Dictionary<int, (ApiMember Declaration, int DeclarationId)>();
        for (int declarationId = 0;
            declarationId < members.Count;
            declarationId++)
        {
            ApiMember member = members[declarationId];
            if (member.BackingStorage is not
                {
                    State: ApiBackingStorageState.Associated,
                    Candidates.Length: 1,
                } backing)
            {
                continue;
            }
            owners.TryAdd(
                backing.Candidates[0].FieldToken,
                (member, declarationId));
        }
        return owners;
    }

    static CSharpTypeArtifactRepresentation? FieldRepresentation(
        MetadataReader reader,
        ApiType type,
        FieldDefinition field,
        int token,
        IReadOnlyDictionary<int, int> declarationIdByToken,
        IReadOnlyDictionary<
            int,
            (ApiMember Declaration, int DeclarationId)> backingOwners)
    {
        string name = reader.GetString(field.Name);
        if (type.Kind == "enum" && name == "value__")
        {
            return new(
                CSharpTypeArtifactRepresentationKind.TypeFrame,
                CSharpTypeArtifactRole.EnumStorage);
        }
        if (backingOwners.TryGetValue(token, out var owner))
        {
            return new(
                CSharpTypeArtifactRepresentationKind.Declaration,
                CSharpTypeArtifactRole.BackingStorage,
                owner.DeclarationId);
        }
        if (declarationIdByToken.TryGetValue(token, out int declarationId))
        {
            return new(
                CSharpTypeArtifactRepresentationKind.Declaration,
                CSharpTypeArtifactRole.Declaration,
                declarationId);
        }
        return null;
    }

    static CSharpTypeArtifactRepresentation? MethodRepresentation(
        ApiType type,
        int token,
        IReadOnlyDictionary<int, int> declarationIdByToken,
        IReadOnlyDictionary<int, AccessorOwner> accessorOwners)
    {
        if (type.Kind == "delegate")
        {
            return new(
                CSharpTypeArtifactRepresentationKind.TypeFrame,
                CSharpTypeArtifactRole.DelegateSignature);
        }
        if (accessorOwners.TryGetValue(token, out AccessorOwner? owner))
        {
            return new(
                CSharpTypeArtifactRepresentationKind.Declaration,
                ArtifactRole(owner.Role),
                declarationIdByToken[DeclarationToken(owner.Declaration)!.Value]);
        }
        if (declarationIdByToken.TryGetValue(token, out int declarationId))
        {
            return new(
                CSharpTypeArtifactRepresentationKind.Declaration,
                CSharpTypeArtifactRole.Declaration,
                declarationId);
        }
        return null;
    }

    static List<CSharpStructuredMemberRequest> BuildMemberRequests(
        MetadataSource source,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        ApiType type,
        IReadOnlyList<ApiMember> members,
        IReadOnlyList<BodyBuild> bodies,
        IReadOnlyDictionary<int, FieldInitializerBuild> initializers,
        PrinterOptions? printerOptions)
    {
        var bodyByToken = bodies.ToDictionary(static body => body.Token);
        var requests = new List<CSharpStructuredMemberRequest>(members.Count);
        foreach (ApiMember member in members)
        {
            if (member.Kind == "field")
            {
                CSharpFieldInitializer? initializer =
                    DeclarationToken(member) is { } token
                    && initializers.TryGetValue(
                        token,
                        out FieldInitializerBuild? value)
                        ? new(value.Text)
                        : member.EnumValueLiteral is { } enumValue
                            ? new(enumValue)
                            : member.ConstantValueLiteral is { } constantValue
                                ? new(constantValue)
                                : null;
                requests.Add(new(
                    member,
                    initializer is null
                        ? CSharpBodyPolicy.Skeleton
                        : CSharpBodyPolicy.Full,
                    initializer,
                    []));
                continue;
            }

            ImmutableArray<CSharpStructuredBodyBinding> bindings =
                [.. Bindings(member).Select(binding => binding with
                {
                    HasBodyEvidence = bodyByToken[binding.MethodToken].Outcome
                        == CSharpTypeBodyOutcome.Available,
                    HasManagedBody = bodyByToken[binding.MethodToken].HasManagedBody,
                })];
            if (IsPropertyMember(member))
            {
                FieldInitializerBuild? initializer =
                    DeclarationToken(member) is { } token
                    && initializers.TryGetValue(
                        token,
                        out FieldInitializerBuild? value)
                        ? value
                        : null;
                requests.Add(PropertyRequest(
                    source,
                    reader,
                    typeHandle,
                    member,
                    bindings,
                    bodyByToken,
                    initializer));
                continue;
            }
            if (IsEventMember(member))
            {
                FieldInitializerBuild? initializer =
                    DeclarationToken(member) is { } token
                    && initializers.TryGetValue(
                        token,
                        out FieldInitializerBuild? value)
                        ? value
                        : null;
                requests.Add(EventRequest(
                    member,
                    bindings,
                    bodyByToken,
                    initializer));
                continue;
            }

            BodyBuild? body = member.MetadataToken is { } methodToken
                ? bodyByToken[methodToken]
                : null;
            CSharpBlockBody? block = body?.Result?.Body
                ?? (body?.HasManagedBody == true
                    ? new CSharpBlockBody("throw null;")
                    : null);
            requests.Add(new(
                member,
                block is null
                    ? CSharpBodyPolicy.Skeleton
                    : CSharpBodyPolicy.Full,
                block,
                bindings));
        }
        return requests;
    }

    static CSharpStructuredMemberRequest PropertyRequest(
        MetadataSource source,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        ApiMember member,
        ImmutableArray<CSharpStructuredBodyBinding> bindings,
        IReadOnlyDictionary<int, BodyBuild> bodies,
        FieldInitializerBuild? initializer)
    {
        MethodDefinitionHandle? getter = ResolveMethodHandle(
            reader,
            typeHandle,
            member.GetterToken);
        MethodDefinitionHandle? setter = ResolveMethodHandle(
            reader,
            typeHandle,
            member.SetterToken);
        bool automatic = IsCompilerGeneratedAutoProperty(
            source,
            reader,
            typeHandle,
            member,
            getter,
            setter,
            out _);
        CSharpAccessorBody? getterBody = Accessor(
            member.GetterToken,
            automatic,
            bodies);
        CSharpAccessorBody? setterBody = Accessor(
            member.SetterToken,
            automatic,
            bodies);
        bool anyFull = getterBody is not null
            || setterBody is not null
            || initializer is not null;
        return new(
            member,
            anyFull
                ? CSharpBodyPolicy.Full
                : CSharpBodyPolicy.Skeleton,
            anyFull
                ? new CSharpPropertyBody(getterBody, setterBody)
                {
                    Initializer = initializer?.Text,
                    RequiresUnsafeModifier =
                        RequiresUnsafeModifier(member.GetterToken, bodies)
                        || RequiresUnsafeModifier(member.SetterToken, bodies),
                }
                : null,
            bindings);
    }

    static CSharpStructuredMemberRequest EventRequest(
        ApiMember member,
        ImmutableArray<CSharpStructuredBodyBinding> bindings,
        IReadOnlyDictionary<int, BodyBuild> bodies,
        FieldInitializerBuild? initializer)
    {
        bool fieldLike = member.BackingStorage is
        {
            State: ApiBackingStorageState.Associated,
            Candidates.Length: 1,
        };
        if (fieldLike)
        {
            return initializer is null
                ? new(
                    member,
                    CSharpBodyPolicy.Skeleton,
                    Body: null,
                    bindings)
                : new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpFieldInitializer(initializer.Text),
                    bindings);
        }
        CSharpAccessorBody? adder = Accessor(
            member.AdderToken,
            automatic: false,
            bodies);
        CSharpAccessorBody? remover = Accessor(
            member.RemoverToken,
            automatic: false,
            bodies);
        return adder is null || remover is null
            ? new(
                member,
                CSharpBodyPolicy.Skeleton,
                Body: null,
                bindings)
            : new(
                member,
                CSharpBodyPolicy.Full,
                new CSharpEventBody(adder, remover)
                {
                    RequiresUnsafeModifier =
                        RequiresUnsafeModifier(member.AdderToken, bodies)
                        || RequiresUnsafeModifier(member.RemoverToken, bodies),
                },
                bindings);
    }

    static bool RequiresUnsafeModifier(
        int? token,
        IReadOnlyDictionary<int, BodyBuild> bodies)
        => token is { } value
            && bodies[value].Result?.Body?.RequiresUnsafeModifier == true;

    static CSharpAccessorBody? Accessor(
        int? token,
        bool automatic,
        IReadOnlyDictionary<int, BodyBuild> bodies)
    {
        if (token is not { } value)
            return null;
        if (automatic)
            return CSharpAccessorBody.Auto;
        if (bodies[value].Result is not
            {
                Status: MemberBodyProductionStatus.Complete,
                Body: { } body,
            } result)
        {
            return bodies[value].HasManagedBody
                ? CSharpAccessorBody.Throw
                : null;
        }
        return result.SingleLineExpression is { } expression
            ? CSharpAccessorBody.Expression(expression)
            : CSharpAccessorBody.Block(body.Source);
    }

    static ImmutableArray<CSharpStructuredBodyBinding> Bindings(
        ApiMember member)
    {
        var bindings =
            ImmutableArray.CreateBuilder<CSharpStructuredBodyBinding>();
        Add(member.MetadataToken, CSharpStructuredBodyRole.Method);
        Add(member.GetterToken, CSharpStructuredBodyRole.Getter);
        Add(
            member.SetterToken,
            member.SignatureModel?.Accessors.Any(
                static accessor => accessor.Kind == "init") == true
                ? CSharpStructuredBodyRole.Init
                : CSharpStructuredBodyRole.Setter);
        Add(member.AdderToken, CSharpStructuredBodyRole.Adder);
        Add(member.RemoverToken, CSharpStructuredBodyRole.Remover);
        return bindings.ToImmutable();

        void Add(int? token, CSharpStructuredBodyRole role)
        {
            if (token is { } value)
                bindings.Add(new(value, role));
        }
    }

    static InitializerCollection? CollectInitializers(
        IReadOnlyList<ApiMember> members,
        IReadOnlyList<BodyBuild> bodies,
        IReadOnlyDictionary<int, int> bodyIdByToken,
        out string? failure)
    {
        failure = null;
        string? localFailure = null;
        var declarationsByStorageName =
            new Dictionary<string, int>(StringComparer.Ordinal);
        var propertyTokens = members.Where(IsPropertyMember)
            .Select(DeclarationToken).OfType<int>().ToHashSet();
        foreach (ApiMember member in members)
        {
            if (DeclarationToken(member) is not { } declarationToken)
                continue;
            if (member.Kind == "field"
                && !AddStorage(member.Name, declarationToken))
            {
                failure = localFailure;
                return null;
            }
            if (member.BackingStorage is
                {
                    State: ApiBackingStorageState.Associated,
                    Candidates: [var backing],
                }
                && !AddStorage(backing.MatchedName, declarationToken))
            {
                failure = localFailure;
                return null;
            }
        }

        var result = new Dictionary<int, FieldInitializerBuild>();
        var declarationOrder = new List<int>();
        var orderByPlacement =
            new Dictionary<bool, ImmutableArray<int>>();
        var memberByMethodToken = members
            .Where(static member => member.MetadataToken is not null)
            .ToDictionary(
                static member => member.MetadataToken!.Value);
        foreach (BodyBuild body in bodies)
        {
            if (body.Result?.Projection.FieldInitializers is not { Count: > 0 }
                initializers)
            {
                continue;
            }
            var bodyDeclarationOrder =
                ImmutableArray.CreateBuilder<int>(initializers.Count);
            foreach ((string name, string text) in initializers)
            {
                if (!declarationsByStorageName.TryGetValue(
                        name,
                        out int declarationToken))
                {
                    failure =
                        $"Lifted initializer storage '{name}' has no exact logical declaration.";
                    return null;
                }
                bodyDeclarationOrder.Add(declarationToken);
                int bodyId = bodyIdByToken[body.Token];
                if (result.TryGetValue(declarationToken, out var previous))
                {
                    if (previous.Text != text)
                    {
                        failure =
                            $"Declaration 0x{declarationToken:X8} has conflicting lifted initializer values.";
                        return null;
                    }
                    result[declarationToken] = previous with
                    {
                        BodyIds = previous.BodyIds.Add(bodyId),
                    };
                }
                else
                {
                    result.Add(declarationToken, new(
                        text,
                        [bodyId],
                        propertyTokens.Contains(declarationToken)
                            ? CSharpTypeBodyContributionRole.PropertyInitializer
                            : CSharpTypeBodyContributionRole.FieldInitializer));
                }
            }
            if (!memberByMethodToken.TryGetValue(
                    body.Token,
                    out ApiMember? constructor)
                || constructor.Kind != "constructor")
            {
                failure =
                    $"Physical body 0x{body.Token:X8} contributes initializers but is not a constructor.";
                return null;
            }
            ImmutableArray<int> observedOrder =
                bodyDeclarationOrder.ToImmutable();
            if (orderByPlacement.TryGetValue(
                    constructor.IsStatic,
                    out ImmutableArray<int> existingOrder))
            {
                if (!observedOrder.SequenceEqual(existingOrder))
                {
                    failure = constructor.IsStatic
                        ? "Static constructor initializers do not have one declaration order."
                        : "Instance constructor initializers do not have one declaration order.";
                    return null;
                }
            }
            else
            {
                orderByPlacement.Add(
                    constructor.IsStatic,
                    observedOrder);
                declarationOrder.AddRange(observedOrder);
            }
        }
        return new(result, [.. declarationOrder]);

        bool AddStorage(string name, int declarationToken)
        {
            if (!declarationsByStorageName.TryGetValue(
                    name,
                    out int existing))
            {
                declarationsByStorageName.Add(name, declarationToken);
                return true;
            }
            if (existing == declarationToken)
                return true;
            localFailure =
                $"Lifted initializer storage '{name}' maps to more than one logical declaration.";
            return false;
        }
    }

    static ImmutableArray<CSharpTypeDeclaration> BuildDeclarations(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        CSharpStructuredTypePlan plan,
        IReadOnlyList<ApiMember> members,
        IReadOnlyDictionary<int, int> bodyIdByToken,
        IReadOnlyDictionary<int, FieldInitializerBuild> initializers)
    {
        var declarations =
            ImmutableArray.CreateBuilder<CSharpTypeDeclaration>();
        for (int id = 0; id < plan.Declarations.Length; id++)
        {
            CSharpStructuredDeclarationPlan planned =
                plan.Declarations[id];
            ApiMember member = members[id];
            int token = DeclarationToken(member)
                ?? throw new InvalidOperationException(
                    $"Declaration '{member.Name}' has no metadata token.");
            MemberAnchor anchor = DeclarationAnchor(
                reader,
                typeHandle,
                token);
            FieldInitializerBuild? initializer =
                initializers.TryGetValue(token, out FieldInitializerBuild? value)
                    ? value
                    : null;
            declarations.Add(new(
                id,
                id,
                anchor,
                token,
                DeclarationKind(member),
                Accessibility(member.Accessibility),
                member.IsStatic
                    ? CSharpTypeDeclarationPlacement.Static
                    : CSharpTypeDeclarationPlacement.Instance,
                member.IsCompilerGenerated
                    ? CSharpTypeOrigin.Generated
                    : CSharpTypeOrigin.NonGenerated,
                ConvertParts(
                    planned.Parts,
                    bodyIdByToken,
                    initializer)));
        }
        return declarations.ToImmutable();

    }

    static MemberAnchor DeclarationAnchor(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        int token)
    {
        int budget = MetadataSafetyPolicy.MaxAnchorSignatureWorkChars;
        return (token >> 24) switch
        {
            0x04 => ApiMemberIdentity.CreateFieldAnchor(
                reader,
                typeHandle,
                reader.GetFieldDefinition(
                    MetadataTokens.FieldDefinitionHandle(
                        token & 0x00FFFFFF)),
                ref budget),
            0x06 => ApiMemberIdentity.CreateMethodAnchor(
                reader,
                typeHandle,
                reader.GetMethodDefinition(
                    MetadataTokens.MethodDefinitionHandle(
                        token & 0x00FFFFFF))),
            0x14 => ApiMemberIdentity.CreateEventAnchor(
                reader,
                typeHandle,
                reader.GetEventDefinition(
                    MetadataTokens.EventDefinitionHandle(
                        token & 0x00FFFFFF)),
                ref budget),
            0x17 => ApiMemberIdentity.CreatePropertyAnchor(
                reader,
                typeHandle,
                reader.GetPropertyDefinition(
                    MetadataTokens.PropertyDefinitionHandle(
                        token & 0x00FFFFFF)),
                ref budget),
            _ => throw new InvalidOperationException(
                $"Declaration token 0x{token:X8} has an unsupported table."),
        };
    }

    static ImmutableArray<CSharpTypeRenderPart> ConvertParts(
        ImmutableArray<CSharpStructuredPart> parts,
        IReadOnlyDictionary<int, int> bodyIdByToken,
        FieldInitializerBuild? initializer)
    {
        var converted = ImmutableArray.CreateBuilder<CSharpTypeRenderPart>();
        for (int id = 0; id < parts.Length; id++)
        {
            CSharpStructuredPart part = parts[id];
            ImmutableArray<CSharpTypeOwnedBodyReference> owned =
                part.Bodies.IsDefault
                    ? []
                    : [
                        .. part.Bodies.Select(body =>
                            new CSharpTypeOwnedBodyReference(
                                bodyIdByToken[body.Binding.MethodToken],
                                body.FullRange,
                                HasDrillDownDestination: body.FullRange.Length > 0)),
                    ];
            ImmutableArray<CSharpTypeBodyContribution> contributions =
                initializer is not null
                    && part.ImplementationKind
                        == CSharpStructuredImplementationKind.Initializer
                    ? [
                        .. initializer.BodyIds.Select(bodyId => new CSharpTypeBodyContribution(
                            bodyId,
                            initializer.Role,
                            new(
                                3,
                                initializer.Text.Length))),
                    ]
                    : [];
            converted.Add(new(
                id,
                part.Kind switch
                {
                    CSharpStructuredPartKind.Attributes =>
                        CSharpTypeRenderPartKind.Attributes,
                    CSharpStructuredPartKind.Implementation =>
                        CSharpTypeRenderPartKind.Implementation,
                    _ => CSharpTypeRenderPartKind.Fixed,
                },
                part.Kind switch
                {
                    CSharpStructuredPartKind.Attributes =>
                        CSharpTypeRegionRole.Attributes,
                    CSharpStructuredPartKind.Implementation =>
                        CSharpTypeRegionRole.Implementation,
                    _ => CSharpTypeRegionRole.Signature,
                },
                part.FullText,
                part.SkeletonText,
                part.ImplementationKind switch
                {
                    CSharpStructuredImplementationKind.Body =>
                        CSharpTypeImplementationKind.Body,
                    CSharpStructuredImplementationKind.Initializer =>
                        CSharpTypeImplementationKind.Initializer,
                    CSharpStructuredImplementationKind.ImplicitAccessors =>
                        CSharpTypeImplementationKind.ImplicitAccessors,
                    _ => null,
                },
                owned,
                contributions));
        }
        return converted.ToImmutable();
    }

    static CSharpTypeArtifactRepresentation ResolveRepresentationTargets(
        CSharpTypeArtifactRepresentation representation,
        IReadOnlyDictionary<int, int> bodyIdByToken)
        => representation.Kind
                == CSharpTypeArtifactRepresentationKind.PhysicalBody
            ? representation with
            {
                TargetId =
                    bodyIdByToken[representation.TargetId!.Value],
            }
            : representation;

    static ImmutableArray<ApiType> ContainingTypes(
        ApiSurface surface,
        ApiType type)
    {
        if (type.DefinitionName is not { Segments.Length: > 1 } name)
            return [];
        var containing = ImmutableArray.CreateBuilder<ApiType>();
        for (int length = 1; length < name.Segments.Length; length++)
        {
            string[] prefix = [.. name.Segments.Take(length)];
            ApiType? candidate = surface.Types.SingleOrDefault(value =>
                value.DefinitionName is { } definition
                && definition.Namespace == name.Namespace
                && definition.Segments.SequenceEqual(prefix));
            if (candidate is null)
            {
                throw new InvalidOperationException(
                    "Complete same-reader extraction omitted a containing Type.");
            }
            containing.Add(candidate);
        }
        return containing.ToImmutable();
    }

    static BodyBuild ReprintBodyWithQualifiedTypeNames(
        BodyBuild body,
        PrinterOptions? printerOptions)
    {
        if (body.Result is not
            {
                IsComplete: true,
                RaisedFunction: { } function,
            } original)
        {
            return body;
        }

        DecompilerResult projection = Pipeline.CSharpPrinter.Print(
            function,
            printerOptions,
            fullyQualifyTypeNames: true);
        if (projection.Output is null)
        {
            var failed = new MemberBodyProductionResult(
                MemberBodyProductionStatus.Failed,
                Body: null,
                projection)
            {
                RaisedFunction = function,
            };
            return body with
            {
                Outcome = CSharpTypeBodyOutcome.Failed,
                Fidelity = DecompilationFidelity.Failed,
                Diagnostics = [.. projection.Diagnostics],
                Result = failed,
            };
        }

        IReadOnlyList<DecompilerDecision> styleLenses =
        [
            .. original.Projection.Decisions.Where(
                decision => decision.Category
                    == DecompilerDecisionCategories.StyleLens),
        ];
        projection = projection with
        {
            Metadata = projection.Metadata with
            {
                Decisions =
                [
                    .. projection.Decisions,
                    .. styleLenses,
                ],
            },
            Trace = original.Projection.Trace,
        };
        var initializer = projection.ConstructorChain is { } chain
            ? CSharpFormatter.ParseConstructorInitializer(chain)
            : null;
        var renderedBody = new CSharpBlockBody(
            projection.Output.TrimEnd(),
            initializer)
        {
            RequiresAsyncModifier =
                projection.RequiresAsyncBodyModifier,
            RequiresUnsafeModifier =
                projection.RequiresUnsafeBodyModifier,
            ParameterNames = projection.ParameterNames,
            SuppressDestructorSyntax =
                !projection.BodyIsDestructor,
        };
        var result = new MemberBodyProductionResult(
            MemberBodyProductionStatus.Complete,
            renderedBody,
            projection)
        {
            RaisedFunction = function,
            SingleLineExpression =
                CSharpExpressionBody.FromSingleStatement(
                    renderedBody.Source),
        };
        return body with
        {
            Fidelity = projection.Fidelity,
            Diagnostics = [.. projection.Diagnostics],
            Result = result,
        };
    }

    static string RenderingPolicy(PrinterOptions? options)
        => $"csharp-type-document-v1:{options ?? PrinterOptions.Default}";

    static CSharpTypeOrigin Origin(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
        => AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.CompilerGeneratedAttribute)
            ? CSharpTypeOrigin.Generated
            : CSharpTypeOrigin.NonGenerated;

    static CSharpTypeArtifactRole ArtifactRole(
        CSharpStructuredBodyRole role)
        => role switch
        {
            CSharpStructuredBodyRole.Getter =>
                CSharpTypeArtifactRole.Getter,
            CSharpStructuredBodyRole.Setter =>
                CSharpTypeArtifactRole.Setter,
            CSharpStructuredBodyRole.Init =>
                CSharpTypeArtifactRole.Init,
            CSharpStructuredBodyRole.Adder =>
                CSharpTypeArtifactRole.Adder,
            CSharpStructuredBodyRole.Remover =>
                CSharpTypeArtifactRole.Remover,
            _ => CSharpTypeArtifactRole.Declaration,
        };

    static CSharpTypeBodyRole BodyRole(
        CSharpStructuredBodyRole role)
        => role switch
        {
            CSharpStructuredBodyRole.Getter =>
                CSharpTypeBodyRole.Getter,
            CSharpStructuredBodyRole.Setter =>
                CSharpTypeBodyRole.Setter,
            CSharpStructuredBodyRole.Init =>
                CSharpTypeBodyRole.Init,
            CSharpStructuredBodyRole.Adder =>
                CSharpTypeBodyRole.Adder,
            CSharpStructuredBodyRole.Remover =>
                CSharpTypeBodyRole.Remover,
            _ => CSharpTypeBodyRole.Method,
        };

    static CSharpTypeDeclarationKind DeclarationKind(ApiMember member)
        => member.Kind switch
        {
            "field" when member.EnumValue is not null =>
                CSharpTypeDeclarationKind.EnumValue,
            "field" => CSharpTypeDeclarationKind.Field,
            "constructor" => CSharpTypeDeclarationKind.Constructor,
            "finalizer" => CSharpTypeDeclarationKind.Finalizer,
            "operator" => CSharpTypeDeclarationKind.Operator,
            "property" => CSharpTypeDeclarationKind.Property,
            "event" => CSharpTypeDeclarationKind.Event,
            "explicit-interface-implementation"
                when member.SignatureModel?.Accessors.Any(
                    static accessor =>
                        accessor.Kind is "get" or "set" or "init") == true =>
                CSharpTypeDeclarationKind.Property,
            "explicit-interface-implementation"
                when member.SignatureModel?.Accessors.Any(
                    static accessor =>
                        accessor.Kind is "add" or "remove") == true =>
                CSharpTypeDeclarationKind.Event,
            _ => CSharpTypeDeclarationKind.Method,
        };

    static bool IsPropertyMember(ApiMember member)
        => member.Kind == "property"
            || member.Kind == "explicit-interface-implementation"
                && member.SignatureModel?.Accessors is { Count: > 0 } accessors
                && accessors.All(static accessor =>
                    accessor.Kind is "get" or "set" or "init");

    static bool IsEventMember(ApiMember member)
        => member.Kind == "event"
            || member.Kind == "explicit-interface-implementation"
                && member.SignatureModel?.Accessors is { Count: > 0 } accessors
                && accessors.All(static accessor =>
                    accessor.Kind is "add" or "remove");

    static bool CanOmitUnavailableConstructorInitializer(
        ApiType type,
        MetadataReader reader,
        TypeDefinition definition)
    {
        if (type.Kind != "class" || definition.BaseType.IsNil)
            return true;
        TypeRef baseType = definition.BaseType.Kind switch
        {
            HandleKind.TypeDefinition =>
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)definition.BaseType,
                    0),
            HandleKind.TypeReference =>
                TypeRefDecoder.Instance.GetTypeFromReference(
                    reader,
                    (TypeReferenceHandle)definition.BaseType,
                    0),
            _ => TypeRef.Unsupported(
                $"base type handle kind {definition.BaseType.Kind}"),
        };
        return MemberIdentity.IsCoreLibraryType(
            baseType,
            "System",
            "Object");
    }

    static CSharpTypeAccessibility Accessibility(string? value)
        => value switch
        {
            null => CSharpTypeAccessibility.Public,
            "private" => CSharpTypeAccessibility.Private,
            "private protected" =>
                CSharpTypeAccessibility.PrivateProtected,
            "protected" => CSharpTypeAccessibility.Protected,
            "internal" => CSharpTypeAccessibility.Internal,
            "protected internal" =>
                CSharpTypeAccessibility.ProtectedInternal,
            "public" => CSharpTypeAccessibility.Public,
            _ => CSharpTypeAccessibility.Unknown,
        };

    sealed record ArtifactBuild(
        int Token,
        CSharpTypeArtifactKind Kind,
        MemberAnchor Anchor,
        CSharpTypeOrigin Origin,
        CSharpTypeArtifactRepresentation Representation);

    sealed record BodyBuild(
        int Token,
        CSharpTypeBodyRole Role,
        bool HasManagedBody,
        CSharpTypeBodyOutcome Outcome,
        DecompilationFidelity? Fidelity,
        string Fingerprint,
        ImmutableArray<DecompilerDiagnostic> Diagnostics,
        MemberBodyProductionResult? Result);

    sealed record AccessorOwner(
        ApiMember Declaration,
        CSharpStructuredBodyRole Role);

    sealed record FieldInitializerBuild(
        string Text,
        ImmutableArray<int> BodyIds,
        CSharpTypeBodyContributionRole Role);

    sealed record InitializerCollection(
        IReadOnlyDictionary<int, FieldInitializerBuild> Initializers,
        ImmutableArray<int> DeclarationOrder);
}
