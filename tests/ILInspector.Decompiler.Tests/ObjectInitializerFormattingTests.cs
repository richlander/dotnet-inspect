using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #3371: once a raised object initializer is long, CSharpPrinter breaks it
// Allman-style — the `new T` head on the first line, `{`/`}` on their own lines,
// and one `Member = value` entry per line under a continuation indent; an
// initializer that still fits stays inline. Breaking is whitespace-only (no
// trailing comma, same head and entry texts), so the broken body still compiles
// and carries the same tokens as the inline form.
public sealed class ObjectInitializerFormattingTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Options = TypeRef.Definition("synthetic", "", "WideOptions");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static readonly MethodRef Ctor = new(Options, ".ctor", Void, [], HasThis: true);

    static ObjectInitializerExpression Initializer(params string[] members)
    {
        var creation = new NewObject(Ctor, []);
        var entries = members.Select(m => new InitializerEntry(m, [new LoadArgument(0, ArgName(m), Int32)]));
        return new ObjectInitializerExpression(creation, isCollection: false, entries);
    }

    static string ArgName(string member) => "value_" + member;

    // Render `return <initializer>;` as the whole method body and reduce it to
    // the raw statement/expression-body text.
    static string Render(ObjectInitializerExpression init, PrinterOptions? options = null)
    {
        var block = new Block(0);
        block.Add(new Return(init));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Options, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Holder, signature, [], container);
        return CSharpPrinter.Print(function, options ?? PrinterOptions.Default).Output!;
    }

    // Four PascalCase members whose flat `new WideOptions { ... }` form runs past
    // the 120-column wrap width.
    static ObjectInitializerExpression LongInitializer()
        => Initializer(
            "FirstMeasuredProperty",
            "SecondMeasuredProperty",
            "ThirdMeasuredProperty",
            "FourthMeasuredProperty");

    [Fact]
    public void LongInitializer_BreaksAllmanOnePerLine()
    {
        string body = Render(LongInitializer());

        // Head, then `{`/`}` on their own lines, one entry per line, no trailing comma.
        Assert.Contains("new WideOptions\n", body);
        Assert.Contains("\n{\n", body);
        Assert.Contains("    FirstMeasuredProperty = value_FirstMeasuredProperty,\n", body);
        Assert.Contains("    FourthMeasuredProperty = value_FourthMeasuredProperty\n", body);
        Assert.DoesNotContain("value_FourthMeasuredProperty,", body);
        Assert.Contains("\n};", body);
        // No flat `{ ... }` body survives.
        Assert.DoesNotContain("{ FirstMeasuredProperty", body);
    }

    [Fact]
    public void ShortInitializer_StaysInline()
    {
        string body = Render(Initializer("X", "Y"));

        Assert.Contains("new WideOptions { X = value_X, Y = value_Y };", body);
        Assert.DoesNotContain("\n{\n", body);
    }

    [Fact]
    public void SingleEntry_StaysInline_EvenWhenLong()
    {
        string body = Render(Initializer("AnExtremelyLongSinglePropertyNameThatOnItsOwnExceedsTheOneHundredAndTwentyColumnWrapWidthEasilyYes"));

        Assert.DoesNotContain("\n{\n", body);
    }

    // Issue #3185 parity: the general one-liner opt-out suppresses the always-on
    // wrapper, so an over-width initializer stays on one line.
    [Fact]
    public void LongInitializer_DisableOneLinerWrapping_StaysInline()
    {
        string body = Render(LongInitializer(), PrinterOptions.Default with { DisableOneLinerWrapping = true });

        Assert.Contains(
            "new WideOptions { FirstMeasuredProperty = value_FirstMeasuredProperty,"
                + " SecondMeasuredProperty = value_SecondMeasuredProperty,"
                + " ThirdMeasuredProperty = value_ThirdMeasuredProperty,"
                + " FourthMeasuredProperty = value_FourthMeasuredProperty };",
            string.Concat(body.Split('\n').Select(line => line.Trim())));
        Assert.DoesNotContain("\n{\n", body);
    }

    // The broken form must be a pure whitespace variant of the inline form: the
    // same tokens, in the same order, differing only in whitespace.
    [Fact]
    public void BrokenForm_IsWhitespaceVariantOfInline()
    {
        string broken = Render(LongInitializer());
        string inline = Render(LongInitializer(), PrinterOptions.Default with { DisableOneLinerWrapping = true });

        static string Collapse(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

        // Drop the trailing comma the broken form omits before `}` is irrelevant —
        // the inline form has commas only between entries too, so collapsing
        // whitespace yields identical token streams.
        Assert.Equal(Collapse(inline), Collapse(broken));
    }
}
