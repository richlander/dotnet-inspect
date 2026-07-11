using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Analysis.Tests;

public class AnalysisFindingsTests
{
    static readonly FindingSubject Subject = new("method:test", "Test.Method()");

    [Fact]
    public void InspectAllocations_ProducesCompleteIlOrderedCensus()
    {
        var later = Occurrence(12, AllocationKind.Box, TypeRef.CoreLib("System", "Int32"));
        var earlier = Occurrence(4, AllocationKind.Object, TypeRef.CoreLib("System", "Object"));

        var inspection = AnalysisFindings.InspectAllocations([later, earlier], Subject);

        Assert.Collection(
            inspection,
            finding =>
            {
                Assert.Same(earlier, finding.Payload);
                Assert.Equal(0, finding.Ordinal);
                Assert.Equal(AnalysisFindings.AllocationDescriptor, finding.Descriptor);
            },
            finding =>
            {
                Assert.Same(later, finding.Payload);
                Assert.Equal(1, finding.Ordinal);
            });
    }

    [Fact]
    public void InspectAllocations_EmptySequence_IsComplete()
    {
        var inspection = AnalysisFindings.InspectAllocations([], Subject);

        Assert.Empty(inspection);
    }

    [Fact]
    public void CompareAllocations_IgnoresVersionLocalProvenance()
    {
        var oldOccurrence = Occurrence(4, AllocationKind.Object, TypeRef.CoreLib("System", "Object"));
        var newOccurrence = oldOccurrence with
        {
            Method = oldOccurrence.Method with
            {
                ModuleVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                MetadataToken = 0x06000002,
            },
            ILOffset = 20,
            OperandToken = 0x0A000002,
        };

        var comparison = AnalysisFindings.CompareAllocations([oldOccurrence], [newOccurrence], Subject);

        var complete = CompleteComparison(comparison);
        var present = Assert.Single(complete.Pairs) switch
        {
            PairFinding<AllocationOccurrence>.Present value => value,
            _ => throw new InvalidOperationException("Expected a present allocation."),
        };
        Assert.Equal(PairKind.Present, present.Kind);
        Assert.Equal(FindingDifferenceKind.None, present.Difference);
    }

    [Fact]
    public void CompareAllocations_ClassifiesSemanticFacetChange()
    {
        var oldOccurrence = Occurrence(4, AllocationKind.Object, TypeRef.CoreLib("System", "Object"));
        var newOccurrence = oldOccurrence with
        {
            ILOffset = 8,
            InLoop = true,
            PathContext = AllocationPathContext.LoopBody,
            Multiplicity = AllocationMultiplicity.Loop,
        };

        var comparison = AnalysisFindings.CompareAllocations([oldOccurrence], [newOccurrence], Subject);

        var complete = CompleteComparison(comparison);
        var changed = Assert.Single(complete.Pairs) switch
        {
            PairFinding<AllocationOccurrence>.Changed value => value,
            _ => throw new InvalidOperationException("Expected a changed allocation."),
        };
        Assert.Contains("in loop: False -> True", changed.Detail);
        Assert.Contains("multiplicity: Unknown -> Loop", changed.Detail);
    }

    [Fact]
    public void CompareAllocations_EqualCountsWithDifferentIdentity_AreRemovedAndAdded()
    {
        var oldOccurrence = Occurrence(4, AllocationKind.Object, TypeRef.CoreLib("System", "Object"));
        var newOccurrence = Occurrence(4, AllocationKind.Array, TypeRef.SzArray(TypeRef.CoreLib("System", "Byte")));

        var comparison = AnalysisFindings.CompareAllocations([oldOccurrence], [newOccurrence], Subject);

        var complete = CompleteComparison(comparison);
        Assert.Contains(complete.Pairs, pair => pair is PairFinding<AllocationOccurrence>.Removed);
        Assert.Contains(complete.Pairs, pair => pair is PairFinding<AllocationOccurrence>.Added);
        Assert.DoesNotContain(complete.Pairs, pair => pair is PairFinding<AllocationOccurrence>.Present);
    }

    static FindingComparison<AllocationOccurrence>.Complete CompleteComparison(
        FindingComparison<AllocationOccurrence> comparison)
        => comparison switch
        {
            FindingComparison<AllocationOccurrence>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete allocation comparison."),
        };

    static AllocationOccurrence Occurrence(int offset, AllocationKind kind, TypeRef type)
        => new(
            new MethodIdentity(
                "TestAssembly",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TypeRef.CoreLib("Test", "Fixture"),
                "Method",
                ImmutableArray<TypeRef>.Empty,
                TypeRef.CoreLib("System", "Void"),
                0x06000001,
                IsStatic: true),
            offset,
            0x0A000001,
            kind,
            type,
            type.ToDisplayString(),
            CountsAsHeapAllocation: true,
            AllocationFrequency.Always,
            InLoop: false,
            AllocationEscape.Unknown,
            kind switch
            {
                AllocationKind.Array => AllocationFactSource.Newarr,
                AllocationKind.Box => AllocationFactSource.Box,
                AllocationKind.Enumerator => AllocationFactSource.GetEnumeratorCall,
                _ => AllocationFactSource.Newobj,
            });
}
