using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// An equality test between a same-width signed/unsigned integer pair — `ceq`/`bne.un`
// on a (ulong, long) or (nuint, nint) — has no C# common type (CS0034), yet the IL
// compares the raw bits regardless of sign. csc emits this shape from its own
// lowerings (e.g. ulong.CreateTruncating(x) != (long)i in Enum.AreSequentialFromZero),
// not from directly spellable source, so these are constructed at the IR level. The
// printer must reconcile the operands to one C# type so the rendered C# binds:
// equality reinterprets the signed operand as unsigned (a same-width no-op cast),
// while a signed ordering (`clt`/`cgt`) reinterprets the unsigned operand as signed
// to preserve the signed comparison (#1476).
public class MixedSignComparisonTests
{
    static readonly TypeRef UInt = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ULong = TypeRef.CoreLib("System", "UInt64");
    static readonly TypeRef Long = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    static string Render(ComparisonKind kind, TypeRef leftType, TypeRef rightType)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Comparison(kind, isUnsigned: false,
            new LoadArgument(0, "a", leftType),
            new LoadArgument(1, "b", rightType))));
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(Bool,
                [new Parameter("a", leftType), new Parameter("b", rightType)],
                HasThis: false, GenericParameterCount: 0),
            [], body);
        return CSharpPrinter.Print(function).Output!;
    }

    static string RenderComparison(ComparisonKind kind, bool isUnsigned, IrExpression left, IrExpression right)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Comparison(kind, isUnsigned, left, right)));
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(Bool,
                [new Parameter("i", left.ResultType ?? UInt)],
                HasThis: false, GenericParameterCount: 0),
            [], body);
        return CSharpPrinter.Print(function).Output!;
    }

    static string RenderImported(string methodName)
    {
        using var source = MetadataSource.Open(typeof(UnsignedComparisonSpecimens).Assembly.Location);
        var function = IrImporter.Import(source, typeof(UnsignedComparisonSpecimens).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void EqualULongLong_ReinterpretsSignedOperandAsUnsigned()
    {
        // `ulong == long` is CS0034; the signed operand reinterprets to unsigned.
        Assert.Contains("a == (ulong)b", Render(ComparisonKind.Equal, ULong, Long));
    }

    [Fact]
    public void NotEqualLongULong_ReinterpretsSignedOperandAsUnsigned()
    {
        // Same reconciliation when the signed operand is on the left.
        Assert.Contains("(ulong)a != b", Render(ComparisonKind.NotEqual, Long, ULong));
    }

    [Fact]
    public void EqualULongULong_LeavesSameTypePairUntouched()
    {
        // Positive canary: a same-type pair needs no cast and must not gain one.
        var output = Render(ComparisonKind.Equal, ULong, ULong);
        Assert.Contains("a == b", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void LessThanULongLong_ReinterpretsUnsignedOperandAsSigned()
    {
        // A signed ordering (`clt`) between a same-width signed/unsigned pair is
        // CS0034. Unlike equality, the unsigned operand reinterprets to SIGNED so
        // the signed comparison the IL performs is preserved.
        var output = Render(ComparisonKind.LessThan, ULong, Long);
        Assert.Contains("(long)a < b", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void GreaterThanLongULong_ReinterpretsUnsignedOperandAsSigned()
    {
        // Same reconciliation when the unsigned operand is on the right.
        var output = Render(ComparisonKind.GreaterThan, Long, ULong);
        Assert.Contains("a > (long)b", output);
        Assert.DoesNotContain("(ulong)", output);
    }

    [Fact]
    public void LessThanLongLong_LeavesSameTypeOrderingUntouched()
    {
        // Positive canary: a same-type ordering pair needs no cast and must not gain one.
        var output = Render(ComparisonKind.LessThan, Long, Long);
        Assert.Contains("a < b", output);
        Assert.DoesNotContain("(long)", output);
    }

    [Fact]
    public void SignedOrdering_NestedUnsignedRenderingOperand_CastsWholeSubtree()
    {
        // A nested mixed-sign arithmetic operand (`long a + ulong b`) renders
        // unsigned (`(ulong)a + b`) though its IR ResultType is signed, so the
        // signed-ordering reconcile must cast the whole rendered subtree —
        // otherwise `ulong < long` stays CS0034.
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var sum = new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false,
            new LoadArgument(0, "a", Long), new LoadArgument(1, "b", ULong));
        var block = new Block(0);
        block.Add(new Return(new Comparison(ComparisonKind.LessThan, isUnsigned: false,
            sum, new LoadArgument(2, "c", Long))));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction("M", owner,
            new MethodSignature(Bool,
                [new Parameter("a", Long), new Parameter("b", ULong), new Parameter("c", Long)],
                HasThis: false, GenericParameterCount: 0),
            [], body);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(long)((ulong)a + b) < c", output);
    }

    [Fact]
    public void UnsignedOrdering_UIntOutOfRangeConstant_WrapsCastInUnchecked()
    {
        var output = RenderComparison(ComparisonKind.GreaterThanOrEqual, isUnsigned: true,
            new LoadArgument(0, "i", UInt),
            new Constant(-294967296, Int));

        Assert.Contains("i >= unchecked((uint)-294967296)", output);
        AssertBodyCompiles("public static bool M(uint i)", output);
    }

    [Fact]
    public void UnsignedOrdering_ULongOutOfRangeConstant_WrapsCastInUnchecked()
    {
        var output = RenderComparison(ComparisonKind.GreaterThanOrEqual, isUnsigned: true,
            new LoadArgument(0, "i", ULong),
            new Constant(-8446744073709551616L, Long));

        Assert.Contains("i >= unchecked((ulong)-8446744073709551616)", output);
        AssertBodyCompiles("public static bool M(ulong i)", output);
    }

    [Fact]
    public void UnsignedOrdering_ULongNegativeLongConstant_WrapsCastInUnchecked()
    {
        var output = RenderComparison(ComparisonKind.LessThan, isUnsigned: true,
            new LoadArgument(0, "i", ULong),
            new Constant(-16L, Long));

        Assert.Contains("i < unchecked((ulong)-16)", output);
        AssertBodyCompiles("public static bool M(ulong i)", output);
    }

    [Fact]
    public void UnsignedEquality_OutOfRangeConstant_StillWrapsCastInUnchecked()
    {
        var output = RenderComparison(ComparisonKind.Equal, isUnsigned: false,
            new LoadArgument(0, "i", ULong),
            new Constant(-1L, Long));

        Assert.Contains("i == unchecked((ulong)-1)", output);
        AssertBodyCompiles("public static bool M(ulong i)", output);
    }

    [Fact]
    public void ImportedUnsignedOrderingOutOfRangeConstants_RenderUncheckedCasts()
    {
        AssertContainsAndCompiles("public static bool M(ulong i)",
            "i >= unchecked((ulong)-8446744073709551616)",
            RenderImported(nameof(UnsignedComparisonSpecimens.GeUlongHighBit)));
        AssertContainsAndCompiles("public static bool M(uint i)",
            "i >= unchecked((uint)-294967296)",
            RenderImported(nameof(UnsignedComparisonSpecimens.UintGe)));
        AssertContainsAndCompiles("public static bool M(ulong i)",
            "i < unchecked((ulong)((long)-16))",
            RenderImported(nameof(UnsignedComparisonSpecimens.UlongLt)));
        AssertContainsAndCompiles("public static bool M(ulong i)",
            "i == unchecked((ulong)((long)-1))",
            RenderImported(nameof(UnsignedComparisonSpecimens.UlongEq)));
    }

    static void AssertContainsAndCompiles(string methodHeader, string expected, string body)
    {
        Assert.Contains(expected, body);
        AssertBodyCompiles(methodHeader, body);
    }

    static void AssertBodyCompiles(string methodHeader, string body)
    {
        var errors = Recompile(methodHeader, body)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body)
    {
        string source = $$"""
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            references.Add(MetadataReference.CreateFromFile(path));
        }
        return references.ToImmutable();
    }
}

public static class UnsignedComparisonSpecimens
{
    public static bool GeUlongHighBit(ulong i) => i >= 10000000000000000000UL;
    public static bool UintGe(uint i) => i >= 4000000000U;
    public static bool UlongLt(ulong i) => i < 0xFFFFFFFFFFFFFFF0UL;
    public static bool UlongEq(ulong i) => i == ulong.MaxValue;
}
