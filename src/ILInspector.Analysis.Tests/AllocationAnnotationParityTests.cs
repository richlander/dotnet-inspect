using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class AllocationAnnotationParityTests
{
    [Fact]
    public void CompareJson_ExactAllocationRows_PassesAndIgnoresNonAllocationRows()
    {
        var expected =
            """
            [
              { "id": "alloc.box", "category": "Allocation", "conditionality": "Always", "offset": 12, "detail": "int" },
              { "id": "unsafe.deref", "category": "Unsafety", "conditionality": "Always", "offset": 12, "detail": "int*" }
            ]
            """;
        var actual =
            """
            [
              { "id": "alloc.box", "category": "Allocation", "conditionality": "Always", "offset": 12, "detail": "int" },
              { "id": "lifetime.stack-bound", "category": "Lifetime", "conditionality": "Always", "offset": 1, "detail": "span" }
            ]
            """;

        var result = AllocationAnnotationParity.CompareJson(expected, actual);

        Assert.True(result.Passed, AllocationAnnotationParity.Format(result));
        Assert.Empty(result.Missing);
        Assert.Empty(result.Unexpected);
    }

    [Fact]
    public void Compare_ReportsMissingAndUnexpectedRows()
    {
        var expected = new[]
        {
            new AllocationAnnotationRow("alloc.array", 0x10, "Always", "int[]"),
            new AllocationAnnotationRow("alloc.delegate", 0x20, "CachedOnce", "System.Func<int>"),
        };
        var actual = new[]
        {
            new AllocationAnnotationRow("alloc.array", 0x10, "Always", "int[]"),
            new AllocationAnnotationRow("alloc.new", 0x24, "Always", "Widget"),
        };

        var result = AllocationAnnotationParity.Compare(expected, actual);

        Assert.False(result.Passed);
        Assert.Equal("alloc.delegate", Assert.Single(result.Missing).Id);
        Assert.Equal("alloc.new", Assert.Single(result.Unexpected).Id);
    }

    [Fact]
    public void Compare_DistinguishesDuplicateOccurrences()
    {
        var row = new AllocationAnnotationRow("alloc.box", 0x10, "Always", "int");

        var result = AllocationAnnotationParity.Compare([row, row], [row]);

        Assert.False(result.Passed);
        Assert.Equal(row, Assert.Single(result.Missing));
        Assert.Empty(result.Unexpected);
    }
}
