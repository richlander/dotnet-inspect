using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #3371 follow-up: the always-on brace-body width wrapper that breaks long object
// initializers Allman-style (#3377) also covers record `with` expressions and
// anonymous objects — the same head + one-entry-per-line shape, no trailing
// comma, so breaking stays whitespace-only and token-identical to the inline form.
public sealed class WithAndAnonymousWrappingTests
{
    static readonly TypeRef Holder = TypeRef.Definition("synthetic", "", "Holder");
    static readonly TypeRef Record = TypeRef.Definition("synthetic", "", "MeasuredRecord");
    static readonly TypeRef Anon = TypeRef.Definition("synthetic", "", "<>f__AnonymousType0");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    static string Render(IrExpression value, TypeRef returnType, PrinterOptions? options = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Holder, signature, [], container);
        return CSharpPrinter.Print(function, options ?? PrinterOptions.Default).Output!;
    }

    static WithExpression With(params string[] members)
    {
        var receiver = new LoadArgument(0, "original", Record);
        var entries = members.Select(m => new InitializerEntry(m, [new LoadArgument(1, "value_" + m, Int32)]));
        return new WithExpression(receiver, entries);
    }

    static WithExpression LongWith()
        => With("FirstMeasuredProperty", "SecondMeasuredProperty", "ThirdMeasuredProperty", "FourthMeasuredProperty");

    static AnonymousObject Anonymous(params string[] names)
    {
        var values = names.Select(n => (IrExpression)new LoadArgument(1, "value_" + n, Int32));
        return new AnonymousObject(Anon, [.. names], values);
    }

    static AnonymousObject LongAnonymous()
        => Anonymous("FirstMeasuredProperty", "SecondMeasuredProperty", "ThirdMeasuredProperty", "FourthMeasuredProperty");

    [Fact]
    public void LongWith_BreaksAllmanOnePerLine()
    {
        string body = Render(LongWith(), Record);

        Assert.Contains("original with\n", body);
        Assert.Contains("\n{\n", body);
        Assert.Contains("    FirstMeasuredProperty = value_FirstMeasuredProperty,\n", body);
        Assert.Contains("    FourthMeasuredProperty = value_FourthMeasuredProperty\n", body);
        Assert.DoesNotContain("value_FourthMeasuredProperty,", body);
        Assert.Contains("\n};", body);
        Assert.DoesNotContain("with { FirstMeasuredProperty", body);
    }

    [Fact]
    public void ShortWith_StaysInline()
    {
        string body = Render(With("X", "Y"), Record);

        Assert.Contains("original with { X = value_X, Y = value_Y };", body);
        Assert.DoesNotContain("\n{\n", body);
    }

    [Fact]
    public void LongWith_DisableOneLinerWrapping_StaysInline()
    {
        string body = Render(LongWith(), Record, PrinterOptions.Default with { DisableOneLinerWrapping = true });

        Assert.DoesNotContain("\n{\n", body);
        Assert.Contains("original with { FirstMeasuredProperty = value_FirstMeasuredProperty,",
            string.Concat(body.Split('\n').Select(line => line.Trim())).Replace("  ", " "));
    }

    [Fact]
    public void LongAnonymous_BreaksAllmanOnePerLine()
    {
        string body = Render(LongAnonymous(), Anon);

        Assert.Contains("new\n", body);
        Assert.Contains("\n{\n", body);
        Assert.Contains("    FirstMeasuredProperty = value_FirstMeasuredProperty,\n", body);
        Assert.Contains("    FourthMeasuredProperty = value_FourthMeasuredProperty\n", body);
        Assert.DoesNotContain("value_FourthMeasuredProperty,", body);
        Assert.Contains("\n};", body);
        Assert.DoesNotContain("new { FirstMeasuredProperty", body);
    }

    [Fact]
    public void ShortAnonymous_StaysInline()
    {
        string body = Render(Anonymous("X", "Y"), Anon);

        Assert.Contains("new { X = value_X, Y = value_Y };", body);
        Assert.DoesNotContain("\n{\n", body);
    }

    // Both broken forms must be pure whitespace variants of their inline forms.
    [Fact]
    public void BrokenForms_AreWhitespaceVariantsOfInline()
    {
        static string Collapse(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

        string brokenWith = Render(LongWith(), Record);
        string inlineWith = Render(LongWith(), Record, PrinterOptions.Default with { DisableOneLinerWrapping = true });
        Assert.Equal(Collapse(inlineWith), Collapse(brokenWith));

        string brokenAnon = Render(LongAnonymous(), Anon);
        string inlineAnon = Render(LongAnonymous(), Anon, PrinterOptions.Default with { DisableOneLinerWrapping = true });
        Assert.Equal(Collapse(inlineAnon), Collapse(brokenAnon));
    }
}

// Real compiler-produced witnesses (imported from compiled fixtures, raised, then
// printed) confirming the brace-body wrapper folds a wide record `with` expression
// and a wide anonymous object end-to-end, not just synthetic IR.
public sealed class WithAndAnonymousWrappingFixtureTests
{
    static string Print(Type sampleType, string methodName)
    {
        using var source = MetadataSource.Open(sampleType.Assembly.Location);
        var function = IrImporter.Import(source, sampleType.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void WideWithExpression_FoldsMultiLine()
    {
        string output = Print(typeof(BraceBodyWrappingSamples), nameof(BraceBodyWrappingSamples.WidenMeasuredRecord));

        // Raised to a multi-entry `with`, then broken Allman one-per-line.
        Assert.Contains(" with\n", output);
        Assert.Contains("    FirstMeasuredValue = ", output);
        Assert.Contains("    FourthMeasuredValue = ", output);
        Assert.DoesNotContain("with { FirstMeasuredValue", output);
        // No trailing comma before the closing brace.
        Assert.DoesNotContain(",\n};", output.Replace("\r", ""));
    }

    [Fact]
    public void WideAnonymousObject_FoldsMultiLine()
    {
        string output = Print(typeof(BraceBodyWrappingSamples), nameof(BraceBodyWrappingSamples.ProjectMeasuredValues));

        Assert.Contains("new\n", output);
        Assert.Contains("    FirstMeasuredProjection = ", output);
        Assert.Contains("    FourthMeasuredProjection = ", output);
        Assert.DoesNotContain("new { FirstMeasuredProjection", output);
    }
}
