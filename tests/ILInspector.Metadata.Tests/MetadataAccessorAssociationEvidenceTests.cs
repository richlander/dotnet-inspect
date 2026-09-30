using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataAccessorAssociationEvidenceTests
{
    [Fact]
    public void Mdp004_ListCountGetterRelatesToExactPropertyAggregate()
    {
        Type runtimeType = typeof(List<>);
        PropertyInfo property =
            runtimeType.GetProperty(nameof(List<int>.Count))!;
        MethodInfo getter = property.GetMethod!;
        string path = runtimeType.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                runtimeType.MetadataToken);
        MethodDefinitionHandle methodHandle =
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                getter.MetadataToken);
        PropertyDefinitionHandle propertyHandle =
            (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                property.MetadataToken);
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                typeHandle);
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(reader, methodHandle);
        var work = new List<MetadataOperationWorkKind>();
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);

        var related =
            Assert.IsType<MetadataAccessorAssociationResult.Related>(
                declaration.RelateAccessor(
                    type,
                    method,
                    TestContext.Current.CancellationToken));

        Assert.Equal(type, related.Certificate.Type);
        Assert.Equal(method, related.Certificate.Method);
        Assert.Equal(
            MetadataAccessorDeclarationAddress.Create(
                reader,
                propertyHandle),
            related.Certificate.Declaration);
        Assert.Equal(
            MetadataAccessorSemanticsRole.Getter,
            related.Certificate.Role);
        Assert.Equal(
            (ushort)MethodSemanticsAttributes.Getter,
            related.Certificate.RawSemantics);
        Assert.True(related.Certificate.PhysicalRowNumber > 0);

        var aggregate =
            Assert.IsType<MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    new(
                        type,
                        related.Certificate.Declaration),
                    TestContext.Current.CancellationToken));
        MetadataAccessorSemanticsOccurrence occurrence =
            Assert.Single(
                aggregate.Evidence.Accessors,
                accessor =>
                    accessor.Method.Method == method);
        Assert.Equal(related.Certificate.Role, occurrence.Role);
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                    == MetadataOperationWorkKind
                        .MethodSemanticsAssociationRead));
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                    == MetadataOperationWorkKind
                        .AccessorReverseLookupProbe));
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                    == MetadataOperationWorkKind
                        .AccessorAssociationPublication));
    }

    [Fact]
    public void Mdp004_OrdinaryMethodPostsCertifiedAbsence()
    {
        Type runtimeType = typeof(List<>);
        MethodInfo methodInfo =
            runtimeType.GetMethod(
                nameof(List<int>.Add),
                [runtimeType.GetGenericArguments()[0]])!;
        string path = runtimeType.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    runtimeType.MetadataToken));
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(
                reader,
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                    methodInfo.MetadataToken));
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);

        Assert.IsType<MetadataAccessorAssociationResult.Absent>(
            declaration.RelateAccessor(
                type,
                method,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Mdp004_EventAdderRelatesToExactEventAggregate()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 2,
                eventCount: 1,
                rows:
                [
                    EventRow(
                        1,
                        1,
                        MethodSemanticsAttributes.Adder),
                    EventRow(
                        2,
                        1,
                        MethodSemanticsAttributes.Remover),
                ]));

        var related =
            Assert.IsType<MetadataAccessorAssociationResult.Related>(
                Relate(fixture, typeRow: 2, methodRow: 1));

        Assert.Equal(
            MetadataAccessorDeclarationKind.Event,
            related.Certificate.Declaration.Kind);
        Assert.Equal(1, related.Certificate.Declaration.RowNumber);
        Assert.Equal(
            MetadataAccessorSemanticsRole.AddOn,
            related.Certificate.Role);
    }

    [Fact]
    public void Mdp004_MultiplePhysicalAssociationsRejectAsAmbiguous()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 2,
                rows:
                [
                    PropertyRow(1, 1, MethodSemanticsAttributes.Getter),
                    PropertyRow(1, 2, MethodSemanticsAttributes.Getter),
                ]));

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                Relate(fixture, typeRow: 2, methodRow: 1));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.AmbiguousAssociation,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorAssociationMechanism.RelationshipLookup,
            rejected.Failure.Mechanism);
        Assert.Equal(2, rejected.Failure.ObservedAssociationCount);
        Assert.Equal(1, rejected.Failure.PhysicalRowNumber);
    }

    [Fact]
    public void Mdp004_CombinedRoleRejectsAsMalformed()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(
                        1,
                        1,
                        MethodSemanticsAttributes.Setter
                            | MethodSemanticsAttributes.Getter),
                ]));

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                Relate(fixture, typeRow: 2, methodRow: 1));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorAssociationMechanism.RoleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal<ushort?>(
            (ushort)(
                MethodSemanticsAttributes.Setter
                | MethodSemanticsAttributes.Getter),
            rejected.Failure.RawSemantics);
    }

    [Fact]
    public void Mdp004_CrossDeclaringTypeAssociationRejectsAsMalformed()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 2,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(
                        2,
                        1,
                        MethodSemanticsAttributes.Getter),
                ],
                secondTypeMethodStart: 2));

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                Relate(fixture, typeRow: 3, methodRow: 2));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorAssociationMechanism.DirectOwnership,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp004_NonmonotonicCensusRejectsBeforeReverseLookup()
    {
        byte[] image = MethodSemanticsRowReaderTests.BuildImage(
            methodCount: 2,
            propertyCount: 2,
            rows:
            [
                PropertyRow(
                    1,
                    1,
                    MethodSemanticsAttributes.Getter),
                PropertyRow(
                    2,
                    2,
                    MethodSemanticsAttributes.Getter),
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

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                Relate(fixture, typeRow: 2, methodRow: 1));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorAssociationMechanism.AssociationOrdering,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Mdp009_ForeignMethodAddressRejectsBeforeCensus()
    {
        byte[] firstImage = MethodSemanticsRowReaderTests.BuildImage(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(
                    1,
                    1,
                    MethodSemanticsAttributes.Getter),
            ]);
        using var first = new Fixture(firstImage);
        using var second = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(
                        1,
                        1,
                        MethodSemanticsAttributes.Getter),
                ]));
        using var firstStream = File.OpenRead(first.Path);
        using var firstPe = new PEReader(firstStream);
        MetadataReader firstReader = firstPe.GetMetadataReader();
        using var secondStream = File.OpenRead(second.Path);
        using var secondPe = new PEReader(secondStream);
        MetadataReader secondReader = secondPe.GetMetadataReader();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                secondReader,
                MetadataTokens.TypeDefinitionHandle(2));
        MetadataMethodAddress foreignMethod =
            MetadataMethodAddress.Create(
                firstReader,
                MetadataTokens.MethodDefinitionHandle(1));
        var work = new List<MetadataOperationWorkKind>();
        using var assembly =
            AssemblyInspectionSession.Open(second.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                declaration.RelateAccessor(
                    type,
                    foreignMethod,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.SessionUnavailable,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorAssociationMechanism.AddressResolution,
            rejected.Failure.Mechanism);
        Assert.DoesNotContain(
            MetadataOperationWorkKind.MethodSemanticsAssociationRead,
            work);
    }

    [Fact]
    public void Mdp006_CensusBudgetFailureRemainsTyped()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(
                        1,
                        1,
                        MethodSemanticsAttributes.Getter),
                ]));
        using var stream = File.OpenRead(fixture.Path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(2));
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(
                reader,
                MetadataTokens.MethodDefinitionHandle(1));
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRetainedMethodSemanticsAssociations: 0));
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                declaration.RelateAccessor(
                    type,
                    method,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension
                .RetainedMethodSemanticsAssociations,
            rejected.Failure.BudgetDimension);
        Assert.NotNull(rejected.Failure.CensusFailure);
    }

    [Fact]
    public void Mdp006_ResultRetentionBudgetIsExact()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(
                        1,
                        1,
                        MethodSemanticsAttributes.Getter),
                ]));
        using var stream = File.OpenRead(fixture.Path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(2));
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(
                reader,
                MetadataTokens.MethodDefinitionHandle(1));
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxStructuredNodes: 0));
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);

        var rejected =
            Assert.IsType<MetadataAccessorAssociationResult.Rejected>(
                declaration.RelateAccessor(
                    type,
                    method,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataAccessorAssociationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorAssociationStage.ResultRetention,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataOperationDimension.StructuredNodes,
            rejected.Failure.BudgetDimension);
        Assert.Equal(0, rejected.Counters.StructuredNodes);
    }

    [Fact]
    public void Mdp009_PostedRelationRemainsDetachedAfterRetirement()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(
                        1,
                        1,
                        MethodSemanticsAttributes.Getter),
                ]));
        using var stream = File.OpenRead(fixture.Path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(2));
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(
                reader,
                MetadataTokens.MethodDefinitionHandle(1));
        var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declaration =
            assembly.CreateDeclarationSession(operation);

        var related =
            Assert.IsType<MetadataAccessorAssociationResult.Related>(
                declaration.RelateAccessor(
                    type,
                    method,
                    TestContext.Current.CancellationToken));

        declaration.Dispose();
        operation.Dispose();
        assembly.Dispose();

        Assert.Equal(type, related.Certificate.Type);
        Assert.Equal(method, related.Certificate.Method);
        Assert.Equal(
            MetadataAccessorSemanticsRole.Getter,
            related.Certificate.Role);
    }

    static MetadataAccessorAssociationResult Relate(
        Fixture fixture,
        int typeRow,
        int methodRow)
    {
        using var stream = File.OpenRead(fixture.Path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(typeRow));
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(
                reader,
                MetadataTokens.MethodDefinitionHandle(methodRow));
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        return declaration.RelateAccessor(
            type,
            method,
            TestContext.Current.CancellationToken);
    }

    static MethodSemanticsRowReaderTests.RawSemanticsRow PropertyRow(
        int methodRow,
        int propertyRow,
        MethodSemanticsAttributes semantics) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Property,
            propertyRow,
            (ushort)semantics);

    static MethodSemanticsRowReaderTests.RawSemanticsRow EventRow(
        int methodRow,
        int eventRow,
        MethodSemanticsAttributes semantics) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Event,
            eventRow,
            (ushort)semantics);

    sealed class Fixture : IDisposable
    {
        internal Fixture(byte[] image)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"accessor-association-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, image);
        }

        internal string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
