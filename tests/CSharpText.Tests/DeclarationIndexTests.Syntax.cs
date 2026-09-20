using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpText.Tests;

public partial class DeclarationIndexTests
{
    [Fact]
    public void AConstructorNamedExtension_IsNotAnExtensionBlock()
    {
        var index = DeclarationIndex.Build("""
            class extension
            {
                extension()
                {
                    void Local() { }
                }
            }
            """);

        var constructor = Assert.Single(
            index.Declarations,
            declaration => declaration.Kind == DeclarationKind.Constructor);
        Assert.Equal("extension", constructor.Name);
        Assert.Equal(3, constructor.SignatureStartLine);
        Assert.Equal(6, constructor.EndLine);
        Assert.DoesNotContain(index.Declarations, declaration => declaration.Name == "Local");
        Assert.Empty(index.TransparentScopes);
    }

    [Fact]
    public void AnExtensionBlockInAPartialPartWithoutStatic_IsTransparent()
    {
        var index = DeclarationIndex.Build("""
            partial class Ext
            {
                extension(int value)
                {
                    public int Doubled => value * 2;
                }
            }

            static partial class Ext
            {
            }
            """);

        Assert.DoesNotContain(index.Declarations, declaration => declaration.Name == "extension");
        var property = Assert.Single(index.FindByName(DeclarationKind.Property, "Doubled"));
        Assert.True(property.SpanKnown);
        Assert.Single(index.TransparentScopes);
    }

    [Fact]
    public void APlainExtensionHeaderInANonStaticPartialTypeNamedExtension_IsAmbiguous()
    {
        var index = DeclarationIndex.Build("""
            partial class extension
            {
                extension()
                {
                    void Local() { }
                }
            }
            """);

        Assert.DoesNotContain(
            index.Declarations,
            declaration => !declaration.IsType && declaration.SpanKnown);
        var local = Assert.Single(index.FindByName(DeclarationKind.Method, "Local"));
        Assert.False(local.SpanKnown);
        Assert.Single(index.TransparentScopes);
    }

    [Fact]
    public void AConditionalStaticModifierKeepsConstructorShapedExtensionSyntaxAmbiguous()
    {
        var index = DeclarationIndex.Build("""
            #if STATIC_EXTENSION
            static
            #endif
            class extension
            {
                extension(int value)
                {
                    public void M() { }
                }
            }
            """);

        var method = Assert.Single(index.FindByName(DeclarationKind.Method, "M"));
        Assert.False(method.SpanKnown);
        Assert.Single(index.TransparentScopes);
    }

    [Fact]
    public void AnIncompleteGenericExtensionHeaderDoesNotBecomeATrustedOuterMethod()
    {
        var index = DeclarationIndex.Build("""
            static class C
            {
                extension<T(int value)
                {
                    public void M() { }
                }
            }
            """);

        Assert.DoesNotContain(
            index.Declarations,
            declaration => declaration.Name == "T" && declaration.SpanKnown);
        var method = Assert.Single(index.FindByName(DeclarationKind.Method, "M"));
        Assert.False(method.SpanKnown);
        Assert.Single(index.TransparentScopes);
    }

    [Theory]
    [InlineData(": base(new Options { Path = path })")]
    [InlineData(": base(new object[] { path })")]
    [InlineData(": base(() => { Use(path); })")]
    [InlineData(": base(path is { Length: > 0 })")]
    public void BracesInsideAConstructorInitializer_DoNotCloseTheConstructor(string initializer)
    {
        var index = DeclarationIndex.Build($$"""
            class C
            {
                C(string path)
                    {{initializer}}
                {
                    Use(path);
                }
            }
            """);

        var ctor = Assert.Single(index.Declarations, d => d.Kind == DeclarationKind.Constructor);
        Assert.Equal(3, ctor.SignatureStartLine);
        Assert.Equal(5, ctor.BodyStartLine);
        Assert.Equal(7, ctor.BodyEndLine);
        Assert.Equal(7, ctor.EndLine);
        Assert.True(ctor.SpanKnown);
        Assert.False(ctor.IsStatic);
    }

