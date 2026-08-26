using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using Fixtures =
    ILInspector.Metadata.StateMachineFixtures.StateMachineFixtures;

namespace ILInspector.Metadata.Tests;

public sealed class StateMachineRelationshipIndexTests
{
    [Theory]
    [InlineData(
        nameof(Fixtures.ClassicAsync),
        StateMachineClaimKind.ClassicAsync,
        StateMachineMethodRole.MoveNext,
        StateMachineMethodRole.SetStateMachine)]
    [InlineData(
        nameof(Fixtures.AsyncIterator),
        StateMachineClaimKind.AsyncIterator,
        StateMachineMethodRole.MoveNext,
        StateMachineMethodRole.SetStateMachine,
        StateMachineMethodRole.MoveNextAsync,
        StateMachineMethodRole.DisposeAsync)]
    [InlineData(
        nameof(Fixtures.Iterator),
        StateMachineClaimKind.Iterator,
        StateMachineMethodRole.MoveNext,
        StateMachineMethodRole.Dispose)]
    public void
        StateMachineRelationshipIndex_ResolvesExactInterfaceImplementations(
            string kickoffName,
            StateMachineClaimKind expectedKind,
            params StateMachineMethodRole[] expectedRoles)
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle kickoff =
            FindMethod(reader, kickoffName);

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                index.GetByKickoff(kickoff));

        Assert.Equal(expectedKind, result.Relationship.Kind);
        Assert.Equal(
            expectedRoles,
            result.Relationship.Methods
                .Select(method => method.Role));
        Assert.Equal(
            MetadataTokens.GetToken(kickoff),
            result.Relationship.Kickoff.Token);
        Assert.True(
            result.Relationship.StateMachineType.TryResolve(
                reader,
                out TypeDefinitionHandle stateMachineType));
        var stateMachineName =
            Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
                MetadataTypeDefinitionName.Read(
                    reader,
                    stateMachineType));
        Assert.Equal(
            stateMachineName.Name,
            result.Relationship.StateMachineName);
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByStateMachine(stateMachineType));
        foreach (StateMachineMethodRelationship method
            in result.Relationship.Methods)
        {
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                index.GetByImplementation(method.Method.Handle));
        }
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_ExplicitMethodImplWinsOverNamedDecoy()
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle kickoff =
            FindMethod(
                reader,
                nameof(Fixtures.ExplicitAsync));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                index.GetByKickoff(kickoff));

        Assert.True(result.Relationship.TryGetMethod(
            StateMachineMethodRole.MoveNext,
            out var moveNext));
        MethodDefinition method =
            reader.GetMethodDefinition(moveNext.Handle);
        Assert.NotEqual("MoveNext", reader.GetString(method.Name));
        Assert.DoesNotContain(
            result.Relationship.Methods,
            candidate =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(
                        candidate.Method.Handle).Name,
                    "MoveNext"));
    }

    [Theory]
    [InlineData("<AsyncLambda>b__", StateMachineClaimKind.ClassicAsync)]
    [InlineData("<AsyncLocalFunction>g__", StateMachineClaimKind.ClassicAsync)]
    [InlineData("CustomBuilderAsync", StateMachineClaimKind.ClassicAsync)]
    [InlineData("GenericAsync", StateMachineClaimKind.ClassicAsync)]
    [InlineData("InstanceAsync", StateMachineClaimKind.ClassicAsync)]
    [InlineData(
        "IExplicitGenericStateMachines<System.String,System.Int32>.GetAsync",
        StateMachineClaimKind.ClassicAsync)]
    [InlineData(
        "IExplicitGenericStateMachines<System.String,System.Int32>.get_Items",
        StateMachineClaimKind.Iterator)]
    public void
        StateMachineRelationshipIndex_ResolvesGeneratedAndCustomBuilderKickoffs(
            string kickoffNameFragment,
            StateMachineClaimKind expectedKind)
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle kickoff =
            FindMethodContaining(
                reader,
                kickoffNameFragment);

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                index.GetByKickoff(kickoff));

        Assert.Equal(expectedKind, result.Relationship.Kind);
        Assert.Equal(
            expectedKind == StateMachineClaimKind.Iterator
                ?
                [
                    StateMachineMethodRole.MoveNext,
                    StateMachineMethodRole.Dispose,
                ]
                :
                [
                    StateMachineMethodRole.MoveNext,
                    StateMachineMethodRole.SetStateMachine,
                ],
            result.Relationship.Methods.Select(
                method => method.Role));
    }

    [Fact]
    public void StateMachineRelationshipIndex_AbsentIsNotARejection()
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            FindMethod(reader, nameof(Fixtures.Synchronous));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(reader);

        Assert.IsType<StateMachineRelationshipResult.Absent>(
            index.GetByKickoff(method));
    }

    [Fact]
    public void StateMachineRelationshipIndex_PropagatesTypedBudgetFailure()
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle kickoff =
            FindMethod(reader, nameof(Fixtures.ClassicAsync));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                reader,
                relationshipBudget: 1);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(kickoff));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RejectsMethodTableBeyondScanBudget()
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle kickoff =
            FindMethod(reader, nameof(Fixtures.ClassicAsync));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                reader,
                relationshipBudget: 100,
                methodRowBudget: 1);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(kickoff));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RejectsMalformedTrustedConstructor()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                malformedConstructor: true));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            result.Failure.Kind);
        Assert.Equal(
            "The state-machine attribute constructor is malformed.",
            result.Failure.Detail);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_IgnoresUntrustedAttributeSpoof()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                trustedAttributeAssembly: false));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);

        Assert.IsType<StateMachineRelationshipResult.Absent>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
    }

    [Theory]
    [InlineData(false, StateMachineRelationshipFailureKind.Duplicate)]
    [InlineData(true, StateMachineRelationshipFailureKind.CrossKind)]
    public void
        StateMachineRelationshipIndex_RejectsCompetingKickoffClaims(
            bool crossKind,
            StateMachineRelationshipFailureKind expected)
    {
        StateMachineClaimKind second = crossKind
            ? StateMachineClaimKind.Iterator
            : StateMachineClaimKind.ClassicAsync;
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync, second],
                secondKickoffClaim:
                    StateMachineClaimKind.ClassicAsync));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(expected, result.Failure.Kind);
        Assert.Single(result.Failure.ClaimedTypes);
        Assert.Single(result.Failure.StateMachineCandidates);
        Assert.IsType<StateMachineRelationshipResult.Rejected>(
            index.GetByStateMachine(
                MetadataTokens.TypeDefinitionHandle(3)));
        var otherKickoff =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(2)));
        Assert.Same(result.Failure, otherKickoff.Failure);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_PreservesUnresolvedClaimedType()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                includeStateMachineType: false));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Unresolved,
            result.Failure.Kind);
        var claimed = Assert.Single(result.Failure.ClaimedTypes);
        Assert.Equal("Fixtures", claimed.Namespace);
        Assert.Equal(["Owner", "Machine"], claimed.Segments);
        Assert.Empty(result.Failure.StateMachineCandidates);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RejectsForeignAssemblyClaim()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName:
                    "Fixtures.Owner+Machine, Foreign.Assembly"));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Unresolved,
            result.Failure.Kind);
        Assert.Empty(result.Failure.StateMachineCandidates);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_AcceptsCurrentAssemblyQualification()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName:
                    "Fixtures.Owner+Machine, StateMachineClaims, "
                    + "Version=1.0.0.0, Culture=neutral, "
                    + "PublicKeyToken=null"));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            "The state-machine type does not implement a required interface.",
            result.Failure.Detail);
        Assert.Single(result.Failure.ClaimedTypes);
        Assert.Single(result.Failure.StateMachineCandidates);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RejectsAmbiguousClaimedType()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                duplicateStateMachineType: true));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Ambiguous,
            result.Failure.Kind);
        Assert.Single(result.Failure.ClaimedTypes);
    }

    [Theory]
    [InlineData(false, StateMachineRelationshipFailureKind.Duplicate)]
    [InlineData(true, StateMachineRelationshipFailureKind.CrossKind)]
    public void
        StateMachineRelationshipIndex_RejectsSharedStateMachineClaims(
            bool crossKind,
            StateMachineRelationshipFailureKind expected)
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                secondKickoffClaim: crossKind
                    ? StateMachineClaimKind.Iterator
                    : StateMachineClaimKind.ClassicAsync));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var first =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));
        var second =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(2)));
        var stateMachine =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByStateMachine(
                    MetadataTokens.TypeDefinitionHandle(3)));

        Assert.Equal(expected, first.Failure.Kind);
        Assert.Same(first.Failure, second.Failure);
        Assert.Same(first.Failure, stateMachine.Failure);
        Assert.Equal(2, first.Failure.KickoffCandidates.Length);
        Assert.Single(first.Failure.StateMachineCandidates);
    }

    [Theory]
    [InlineData(
        ClassicRelationshipMutation.CustomModifiedSetStateMachine,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.ValueTypeSetStateMachine,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.StaticMoveNext,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.NonIlMoveNext,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.MethodImplBodyOnOtherType,
        StateMachineRelationshipFailureKind.Malformed)]
    [InlineData(
        ClassicRelationshipMutation.NonIlKickoff,
        StateMachineRelationshipFailureKind.Malformed)]
    public void
        StateMachineRelationshipIndex_RejectsInvalidImplementationShapes(
            ClassicRelationshipMutation mutation,
            StateMachineRelationshipFailureKind expected)
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(mutation));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var kickoff =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));
        var stateMachine =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByStateMachine(
                    MetadataTokens.TypeDefinitionHandle(3)));

        Assert.Equal(expected, kickoff.Failure.Kind);
        Assert.Same(kickoff.Failure, stateMachine.Failure);
        Assert.Single(kickoff.Failure.ClaimedTypes);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RejectsOversizedTypeBeforeDecode()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName: new string(
                    'A',
                    MetadataSafetyPolicy.MaxTypeNameCharacters
                        * 4
                        + 1)));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            result.Failure.Kind);
        Assert.Equal(
            "The state-machine type name exceeds its encoded byte budget.",
            result.Failure.Detail);
    }

    [Theory]
    [InlineData(AsyncEnumeratorShape.Bare)]
    [InlineData(AsyncEnumeratorShape.WrongArity)]
    public void
        StateMachineRelationshipIndex_RejectsMalformedAsyncEnumeratorShape(
            AsyncEnumeratorShape shape)
    {
        using var image = new LoadedImage(
            BuildAsyncIteratorRelationshipImage(shape));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Unresolved,
            result.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_ChargesUnrelatedAttributeRows()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [],
                unrelatedAttributeCount: 2));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 1);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string name,
        int? parameterCount = null)
    {
        foreach (MethodDefinitionHandle handle
            in reader.MethodDefinitions)
        {
            MethodDefinition method =
                reader.GetMethodDefinition(handle);
            if (!reader.StringComparer.Equals(method.Name, name))
                continue;
            if (parameterCount is not null
                && method.GetParameters().Count - 1 != parameterCount)
            {
                continue;
            }
            return handle;
        }

        throw new InvalidOperationException(
            $"Method '{name}' was not found.");
    }

    static MethodDefinitionHandle FindMethodContaining(
        MetadataReader reader,
        string fragment)
    {
        foreach (MethodDefinitionHandle handle
            in reader.MethodDefinitions)
        {
            MethodDefinition method =
                reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name).Contains(
                    fragment,
                    StringComparison.Ordinal))
            {
                return handle;
            }
        }

        throw new InvalidOperationException(
            $"A method containing '{fragment}' was not found.");
    }

    static byte[] BuildClaimImage(
        IReadOnlyList<StateMachineClaimKind> claims,
        bool malformedConstructor = false,
        bool trustedAttributeAssembly = true,
        bool includeStateMachineType = true,
        bool duplicateStateMachineType = false,
        StateMachineClaimKind? secondKickoffClaim = null,
        string serializedTypeName = "Fixtures.Owner+Machine",
        int unrelatedAttributeCount = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("StateMachineClaims.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("StateMachineClaims"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyName coreLibrary =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        BlobHandle publicKeyToken = trustedAttributeAssembly
            ? metadata.GetOrAddBlob(coreLibrary.GetPublicKeyToken()!)
            : default;
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(coreLibrary.Name!),
                coreLibrary.Version!,
                default,
                publicKeyToken,
                default,
                default);
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        int methodSentinelRow =
            secondKickoffClaim is null ? 2 : 3;
        if (includeStateMachineType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("Machine"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    methodSentinelRow));
            metadata.AddNestedType(
                MetadataTokens.TypeDefinitionHandle(3),
                MetadataTokens.TypeDefinitionHandle(2));
        }
        if (duplicateStateMachineType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("Machine"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    methodSentinelRow));
            metadata.AddNestedType(
                MetadataTokens.TypeDefinitionHandle(4),
                MetadataTokens.TypeDefinitionHandle(2));
        }

        MethodDefinitionHandle kickoff =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Kickoff"),
                metadata.GetOrAddBlob(methodSignature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));

        foreach (StateMachineClaimKind kind in claims)
            AddClaimAttribute(kickoff, kind);

        if (secondKickoffClaim is { } secondKind)
        {
            MethodDefinitionHandle secondKickoff =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("SecondKickoff"),
                    metadata.GetOrAddBlob(methodSignature),
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1));
            AddClaimAttribute(secondKickoff, secondKind);
        }

        if (unrelatedAttributeCount > 0)
        {
            TypeReferenceHandle unrelatedType =
                metadata.AddTypeReference(
                    coreReference,
                    metadata.GetOrAddString("Other"),
                    metadata.GetOrAddString("UnrelatedAttribute"));
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    0,
                    returnType => returnType.Void(),
                    parameters => { });
            MemberReferenceHandle constructor =
                metadata.AddMemberReference(
                    unrelatedType,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(signature));
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteUInt16(0);
            BlobHandle valueHandle =
                metadata.GetOrAddBlob(value);
            for (int i = 0; i < unrelatedAttributeCount; i++)
            {
                metadata.AddCustomAttribute(
                    kickoff,
                    constructor,
                    valueHandle);
            }
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();

        void AddClaimAttribute(
            MethodDefinitionHandle owner,
            StateMachineClaimKind kind)
        {
            string attributeName = kind switch
            {
                StateMachineClaimKind.ClassicAsync =>
                    nameof(AsyncStateMachineAttribute),
                StateMachineClaimKind.AsyncIterator =>
                    nameof(AsyncIteratorStateMachineAttribute),
                StateMachineClaimKind.Iterator =>
                    nameof(IteratorStateMachineAttribute),
                _ => throw new InvalidOperationException(),
            };
            TypeReferenceHandle attributeType =
                metadata.AddTypeReference(
                    coreReference,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(attributeName));
            BlobBuilder constructorSignature =
                malformedConstructor
                    ? ValueTypeConstructorSignature(systemType)
                    : TypeConstructorSignature(systemType);
            MemberReferenceHandle constructor =
                metadata.AddMemberReference(
                    attributeType,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(constructorSignature));
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteSerializedString(serializedTypeName);
            value.WriteUInt16(0);
            metadata.AddCustomAttribute(
                owner,
                constructor,
                metadata.GetOrAddBlob(value));
        }
    }

    static byte[] BuildClassicRelationshipImage(
        ClassicRelationshipMutation mutation)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ClassicRelationship.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ClassicRelationship"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyName coreLibrary =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(coreLibrary.Name!),
                coreLibrary.Version!,
                default,
                metadata.GetOrAddBlob(
                    coreLibrary.GetPublicKeyToken()!),
                default,
                default);
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle asyncStateMachine =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IAsyncStateMachine"));
        TypeReferenceHandle asyncAttribute =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    nameof(AsyncStateMachineAttribute)));
        TypeReferenceHandle modifier =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IsReadOnlyAttribute"));

        var staticVoidSignature = new BlobBuilder();
        new BlobEncoder(staticVoidSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        var instanceVoidSignature = new BlobBuilder();
        new BlobEncoder(instanceVoidSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        BlobBuilder setStateMachineSignature =
            mutation switch
            {
                ClassicRelationshipMutation
                    .CustomModifiedSetStateMachine =>
                    CustomModifiedSetStateMachineSignature(
                        asyncStateMachine,
                        modifier),
                ClassicRelationshipMutation
                    .ValueTypeSetStateMachine =>
                    ValueTypeSetStateMachineSignature(
                        asyncStateMachine),
                _ => SetStateMachineSignature(
                    asyncStateMachine),
            };

        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset =
            new MethodBodyStreamEncoder(methodBodies)
                .AddMethodBody(encoder, maxStack: 0);

        bool bodyOnOwner =
            mutation
                == ClassicRelationshipMutation
                    .MethodImplBodyOnOtherType;
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle machine =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("Machine"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    bodyOnOwner ? 3 : 2));
        metadata.AddNestedType(
            machine,
            MetadataTokens.TypeDefinitionHandle(2));
        metadata.AddInterfaceImplementation(
            machine,
            asyncStateMachine);

        MethodDefinitionHandle kickoff =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                mutation
                    == ClassicRelationshipMutation.NonIlKickoff
                        ? MethodImplAttributes.Runtime
                        : MethodImplAttributes.IL,
                metadata.GetOrAddString("Kickoff"),
                metadata.GetOrAddBlob(staticVoidSignature),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodAttributes moveNextAttributes =
            mutation
                == ClassicRelationshipMutation.StaticMoveNext
                ? MethodAttributes.Public
                    | MethodAttributes.Static
                : MethodAttributes.Public
                    | MethodAttributes.Virtual;
        MethodDefinitionHandle moveNext =
            metadata.AddMethodDefinition(
                moveNextAttributes,
                mutation
                    == ClassicRelationshipMutation.NonIlMoveNext
                        ? MethodImplAttributes.Runtime
                        : MethodImplAttributes.IL,
                metadata.GetOrAddString("MoveNext"),
                metadata.GetOrAddBlob(
                    mutation
                        == ClassicRelationshipMutation.StaticMoveNext
                            ? staticVoidSignature
                            : instanceVoidSignature),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("SetStateMachine"),
            metadata.GetOrAddBlob(setStateMachineSignature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        if (bodyOnOwner)
        {
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    asyncStateMachine,
                    metadata.GetOrAddString("MoveNext"),
                    metadata.GetOrAddBlob(
                        instanceVoidSignature));
            metadata.AddMethodImplementation(
                machine,
                moveNext,
                declaration);
        }

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(systemType, isValueType: false));
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                asyncAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixtures.Owner+Machine");
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            kickoff,
            constructor,
            metadata.GetOrAddBlob(value));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildAsyncIteratorRelationshipImage(
        AsyncEnumeratorShape shape)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("AsyncIteratorRelationship.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("AsyncIteratorRelationship"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyName coreLibrary =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(coreLibrary.Name!),
                coreLibrary.Version!,
                default,
                metadata.GetOrAddBlob(
                    coreLibrary.GetPublicKeyToken()!),
                default,
                default);
        TypeReferenceHandle systemType =
            AddTypeReference(
                "System",
                "Type");
        TypeReferenceHandle asyncStateMachine =
            AddTypeReference(
                "System.Runtime.CompilerServices",
                "IAsyncStateMachine");
        TypeReferenceHandle asyncAttribute =
            AddTypeReference(
                "System.Runtime.CompilerServices",
                nameof(AsyncIteratorStateMachineAttribute));
        TypeReferenceHandle asyncEnumerator =
            AddTypeReference(
                "System.Collections.Generic",
                "IAsyncEnumerator`1");
        TypeReferenceHandle asyncDisposable =
            AddTypeReference(
                "System",
                "IAsyncDisposable");
        TypeReferenceHandle valueTask =
            AddTypeReference(
                "System.Threading.Tasks",
                "ValueTask");
        TypeReferenceHandle valueTaskOfT =
            AddTypeReference(
                "System.Threading.Tasks",
                "ValueTask`1");

        var staticVoid = new BlobBuilder();
        new BlobEncoder(staticVoid)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                result => result.Void(),
                parameters => { });
        var instanceVoid = new BlobBuilder();
        new BlobEncoder(instanceVoid)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                result => result.Void(),
                parameters => { });
        BlobBuilder setStateMachine =
            SetStateMachineSignature(asyncStateMachine);
        var moveNextAsync = new BlobBuilder();
        moveNextAsync.WriteByte(0x20);
        moveNextAsync.WriteCompressedInteger(0);
        moveNextAsync.WriteByte(0x15);
        moveNextAsync.WriteByte(0x11);
        WriteTypeDefOrRefEncoded(
            moveNextAsync,
            valueTaskOfT);
        moveNextAsync.WriteCompressedInteger(1);
        moveNextAsync.WriteByte(0x02);
        var disposeAsync = new BlobBuilder();
        disposeAsync.WriteByte(0x20);
        disposeAsync.WriteCompressedInteger(0);
        disposeAsync.WriteByte(0x11);
        WriteTypeDefOrRefEncoded(
            disposeAsync,
            valueTask);

        var asyncEnumeratorSpecification = new BlobBuilder();
        asyncEnumeratorSpecification.WriteByte(0x15);
        asyncEnumeratorSpecification.WriteByte(0x12);
        WriteTypeDefOrRefEncoded(
            asyncEnumeratorSpecification,
            asyncEnumerator);
        asyncEnumeratorSpecification.WriteCompressedInteger(
            shape == AsyncEnumeratorShape.Bare ? 1 : 2);
        asyncEnumeratorSpecification.WriteByte(0x02);
        if (shape == AsyncEnumeratorShape.WrongArity)
            asyncEnumeratorSpecification.WriteByte(0x02);
        EntityHandle implementedAsyncEnumerator =
            shape == AsyncEnumeratorShape.Bare
                ? asyncEnumerator
                : metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        asyncEnumeratorSpecification));

        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset =
            new MethodBodyStreamEncoder(methodBodies)
                .AddMethodBody(encoder, maxStack: 0);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle machine =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("Machine"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(
            machine,
            MetadataTokens.TypeDefinitionHandle(2));
        metadata.AddInterfaceImplementation(
            machine,
            asyncStateMachine);
        metadata.AddInterfaceImplementation(
            machine,
            implementedAsyncEnumerator);
        metadata.AddInterfaceImplementation(
            machine,
            asyncDisposable);

        MethodDefinitionHandle kickoff =
            AddMethod(
                "Kickoff",
                staticVoid,
                MethodAttributes.Public
                    | MethodAttributes.Static);
        AddMethod(
            "MoveNext",
            instanceVoid,
            MethodAttributes.Public
                | MethodAttributes.Virtual);
        AddMethod(
            "SetStateMachine",
            setStateMachine,
            MethodAttributes.Public
                | MethodAttributes.Virtual);
        AddMethod(
            "MoveNextAsync",
            moveNextAsync,
            MethodAttributes.Public
                | MethodAttributes.Virtual);
        AddMethod(
            "DisposeAsync",
            disposeAsync,
            MethodAttributes.Public
                | MethodAttributes.Virtual);

        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                asyncAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    TypeConstructorSignature(systemType)));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixtures.Owner+Machine");
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            kickoff,
            constructor,
            metadata.GetOrAddBlob(value));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();

        TypeReferenceHandle AddTypeReference(
            string @namespace,
            string name) =>
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString(@namespace),
                metadata.GetOrAddString(name));

        MethodDefinitionHandle AddMethod(
            string name,
            BlobBuilder signature,
            MethodAttributes attributes) =>
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
    }

    static BlobBuilder SetStateMachineSignature(
        TypeReferenceHandle asyncStateMachine)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(
                        asyncStateMachine,
                        isValueType: false));
        return signature;
    }

    static BlobBuilder TypeConstructorSignature(
        TypeReferenceHandle systemType)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(systemType, isValueType: false));
        return signature;
    }

    static BlobBuilder ValueTypeConstructorSignature(
        TypeReferenceHandle systemType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x11);
        WriteTypeDefOrRefEncoded(signature, systemType);
        return signature;
    }

    static BlobBuilder ValueTypeSetStateMachineSignature(
        TypeReferenceHandle asyncStateMachine)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x11);
        WriteTypeDefOrRefEncoded(
            signature,
            asyncStateMachine);
        return signature;
    }

    static BlobBuilder CustomModifiedSetStateMachineSignature(
        TypeReferenceHandle asyncStateMachine,
        TypeReferenceHandle modifier)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1F);
        WriteTypeDefOrRefEncoded(signature, modifier);
        signature.WriteByte(0x12);
        WriteTypeDefOrRefEncoded(
            signature,
            asyncStateMachine);
        return signature;
    }

    static void WriteTypeDefOrRefEncoded(
        BlobBuilder signature,
        EntityHandle handle)
    {
        int tag = handle.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(handle)),
        };
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(handle) << 2 | tag);
    }

    public enum ClassicRelationshipMutation
    {
        CustomModifiedSetStateMachine,
        ValueTypeSetStateMachine,
        StaticMoveNext,
        NonIlMoveNext,
        MethodImplBodyOnOtherType,
        NonIlKickoff,
    }

    public enum AsyncEnumeratorShape
    {
        Bare,
        WrongArity,
    }

    sealed class LoadedImage(byte[] image) : IDisposable
    {
        readonly PEReader _reader = new(
            new MemoryStream(image, writable: false));

        internal MetadataReader Reader =>
            _reader.GetMetadataReader();

        public void Dispose() =>
            _reader.Dispose();
    }
}
