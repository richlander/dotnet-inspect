using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void OptimizationOpportunities_TracksFieldAccessAndClearsStaleConstants()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var fieldAccessMethod = Assert.Single(index.OptimizationOpportunities.Where(opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.MakesArrayAfterFieldAccess)));
        Assert.Equal("small-array", fieldAccessMethod.Shape);

        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.MakesArrayAfterCallAndArgument)
            && opportunity.Shape == "small-array");
    }

    [Fact]
    public void OptimizationOpportunities_PromotesProvablyLocalArrayToStackalloc()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var local = Assert.Single(ArrayShapes(index, nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal)));
        Assert.Equal("stackalloc-candidate", local);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotTrustIncompleteReachingDefinitionsForArrayEscape()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var local = Assert.Single(ArrayShapes(index, nameof(OptimizationOpportunityFixtures.LocalArrayInTryCatch)));
        Assert.Equal("small-array", local);
    }

    [Fact]
    public void OptimizationOpportunities_ReachingDefinitionsSeparateReusedLocalSlots()
    {
        var (path, directory) = BuildSlotReuseArrayFixture();
        try
        {
            var index = LibraryBodyIndex.Open(path);

            var shapes = ArrayShapes(index, "LocalThenEscapingSlotReuse").ToArray();

            Assert.Equal(2, shapes.Length);
            Assert.Contains("stackalloc-candidate", shapes);
            Assert.Contains("small-array", shapes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsIntArray5))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsIntArray5AfterUnrelatedBranch))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsSmallArray))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresArrayToField))]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayPassedToCall))]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalStringArrayStaysLocal))]
    public void OptimizationOpportunities_KeepsEscapingOrIneligibleArrayAsSmallArray(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(ArrayShapes(index, methodName));
        Assert.Equal("small-array", shape);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsConditionalSmallOrHugeArray))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsConditionalHugeOrSmallArray))]
    public void OptimizationOpportunities_DoesNotTreatConditionalLengthArraysAsSmallArrays(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.Empty(ArrayShapes(index, methodName));
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReadsSpanToArrayLocally))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReadsMutableSpanToArrayLocally))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReadsSpanToArrayLengthLocally))]
    public void OptimizationOpportunities_FlagsNonEscapingSpanToArrayCopy(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == methodName && o.Shape == "span-to-array-copy"));
        // The IL offset must point at the real ToArray call (oracle-verifiable), not be inferred.
        Assert.NotNull(opportunity.ILOffset);
        Assert.Equal("medium", opportunity.Confidence);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.MutableSpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanToArrayPassedToArrayApi))]
    public void OptimizationOpportunities_DoesNotFlagEscapingSpanToArrayCopy(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == methodName && o.Shape == "span-to-array-copy");
    }

    [Fact]
    public void OptimizationOpportunities_SpanToArrayReachingDefinitionsTrackStoredLocal()
    {
        var (path, directory) = BuildSpanToArrayLocalFixture();
        try
        {
            var index = LibraryBodyIndex.Open(path);

            Assert.Contains(index.OptimizationOpportunities, o =>
                o.Method.Name == "LocalRead" && o.Shape == "span-to-array-copy");
            Assert.DoesNotContain(index.OptimizationOpportunities, o =>
                o.Method.Name == "LocalReturn" && o.Shape == "span-to-array-copy");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal), AllocationKind.Array, AllocationEscape.LocalOnly)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsSmallArray), AllocationKind.Array, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresArrayToField), AllocationKind.Array, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayPassedToCall), AllocationKind.Array, AllocationEscape.Unknown)]
    [InlineData(nameof(OptimizationOpportunityFixtures.DropsPlainObject), AllocationKind.Object, AllocationEscape.LocalOnly)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsPlainObject), AllocationKind.Object, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresPlainObjectToField), AllocationKind.Object, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.CapturingLambda), AllocationKind.Closure, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.CapturingLambdaWithDependentLocal), AllocationKind.Closure, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ExceptionUnknownOrThrow), AllocationKind.Object, AllocationEscape.Unknown)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesThenUnboxesLocal), AllocationKind.Box, AllocationEscape.LocalOnly)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxThenIsinstReturn), AllocationKind.Box, AllocationEscape.Unknown)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxThenIsinstStoreField), AllocationKind.Box, AllocationEscape.Unknown)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), AllocationKind.Box, AllocationEscape.Escapes)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat), AllocationKind.Box, AllocationEscape.Unknown)]
    public void AllocationOccurrences_ClassifyIntraproceduralEscape(string methodName, AllocationKind kind, AllocationEscape expected)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(index, methodName, kind);

        Assert.Equal(expected, occurrence.Escape);
    }

    [Theory]
    // Refined escape kinds: WHERE an Escapes value escapes (objective, additive on the verdict).
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsSmallArray), AllocationKind.Array, AllocationEscapeKind.Return)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsPlainObject), AllocationKind.Object, AllocationEscapeKind.Return)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), AllocationKind.Box, AllocationEscapeKind.Return)]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresArrayToField), AllocationKind.Array, AllocationEscapeKind.Field)]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresPlainObjectToField), AllocationKind.Object, AllocationEscapeKind.Field)]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresObjectToStaticField), AllocationKind.Object, AllocationEscapeKind.Static)]
    // A user type whose name echoes the closure suffix is a plain Field escape, not Capture.
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresObjectToLookalikeDisplayClassField), AllocationKind.Object, AllocationEscapeKind.Field)]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresObjectIntoArrayElement), AllocationKind.Object, AllocationEscapeKind.Collection)]
    [InlineData(nameof(OptimizationOpportunityFixtures.CapturesArrayInClosure), AllocationKind.Array, AllocationEscapeKind.Capture)]
    // The display-class allocation itself escapes as the target object captured by the delegate.
    [InlineData(nameof(OptimizationOpportunityFixtures.CapturingLambda), AllocationKind.Closure, AllocationEscapeKind.Capture)]
    [InlineData(nameof(OptimizationOpportunityFixtures.CapturingLambdaWithDependentLocal), AllocationKind.Closure, AllocationEscapeKind.Capture)]
    // Multiple distinct escape sinks -> fail-honest None (still an Escapes verdict).
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresToStaticThenReturns), AllocationKind.Object, AllocationEscapeKind.None)]
    // Fail-honest: non-escaping / unknown verdicts carry no kind.
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal), AllocationKind.Array, AllocationEscapeKind.None)]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayPassedToCall), AllocationKind.Array, AllocationEscapeKind.None)]
    public void AllocationOccurrences_RefineEscapeKind(string methodName, AllocationKind kind, AllocationEscapeKind expected)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(index, methodName, kind);

        Assert.Equal(expected, occurrence.EscapeKind);
        // Kind is only meaningful on an Escapes verdict; otherwise it must be None.
        if (occurrence.Escape != AllocationEscape.Escapes)
            Assert.Equal(AllocationEscapeKind.None, occurrence.EscapeKind);
    }

    [Fact]
    public void AllocationOccurrences_MultipleDistinctSinks_FailHonestNoneOnEscapesVerdict()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(
            index,
            nameof(OptimizationOpportunityFixtures.StoresToStaticThenReturns),
            AllocationKind.Object);

        // Escapes to both a static field and the return: the verdict stays Escapes, but
        // the conflicting sinks degrade the refined kind to None (fail-honest).
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.None, occurrence.EscapeKind);
    }

    [Theory]
    [InlineData("YieldsPlainObject")]
    [InlineData("YieldsPlainObjectAsync")]
    public void AllocationOccurrences_IteratorYieldedValue_IsNotLabeledCapture(string iteratorMethod)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // The yielded value is stored into the (sync or async) iterator state machine's
        // <>2__current field. That is not a hoisted capture, so the verdict stays Escapes
        // but the refined kind is fail-honest None rather than Capture.
        var occurrence = index.Methods
            .Where(m => m.DeclaringType.Name.Contains("<" + iteratorMethod + ">", StringComparison.Ordinal)
                && m.Name == "MoveNext")
            .SelectMany(m => index.GetAllocationOccurrences().TryGetValue(m.MetadataToken, out var occ) ? occ : [])
            .Single(o => o.CountsAsHeapAllocation
                && o.Kind == AllocationKind.Object
                && o.AllocatedType?.Name == "PlainObject");

        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.None, occurrence.EscapeKind);
    }

    [Fact]
    public void AllocationOccurrences_ArrayLongAddressLoadsDoNotTerminateTrackedArray()
    {
        var (path, directory) = BuildLongAddressLoadArrayFixture();
        try
        {
            var index = LibraryBodyIndex.Open(path);

            Assert.Equal(
                AllocationEscape.Escapes,
                SingleAllocationOccurrence(index, "LongAddressLoadArrayFixture", "ArrayReturnedAfterLongLdloca", AllocationKind.Array).Escape);
            Assert.Equal(
                AllocationEscape.Escapes,
                SingleAllocationOccurrence(index, "LongAddressLoadArrayFixture", "ArrayReturnedAfterLongLdarga", AllocationKind.Array).Escape);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal), AllocationKind.Array, "System.Int32[]", AllocationPathContext.StraightLine, AllocationPathConfidence.DominatesReturn, AllocationPostDominance.ReturnPostDominates, AllocationMultiplicity.Once)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), AllocationKind.Box, "boxed System.Guid", AllocationPathContext.StraightLine, AllocationPathConfidence.DominatesReturn, AllocationPostDominance.ReturnPostDominates, AllocationMultiplicity.Once)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ThrowsInLoop), AllocationKind.Object, "System.InvalidOperationException", AllocationPathContext.ErrorPath, AllocationPathConfidence.Unknown, AllocationPostDominance.Unknown, AllocationMultiplicity.Unknown)]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesOnceInLoop), AllocationKind.Object, "System.Object", AllocationPathContext.LoopBody, AllocationPathConfidence.BehindBranch, AllocationPostDominance.Unknown, AllocationMultiplicity.Loop)]
    [InlineData(nameof(OptimizationOpportunityFixtures.FinallyAllocates), AllocationKind.Object, "ILInspector.Analysis.Tests.PlainObject", AllocationPathContext.StraightLine, AllocationPathConfidence.DominatesReturn, AllocationPostDominance.Unknown, AllocationMultiplicity.Once)]
    [InlineData(nameof(OptimizationOpportunityFixtures.CatchAllocatesInLoop), AllocationKind.Object, "ILInspector.Analysis.Tests.PlainObject", AllocationPathContext.ErrorPath, AllocationPathConfidence.Unknown, AllocationPostDominance.Unknown, AllocationMultiplicity.Loop)]
    [InlineData(nameof(OptimizationOpportunityFixtures.CatchAllocatesBeforeOnlyReturn), AllocationKind.Object, "ILInspector.Analysis.Tests.PlainObject", AllocationPathContext.ErrorPath, AllocationPathConfidence.Unknown, AllocationPostDominance.Unknown, AllocationMultiplicity.Conditional)]
    public void AllocationOccurrences_IncludeRuntimeTypePathContextConfidenceAndPostDominance(string methodName, AllocationKind kind, string expectedRuntimeType, AllocationPathContext expectedPath, AllocationPathConfidence expectedConfidence, AllocationPostDominance expectedPostDominance, AllocationMultiplicity expectedMultiplicity)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = index.GetAllocationOccurrences()
            .Where(pair => index.Methods.Any(method => method.MetadataToken == pair.Key && method.Name == methodName))
            .SelectMany(pair => pair.Value)
            .First(occurrence => occurrence.Kind == kind
                && occurrence.PathContext == expectedPath
                && occurrence.RuntimeAllocationType == expectedRuntimeType);

        Assert.Equal(expectedRuntimeType, occurrence.RuntimeAllocationType);
        Assert.Equal(expectedPath, occurrence.PathContext);
        Assert.Equal(expectedConfidence, occurrence.PathConfidence);
        Assert.Equal(expectedPostDominance, occurrence.PostDominance);
        Assert.Equal(expectedMultiplicity, occurrence.Multiplicity);
    }

    [Fact]
    public void AllocationOccurrences_EarlyReturnInsideLoop_IsNotLoopMultiplicity()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = index.GetAllocationOccurrences()
            .Where(pair => index.Methods.Any(method => method.MetadataToken == pair.Key
                && method.Name == nameof(OptimizationOpportunityFixtures.ReturnsObjectFromLoop)))
            .SelectMany(pair => pair.Value)
            .Single(occ => occ.CountsAsHeapAllocation
                && occ.Kind == AllocationKind.Object
                && occ.AllocatedType?.Name == "PlainObject");

        // The allocation is returned from inside the loop; the return exits the frame,
        // so it runs at most once -> Conditional (behind the i==5 branch), never Loop.
        Assert.NotEqual(AllocationMultiplicity.Loop, occurrence.Multiplicity);
        Assert.Equal(AllocationMultiplicity.Conditional, occurrence.Multiplicity);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesListOfInt), "System.Int32[]")]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesQueueOfByte), "System.Byte[]")]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesStackOfLong), "System.Int64[]")]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesStringBuilder), "System.Char[]")]
    // Fail-honest: no single known backing store.
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesDictionary), null)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsPlainObject), null)]
    public void AllocationOccurrences_ReportChurnedBackingType(string methodName, string? expectedChurnedType)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(index, methodName, AllocationKind.Object);

        Assert.Equal(expectedChurnedType, occurrence.ChurnedType);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsIntArray10), 64)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsIntArray5), 48)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsIntArray5AfterUnrelatedBranch), 48)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsByteArray100), 128)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsStringArray8), 88)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsLongArray4), 56)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsIntPtrArray3), 48)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsUIntPtrArray3), 48)]
    public void AllocationOccurrences_EstimatesExactSizeForConstantSzArrays(string methodName, int expectedSizeBytes)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(index, methodName, AllocationKind.Array);

        Assert.Equal(expectedSizeBytes, occurrence.EstimatedSizeBytes);
        Assert.Equal(AllocationSizeTier.Exact, occurrence.SizeTier);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsConditionalSmallOrHugeArray))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsConditionalHugeOrSmallArray))]
    public void AllocationFacts_LeaveConditionalLengthArraysUnknown(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var occurrence = SingleAllocationOccurrence(index, methodName, AllocationKind.Array);

        var fact = Assert.Single(SemanticFactProjection.AllocationFacts(
            index.GetAllocationOccurrences(),
            occurrence.Method.MetadataToken,
            occurrence.ILOffset));

        Assert.Null(occurrence.EstimatedSizeBytes);
        Assert.Equal(AllocationSizeTier.Unknown, occurrence.SizeTier);
        Assert.Null(fact.EstimatedSizeBytes);
        Assert.Null(fact.SizeTier);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.MakesArrayAfterCallAndArgument), AllocationKind.Array)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsGuidArray4), AllocationKind.Array)]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), AllocationKind.Box)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsPlainObject), AllocationKind.Object)]
    public void AllocationOccurrences_LeavesLayoutDependentOrNonConstantSizesUnknown(string methodName, AllocationKind kind)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(index, methodName, kind);

        Assert.Null(occurrence.EstimatedSizeBytes);
        Assert.Equal(AllocationSizeTier.Unknown, occurrence.SizeTier);
    }

    [Fact]
    public void AllocationOccurrences_IncludeBranchAndSwitchPathContext()
    {
        var (path, directory) = BuildPathContextFixture();
        try
        {
            var index = LibraryBodyIndex.Open(path);

            Assert.Contains(
                index.GetAllocationOccurrences().Values.SelectMany(occurrences => occurrences),
                occurrence => occurrence.Method.Name == "BranchAllocation"
                    && occurrence.Kind == AllocationKind.Object
                    && occurrence.PathContext == AllocationPathContext.Branch
                    && occurrence.PathConfidence == AllocationPathConfidence.BehindBranch
                    && occurrence.PostDominance == AllocationPostDominance.ReturnPostDominates
                    && occurrence.Multiplicity == AllocationMultiplicity.Conditional);
            Assert.Contains(
                index.GetAllocationOccurrences().Values.SelectMany(occurrences => occurrences),
                occurrence => occurrence.Method.Name == "SwitchAllocation"
                    && occurrence.Kind == AllocationKind.Object
                    && occurrence.PathContext == AllocationPathContext.SwitchArm);
            var switchAllocations = index.GetAllocationOccurrences().Values
                .SelectMany(occurrences => occurrences)
                .Where(occurrence => occurrence.Method.Name == "SwitchAllocation" && occurrence.Kind == AllocationKind.Object)
                .ToArray();
            Assert.Equal(3, switchAllocations.Length);
            Assert.All(switchAllocations, occurrence => Assert.Equal(AllocationPathContext.SwitchArm, occurrence.PathContext));
            Assert.All(switchAllocations, occurrence => Assert.Equal(AllocationPathConfidence.BehindBranch, occurrence.PathConfidence));
            Assert.All(switchAllocations, occurrence => Assert.Equal(AllocationPostDominance.ReturnPostDominates, occurrence.PostDominance));
            Assert.All(switchAllocations, occurrence => Assert.Equal(AllocationMultiplicity.Conditional, occurrence.Multiplicity));
            Assert.Contains(
                index.GetAllocationOccurrences().Values.SelectMany(occurrences => occurrences),
                occurrence => occurrence.Method.Name == "AfterIfJoinAllocation"
                    && occurrence.Kind == AllocationKind.Object
                    && occurrence.PathContext == AllocationPathContext.StraightLine
                    && occurrence.PathConfidence == AllocationPathConfidence.DominatesReturn
                    && occurrence.PostDominance == AllocationPostDominance.ReturnPostDominates);
            Assert.Contains(
                index.GetAllocationOccurrences().Values.SelectMany(occurrences => occurrences),
                occurrence => occurrence.Method.Name == "ReturnOrInfiniteLoopAllocation"
                    && occurrence.Kind == AllocationKind.Object
                    && occurrence.PathContext == AllocationPathContext.StraightLine
                    && occurrence.PathConfidence == AllocationPathConfidence.DominatesReturn
                    && occurrence.PostDominance == AllocationPostDominance.Unknown);
            Assert.Contains(
                index.GetAllocationOccurrences().Values.SelectMany(occurrences => occurrences),
                occurrence => occurrence.Method.Name == "InternalReturnLoopAllocation"
                    && occurrence.Kind == AllocationKind.Object
                    && occurrence.PathContext == AllocationPathContext.StraightLine
                    && occurrence.PathConfidence == AllocationPathConfidence.DominatesReturn
                    && occurrence.PostDominance == AllocationPostDominance.Unknown);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AllocationOccurrences_NestedGenericRuntimeTypeKeepsNestedName()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var occurrence = SingleAllocationOccurrence(
            index,
            nameof(OptimizationOpportunityFixtures.ReturnsNestedGenericObject),
            AllocationKind.Object);

        Assert.Contains("+Inner", occurrence.RuntimeAllocationType, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal), "stackalloc-candidate", "System.Int32[]", "straight-line", "dominates-return", "return-post-dominates")]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), "box-value-type", "boxed System.Guid", "straight-line", "dominates-return", "return-post-dominates")]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesInLoop), "box-value-type", "boxed System.Int32", "loop body", "behind-branch", null)]
    [InlineData(nameof(OptimizationOpportunityFixtures.AppendsStringInLoop), "string-build-in-loop", "System.String", "loop body", "behind-branch", null)]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop), "allocation-hotspot", "newobj/newarr/box", "loop body", null, null)]
    [InlineData(nameof(OptimizationOpportunityFixtures.ContainsKey), "scan-method-in-loop-call", null, "loop body", null, null)]
    public void OptimizationOpportunities_IncludeAllocationPathConfidenceAndPostDominanceMetadata(string methodName, string shape, string? expectedAllocation, string expectedPath, string? expectedConfidence, string? expectedPostDominance)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(opportunity =>
            opportunity.Method.Name == methodName
            && opportunity.Shape == shape));

        Assert.Equal(expectedAllocation, opportunity.RuntimeAllocationType);
        Assert.Equal(expectedPath, opportunity.PathContext);
        Assert.Equal(expectedConfidence, opportunity.PathConfidence);
        Assert.Equal(expectedPostDominance, opportunity.PostDominance);
    }

    [Fact]
    public void OptimizationOpportunities_RetainAllocationFindingProvenance()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.BoxesGuidValue)
            && opportunity.Shape == "box-value-type"));
        var occurrence = Assert.Single(index.GetAllocationOccurrences()[opportunity.Method.MetadataToken]
            .Where(occurrence => occurrence.ILOffset == opportunity.ILOffset));

        Assert.Equal(AnalysisFindings.AllocationDescriptor.Id, opportunity.SourceFinding);
        Assert.Equal(PerformanceTriageProvenance.Exact, opportunity.Provenance);
        Assert.Equal("box", opportunity.Operation);
        Assert.Equal(occurrence.OperandToken, opportunity.OperandToken);
        Assert.StartsWith("pt~", opportunity.CandidateId, StringComparison.Ordinal);
        Assert.Equal(19, opportunity.CandidateId!.Length);
    }

    [Fact]
    public void OptimizationOpportunities_RetainCallSiteFindingProvenance()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.AppendsStringInLoop)
            && opportunity.Shape == "string-build-in-loop"));
        var call = Assert.Single(index.GetDirectCallsByCaller()[opportunity.Method.MetadataToken]
            .Where(call => call.ILOffset == opportunity.ILOffset));

        Assert.Equal(AnalysisFindings.CallSiteDescriptor.Id, opportunity.SourceFinding);
        Assert.Equal(PerformanceTriageProvenance.Exact, opportunity.Provenance);
        Assert.Equal(call.Opcode, opportunity.Operation);
        Assert.Equal(call.OperandToken, opportunity.OperandToken);
        Assert.StartsWith("pt~", opportunity.CandidateId, StringComparison.Ordinal);
        Assert.Equal(19, opportunity.CandidateId!.Length);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop), "allocation-hotspot")]
    [InlineData(nameof(OptimizationOpportunityFixtures.ContainsKey), "scan-method-in-loop-call")]
    public void OptimizationOpportunities_MarkAggregateProvenance(string methodName, string shape)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(opportunity =>
            opportunity.Method.Name == methodName
            && opportunity.Shape == shape));

        Assert.Equal(PerformanceTriageProvenance.Aggregate, opportunity.Provenance);
        Assert.Null(opportunity.SourceFinding);
        Assert.Null(opportunity.Operation);
        Assert.Null(opportunity.OperandToken);
        Assert.NotNull(opportunity.CandidateId);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void OptimizationOpportunities_CandidateIdsAreStableAndUnique()
    {
        string path = typeof(OptimizationOpportunityFixtures).Assembly.Location;
        var first = LibraryBodyIndex.Open(path).OptimizationOpportunities
            .Select(opportunity => opportunity.CandidateId)
            .ToArray();
        var second = LibraryBodyIndex.Open(path).OptimizationOpportunities
            .Select(opportunity => opportunity.CandidateId)
            .ToArray();

        Assert.Equal(first, second);
        Assert.DoesNotContain(first, candidate => candidate is null);
        Assert.Equal(first.Length, first.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagListToArrayAsCopy()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // List<T>.ToArray() is intentionally not promoted (too common to flag without
        // escape/usage analysis), so no span-to-array-copy row is emitted for it.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ListToArrayNotFlagged)
            && o.Shape == "span-to-array-copy");
    }

    [Fact]
    public void OptimizationOpportunities_FlagsLinqMembershipScanInsideLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LinqScanInLoop)
            && o.Shape == "linq-scan-in-loop"));
        Assert.True(op.InLoop);
        Assert.Equal("medium", op.Confidence);
        Assert.Contains("Any", op.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagLinqScanOutsideLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LinqScanOutsideLoop)
            && o.Shape == "linq-scan-in-loop");
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagLazyWhereInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Enumerable.Where is lazy: calling it in a loop does not enumerate, so it is
        // not a repeated scan and must not be flagged.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LazyWhereInLoopNotFlagged)
            && o.Shape == "linq-scan-in-loop");
    }

    [Fact]
    public void OptimizationOpportunities_FlagsScanMethodInvokedInCallerLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ContainsKey)
            && o.Shape == "scan-method-in-loop-call"));
        Assert.True(op.InLoop);
        Assert.Equal("low", op.Confidence);
        Assert.Contains(nameof(OptimizationOpportunityFixtures.CallsScanHelperInLoop), op.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagScanMethodInvokedOnlyOutsideLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ContainsKeyNeverLooped)
            && o.Shape == "scan-method-in-loop-call");
    }

    [Fact]
    public void OptimizationOpportunities_FlagsLazyReturningScanMethodInvokedInCallerLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A helper that returns a deferred Where query, enumerated once per caller-loop
        // iteration, is the cross-method shape of a repeated scan (e.g. Aspire's
        // GetChildSpans().Any()). Flagged at low confidence against the helper.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.FilterLazy)
            && o.Shape == "scan-method-in-loop-call"));
        Assert.Equal("low", op.Confidence);
        Assert.Contains("Where", op.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_FlagsScanMethodInvokedPerRecursiveTraversalNode()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerLoop.AssemblyPath());

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == "GetTraversalChildren"
            && o.Shape == "scan-method-in-recursive-traversal"));
        Assert.True(op.InLoop);
        Assert.Equal("low", op.Confidence);
        Assert.Contains("TraverseWithSequenceScan", op.Evidence, StringComparison.Ordinal);
        Assert.Contains("recursive traversal node", op.Evidence, StringComparison.Ordinal);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == "GetChildrenOutsideTraversal"
            && o.Shape == "scan-method-in-recursive-traversal");
    }

    [Fact]
    public void OptimizationOpportunities_FunctionLoadIsNotARecursiveInvocation()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());

        Assert.Contains(
            index.DirectCalls,
            call =>
                call.Caller.Name
                    == "LoadScanFunctionDuringTraversal"
                && call.Callee.Name
                    == "GetChildrenOutsideTraversal"
                && call.Kind == CallKind.LoadFunction);
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity =>
                opportunity.Method.Name
                    == "GetChildrenOutsideTraversal"
                && opportunity.Shape
                    == "scan-method-in-recursive-traversal");
    }

    [Fact]
    public void OptimizationOpportunities_FlagsImmediateLazyQueryTerminalInvokedInCallerLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A parameterless terminal is a real scan when it directly consumes a lazy Where
        // iterator. This is the Aspire OtlpSpan.GetParentSpan shape from the acceptance case.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.FilterThenFirstOrDefault)
            && o.Shape == "scan-method-in-loop-call"));
        Assert.Equal("low", op.Confidence);
        Assert.Contains("Where+FirstOrDefault", op.Evidence, StringComparison.Ordinal);
        Assert.Contains(nameof(OptimizationOpportunityFixtures.CallsComposedScanHelperInLoop), op.Evidence, StringComparison.Ordinal);
        var support = Assert.IsType<OptimizationSupportingCallSite>(
            op.SupportingCallSite);
        var whereCall = Assert.Single(
            index.GetDirectCallsByCaller()[
                    op.Method.MetadataToken]
                .Where(call =>
                    call.Callee.Name == "Where"));
        var supportCall = Assert.Single(
            index.GetDirectCallsByCaller()[
                    op.Method.MetadataToken]
                .Where(call =>
                    call.Kind == CallKind.NewObject
                    && call.ReturnAddress
                        == whereCall.ILOffset));
        Assert.Equal(
            supportCall.EvidenceMethod.MetadataToken,
            support.EvidenceMethodToken);
        Assert.Equal(
            supportCall.ILOffset,
            support.ILOffset);
        Assert.Equal(
            AnalysisFindings.CallSiteDescriptor.Id,
            support.SourceFinding);
        Assert.Equal(
            supportCall.Opcode,
            support.Operation);
        Assert.Equal(
            supportCall.OperandToken,
            support.OperandToken);
        Assert.Null(op.ILOffset);
        Assert.Null(op.SourceFinding);
        Assert.Equal(
            PerformanceTriageProvenance.Aggregate,
            op.Provenance);
        Assert.Equal(
            PerformanceTriageCandidateId.Create(
                op,
                descriptor: null,
                findingKey: null,
                ordinal: null),
            PerformanceTriageCandidateId.Create(
                op with
                {
                    SupportingCallSite = null,
                },
                descriptor: null,
                findingKey: null,
                ordinal: null));
    }

    [Fact]
    public void OptimizationOpportunities_DoNotChooseAmbiguousScanSupport()
    {
        var index = LibraryBodyIndex.Open(
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location);

        var op = Assert.Single(
            index.OptimizationOpportunities.Where(o =>
                o.Method.Name
                    == nameof(
                        OptimizationOpportunityFixtures
                            .ContainsEither)
                && o.Shape
                    == "scan-method-in-loop-call"));

        Assert.Null(op.SupportingCallSite);
        Assert.Equal(
            PerformanceTriageProvenance.Aggregate,
            op.Provenance);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotJoinUnrelatedLazyQueryAndTerminal()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Where is stored and the terminal consumes a different source. The intervening
        // store/load breaks the immediate stack-chain gate, even when this helper is loop-called.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.UnrelatedLazyAndTerminal)
            && o.Shape == "scan-method-in-loop-call");
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagProjectedFirstAsLinearScan()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Select(...).First() only projects the first element; unlike Where(...).First(),
        // it does not search through the sequence.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ProjectThenFirst)
            && o.Shape == "scan-method-in-loop-call");
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagParameterlessTerminalsInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // First()/Any()/Count() with no predicate are O(1) (positional read or the
        // ICollection.Count fast path), so neither shape may flag them in a loop.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ParameterlessTerminalsInLoopNotFlagged)
            && (o.Shape == "linq-scan-in-loop" || o.Shape == "scan-method-in-loop-call"));
    }

    [Theory]
    [InlineData("System.Linq")]   // .NET 5+ reference assemblies
    [InlineData("System.Core")]   // .NET Framework
    [InlineData("netstandard")]   // canonicalizes to the core library
    public void IsLinqMembershipScan_MatchesPredicateOverloadAcrossTargetFrameworks(string assembly)
    {
        var enumerable = TypeRef.Definition(assembly, "System.Linq", "Enumerable");
        var anyPredicate = new MemberRef(
            enumerable,
            "Any",
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), TypeRef.CoreLib("System", "Func`2")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.True(LibraryBodyIndex.IsLinqMembershipScan(anyPredicate, out var op));
        Assert.Equal("Any", op);
    }

    [Theory]
    [InlineData("Any")]
    [InlineData("First")]
    [InlineData("Single")]
    [InlineData("Count")]
    [InlineData("Last")]
    public void IsLinqMembershipScan_RejectsParameterlessOverload(string name)
    {
        var enumerable = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable");
        var parameterless = new MemberRef(
            enumerable,
            name,
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(parameterless, out _));
    }

    [Fact]
    public void IsLinqMembershipScan_RejectsLazyOperatorAndUserTypeLookalike()
    {
        var enumerable = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable");
        var where = new MemberRef(
            enumerable,
            "Where",
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), TypeRef.CoreLib("System", "Func`2")],
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            MemberKind.Method);
        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(where, out _));

        // A user-defined Enumerable in a different namespace/assembly must not match.
        var userEnumerable = TypeRef.Definition("MyLib", "My.Linq", "Enumerable");
        var userAny = new MemberRef(
            userEnumerable,
            "Any",
            [TypeRef.CoreLib("System", "Object"), TypeRef.CoreLib("System", "Object")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);
        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(userAny, out _));
    }

    [Fact]
    public void IsLinqMembershipScan_RejectsSameNameAssemblyWithoutFrameworkKey()
    {
        // #1708 Row A: an assembly literally named System.Linq but without a framework
        // public-key-token (trusted = false) must not be classified as real LINQ for the
        // #1725 repeated-scan shapes — the matcher must honor the trust gate, not just the
        // simple name.
        var spoof = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable", trustedFrameworkAssembly: false);
        var anyPredicate = new MemberRef(
            spoof,
            "Any",
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), TypeRef.CoreLib("System", "Func`2")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(anyPredicate, out _));
    }

    [Fact]
    public void OptimizationOpportunities_CapturingLambdaIsCapturingDelegate_SingleRow()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(DelegateShapes(index, nameof(OptimizationOpportunityFixtures.CapturingLambda)));
        Assert.Equal("capturing-delegate", shape);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateConfidence_IsLoopGated()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A one-shot capturing delegate (not in a loop) is low-value -> low confidence,
        // especially since .NET 10+ partially stack-allocates non-escaping closures.
        var oneShot = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambda)
            && o.Shape == "capturing-delegate"));
        Assert.False(oneShot.InLoop);
        Assert.Equal("low", oneShot.Confidence);

        // A capturing delegate allocated inside a loop is a repeated allocation -> high.
        var inLoop = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop)
            && o.Shape == "capturing-delegate"));
        Assert.True(inLoop.InLoop);
        Assert.Equal("high", inLoop.Confidence);
    }

    [Fact]
    public void OptimizationOpportunities_AsyncStateMachine_IsAmortized()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var row = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.AsyncStream)
            && o.Shape == "async-state-machine"));
        Assert.False(row.InLoop);
        Assert.True(row.Amortized);
        Assert.Equal("low", row.Confidence);
        Assert.Contains("once per call/enumeration/subscription", row.Caveat);
    }

    [Fact]
    public void OptimizationOpportunities_PlainAsyncTask_IsNotClassStateMachineAllocationInRelease()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.PlainAsyncTask)
            && o.Shape == "async-state-machine");
    }

    [Fact]
    public void OptimizationOpportunities_MaterializeInLoop_RequiresLoopInvariantSource()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var row = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.MaterializesInvariantSourceInLoop)
            && o.Shape == "materialize-in-loop"));
        Assert.True(row.InLoop);
        Assert.Equal("high", row.Confidence);

        var shortArg = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.MaterializesShortFormSourceArgumentInLoop)
            && o.Shape == "materialize-in-loop"));
        Assert.True(shortArg.InLoop);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.MaterializesPerIterationSourceInLoop)
            && o.Shape == "materialize-in-loop");
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.MaterializesSourceMutatedByRefInLoop)
            && o.Shape == "materialize-in-loop");
    }

    [Theory]
    // Non-loop delegate on a high-reach method -> lifted from low to medium.
    [InlineData("capturing-delegate", false, "low", LibraryBodyIndex.DelegateHotRootReach, "medium")]
    [InlineData("instance-method-group-delegate", false, "low", 50, "medium")]
    // Below the reach threshold -> stays low (cold one-shot).
    [InlineData("capturing-delegate", false, "low", LibraryBodyIndex.DelegateHotRootReach - 1, "low")]
    // In-loop (already high) and non-delegate shapes are never adjusted.
    [InlineData("capturing-delegate", true, "high", 99, "high")]
    [InlineData("box-value-type", false, "low", 99, "low")]
    public void AdjustDelegateConfidenceForReach_LiftsHotNonLoopDelegatesOnly(
        string shape, bool inLoop, string confidence, int rootReach, string expected)
    {
        Assert.Equal(expected, LibraryBodyIndex.AdjustDelegateConfidenceForReach(shape, inLoop, confidence, rootReach));
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesRealBlazorRenderMethods()
    {
        // The REAL framework RenderTreeBuilder (trusted public-key-token, from
        // Microsoft.AspNetCore.App) marks a method as Razor render plumbing, so its capturing
        // delegate is suppressed (intrinsic component-model cost, not actionable).
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisRender.AssemblyPath());

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == "RenderWithDelegateLoop");
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name.Contains(
                "RenderGenericEqualityFragment",
                StringComparison.Ordinal));
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name.Contains(
                "RenderGenericEqualityLocal",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OptimizationOpportunities_LookalikeRenderTreeBuilder_IsNotSuppressed()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // RenderLikeMethod takes an UNTRUSTED RenderTreeBuilder lookalike (no framework
        // public-key-token). The render-method suppression is trust-gated (#1708), so this is
        // not mistaken for render plumbing and its in-loop capturing delegate is reported.
        Assert.Contains(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.RenderLikeMethod)
            && o.Shape == "capturing-delegate");
    }

    [Fact]
    public void OptimizationOpportunities_DelegateConsumedByLazyLinq_FixDescribesMovedAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambdaConsumedByWhere)
            && o.Shape == "capturing-delegate"));
        // The surfaced Fix text (not just the dropped Caveat) must convey that the closure
        // rewrite reduces but does not eliminate the allocation (the lazy iterator remains).
        Assert.Contains("reduced, not eliminated", op.SafeFixDirection, StringComparison.Ordinal);
        Assert.Contains("iterator", op.SafeFixDirection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateNotConsumedByLinq_KeepsDefaultFix()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambdaConsumedByNonLinq)
            && o.Shape == "capturing-delegate"));
        Assert.DoesNotContain("iterator", op.SafeFixDirection, StringComparison.OrdinalIgnoreCase);
        // Not consumed by LINQ, so the lazy/iterator override does not fire; the row keeps
        // the default escape-awareness caveat (calibration), not the iterator caveat.
        Assert.NotNull(op.Caveat);
        Assert.Contains("escapes", op.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iterator", op.Caveat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateConsumedByMembershipTerminal_NotGivenLazyIteratorFix()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A predicate consumed by an EAGER membership terminal (Any) allocates no iterator,
        // so it must NOT receive the lazy/iterator "moved allocation" wording. The repeated
        // scan itself is covered separately by the linq-scan-in-loop shape. The row keeps the
        // default escape-awareness caveat, not the iterator caveat.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LinqScanInLoop)
            && o.Shape == "capturing-delegate"));
        Assert.DoesNotContain("iterator", op.SafeFixDirection, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(op.Caveat);
        Assert.Contains("escapes", op.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iterator", op.Caveat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateShapes_CarryEscapeAwarenessCaveat()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Calibration (#1714): on .NET 10+ the JIT stack-allocates non-escaping
        // closures/delegates, so the high-confidence delegate shapes must carry an
        // escape-awareness caveat (mirroring box-value-type), not assert an
        // unconditional allocation.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambda)
            && o.Shape == "capturing-delegate"));
        Assert.NotNull(op.Caveat);
        Assert.Contains("escapes", op.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET 10", op.Caveat, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.NonCapturingLambda))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StaticMethodGroup))]
    public void OptimizationOpportunities_NonCapturingDelegate_NotReported(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Non-capturing lambdas and static method groups are cached by the compiler, so they
        // are not a high-value allocation signal and no delegate row is emitted for them.
        Assert.Empty(DelegateShapes(index, methodName));
    }

    [Fact]
    public void OptimizationOpportunities_CachedInstanceMethodGroup_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A cached stable-receiver method group still uses ldftn/newobj on cache miss, but the
        // surrounding ldsfld/dup/brtrue/stsfld pattern means it is not a per-call allocation.
        Assert.Empty(DelegateShapes(index, nameof(OptimizationOpportunityFixtures.CachedInstanceMethodGroup)));
    }

    [Fact]
    public void OptimizationOpportunities_StackGuardFallbackDelegate_IsCold()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        foreach (var methodName in new[]
        {
            nameof(OptimizationOpportunityFixtures.StackGuardFallback),
            nameof(OptimizationOpportunityFixtures.StackGuardFallbackStoredInvertedCondition),
        })
        {
            var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
                o.Method.Name == methodName
                && o.Shape == "instance-method-group-delegate"));
            Assert.Equal("low", op.Confidence);
            Assert.True(op.ColdPath);
            Assert.Contains("StackGuard fallback", op.SafeFixDirection, StringComparison.Ordinal);
            Assert.Contains("Cold StackGuard fallback", op.Caveat, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OptimizationOpportunities_ConstructorDelegate_IsAmortizedSetup()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.DeclaringType.Name.EndsWith("+AmortizedConstructorFixture", StringComparison.Ordinal)
            && o.Method.Name == ".ctor"
            && o.Shape == "capturing-delegate"));

        Assert.True(op.InLoop);
        Assert.Equal("low", op.Confidence);
        Assert.True(op.Amortized);
        Assert.Contains("constructor/type-initializer setup", op.SafeFixDirection, StringComparison.Ordinal);
        Assert.Contains("Amortized setup", op.Caveat, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_ConstructorCalledInLoop_RemainsHigh()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.DeclaringType.Name.EndsWith("+HotConstructorFixture", StringComparison.Ordinal)
            && o.Method.Name == ".ctor"
            && o.Shape == "capturing-delegate"));

        Assert.True(op.InLoop);
        Assert.Equal("high", op.Confidence);
        Assert.False(op.Amortized);
    }

    [Fact]
    public void OptimizationOpportunities_OrdinaryLoopDelegate_RemainsHigh()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop)
            && o.Shape == "capturing-delegate"));

        Assert.True(op.InLoop);
        Assert.Equal("high", op.Confidence);
        Assert.False(op.Amortized);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.InstanceMethodGroup))]
    [InlineData(nameof(OptimizationOpportunityFixtures.VirtualInstanceMethodGroup))]
    public void OptimizationOpportunities_InstanceMethodGroup_IsInstanceMethodGroupDelegate(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // An instance method group binds a runtime receiver and is never compiler-cached, so
        // it allocates a delegate per call -> a single instance-method-group-delegate row.
        var shape = Assert.Single(DelegateShapes(index, methodName));
        Assert.Equal("instance-method-group-delegate", shape);
    }

    [Fact]
    public void OptimizationOpportunities_ConcurrentDictionaryInstanceFactory_IsCacheLookupDelegate()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ConcurrentDictionaryInstanceFactory)
            && o.Shape == "cache-lookup-factory-delegate"));
        Assert.Equal("high", op.Confidence);
        Assert.Contains("cache hits", op.SafeFixDirection, StringComparison.Ordinal);

        Assert.Contains(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ConcurrentDictionaryStableGetterFactory)
            && o.Shape == "cache-lookup-factory-delegate");

        var lookalike = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.UserGetOrAddInstanceFactory)
            && o.Shape == "instance-method-group-delegate"));
        Assert.DoesNotContain("ConcurrentDictionary", lookalike.Evidence, StringComparison.Ordinal);

        var freshReceiver = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ConcurrentDictionaryFreshReceiverFactory)
            && o.Shape == "instance-method-group-delegate"));
        Assert.DoesNotContain("cache hits", freshReceiver.SafeFixDirection, StringComparison.Ordinal);

        var virtualReceiver = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ConcurrentDictionaryVirtualGetterFactory)
            && o.Shape == "instance-method-group-delegate"));
        Assert.DoesNotContain("cache hits", virtualReceiver.SafeFixDirection, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_StableReceiverGetter_IsClassifiedOnce()
    {
        string path =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle getterHandle = reader.MethodDefinitions
            .Single(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                "get_StableFactory"));
        int classified = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            stableReceiverGetterClassified: handle =>
            {
                if (handle == getterHandle)
                    Interlocked.Increment(ref classified);
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));

        Assert.Equal(1, classified);
    }

    [Fact]
    public void OptimizationOpportunities_AsyncStateMachineTypesArePrewarmedBeforeParallelAnalysis()
    {
        string path =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        Assert.True(reader.MethodDefinitions.Count >= 200);
        int built = 0;
        bool parallelStarted = false;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            asyncStateMachineTypesBuilt:
                () => Interlocked.Increment(ref built),
            parallelBuildStarting: () =>
            {
                parallelStarted = true;
                Assert.Equal(1, Volatile.Read(ref built));
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));

        Assert.True(parallelStarted);
        Assert.Equal(1, built);
    }

    [Fact]
    public void OptimizationOpportunities_UserDisplayClassName_IsInstanceMethodGroupDelegate()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(DelegateShapes(index, nameof(OptimizationOpportunityFixtures.UserTypeNameContainsDisplayClass)));
        Assert.Equal("instance-method-group-delegate", shape);
    }

    [Fact]
    public void OptimizationOpportunities_GenericCachedLambda_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures.GenericOptimizationOpportunityFixtures<>).Assembly.Location);

        Assert.Empty(index.OptimizationOpportunities
            .Where(o => o.Method.DeclaringType.Name.EndsWith("+GenericOptimizationOpportunityFixtures`1", StringComparison.Ordinal)
                && o.Method.Name == nameof(OptimizationOpportunityFixtures.GenericOptimizationOpportunityFixtures<int>.NonCapturingLambda)
                && o.Shape is "delegate-allocation" or "capturing-delegate" or "instance-method-group-delegate"));
    }

    [Fact]
    public void OptimizationOpportunities_CarryContainingMethodRootReach()
    {
        var index = LibraryBodyIndex.Open(typeof(OpportunityLeverageFixtures).Assembly.Location);

        // Root1/Root2 both reach Allocator, so its small-array opportunity should carry the
        // method's Root Reach of 2 (the leverage join), matching the Top Leverage ranking.
        var expected = Assert.Single(
            index.TopLeverage(int.MaxValue, m => m.DeclaringType.Name == nameof(OpportunityLeverageFixtures))
                .Where(e => e.Method.Name == nameof(OpportunityLeverageFixtures.Allocator)));
        Assert.Equal(2, expected.RootReach);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OpportunityLeverageFixtures.Allocator) && o.Shape == "small-array"));
        Assert.Equal(2, opportunity.RootReach);
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesGeneratedActionableRecordMembers()
    {
        var index = LibraryBodyIndex.Open(typeof(OpportunityRecordFixture).Assembly.Location);

        // Record synthesized members (e.g. get_EqualityContract) are [CompilerGenerated],
        // so actionable opportunities are excluded. Exact diagnostic censuses remain visible.
        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Shape
                != AnalysisFindings.StringMaterializationShape
            && opportunity.Method.DeclaringType.Name
                == nameof(OpportunityRecordFixture));
    }

    [Fact]
    public void OptimizationOpportunities_ForeachInterfaceInLoop_IsEnumeratorAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // foreach over an interface-typed sequence inside a loop allocates a reference-type
        // enumerator each outer iteration.
        var row = Assert.Single(EnumeratorRows(index, nameof(OptimizationOpportunityFixtures.ForeachInterfaceInLoop)));
        Assert.Equal("medium", row.Confidence);
        Assert.True(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_ForeachInterfaceOnce_IsNotEnumeratorAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A one-shot foreach (not in a loop) allocates one enumerator -> not flagged (the
        // non-loop tier was measured to be essentially all noise).
        Assert.Empty(EnumeratorRows(index, nameof(OptimizationOpportunityFixtures.ForeachInterfaceOnce)));
    }

    [Fact]
    public void OptimizationOpportunities_ForeachConcreteListInLoop_IsNotEnumeratorAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // foreach over a concrete List<T> uses a struct enumerator (returns by value): no heap
        // allocation, so it must not be flagged even inside a loop.
        Assert.Empty(EnumeratorRows(index, nameof(OptimizationOpportunityFixtures.ForeachConcreteListInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_ForeachLookalikeEnumeratorInLoop_IsNotEnumeratorAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // foreach binding to a GetEnumerator that returns an untrusted IEnumerator lookalike (a
        // user type reusing the framework namespace + name) must not be flagged: the enumerator
        // identity is trust-gated (#1708), so only the real framework IEnumerator counts.
        Assert.Empty(EnumeratorRows(index, nameof(OptimizationOpportunityFixtures.ForeachLookalikeEnumeratorInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_StringAppendInLoop_IsHighStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `s += x` inside a loop is the O(n^2) growing-accumulator anti-pattern.
        var row = Assert.Single(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.AppendsStringInLoop)));
        Assert.Equal("high", row.Confidence);
        Assert.True(row.InLoop);
        Assert.Contains("StringBuilder", row.SafeFixDirection);
    }

    [Fact]
    public void OptimizationOpportunities_StringAppendOfPropertyInLoop_IsHighStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // An intervening sub-expression call (indexer / property get) between the accumulator
        // load and the Concat must not hide the self-accumulation.
        Assert.Single(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.AppendsStringPropertyInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_StringAppendToParameterInLoop_IsHighStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Accumulation into a parameter slot is the same shape as into a local.
        Assert.Single(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.AppendsToParameterInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_ConcatIntoListInLoop_IsNotStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A per-iteration string added to a list is not accumulation -> no StringBuilder fix.
        Assert.Empty(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.ConcatsIntoListInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_ReturnConcatInLoop_IsNotStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A one-time concat on a return path inside a loop is not a repeated copy.
        Assert.Empty(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.ReturnsConcatInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_StringAppendOutsideLoop_IsNotStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `s += x` outside any loop allocates once -> not the StringBuilder anti-pattern.
        Assert.Empty(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.AppendsStringOnce)));
    }

    [Fact]
    public void OptimizationOpportunities_PrependStringInLoop_IsHighStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // The accumulator as the LAST concat argument (`s = x + sep + s`) is still O(n^2).
        Assert.Single(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.PrependsStringInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_ReassignUnrelatedSlotInLoop_IsNotStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `Foo(s); s = a + b;` — `s` is loaded only for the unrelated call, not as a concat
        // argument. The stack-aware check must not misread this as accumulation.
        Assert.Empty(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.ReassignsUnrelatedSlotInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_DerivedAccumulatorInLoop_IsNotStringBuild()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // The accumulator flows through `s.Trim()` before the concat -> conservatively not
        // matched (the bare-load bit does not survive the intermediate call).
        Assert.Empty(StringBuildRows(index, nameof(OptimizationOpportunityFixtures.DerivedAccumulatorInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_AllocationDenseNonLoopMethod_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A dense but NON-loop method is usually intrinsic one-shot construction, not
        // reducible repeated waste -> no hotspot row (avoids flooding allocation-heavy code).
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyObjects)));
    }

    [Fact]
    public void OptimizationOpportunities_AllocationDenseLoop_IsMediumHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Dense allocation inside a loop, matching no specific shape -> a medium hotspot.
        var row = Assert.Single(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)));
        Assert.Equal("medium", row.Confidence);
        Assert.True(row.InLoop);
        Assert.Contains("in a loop", row.Evidence);
    }

    [Fact]
    public void OptimizationOpportunities_AllocationDenseLoopWithSpecificShape_IsDeduped()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // The method is dense-in-loop but already has a specific (capturing-delegate) row, so
        // the vague aggregate hotspot row is deduped away.
        Assert.NotEmpty(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.AllocatesDenselyInLoopWithDelegate)
            && o.Shape == "capturing-delegate"));
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesDenselyInLoopWithDelegate)));
    }

    [Fact]
    public void OptimizationOpportunities_ValueTypeConstructionInLoop_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A loop densely constructing VALUE types (Nullable + an in-assembly struct) clears the
        // >= 16 newobj count but allocates nothing on the heap -> must not be a hotspot.
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ConstructsManyValueTypesInLoop)));
    }

    // #1804 / rung 7 cross-assembly shape honesty: the allocation signal must classify a
    // `newobj` by the constructed type's true shape, resolving what it can from the inspected
    // assembly's own metadata and degrading honestly otherwise.
    [Fact]
    public void Allocations_ClassifiesCrossAndInAssemblyValueTypeNewobj_ByShape()
    {
        var index = LibraryBodyIndex.Open(typeof(CrossAsmShapeConsumer).Assembly.Location);

        // Case 1 (in-assembly struct): resolvable from this assembly's metadata -> not heap.
        Assert.Equal(0, AllocationsOf(index, nameof(CrossAsmShapeConsumer.ConstructsInAssemblyStructInLoop)));

        // Case 2 (cross-assembly GENERIC struct): the consumer's own TypeSpec signature blob
        // encodes VALUETYPE, so the `newobj` is resolved as non-heap even though the defining
        // assembly is not loaded.
        Assert.Equal(0, AllocationsOf(index, nameof(CrossAsmShapeConsumer.ConstructsCrossGenericStructInLoop)));

        // A cross-assembly REFERENCE type's `newobj` is a real heap allocation (recall kept).
        Assert.True(AllocationsOf(index, nameof(CrossAsmShapeConsumer.ConstructsCrossRefTypeInLoop)) >= 1);

        // A cross-assembly enum cast/use allocates nothing.
        Assert.Equal(0, AllocationsOf(index, nameof(CrossAsmShapeConsumer.UsesCrossEnum)));
    }

    [Fact]
    public void Allocations_CrossAssemblyNonGenericStructNewobj_IsOwnedFalsePositive()
    {
        var index = LibraryBodyIndex.Open(typeof(CrossAsmShapeConsumer).Assembly.Location);

        // #1804 / rung 7 owned boundary. A cross-assembly NON-generic user struct is a bare
        // TypeRef whose value-type-ness cannot be proven without loading the referenced
        // assembly (Invariant 1: the product is SRM-direct). Its `newobj` is therefore counted
        // as a heap allocation -- a deliberately-owned false positive, pinned here so the
        // boundary is explicit rather than silent. If referenced-assembly shape resolution is
        // ever added, this assertion flips to 0.
        Assert.True(AllocationsOf(index, nameof(CrossAsmShapeConsumer.ConstructsCrossNonGenericStructInLoop)) >= 1);
    }

    [Fact]
    public void OptimizationOpportunities_LowAllocationMethod_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A method below the allocation threshold does not produce a hotspot row (avoids noise).
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
    }

    [Fact]
    public void OptimizationOpportunities_ManyExceptionArms_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Exception construction only allocates on throw paths, so a method that is mostly
        // `throw new ...` arms is not steady-state allocation pay-dirt and must not be a hotspot.
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyThrowArms)));
    }

    [Fact]
    public void OptimizationOpportunities_PseudoExceptionAllocationsInLoop_AreAllocationHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // Pseudo-exceptions (non-Exception types named like exceptions) are real steady-state
        // allocations and must not be excluded as exception construction.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyPseudoExceptionsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Allocations >= 16, $"expected >= 16 allocations, got {s.Allocations}");

        var row = Assert.Single(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyPseudoExceptionsInLoop)));
        Assert.Equal("medium", row.Confidence);
        Assert.True(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_PlainObjectAllocationsNonLoop_AreNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // Plain custom objects are counted as allocations (not excluded), but a non-loop
        // dense method is no longer flagged as a hotspot.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyPlainObjects)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Allocations >= 8, $"expected >= 8 allocations, got {s.Allocations}");

        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyPlainObjects)));
    }

    [Fact]
    public void OptimizationOpportunities_CustomExceptionThrowArms_AreNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyCustomThrowArms)));
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyDerivedCustomThrowArms)));
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyCrossAssemblyCustomThrowArms)));
    }

    [Fact]
    public void OptimizationOpportunities_BoxIntoObjectApi_IsBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Passing an int to an object-typed API boxes it -> a real heap allocation.
        var row = Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
        Assert.Equal("medium", row.Confidence);
        Assert.False(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_BoxFeedingThrow_IsNotBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A value boxed into an exception message that is thrown is an error-path allocation,
        // not steady-state pay-dirt, so it is suppressed (not just demoted off the loop bit).
        Assert.Empty(BoxRows(index, nameof(OptimizationOpportunityFixtures.ThrowsWithBoxedValue)));
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue))]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesDateTimeValue))]
    public void OptimizationOpportunities_ExternalWellKnownValueTypeBox_IsBoxValueType(string methodName)
    {
        // #1623 rung 3: boxing a referenced (TypeReference) framework struct is recognized
        // only via IsWellKnownValueType's curated set, since the SRM-direct product cannot
        // resolve the external type definition. Recall the curated external value types so
        // dropping one is not silent.
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var row = Assert.Single(BoxRows(index, methodName));
        Assert.Equal("medium", row.Confidence);
        Assert.False(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_BoxInLoop_IsHighConfidence()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Boxing inside a loop is repeated cost -> promoted to high confidence.
        var row = Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesInLoop)));
        Assert.Equal("high", row.Confidence);
        Assert.True(row.InLoop);
    }

    [Fact]
    public void MethodSignals_Allocations_CountBoxing()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // A method that boxes a value type but performs no newobj/newarr must still report a
        // heap allocation in its signals (box allocates, like newobj/newarr).
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Allocations >= 1, $"expected boxing to count as an allocation, got {s.Allocations}");
    }

    [Fact]
    public void MethodSignals_Allocations_AreDerivedFromAllocationOccurrences()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var occurrences = index.GetAllocationOccurrences();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
        var methodOccurrences = Assert.Contains(method.MetadataToken, occurrences);
        Assert.True(signals.TryGetValue(method.MetadataToken, out var signal));

        Assert.Equal(methodOccurrences.Length, signal.Allocations);
        Assert.Contains(methodOccurrences, occurrence => occurrence.Kind == AllocationKind.Box);
        Assert.Contains(methodOccurrences.Select(occurrence => occurrence.ILOffset), offset => signal.Evidence.Contains(offset));
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.UserJoinLookalike))]
    [InlineData(nameof(OptimizationOpportunityFixtures.UserConcatLookalike))]
    [InlineData(nameof(OptimizationOpportunityFixtures.UserSubstringLookalike))]
    public void MethodSignals_UserCopyNameLookalikes_DoNotCountCopies(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        int copies = signals.TryGetValue(method.MetadataToken, out var s) ? s.Copies : 0;
        Assert.Equal(0, copies);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.EnumerableToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ListToArrayNotFlagged))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StringConcatCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StringJoinCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StringSubstringCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.EnumerableToListCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ArrayCopyToCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanCopyToCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.MutableSpanCopyToCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.MutableSpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.RangeSubArrayCopy))]
    public void MethodSignals_FrameworkCopyApis_CountCopies(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s), $"expected copy signal for {methodName}");
        Assert.True(s.Copies >= 1, $"expected at least one copy for {methodName}, got {s.Copies}");
    }

    // #1623 rung 3 / #1715: reflection recall per IsReflectionApi branch. Without these,
    // a regression removing the System.Reflection.* namespace path, the Expressions
    // path, or a System.Type curated-set member would not fail any recall gate.
    [Theory]
    [InlineData(nameof(ReflectionRecallFixtures.InvokesViaReflection), 1)]
    [InlineData(nameof(ReflectionRecallFixtures.EnumeratesAssemblyTypes), 1)]
    [InlineData(nameof(ReflectionRecallFixtures.BuildsExpression), 1)]
    [InlineData(nameof(ReflectionRecallFixtures.CallsTypeMemberSet), 4)]
    public void MethodSignals_ReflectionApis_CountReflection(string methodName, int expected)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s), $"expected reflection signal for {methodName}");
        Assert.True(s.Reflection >= expected, $"expected >= {expected} reflection for {methodName}, got {s.Reflection}");
    }

    [Fact]
    public void MethodSignals_ReflectionDoesNotDependOnAllocationFeature()
    {
        string path = typeof(LibraryBodyIndex).Assembly.Location;
        var compact = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        var classified = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.Allocations);
        IReadOnlyDictionary<int, MethodSignals> compactSignals =
            compact.GetMethodSignals();
        IReadOnlyDictionary<int, MethodSignals> classifiedSignals =
            classified.GetMethodSignals();

        Assert.All(
            classified.Methods,
            method => Assert.Equal(
                classifiedSignals
                    .GetValueOrDefault(method.MetadataToken)
                    ?.Reflection ?? 0,
                compactSignals
                    .GetValueOrDefault(method.MetadataToken)
                    ?.Reflection ?? 0));
    }

    // #1623 rung 3 (signal recall): one consolidated per-signal recall scorecard over a
    // labeled in-assembly fixture. Each row names a method seeded with a known signal and
    // asserts the analysis detects it, so a whole signal family (or one recognized
    // framework-API variant) silently going dark fails here in one place rather than only
    // in a single distant test. Recall is measured on hand-seeded in-assembly fixtures
    // (the SRM-direct no-referenced-assembly-loading boundary applies, per #1623
    // Invariant 1); real-world / corpus recall is rungs 8-10, not this rung.
    [Fact]
    public void Rung3_SignalRecall_DetectsEverySeededSignalFamily()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        MethodSignals Signal(string method)
        {
            var m = Assert.Single(index.Methods.Where(x => x.Name == method));
            Assert.True(signals.TryGetValue(m.MetadataToken, out var s), $"no signals computed for {method}");
            return s!;
        }

        bool HasOpportunity(string method, string shape)
            => index.OptimizationOpportunities.Any(o => o.Method.Name == method && o.Shape == shape);

        bool HasUnsafe(string method)
            => index.UnsafeEvidence.Any(e => e.Member.Name == method);

        // --- MethodSignals families ---
        Assert.True(Signal(nameof(CallTreeFixtures.AllocatesAndCopies)).Allocations >= 2, "object allocation (newobj)");
        Assert.True(Signal(nameof(CallTreeFixtures.AllocatesArray)).Allocations >= 1, "array allocation (newarr)");
        Assert.True(Signal(nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)).Allocations >= 1, "boxing allocation (box)");
        Assert.True(Signal(nameof(CallTreeFixtures.AllocatesAndCopies)).Copies >= 1, "copy signal");
        Assert.True(Signal(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy)).Copies >= 1, "copy: span ToArray");
        Assert.Equal(2, Signal(nameof(CallTreeFixtures.Reflects)).Reflection); // seeded: Type.GetMethods + Activator.CreateInstance
        Assert.True(Signal(nameof(ReflectionRecallFixtures.EnumeratesAssemblyTypes)).Reflection >= 1, "reflection: System.Reflection.* namespace");
        Assert.True(Signal(nameof(ReflectionRecallFixtures.BuildsExpression)).Reflection >= 1, "reflection: System.Linq.Expressions");
        Assert.True(Signal(nameof(ReflectionRecallFixtures.CallsTypeMemberSet)).Reflection >= 4, "reflection: System.Type curated member set");
        Assert.True(HasUnsafe(nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)), "unsafe evidence");
        Assert.True(Signal(nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)).Unsafe, "unsafe signal (folded MethodSignals.Unsafe field)");
        Assert.True(Signal(nameof(CallTreeFixtures.Throws)).Throws >= 1, "throw signal");
        Assert.True(Signal(nameof(CallTreeFixtures.TryCatchFinally)).Catches >= 1, "catch signal");
        Assert.True(Signal(nameof(CallTreeFixtures.TryCatchFinally)).Finallys >= 1, "finally signal");
        Assert.Contains("InvalidOperationException", Signal(nameof(CallTreeFixtures.Throws)).ExceptionTypes);
        Assert.True(Signal(nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)).AllocInLoop, "alloc-in-loop hot path");

        // --- OptimizationOpportunity shapes ---
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop), "allocation-hotspot"), "allocation-hotspot");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal), "stackalloc-candidate"), "stackalloc-candidate");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.ReadsSpanToArrayLocally), "span-to-array-copy"), "span-to-array-copy");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.FirstValueByte), "temporary-byte-array-copy"), "temporary-byte-array-copy");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat), "box-value-type"), "box-value-type");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), "box-value-type"), "box: external well-known value type (Guid)");

        // A dense in-loop allocation hotspot (matching no specific shape) is recalled at medium.
        Assert.Contains(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)
            && o.Shape == "allocation-hotspot" && o.InLoop && o.Confidence == "medium");
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueForLoopAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForOneTimeAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyObjects)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueBelowHotspotThreshold_WithoutOpportunity()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // The key fidelity case: a single allocation in a loop is hot, but below the
        // hotspot threshold and matching no shape, so it surfaces no opportunity. The
        // loop bit must still be set (it is not gated on the opportunity machinery).
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesOnceInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesOnceInLoop)));
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueForInAssemblyPseudoExceptionLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // #1607: a same-assembly type whose name ends with Exception is not an
        // exception unless the metadata base chain proves it derives from System.Exception.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesPseudoExceptionOnceInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
        Assert.DoesNotContain(nameof(PseudoException), s.ExceptionTypes);
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesPseudoExceptionOnceInLoop)));
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForExceptionConstructionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // Exception construction inside a loop only allocates on the throw path, so the
        // hot-allocation bit must exclude it.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.ThrowsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForInitializedExceptionConstructionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.ThrowsInitializedExceptionInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForConditionalExceptionConstructionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.ThrowsConditionalExceptionInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueForRetainedExceptionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // A real exception retained (stored) per iteration is a steady-state hot
        // allocation, distinct from throw-path construction, so the loop bit is set (#1610).
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.RetainsExceptionsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
    }

    [Fact]
    public void OptimizationOpportunities_BoxOnThrowPathInLoop_IsSuppressed()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A box that feeds an exception message only allocates on the throw path, so it is not
        // steady-state pay-dirt even inside a loop -> suppressed entirely (the throw-probe now
        // gates emission, not just the loop bit).
        Assert.Empty(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesIntoThrowMessage)));
    }

    [Fact]
    public void OptimizationOpportunities_GenericParameterBox_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `box !!T` is compiler-mandated and JIT-specialized; not a user-actionable allocation.
        Assert.Empty(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesGenericParameter)));
    }

    [Fact]
    public void OptimizationOpportunities_GenericObjectEqualsBox_IsReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var row = Assert.Single(GenericObjectBoxRows(
            index,
            nameof(OptimizationOpportunityFixtures.GenericObjectEquals)));
        Assert.Equal("medium", row.Confidence);
        Assert.Contains("value-type instantiations", row.Caveat);
        Assert.Null(row.RuntimeAllocationType);
        Assert.Null(row.SourceFinding);
        Assert.Null(row.Operation);
        Assert.Equal(PerformanceTriageProvenance.Unmatched, row.Provenance);
        Assert.Null(row.Weight);
        Assert.Null(row.EstimatedSizeBytes);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReferenceGenericObjectEquals))]
    [InlineData(nameof(OptimizationOpportunityFixtures.NamedClassGenericObjectEquals))]
    [InlineData(nameof(OptimizationOpportunityFixtures.GenericTypedEquals))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StaticObjectEquals))]
    [InlineData(nameof(OptimizationOpportunityFixtures.PassesGenericToObjectConsumer))]
    [InlineData(nameof(OptimizationOpportunityFixtures.CompilerGeneratedOrdinaryGenericObjectEquals))]
    public void OptimizationOpportunities_GenericObjectEqualsNearMiss_NotReported(
        string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.Empty(GenericObjectBoxRows(index, methodName));
    }

    [Fact]
    public void OptimizationOpportunities_NullableBox_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `box Nullable<T>` pushes null (no allocation) when the value is absent, so it is
        // conservatively not reported.
        Assert.Empty(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesNullable)));
    }

    [Fact]
    public void OptimizationOpportunities_InAssemblyStructBox_IsBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A non-generic in-assembly struct is positively identified as a value type via its
        // System.ValueType base, so boxing it into an object-typed API is reported.
        Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesInAssemblyStruct)));
    }

    [Fact]
    public void OptimizationOpportunities_GenericStructBox_IsBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A constructed generic struct boxes via a TypeSpec; value-type-ness is read from the
        // signature blob, so it is reported (not missed for lacking a well-known name).
        Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesGenericStruct)));
    }

    [Fact]
    public void OptimizationOpportunities_BitConverterGetBytes_IsTemporaryByteArrayCopy()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // BitConverter.GetBytes allocates a transient byte[] that is usually replaceable by
        // BinaryPrimitives.Write* / a stackalloc span.
        Assert.Contains(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.FirstValueByte)
            && o.Shape == "temporary-byte-array-copy");
    }

    [Fact]
    public void FrameworkApiPredicates_AcceptRealFrameworkApis()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var signals = index.GetMethodSignals();
        var reflects = Assert.Single(index.Methods.Where(method =>
            method.Name == nameof(CallTreeFixtures.Reflects)));
        Assert.True(signals.TryGetValue(reflects.MetadataToken, out var reflectSignals));
        Assert.Equal(2, reflectSignals.Reflection);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)
            && evidence.Reason == "Unsafe call"
            && evidence.Detail.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", StringComparison.Ordinal));

        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.FirstValueByte)
            && opportunity.Shape == "temporary-byte-array-copy");
    }

    [Fact]
    public void FrameworkApiPredicates_IgnoreUserDefinedLookalikes()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisLookalike.AssemblyPath());
        var signals = index.GetMethodSignals();

        var fakeReflection = Assert.Single(index.Methods.Where(method =>
            method.Name == "CallsFakeReflection"));
        signals.TryGetValue(fakeReflection.MetadataToken, out var fakeReflectionSignals);
        Assert.Equal(0, fakeReflectionSignals?.Reflection ?? 0);

        Assert.DoesNotContain(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == "CallsFakeUnsafe"
            && evidence.Reason == "Unsafe call");

        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.Name == "CallsFakeBitConverter"
            && opportunity.Shape == "temporary-byte-array-copy");
    }

    // #1708 Row A end-to-end. A real assembly literally named "System.Linq" (unsigned,
    // so no framework public-key-token) exposes a System.Linq.Enumerable.ToArray
    // lookalike. Simple-name identity accepted it as a framework copy; strong
    // (public-key-token) identity must reject it. The decoder lowers
    // TrustedFrameworkAssembly from the AssemblyDefinition's (empty) key.
    [Fact]
    public void MethodSignals_CopyApis_RejectSimpleNameSpoofWithoutFrameworkKey()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisSpoofSystemLinq.AssemblyPath());
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == "CallsFakeEnumerableToArray"));

        int copies = signals.TryGetValue(method.MetadataToken, out var s) ? s.Copies : 0;
        Assert.Equal(0, copies);
    }

    // #1708 Row A, span-to-array opportunity path. An assembly named "System.Runtime"
    // (unsigned -> canonicalizes to corelib, no framework key) exposes a System.Span<T>
    // lookalike. The span-to-array-copy opportunity must require a trusted framework key,
    // not just the corelib-canonical name.
    [Fact]
    public void OptimizationOpportunities_SpanToArray_RejectSimpleNameSpoofWithoutFrameworkKey()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisSpoofSystemRuntime.AssemblyPath());

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == "CallsFakeSpanToArray" && o.Shape == "span-to-array-copy");
    }
    // netstandard facade (assembly canonicalizes to corelib), reproducing the
    // legacy-TFM recall gap where copy predicates expected the modern split assembly.
    [Theory]
    [InlineData("EnumerableToArrayCopy")]
    [InlineData("EnumerableToListCopy")]
    public void MethodSignals_CopyApis_RecognizeLegacyFacadeAssemblies(string methodName)
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisFacade.AssemblyPath());
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s), $"no signals for {methodName}");
        Assert.True(s.Copies >= 1, $"expected copy on facade assembly for {methodName}, got {s.Copies}");
    }

    [Fact]
    public void MethodSignals_Reflection_RecognizesLegacyFacadeExpressions()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisFacade.AssemblyPath());
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == "BuildsExpression"));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Reflection >= 1, $"expected reflection on facade assembly, got {s.Reflection}");
    }

    // #1708 Row B (.NET Framework path). Real net48 assemblies cannot be built on this
    // host, so exercise the predicate directly: Enumerable lives in System.Core on net48
    // and in the netstandard/corelib facade bucket on netstandard, yet a same-named user
    // assembly must still be rejected (precision preserved).
    [Theory]
    [InlineData("System.Linq", 1)]   // modern split assembly
    [InlineData("System.Core", 1)]   // .NET Framework facade
    [InlineData("netstandard", 1)]   // canonicalizes to corelib
    [InlineData("Contoso.Data", 0)]  // user assembly lookalike -> not a copy
    public void Collect_CopyApiRecall_HonorsFrameworkFacadesButNotUserLookalikes(string calleeAssembly, int expectedCopies)
    {
        const int callerToken = 0x06000123;
        var calls = ImmutableArray.Create(EnumerableToArrayCall(calleeAssembly, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int copies = signals.TryGetValue(callerToken, out var s) ? s.Copies : 0;
        Assert.Equal(expectedCopies, copies);
    }

    // #1708 Row B reflection path. Expression trees live in System.Core on net48 and in
    // the netstandard/corelib facade bucket on netstandard, but a same-named user
    // assembly must not be classified as reflection.
    [Theory]
    [InlineData("System.Linq.Expressions", 1)] // modern split assembly
    [InlineData("System.Core", 1)]             // .NET Framework facade
    [InlineData("netstandard", 1)]             // canonicalizes to corelib
    [InlineData("Contoso.Expr", 0)]            // user assembly lookalike -> not reflection
    public void Collect_ReflectionRecall_HonorsFrameworkFacadesButNotUserLookalikes(string calleeAssembly, int expectedReflection)
    {
        const int callerToken = 0x06000124;
        var calls = ImmutableArray.Create(ExpressionConstantCall(calleeAssembly, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int reflection = signals.TryGetValue(callerToken, out var s) ? s.Reflection : 0;
        Assert.Equal(expectedReflection, reflection);
    }

    // #1708 Row A. The same framework-named assembly is accepted only when it carries a
    // known framework public-key-token (TrustedFrameworkAssembly). A simple-name match
    // alone (trusted = false) must not fire the copy or reflection signal.
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Collect_CopyApi_RequiresTrustedFrameworkAssembly(bool trusted, int expectedCopies)
    {
        const int callerToken = 0x06000201;
        var enumerable = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable", trustedFrameworkAssembly: trusted);
        var callee = new MemberRef(enumerable, "ToArray", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        var calls = ImmutableArray.Create(FrameworkCall(callee, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int copies = signals.TryGetValue(callerToken, out var s) ? s.Copies : 0;
        Assert.Equal(expectedCopies, copies);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Collect_ReflectionApi_RequiresTrustedFrameworkAssembly(bool trusted, int expectedReflection)
    {
        const int callerToken = 0x06000202;
        var expression = TypeRef.Definition("System.Linq.Expressions", "System.Linq.Expressions", "Expression", trustedFrameworkAssembly: trusted);
        var callee = new MemberRef(expression, "Constant", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        var calls = ImmutableArray.Create(FrameworkCall(callee, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int reflection = signals.TryGetValue(callerToken, out var s) ? s.Reflection : 0;
        Assert.Equal(expectedReflection, reflection);
    }
}