    [Fact]
    public void ObjectInitializerFollowedByAnotherConstructorArgument_DoesNotCreateAPhantomRow()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                C(S value, int count) { }
                C() : this(new S { Value = 1 }, 2) { }
            }
            """);

        Assert.Equal(
            2,
            index.Declarations.Count(d => d.Kind == DeclarationKind.Constructor));
        Assert.DoesNotContain(
            index.Declarations,
            d => d.ParentIndex >= 0 && d.Kind == DeclarationKind.Property);
    }

    [Fact]
    public void AStaticConstructorCarriesStaticIdentity()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                C() { }
                static C() { }
            }
            """);

        var constructors = index.Declarations
            .Where(d => d.Kind == DeclarationKind.Constructor)
            .OrderBy(d => d.SignatureStartLine)
            .ToArray();
        Assert.False(constructors[0].IsStatic);
        Assert.True(constructors[1].IsStatic);
    }

    [Fact]
    public void SignatureColumnAndInitializerFacts_AreCarriedByTheIndex()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                /* closed */ static int Field = 1;
                int Property { get; } = 2;
                void M() { }
            }
            """);

        var field = Assert.Single(index.Declarations, d => d.Name == "Field");
        Assert.Equal(17, field.SignatureStartColumn);
        Assert.Equal(17, field.FirstCodeColumn);
        Assert.True(field.IsStatic);
        Assert.True(field.HasInitializer);

        var property = Assert.Single(index.Declarations, d => d.Name == "Property");
        Assert.False(property.IsStatic);
        Assert.True(property.HasInitializer);

        var method = Assert.Single(index.Declarations, d => d.Name == "M");
        Assert.False(method.HasInitializer);
    }

    [Fact]
    public void StaticInAnExpressionBody_IsNotADeclarationModifier()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                Func<int> Factory;
                C() => Factory = static () => 1;
                static C() => Factory = static () => 2;
            }
            """);

        var constructors = index.Declarations
            .Where(d => d.Kind == DeclarationKind.Constructor)
            .OrderBy(d => d.SignatureStartLine)
            .ToArray();
        Assert.False(constructors[0].IsStatic);
        Assert.True(constructors[1].IsStatic);
    }

    [Fact]
    public void ATypeTrailerExtendsTheDeclarationButNotItsBody()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            }
            ;
            """);

        var type = Assert.Single(index.Declarations);
        Assert.Equal(3, type.BodyEndLine);
        Assert.Equal(4, type.EndLine);
    }

    /// <summary>
    /// The selector for members the PDB cannot reach. An interface method has no body, so it has no
    /// sequence point and no line range — a name is the only way in.
    /// </summary>
    [Fact]
    public void FindByName_ReachesADeclarationThatHasNoBody()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public interface IThing
            {
                /// <summary>Writes it.</summary>
                void WriteTo(int x);
            }
            """);

        var found = Assert.Single(index.FindByName(DeclarationKind.Method, "WriteTo"));
        Assert.Equal(5, found.SignatureStartLine);
        Assert.Equal(4, found.TriviaStartLine);
        Assert.False(found.HasBody);
    }

    [Fact]
    public void AFileScopedNamespace_EnclosesTheRestOfTheFileJustAsABlockNamespaceDoes()
    {
        var scoped = DeclarationIndex.Build("""
            namespace N;
            public class C { }
            """);
        var block = DeclarationIndex.Build("""
            namespace N
            {
                public class C { }
            }
            """);

        var a = Assert.Single(scoped.Declarations.Where(s => s.Kind == DeclarationKind.Class));
        var b = Assert.Single(block.Declarations.Where(s => s.Kind == DeclarationKind.Class));
        Assert.Equal(1, a.Depth);
        Assert.Equal(1, b.Depth);
        Assert.Equal("N", scoped.ParentOf(a)!.Name);
        Assert.Equal("N", block.ParentOf(b)!.Name);
    }

    /// <summary>
    /// Metadata sees three fields, so the index owes three rows. They share one span because they
    /// share one declaration.
    /// </summary>
    [Fact]
    public void EachDeclaratorOfAMultiNameFieldGetsItsOwnRow()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int A, B, C2;
                public const int P = 1, Q = 2;
                public System.Collections.Generic.Dictionary<string, int> Map = new();
            }
            """);

        Assert.Equal(
            ["A", "B", "C2", "P", "Q", "Map"],
            index.Declarations.Where(s => s.Kind == DeclarationKind.Field).Select(s => s.Name));
    }

    [Fact]
    public void EachDeclaratorCarriesItsOwnInitializerFact()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                static int A, B = 1, C2;
                event System.Action E1, E2 = null, E3;
            }
            """);

        Assert.Equal(
            [("A", false), ("B", true), ("C2", false)],
            index.Declarations
                .Where(declaration => declaration.Kind == DeclarationKind.Field)
                .Select(declaration => (declaration.Name, declaration.HasInitializer)));
        Assert.Equal(
            [("E1", false), ("E2", true), ("E3", false)],
            index.Declarations
                .Where(declaration => declaration.Kind == DeclarationKind.Event)
                .Select(declaration => (declaration.Name, declaration.HasInitializer)));
    }

    /// <summary>
    /// An expression-bodied property and a field initialized to a lambda both spell "=&gt;", and
    /// the difference is which one the header was cut at.
    /// </summary>
    [Fact]
    public void AnArrowIsAnExpressionBodyOnlyWhenItIsTheHeadersOwn()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int P => 1;
                public System.Func<int, int> F = x => x;
                public T G<T>(T x) where T : new() => x;
            }
            """);

        Assert.Equal(DeclarationKind.Property, Assert.Single(index.FindByName(DeclarationKind.Property, "P")).Kind);
        var f = Assert.Single(index.FindByName(DeclarationKind.Field, "F"));
        Assert.False(f.HasBody);
        Assert.True(Assert.Single(index.FindByName(DeclarationKind.Method, "G")).HasBody);
    }

    /// <summary>
    /// A generic type argument list carries commas of its own, and they are not declarator
    /// boundaries. Two arguments happen to survive a lookahead-only rule; three do not, because
    /// the first comma is then followed by a name and another comma — the exact shape a declarator
    /// list has.
    /// </summary>
    [Fact]
    public void CommasInsideATypeArgumentList_AreNotDeclaratorBoundaries()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public System.Action<string, int, float> A;
                public System.Func<int, int, int, int, string> B, B2;
                public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>> D;
            }
            """);

        Assert.Equal(
            ["A", "B", "B2", "D"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
    }

    /// <summary>
    /// A verbatim identifier is a name that merely spells a keyword. Reading it as the keyword
    /// turns a field into a type declaration and a parameter into a delegate.
    /// </summary>
    [Fact]
    public void AnIdentifierThatSpellsAKeyword_IsNotThatKeyword()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int @class;
                public int @where => 1;
                public void M(int @delegate) { }
                public void N2(int @this) { }
                public int @event;
            }
            """);

        Assert.Equal(["class", "event"], index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
        Assert.Equal(["where"], index.Declarations.Where(d => d.Kind == DeclarationKind.Property).Select(d => d.Name));
        Assert.Equal(["M", "N2"], index.Declarations.Where(d => d.Kind == DeclarationKind.Method).Select(d => d.Name));
        Assert.Empty(index.Declarations.Where(d => d.Kind is DeclarationKind.Delegate or DeclarationKind.Event));
        Assert.Single(index.Declarations.Where(d => d.IsType));
    }

    /// <summary>
    /// Two declarations can be spelled identically — a partial method's defining and implementing
    /// halves share a name and differ only in span. The differential compares multisets, so a row
    /// emitted twice or dropped once is a failure; this is the fixture that has cardinality to
    /// lose.
    /// </summary>
    [Fact]
    public void TwoDeclarationsSpelledAlike_AreBothReported()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public partial class C
            {
                public partial void M();
                public partial void M() { }
            }
            """);

        var found = index.FindByName(DeclarationKind.Method, "M");
        Assert.Equal(2, found.Length);
        Assert.False(found[0].HasBody);
        Assert.True(found[1].HasBody);
    }

    /// <summary>
    /// The oracle's decline rule, gated directly. The corpus currently contains no file with a
    /// conditional directive, so nothing else exercises either arm: a rule that declined every
    /// file, or none, would look identical from the corpus. It declines on a real directive and
    /// only on a real directive — <c>#if</c> spelled in a comment, a string, or a
    /// <c>#region</c>/<c>#pragma</c>/<c>#nullable</c> directive is not conditional compilation.
    /// </summary>
    [Fact]
    public void TheOracleDeclines_OnAConditionalDirectiveAndOnlyOnOne()
    {
        Assert.Null(RoslynDeclarations(["#if NET", "class A { }", "#endif"]));
        Assert.Null(RoslynDeclarations(["#if NET", "class A { }", "#else", "class B { }", "#endif"]));

        Assert.NotNull(RoslynDeclarations(["// mentions #if and #else", "class A { }"]));
        Assert.NotNull(RoslynDeclarations(["class A { const string S = \"#if\"; }"]));
        Assert.NotNull(RoslynDeclarations(["#nullable enable", "#region R", "#pragma warning disable", "class A { }", "#endregion"]));
    }

    /// <summary>
    /// An extension block is a scope, not a declaration, in both its plain and its generic form.
    /// Getting this wrong does not cost one bad row: the block is indexed as a method, and every
    /// member inside it is then rejected for sitting in a method rather than a type, so the
    /// extension members vanish from the index entirely.
    /// </summary>
    [Fact]
    public void AGenericExtensionBlock_IsTransparentJustLikeAPlainOne()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public static class C
            {
                extension(string receiver)
                {
                    public int Plain => 1;
                }

                extension<T>(System.Collections.Generic.IEnumerable<T> source)
                {
                    public System.Collections.Generic.IEnumerable<T> Page(int n) => source;
                    public bool IsEmpty => true;
                }
            }
            """);

        Assert.Empty(index.Declarations.Where(d => d.Name == "extension"));
        Assert.Equal(
            ["Plain", "Page", "IsEmpty"],
            index.Declarations.Where(d => d.Kind is DeclarationKind.Method or DeclarationKind.Property)
                .Select(d => d.Name));

        // Every extension member's parent is the enclosing class, not a row for the block.
        var owner = index.Declarations.Single(d => d.Kind == DeclarationKind.Class);
        Assert.All(
            index.Declarations.Where(d => d.Kind is DeclarationKind.Method or DeclarationKind.Property),
            d => Assert.Equal(owner, index.ParentOf(d)));

        Assert.Equal(
            ["4-7", "9-13"],
            index.TransparentScopes.Select(scope => scope.ToString()));
        Assert.Equal(
            [5, 10],
            index.TransparentScopes.Select(scope => scope.BodyStartLine));
    }

    [Fact]
    public void AnAttributeArrayInAnExtensionReceiver_DoesNotOpenTheExtensionBody()
    {
        var index = DeclarationIndex.Build("""
            static class C
            {
                extension(
                    [A(new int[] {
                        1
                    })]
                    string receiver)
                {
                    public void M()
                    {
                    }
                }
            }
            """);

        var method = Assert.Single(index.FindByName(DeclarationKind.Method, "M"));
        var owner = Assert.Single(index.FindByName(DeclarationKind.Class, "C"));
        Assert.Equal(owner, index.ParentOf(method));
        Assert.Equal(9, method.SignatureStartLine);
        Assert.Equal(11, method.EndLine);

        var scope = Assert.Single(index.TransparentScopes);
        Assert.Equal(3, scope.StartLine);
        Assert.Equal(8, scope.BodyStartLine);
        Assert.Equal(12, scope.EndLine);
    }

    [Fact]
    public void ARelationalOperatorInAGenericExtensionAttribute_DoesNotHideTheExtensionBlock()
    {
        var index = DeclarationIndex.Build("""
            static class C
            {
                extension<[A(1 > 0)] T>(T receiver)
                {
                    public void M() { }
                }
            }
            """);

        var method = Assert.Single(index.FindByName(DeclarationKind.Method, "M"));
        var owner = Assert.Single(index.FindByName(DeclarationKind.Class, "C"));
        Assert.Equal(owner, index.ParentOf(method));
        Assert.Equal(5, method.SignatureStartLine);
        Assert.Equal(5, method.EndLine);
        Assert.Single(index.TransparentScopes);
    }

    [Fact]
    public void AnUnknownExtensionHeader_DoesNotVouchForDeclarationsInsideItsBraces()
    {
        var conditional = DeclarationIndex.Build("""
            static class C
            {
            #if EXT
                extension(C receiver)
            #else
                int P
            #endif
                {
                    get { return 1; }
                }
            }
            """);
        var malformed = DeclarationIndex.Build("""
            static class C
            {
                extension([)] )
                {
                    public static void M()
                    {
                    }
                }
            }
            """);

        Assert.DoesNotContain(
            conditional.Declarations,
            declaration => !declaration.IsType
                && declaration.SpanKnown
                && declaration.Contains(9));
        Assert.DoesNotContain(
            malformed.Declarations,
            declaration => !declaration.IsType
                && declaration.SpanKnown
                && declaration.Contains(5));
    }

    [Fact]
    public void AnUnknownExtensionScopeCarriesTrustIntoInPassParentDecisions()
    {
        var index = DeclarationIndex.Build("""
            static class C
            {
                class Sh { }
            #if X
                extension(int x)
            #else
                int P
            #endif
                {
                    public void M()
                    {
            #if Y
                        int F = 1
            #endif
                        ;
                    }
                }
            }
            """);

        var sibling = Assert.Single(index.FindByName(DeclarationKind.Class, "Sh"));
        Assert.False(
            sibling.SpanKnown,
            "the in-pass reachability walk must not stop at a parent whose scope is unknown");
        var method = Assert.Single(index.FindByName(DeclarationKind.Method, "M"));
        Assert.False(method.SpanKnown);
    }

    [Fact]
    public void MismatchedHeaderDelimiters_DoNotProduceAKnownDeclaration()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                void M() ) [
                {
                    void N() { }
                }
            }
            """);

        Assert.DoesNotContain(
            index.Declarations,
            declaration => declaration.SpanKnown && (declaration.Name is "M" or "N"));
    }

    [Theory]
    [InlineData("() => { return 1; }")]
    [InlineData("new Holder { Value = 1 }")]
    [InlineData("value is { }")]
    public void PrimaryConstructorBaseArgumentBraces_DoNotBecomeTheTypeBody(string argument)
    {
        var index = DeclarationIndex.Build($$"""
            class C(object value) : B({{argument}})
            {
                void M() { }
            }
            """);

        var type = Assert.Single(index.FindByName(DeclarationKind.Class, "C"));
        var method = Assert.Single(index.FindByName(DeclarationKind.Method, "M"));
        Assert.Equal(4, type.EndLine);
        Assert.Equal(type, index.ParentOf(method));
    }

    /// <summary>
    /// A checked operator carries <c>checked</c> in its name, because it is a distinct metadata
    /// member: <c>operator checked +</c> emits <c>op_CheckedAddition</c> and may be declared
    /// alongside <c>op_Addition</c> in the same type. The oracle derives the same name
    /// independently, from Roslyn's <c>CheckedKeyword</c>.
    /// </summary>
    [Fact]
    public void ACheckedOperator_IsNamedForItsSymbolAlone()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class D
            {
                public static D operator +(D x, D y) => x;
                public static D operator checked +(D x, D y) => x;
                public static explicit operator int(D d) => 0;
                public static explicit operator checked int(D d) => 0;
            }
            """);

        Assert.Equal(
            ["operator +", "operator checked +", "operator explicit", "operator checked explicit"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Method).Select(d => d.Name));
    }

    /// <summary>
    /// A generic type argument list in an <em>initializer</em> also carries commas, and the angle
    /// counter cannot run there because a relational <c>&lt;</c> never closes. The speculative
    /// match must skip the real type argument list without swallowing a relational comparison —
    /// the last two fields here are the negative case, and losing them would be as wrong as
    /// inventing a row from the first.
    /// </summary>
    [Fact]
    public void CommasInsideAGenericInitializer_AreNotDeclaratorBoundaries()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            using System;
            public class C
            {
                public Action A = new Action<int, int, int>(null), B = null;
                public object F = Foo<int, string>.Bar, G = null;
                public bool X = 1 < 2, Y = 3 > 2;
                public bool P = A2 < B2, Q = C2 > D2;
                public dynamic H = a < Broken + Foo<int, string, float>.Bar, I = null;
            }
            """);

        Assert.Equal(
            ["A", "B", "F", "G", "X", "Y", "P", "Q", "H", "I"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
    }

    /// <summary>
    /// A file-scoped namespace <em>scopes</em> the rest of the file, but its declaration ends where
    /// its last member ends. Trailing trivia belongs to the file, not to the namespace — Roslyn's
    /// span says so, and no corpus file happens to have any, so without this the gate agreed only
    /// by luck and one added trailing comment anywhere in the repository would have turned it red.
    /// </summary>
    [Fact]
    public void AFileScopedNamespace_EndsAtItsLastMemberNotAtTheLastLine()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            class C { }
            // trailing comment


            """);

        var ns = index.Declarations.Single(d => d.Kind == DeclarationKind.Namespace);
        Assert.Equal(2, ns.EndLine);
        Assert.True(ns.SpanKnown);

        var empty = DeclarationIndex.Build("namespace N;");
        Assert.Equal(1, empty.Declarations.Single().EndLine);
    }

    /// <summary>
    /// A file-scoped namespace's span reaches every later declaration, so it cannot be better known
    /// than they are. Its EOF-closing special case used to skip the unknown-span marking entirely,
    /// which reported a measured span for a file whose brace structure the scan could not follow —
    /// exactly the guess the type exists to avoid.
    /// </summary>
    [Fact]
    public void AFileScopedNamespaceOverAConditionalRegion_ReportsAnUnknownSpan()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            #if FEATURE
            class A { }
            #else
            class B { }
            #endif
            """);

        Assert.All(index.Declarations, d => Assert.False(d.SpanKnown));
    }

    /// <summary>
    /// A file-scoped namespace's end is a maximum over the rows it encloses, so it is only as good
    /// as the worst of them. A member whose brace never closes reports the last line as a guess;
    /// the namespace used to adopt that guess and still call its own span measured, because the
    /// unknown-span marking keyed on lost lexical depth and an unclosed brace never loses it.
    /// </summary>
    [Fact]
    public void AFileScopedNamespaceOverAnUnclosedMember_ReportsAnUnknownSpan()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            class A {
            // trailing comment
            """);

        Assert.All(index.Declarations, d => Assert.False(d.SpanKnown));
    }

    /// <summary>
    /// The punctuators <see cref="DeclarationIndexBuilder"/> accepts inside a speculatively matched
    /// type argument list are load-bearing: drop the array, tuple, pointer, or qualified-name
    /// entries and each of these initializers stops matching, so its commas split declarators and
    /// invent fields named for a type. Every entry in that allow list appears below.
    /// </summary>
    [Fact]
    public void ATypeArgumentListInAnInitializer_MayContainAnyTypeSyntax()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            using System;
            public unsafe class C
            {
                public object a = new Func<int[], string, int>(null), b = null;
                public object c = new Func<(int, string), string, int>(null), d = null;
                public object e = new Func<int*[], string, int>(null), f = null;
                public object g = new Func<System.Text.Rune, string, int>(null), h = null;
                public object i = new Func<global::System.Guid, string, int>(null), j = null;
                public object k = new Func<int?, string, int>(null), l = null;
                public object m = new Func<Func<int, int>, string, int>(null), n = null;
            }
            """);

        Assert.Equal(
            ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n"],
            index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name));
    }

    /// <summary>
    /// A speculative type argument list must leave its own groups balanced. <c>a &lt; b(name: c &gt; d)</c>
    /// reaches a <c>&gt;</c> with a <c>(</c> still open; accepting it skipped the <c>(</c> and left the
    /// matching <c>)</c> to drive the caller's group depth negative, after which no later comma could
    /// separate a declarator and every trailing declarator vanished from the index. All three shapes
    /// below compile.
    /// </summary>
    [Fact]
    public void ARelationalComparisonThatOpensAGroup_DoesNotMatchATypeArgumentList()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                static int x = 1, c = 2, d = 3;
                static int b(bool name) => 1;
                static int p = 1, q = 2, r = 3, s = 4;
                static int m(bool v) => 0;
                static int[] arr = new int[9];

                static bool a = x < b(name: c > d), f = true;
                static int g = x < p ? q : m(r > s), h = 1;
                static bool k = x < arr[c > d ? 1 : 0], n = false;
            }
            """);

        var fields = index.Declarations.Where(d => d.Kind == DeclarationKind.Field).Select(d => d.Name);
        Assert.Equal(
            ["x", "c", "d", "p", "q", "r", "s", "arr", "a", "f", "g", "h", "k", "n"],
            fields);
    }

    /// <summary>
    /// A block comment yields one scanner token per line it covers, so the token's own line is not
    /// where the comment began. Trivia attribution used the token's line, which put the second line
    /// of <c>int A; /* x</c> after the previous declaration's terminator and made it the *next*
    /// declaration's trivia start — a slice from there would begin inside the comment, past its
    /// <c>/*</c>, and produce source that does not compile.
    /// <para>
    /// Which token opens a comment cannot be read off the text: a continuation line may itself
    /// start with <c>//</c> or <c>/*</c>, and both shapes are below. Roslyn is the oracle here
    /// rather than a hand-written line number, because the whole question is what counts as
    /// leading trivia and that is Roslyn's answer to give. Every fixture compiles.
    /// </para>
    /// <para>
    /// Several fixtures exist only to make a sub-rule's misreading change an ANSWER rather than
    /// merely shift state, which is what a mutation can see: the last two put a comment after a
    /// block that must have closed, and the single-line <c>/* … */</c> before a further comment
    /// gates the opening test in both directions — treating <c>/*/</c> as closed, and treating a
    /// closed one-line block as still open.
    /// </para>
    /// </summary>
    [Fact]
    public void ABlockCommentSpanningATerminatorLine_TrailsTheDeclarationItStartedOn()
    {
        string[] fixtures =
        [
            "class C\n{\n    int A; /* trailing\n    comment */\n    int B;\n}",
            "class C\n{\n    int A;\n    /* leading\n    comment */\n    int B;\n}",
            "class C\n{\n    int A; // trailing\n    // leading\n    int B;\n}",
            "class C\n{\n    int A; /* one\n// still inside\n*/\n    int B;\n}",
            "class C\n{\n    int A; /* x\n/* y */\n    int B;\n}",
            "class C\n{\n    int A; /* a */ /* b\n c */\n    int B;\n}",
            "class C\n{\n    int A;\n    /*/ still open\n    */ int B;\n}",
            "class C\n{\n    int A; /* trailing\n    comment */\n    // leading B\n    int B;\n}",
            "class C\n{\n    int A; /*/\n    still inside */\n    int B;\n}",
            "class C\n{\n    /* single line */ int A;\n    /* next comment */\n    int B;\n}",
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            var actual = DeclarationIndex.Build(lines).Declarations;
            Assert.Equal(
                expected.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"),
                actual.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"));
        }
    }

    /// <summary>
    /// An <c>assembly:</c> or <c>module:</c> attribute list belongs to the compilation unit, not to
    /// whatever declaration follows it. It was treated as leading trivia of that declaration, so a
    /// slice would open with an assembly attribute that has nothing to do with the member selected.
    /// Roslyn also puts a file header comment above such a list inside the list's own trivia, so a
    /// unit attribute has to clear what came before it rather than merely decline to extend it.
    /// <para>
    /// The close negatives are the point: <c>[Obsolete]</c>, <c>[type: Obsolete]</c> and
    /// <c>[return: ...]</c> are part of the declaration that follows and must still be kept. Roslyn
    /// is the oracle rather than hand-written line numbers. Every fixture compiles.
    /// </para>
    /// </summary>
    [Fact]
    public void ACompilationUnitAttribute_IsNotTheNextDeclarationsTrivia()
    {
        string[] fixtures =
        [
            "using System;\n[assembly: CLSCompliant(true)]\nclass A1 { }",
            "[module: System.CLSCompliant(true)]\nclass A2 { }",
            "// file header\nusing System;\n[assembly: System.Reflection.AssemblyMetadata(\"k\",\"v\")]\nclass A3 { }",
            "using System;\n[Obsolete]\nclass A4 { }",
            "using System;\n[type: Obsolete]\nclass A5 { }",
            "class A6\n{\n    [return: System.Diagnostics.CodeAnalysis.NotNull]\n    string M() => \"\";\n}",
            "using System;\n[assembly: System.Reflection.AssemblyDescription(\"d\")]\n[Obsolete]\nclass A7 { }",
            "using System;\n[assembly: System.Reflection.AssemblyProduct(\"p\")]\n\n[assembly: System.Reflection.AssemblyCompany(\"c\")]\nclass A8 { }",
            "using System;\nclass assemblyAttribute : Attribute { }\n[assembly]\nclass A9 { }",
            "using System;\nclass assemblyAttribute : Attribute { }\n[assembly()]\nclass A10 { }",
            "using System;\nclass moduleAttribute : Attribute { }\n[module, Obsolete]\nclass A11 { }",
        ];

        foreach (var fixture in fixtures)
        {
            var lines = fixture.Split('\n');
            var expected = RoslynDeclarations(lines);
            Assert.NotNull(expected);

            var actual = DeclarationIndex.Build(lines).Declarations;
            Assert.Equal(
                expected.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"),
                actual.Select(d => $"{d.Kind} {d.Name} trivia={d.TriviaStartLine} sig={d.SignatureStartLine}"));
        }
    }

    /// <summary>
    /// The corpus contains no <c>delegate</c> type and no destructor, so both classifications are
    /// gated here or not at all: with these fixtures absent, <c>DeclaresADelegate</c> and the
    /// <c>~</c> branch of <c>Classify</c> can each be deleted outright with the suite still green.
    /// The close negatives matter as much as the positives — a function pointer spells
    /// <c>delegate</c> too, and so does an anonymous method in a field initializer, and neither
    /// declares a type. The fixture compiles.
    /// </summary>
    [Fact]
    public void DelegatesFunctionPointersAndDestructors_AreClassifiedApart()
    {
        var index = DeclarationIndex.Build("""
            using System;

            namespace N;

            public delegate int Handler(int x, int y);

            public unsafe class C
            {
                public delegate int Nested(int x);
                public void M(delegate*<int, int> p) { }
                public Action<int> a = delegate (int x) { }, b = null;
                public Action nop = delegate { };
                ~C() { }
            }
            """);

        Assert.Equal(
            [
                (DeclarationKind.Namespace, "N"),
                (DeclarationKind.Delegate, "Handler"),
                (DeclarationKind.Class, "C"),
                (DeclarationKind.Delegate, "Nested"),
                (DeclarationKind.Method, "M"),
                (DeclarationKind.Field, "a"),
                (DeclarationKind.Field, "b"),
                (DeclarationKind.Field, "nop"),
                (DeclarationKind.Destructor, "~C"),
            ],
            index.Declarations.Select(d => (d.Kind, d.Name)));
    }

    /// <summary>
    /// An <c>extern alias</c> is not a declaration, and the corpus contains none. This gates the
    /// behavior, not the branch: mutating <c>Classify</c>'s <c>extern alias</c> skip leaves the
    /// suite green, because <c>Allowed</c> independently rejects the field the skip prevents — a
    /// file cannot put an <c>extern alias</c> inside a type. The construct is parse-valid on its
    /// own; resolving <c>LibA</c> would need an aliased reference, which nothing here consults.
    /// </summary>
    [Fact]
    public void AnExternAlias_IsNotADeclaration()
    {
        var index = DeclarationIndex.Build("""
            extern alias LibA;
            class C { }
            """);

        Assert.Equal([(DeclarationKind.Class, "C")], index.Declarations.Select(d => (d.Kind, d.Name)));
    }

    /// <summary>
    /// A local function is a declaration Roslyn recognizes and the index deliberately does not: it
    /// is not a member, and reporting one would let a body-line lookup return something that has no
    /// metadata counterpart. Nothing here recognizes a local function — the enclosing scope of a
    /// method body simply is not a type.
    /// </summary>
    [Fact]
    public void ALocalFunctionAndALambdaAreNotDeclarations()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
                public int M()
                {
                    int Helper(int x) => x + 1;
                    System.Func<int, int> f = y => y;
                    return Helper(1) + f(2);
                }
            }
            """);

        Assert.Equal(
            ["M"],
            index.Declarations.Where(s => s.Kind is DeclarationKind.Method or DeclarationKind.Field).Select(s => s.Name));
    }

    /// <summary>
    /// The scan cannot decide which branch of a conditional compiles, so a span it cannot vouch for
    /// must report unknown rather than a guess.
    /// </summary>
    [Fact]
    public void ASpanTheScanCannotVouchFor_ReportsUnknown()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            public class C
            {
            #if FEATURE
                public void M() {
            #else
                public void M() {
            #endif
                }
            }
            """);

        Assert.Contains(index.Declarations, s => !s.SpanKnown);
    }
}
