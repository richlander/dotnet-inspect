using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataAccessorMethodAssociationTests
{
    [Fact]
    public void Mdp004_RelatesEveryLegalRoleToExactAggregate()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 7,
                propertyCount: 1,
                eventCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0002),
                    PropertyRow(2, 0x0001),
                    PropertyRow(3, 0x0004),
                    EventRow(4, 0x0008),
                    EventRow(5, 0x0010),
                    EventRow(6, 0x0020),
                    EventRow(7, 0x0004),
                ]));

        AssertRelated(
            Run(fixture, 1),
            MetadataAccessorDeclarationKind.Property,
            MetadataAccessorSemanticsRole.Getter,
            physicalRow: 5);
        AssertRelated(
            Run(fixture, 2),
            MetadataAccessorDeclarationKind.Property,
            MetadataAccessorSemanticsRole.Setter,
            physicalRow: 6);
        AssertRelated(
            Run(fixture, 3),
            MetadataAccessorDeclarationKind.Property,
            MetadataAccessorSemanticsRole.Other,
            physicalRow: 7);
        AssertRelated(
            Run(fixture, 4),
            MetadataAccessorDeclarationKind.Event,
            MetadataAccessorSemanticsRole.AddOn,
            physicalRow: 1);
        AssertRelated(
            Run(fixture, 5),
            MetadataAccessorDeclarationKind.Event,
            MetadataAccessorSemanticsRole.RemoveOn,
            physicalRow: 2);
        AssertRelated(
            Run(fixture, 6),
            MetadataAccessorDeclarationKind.Event,
            MetadataAccessorSemanticsRole.Fire,
            physicalRow: 3);
        AssertRelated(
            Run(fixture, 7),
            MetadataAccessorDeclarationKind.Event,
            MetadataAccessorSemanticsRole.Other,
            physicalRow: 4);
    }

    [Fact]
    public void CompilerProducedExplicitInterfaceAccessorsRelateExactly()
    {
        string path =
            typeof(ExplicitAccessorFixture).Assembly.Location;
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.Handle(
                typeof(ExplicitAccessorFixture).MetadataToken);
        InterfaceMapping map =
            typeof(ExplicitAccessorFixture).GetInterfaceMap(
                typeof(IExplicitAccessorContract));

        MethodInfo getter = map.TargetMethods.Single(method =>
            method.Name.EndsWith(".get_Value", StringComparison.Ordinal));
        MethodInfo adder = map.TargetMethods.Single(method =>
            method.Name.EndsWith(".add_Changed", StringComparison.Ordinal));
        MethodInfo remover = map.TargetMethods.Single(method =>
            method.Name.EndsWith(
                ".remove_Changed",
                StringComparison.Ordinal));

        MetadataAccessorMethodAssociationResult.Related property =
            AssertRelated(
                getter,
                MetadataAccessorDeclarationKind.Property,
                MetadataAccessorSemanticsRole.Getter);
        MetadataAccessorMethodAssociationResult.Related add =
            AssertRelated(
                adder,
                MetadataAccessorDeclarationKind.Event,
                MetadataAccessorSemanticsRole.AddOn);
        MetadataAccessorMethodAssociationResult.Related remove =
            AssertRelated(
                remover,
                MetadataAccessorDeclarationKind.Event,
                MetadataAccessorSemanticsRole.RemoveOn);

        Assert.NotEqual(
            property.Evidence.Declaration,
            add.Evidence.Declaration);
        Assert.Equal(
            add.Evidence.Declaration,
            remove.Evidence.Declaration);

        MetadataAccessorMethodAssociationResult.Related AssertRelated(
            MethodInfo method,
            MetadataAccessorDeclarationKind kind,
            MetadataAccessorSemanticsRole role)
        {
            var result = Assert.IsType<
                MetadataAccessorMethodAssociationResult.Related>(
                    declaration.RelateAccessorDeclaration(
                        new(
                            MetadataTypeDefinitionAddress.FromHandle(
                                reader,
                                type),
                            MetadataMethodAddress.Create(
                                reader,
                                (MethodDefinitionHandle)
                                    MetadataTokens.Handle(
                                        method.MetadataToken))),
                        TestContext.Current.CancellationToken));
            Assert.Equal(kind, result.Evidence.Declaration.Kind);
            Assert.Equal(role, result.Evidence.Role);
            return result;
        }
    }

    [Fact]
    public void Mdp006_CertifiedAbsenceAndRelatedResultsAreCached()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 2,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0002),
                ]));
        var work = new List<MetadataOperationWorkKind>();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();

        MetadataAccessorMethodAssociationRequest relatedRequest =
            Request(reader, methodRow: 1);
        MetadataAccessorMethodAssociationResult firstRelated =
            declaration.RelateAccessorDeclaration(
                relatedRequest,
                TestContext.Current.CancellationToken);
        MetadataAccessorMethodAssociationResult secondRelated =
            declaration.RelateAccessorDeclaration(
                relatedRequest,
                TestContext.Current.CancellationToken);
        Assert.Same(firstRelated, secondRelated);

        MetadataAccessorMethodAssociationRequest absentRequest =
            Request(reader, methodRow: 2);
        MetadataAccessorMethodAssociationResult firstAbsent =
            declaration.RelateAccessorDeclaration(
                absentRequest,
                TestContext.Current.CancellationToken);
        MetadataAccessorMethodAssociationResult secondAbsent =
            declaration.RelateAccessorDeclaration(
                absentRequest,
                TestContext.Current.CancellationToken);
        Assert.Same(firstAbsent, secondAbsent);
        Assert.IsType<MetadataAccessorMethodAssociationResult.Absent>(
            firstAbsent);

        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .MethodSemanticsAssociationRead));
        Assert.Equal(
            2,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .AccessorMethodAssociationLookupProbe));
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .AccessorMethodAssociationPublication));
    }

    [Fact]
    public void MultiplePhysicalAssociationsRejectAsAmbiguous()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0002),
                    PropertyRow(1, 0x0002),
                ]));

        var rejected = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Rejected>(
                Run(fixture, 1));

        Assert.Equal(
            MetadataAccessorMethodAssociationFailureReason
                .AmbiguousAssociation,
            rejected.Failure.Reason);
        Assert.Equal(1, rejected.Failure.PhysicalRowNumber);
        Assert.NotNull(rejected.Failure.RelevantDeclaration);
    }

    [Fact]
    public void CertifiedAbsenceDoesNotHideMalformedLaterCensusRow()
    {
        byte[] image = MethodSemanticsRowReaderTests.BuildImage(
            methodCount: 2,
            propertyCount: 1,
            rows:
            [
                PropertyRow(2, 0x0002),
                PropertyRow(2, 0x0004),
            ]);
        MethodSemanticsRowReaderTests.PatchMethodRow(
            image,
            methodRow: 0,
            rowIndex: 1);
        using var fixture = new Fixture(image);

        var rejected = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Rejected>(
                Run(fixture, methodRow: 1));

        Assert.Equal(
            MetadataAccessorMethodAssociationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataMethodSemanticsFailureReason.NilMethod,
            rejected.Failure.CensusFailure?.Reason);
        Assert.Equal(2, rejected.Failure.CensusFailure?.RowsVisited);
        Assert.Equal(
            1,
            rejected.Counters.RetainedMethodSemanticsAssociations);
    }

    [Fact]
    public void InvalidRoleRejectsAsMalformedMetadata()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0003),
                ]));

        var rejected = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Rejected>(
                Run(fixture, 1));

        Assert.Equal(
            MetadataAccessorMethodAssociationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorMethodAssociationMechanism.RoleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(
            (ushort)0x0003,
            rejected.Failure.RawSemantics);
    }

    [Fact]
    public void CrossDeclaringTypeAssociationRejectsAsMalformedMetadata()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 2,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(2, 0x0002),
                ],
                secondTypeMethodStart: 2));

        var rejected = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Rejected>(
                Run(fixture, methodRow: 2, typeRow: 3));

        Assert.Equal(
            MetadataAccessorMethodAssociationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorMethodAssociationMechanism.DirectOwnership,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void CensusBudgetRejectionIsPreserved()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0002),
                ]));
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxRetainedMethodSemanticsAssociations: 0);

        var rejected = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Rejected>(
                Run(fixture, 1, policy: policy));

        Assert.Equal(
            MetadataAccessorMethodAssociationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension
                .RetainedMethodSemanticsAssociations,
            rejected.Failure.BudgetDimension);
        Assert.NotNull(rejected.Failure.CensusFailure);
    }

    [Theory]
    [InlineData(
        0,
        MetadataAccessorMethodAssociationStage.RequestValidation)]
    [InlineData(
        1,
        MetadataAccessorMethodAssociationStage.AssociationValidation)]
    public void RelationshipBudgetNamesExactValidationStage(
        long relationshipLimit,
        MetadataAccessorMethodAssociationStage expectedStage)
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0002),
                ]));
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxRelationshipEdges: relationshipLimit,
            maxRetainedMethodSemanticsAssociations: 1);

        var rejected = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Rejected>(
                Run(fixture, 1, policy: policy));

        Assert.Equal(
            MetadataAccessorMethodAssociationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(expectedStage, rejected.Failure.Stage);
        Assert.Equal(
            MetadataAccessorMethodAssociationMechanism.DirectOwnership,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            rejected.Failure.BudgetDimension);
        Assert.Equal(relationshipLimit, rejected.Counters.RelationshipEdges);
    }

    [Fact]
    public void InvalidRequestRejectsBeforeCensusAndRelatedIsDetached()
    {
        using var fixture = new Fixture(
            MethodSemanticsRowReaderTests.BuildImage(
                methodCount: 2,
                propertyCount: 1,
                rows:
                [
                    PropertyRow(1, 0x0002),
                ],
                secondTypeMethodStart: 2));
        var work = new List<MetadataOperationWorkKind>();
        MetadataAccessorMethodAssociationResult.Related detached;
        MetadataDeclarationSession declaration;
        using (var assembly =
            AssemblyInspectionSession.Open(fixture.Path))
        using (var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            work.Add))
        {
            declaration = assembly.CreateDeclarationSession(operation);
            MetadataReader reader =
                assembly.GetMetadataReaderForDeclarationSession();

            var invalid = Assert.IsType<
                MetadataAccessorMethodAssociationResult.Rejected>(
                    declaration.RelateAccessorDeclaration(
                        Request(
                            reader,
                            methodRow: 2,
                            typeRow: 2),
                        TestContext.Current.CancellationToken));
            Assert.Equal(
                MetadataAccessorMethodAssociationFailureReason
                    .InvalidRequest,
                invalid.Failure.Reason);
            Assert.DoesNotContain(
                MetadataOperationWorkKind.MethodSemanticsAssociationRead,
                work);

            detached = Assert.IsType<
                MetadataAccessorMethodAssociationResult.Related>(
                    declaration.RelateAccessorDeclaration(
                        Request(reader, methodRow: 1),
                        TestContext.Current.CancellationToken));
            declaration.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => declaration.RelateAccessorDeclaration(
                    Request(reader, methodRow: 1),
                    TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            MetadataAccessorSemanticsRole.Getter,
            detached.Evidence.Role);
        Assert.Equal(
            MetadataAccessorDeclarationKind.Property,
            detached.Evidence.Declaration.Kind);
    }

    static MetadataAccessorMethodAssociationResult Run(
        Fixture fixture,
        int methodRow,
        int typeRow = 2,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        return declaration.RelateAccessorDeclaration(
            Request(reader, methodRow, typeRow),
            TestContext.Current.CancellationToken);
    }

    static MetadataAccessorMethodAssociationRequest Request(
        MetadataReader reader,
        int methodRow,
        int typeRow = 2) =>
        new(
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(typeRow)),
            MetadataMethodAddress.Create(
                reader,
                MetadataTokens.MethodDefinitionHandle(methodRow)));

    static void AssertRelated(
        MetadataAccessorMethodAssociationResult result,
        MetadataAccessorDeclarationKind kind,
        MetadataAccessorSemanticsRole role,
        int physicalRow)
    {
        var related = Assert.IsType<
            MetadataAccessorMethodAssociationResult.Related>(result);
        Assert.Equal(kind, related.Evidence.Declaration.Kind);
        Assert.Equal(1, related.Evidence.Declaration.RowNumber);
        Assert.Equal(role, related.Evidence.Role);
        Assert.Equal(physicalRow, related.Evidence.PhysicalRowNumber);
    }

    static MethodSemanticsRowReaderTests.RawSemanticsRow PropertyRow(
        int methodRow,
        ushort rawSemantics) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Property,
            1,
            rawSemantics);

    static MethodSemanticsRowReaderTests.RawSemanticsRow EventRow(
        int methodRow,
        ushort rawSemantics) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Event,
            1,
            rawSemantics);

    sealed class Fixture : IDisposable
    {
        internal Fixture(byte[] image)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"accessor-method-association-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, image);
        }

        internal string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    interface IExplicitAccessorContract
    {
        int Value { get; }

        event EventHandler Changed;
    }

    sealed class ExplicitAccessorFixture : IExplicitAccessorContract
    {
        int IExplicitAccessorContract.Value => 42;

        event EventHandler IExplicitAccessorContract.Changed
        {
            add { }
            remove { }
        }
    }
}
