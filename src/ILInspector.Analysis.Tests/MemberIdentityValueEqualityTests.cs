using System.Collections.Immutable;

namespace ILInspector.Analysis.Tests;

public class MemberIdentityValueEqualityTests
{
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
