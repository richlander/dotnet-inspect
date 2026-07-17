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
    public void FormatTypeParameterConstraints_UsesStructuredKindToDisambiguateKeywordFromTypeName()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["struct", "struct"],
            StructuredConstraints =
            [
                new TypeParameterConstraint("struct", IsTypeName: false),
                new TypeParameterConstraint("struct", IsTypeName: true),
            ],
        };

        Assert.Equal(
            "struct, @struct",
            CSharpFormatter.FormatTypeParameterConstraints(typeParameter, ["T"]));
    }

    [Fact]
    public void FormatTypeParameterConstraints_FallsBackToHeuristicWithoutStructuredKind()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["class", "TestNS.class"],
        };

        Assert.Equal(
            "class, TestNS.@class",
            CSharpFormatter.FormatTypeParameterConstraints(typeParameter, ["T"]));
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
    // A qualified "delegate" segment is a type name, never a function-pointer head.
    [InlineData("N.delegate*<int, void>", "N.@delegate*<int, void>")]
    [InlineData("N.delegate", "N.@delegate")]
    // Already-escaped identifiers are left untouched (idempotent).
    [InlineData("@int", "@int")]
    [InlineData("N.@int", "N.@int")]
    public void EscapeTypeKeywords_EscapesIdentifiersButNotTypeSyntax(string input, string expected)
        => Assert.Equal(expected, CSharpFormatter.EscapeTypeKeywords(input));

    [Theory]
    // Every CLR primitive full name aliases to its C# keyword, including the native
    // ints (nint/nuint) and decimal, matching the product decompiler's spelling.
    [InlineData("System.Boolean", "bool")]
    [InlineData("System.Byte", "byte")]
    [InlineData("System.SByte", "sbyte")]
    [InlineData("System.Char", "char")]
    [InlineData("System.Decimal", "decimal")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Int16", "short")]
    [InlineData("System.UInt16", "ushort")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.UInt32", "uint")]
    [InlineData("System.Int64", "long")]
    [InlineData("System.UInt64", "ulong")]
    [InlineData("System.IntPtr", "nint")]
    [InlineData("System.UIntPtr", "nuint")]
    [InlineData("System.Object", "object")]
    [InlineData("System.String", "string")]
    [InlineData("System.Void", "void")]
    // Primitives nested in generics, arrays, pointers, and by-ref forms are aliased.
    [InlineData("System.Collections.Generic.List<System.Int32>", "System.Collections.Generic.List<int>")]
    [InlineData("System.Collections.Generic.Dictionary<System.String,System.Boolean>", "System.Collections.Generic.Dictionary<string,bool>")]
    [InlineData("System.Int32[]", "int[]")]
    [InlineData("System.Int32[,]", "int[,]")]
    [InlineData("System.Int32&", "int&")]
    [InlineData("System.Int32*", "int*")]
    [InlineData("System.Nullable<System.Int32>[]", "System.Nullable<int>[]")]
    // A longer name that merely contains a primitive as a substring is left alone.
    [InlineData("System.Int32Enum", "System.Int32Enum")]
    [InlineData("A.System.Int32", "A.System.Int32")]
    [InlineData("System.Int32.MaxValue", "System.Int32.MaxValue")]
    [InlineData("System.Collections.Generic.List<System.Guid>", "System.Collections.Generic.List<System.Guid>")]
    // An explicitly-escaped identifier (leading '@') is not a primitive reference.
    [InlineData("@System.Int32", "@System.Int32")]
    [InlineData("List<@System.Int32>", "List<@System.Int32>")]
    // Non-System text and already-keyword spellings pass through unchanged.
    [InlineData("int", "int")]
    [InlineData("MyNamespace.MyType", "MyNamespace.MyType")]
    [InlineData("", "")]
    public void AliasPrimitiveTypeNames_RewritesClrPrimitivesToKeywords(string input, string expected)
        => Assert.Equal(expected, CSharpFormatter.AliasPrimitiveTypeNames(input));

    [Theory]
    [InlineData("this(x)", CSharpConstructorInitializerKind.This, "x")]
    [InlineData("base(a, b)", CSharpConstructorInitializerKind.Base, "a, b")]
    // A leading ": " (the emitted form) is accepted and stripped.
    [InlineData(": this(1)", CSharpConstructorInitializerKind.This, "1")]
    [InlineData(": base()", CSharpConstructorInitializerKind.Base, null)]
    // Nested calls are carried verbatim as a single argument (no top-level split).
    [InlineData("base(Wrap(a, b), c)", CSharpConstructorInitializerKind.Base, "Wrap(a, b), c")]
    public void ParseConstructorInitializer_ParsesThisAndBaseChains(
        string chain,
        CSharpConstructorInitializerKind expectedKind,
        string? expectedArgument)
    {
        var initializer = CSharpFormatter.ParseConstructorInitializer(chain);
        Assert.NotNull(initializer);
        Assert.Equal(expectedKind, initializer!.Kind);
        Assert.Equal(
            expectedArgument is null ? [] : new[] { expectedArgument },
            initializer.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomeMethod(x)")]
    [InlineData("this")]
    public void ParseConstructorInitializer_ReturnsNullForNonChains(string? chain)
        => Assert.Null(CSharpFormatter.ParseConstructorInitializer(chain));

    [Fact]
    public void ParseConstructorInitializer_RoundTripsFormatConstructorInitializer()
    {
        var initializer = new CSharpConstructorInitializer(
            CSharpConstructorInitializerKind.This,
            ["a, b"]);
        string formatted = CSharpFormatter.FormatConstructorInitializer(initializer);
        var parsed = CSharpFormatter.ParseConstructorInitializer(formatted);
        Assert.NotNull(parsed);
        Assert.Equal(initializer.Kind, parsed!.Kind);
        Assert.Equal(initializer.Arguments, parsed.Arguments);
    }

    [Theory]
    [InlineData("int*", true)]
    [InlineData("delegate*<int, void>", true)]
    [InlineData("stackalloc int[4]", true)]
    [InlineData("int", false)]
    [InlineData("System.Collections.Generic.List<int>", false)]
    [InlineData("a + b", false)]
    public void RequiresUnsafeModifier_DetectsPointerAndStackalloc(string csharp, bool expected)
        => Assert.Equal(expected, CSharpFormatter.RequiresUnsafeModifier(csharp));

    [Theory]
    [InlineData("int*", true)]
    [InlineData("delegate*<int, void>", true)]
    [InlineData("System.Int32*", true)]
    [InlineData("int", false)]
    [InlineData("System.Collections.Generic.List<int>", false)]
    [InlineData("stackalloc", false)]
    [InlineData("@stackalloc", false)]
    [InlineData("N.stackalloc", false)]
    public void TypeRequiresUnsafeModifier_MatchesPointersButNotStackallocIdentifiers(string typeDisplayName, bool expected)
        => Assert.Equal(expected, CSharpFormatter.TypeRequiresUnsafeModifier(typeDisplayName));

    [Theory]
    [InlineData("List`1", "List")]
    [InlineData("Dictionary`2", "Dictionary")]
    [InlineData("Widget", "Widget")]
    [InlineData("", "")]
    public void StripArity_RemovesGenericAritySuffix(string name, string expected)
        => Assert.Equal(expected, CSharpFormatter.StripArity(name));

    [Theory]
    [InlineData("System.Int32", "int")]
    [InlineData("A.B.delegate*", "A.B.@delegate*")]
    [InlineData("ref readonly", "ref @readonly")]
    public void CleanTypeDisplay_NormalizesToCSharpSpelling(string type, string expected)
        => Assert.Equal(expected, CSharpFormatter.CleanTypeDisplay(type));

    [Fact]
    public void CleanTypeDisplay_CollapsesUnspeakableGenericParameterToObject()
        => Assert.Equal("object", CSharpFormatter.CleanTypeDisplay("!0"));
}
