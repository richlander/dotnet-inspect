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

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentDuplicates);
        Assert.NotEqual(first, differentHeader);
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

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentTypeArguments);
        Assert.NotEqual(first, differentOpenParameters);
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
