using System.Collections.Immutable;
using DotnetInspector.Output;
using ILInspector.Analysis;
using ILInspector.Findings;

namespace DotnetInspector.Tests;

public sealed class FindingTargetFormatterTests
{
    static readonly FindingSubject Subject = new(
        "analysis.member:Sample.Widget:Run",
        "Sample.Widget.Run");
    static readonly FindingDescriptor Descriptor = new("test.target", "Target");

    [Fact]
    public void FindingAndExplicitSubjectPaths_RenderIdenticalTargets()
    {
        var allocation = Finding(new AllocationOccurrence(
            Method(),
            ILOffset: 0,
            OperandToken: null,
            AllocationKind.Object,
            TypeRef.CoreLib("System", "Object"),
            Detail: null,
            CountsAsHeapAllocation: true,
            AllocationFrequency.Always,
            InLoop: false,
            AllocationEscape.Unknown,
            AllocationFactSource.Newobj));
        var callSite = Finding(new DirectCall(
            Method(),
            new MemberRef(
                TypeRef.CoreLib("System", "Math"),
                "Abs",
                [TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Int32"),
                MemberKind.Method),
            ILOffset: 0,
            OperandToken: 0x0A000001,
            CalleeDefinitionToken: 0x0A000001,
            CallKind.Call));
        var unsafety = Finding(new UnsafetyOccurrence(
            Method(),
            ILOffset: 0,
            UnsafetyKind.StackAlloc,
            "byte*"));

        Assert.Equal(
            "Sample.Widget.Run :: Newobj/Object object",
            FindingTargetFormatter.Format(allocation));
        Assert.Equal(
            FindingTargetFormatter.Format(allocation),
            FindingTargetFormatter.Format(Subject.Display, allocation));
        Assert.Equal(
            "Sample.Widget.Run :: System.Math.Abs(int)",
            FindingTargetFormatter.Format(callSite));
        Assert.Equal(
            FindingTargetFormatter.Format(callSite),
            FindingTargetFormatter.Format(Subject.Display, callSite));
        Assert.Equal(
            "Sample.Widget.Run :: StackAlloc byte*",
            FindingTargetFormatter.Format(unsafety));
        Assert.Equal(
            FindingTargetFormatter.Format(unsafety),
            FindingTargetFormatter.Format(Subject.Display, unsafety));
    }

    static Finding<T> Finding<T>(T payload)
        where T : notnull
        => new(Subject, Descriptor, new FindingKey(typeof(T).Name), payload);

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
