using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpFormatterTests
{
    [Fact]
    public void FormatsStructuredMemberDeclaration()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Container`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };
        var member = new ApiMember
        {
            Name = "Map",
            Kind = "method",
            Signature = "compatibility text",
            SignatureModel = new ApiSignature
            {
                ReturnType = "TResult",
                MemberName = "Map<TResult>",
                TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                Parameters = [new ApiParameter { Type = "T", Name = "value" }]
            }
        };

        var declaration = new CSharpFormatter().FormatMember(type, member);

        Assert.Equal(
            "public TResult Map<TResult>(T value) where TResult : class",
            declaration);
    }

    [Fact]
    public void FormatsContextualTypeUnit()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Create",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Samples.Widget",
                        MemberName = "Create"
                    }
                }
            ]
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = "Samples",
            TerminateMemberDeclaration = true
        });

        var declaration = formatter.FormatTypeUnit(type, type.Members);

        Assert.Equal(
            """
            public class Widget
            {
                public Widget Create();
            }
            """,
            declaration.Text);
        Assert.Empty(declaration.Usings);
        Assert.Empty(declaration.Diagnostics);
    }

    [Fact]
    public void FormatsParameterListsWithAttributesDefaultsAndEscapedKeywords()
    {
        var parameters = new ApiParameter[]
        {
            new()
            {
                Attributes = ["System.Runtime.InteropServices.Optional"],
                Type = "System.event.MyClass",
                Name = "event",
                HasDefault = true,
                DefaultValueText = "default"
            },
            new() { Type = "class", Name = "value" },
            new() { Type = "System.Collections.Generic.List<class>", Name = "items" },
            new() { Type = "delegate", Name = "delegateValue" },
            new() { Type = "readonly", Name = "readonlyValue" },
            new() { Type = "scoped", Name = "scopedValue" },
            new() { Type = "delegate*<ref int, void>", Name = "callback" }
        };

        Assert.Equal(
            "([System.Runtime.InteropServices.Optional] System.@event.MyClass @event = default, @class value, System.Collections.Generic.List<@class> items, @delegate delegateValue, @readonly readonlyValue, @scoped scopedValue, delegate*<ref int, void> callback)",
            CSharpFormatter.FormatParameterList(parameters));
    }

    [Theory]
    [InlineData("await")]
    [InlineData("file")]
    [InlineData("init")]
    [InlineData("record")]
    [InlineData("required")]
    [InlineData("scoped")]
    public void EscapesConservativeContextualKeywordSet(string identifier)
        => Assert.Equal($"@{identifier}", CSharpFormatter.EscapeIdentifier(identifier));

    [Fact]
    public void FormatsDelegateWithStructuredAccessibility()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Callback",
            Kind = "delegate",
            Accessibility = "private",
            Members =
            [
                new ApiMember
                {
                    Name = "Invoke",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = "Invoke"
                    }
                }
            ]
        };

        Assert.Equal(
            "private delegate void Callback();",
            new CSharpFormatter().FormatDelegate(type, type.Members.Single()));
    }

    [Fact]
    public void KnownIdentifierEscapingIsIdempotent()
    {
        Assert.Equal(
            "System.Action<@event>",
            CSharpFormatter.EscapeKnownIdentifiers("System.Action<@event>", ["event"]));
        Assert.Equal(
            "System.Action<@event>",
            CSharpFormatter.EscapeKnownIdentifiers("System.Action<event>", ["event"]));
    }

    [Fact]
    public void RejectsUndefinedPolicies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CSharpFormatter(new CSharpFormatOptions
            {
                TypeNamePolicy = (CSharpTypeNamePolicy)42
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CSharpFormatter(new CSharpFormatOptions
            {
                NamespacePolicy = (CSharpNamespacePolicy)42
            }));
    }

    [Theory]
    // Primitive/void aliases stay bare when they name the primitive.
    [InlineData("int", "int")]
    [InlineData("string", "string")]
    [InlineData("void", "void")]
    [InlineData("int[]", "int[]")]
    [InlineData("int*", "int*")]
    [InlineData("System.Int32", "System.Int32")]
    [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List<int>")]
    // A type literally named after a primitive keyword (a dotted-name segment) is escaped.
    [InlineData("N.int", "N.@int")]
    [InlineData("int.MaxValue", "int.MaxValue")]
    // Reserved keywords used as identifiers are escaped, including inside generic args.
    [InlineData("class", "@class")]
    [InlineData("await", "@await")]
    [InlineData("record", "@record")]
    [InlineData("List<await>", "List<@await>")]
    [InlineData("MyType<class>", "MyType<@class>")]
    [InlineData("Foo.await", "Foo.@await")]
    [InlineData("await.Foo", "@await.Foo")]
    [InlineData("N.readonly", "N.@readonly")]
    // Parameter/type modifiers stay bare in a leading modifier run.
    [InlineData("ref int", "ref int")]
    [InlineData("ref readonly int", "ref readonly int")]
    [InlineData("scoped ref int", "scoped ref int")]
    [InlineData("in long", "in long")]
    [InlineData("out string", "out string")]
    [InlineData("params byte[]", "params byte[]")]
    // Function-pointer syntax stays bare, and reserved args inside are still escaped.
    [InlineData("delegate*<int, void>", "delegate*<int, void>")]
    [InlineData("delegate* unmanaged<int>", "delegate* unmanaged<int>")]
    [InlineData("delegate*<ref int, void>", "delegate*<ref int, void>")]
    [InlineData("delegate*<await, void>", "delegate*<@await, void>")]
    // Pointers to types literally named like a keyword must be escaped, not read as
    // type syntax: "ref*"/"in*" are pointers to a type named ref/in, and a bare
    // "delegate*" (not a function-pointer head) is a pointer to a type named delegate.
    [InlineData("ref*", "@ref*")]
    [InlineData("in*", "@in*")]
    [InlineData("readonly*", "@readonly*")]
    [InlineData("delegate*", "@delegate*")]
    [InlineData("delegate*[]", "@delegate*[]")]
    // Whitespace before terminating punctuation is not a modifier/calling-convention
    // boundary: the keyword names a type and must be escaped.
    [InlineData("ref *", "@ref *")]
    [InlineData("Tuple<readonly >", "Tuple<@readonly >")]
    [InlineData("(delegate* , int)", "(@delegate* , int)")]
    [InlineData("Tuple<delegate* >", "Tuple<@delegate* >")]
    [InlineData("delegate* managed<int, void>", "delegate* managed<int, void>")]
    // Whitespace between "delegate*" and '<' is still a function-pointer head.
    [InlineData("delegate* <int, void>", "delegate* <int, void>")]
    // Already-escaped identifiers are left untouched (idempotent).
    [InlineData("@int", "@int")]
    [InlineData("N.@int", "N.@int")]
    public void EscapeTypeKeywords_EscapesIdentifiersButNotTypeSyntax(string input, string expected)
        => Assert.Equal(expected, CSharpFormatter.EscapeTypeKeywords(input));
}
