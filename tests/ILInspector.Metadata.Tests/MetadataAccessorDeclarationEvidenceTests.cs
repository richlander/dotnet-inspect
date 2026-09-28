using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.MemorySafetyFixtures;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataAccessorDeclarationEvidenceTests
{
    [Fact]
    public void Mdp004_PropertyAggregatePreservesEveryPhysicalOccurrence()
    {
        using var fixture = Fixture.Create(
            methodCount: 4,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
                PropertyRow(2, 0x0004),
                PropertyRow(3, 0x0004),
                PropertyRow(4, 0x0001),
            ]);
        var work = new List<MetadataOperationWorkKind>();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property);

        var first = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    request,
                    TestContext.Current.CancellationToken));
        var second = declaration.PostAccessorDeclaration(
            request,
            TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(request.Type, first.Evidence.Type);
        Assert.Equal(request.Declaration, first.Evidence.Declaration);
        Assert.Equal(
            [
                (1, 0x0002, MetadataAccessorSemanticsRole.Getter, 1),
                (2, 0x0004, MetadataAccessorSemanticsRole.Other, 2),
                (3, 0x0004, MetadataAccessorSemanticsRole.Other, 3),
                (4, 0x0001, MetadataAccessorSemanticsRole.Setter, 4),
            ],
            first.Evidence.Accessors
                .Select(accessor => (
                    accessor.PhysicalRowNumber,
                    (int)accessor.RawSemantics,
                    accessor.Role,
                    MetadataTokens.GetRowNumber(
                        accessor.Method.Method.Handle)))
                .ToArray());
        Assert.Equal(
            4,
            first.Evidence.Accessors
                .Select(accessor => accessor.Method)
                .Distinct()
                .Count());
        Assert.Contains(
            MetadataOperationWorkKind.MethodSemanticsAssociationRead,
            work);
        Assert.Contains(
            MetadataOperationWorkKind.AccessorDeclarationPublication,
            work);
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .MemorySafetyIndexMaterialization));
        Assert.Equal(
            5,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .AccessorSafetyEvidenceRead));
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .AccessorDeclarationPublication));
    }

    [Fact]
    public void Mdp004_EventAggregatePreservesRaiseAndOther()
    {
        using var fixture = Fixture.Create(
            methodCount: 5,
            eventCount: 1,
            rows:
            [
                EventRow(1, 0x0008),
                EventRow(2, 0x0010),
                EventRow(3, 0x0020),
                EventRow(4, 0x0004),
                EventRow(5, 0x0004),
            ]);

        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            [
                MetadataAccessorSemanticsRole.AddOn,
                MetadataAccessorSemanticsRole.RemoveOn,
                MetadataAccessorSemanticsRole.Fire,
                MetadataAccessorSemanticsRole.Other,
                MetadataAccessorSemanticsRole.Other,
            ],
            posted.Evidence.Accessors
                .Select(accessor => accessor.Role)
                .ToArray());
        Assert.Equal(
            [1, 2, 3, 4, 5],
            posted.Evidence.Accessors
                .Select(accessor => accessor.PhysicalRowNumber)
                .ToArray());
        var root = Assert.IsType<
            MetadataAccessorRootDeclarationEvidence.Event>(
                posted.Evidence.Root);
        Assert.Equal("E1", root.Name.ToString());
    }

    [Fact]
    public void Mdp004_CompilerProducedPropertyAndEventPostExactAccessors()
    {
        Type fixtureType = typeof(MemorySafetyDeclarationFixtures);
        string path = fixtureType.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                fixtureType.MetadataToken);
        PropertyDefinitionHandle property =
            (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                fixtureType.GetProperty(
                    nameof(MemorySafetyDeclarationFixtures.Property))!
                    .MetadataToken);
        EventDefinitionHandle @event =
            (EventDefinitionHandle)MetadataTokens.EntityHandle(
                fixtureType.GetEvent(
                    nameof(MemorySafetyDeclarationFixtures.CustomEvent))!
                    .MetadataToken);
        MetadataTypeDefinitionAddress typeAddress =
            MetadataTypeDefinitionAddress.FromHandle(reader, type);
        var propertyRequest = new MetadataAccessorDeclarationRequest(
            typeAddress,
            MetadataAccessorDeclarationAddress.Create(reader, property));
        var eventRequest = new MetadataAccessorDeclarationRequest(
            typeAddress,
            MetadataAccessorDeclarationAddress.Create(reader, @event));

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        var propertyPosted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    propertyRequest,
                    TestContext.Current.CancellationToken));
        var eventPosted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    eventRequest,
                    TestContext.Current.CancellationToken));

        Assert.Equal(typeAddress, propertyPosted.Evidence.Type);
        Assert.Equal(typeAddress, eventPosted.Evidence.Type);
        var propertyRoot = Assert.IsType<
            MetadataAccessorRootDeclarationEvidence.Property>(
                propertyPosted.Evidence.Root);
        Assert.Equal("Property", propertyRoot.Name.ToString());
        Assert.Equal(
            reader.GetPropertyDefinition(property).Attributes,
            propertyRoot.Attributes);
        Assert.Equal(
            "int",
            Assert.IsType<MetadataTypeIdentity.Primitive>(
                propertyRoot.Signature.ValueType)
                .Name.ToString());
        Assert.Empty(propertyRoot.Signature.IndexParameterTypes);
        var eventRoot = Assert.IsType<
            MetadataAccessorRootDeclarationEvidence.Event>(
                eventPosted.Evidence.Root);
        Assert.Equal("CustomEvent", eventRoot.Name.ToString());
        Assert.Equal(
            reader.GetEventDefinition(@event).Attributes,
            eventRoot.Attributes);
        Assert.EndsWith(
            "Action",
            Assert.IsType<MetadataTypeIdentity.Named>(
                eventRoot.EventType)
                .Definition.Segments[^1].ToString(),
            StringComparison.Ordinal);
        Assert.Equal(2, propertyPosted.Evidence.Accessors.Length);
        Assert.Equal(2, eventPosted.Evidence.Accessors.Length);
        Assert.Equal(
            (ApiTypeLayout)(
                reader.GetTypeDefinition(type).Attributes
                    & TypeAttributes.LayoutMask),
            propertyPosted.Evidence.Safety.DeclaringTypeLayout);
        Assert.Equal(
            MetadataTokens.GetToken(property),
            propertyPosted.Evidence.Safety.Declaration
                .CallerContract.Evidence.MemberToken);
        Assert.All(
            propertyPosted.Evidence.Accessors,
            accessor => Assert.Equal(
                MetadataTokens.GetToken(
                    accessor.Method.Method.Handle),
                accessor.MemorySafety.CallerContract
                    .Evidence.MemberToken));

        Dictionary<MetadataAccessorSemanticsRole,
            MetadataMethodDeclarationEvidence> propertyAccessors =
            propertyPosted.Evidence.Accessors.ToDictionary(
                accessor => accessor.Role,
                accessor => accessor.Method);
        Assert.Equal(
            "get_Property",
            propertyAccessors[MetadataAccessorSemanticsRole.Getter]
                .Name.ToString());
        Assert.Empty(
            propertyAccessors[MetadataAccessorSemanticsRole.Getter]
                .Signature.ParameterTypes);
        Assert.Equal(
            "set_Property",
            propertyAccessors[MetadataAccessorSemanticsRole.Setter]
                .Name.ToString());
        Assert.Single(
            propertyAccessors[MetadataAccessorSemanticsRole.Setter]
                .Signature.ParameterTypes);

        Dictionary<MetadataAccessorSemanticsRole,
            MetadataMethodDeclarationEvidence> eventAccessors =
            eventPosted.Evidence.Accessors.ToDictionary(
                accessor => accessor.Role,
                accessor => accessor.Method);
        Assert.Equal(
            "add_CustomEvent",
            eventAccessors[MetadataAccessorSemanticsRole.AddOn]
                .Name.ToString());
        Assert.Equal(
            "remove_CustomEvent",
            eventAccessors[MetadataAccessorSemanticsRole.RemoveOn]
                .Name.ToString());
        Assert.All(
            propertyAccessors.Values.Concat(eventAccessors.Values),
            accessor =>
            {
                Assert.True(accessor.Attributes.HasFlag(
                    MethodAttributes.SpecialName));
                Assert.False(accessor.Attributes.HasFlag(
                    MethodAttributes.Static));
                Assert.False(accessor.OperatorCandidate);
            });
    }

    [Fact]
    public void Mdp007_CompilerProducedIndexerPostsExactRootSignature()
    {
            Type fixtureType = typeof(MemorySafetyDeclarationFixtures);
            string path = fixtureType.Assembly.Location;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinitionHandle type =
                (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    fixtureType.MetadataToken);
            PropertyDefinitionHandle property =
                (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                    fixtureType.GetProperty("Item")!.MetadataToken);
            var request = new MetadataAccessorDeclarationRequest(
                MetadataTypeDefinitionAddress.FromHandle(reader, type),
                MetadataAccessorDeclarationAddress.Create(
                    reader,
                    property));

            using var assembly = AssemblyInspectionSession.Open(path);
            using var operation = new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
            using MetadataDeclarationSession declaration =
                assembly.CreateDeclarationSession(operation);
            var posted = Assert.IsType<
                MetadataAccessorDeclarationResult.Posted>(
                    declaration.PostAccessorDeclaration(
                        request,
                        TestContext.Current.CancellationToken));

            var root = Assert.IsType<
                MetadataAccessorRootDeclarationEvidence.Property>(
                    posted.Evidence.Root);
            Assert.Equal("Item", root.Name.ToString());
            Assert.Equal(
                "int",
                Assert.IsType<MetadataTypeIdentity.Primitive>(
                    root.Signature.ValueType)
                    .Name.ToString());
            Assert.Equal(
                "int",
                Assert.IsType<MetadataTypeIdentity.Primitive>(
                    Assert.Single(
                        root.Signature.IndexParameterTypes))
                    .Name.ToString());
            Assert.Equal(
                [
                    MetadataAccessorSemanticsRole.Getter,
                    MetadataAccessorSemanticsRole.Setter,
                ],
                posted.Evidence.Accessors
                    .Select(accessor => accessor.Role)
                    .ToArray());
    }

    [Fact]
    public void Mdp007_RuntimeListCountPostsDetachedRootAndGetter()
    {
            Type runtimeType = typeof(List<>);
            string path = runtimeType.Assembly.Location;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinitionHandle type =
                (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    runtimeType.MetadataToken);
            PropertyDefinitionHandle property =
                (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                    runtimeType.GetProperty(nameof(List<int>.Count))!
                        .MetadataToken);
            var request = new MetadataAccessorDeclarationRequest(
                MetadataTypeDefinitionAddress.FromHandle(reader, type),
                MetadataAccessorDeclarationAddress.Create(
                    reader,
                    property));

            using var assembly = AssemblyInspectionSession.Open(path);
            using var operation = new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
            using MetadataDeclarationSession declaration =
                assembly.CreateDeclarationSession(operation);
            var posted = Assert.IsType<
                MetadataAccessorDeclarationResult.Posted>(
                    declaration.PostAccessorDeclaration(
                        request,
                        TestContext.Current.CancellationToken));

            var root = Assert.IsType<
                MetadataAccessorRootDeclarationEvidence.Property>(
                    posted.Evidence.Root);
            Assert.Equal("Count", root.Name.ToString());
            Assert.Empty(root.Signature.IndexParameterTypes);
            Assert.Equal(
                "int",
                Assert.IsType<MetadataTypeIdentity.Primitive>(
                    root.Signature.ValueType)
                    .Name.ToString());
            Assert.Equal(
                MetadataAccessorSemanticsRole.Getter,
                Assert.Single(posted.Evidence.Accessors).Role);
            Assert.Equal(
                request.Declaration.ModuleVersionId,
                posted.Evidence.Safety.Module.ModuleVersionId);

            assembly.Dispose();
            Assert.Equal("Count", root.Name.ToString());
            Assert.Equal(
                "int",
                Assert.IsType<MetadataTypeIdentity.Primitive>(
                    root.Signature.ValueType)
                    .Name.ToString());
    }

    [Fact]
    public void Mdp007_PointerPropertyComposesRootAndAccessorSafety()
    {
        Type fixtureType = typeof(MemorySafetyGenericStorage<>);
        string path = fixtureType.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    fixtureType.MetadataToken);
        PropertyDefinitionHandle property =
            (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                    fixtureType.GetProperty("PointerProperty")!
                        .MetadataToken);
        var request = new MetadataAccessorDeclarationRequest(
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataAccessorDeclarationAddress.Create(
                    reader,
                    property));

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                    declaration.PostAccessorDeclaration(
                        request,
                        TestContext.Current.CancellationToken));

        var root = Assert.IsType<
            MetadataAccessorRootDeclarationEvidence.Property>(
                    posted.Evidence.Root);
        Assert.IsType<MetadataTypeIdentity.Pointer>(
            root.Signature.ValueType);
        Assert.Equal(
            MemorySafetyPointerEvidence.Present,
            posted.Evidence.Safety.Declaration
                    .SignaturePointer);
        Assert.All(
            posted.Evidence.Accessors,
            accessor => Assert.Equal(
                    MemorySafetyPointerEvidence.Present,
                    accessor.MemorySafety.SignaturePointer));
    }

    [Fact]
    public void Mdp007_CompilerProducedInitSetterRetainsModifiedVoid()
    {
        Type fixtureType = typeof(MemorySafetyDeclarationFixtures);
        string path = fixtureType.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    fixtureType.MetadataToken);
        PropertyDefinitionHandle property =
            (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                    fixtureType.GetProperty("InitProperty")!
                        .MetadataToken);
        var request = new MetadataAccessorDeclarationRequest(
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            MetadataAccessorDeclarationAddress.Create(
                    reader,
                    property));

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                    declaration.PostAccessorDeclaration(
                        request,
                        TestContext.Current.CancellationToken));

        MetadataAccessorSemanticsOccurrence setter =
            Assert.Single(
                    posted.Evidence.Accessors,
                    accessor =>
                        accessor.Role
                        == MetadataAccessorSemanticsRole.Setter);
        var modified = Assert.IsType<
            MetadataTypeIdentity.Modified>(
                    setter.Method.Signature.ReturnType);
        Assert.True(modified.IsRequired);
        Assert.Equal(
            "void",
            Assert.IsType<MetadataTypeIdentity.Primitive>(
                    modified.Type)
                    .Name.ToString());
        Assert.Equal(
            "IsExternalInit",
            Assert.IsType<MetadataTypeIdentity.Named>(
                    modified.Modifier)
                    .Definition.Segments[^1].ToString());
    }

    [Theory]
    [InlineData(
            RootShapeMismatch.GetterReturn,
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence)]
    [InlineData(
            RootShapeMismatch.SetterValue,
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence)]
    [InlineData(
            RootShapeMismatch.SetterIndexPrefix,
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence)]
    [InlineData(
            RootShapeMismatch.Staticness,
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence)]
    [InlineData(
            RootShapeMismatch.DeclarationModifiers,
            MetadataAccessorDeclarationMechanism.DeclarationModifierConsistency)]
    public void Mdp007_InconsistentPropertyRootRejectsAggregate(
            RootShapeMismatch mismatch,
            MetadataAccessorDeclarationMechanism mechanism)
    {
            using var fixture = new Fixture(
                BuildPropertyRootShapeImage(mismatch));

            var rejected = Assert.IsType<
                MetadataAccessorDeclarationResult.Rejected>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Property));

            Assert.Equal(
                MetadataAccessorDeclarationFailureReason.MalformedMetadata,
                rejected.Failure.Reason);
            Assert.Equal(
                MetadataAccessorDeclarationStage.ConsistencyValidation,
                rejected.Failure.Stage);
            Assert.Equal(mechanism, rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(new byte[] { 0xA8, 0x00, 0x08 })]
    [InlineData(new byte[] { 0x28, 0x01, 0x08, 0x01 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x10, 0x01 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x45, 0x08 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x1D, 0x01 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x0F, 0x10, 0x08 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x14, 0x08, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x1B, 0x00, 0x01, 0x01, 0x01 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x1D, 0x16 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x10, 0x16 })]
    public void Mdp007_MalformedPropertySignatureGrammarRejectsRoot(
        byte[] signature)
    {
        using var fixture = new Fixture(
            BuildPropertyRawRootImage(signature));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.RootDeclaration,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.SignatureDecode,
            rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(new byte[] { 0x08, 0x00, 0x08 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x16 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x10, 0x08 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x0F, 0x01 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x1D, 0x08 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x14, 0x08, 0x01, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x28, 0x00, 0x1B, 0x00, 0x00, 0x01 })]
    public void Mdp007_LegalPropertySignatureGrammarPostsRoot(
        byte[] signature)
    {
        using var fixture = new Fixture(
            BuildPropertyRawRootImage(signature));

        Assert.IsType<MetadataAccessorDeclarationResult.Posted>(
            Run(
                fixture,
                MetadataAccessorDeclarationKind.Property));
    }

    [Fact]
    public void Mdp007_CensusFailurePrecedesRootSignatureDecode()
    {
        using var fixture = new Fixture(
                BuildPropertyRootShapeImage(
                    RootShapeMismatch.VoidProperty,
                    getterSemantics: 0));

        var rejected = Assert.IsType<
                MetadataAccessorDeclarationResult.Rejected>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
                MetadataAccessorDeclarationStage.AssociationCensus,
                rejected.Failure.Stage);
        Assert.Equal(
                MetadataAccessorDeclarationMechanism.RoleValidation,
                rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(new byte[] { 0x01 })]
    [InlineData(new byte[] { 0x10, 0x01 })]
    [InlineData(new byte[] { 0x16 })]
    public void Mdp007_MalformedEventTypeSpecGrammarRejectsRoot(
        byte[] signature)
    {
        using var fixture = new Fixture(
            BuildEventTypeSpecRootImage(
                blob =>
                {
                    foreach (byte value in signature)
                        blob.WriteByte(value);
                }));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.RootDeclaration,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.SignatureDecode,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_LegalEventPointerToVoidTypeSpecPostsRoot()
    {
        using var fixture = new Fixture(
            BuildEventTypeSpecRootImage(
                signature =>
                {
                    signature.WriteByte(0x0F);
                    signature.WriteByte(0x01);
                }));

        Assert.IsType<MetadataAccessorDeclarationResult.Posted>(
            Run(
                fixture,
                MetadataAccessorDeclarationKind.Event));
    }

    [Fact]
    public void Mdp007_EventAddParameterMismatchRejectsAggregate()
    {
            using var fixture = new Fixture(
                BuildEventRootShapeImage(
                    mismatchedAddParameter: true));

            var rejected = Assert.IsType<
                MetadataAccessorDeclarationResult.Rejected>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Event));

            Assert.Equal(
                MetadataAccessorDeclarationFailureReason.MalformedMetadata,
                rejected.Failure.Reason);
            Assert.Equal(
                MetadataAccessorDeclarationStage.ConsistencyValidation,
                rejected.Failure.Stage);
            Assert.Equal(
                MetadataAccessorDeclarationMechanism.SignatureCorrespondence,
                rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_AccessorReservedHeaderRejectsAggregate()
    {
        using var fixture = new Fixture(
            BuildPropertyRawAccessorImage(
                [0xA0, 0x00, 0x08],
                MethodSemanticsAttributes.Getter));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.ConsistencyValidation,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_OtherNonMethodDefinitionSignatureRejectsAggregate()
    {
        using var fixture = new Fixture(
            BuildPropertyRawAccessorImage(
                [0x01, 0x00, 0x01],
                MethodSemanticsAttributes.Other));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.ConsistencyValidation,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_EventTypeSpecTrailingDataRejectsRoot()
    {
        using var fixture = new Fixture(
                BuildEventTypeSpecRootImage(
                    signature =>
                    {
                        signature.WriteByte(0x12);
                        signature.WriteByte(0x05);
                        signature.WriteByte(0xFF);
                    }));

        var rejected = Assert.IsType<
                MetadataAccessorDeclarationResult.Rejected>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
                MetadataAccessorDeclarationFailureReason.MalformedMetadata,
                rejected.Failure.Reason);
        Assert.Equal(
                MetadataAccessorDeclarationStage.RootDeclaration,
                rejected.Failure.Stage);
        Assert.Equal(
                MetadataAccessorDeclarationMechanism.SignatureDecode,
                rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_EventTypeSpecChargesRootSignatureBytes()
    {
        using var fixture = new Fixture(
                BuildEventTypeSpecRootImage(
                    signature =>
                    {
                        signature.WriteByte(0x12);
                        signature.WriteByte(0x05);
                    }));
        var policy = new MetadataOperationPolicy(
                long.MaxValue,
                maxSignatureBytes: 0);

        var rejected = Assert.IsType<
                MetadataAccessorDeclarationResult.Rejected>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Event,
                        policy));

        Assert.Equal(
                MetadataAccessorDeclarationFailureReason.BudgetExceeded,
                rejected.Failure.Reason);
        Assert.Equal(
                MetadataOperationDimension.SignatureBytes,
                rejected.Failure.BudgetDimension);
        Assert.Equal(0, rejected.Failure.BudgetLimit);
    }

    [Fact]
    public void Mdp007_EventTypeSpecDepthRejectsRoot()
    {
        using var fixture = new Fixture(
                BuildEventTypeSpecRootImage(
                    signature =>
                    {
                        for (int depth = 0; depth < 100; depth++)
                            signature.WriteByte(0x1D);
                        signature.WriteByte(0x12);
                        signature.WriteByte(0x05);
                    }));

        var rejected = Assert.IsType<
                MetadataAccessorDeclarationResult.Rejected>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
                MetadataAccessorDeclarationFailureReason.BudgetExceeded,
                rejected.Failure.Reason);
        Assert.Equal(
                MetadataAccessorDeclarationStage.RootDeclaration,
                rejected.Failure.Stage);
    }

    [Fact]
    public void Mdp007_GenericEventTypeSpecPostsWithMatchingAccessors()
    {
        using var fixture = new Fixture(
                BuildEventTypeSpecRootImage(
                    signature =>
                    {
                        signature.WriteByte(0x15);
                        signature.WriteByte(0x12);
                        signature.WriteByte(0x05);
                        signature.WriteByte(0x01);
                        signature.WriteByte(0x08);
                    },
                    includeAccessors: true));

        var posted = Assert.IsType<
                MetadataAccessorDeclarationResult.Posted>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Event));

        var root = Assert.IsType<
                MetadataAccessorRootDeclarationEvidence.Event>(
                    posted.Evidence.Root);
        var eventType = Assert.IsType<
                MetadataTypeIdentity.GenericInstance>(
                    root.EventType);
        Assert.Equal("Action`1", eventType.Definition.Segments[^1].ToString());
        Assert.Equal(
                "int",
                Assert.IsType<MetadataTypeIdentity.Primitive>(
                    Assert.Single(eventType.Arguments))
                    .Name.ToString());
        Assert.All(
                posted.Evidence.Accessors,
                accessor =>
                    Assert.Equal(
                        root.EventType,
                        Assert.Single(
                            accessor.Method.Signature.ParameterTypes)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mdp007_EventRootTypeNameBudgetIsTyped(
        bool wrapInTypeSpec)
    {
        using var fixture = new Fixture(
            BuildEventNamedRootImage(
                new string('N', 64 * 1024),
                wrapInTypeSpec: wrapInTypeSpec));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.RootDeclaration,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_EventRootResolutionScopeBudgetIsTyped()
    {
        using var fixture = new Fixture(
            BuildEventNamedRootImage(
                "EventHandler",
                resolutionScopeDepth:
                    MetadataSafetyPolicy.MaxRelationshipNodes));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.RootDeclaration,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_EventRootResolutionScopeCycleRemainsMalformed()
    {
        using var fixture = new Fixture(
            BuildEventNamedRootImage(
                "EventHandler",
                cyclicResolutionScope: true));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.RootDeclaration,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 1)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 1)]
    public void Mdp007_AccessorRootNameLimitIsTyped(
        MetadataAccessorDeclarationKind kind,
        int excessLength)
    {
        string name = new(
            'N',
            MetadataSafetyPolicy.MaxStructuralSignatureChars
                + excessLength);
        using var fixture = new Fixture(
            kind == MetadataAccessorDeclarationKind.Property
                ? BuildPropertyRootShapeImage(
                    RootShapeMismatch.None,
                    rootName: name)
                : BuildEventRootShapeImage(rootName: name));

        MetadataAccessorDeclarationResult result =
            Run(fixture, kind);

        if (excessLength == 0)
        {
            MetadataAccessorDeclarationResult.Posted posted =
                Assert.IsType<
                    MetadataAccessorDeclarationResult.Posted>(
                        result);
            Assert.Equal(
                name.Length,
                posted.Evidence.Root.Name.ToString().Length);
            return;
        }

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(result);
        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.RootDeclaration,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.TextRetention,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp007_EventFirePreservesInvocationSignature()
    {
            using var fixture = new Fixture(
                BuildEventRootShapeImage(
                    includeFireWithInvocationParameter: true));

            var posted = Assert.IsType<
                MetadataAccessorDeclarationResult.Posted>(
                    Run(
                        fixture,
                        MetadataAccessorDeclarationKind.Event));

            MetadataAccessorSemanticsOccurrence fire =
                Assert.Single(
                    posted.Evidence.Accessors,
                    accessor =>
                        accessor.Role
                        == MetadataAccessorSemanticsRole.Fire);
            Assert.Equal(
                "int",
                Assert.IsType<MetadataTypeIdentity.Primitive>(
                    Assert.Single(
                        fire.Method.Signature.ParameterTypes))
                    .Name.ToString());
    }

    [Theory]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x01 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x10, 0x01 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x45, 0x08 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x1D, 0x16 })]
    public void Mdp007_EventFireRejectsMalformedParameterGrammar(
        byte[] signature)
    {
        using var fixture = new Fixture(
            BuildEventRootShapeImage(
                fireSignatureBytes: signature));

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.ConsistencyValidation,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.SignatureCorrespondence,
            rejected.Failure.Mechanism);
    }

    [Theory]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x08 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x10, 0x08 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x16 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x1D, 0x08 })]
    [InlineData(new byte[] { 0x20, 0x01, 0x01, 0x0F, 0x01 })]
    [InlineData(new byte[] { 0x25, 0x01, 0x01, 0x08 })]
    [InlineData(new byte[] { 0x60, 0x01, 0x01, 0x08 })]
    public void Mdp007_EventFirePreservesLegalMethodDefinitionGrammar(
        byte[] signature)
    {
        using var fixture = new Fixture(
            BuildEventRootShapeImage(
                fireSignatureBytes: signature));

        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Contains(
            posted.Evidence.Accessors,
            accessor =>
                accessor.Role
                == MetadataAccessorSemanticsRole.Fire);
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property)]
    [InlineData(MetadataAccessorDeclarationKind.Event)]
    public void Mdp004_ZeroAccessorAggregateIsComplete(
        MetadataAccessorDeclarationKind kind)
    {
        using var fixture = Fixture.Create(
            methodCount: 0,
            propertyCount:
                kind == MetadataAccessorDeclarationKind.Property ? 1 : 0,
            eventCount:
                kind == MetadataAccessorDeclarationKind.Event ? 1 : 0);

        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Run(fixture, kind));

        Assert.Empty(posted.Evidence.Accessors);
    }

    [Fact]
    public void Mdp006_UnrelatedAssociationLookupUsesOneIndexedProbe()
    {
        const int associationCount = 4096;
        using var fixture = Fixture.Create(
            methodCount: associationCount,
            propertyCount: associationCount,
            rows: Enumerable.Range(1, associationCount)
                .Select(methodRow => PropertyRow(
                    methodRow,
                    (ushort)MethodSemanticsAttributes.Other))
                .ToArray());
        var work = new List<MetadataOperationWorkKind>();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRelationshipEdges: 0,
                maxRetainedMethodSemanticsAssociations: associationCount),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property,
                associationCount);

        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Empty(posted.Evidence.Accessors);
        Assert.Equal(0, operation.Counters.RelationshipEdges);
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .AccessorAssociationLookupProbe));
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0000)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0003)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0008)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x8000)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0x0002)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0x0018)]
    public void Mdp004_IllegalRoleBitsRejectTheAggregate(
        MetadataAccessorDeclarationKind kind,
        ushort rawSemantics)
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount:
                kind == MetadataAccessorDeclarationKind.Property ? 1 : 0,
            eventCount:
                kind == MetadataAccessorDeclarationKind.Event ? 1 : 0,
            rows:
            [
                new(
                    1,
                    kind == MetadataAccessorDeclarationKind.Property
                        ? MethodSemanticsAssociationKind.Property
                        : MethodSemanticsAssociationKind.Event,
                    1,
                    rawSemantics),
            ]);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(fixture, kind));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.RoleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(1, rejected.Failure.PhysicalRowNumber);
        Assert.Equal(rawSemantics, rejected.Failure.RawSemantics);
        Assert.Null(rejected.Failure.AccessorFailure);
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0002)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0x0008)]
    public void Mdp004_DuplicateConventionalRoleRejectsTheAggregate(
        MetadataAccessorDeclarationKind kind,
        ushort rawSemantics)
    {
        MethodSemanticsAssociationKind associationKind =
            kind == MetadataAccessorDeclarationKind.Property
                ? MethodSemanticsAssociationKind.Property
                : MethodSemanticsAssociationKind.Event;
        using var fixture = Fixture.Create(
            methodCount: 2,
            propertyCount:
                kind == MetadataAccessorDeclarationKind.Property ? 1 : 0,
            eventCount:
                kind == MetadataAccessorDeclarationKind.Event ? 1 : 0,
            rows:
            [
                new(1, associationKind, 1, rawSemantics),
                new(2, associationKind, 1, rawSemantics),
            ]);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(fixture, kind));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.DuplicateRole,
            rejected.Failure.Mechanism);
        Assert.Equal(2, rejected.Failure.PhysicalRowNumber);
    }

    [Fact]
    public void Mdp004_CrossDeclaringTypeMethodRejectsTheAggregate()
    {
        using var fixture = Fixture.Create(
            methodCount: 2,
            propertyCount: 1,
            rows:
            [
                PropertyRow(2, 0x0002),
            ],
            secondTypeMethodStart: 2);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.DirectOwnership,
            rejected.Failure.Mechanism);
        Assert.Equal(1, rejected.Failure.PhysicalRowNumber);
        Assert.Equal(2, rejected.Failure.Method!.Value.Token
            & 0x00FFFFFF);
    }

    [Fact]
    public void Mdp004_NonmonotonicAssociationOrderRejectsTheCensus()
    {
        byte[] image = MethodSemanticsRowReaderTests.BuildImage(
            methodCount: 2,
            propertyCount: 2,
            rows:
            [
                PropertyRow(1, 0x0002, propertyRow: 1),
                PropertyRow(2, 0x0002, propertyRow: 2),
            ]);
        MethodSemanticsRowReaderTests.PatchAssociation(
            image,
            encodedAssociation: 5,
            rowIndex: 0);
        MethodSemanticsRowReaderTests.PatchAssociation(
            image,
            encodedAssociation: 3,
            rowIndex: 1);
        using var fixture = new Fixture(image);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.AssociationOrdering,
            rejected.Failure.Mechanism);
        Assert.Null(rejected.Failure.AccessorFailure);
    }

    [Fact]
    public void Mdp006_CensusBudgetRejectionRemainsVisible()
    {
        using var fixture = Fixture.Create(
            methodCount: 2,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
                PropertyRow(2, 0x0001),
            ]);
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxRetainedMethodSemanticsAssociations: 1);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property,
                    policy));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.AssociationCensus,
            rejected.Failure.Stage);
        Assert.NotNull(rejected.Failure.CensusFailure);
        Assert.Equal(
            MetadataOperationDimension
                .RetainedMethodSemanticsAssociations,
            rejected.Failure.BudgetDimension);
        Assert.Null(rejected.Failure.AccessorFailure);
    }

    [Fact]
    public void Mdp006_AccessorPostRejectionRemainsVisible()
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
            ]);
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxDeclarationCandidates: 1);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property,
                    policy));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.AccessorDeclaration,
            rejected.Failure.Stage);
        Assert.NotNull(rejected.Failure.AccessorFailure);
        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.AccessorFailure!.Reason);
        Assert.Equal(
            MetadataOperationDimension.DeclarationCandidates,
            rejected.Failure.BudgetDimension);
    }

    [Fact]
    public void Mdp006_ImageAdmissionRejectionAvoidsCensusWork()
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
            ]);
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(maxMetadataRows: 0),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                declaration.PostAccessorDeclaration(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.ImageAdmission,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataOperationDimension.MetadataRows,
            rejected.Failure.BudgetDimension);
        Assert.Empty(work);
    }

    [Fact]
    public void Mdp009_CachedAggregateRequiresLiveDeclarationOwner()
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
            ]);
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property);
        MetadataAccessorDeclarationResult detached =
            declaration.PostAccessorDeclaration(
                request,
                TestContext.Current.CancellationToken);

        declaration.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            declaration.PostAccessorDeclaration(
                request,
                TestContext.Current.CancellationToken));
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(detached);
        Assert.Single(posted.Evidence.Accessors);
    }

    static MetadataAccessorDeclarationResult Run(
        Fixture fixture,
        MetadataAccessorDeclarationKind kind,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                kind);
        return declaration.PostAccessorDeclaration(
            request,
            TestContext.Current.CancellationToken);
    }

    static MetadataAccessorDeclarationRequest CreateRequest(
        MetadataReader reader,
        MetadataAccessorDeclarationKind kind,
        int rowNumber = 1)
    {
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(2));
        MetadataAccessorDeclarationAddress declaration = kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                MetadataAccessorDeclarationAddress.Create(
                    reader,
                    MetadataTokens.PropertyDefinitionHandle(rowNumber)),
            MetadataAccessorDeclarationKind.Event =>
                MetadataAccessorDeclarationAddress.Create(
                    reader,
                    MetadataTokens.EventDefinitionHandle(rowNumber)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new(type, declaration);
    }

    static MethodSemanticsRowReaderTests.RawSemanticsRow PropertyRow(
        int methodRow,
        ushort rawSemantics,
        int propertyRow = 1) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Property,
            propertyRow,
            rawSemantics);

    static MethodSemanticsRowReaderTests.RawSemanticsRow EventRow(
        int methodRow,
        ushort rawSemantics) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Event,
            1,
            rawSemantics);

    static byte[] BuildPropertyRootShapeImage(
        RootShapeMismatch mismatch,
        ushort getterSemantics =
            (ushort)MethodSemanticsAttributes.Getter,
        string rootName = "Value")
    {
        var metadata = CreateMetadata();
        bool indexed =
            mismatch
            == RootShapeMismatch.SetterIndexPrefix;
        MethodAttributes getterAttributes =
            AccessorAttributes
            | (mismatch == RootShapeMismatch.Staticness
                ? MethodAttributes.Static
                : 0);
        MethodAttributes setterAttributes =
            AccessorAttributes
            | (mismatch
                    == RootShapeMismatch.DeclarationModifiers
                ? MethodAttributes.NewSlot
                : 0);
        MethodDefinitionHandle getter = AddMethod(
            metadata,
            "get_Value",
            getterAttributes,
            isInstance: true,
            indexed ? 1 : 0,
            returnType =>
            {
                if (mismatch
                    == RootShapeMismatch.GetterReturn)
                {
                    returnType.Type().String();
                }
                else
                {
                    returnType.Type().Int32();
                }
            },
            parameters =>
            {
                if (indexed)
                {
                    parameters.AddParameter()
                        .Type()
                        .Int32();
                }
            });
        MethodDefinitionHandle setter = AddMethod(
            metadata,
            "set_Value",
            setterAttributes,
            isInstance: true,
            indexed ? 2 : 1,
            returnType => returnType.Void(),
            parameters =>
            {
                if (indexed)
                {
                    parameters.AddParameter()
                        .Type()
                        .String();
                }
                SignatureTypeEncoder value =
                    parameters.AddParameter().Type();
                if (mismatch
                    == RootShapeMismatch.SetterValue)
                {
                    value.String();
                }
                else
                {
                    value.Int32();
                }
            });
        AddModuleType(metadata, getter);
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .PropertySignature(isInstanceProperty: true)
            .Parameters(
                indexed ? 1 : 0,
                returnType =>
                {
                    if (mismatch
                        == RootShapeMismatch.VoidProperty)
                    {
                        returnType.Void();
                    }
                    else
                    {
                        returnType.Type().Int32();
                    }
                },
                parameters =>
                {
                    if (indexed)
                    {
                        parameters.AddParameter()
                            .Type()
                            .Int32();
                    }
                });
        PropertyDefinitionHandle property =
            metadata.AddProperty(
                PropertyAttributes.SpecialName,
                metadata.GetOrAddString(rootName),
                metadata.GetOrAddBlob(signature));
        metadata.AddPropertyMap(owner, property);
        metadata.AddMethodSemantics(
            property,
            (MethodSemanticsAttributes)getterSemantics,
            getter);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Setter,
            setter);
        return Serialize(metadata);
    }

    static byte[] BuildPropertyRawRootImage(byte[] signatureBytes)
    {
        var metadata = CreateMetadata();
        AddModuleType(
            metadata,
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        foreach (byte value in signatureBytes)
            signature.WriteByte(value);
        PropertyDefinitionHandle property =
            metadata.AddProperty(
                PropertyAttributes.SpecialName,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(signature));
        metadata.AddPropertyMap(owner, property);
        return Serialize(metadata);
    }

    static byte[] BuildPropertyRawAccessorImage(
        byte[] accessorSignatureBytes,
        MethodSemanticsAttributes semantics)
    {
        var metadata = CreateMetadata();
        MethodDefinitionHandle accessor = AddRawMethod(
            metadata,
            semantics == MethodSemanticsAttributes.Getter
                ? "get_Value"
                : "Other",
            AccessorAttributes,
            accessorSignatureBytes);
        AddModuleType(metadata, accessor);
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            accessor);
        var signature = new BlobBuilder();
        signature.WriteByte(0x28);
        signature.WriteByte(0x00);
        signature.WriteByte(0x08);
        PropertyDefinitionHandle property =
            metadata.AddProperty(
                PropertyAttributes.SpecialName,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(signature));
        metadata.AddPropertyMap(owner, property);
        metadata.AddMethodSemantics(
            property,
            semantics,
            accessor);
        return Serialize(metadata);
    }

    static byte[] BuildEventRootShapeImage(
        bool mismatchedAddParameter = false,
        bool includeFireWithInvocationParameter = false,
        string rootName = "Value",
        byte[]? fireSignatureBytes = null)
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("mscorlib"),
                new Version(4, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle eventType =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("EventHandler"));
        MethodDefinitionHandle add = AddMethod(
            metadata,
            "add_Value",
            AccessorAttributes,
            isInstance: true,
            1,
            returnType => returnType.Void(),
            parameters =>
            {
                if (mismatchedAddParameter)
                {
                    parameters.AddParameter()
                        .Type()
                        .String();
                }
                else
                {
                    parameters.AddParameter()
                        .Type()
                        .Type(
                            eventType,
                            isValueType: false);
                }
            });
        MethodDefinitionHandle remove = AddMethod(
            metadata,
            "remove_Value",
            AccessorAttributes,
            isInstance: true,
            1,
            returnType => returnType.Void(),
            parameters =>
                parameters.AddParameter()
                    .Type()
                    .Type(
                        eventType,
                        isValueType: false));
        MethodDefinitionHandle fire = default;
        if (fireSignatureBytes is not null)
        {
            fire = AddRawMethod(
                metadata,
                "raise_Value",
                AccessorAttributes,
                fireSignatureBytes);
        }
        else if (includeFireWithInvocationParameter)
        {
            fire = AddMethod(
                metadata,
                "raise_Value",
                AccessorAttributes,
                isInstance: true,
                1,
                returnType => returnType.Void(),
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .Int32());
        }
        AddModuleType(metadata, add);
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            add);
        EventDefinitionHandle @event = metadata.AddEvent(
            EventAttributes.SpecialName,
            metadata.GetOrAddString(rootName),
            eventType);
        metadata.AddEventMap(owner, @event);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Adder,
            add);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Remover,
            remove);
        if (!fire.IsNil)
        {
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Raiser,
                fire);
        }
        return Serialize(metadata);
    }

    static byte[] BuildEventTypeSpecRootImage(
        Action<BlobBuilder> writeSignature,
        bool includeAccessors = false)
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("mscorlib"),
                new Version(4, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle eventTypeDefinition =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString(
                    includeAccessors
                        ? "Action`1"
                        : "EventHandler"));
        var signature = new BlobBuilder();
        writeSignature(signature);
        TypeSpecificationHandle eventType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        MethodDefinitionHandle add = default;
        MethodDefinitionHandle remove = default;
        if (includeAccessors)
        {
            add = AddMethod(
                metadata,
                "add_Value",
                AccessorAttributes,
                isInstance: true,
                1,
                returnType => returnType.Void(),
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .GenericInstantiation(
                            eventTypeDefinition,
                            genericArgumentCount: 1,
                            isValueType: false)
                        .AddArgument()
                        .Int32());
            remove = AddMethod(
                metadata,
                "remove_Value",
                AccessorAttributes,
                isInstance: true,
                1,
                returnType => returnType.Void(),
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .GenericInstantiation(
                            eventTypeDefinition,
                            genericArgumentCount: 1,
                            isValueType: false)
                        .AddArgument()
                        .Int32());
        }
        MethodDefinitionHandle firstMethod = includeAccessors
            ? add
            : MetadataTokens.MethodDefinitionHandle(1);
        AddModuleType(metadata, firstMethod);
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            firstMethod);
        EventDefinitionHandle @event = metadata.AddEvent(
            EventAttributes.SpecialName,
            metadata.GetOrAddString("Value"),
            eventType);
        metadata.AddEventMap(owner, @event);
        if (includeAccessors)
        {
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Adder,
                add);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Remover,
                remove);
        }
        return Serialize(metadata);
    }

    static byte[] BuildEventNamedRootImage(
        string typeName,
        int resolutionScopeDepth = 0,
        bool wrapInTypeSpec = false,
        bool cyclicResolutionScope = false)
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("mscorlib"),
                new Version(4, 0, 0, 0),
                default,
                default,
                default,
                default);
        EntityHandle resolutionScope = cyclicResolutionScope
            ? MetadataTokens.TypeReferenceHandle(1)
            : coreLibrary;
        for (int i = 0; i < resolutionScopeDepth; i++)
        {
            resolutionScope = metadata.AddTypeReference(
                resolutionScope,
                metadata.GetOrAddString("Scopes"),
                metadata.GetOrAddString($"Scope{i}"));
        }
        TypeReferenceHandle eventTypeDefinition =
            metadata.AddTypeReference(
                resolutionScope,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString(typeName));
        EntityHandle eventType = eventTypeDefinition;
        if (wrapInTypeSpec)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .TypeSpecificationSignature()
                .Type(
                    eventTypeDefinition,
                    isValueType: false);
            eventType = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        }
        AddModuleType(
            metadata,
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        EventDefinitionHandle @event = metadata.AddEvent(
            EventAttributes.SpecialName,
            metadata.GetOrAddString("Value"),
            eventType);
        metadata.AddEventMap(owner, @event);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        return metadata;
    }

    static void AddModuleType(
        MetadataBuilder metadata,
        MethodDefinitionHandle firstMethod) =>
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            firstMethod);

    static MethodDefinitionHandle AddMethod(
        MetadataBuilder metadata,
        string name,
        MethodAttributes attributes,
        bool isInstance,
        int parameterCount,
        Action<ReturnTypeEncoder> returnType,
        Action<ParametersEncoder> parameters)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: isInstance)
            .Parameters(
                parameterCount,
                returnType,
                parameters);
        return metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
    }

    static MethodDefinitionHandle AddRawMethod(
        MetadataBuilder metadata,
        string name,
        MethodAttributes attributes,
        byte[] signatureBytes)
    {
        var signature = new BlobBuilder();
        foreach (byte value in signatureBytes)
            signature.WriteByte(value);
        return metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    const MethodAttributes AccessorAttributes =
        MethodAttributes.Public
        | MethodAttributes.Abstract
        | MethodAttributes.Virtual
        | MethodAttributes.HideBySig
        | MethodAttributes.SpecialName;

    public enum RootShapeMismatch
    {
        None,
        GetterReturn,
        SetterValue,
        SetterIndexPrefix,
        Staticness,
        DeclarationModifiers,
        VoidProperty,
    }

    sealed class Fixture : IDisposable
    {
        internal Fixture(byte[] image)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"accessor-declaration-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, image);
        }

        internal string Path { get; }

        internal static Fixture Create(
            int methodCount,
            int propertyCount = 0,
            int eventCount = 0,
            IReadOnlyList<
                MethodSemanticsRowReaderTests.RawSemanticsRow>? rows = null,
            int? secondTypeMethodStart = null) =>
            new(
                MethodSemanticsRowReaderTests.BuildImage(
                    methodCount: methodCount,
                    propertyCount: propertyCount,
                    eventCount: eventCount,
                    rows: rows,
                    secondTypeMethodStart: secondTypeMethodStart));

        public void Dispose() => File.Delete(Path);
    }
}
