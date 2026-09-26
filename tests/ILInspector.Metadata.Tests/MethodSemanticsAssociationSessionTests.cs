using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MethodSemanticsAssociationSessionTests
{
    [Fact]
    public void Mdp006_CompletedReceiptPreservesPhysicalRowsAndCachesCharge()
    {
        byte[] image = MethodSemanticsRowReaderTests.BuildImage(
            methodCount: 3,
            propertyCount: 2,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x0003),
                new(
                    2,
                    MethodSemanticsAssociationKind.Property,
                    2,
                    0x0000),
                new(
                    3,
                    MethodSemanticsAssociationKind.Property,
                    2,
                    0x8000),
            ]);
        MethodSemanticsRowReaderTests.PatchAssociation(
            image,
            encodedAssociation: 5,
            rowIndex: 0);
        MethodSemanticsRowReaderTests.PatchAssociation(
            image,
            encodedAssociation: 3,
            rowIndex: 1);
        using var fixture = new TemporaryAssemblyImage(image);
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRetainedMethodSemanticsAssociations: 3),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        long admittedRows = operation.Counters.MetadataRows;

        MethodSemanticsAssociationSession session =
            declaration.MethodSemanticsAssociations;
        var first = Assert.IsType<
            MetadataMethodSemanticsAssociationResult.Completed>(
                session.Post());
        var second = session.Post();

        Assert.Same(first, second);
        Assert.False(first.AssociationsAreNondecreasing);
        Assert.Equal(
            [
                (1, 0x0003, 1, 2),
                (2, 0x0000, 2, 1),
                (3, 0x8000, 3, 2),
            ],
            first.Associations
                .Select(association => (
                    association.PhysicalRowNumber,
                    (int)association.RawSemantics,
                    MetadataTokens.GetRowNumber(association.Method),
                    association.AssociationRowNumber))
                .ToArray());
        Assert.All(
            first.Associations,
            association => Assert.Equal(
                MetadataMethodSemanticsAssociationKind.Property,
                association.AssociationKind));
        Assert.Equal(
            3,
            first.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            3,
            operation.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(admittedRows, operation.Counters.MetadataRows);
        Assert.Equal(
            [MetadataOperationWorkKind.MethodSemanticsAssociationRead],
            work);
    }

    [Fact]
    public void Mdp006_BelowLimitPostsTypedCachedRejection()
    {
        using var fixture = new TemporaryAssemblyImage(
            BuildPropertyRows(3));
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRetainedMethodSemanticsAssociations: 2),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MethodSemanticsAssociationSession session =
            declaration.MethodSemanticsAssociations;

        var first = Assert.IsType<
            MetadataMethodSemanticsAssociationResult.Rejected>(
                session.Post());
        var second = session.Post();

        Assert.Same(first, second);
        Assert.Equal(
            MetadataMethodSemanticsFailureReason.BudgetExceeded,
            first.Failure.Reason);
        Assert.Equal(3, first.Failure.RowsVisited);
        Assert.Equal(3, first.Failure.PhysicalRowNumber);
        Assert.Equal(
            MetadataOperationDimension
                .RetainedMethodSemanticsAssociations,
            first.Failure.BudgetDimension);
        Assert.Equal(2, first.Failure.BudgetLimit);
        Assert.Equal(1, first.Failure.AttemptedCharge);
        Assert.Equal(
            2,
            first.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            2,
            operation.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            [MetadataOperationWorkKind.MethodSemanticsAssociationRead],
            work);
    }

    [Fact]
    public void Mdp006_MalformedLaterRowPreservesPartialAccounting()
    {
        byte[] image = BuildPropertyRows(2);
        MethodSemanticsRowReaderTests.PatchMethodRow(
            image,
            methodRow: 0,
            rowIndex: 1);
        using var fixture = new TemporaryAssemblyImage(image);
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRetainedMethodSemanticsAssociations: 10),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MethodSemanticsAssociationSession session =
            declaration.MethodSemanticsAssociations;

        var first = Assert.IsType<
            MetadataMethodSemanticsAssociationResult.Rejected>(
                session.Post());
        var second = session.Post();

        Assert.Same(first, second);
        Assert.Equal(
            MetadataMethodSemanticsFailureReason.NilMethod,
            first.Failure.Reason);
        Assert.Equal(2, first.Failure.RowsVisited);
        Assert.Equal(2, first.Failure.PhysicalRowNumber);
        Assert.Null(first.Failure.BudgetDimension);
        Assert.Equal(
            1,
            first.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            1,
            operation.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            [MetadataOperationWorkKind.MethodSemanticsAssociationRead],
            work);
    }

    [Fact]
    public void Mdp006_SharedOperationUsesRemainingAssociationCapacity()
    {
        using var fixture = new TemporaryAssemblyImage(
            BuildPropertyRows(2));
        using var firstAssembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var secondAssembly =
            AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRetainedMethodSemanticsAssociations: 3),
            work.Add);
        using MetadataDeclarationSession firstDeclaration =
            firstAssembly.CreateDeclarationSession(operation);
        using MetadataDeclarationSession secondDeclaration =
            secondAssembly.CreateDeclarationSession(operation);

        var first = Assert.IsType<
            MetadataMethodSemanticsAssociationResult.Completed>(
                firstDeclaration.MethodSemanticsAssociations.Post());
        var second = Assert.IsType<
            MetadataMethodSemanticsAssociationResult.Rejected>(
                secondDeclaration.MethodSemanticsAssociations.Post());

        Assert.Equal(
            2,
            first.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            MetadataMethodSemanticsFailureReason.BudgetExceeded,
            second.Failure.Reason);
        Assert.Equal(2, second.Failure.RowsVisited);
        Assert.Equal(3, second.Failure.BudgetLimit);
        Assert.Equal(1, second.Failure.AttemptedCharge);
        Assert.Equal(
            3,
            second.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            3,
            operation.Counters.RetainedMethodSemanticsAssociations);
        Assert.Equal(
            [
                MetadataOperationWorkKind.MethodSemanticsAssociationRead,
                MetadataOperationWorkKind.MethodSemanticsAssociationRead,
            ],
            work);
    }

    [Fact]
    public void Mdp006_ImageAdmissionRejectionAvoidsAssociationWork()
    {
        using var fixture = new TemporaryAssemblyImage(
            BuildPropertyRows(2));
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                maxMetadataRows: 0,
                maxRetainedMethodSemanticsAssociations: 2),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);

        var result = Assert.IsType<
            MetadataMethodSemanticsAssociationResult.Rejected>(
                declaration.MethodSemanticsAssociations.Post());

        Assert.Equal(
            MetadataMethodSemanticsFailureReason.BudgetExceeded,
            result.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.MetadataRows,
            result.Failure.BudgetDimension);
        Assert.Equal(0, result.Counters.MetadataRows);
        Assert.Equal(
            0,
            result.Counters.RetainedMethodSemanticsAssociations);
        Assert.Empty(work);
    }

    [Theory]
    [InlineData(SessionRetirement.Declaration, false)]
    [InlineData(SessionRetirement.Operation, false)]
    [InlineData(SessionRetirement.Assembly, false)]
    [InlineData(SessionRetirement.Lender, false)]
    [InlineData(SessionRetirement.Declaration, true)]
    [InlineData(SessionRetirement.Operation, true)]
    [InlineData(SessionRetirement.Assembly, true)]
    [InlineData(SessionRetirement.Lender, true)]
    public void Mdp009_CachedReceiptRequiresLiveOwner(
        SessionRetirement retirement,
        bool rejected)
    {
        using var fixture = new TemporaryAssemblyImage(
            BuildPropertyRows(2));
        PdbContext? lender = null;
        AssemblyInspectionSession assembly;
        if (retirement == SessionRetirement.Lender)
        {
            lender = PdbContext.Open(fixture.Path);
            assembly = AssemblyInspectionSession.Borrow(lender);
        }
        else
        {
            assembly = AssemblyInspectionSession.Open(fixture.Path);
        }

        var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                long.MaxValue,
                maxRetainedMethodSemanticsAssociations:
                    rejected ? 1 : 2));
        MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MethodSemanticsAssociationSession associationSession =
            declaration.MethodSemanticsAssociations;
        MetadataMethodSemanticsAssociationResult detached =
            associationSession.Post();
        if (rejected)
        {
            Assert.IsType<
                MetadataMethodSemanticsAssociationResult.Rejected>(
                    detached);
        }
        else
        {
            Assert.IsType<
                MetadataMethodSemanticsAssociationResult.Completed>(
                    detached);
        }

        try
        {
            switch (retirement)
            {
                case SessionRetirement.Declaration:
                    declaration.Dispose();
                    break;
                case SessionRetirement.Operation:
                    operation.Dispose();
                    break;
                case SessionRetirement.Assembly:
                    assembly.Dispose();
                    break;
                case SessionRetirement.Lender:
                    lender!.Dispose();
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown session retirement.");
            }

            Assert.Throws<ObjectDisposedException>(
                () => associationSession.Post());
            Assert.True(
                detached.Counters
                    .RetainedMethodSemanticsAssociations > 0);
        }
        finally
        {
            declaration.Dispose();
            operation.Dispose();
            assembly.Dispose();
            lender?.Dispose();
        }
    }

    static byte[] BuildPropertyRows(int count) =>
        MethodSemanticsRowReaderTests.BuildImage(
            methodCount: count,
            propertyCount: 1,
            rows:
            [
                .. Enumerable.Range(1, count)
                    .Select(row => new MethodSemanticsRowReaderTests
                        .RawSemanticsRow(
                            row,
                            MethodSemanticsAssociationKind.Property,
                            1,
                            0x0002)),
            ]);

    public enum SessionRetirement
    {
        Declaration,
        Operation,
        Assembly,
        Lender,
    }

    sealed class TemporaryAssemblyImage : IDisposable
    {
        public TemporaryAssemblyImage(byte[] image)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"method-semantics-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, image);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
