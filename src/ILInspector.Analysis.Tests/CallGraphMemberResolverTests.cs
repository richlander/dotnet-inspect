using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class CallGraphMemberResolverTests
{
    [Fact]
    public void Selector_PreservesGenericArgumentsAndByRefShape()
    {
        var span = TypeRef.Definition("corelib", "System", "ReadOnlySpan`1");
        var objectSpan = TypeRef.GenericInstance(
            span,
            [TypeRef.CoreLib("System", "Object")]);
        var stringSpan = TypeRef.GenericInstance(
            span,
            [TypeRef.CoreLib("System", "String")]);

        var objectSelector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.CoreLib("System", "String"),
            "Concat",
            [objectSpan],
            TypeRef.CoreLib("System", "String"),
            MemberKind.Method));
        var stringSelector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.CoreLib("System", "String"),
            "Concat",
            [stringSpan],
            TypeRef.CoreLib("System", "String"),
            MemberKind.Method));
        var byRefSelector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.CoreLib("System", "Int32"),
            "TryParse",
            [TypeRef.CoreLib("System", "String"), TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method));

        Assert.NotEqual(objectSelector.Key, stringSelector.Key);
        Assert.Equal("System.ReadOnlySpan{System.Object}", objectSelector.ParameterTypes[0]);
        Assert.Equal("System.Int32@", byRefSelector.ParameterTypes[1]);
    }

    [Fact]
    public void Resolve_UsesStructuredIndexerAccessorIdentity()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Indexed",
            Members =
            [
                Indexer("int", 0x06000001),
                Indexer("string", 0x06000002),
            ],
        };
        var selector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Indexed"),
            "get_Item",
            [TypeRef.CoreLib("System", "String")],
            TypeRef.CoreLib("System", "Int32"),
            MemberKind.Method));

        var resolved = CallGraphMemberResolver.Resolve(
            type,
            selector.Name,
            selector.Key);

        Assert.NotNull(resolved);
        Assert.Equal(0x06000002, resolved.BodyToken);
        Assert.Equal("string", resolved.Member.SignatureModel!.Parameters[0].Type);
    }

    [Fact]
    public void Resolve_PrefersExactAccessorToken()
    {
        var first = Indexer("int", 0x06000001);
        var second = Indexer("string", 0x06000002);
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Indexed",
            Members = [first, second],
        };
        var deliberatelyWrongShape = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Indexed"),
            "get_Item",
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Int32"),
            MemberKind.Method));

        var resolved = CallGraphMemberResolver.Resolve(
            type,
            deliberatelyWrongShape.Name,
            deliberatelyWrongShape.Key,
            metadataToken: 0x06000002);

        Assert.Same(second, resolved!.Member);
        Assert.Equal(0x06000002, resolved.BodyToken);
    }

    static ApiMember Indexer(string parameterType, int getterToken) => new()
    {
        Name = "Item",
        Kind = "property",
        ReturnType = "int",
        GetterToken = getterToken,
        SignatureModel = new ApiSignature
        {
            ReturnType = "int",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "index",
                    Type = parameterType,
                },
            ],
        },
    };
}
