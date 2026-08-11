using System.Collections.Immutable;
using DotnetInspector.Output;
using ILInspector.Analysis;
using ILInspector.Findings;

namespace DotnetInspector.Tests;

public sealed class FindingTargetFormatterTests
{
    const string ExplicitSubject = "Retained.Sample.Widget.Run";

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
            "Retained.Sample.Widget.Run :: Newobj/Object object",
            FindingTargetFormatter.Format(ExplicitSubject, allocation));
        Assert.Equal(
            "Sample.Widget.Run :: System.Math.Abs(int)",
            FindingTargetFormatter.Format(callSite));
        Assert.Equal(
            "Retained.Sample.Widget.Run :: System.Math.Abs(int)",
            FindingTargetFormatter.Format(ExplicitSubject, callSite));
        Assert.Equal(
            "Sample.Widget.Run :: StackAlloc byte*",
            FindingTargetFormatter.Format(unsafety));
        Assert.Equal(
            "Retained.Sample.Widget.Run :: StackAlloc byte*",
            FindingTargetFormatter.Format(ExplicitSubject, unsafety));
    }

    [Fact]
    public void AllocationTarget_PreservesFallbackOrder()
    {
        Assert.Equal(
            "Sample.Widget.Run :: Newobj/Object Runtime.Type",
            FindingTargetFormatter.Format(Allocation(
                allocatedType: null,
                detail: "detail",
                runtimeAllocationType: "Runtime.Type")));
        Assert.Equal(
            "Sample.Widget.Run :: Newobj/Object detail",
            FindingTargetFormatter.Format(Allocation(
                allocatedType: null,
                detail: "detail")));
        Assert.Equal(
            "Sample.Widget.Run :: Newobj/Object ?",
            FindingTargetFormatter.Format(Allocation(
                allocatedType: null,
                detail: null)));
    }

    [Fact]
    public void DirectCallTarget_PreservesSpecialCaseSpelling()
    {
        Assert.Equal(
            "Sample.Widget.Run :: Math",
            FindingTargetFormatter.Format(Call(new MemberRef(
                TypeRef.CoreLib("System", "Math"),
                "?",
                [],
                TypeRef.Unsupported("unknown return"),
                MemberKind.Unsupported))));
        Assert.Equal(
            "Sample.Widget.Run :: System.Collections.Generic.List<string>(int)",
            FindingTargetFormatter.Format(Call(new MemberRef(
                TypeRef.CoreLib("System.Collections.Generic", "List"),
                ".ctor",
                [TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Constructor)
            {
                TypeArguments = [TypeRef.CoreLib("System", "String")],
            })));
        Assert.Equal(
            "Sample.Widget.Run :: System.Linq.Enumerable.Select<string>(int)",
            FindingTargetFormatter.Format(Call(new MemberRef(
                TypeRef.CoreLib("System.Linq", "Enumerable"),
                "Select",
                [TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "String"),
                MemberKind.Method)
            {
                TypeArguments = [TypeRef.CoreLib("System", "String")],
            })));
    }

    [Fact]
    public void UnsafetyTarget_OmitsBlankDetail()
    {
        Assert.Equal(
            "Sample.Widget.Run :: Deref",
            FindingTargetFormatter.Format(Finding(new UnsafetyOccurrence(
                Method(),
                ILOffset: 0,
                UnsafetyKind.Deref,
                "  "))));
    }

    static Finding<T> Finding<T>(T payload)
        where T : notnull
        => new(Subject, Descriptor, new FindingKey(typeof(T).Name), payload);

    static Finding<AllocationOccurrence> Allocation(
        TypeRef? allocatedType,
        string? detail,
        string? runtimeAllocationType = null)
        => Finding(new AllocationOccurrence(
            Method(),
            ILOffset: 0,
            OperandToken: null,
            AllocationKind.Object,
            allocatedType,
            detail,
            CountsAsHeapAllocation: true,
            AllocationFrequency.Always,
            InLoop: false,
            AllocationEscape.Unknown,
            AllocationFactSource.Newobj,
            RuntimeAllocationType: runtimeAllocationType));

    static Finding<DirectCall> Call(MemberRef callee)
        => Finding(new DirectCall(
            Method(),
            callee,
            ILOffset: 0,
            OperandToken: 0x0A000001,
            CalleeDefinitionToken: 0x0A000001,
            CallKind.Call));

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
