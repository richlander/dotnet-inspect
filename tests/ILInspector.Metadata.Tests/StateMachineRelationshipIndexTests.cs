using System.Buffers.Binary;
using System.Collections.Immutable;
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
            result.Relationship.Roles.Select(role => role.Role));
        Assert.All(
            result.Relationship.Roles,
            role => Assert.IsType<
                StateMachineRoleDisposition.Present>(role));
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
        foreach (StateMachineRoleDisposition.Present method
            in result.Relationship.Roles.OfType<
                StateMachineRoleDisposition.Present>())
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
            result.Relationship.Roles.OfType<
                StateMachineRoleDisposition.Present>(),
            candidate =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(
                        candidate.Method.Handle).Name,
                    "MoveNext"));
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_ResolvesClassicAsyncWithAbsentSupportRole()
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.MissingSetStateMachine));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Collection(
            result.Relationship.Roles.OrderBy(role => role.Role),
            role =>
            {
                var present = Assert.IsType<
                    StateMachineRoleDisposition.Present>(role);
                Assert.Equal(
                    StateMachineMethodRole.MoveNext,
                    present.Role);
            },
            role =>
            {
                var absent = Assert.IsType<
                    StateMachineRoleDisposition.AbsentFromArtifact>(role);
                Assert.Equal(
                    StateMachineMethodRole.SetStateMachine,
                    absent.Role);
            });
        Assert.True(
            result.Relationship.TryGetMethod(
                StateMachineMethodRole.MoveNext,
                out var moveNext));
        Assert.False(
            result.Relationship.TryGetMethod(
                StateMachineMethodRole.SetStateMachine,
                out _));
        Assert.IsType<
            StateMachineRoleDisposition.AbsentFromArtifact>(
                result.Relationship.GetRole(
                    StateMachineMethodRole.SetStateMachine));
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByImplementation(moveNext.Handle));
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByStateMachine(
                MetadataTokens.TypeDefinitionHandle(3)));
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
            result.Relationship.Roles.Select(role => role.Role));
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
        StateMachineRelationshipIndex_RelationshipsReportsGlobalFailure()
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
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Rejected>(
                index.Relationships);
        var keyed =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(kickoff));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            relationships.Failure.Kind);
        Assert.Same(relationships.Failure, keyed.Failure);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RelationshipsKeepsSuccessfulEmptyDistinct()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [],
                includeStateMachineType: false));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);

        Assert.Empty(relationships.Relationships);
        Assert.IsType<StateMachineRelationshipResult.Absent>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9999, false)]
    [InlineData(0x10000001, true)]
    public void
        StateMachineRelationshipIndex_InvalidMvidPreservesGlobalFailureForValidHandles(
            int mvidIndex,
            bool largeGuidHeap)
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [],
                includeStateMachineType: false,
                moduleVersionId:
                    MetadataTokens.GuidHandle(mvidIndex),
                largeGuidHeap: largeGuidHeap));
        MetadataReader reader = image.Reader;

        Assert.Equal(
            mvidIndex,
            MetadataTokens.GetHeapOffset(
                reader.GetModuleDefinition().Mvid));
        Assert.Equal(
            largeGuidHeap,
            reader.GetHeapSize(HeapIndex.Guid)
                > ushort.MaxValue);

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Rejected>(
                index.Relationships);
        var kickoff =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));
        var implementation =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByImplementation(
                    MetadataTokens.MethodDefinitionHandle(1)));
        var stateMachine =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByStateMachine(
                    MetadataTokens.TypeDefinitionHandle(2)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            relationships.Failure.Kind);
        Assert.Same(relationships.Failure, kickoff.Failure);
        Assert.Same(relationships.Failure, implementation.Failure);
        Assert.Same(relationships.Failure, stateMachine.Failure);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_PortablePdbReturnsGlobalFailure()
    {
        var metadata = new MetadataBuilder();
        var builder = new PortablePdbBuilder(
            metadata,
            ImmutableArray.Create(new int[64]),
            default);
        var image = new BlobBuilder();
        builder.Serialize(image);
        using MetadataReaderProvider provider =
            MetadataReaderProvider.FromPortablePdbImage(
                image.ToImmutableArray());

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                provider.GetMetadataReader());
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Rejected>(
                index.Relationships);

        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            relationships.Failure.Kind);
    }

    [Theory]
    [InlineData(StateMachineRelationshipFailureKind.Malformed)]
    [InlineData(StateMachineRelationshipFailureKind.BudgetExceeded)]
    public void
        StateMachineRelationshipIndex_RelationshipsDistinguishesFailureScopeFromKind(
            StateMachineRelationshipFailureKind kind)
    {
        byte[] localBytes =
            kind == StateMachineRelationshipFailureKind.Malformed
                ? BuildClaimImage(
                    [StateMachineClaimKind.ClassicAsync],
                    malformedConstructor: true)
                : BuildClaimImage(
                    Enumerable.Repeat(
                            StateMachineClaimKind.ClassicAsync,
                            MetadataSafetyPolicy.MaxRelationshipNodes + 1)
                        .ToArray(),
                    reuseClaimConstructors: true);
        byte[] globalBytes =
            kind == StateMachineRelationshipFailureKind.Malformed
                ? BuildClaimImage(
                    [],
                    includeStateMachineType: false,
                    moduleVersionId:
                        MetadataTokens.GuidHandle(9999))
                : localBytes;
        using var localImage = new LoadedImage(localBytes);
        using var globalImage = new LoadedImage(globalBytes);

        StateMachineRelationshipIndex local =
            StateMachineRelationshipIndex.Create(localImage.Reader);
        StateMachineRelationshipIndex global =
            kind == StateMachineRelationshipFailureKind.BudgetExceeded
                ? StateMachineRelationshipIndex.Create(
                    globalImage.Reader,
                    relationshipBudget: 1)
                : StateMachineRelationshipIndex.Create(
                    globalImage.Reader);
        var localRelationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                local.Relationships);
        var localFailure =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                local.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));
        var globalRelationships =
            Assert.IsType<StateMachineRelationshipsResult.Rejected>(
                global.Relationships);
        var globalFailure =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                global.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Empty(localRelationships.Relationships);
        Assert.Equal(kind, localFailure.Failure.Kind);
        Assert.Equal(kind, globalRelationships.Failure.Kind);
        Assert.Same(
            globalRelationships.Failure,
            globalFailure.Failure);
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
        StateMachineRelationshipIndex_IsolatesMalformedConstructorRow()
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.None,
                addMalformedConstructorRow: true));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        StateMachineRelationship relationship =
            Assert.Single(relationships.Relationships);
        var damaged =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(4)));

        Assert.Equal(0x06000001, relationship.Kickoff.Token);
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            damaged.Failure.Kind);
        Assert.Equal(
            "A custom-attribute constructor could not be read.",
            damaged.Failure.Detail);
        Assert.Equal(
            0x06000004,
            Assert.Single(damaged.Failure.KickoffCandidates).Token);
        Assert.Empty(damaged.Failure.StateMachineCandidates);
        Assert.Empty(damaged.Failure.ClaimedTypes);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_IsolatesReservedConstructorTag()
    {
        byte[] bytes =
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.None,
                addMalformedConstructorRow: true);
        using (var pe = new PEReader(ImmutableArray.Create(bytes)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            int rowSize =
                reader.GetTableRowSize(TableIndex.CustomAttribute);
            int constructorOffset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.CustomAttribute)
                + rowSize
                + sizeof(ushort);
            Span<byte> constructor =
                bytes.AsSpan(constructorOffset, sizeof(ushort));
            Assert.Equal(
                (2 << 3) | 3,
                BinaryPrimitives.ReadUInt16LittleEndian(constructor));
            BinaryPrimitives.WriteUInt16LittleEndian(
                constructor,
                (2 << 3) | 4);
        }

        using var image = new LoadedImage(bytes);
        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);

        Assert.Single(relationships.Relationships);
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
        var damaged =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(4)));
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            damaged.Failure.Kind);
    }

    [Theory]
    [InlineData(ConstructorTypeNameRejection.MissingName)]
    [InlineData(ConstructorTypeNameRejection.NameBudget)]
    public void
        StateMachineRelationshipIndex_IsolatesRejectedConstructorTypeName(
            ConstructorTypeNameRejection rejection)
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.None,
                rejectedConstructorTypeName: rejection));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        StateMachineRelationship relationship =
            Assert.Single(relationships.Relationships);
        var damaged =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(4)));

        Assert.Equal(0x06000001, relationship.Kickoff.Token);
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            damaged.Failure.Kind);
        Assert.Equal(
            "A custom-attribute constructor could not be read.",
            damaged.Failure.Detail);
        Assert.Equal(
            0x06000004,
            Assert.Single(damaged.Failure.KickoffCandidates).Token);
        Assert.Empty(damaged.Failure.StateMachineCandidates);
        Assert.Empty(damaged.Failure.ClaimedTypes);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_MalformedConstructorRowRejectsOwningClaim()
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.None,
                addMalformedConstructorRow: true,
                malformedConstructorOnRelationshipKickoff: true));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        var kickoff =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));
        var stateMachine =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByStateMachine(
                    MetadataTokens.TypeDefinitionHandle(3)));

        Assert.Empty(relationships.Relationships);
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            kickoff.Failure.Kind);
        Assert.Same(kickoff.Failure, stateMachine.Failure);
        Assert.Single(kickoff.Failure.StateMachineCandidates);
        Assert.Single(kickoff.Failure.ClaimedTypes);
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
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 10,
                signatureWorkBudget: 1);

        Assert.IsType<StateMachineRelationshipResult.Absent>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_BoundsCumulativeConstructorSignatures()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                ]));
        CustomAttribute firstAttribute = image.Reader.GetCustomAttribute(
            image.Reader.GetMethodDefinition(
                    MetadataTokens.MethodDefinitionHandle(1))
                .GetCustomAttributes()
                .First());
        MemberReference constructor =
            image.Reader.GetMemberReference(
                (MemberReferenceHandle)firstAttribute.Constructor);
        int oneSignature =
            image.Reader.GetBlobReader(constructor.Signature).Length;

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 10,
                signatureWorkBudget: oneSignature);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
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
        Assert.Equal(2, result.Failure.KickoffCandidates.Length);
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
        Assert.Equal(2, result.Failure.StateMachineCandidates.Length);
        Assert.IsType<StateMachineRelationshipResult.Rejected>(
            index.GetByStateMachine(
                MetadataTokens.TypeDefinitionHandle(3)));
        Assert.IsType<StateMachineRelationshipResult.Rejected>(
            index.GetByStateMachine(
                MetadataTokens.TypeDefinitionHandle(4)));
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

    [Fact]
    public void
        StateMachineRelationshipIndex_MergesEveryOverlappingRejection()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                ],
                additionalKickoffClaims:
                [
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                ]));

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
        var third =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(3)));
        var stateMachine =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByStateMachine(
                    MetadataTokens.TypeDefinitionHandle(3)));

        Assert.Same(first.Failure, second.Failure);
        Assert.Same(first.Failure, third.Failure);
        Assert.Same(first.Failure, stateMachine.Failure);
        Assert.Equal(
            [0x06000001, 0x06000002, 0x06000003],
            first.Failure.KickoffCandidates.Select(
                candidate => candidate.Token));
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_MergesRejectionsWithoutQuadraticRescan()
    {
        const int relationships = 10_000;
        using var image = new LoadedImage(
            BuildRejectionMergeImage(relationships));
        int rejectionWork = 0;

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget:
                    MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
                rejectionWorkObserved: () => rejectionWork++);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Duplicate,
            result.Failure.Kind);
        Assert.Equal(2, result.Failure.KickoffCandidates.Length);
        Assert.InRange(
            rejectionWork,
            relationships * 8,
            relationships * 16);
    }

    [Theory]
    [InlineData(
        ClassicRelationshipMutation.CustomModifiedSetStateMachine,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.ValueTypeSetStateMachine,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.NonIlSetStateMachine,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.ExplicitWrongSignatureSetStateMachine,
        StateMachineRelationshipFailureKind.Unresolved)]
    [InlineData(
        ClassicRelationshipMutation.DuplicateSetStateMachine,
        StateMachineRelationshipFailureKind.Ambiguous)]
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
                        * 3
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
    [InlineData(AsyncEnumeratorShape.CrossConstruction)]
    [InlineData(AsyncEnumeratorShape.ModifiedArgument)]
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

    [Fact]
    public void
        StateMachineRelationshipIndex_BoundsAttributeNameMaterialization()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [],
                unrelatedAttributeCount: 1));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 10,
                nameWorkBudget: 1);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_CachesConstructorAuthentication()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [],
                unrelatedAttributeCount: 100));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 101,
                nameWorkBudget: 64);

        Assert.IsType<StateMachineRelationshipResult.Absent>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_CachesThrownConstructorAuthenticationFailure()
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.None,
                repeatedThrowingConstructorAttributes: 100));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 256,
                signatureWorkBudget: 10_000);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        StateMachineRelationship relationship =
            Assert.Single(relationships.Relationships);
        var damaged =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(4)));

        Assert.Equal(0x06000001, relationship.Kickoff.Token);
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            damaged.Failure.Kind);
        Assert.Equal(
            "A custom-attribute constructor could not be read.",
            damaged.Failure.Detail);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_BoundsCumulativeSerializedTypeNames()
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName:
                    "Fixtures.Owner+" + new string('X', 200),
                additionalKickoffClaims:
                [
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                ],
                reuseClaimConstructors: true));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget: 10,
                nameWorkBudget: 512);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_ReportsTypeDefNameBudget()
    {
        using var image = new LoadedImage(
            BuildTypeDefinitionNameBudgetImage());

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByStateMachine(
                    MetadataTokens.TypeDefinitionHandle(2)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            result.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_ExpandsAmbiguousClaimsOnce()
    {
        const int kickoffs = 4_000;
        const int duplicates = 4_000;
        using var image = new LoadedImage(
            BuildAmbiguousClaimFanOutImage(kickoffs, duplicates));
        int rejectionWork = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();
        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget:
                    MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
                rejectionWorkObserved: () => rejectionWork++);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Duplicate,
            result.Failure.Kind);
        Assert.Equal(
            kickoffs,
            result.Failure.KickoffCandidates.Length);
        Assert.Equal(
            duplicates,
            result.Failure.StateMachineCandidates.Length);
        Assert.InRange(
            rejectionWork,
            kickoffs + duplicates,
            16 * (kickoffs + duplicates));
        Assert.InRange(allocated, 0, 64L * 1024 * 1024);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_RejectsNamedArgumentsBeforeDecode()
    {
        const int kickoffs = 200;
        const int namedValueCharacters = 100_000;
        using var image = new LoadedImage(
            BuildNamedArgumentClaimImage(
                kickoffs,
                namedValueCharacters));

        long before = GC.GetAllocatedBytesForCurrentThread();
        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            result.Failure.Kind);
        Assert.Equal(
            "The state-machine attribute value is malformed.",
            result.Failure.Detail);
        Assert.InRange(
            allocated,
            0,
            (long)kickoffs * namedValueCharacters / 4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void
        StateMachineRelationshipIndex_ChargesUntrustedAssemblyKeyOnce(
            bool chargedOnce)
    {
        const int constructors = 64;
        const int keyBytes = 4_096;
        using var image = new LoadedImage(
            BuildUntrustedAssemblyKeyImage(
                constructors,
                keyBytes,
                referenceRows: 3));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget:
                    MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
                nameWorkBudget: chargedOnce
                    ? 2 * keyBytes
                    : keyBytes - 1);

        if (!chargedOnce)
        {
            var rejected =
                Assert.IsType<StateMachineRelationshipResult.Rejected>(
                    index.GetByKickoff(
                        MetadataTokens.MethodDefinitionHandle(1)));
            Assert.Equal(
                StateMachineRelationshipFailureKind.BudgetExceeded,
                rejected.Failure.Kind);
            return;
        }

        Assert.IsType<StateMachineRelationshipResult.Absent>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
    }

    /// <summary>
    /// Gates that this image's own assembly public key is charged against the
    /// name-work budget and projected at most once, however many claims carry
    /// an assembly qualifier. The <c>false</c> arm fails if the charge is
    /// removed, because an oversized key would then go unnoticed. The
    /// <c>true</c> arm fails if the projection stops being cached, because
    /// four kickoffs would then charge the key four times and exhaust a budget
    /// that admits it twice.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void
        StateMachineRelationshipIndex_ChargesOwnAssemblyKeyOnce(
            bool chargedOnce)
    {
        const int keyBytes = 4_096;
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName:
                    "Fixtures.Owner+Machine, StateMachineClaims",
                additionalKickoffClaims:
                [
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineClaimKind.ClassicAsync,
                ],
                assemblyPublicKey: new byte[keyBytes]));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget:
                    MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
                nameWorkBudget: chargedOnce
                    ? 2 * keyBytes
                    : keyBytes - 1);

        StateMachineRelationshipResult result =
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1));

        if (!chargedOnce)
        {
            var rejected =
                Assert.IsType<StateMachineRelationshipResult.Rejected>(
                    result);
            Assert.Equal(
                StateMachineRelationshipFailureKind.BudgetExceeded,
                rejected.Failure.Kind);
            return;
        }

        // Four kickoffs claiming one state-machine type is an ordinary
        // `Duplicate` rejection. Naming that kind rather than merely excluding
        // `BudgetExceeded` keeps the arm from passing on an unrelated failure.
        var admitted =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                result);
        Assert.Equal(
            StateMachineRelationshipFailureKind.Duplicate,
            admitted.Failure.Kind);
    }

    /// <summary>
    /// Gates every name-work charge on the constructor type-name path: the
    /// resolution-scope walk, the chain's structural cost, and the per-node
    /// name components including nil-named ones. A nil name decodes to zero
    /// characters and a non-platform terminal skips the name read entirely, so
    /// a charge keyed only on decoded length accounts for nothing while the
    /// walk and the read still do work proportional to depth; distinct
    /// constructor rows sharing one deep chain leaf then drive that work once
    /// each. Readable unrelated names end as <c>Absent</c>; nil names retain
    /// their typed malformed-name failure as a local rejection.
    ///
    /// This asserts the fixture's minimum admitting budget itself rather than
    /// picking a literal with margin. A tuned literal only has to sit
    /// somewhere between the charged and under-charged thresholds, so removing
    /// one of several charges can leave it on the same side of the boundary
    /// and the gate stays green while the property it names is gone. Measuring
    /// the boundary makes every charge on this path load-bearing: remove one,
    /// weaken one, or add one, and this number moves and the test says so.
    ///
    /// The two arms differ only in whether the chain's nodes are nil-named,
    /// because the nil and non-nil component charges are separate branches of
    /// <c>MetadataTypeNameBudget.TryRead</c>; one arm each keeps both branches
    /// gated here rather than incidentally by another subsystem's tests.
    /// </summary>
    [Theory]
    [InlineData(null, 17_702)]
    [InlineData("Node", 19_238)]
    public void
        StateMachineRelationshipIndex_ChargesNilNamedTypeNameChainNodes(
            string? nodeName,
            int expectedMinimumAdmittingBudget)
    {
        const int depth = 64;
        const int constructors = 8;

        byte[] bytes =
            BuildNilNamedChainImage(
                depth: depth,
                constructors: constructors,
                nodeName: nodeName);

        int measured = MinimumAdmittingNameWorkBudget(bytes);

        Assert.Equal(expectedMinimumAdmittingBudget, measured);

        // The boundary is a real behavioral edge, not just a number: one unit
        // below it the image must fail visibly rather than report an empty
        // success, and at it the image must be admitted.
        StateMachineRelationshipResult admitted =
            RunWithNameWorkBudget(bytes, measured);
        if (nodeName is null)
        {
            var malformed =
                Assert.IsType<StateMachineRelationshipResult.Rejected>(
                    admitted);
            Assert.Equal(
                StateMachineRelationshipFailureKind.Malformed,
                malformed.Failure.Kind);
        }
        else
        {
            Assert.IsType<StateMachineRelationshipResult.Absent>(
                admitted);
        }

        var rejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                RunWithNameWorkBudget(bytes, measured - 1));
        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            rejected.Failure.Kind);
    }

    [Fact]
    public void
        StateMachineRelationshipIndex_IsolatesTypeReferenceTraversalRejection()
    {
        using var image = new LoadedImage(
            BuildNilNamedChainImage(
                depth:
                    MetadataSafetyPolicy.MaxRelationshipNodes
                    + 1,
                constructors: 1,
                nodeName: "Node"));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        var rejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Empty(relationships.Relationships);
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            rejected.Failure.Kind);
        Assert.Equal(
            "A custom-attribute constructor could not be read.",
            rejected.Failure.Detail);
        Assert.Equal(
            0x06000001,
            Assert.Single(
                rejected.Failure.KickoffCandidates).Token);
        Assert.Empty(rejected.Failure.StateMachineCandidates);
        Assert.Empty(rejected.Failure.ClaimedTypes);
    }

    [Theory]
    [InlineData(ConstructorTypeSpecificationRejection.UnsafeStructure)]
    [InlineData(ConstructorTypeSpecificationRejection.BudgetExceeded)]
    public void
        StateMachineRelationshipIndex_IsolatesTypeSpecificationGuardRejection(
            ConstructorTypeSpecificationRejection rejection)
    {
        using var image = new LoadedImage(
            BuildClassicRelationshipImage(
                ClassicRelationshipMutation.None,
                rejectedConstructorTypeSpecification: rejection));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        StateMachineRelationship relationship =
            Assert.Single(relationships.Relationships);
        var damaged =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(4)));

        Assert.Equal(0x06000001, relationship.Kickoff.Token);
        Assert.IsType<StateMachineRelationshipResult.Resolved>(
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            damaged.Failure.Kind);
        Assert.Equal(
            "A custom-attribute constructor could not be read.",
            damaged.Failure.Detail);
        Assert.Equal(
            0x06000004,
            Assert.Single(damaged.Failure.KickoffCandidates).Token);
        Assert.Empty(damaged.Failure.StateMachineCandidates);
        Assert.Empty(damaged.Failure.ClaimedTypes);
    }

    [Theory]
    [InlineData(ConstructorTypeSpecificationShape.SzArray)]
    [InlineData(ConstructorTypeSpecificationShape.Array)]
    [InlineData(ConstructorTypeSpecificationShape.ByReference)]
    [InlineData(ConstructorTypeSpecificationShape.Pointer)]
    [InlineData(ConstructorTypeSpecificationShape.Pinned)]
    [InlineData(ConstructorTypeSpecificationShape.GenericType)]
    [InlineData(ConstructorTypeSpecificationShape.GenericArgument)]
    [InlineData(ConstructorTypeSpecificationShape.FunctionPointerReturn)]
    [InlineData(ConstructorTypeSpecificationShape.FunctionPointerParameter)]
    [InlineData(ConstructorTypeSpecificationShape.Modifier)]
    public void
        StateMachineRelationshipIndex_PreservesNestedConstructorTypeNameFailure(
            ConstructorTypeSpecificationShape shape)
    {
        using var image = new LoadedImage(
            BuildNilNamedChainImage(
                depth:
                    MetadataSafetyPolicy.MaxRelationshipNodes
                    + 1,
                constructors: 1,
                nodeName: "Node",
                constructorTypeSpecificationShape: shape));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Available>(
                index.Relationships);
        var rejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Empty(relationships.Relationships);
        Assert.Equal(
            StateMachineRelationshipFailureKind.Malformed,
            rejected.Failure.Kind);
        Assert.Equal(
            "A custom-attribute constructor could not be read.",
            rejected.Failure.Detail);
        Assert.Equal(
            0x06000001,
            Assert.Single(
                rejected.Failure.KickoffCandidates).Token);
        Assert.Empty(rejected.Failure.StateMachineCandidates);
        Assert.Empty(rejected.Failure.ClaimedTypes);
    }

    /// <summary>
    /// Gates that projecting this image's own assembly identity charges its
    /// name and culture, not only its public key. An unsigned assembly has a
    /// nil key blob, so a key-only charge would let the name and culture
    /// decode entirely uncharged. `ChargesOwnAssemblyKeyOnce` gates the key
    /// and never reaches this branch, so without this arm the name and culture
    /// charges are asserted by a comment and enforced by nothing here.
    /// </summary>
    [Fact]
    public void
        StateMachineRelationshipIndex_ChargesUnsignedAssemblyNameAndCulture()
    {
        // No public key: the key charge is skipped entirely, so the boundary
        // this measures is owned by the name and culture charges alone.
        byte[] bytes =
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName:
                    "Fixtures.Owner+Machine, StateMachineClaims",
                assemblyCulture: new string('c', 400));

        Assert.Equal(561, MinimumAdmittingNameWorkBudget(bytes));
    }

    /// <summary>
    /// Binary-searches the smallest name-work budget that admits
    /// <paramref name="image"/>, where "admits" means the index did not run
    /// out of name work. Admission is monotonic in the budget: charges do not
    /// depend on how much budget remains, so a budget that admits implies
    /// every larger one admits.
    /// </summary>
    static int MinimumAdmittingNameWorkBudget(byte[] image)
    {
        int low = 1;
        int high = 1 << 22;

        Assert.True(
            AdmitsNameWork(image, high),
            "The fixture must be admitted at the maximum budget, otherwise "
                + "the measured boundary is not a name-work boundary.");

        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (AdmitsNameWork(image, mid))
                high = mid;
            else
                low = mid + 1;
        }

        return low;
    }

    static bool AdmitsNameWork(byte[] image, int nameWorkBudget)
        => RunWithNameWorkBudget(image, nameWorkBudget)
            is not StateMachineRelationshipResult.Rejected
            {
                Failure.Kind:
                    StateMachineRelationshipFailureKind.BudgetExceeded,
            };

    static StateMachineRelationshipResult RunWithNameWorkBudget(
        byte[] image,
        int nameWorkBudget)
    {
        using var loaded = new LoadedImage(image);
        return StateMachineRelationshipIndex.Create(
                loaded.Reader,
                relationshipBudget:
                    MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
                nameWorkBudget: nameWorkBudget)
            .GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1));
    }

    /// <summary>
    /// Builds an image whose custom-attribute constructors are all parented on
    /// the leaf of a nil-named type-reference chain of the requested depth, so
    /// each distinct constructor row drives one full chain read.
    /// </summary>
    static byte[] BuildNilNamedChainImage(
        int depth,
        int constructors,
        string? nodeName = null,
        ConstructorTypeSpecificationShape
            constructorTypeSpecificationShape =
                ConstructorTypeSpecificationShape.None)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("NilChain.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NilChain"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle platform =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(10, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a,
                    }),
                default,
                default);

        StringHandle nodeNameHandle =
            nodeName is null
                ? default
                : metadata.GetOrAddString(nodeName);
        EntityHandle scope = platform;
        TypeReferenceHandle leaf = default;
        for (int i = 0; i < depth; i++)
        {
            leaf = metadata.AddTypeReference(
                scope,
                default,
                nodeNameHandle);
            scope = leaf;
        }

        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                platform,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle safeType =
            metadata.AddTypeReference(
                platform,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        EntityHandle constructorParent = leaf;
        if (constructorTypeSpecificationShape
            != ConstructorTypeSpecificationShape.None)
        {
            constructorParent =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        ConstructorTypeSpecification(
                            constructorTypeSpecificationShape,
                            leaf,
                            safeType)));
        }

        var ctorSig = new BlobBuilder();
        new BlobEncoder(ctorSig)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                r => r.Void(),
                p => p.AddParameter().Type().Type(
                    systemType,
                    isValueType: false));
        BlobHandle ctorSignature = metadata.GetOrAddBlob(ctorSig);

        var staticVoid = new BlobBuilder();
        new BlobEncoder(staticVoid)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(0, r => r.Void(), p => { });
        BlobHandle staticVoidSignature = metadata.GetOrAddBlob(staticVoid);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixtures.Owner+Machine");
        value.WriteUInt16(0);
        BlobHandle claimValue = metadata.GetOrAddBlob(value);

        MethodDefinitionHandle kickoff =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Kickoff"),
                staticVoidSignature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));

        StringHandle ctorName = metadata.GetOrAddString(".ctor");
        for (int i = 0; i < constructors; i++)
        {
            metadata.AddCustomAttribute(
                kickoff,
                metadata.AddMemberReference(
                    constructorParent,
                    ctorName,
                    ctorSignature),
                claimValue);
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
    }

    static BlobBuilder ConstructorTypeSpecification(
        ConstructorTypeSpecificationShape shape,
        TypeReferenceHandle rejectedType,
        TypeReferenceHandle safeType)
    {
        var signature = new BlobBuilder();
        switch (shape)
        {
            case ConstructorTypeSpecificationShape.SzArray:
                signature.WriteByte(0x1D);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.Array:
                signature.WriteByte(0x14);
                WriteClass(rejectedType);
                signature.WriteCompressedInteger(1);
                signature.WriteCompressedInteger(0);
                signature.WriteCompressedInteger(0);
                break;
            case ConstructorTypeSpecificationShape.ByReference:
                signature.WriteByte(0x10);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.Pointer:
                signature.WriteByte(0x0F);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.Pinned:
                signature.WriteByte(0x45);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.GenericType:
                signature.WriteByte(0x15);
                WriteClass(rejectedType);
                signature.WriteCompressedInteger(1);
                WriteClass(safeType);
                break;
            case ConstructorTypeSpecificationShape.GenericArgument:
                signature.WriteByte(0x15);
                WriteClass(safeType);
                signature.WriteCompressedInteger(1);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.FunctionPointerReturn:
                signature.WriteByte(0x1B);
                signature.WriteByte(0x00);
                signature.WriteCompressedInteger(0);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.FunctionPointerParameter:
                signature.WriteByte(0x1B);
                signature.WriteByte(0x00);
                signature.WriteCompressedInteger(1);
                signature.WriteByte(0x01);
                WriteClass(rejectedType);
                break;
            case ConstructorTypeSpecificationShape.Modifier:
                signature.WriteByte(0x1F);
                WriteTypeDefOrRefEncoded(signature, rejectedType);
                WriteClass(safeType);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return signature;

        void WriteClass(TypeReferenceHandle type)
        {
            signature.WriteByte(0x12);
            WriteTypeDefOrRefEncoded(signature, type);
        }
    }

    /// <summary>
    /// Gates that projecting an assembly-reference row charges the name it
    /// decodes. Distinct rows sharing one oversized name `StringHandle` defeat
    /// row-keyed projection caching, so the decode repeats per row; without a
    /// per-row charge that work is unbounded and ends as a success-shaped
    /// `Absent` rather than `BudgetExceeded`. The `false` arm fails if the
    /// charge is removed; the `true` arm fails if the charge runs away and
    /// rejects an image the budget should admit.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void
        StateMachineRelationshipIndex_ChargesRepeatedAssemblyRowNames(
            bool admitted)
    {
        const int rows = 4;
        const int nameChars = 4_096;
        using var image = new LoadedImage(
            BuildUntrustedAssemblyKeyImage(
                constructors: rows,
                keyBytes: 64,
                referenceRows: rows,
                sharedNameChars: nameChars));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                image.Reader,
                relationshipBudget:
                    MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
                nameWorkBudget: admitted
                    ? 16 * nameChars
                    : 2 * nameChars);

        StateMachineRelationshipResult result =
            index.GetByKickoff(
                MetadataTokens.MethodDefinitionHandle(1));

        if (!admitted)
        {
            var rejected =
                Assert.IsType<StateMachineRelationshipResult.Rejected>(
                    result);
            Assert.Equal(
                StateMachineRelationshipFailureKind.BudgetExceeded,
                rejected.Failure.Kind);
            return;
        }

        // Untrusted parents make every claim foreign, so an admitted image
        // reports no relationship at all rather than a budget failure.
        Assert.IsType<StateMachineRelationshipResult.Absent>(result);
    }

    [Theory]
    [InlineData("PublicKeyToken=null", false)]
    [InlineData("PublicKeyToken=0011223344556677", false)]
    [InlineData("PublicKeyToken=473c444ebb4661a5", true)]
    [InlineData("Culture=neutral", false)]
    [InlineData("Culture=en-US", true)]
    public void
        StateMachineRelationshipIndex_MatchesExplicitAssemblyQualifiers(
            string qualifier,
            bool matches)
    {
        using var image = new LoadedImage(
            BuildClaimImage(
                [StateMachineClaimKind.ClassicAsync],
                serializedTypeName:
                    "Fixtures.Owner+Machine, StateMachineClaims, "
                    + qualifier,
                assemblyPublicKey: QualifierPublicKey,
                assemblyCulture: "en-US"));

        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(image.Reader);
        var result =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(
                index.GetByKickoff(
                    MetadataTokens.MethodDefinitionHandle(1)));

        Assert.Equal(
            StateMachineRelationshipFailureKind.Unresolved,
            result.Failure.Kind);
        Assert.Equal(
            matches ? 1 : 0,
            result.Failure.StateMachineCandidates.Length);
    }

    /// <summary>
    /// Fixed signing key for
    /// <c>StateMachineRelationshipIndex_MatchesExplicitAssemblyQualifiers</c>.
    /// Its ECMA-335 II.23.3 token is <c>473c444ebb4661a5</c>, which the theory
    /// data spells out so a qualifier that names the correct signed assembly
    /// stays distinguishable from one that names an unsigned assembly.
    /// </summary>
    static byte[] QualifierPublicKey =>
        [.. Enumerable.Range(0, 160).Select(value => (byte)value)];

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

    static byte[] BuildTypeDefinitionNameBudgetImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("TypeNameBudget.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TypeNameBudget"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < 1_025; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    new string('X', 4_088)
                    + i.ToString("D6")),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
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
    }

    /// <summary>
    /// Builds an image where every kickoff carries duplicate claims naming one
    /// ambiguous type, and that name matches <paramref name="duplicates"/> type
    /// definitions. Expanding the name per kickoff would retain and republish
    /// the whole matching set once per kickoff, so this is the fan-out shape
    /// <c>StateMachineRelationshipIndex_ExpandsAmbiguousClaimsOnce</c> bounds.
    /// </summary>
    static byte[] BuildAmbiguousClaimFanOutImage(
        int kickoffs,
        int duplicates)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ClaimFanOut.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ClaimFanOut"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyName core =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(core.Name!),
                core.Version!,
                default,
                metadata.GetOrAddBlob(core.GetPublicKeyToken()!),
                default,
                default);
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                metadata.AddTypeReference(
                    coreReference,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(
                        nameof(AsyncStateMachineAttribute))),
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    TypeConstructorSignature(systemType)));

        var staticVoid = new BlobBuilder();
        new BlobEncoder(staticVoid)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                result => result.Void(),
                parameters => { });
        BlobHandle staticVoidSignature =
            metadata.GetOrAddBlob(staticVoid);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Owner"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        StringHandle sharedName = metadata.GetOrAddString("Machine");
        var stateMachines =
            new List<TypeDefinitionHandle>(duplicates);
        for (int i = 0; i < duplicates; i++)
        {
            stateMachines.Add(
                metadata.AddTypeDefinition(
                    TypeAttributes.NestedPrivate
                        | TypeAttributes.Sealed,
                    default,
                    sharedName,
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        kickoffs + 1)));
        }
        foreach (TypeDefinitionHandle stateMachine in stateMachines)
            metadata.AddNestedType(stateMachine, owner);

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixtures.Owner+Machine");
        value.WriteUInt16(0);
        BlobHandle claimValue = metadata.GetOrAddBlob(value);

        for (int i = 0; i < kickoffs; i++)
        {
            MethodDefinitionHandle kickoff =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"K{i}"),
                    staticVoidSignature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1));
            metadata.AddCustomAttribute(
                kickoff,
                constructor,
                claimValue);
            metadata.AddCustomAttribute(
                kickoff,
                constructor,
                claimValue);
        }

        return Serialize(metadata);
    }

    /// <summary>
    /// Builds an image whose shared claim value carries a named argument with a
    /// large string payload. The claim contract forbids named arguments, so the
    /// value must be refused from the blob rather than after SRM materializes
    /// every kickoff's copy of that payload.
    /// </summary>
    static byte[] BuildNamedArgumentClaimImage(
        int kickoffs,
        int namedValueCharacters)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("NamedArgumentClaim.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NamedArgumentClaim"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyName core =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(core.Name!),
                core.Version!,
                default,
                metadata.GetOrAddBlob(core.GetPublicKeyToken()!),
                default,
                default);
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                metadata.AddTypeReference(
                    coreReference,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(
                        nameof(AsyncStateMachineAttribute))),
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    TypeConstructorSignature(systemType)));

        var staticVoid = new BlobBuilder();
        new BlobEncoder(staticVoid)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                result => result.Void(),
                parameters => { });
        BlobHandle staticVoidSignature =
            metadata.GetOrAddBlob(staticVoid);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixtures.Owner+Machine");
        value.WriteUInt16(1);
        value.WriteByte(0x54);
        value.WriteByte(0x0E);
        value.WriteSerializedString("Payload");
        value.WriteSerializedString(
            new string('x', namedValueCharacters));
        BlobHandle claimValue = metadata.GetOrAddBlob(value);

        for (int i = 0; i < kickoffs; i++)
        {
            metadata.AddCustomAttribute(
                metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"K{i}"),
                    staticVoidSignature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1)),
                constructor,
                claimValue);
        }

        return Serialize(metadata);
    }

    /// <summary>
    /// Builds an image whose claim attributes all reference one untrusted
    /// assembly through distinct constructor member references. Authenticating
    /// each constructor has to decide the parent's trust, and the parent's
    /// public key is unbounded, so the key must be projected and charged once
    /// rather than once per constructor.
    /// </summary>
    static byte[] BuildUntrustedAssemblyKeyImage(
        int constructors,
        int keyBytes,
        int referenceRows = 1,
        int sharedNameChars = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("UntrustedKey.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("UntrustedKey"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        // Every reference row shares one key blob, so `GetOrAddBlob` hands
        // back the same `BlobHandle`. Handle-keyed memoization alone cannot
        // collapse these rows; only a blob-keyed charge set can.
        byte[] key =
            Enumerable.Range(0, keyBytes)
                .Select(value => (byte)value)
                .ToArray();
        // When a shared name is requested every row reuses one `StringHandle`
        // and differs only by version. That is the shape row-keyed projection
        // caching cannot collapse, so the name really is decoded once per row.
        StringHandle sharedName =
            sharedNameChars > 0
                ? metadata.GetOrAddString(
                    new string('N', sharedNameChars))
                : default;
        var untrustedRows = new AssemblyReferenceHandle[referenceRows];
        for (int row = 0; row < referenceRows; row++)
        {
            untrustedRows[row] =
                metadata.AddAssemblyReference(
                    sharedNameChars > 0
                        ? sharedName
                        : metadata.GetOrAddString($"Untrusted{row}"),
                    new Version(
                        1,
                        0,
                        0,
                        sharedNameChars > 0 ? row : 0),
                    default,
                    metadata.GetOrAddBlob(key),
                    AssemblyFlags.PublicKey,
                    default);
        }

        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                untrustedRows[0],
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        var attributeTypes = new TypeReferenceHandle[referenceRows];
        for (int row = 0; row < referenceRows; row++)
        {
            attributeTypes[row] =
                metadata.AddTypeReference(
                    untrustedRows[row],
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(
                        nameof(AsyncStateMachineAttribute)));
        }
        BlobHandle constructorSignature =
            metadata.GetOrAddBlob(
                TypeConstructorSignature(systemType));

        var staticVoid = new BlobBuilder();
        new BlobEncoder(staticVoid)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                result => result.Void(),
                parameters => { });
        BlobHandle staticVoidSignature =
            metadata.GetOrAddBlob(staticVoid);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixtures.Owner+Machine");
        value.WriteUInt16(0);
        BlobHandle claimValue = metadata.GetOrAddBlob(value);

        for (int i = 0; i < constructors; i++)
        {
            metadata.AddCustomAttribute(
                metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"K{i}"),
                    staticVoidSignature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1)),
                metadata.AddMemberReference(
                    attributeTypes[i % attributeTypes.Length],
                    metadata.GetOrAddString(".ctor"),
                    constructorSignature),
                claimValue);
        }

        return Serialize(metadata);
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
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
    }

    static byte[] BuildRejectionMergeImage(int count)    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("RejectionMerge.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("RejectionMerge"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyName core =
            typeof(AsyncStateMachineAttribute).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(core.Name!),
                core.Version!,
                default,
                metadata.GetOrAddBlob(
                    core.GetPublicKeyToken()!),
                default,
                default);
        TypeReferenceHandle systemType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        TypeReferenceHandle asyncAttribute =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    nameof(AsyncStateMachineAttribute)));
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                asyncAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    TypeConstructorSignature(systemType)));

        var staticVoid = new BlobBuilder();
        new BlobEncoder(staticVoid)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                result => result.Void(),
                parameters => { });
        BlobHandle staticVoidSignature =
            metadata.GetOrAddBlob(staticVoid);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString("Owner"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        var stateMachines =
            new List<TypeDefinitionHandle>(count);
        for (int i = 0; i < count; i++)
        {
            stateMachines.Add(
                metadata.AddTypeDefinition(
                    TypeAttributes.NestedPrivate
                        | TypeAttributes.Sealed,
                    default,
                    metadata.GetOrAddString($"M{i}"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        2 * count + 1)));
        }
        foreach (TypeDefinitionHandle stateMachine
            in stateMachines)
        {
            metadata.AddNestedType(stateMachine, owner);
        }

        var kickoffs =
            new List<MethodDefinitionHandle>(2 * count);
        for (int i = 0; i < count; i++)
        {
            kickoffs.Add(
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"A{i}"),
                    staticVoidSignature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1)));
            kickoffs.Add(
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"B{i}"),
                    staticVoidSignature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1)));
        }

        for (int i = 0; i < count; i++)
        {
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteSerializedString(
                $"Fixtures.Owner+M{i}");
            value.WriteUInt16(0);
            BlobHandle valueBlob =
                metadata.GetOrAddBlob(value);
            metadata.AddCustomAttribute(
                kickoffs[2 * i],
                constructor,
                valueBlob);
            metadata.AddCustomAttribute(
                kickoffs[2 * i],
                constructor,
                valueBlob);
            metadata.AddCustomAttribute(
                kickoffs[2 * i + 1],
                constructor,
                valueBlob);
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
    }

    static byte[] BuildClaimImage(
        IReadOnlyList<StateMachineClaimKind> claims,
        bool malformedConstructor = false,
        bool trustedAttributeAssembly = true,
        bool includeStateMachineType = true,
        bool duplicateStateMachineType = false,
        StateMachineClaimKind? secondKickoffClaim = null,
        string serializedTypeName = "Fixtures.Owner+Machine",
        int unrelatedAttributeCount = 0,
        IReadOnlyList<StateMachineClaimKind>?
            additionalKickoffClaims = null,
        bool reuseClaimConstructors = false,
        byte[]? assemblyPublicKey = null,
        string? assemblyCulture = null,
        GuidHandle? moduleVersionId = null,
        bool largeGuidHeap = false)
    {
        var metadata = new MetadataBuilder();
        if (largeGuidHeap)
        {
            for (int i = 1; i <= 4_096; i++)
            {
                metadata.GetOrAddGuid(
                    new Guid(
                        i,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0));
            }
        }
        metadata.AddModule(
            0,
            metadata.GetOrAddString("StateMachineClaims.dll"),
            moduleVersionId
                ?? metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("StateMachineClaims"),
            new Version(1, 0, 0, 0),
            assemblyCulture is null
                ? default
                : metadata.GetOrAddString(assemblyCulture),
            assemblyPublicKey is null
                ? default
                : metadata.GetOrAddBlob(assemblyPublicKey),
            assemblyPublicKey is null
                ? default
                : AssemblyFlags.PublicKey,
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
        int additionalKickoffCount =
            (secondKickoffClaim is null ? 0 : 1)
            + (additionalKickoffClaims?.Count ?? 0);
        int methodSentinelRow =
            2 + additionalKickoffCount;
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

        var constructors =
            new Dictionary<
                StateMachineClaimKind,
                MemberReferenceHandle>();
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
            AddAdditionalKickoff(
                "SecondKickoff",
                secondKind);
        }
        if (additionalKickoffClaims is not null)
        {
            for (int i = 0;
                i < additionalKickoffClaims.Count;
                i++)
            {
                AddAdditionalKickoff(
                    $"AdditionalKickoff{i}",
                    additionalKickoffClaims[i]);
            }
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

        void AddAdditionalKickoff(
            string name,
            StateMachineClaimKind kind)
        {
            MethodDefinitionHandle additional =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(name),
                    metadata.GetOrAddBlob(methodSignature),
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1));
            AddClaimAttribute(additional, kind);
        }

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
            if (!reuseClaimConstructors
                || !constructors.TryGetValue(
                    kind,
                    out MemberReferenceHandle constructor))
            {
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
                constructor =
                    metadata.AddMemberReference(
                        attributeType,
                        metadata.GetOrAddString(".ctor"),
                        metadata.GetOrAddBlob(constructorSignature));
                if (reuseClaimConstructors)
                    constructors.Add(kind, constructor);
            }
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
        ClassicRelationshipMutation mutation,
        bool addMalformedConstructorRow = false,
        bool malformedConstructorOnRelationshipKickoff = false,
        ConstructorTypeNameRejection rejectedConstructorTypeName =
            ConstructorTypeNameRejection.None,
        ConstructorTypeSpecificationRejection
            rejectedConstructorTypeSpecification =
                ConstructorTypeSpecificationRejection.None,
        int repeatedThrowingConstructorAttributes = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            repeatedThrowingConstructorAttributes);

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
        bool omitSetStateMachine =
            mutation
                == ClassicRelationshipMutation.MissingSetStateMachine;
        bool explicitWrongSignature =
            mutation
                == ClassicRelationshipMutation
                    .ExplicitWrongSignatureSetStateMachine;
        MethodDefinitionHandle setStateMachine = default;
        if (!omitSetStateMachine)
        {
            setStateMachine =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Virtual,
                    mutation
                        == ClassicRelationshipMutation
                            .NonIlSetStateMachine
                        ? MethodImplAttributes.Runtime
                        : MethodImplAttributes.IL,
                    metadata.GetOrAddString(
                        explicitWrongSignature
                            ? "IAsyncStateMachine.SetStateMachine"
                            : "SetStateMachine"),
                    metadata.GetOrAddBlob(
                        explicitWrongSignature
                            ? instanceVoidSignature
                            : setStateMachineSignature),
                    bodyOffset,
                    MetadataTokens.ParameterHandle(1));
            if (mutation
                == ClassicRelationshipMutation.DuplicateSetStateMachine)
            {
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Virtual,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("SetStateMachine"),
                    metadata.GetOrAddBlob(setStateMachineSignature),
                    bodyOffset,
                    MetadataTokens.ParameterHandle(1));
            }
        }
        MethodDefinitionHandle damagedKickoff = default;
        if ((addMalformedConstructorRow
                || rejectedConstructorTypeName
                    != ConstructorTypeNameRejection.None
                || rejectedConstructorTypeSpecification
                    != ConstructorTypeSpecificationRejection.None
                || repeatedThrowingConstructorAttributes > 0)
            && !malformedConstructorOnRelationshipKickoff)
        {
            damagedKickoff =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("DamagedKickoff"),
                    metadata.GetOrAddBlob(staticVoidSignature),
                    bodyOffset,
                    MetadataTokens.ParameterHandle(1));
        }

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
        if (explicitWrongSignature)
        {
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    asyncStateMachine,
                    metadata.GetOrAddString("SetStateMachine"),
                    metadata.GetOrAddBlob(instanceVoidSignature));
            metadata.AddMethodImplementation(
                machine,
                setStateMachine,
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
        if (rejectedConstructorTypeName
            != ConstructorTypeNameRejection.None)
        {
            StringHandle rejectedName =
                rejectedConstructorTypeName switch
                {
                    ConstructorTypeNameRejection.MissingName =>
                        default,
                    ConstructorTypeNameRejection.NameBudget =>
                        metadata.GetOrAddString(
                            new string(
                                'A',
                                MetadataSafetyPolicy
                                    .MaxTypeNameCharacters
                                + 1)),
                    _ => throw new InvalidOperationException(),
                };
            TypeReferenceHandle rejectedAttributeType =
                metadata.AddTypeReference(
                    coreReference,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    rejectedName);
            MemberReferenceHandle rejectedConstructor =
                metadata.AddMemberReference(
                    rejectedAttributeType,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        constructorSignature));
            metadata.AddCustomAttribute(
                damagedKickoff,
                rejectedConstructor,
                metadata.GetOrAddBlob(value));
        }
        if (rejectedConstructorTypeSpecification
            != ConstructorTypeSpecificationRejection.None)
        {
            var typeSpecification = new BlobBuilder();
            typeSpecification.WriteByte(0x12);
            WriteTypeDefOrRefEncoded(
                typeSpecification,
                asyncAttribute);
            int trailingBytes =
                rejectedConstructorTypeSpecification switch
                {
                    ConstructorTypeSpecificationRejection.UnsafeStructure =>
                        8,
                    ConstructorTypeSpecificationRejection.BudgetExceeded =>
                        TypeSpecGuard.MaxCumulativeBytes,
                    _ => throw new InvalidOperationException(),
                };
            for (int i = 0; i < trailingBytes; i++)
                typeSpecification.WriteByte(0);

            TypeSpecificationHandle rejectedAttributeType =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(typeSpecification));
            MemberReferenceHandle rejectedConstructor =
                metadata.AddMemberReference(
                    rejectedAttributeType,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        constructorSignature));
            metadata.AddCustomAttribute(
                damagedKickoff,
                rejectedConstructor,
                metadata.GetOrAddBlob(value));
        }
        if (repeatedThrowingConstructorAttributes > 0)
        {
            var malformedSignature = new BlobBuilder();
            malformedSignature.WriteByte(0x20);
            malformedSignature.WriteCompressedInteger(1);
            malformedSignature.WriteByte(0x01);
            for (int i = 0; i < 500; i++)
                malformedSignature.WriteByte(0x0F);

            MemberReferenceHandle throwingConstructor =
                metadata.AddMemberReference(
                    asyncAttribute,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        malformedSignature));
            for (int i = 0;
                i < repeatedThrowingConstructorAttributes;
                i++)
            {
                metadata.AddCustomAttribute(
                    damagedKickoff,
                    throwingConstructor,
                    metadata.GetOrAddBlob(value));
            }
        }
        if (addMalformedConstructorRow)
        {
            metadata.AddCustomAttribute(
                malformedConstructorOnRelationshipKickoff
                    ? kickoff
                    : damagedKickoff,
                MetadataTokens.MemberReferenceHandle(2),
                metadata.GetOrAddBlob(value));
        }

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
        TypeReferenceHandle modifier =
            AddTypeReference(
                "System.Runtime.CompilerServices",
                "IsReadOnlyAttribute");

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

        BlobBuilder asyncEnumeratorSpecification =
            GenericInterfaceSpecification(
                asyncEnumerator,
                argumentCount:
                    shape == AsyncEnumeratorShape.WrongArity
                        ? 2
                        : 1,
                argumentTypeCode: 0x08);
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
        MethodDefinitionHandle moveNextAsyncMethod =
            AddMethod(
            "MoveNextAsync",
            moveNextAsync,
            shape is AsyncEnumeratorShape.CrossConstruction
                    or AsyncEnumeratorShape.ModifiedArgument
                ? MethodAttributes.Private
                    | MethodAttributes.Virtual
                : MethodAttributes.Public
                    | MethodAttributes.Virtual);
        AddMethod(
            "DisposeAsync",
            disposeAsync,
            MethodAttributes.Public
                | MethodAttributes.Virtual);

        if (shape is AsyncEnumeratorShape.CrossConstruction
            or AsyncEnumeratorShape.ModifiedArgument)
        {
            var declarationType = new BlobBuilder();
            declarationType.WriteByte(0x15);
            declarationType.WriteByte(0x12);
            WriteTypeDefOrRefEncoded(
                declarationType,
                asyncEnumerator);
            declarationType.WriteCompressedInteger(1);
            if (shape
                == AsyncEnumeratorShape.ModifiedArgument)
            {
                declarationType.WriteByte(0x1F);
                WriteTypeDefOrRefEncoded(
                    declarationType,
                    modifier);
                declarationType.WriteByte(0x08);
            }
            else
            {
                declarationType.WriteByte(0x0E);
            }
            TypeSpecificationHandle declarationParent =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(declarationType));
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    declarationParent,
                    metadata.GetOrAddString("MoveNextAsync"),
                    metadata.GetOrAddBlob(moveNextAsync));
            metadata.AddMethodImplementation(
                machine,
                moveNextAsyncMethod,
                declaration);
        }

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

    static BlobBuilder GenericInterfaceSpecification(
        TypeReferenceHandle genericType,
        int argumentCount,
        byte argumentTypeCode)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        WriteTypeDefOrRefEncoded(signature, genericType);
        signature.WriteCompressedInteger(argumentCount);
        for (int i = 0; i < argumentCount; i++)
            signature.WriteByte(argumentTypeCode);
        return signature;
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
        None,
        MissingSetStateMachine,
        CustomModifiedSetStateMachine,
        ValueTypeSetStateMachine,
        NonIlSetStateMachine,
        ExplicitWrongSignatureSetStateMachine,
        DuplicateSetStateMachine,
        StaticMoveNext,
        NonIlMoveNext,
        MethodImplBodyOnOtherType,
        NonIlKickoff,
    }

    public enum AsyncEnumeratorShape
    {
        Bare,
        WrongArity,
        CrossConstruction,
        ModifiedArgument,
    }

    public enum ConstructorTypeNameRejection
    {
        None,
        MissingName,
        NameBudget,
    }

    public enum ConstructorTypeSpecificationRejection
    {
        None,
        UnsafeStructure,
        BudgetExceeded,
    }

    public enum ConstructorTypeSpecificationShape
    {
        None,
        SzArray,
        Array,
        ByReference,
        Pointer,
        Pinned,
        GenericType,
        GenericArgument,
        FunctionPointerReturn,
        FunctionPointerParameter,
        Modifier,
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
