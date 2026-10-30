using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

/// <summary>
/// Preset gate for docs/design/notable-findings.md#the-notable-set: the
/// notable set admits exactly the Hidden, Amplified, and Contract cases and
/// demotes straight-line visible allocations, error-path, and cached-once
/// facts, reading demotion before amplification.
/// </summary>
public class NotableFindingPresetTests
{
    [Theory]
    [InlineData("alloc.box", AllocationKind.Box)]
    [InlineData("alloc.closure", AllocationKind.Closure)]
    [InlineData("alloc.statemachine", AllocationKind.StateMachine)]
    [InlineData("alloc.delegate", AllocationKind.Delegate)]
    [InlineData("alloc.enumerator", AllocationKind.Enumerator)]
    public void HiddenAllocation_OnStraightLinePath_IsHidden(string id, AllocationKind kind)
    {
        var classification = NotableFindingPreset.Classify(Allocation(id, kind));

        Assert.Equal(NotableClassification.Admit(NotableCriterion.Hidden), classification);
        Assert.True(classification.IsNotable);
    }

    [Theory]
    [InlineData("alloc.new", AllocationKind.Object)]
    [InlineData("alloc.array", AllocationKind.Array)]
    public void VisibleAllocation_OnStraightLinePath_IsDemoted(string id, AllocationKind kind)
    {
        var classification = NotableFindingPreset.Classify(Allocation(id, kind));

        Assert.Equal(NotableClassification.Demote(NotableDemotion.VisibleStraightLine), classification);
        Assert.False(classification.IsNotable);
    }

