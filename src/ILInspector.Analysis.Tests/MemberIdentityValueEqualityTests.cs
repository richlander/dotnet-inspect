using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class MemberIdentityValueEqualityTests
{
    [Fact]
    public void MemberRefEquality_IgnoresInternalParameterDirectionEvidence()
    {
        var baseline = new MemberRef(
            TypeRef.Definition("Sample", "Sample", "Api"),
            "Read",
            [TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            ParameterDirections = [ParameterDirection.Ref],
        };
        var memberReference = baseline with
        {
            ParameterDirections = [],
        };

        Assert.Equal(baseline, memberReference);
        Assert.Equal(
            baseline.GetHashCode(),
            memberReference.GetHashCode());
    }

    [Fact]
    public void TypeRefSharedDag_EqualityHashAndAsyncIdentityAreLinear()
    {
        TypeRef left = TypeRef.CoreLib("System", "Int32");
        TypeRef right = TypeRef.CoreLib("System", "Int32");
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        for (int depth = 0; depth < 30; depth++)
        {
            left = TypeRef.GenericInstance(pair, [left, left]);
            right = TypeRef.GenericInstance(pair, [right, right]);
        }

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.True(
            LibraryBodyAnalysisBuilder
                .AsyncSiblingTypesMatch(left, right));
        Assert.True(
            LibraryBodyAnalysisBuilder
                .AsyncSiblingTypeIdentity(left)
                .Length < 10_000);
    }

    [Fact]
    public void TypeRefShallowEqualityAndHashing_DoNotAllocate()
    {
        TypeRef leftLeaf = TypeRef.CoreLib("System", "Int32");
        TypeRef rightLeaf = TypeRef.CoreLib("System", "Int32");
        TypeRef definition =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        TypeRef leftGeneric =
            TypeRef.GenericInstance(
                definition,
                [leftLeaf, TypeRef.CoreLib("System", "String")]);
        TypeRef rightGeneric =
            TypeRef.GenericInstance(
                TypeRef.Definition("Sample", "Sample", "Pair`2"),
                [rightLeaf, TypeRef.CoreLib("System", "String")]);

        Assert.Equal(
            0,
            MeasureEqualityAllocations(leftLeaf, leftLeaf));
        Assert.Equal(
            0,
            MeasureEqualityAllocations(leftLeaf, rightLeaf));
        Assert.Equal(
            0,
            MeasureHashAllocations(leftLeaf));
        Assert.Equal(
            0,
            MeasureEqualityAllocations(leftGeneric, rightGeneric));
        Assert.Equal(
            0,
            MeasureHashAllocations(leftGeneric));
    }

    [Fact]
    public void AsyncSiblingExactIdentity_DistinguishesOriginsWithinSharedDag()
    {
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        static TypeRef ReferencedType(
            Version version,
            MetadataTypeDefinitionName name)
        {
            var assembly = new AssemblyReferenceIdentity(
                "Dependency",
                version,
                null,
                null);
            return TypeRef.Definition(
                assembly.Name,
                name.Namespace,
                name.Segments[0],
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(assembly),
                    name));
        }

        TypeRef versionOne =
            ReferencedType(new Version(1, 0), name);
        TypeRef versionTwo =
            ReferencedType(new Version(2, 0), name);
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        TypeRef shared = TypeRef.GenericInstance(
            pair,
            [versionOne, versionOne]);
        TypeRef mixed = TypeRef.GenericInstance(
            pair,
            [versionOne, versionTwo]);

        Assert.Equal(versionOne, versionTwo);
        Assert.NotEqual(
            LibraryBodyAnalysisBuilder
                .AsyncSiblingTypeIdentity(shared),
            LibraryBodyAnalysisBuilder
                .AsyncSiblingTypeIdentity(mixed));
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_RejectsExponentialDagExpansion()
    {
        TypeRef value = TypeRef.CoreLib("System", "Int32");
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        for (int depth = 0; depth < 30; depth++)
            value = TypeRef.GenericInstance(pair, [value, value]);

        var exception = Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAnalysisBuilder
                .EnsureAsyncSiblingDisplayIsBounded(value));
        Assert.Contains(
            "output limit",
            exception.Message);
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_AcceptsWideFlatSignature()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var member = new MemberRef(
            TypeRef.Definition("Sample", "Sample", "Api"),
            "Read",
            [.. Enumerable.Repeat(int32, 256)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        LibraryBodyAnalysisBuilder
            .EnsureAsyncSiblingDisplayIsBounded(member);
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_BoundsAggregateMemberText()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        var member = new MemberRef(
            TypeRef.Definition("Sample", "Sample", "Api"),
            "Read",
            [.. Enumerable.Repeat(int32, 5_000)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAnalysisBuilder
                .EnsureAsyncSiblingDisplayIsBounded(member));
    }

    [Fact]
    public void AsyncSiblingIdentityAndMatching_DistinguishArrayShape()
    {
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        TypeRef baseline = TypeRef.MdArray(
            int32,
            new ArrayShape(
                1,
                [6],
                [0]));
        TypeRef differentSize = TypeRef.MdArray(
            int32,
            new ArrayShape(
                1,
                [7],
                [0]));
        TypeRef differentLowerBound = TypeRef.MdArray(
            int32,
            new ArrayShape(
                1,
                [6],
                [1]));

        Assert.False(
            LibraryBodyAnalysisBuilder.AsyncSiblingTypesMatch(
                baseline,
                differentSize));
        Assert.False(
            LibraryBodyAnalysisBuilder.AsyncSiblingTypesMatch(
                baseline,
                differentLowerBound));
        Assert.NotEqual(
            LibraryBodyAnalysisBuilder.AsyncSiblingTypeIdentity(
                baseline),
            LibraryBodyAnalysisBuilder.AsyncSiblingTypeIdentity(
                differentSize));
        Assert.NotEqual(
            LibraryBodyAnalysisBuilder.AsyncSiblingTypeIdentity(
                baseline),
            LibraryBodyAnalysisBuilder.AsyncSiblingTypeIdentity(
                differentLowerBound));
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_RejectsExcessiveArrayRank()
    {
        TypeRef array = TypeRef.MdArray(
            TypeRef.CoreLib("System", "Int32"),
            rank: 1_000_000);

        Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAnalysisBuilder
                .EnsureAsyncSiblingDisplayIsBounded(array));
    }

    [Fact]
    public void AsyncSiblingFindingDisplay_AccumulatesNestedArrayRanks()
    {
        TypeRef array = TypeRef.CoreLib("System", "Int32");
        for (int depth = 0; depth < 200; depth++)
            array = TypeRef.MdArray(array, rank: 60_000);

        Assert.Throws<BadImageFormatException>(
            () => LibraryBodyAnalysisBuilder
                .EnsureAsyncSiblingDisplayIsBounded(array));
    }

    [Fact]
    public void AsyncSiblingTypeSupport_IsLinearForSharedDag()
    {
        TypeRef value = TypeRef.CoreLib("System", "Int32");
        TypeRef pair =
            TypeRef.Definition("Sample", "Sample", "Pair`2");
        for (int depth = 0; depth < 30; depth++)
            value = TypeRef.GenericInstance(pair, [value, value]);

        Assert.True(
            LibraryBodyAnalysisBuilder
                .IsSupportedAsyncSiblingType(value));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_DistinguishesStructuredNames()
    {
        var assembly = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(1, 0),
            null,
            null);
        MetadataTypeDefinitionName literal =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Probe",
                    ["A+B"]))
            .Name;
        MetadataTypeDefinitionName nested =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Probe",
                    ["A", "B"]))
            .Name;
        static TypeRef Create(
            AssemblyReferenceIdentity assembly,
            MetadataTypeDefinitionName name) =>
            TypeRef.Definition(
                assembly.Name,
                name.Namespace,
                name.ToNestedMetadataName(),
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(assembly),
                    name));

        Assert.False(
            LibraryBodyAnalysisBuilder.AsyncSiblingTypesMatch(
                Create(assembly, literal),
                Create(assembly, nested)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long MeasureEqualityAllocations(
        TypeRef left,
        TypeRef right)
    {
        bool result = false;
        for (int i = 0; i < 1_000; i++)
            result ^= left.Equals(right);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
            result ^= left.Equals(right);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        Consume(result);
        return allocated;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long MeasureHashAllocations(TypeRef type)
    {
        int result = 0;
        for (int i = 0; i < 1_000; i++)
            result ^= type.GetHashCode();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
            result ^= type.GetHashCode();
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        Consume(result);
        return allocated;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Consume<T>(T value) { }

    static readonly TypeRef DeclaringType = TypeRef.Definition(
        "Example",
        "Example",
        "Container");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void MethodIdentity_UsesOrderedSequenceEquality()
    {
        var first = Method(
            ImmutableArray.Create(Int32, Int32, String),
            ImmutableArray.Create("T", "U"));
        var equivalent = Method(
            ImmutableArray.Create(Int32, Int32, String),
            ImmutableArray.Create("T", "U"));
        var reordered = Method(
            ImmutableArray.Create(String, Int32, Int32),
            ImmutableArray.Create("T", "U"));
        var differentDuplicates = Method(
            ImmutableArray.Create(Int32, String, String),
            ImmutableArray.Create("T", "U"));
        MethodIdentity differentHeader = first with
        {
            SignatureHeader = 0x05,
        };
        MethodIdentity capturedNonVarargCount = first with
        {
            SignatureHeader = 0x10,
            RequiredParameterCount = first.ParameterTypes.Length,
        };
        MethodIdentity vararg = first with
        {
            SignatureHeader = 0x05,
            RequiredParameterCount = 1,
        };
        MethodIdentity differentRequiredCount = vararg with
        {
            RequiredParameterCount = 2,
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first, capturedNonVarargCount);
        Assert.Equal(
            first.GetHashCode(),
            capturedNonVarargCount.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentDuplicates);
        Assert.NotEqual(first, differentHeader);
        Assert.NotEqual(first, differentRequiredCount);
    }

    [Fact]
    public void MethodIdentity_NormalizesOmittedGenericNamesAndRejectsInvalidParameters()
    {
        var omitted = Method([]);
        var explicitEmpty = Method([], []);

        Assert.False(omitted.GenericParameterNames.IsDefault);
        Assert.Equal(omitted, explicitEmpty);
        Assert.Throws<ArgumentException>(() => Method(default));
        Assert.Throws<ArgumentException>(() => Method([null!]));
        Assert.Throws<ArgumentException>(
            () => omitted with { GenericParameterNames = [null!] });
    }

    [Fact]
    public void MemberRef_ComposesAllOrderedCollectionProperties()
    {
        var first = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(String),
            ImmutableArray.Create(Int32));
        var equivalent = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(String),
            ImmutableArray.Create(Int32));
        var reordered = Member(
            ImmutableArray.Create(String, Int32),
            ImmutableArray.Create(String),
            ImmutableArray.Create(Int32));
        var differentTypeArguments = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(Int32),
            ImmutableArray.Create(Int32));
        var differentOpenParameters = Member(
            ImmutableArray.Create(Int32, String),
            ImmutableArray.Create(String),
            ImmutableArray.Create(String));
        MemberRef capturedNonVarargCount = first with
        {
            RequiredParameterCount = first.ParameterTypes.Length,
        };
        MemberRef vararg = first with
        {
            SignatureHeader = 0x05,
            RequiredParameterCount = 1,
        };
        MemberRef differentRequiredCount = vararg with
        {
            RequiredParameterCount = 2,
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first, capturedNonVarargCount);
        Assert.Equal(
            first.GetHashCode(),
            capturedNonVarargCount.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentTypeArguments);
        Assert.NotEqual(first, differentOpenParameters);
        Assert.NotEqual(first, differentRequiredCount);
    }

    [Fact]
    public void MemberRef_RejectsDefaultCollectionsIncludingWithExpressions()
    {
        Assert.Throws<ArgumentException>(() => Member(default, [], []));

        var member = Member([], [], []);
        Assert.Throws<ArgumentException>(
            () => member with { ParameterTypes = default });
        Assert.Throws<ArgumentException>(
            () => member with { TypeArguments = default });
        Assert.Throws<ArgumentException>(
            () => member with { OpenParameterTypes = default });
    }

    [Fact]
    public void DirectCall_ComposesMemberIdentityValueEquality()
    {
        var first = new DirectCall(
            Method(ImmutableArray.Create(Int32, String)),
            Member(
                ImmutableArray.Create(Int32),
                ImmutableArray.Create(String),
                ImmutableArray.Create(Int32)),
            ILOffset: 3,
            OperandToken: 0x0a000001,
            CalleeDefinitionToken: 0x06000002,
            CallKind.Call)
        {
            Opcode = "call",
        };
        var equivalent = new DirectCall(
            Method(ImmutableArray.Create(Int32, String)),
            Member(
                ImmutableArray.Create(Int32),
                ImmutableArray.Create(String),
                ImmutableArray.Create(Int32)),
            ILOffset: 3,
            OperandToken: 0x0a000001,
            CalleeDefinitionToken: 0x06000002,
            CallKind.Call)
        {
            Opcode = "call",
        };

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
    }

    /// <summary>
    /// The operator fact is part of a <em>definition's</em> identity: a
    /// <see cref="MethodIdentity"/> names a MethodDef whose own metadata always
    /// supplies the answer, so a hash that ignored it would let a real operator
    /// and an ordinary <c>op_</c>-named method collide in an identity-keyed map.
    /// A <see cref="MemberRef"/> is a reference, not a definition, and the same
    /// member reached through a MemberRef token knows less than the same member
    /// reached through a MethodDef token — so there the fact must stay out of
    /// equality or one member stops being equal to itself.
    /// </summary>
    [Fact]
    public void OperatorFact_IsIdentityForDefinitionsAndKnowledgeForReferences()
    {
        var unknown = Method(ImmutableArray.Create(Int32));
        var isOperator = unknown with { IsOperator = MetadataOperatorFact.Yes };
        var isNotOperator = unknown with { IsOperator = MetadataOperatorFact.No };

        Assert.NotEqual(unknown, isOperator);
        Assert.NotEqual(isOperator, isNotOperator);
        Assert.NotEqual(isOperator.GetHashCode(), isNotOperator.GetHashCode());
        Assert.Equal(isOperator, unknown with { IsOperator = MetadataOperatorFact.Yes });
        Assert.Equal(
            isOperator.GetHashCode(),
            (unknown with { IsOperator = MetadataOperatorFact.Yes }).GetHashCode());

        var memberUnknown = Member([Int32], [], []);
        var memberOperator = memberUnknown with { IsOperator = MetadataOperatorFact.Yes };
        var memberOrdinary = memberUnknown with { IsOperator = MetadataOperatorFact.No };

        Assert.Equal(memberUnknown, memberOperator);
        Assert.Equal(memberOperator, memberOrdinary);
        Assert.Equal(memberOperator.GetHashCode(), memberOrdinary.GetHashCode());
    }

    static MethodIdentity Method(
        ImmutableArray<TypeRef> parameterTypes,
        ImmutableArray<string> genericParameterNames = default)
        => new(
            "Example",
            Guid.Parse("252451b8-cd83-4e7b-b7e6-8f05b6e4900c"),
            DeclaringType,
            "M",
            parameterTypes,
            Void,
            MetadataToken: 0x06000001,
            IsStatic: true,
            GenericArity: genericParameterNames.IsDefault ? 0 : genericParameterNames.Length,
            GenericParameterNames: genericParameterNames);

    static MemberRef Member(
        ImmutableArray<TypeRef> parameterTypes,
        ImmutableArray<TypeRef> typeArguments,
        ImmutableArray<TypeRef> openParameterTypes)
        => new(DeclaringType, "M", parameterTypes, Void, MemberKind.Method)
        {
            TypeArguments = typeArguments,
            HasThis = true,
            SignatureHeader = 0x20,
            GenericArity = typeArguments.Length,
            OpenParameterTypes = openParameterTypes,
            OpenReturnType = String,
        };
}
