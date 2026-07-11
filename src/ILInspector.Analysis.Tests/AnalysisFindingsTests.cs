using System.Collections.Immutable;

using DotnetInspector.Fixtures;
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

    [Fact]
    public void InspectCallSites_ProducesCompleteIlOrderedCensus()
    {
        var later = Call(12, "Later");
        var earlier = Call(4, "Earlier");

        var findings = AnalysisFindings.InspectCallSites([later, earlier], Subject);

        Assert.Collection(
            findings,
            finding =>
            {
                Assert.Same(earlier, finding.Payload);
                Assert.Equal(0, finding.Ordinal);
                Assert.Equal(AnalysisFindings.CallSiteDescriptor, finding.Descriptor);
            },
            finding =>
            {
                Assert.Same(later, finding.Payload);
                Assert.Equal(1, finding.Ordinal);
            });
    }

    [Fact]
    public void CompareCallSites_IgnoresVersionLocalProvenance()
    {
        var oldCall = Call(4, "Target") with
        {
            Opcode = "call",
            ReturnAddress = 9,
        };
        var newCall = oldCall with
        {
            Caller = oldCall.Caller with
            {
                ModuleVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                MetadataToken = 0x06000002,
            },
            ILOffset = 20,
            OperandToken = 0x0A000002,
            CalleeDefinitionToken = 0x06000003,
            ReturnAddress = 25,
        };

        var complete = CompleteComparison(
            AnalysisFindings.CompareCallSites([oldCall], [newCall], Subject));
        var present = Assert.Single(complete.Pairs) switch
        {
            PairFinding<DirectCall>.Present value => value,
            _ => throw new InvalidOperationException("Expected a present call site."),
        };

        Assert.Equal(FindingDifferenceKind.None, present.Difference);
    }

    [Fact]
    public void CompareCallSites_ClassifiesDispatchFacetChange()
    {
        var oldCall = Call(4, "Target") with { Opcode = "call" };
        var newCall = oldCall with
        {
            ILOffset = 8,
            Kind = CallKind.CallVirtual,
            InLoop = true,
            Opcode = "callvirt",
        };

        var complete = CompleteComparison(
            AnalysisFindings.CompareCallSites([oldCall], [newCall], Subject));
        var changed = Assert.Single(complete.Pairs) switch
        {
            PairFinding<DirectCall>.Changed value => value,
            _ => throw new InvalidOperationException("Expected a changed call site."),
        };

        Assert.Contains("kind: Call -> CallVirtual", changed.Detail);
        Assert.Contains("in loop: False -> True", changed.Detail);
        Assert.Contains("opcode: call -> callvirt", changed.Detail);
    }

    [Fact]
    public void CompareCallSites_ChangedGenericInstantiation_IsRemovedAndAdded()
    {
        var genericType = TypeRef.Definition("GenericAssembly", "Test", "Box`1");
        var oldType = TypeRef.GenericInstance(genericType, [TypeRef.CoreLib("System", "Int32")]);
        var newType = TypeRef.GenericInstance(genericType, [TypeRef.CoreLib("System", "String")]);
        var oldCall = Call(4, "Store", oldType, [TypeRef.CoreLib("System", "Int32")]);
        var newCall = Call(4, "Store", newType, [TypeRef.CoreLib("System", "String")]);

        var complete = CompleteComparison(
            AnalysisFindings.CompareCallSites([oldCall], [newCall], Subject));

        Assert.Contains(complete.Pairs, pair => pair is PairFinding<DirectCall>.Removed);
        Assert.Contains(complete.Pairs, pair => pair is PairFinding<DirectCall>.Added);
        Assert.DoesNotContain(complete.Pairs, pair => pair is PairFinding<DirectCall>.Present);
    }

    [Fact]
    public void CompareCallSites_SeparatelyDecodedSelfComparison_IsExact()
    {
        var oldIndex = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.NewAssemblyPath());
        var newIndex = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldCallsByCaller = oldIndex.GetDirectCallsByCaller();
        var newCallsByCaller = newIndex.GetDirectCallsByCaller();
        int methodToken = oldCallsByCaller.First(pair => pair.Value.Length > 0).Key;

        var complete = CompleteComparison(AnalysisFindings.CompareCallSites(
            oldCallsByCaller[methodToken],
            newCallsByCaller[methodToken],
            Subject));

        Assert.True(complete.IsExact);
    }

    [Fact]
    public void CompareCallSites_CallIndirectFallbackIgnoresSignatureToken()
    {
        var oldCall = new DirectCall(
            Method(),
            MemberRef.Unsupported("calli signature token 0x11000001"),
            4,
            0x11000001,
            0x11000001,
            CallKind.CallIndirect);
        var newCall = oldCall with
        {
            Callee = MemberRef.Unsupported("calli signature token 0x11000007"),
            ILOffset = 12,
            OperandToken = 0x11000007,
            CalleeDefinitionToken = 0x11000007,
        };

        var complete = CompleteComparison(
            AnalysisFindings.CompareCallSites([oldCall], [newCall], Subject));

        Assert.True(complete.IsExact);
    }

    [Fact]
    public void InspectCallSites_DecodesCallIndirectSignatureStructurally()
    {
        var index = LibraryBodyIndex.Open(typeof(AnalysisFindingsTests).Assembly.Location);
        var method = index.Methods.Single(method =>
            method.DeclaringType.ToQualifiedDisplayString().EndsWith(
                nameof(AnalysisFindingsTests),
                StringComparison.Ordinal)
            && method.Name == nameof(InvokeFunctionPointer));
        var call = Assert.Single(index.GetDirectCallsByCaller()[method.MetadataToken]);

        Assert.Equal(CallKind.CallIndirect, call.Kind);
        Assert.Equal(MemberKind.FunctionPointer, call.Callee.Kind);
        Assert.Equal(TypeRef.CoreLib("System", "Int32"), call.Callee.ReturnType);
        Assert.Equal(
            [TypeRef.CoreLib("System", "Int32")],
            call.Callee.ParameterTypes);
        Assert.DoesNotContain("token", AnalysisFindings.GetCallSiteIdentityKey(call));
    }

    [Fact]
    public void InspectUnsafety_ProducesCompleteIlOrderedCensus()
    {
        var method = Method();
        var later = new UnsafetyOccurrence(method, 12, UnsafetyKind.Deref, "int");
        var earlier = new UnsafetyOccurrence(method, 4, UnsafetyKind.StackAlloc, "byte*");

        var findings = AnalysisFindings.InspectUnsafety([later, earlier], Subject);

        Assert.Collection(
            findings,
            finding =>
            {
                Assert.Same(earlier, finding.Payload);
                Assert.Equal(0, finding.Ordinal);
                Assert.Equal(AnalysisFindings.UnsafetyDescriptor, finding.Descriptor);
            },
            finding =>
            {
                Assert.Same(later, finding.Payload);
                Assert.Equal(1, finding.Ordinal);
            });
    }

    [Fact]
    public void CompareUnsafety_IgnoresVersionLocalCoordinates()
    {
        var oldOccurrence = new UnsafetyOccurrence(Method(), 4, UnsafetyKind.Deref, "int");
        var newOccurrence = oldOccurrence with
        {
            Method = oldOccurrence.Method with
            {
                ModuleVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                MetadataToken = 0x06000002,
            },
            ILOffset = 20,
        };

        var complete = CompleteComparison(
            AnalysisFindings.CompareUnsafety([oldOccurrence], [newOccurrence], Subject));

        _ = Assert.Single(complete.Pairs) switch
        {
            PairFinding<UnsafetyOccurrence>.Present value => value,
            _ => throw new InvalidOperationException("Expected a present unsafe operation."),
        };
    }

    [Fact]
    public void InspectUnsafeEvidence_IsTotalIdentityMultisetWithoutOrdinals()
    {
        var method = Method();
        var declaration = new UnsafeEvidence(
            method, "Unsafe signature", "int*", "signature", null, null);
        var body = new UnsafeEvidence(
            method, "Unsafe signature", "int*", "signature", 4, 0x0A000001);

        var findings = AnalysisFindings.InspectUnsafeEvidence([body, declaration], Subject);

        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding => Assert.Null(finding.Ordinal));
        Assert.NotEqual(findings[0].Key.IdentityKey, findings[1].Key.IdentityKey);
    }

    [Fact]
    public void CompareUnsafeEvidence_PreservesMultiplicityAndIgnoresBodyProvenance()
    {
        var method = Method();
        var oldEvidence = new UnsafeEvidence(
            method, "Unsafe operation", "stackalloc", "opcode", 4, 0x0A000001);
        var newEvidence = oldEvidence with
        {
            Member = method with
            {
                ModuleVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                MetadataToken = 0x06000002,
            },
            ILOffset = 20,
            OperandToken = 0x0A000002,
        };

        var complete = CompleteComparison(
            AnalysisFindings.CompareUnsafeEvidence(
                [oldEvidence],
                [newEvidence, newEvidence with { ILOffset = 24 }],
                Subject));

        Assert.Single(complete.Pairs, pair => pair is PairFinding<UnsafeEvidence>.Present);
        Assert.Single(complete.Pairs, pair => pair is PairFinding<UnsafeEvidence>.Added);
    }

    static FindingComparison<AllocationOccurrence>.Complete CompleteComparison(
        FindingComparison<AllocationOccurrence> comparison)
        => comparison switch
        {
            FindingComparison<AllocationOccurrence>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete allocation comparison."),
        };

    static FindingComparison<DirectCall>.Complete CompleteComparison(
        FindingComparison<DirectCall> comparison)
        => comparison switch
        {
            FindingComparison<DirectCall>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete call-site comparison."),
        };

    static FindingComparison<UnsafetyOccurrence>.Complete CompleteComparison(
        FindingComparison<UnsafetyOccurrence> comparison)
        => comparison switch
        {
            FindingComparison<UnsafetyOccurrence>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete unsafety comparison."),
        };

    static FindingComparison<UnsafeEvidence>.Complete CompleteComparison(
        FindingComparison<UnsafeEvidence> comparison)
        => comparison switch
        {
            FindingComparison<UnsafeEvidence>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete unsafe-evidence comparison."),
        };

    static DirectCall Call(
        int offset,
        string name,
        TypeRef? declaringType = null,
        ImmutableArray<TypeRef> parameterTypes = default)
    {
        declaringType ??= TypeRef.Definition("TargetAssembly", "Test", "Target");
        if (parameterTypes.IsDefault)
            parameterTypes = [];
        var callee = new MemberRef(
            declaringType,
            name,
            parameterTypes,
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);
        return new DirectCall(
            Method(),
            callee,
            offset,
            0x0A000001,
            0x06000002,
            CallKind.Call);
    }

    static AllocationOccurrence Occurrence(int offset, AllocationKind kind, TypeRef type)
        => new(
            Method(),
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

    static MethodIdentity Method()
        => new(
            "TestAssembly",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TypeRef.Definition("TestAssembly", "Test", "Fixture"),
            "Method",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

    static unsafe int InvokeFunctionPointer(delegate*<int, int> function, int value)
        => function(value);
}