    [Fact]
    public void VisibleAllocation_InLoopBody_IsAmplified()
    {
        var inLoop = Allocation(
            "alloc.new",
            AllocationKind.Object,
            pathContext: AllocationPathContext.LoopBody);

        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Amplified),
            NotableFindingPreset.Classify(inLoop));
    }

    [Fact]
    public void VisibleAllocation_WithLoopMultiplicity_IsAmplified()
    {
        var multiplied = Allocation(
            "alloc.array",
            AllocationKind.Array,
            multiplicity: AllocationMultiplicity.Loop);

        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Amplified),
            NotableFindingPreset.Classify(multiplied));
    }

    [Fact]
    public void VisibleAllocation_PerIterationWithoutPayload_IsAmplified()
    {
        var perIteration = new Annotation(
            new AnnotationDescriptor("alloc.new", AnnotationCategory.Allocation, "allocates an object"),
            SourceOffset: 4,
            Conditionality: AnnotationConditionality.PerIteration);

        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Amplified),
            NotableFindingPreset.Classify(perIteration));
    }

    [Fact]
    public void AllocationInLoopInsideCatch_IsErrorPath_NotAmplified()
    {
        var caught = Allocation(
            "alloc.new",
            AllocationKind.Object,
            pathContext: AllocationPathContext.ErrorPath,
            multiplicity: AllocationMultiplicity.Loop);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.ErrorPath),
            NotableFindingPreset.Classify(caught));
    }

    [Fact]
    public void AllocationFeedingThrow_InLoopBody_IsErrorPath()
    {
        var thrown = Allocation(
            "alloc.array",
            AllocationKind.Array,
            pathContext: AllocationPathContext.LoopBody,
            escape: AllocationEscape.ThrowPath);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.ErrorPath),
            NotableFindingPreset.Classify(thrown));
    }

    [Fact]
    public void HiddenAllocation_OnErrorPath_IsDemoted()
    {
        var box = Allocation(
            "alloc.box",
            AllocationKind.Box,
            pathContext: AllocationPathContext.ErrorPath);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.ErrorPath),
            NotableFindingPreset.Classify(box));
    }

    [Fact]
    public void CachedOnceDelegate_IsDemoted_ByFrequencyAndByConditionality()
    {
        var cachedPayload = Allocation(
            "alloc.delegate",
            AllocationKind.Delegate,
            frequency: AllocationFrequency.CachedOnce,
            conditionality: AnnotationConditionality.CachedOnce);
        var cachedConditionality = new Annotation(
            new AnnotationDescriptor("alloc.delegate", AnnotationCategory.Allocation, "allocates a delegate"),
            SourceOffset: 8,
            Conditionality: AnnotationConditionality.CachedOnce);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.CachedOnce),
            NotableFindingPreset.Classify(cachedPayload));
        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.CachedOnce),
            NotableFindingPreset.Classify(cachedConditionality));
    }

    [Fact]
    public void CachedOnce_InLoop_StaysDemoted()
    {
        var cachedInLoop = Allocation(
            "alloc.new",
            AllocationKind.Object,
            pathContext: AllocationPathContext.LoopBody,
            frequency: AllocationFrequency.CachedOnce);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.CachedOnce),
            NotableFindingPreset.Classify(cachedInLoop));
    }

    [Theory]
    [InlineData("unsafe.deref", AnnotationCategory.Unsafety)]
    [InlineData("unsafe.stackalloc", AnnotationCategory.Unsafety)]
    [InlineData("unsafe.calli", AnnotationCategory.Unsafety)]
    [InlineData("lifetime.ref-return", AnnotationCategory.Lifetime)]
    [InlineData("lifetime.stack-bound", AnnotationCategory.Lifetime)]
    [InlineData("lifetime.ref-struct-return", AnnotationCategory.Lifetime)]
    [InlineData("lifetime.pointer-return", AnnotationCategory.Lifetime)]
    [InlineData("lifetime.stack-escape", AnnotationCategory.Lifetime)]
    public void ContractDescriptor_IsContract(string id, AnnotationCategory category)
    {
        var contract = new Annotation(
            new AnnotationDescriptor(id, category, id),
            SourceOffset: 0);

        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Contract),
            NotableFindingPreset.Classify(contract));
    }

    [Theory]
    [InlineData("cost.callee", AnnotationCategory.Cost)]
    [InlineData("cost.method", AnnotationCategory.Cost)]
    [InlineData("semantics.callee", AnnotationCategory.Semantics)]
    [InlineData("safety.callee", AnnotationCategory.Semantics)]
    [InlineData(ResearchFactRegistry.CallRelationshipDescriptorId, AnnotationCategory.Relationship)]
    public void DescriptorOutsidePreset_IsNeverNotable(string id, AnnotationCategory category)
    {
        var outside = new Annotation(
            new AnnotationDescriptor(id, category, id),
            SourceOffset: 0,
            Conditionality: AnnotationConditionality.PerIteration);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.OutsidePreset),
            NotableFindingPreset.Classify(outside));
    }

    [Fact]
    public void StateMachine_ExplainedByProfile_MovesToLibraryMark()
    {
        var stateMachine = Allocation("alloc.statemachine", AllocationKind.StateMachine);
        var explained = new NotableFindingContext(ProfileExplainsStateMachine: true);

        Assert.Equal(
            NotableClassification.Demote(NotableDemotion.LibraryExplained),
            NotableFindingPreset.Classify(stateMachine, explained));
        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Hidden),
            NotableFindingPreset.Classify(stateMachine, NotableFindingContext.None));
    }

    [Fact]
    public void ProfileExplanation_ReadsOnlyTheStateMachine()
    {
        var explained = new NotableFindingContext(ProfileExplainsStateMachine: true);

        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Hidden),
            NotableFindingPreset.Classify(Allocation("alloc.box", AllocationKind.Box), explained));
        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Hidden),
            NotableFindingPreset.Classify(Allocation("alloc.closure", AllocationKind.Closure), explained));
    }

    [Theory]
    [InlineData(true, StateMachineClaimKind.ClassicAsync, true)]
    [InlineData(true, StateMachineClaimKind.AsyncIterator, false)]
    [InlineData(true, StateMachineClaimKind.Iterator, false)]
    [InlineData(true, null, false)]
    [InlineData(false, StateMachineClaimKind.ClassicAsync, false)]
    public void Context_ReadsTheProfileRule_ForTheMembersClaimedStateMachine(
        bool isDebug,
        StateMachineClaimKind? claim,
        bool explained)
    {
        var profile = new LibraryCompilationProfile(isDebug, MethodsWithBody: 4, 0, 3, 1, 1);
        var stateMachine = Allocation("alloc.statemachine", AllocationKind.StateMachine);

        var context = NotableFindingContext.For(profile, claim);

        Assert.Equal(explained, context.ProfileExplainsStateMachine);
        Assert.Equal(claim, context.StateMachine);
        Assert.Equal(
            explained
                ? NotableClassification.Demote(NotableDemotion.LibraryExplained)
                : NotableClassification.Admit(NotableCriterion.Hidden),
            NotableFindingPreset.Classify(stateMachine, context));
    }

    [Fact]
    public void Expectation_AdmitsNoDescriptorBeforeTheMemberHeaderAdoption()
    {
        Assert.Empty(NotableFindingPreset.ExpectationDescriptorIds);

        foreach (string id in ResearchFactRegistry.MemberCensusWithCallRelationships.DescriptorIds)
        {
            var annotation = new Annotation(
                new AnnotationDescriptor(id, AnnotationCategory.Allocation, id),
                SourceOffset: 0);
            Assert.NotEqual(
                NotableCriterion.Expectation,
                NotableFindingPreset.Classify(annotation).Criterion);
        }
    }

    [Fact]
    public void AdmittedDescriptors_AreDeclaredByTheRegistry()
    {
        var declared = ResearchFactRegistry.MemberCensusWithCallRelationships.DescriptorIds
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(
            NotableFindingPreset.AdmittedDescriptorIds,
            id => Assert.Contains(id, declared));
        Assert.Equal(
            NotableFindingPreset.AdmittedDescriptorIds.Order(StringComparer.Ordinal),
            NotableFindingPreset.AdmittedDescriptorIds);
        Assert.Equal(
            NotableFindingPreset.AdmittedDescriptorIds.Length,
            NotableFindingPreset.HiddenDescriptorIds.Length
            + NotableFindingPreset.AmplifiedDescriptorIds.Length
            + NotableFindingPreset.ContractDescriptorIds.Length
            + NotableFindingPreset.ExpectationDescriptorIds.Length);
    }

    internal static Annotation<AllocationOccurrence> Allocation(
        string id,
        AllocationKind kind,
        AllocationPathContext pathContext = AllocationPathContext.StraightLine,
        AllocationMultiplicity multiplicity = AllocationMultiplicity.Unknown,
        AllocationEscape escape = AllocationEscape.Unknown,
        AllocationFrequency frequency = AllocationFrequency.Always,
        AnnotationConditionality conditionality = AnnotationConditionality.Always)
    {
        var occurrence = new AllocationOccurrence(
            Method(),
            ILOffset: 2,
            OperandToken: null,
            kind,
            TypeRef.CoreLib("System", "Object"),
            Detail: null,
            CountsAsHeapAllocation: true,
            frequency,
            InLoop: pathContext == AllocationPathContext.LoopBody,
            escape,
            AllocationFactSource.Newobj,
            PathContext: pathContext)
        {
            Multiplicity = multiplicity,
        };
        return new Annotation<AllocationOccurrence>(
            new AnnotationDescriptor(id, AnnotationCategory.Allocation, id),
            occurrence.ILOffset,
            occurrence,
            conditionality);
    }

    static MethodIdentity Method()
        => new(
            "Sample",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TypeRef.Definition("Sample", "Sample", "Widget"),
            "Run",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
}
