using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Slice 1 of docs/design/value-typed-emission.md: the coercion decision lives in
// one place (Coerce / TryCoerceEnumOperand / EnumConstantText), not per render
// branch. These tests pin the two behavior deltas of the consolidation:
//
// 1. Guard drift fixed: the binary/comparison enum-operand sites used
//    TypeFamilies.IsInteger, which admits bool (I4 stack family), so an enum
//    met by a bool operand rendered `(E)(x == y)` — CS0030. The shared operand
//    coercion composes the truthiness spelling instead: `(E)(x == y ? 1 : 0)`.
//    csc never emits this shape; it is verifiable IL (bool and enum share I4),
//    so the fixtures are synthetic IR.
// 2. Member naming is part of the one name-or-cast rule: an un-retyped integer
//    constant at a known-enum sink renders the member name when one matches,
//    where the sink previously open-coded the cast.
public class CoerceChokePointTests
{
    [Fact]
    public void EnumComparedToBool_ComposesTruthinessWithEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "x", intType),
            new LoadArgument(2, "y", intType));
        var outer = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            inner);
        string body = RenderReturn(
            outer,
            TypeRef.CoreLib("System", "Boolean"),
            [new Parameter("flags", enumType), new Parameter("x", intType), new Parameter("y", intType)],
            enumType);

        Assert.Contains("(Tiny)(x == y ? 1 : 0)", body);
        Assert.DoesNotContain("(Tiny)(x == y)", body);
        AssertCompiles("public static bool M(Tiny flags, int x, int y)", body, "public enum Tiny { }");
    }

    [Fact]
    public void EnumBitwiseWithBool_ComposesTruthinessWithEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "x", intType),
            new LoadArgument(2, "y", intType));
        var and = new Binary(
            BinaryKind.And,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            inner);
        string body = RenderReturn(
            and,
            enumType,
            [new Parameter("flags", enumType), new Parameter("x", intType), new Parameter("y", intType)],
            enumType);

        Assert.Contains("flags & (Tiny)(x == y ? 1 : 0)", body);
        Assert.DoesNotContain("& (Tiny)(x == y)", body);
        AssertCompiles("public static Tiny M(Tiny flags, int x, int y)", body, "public enum Tiny { }");
    }

    [Fact]
    public void BoolConditionalArm_AtEnumMergedType_ComposesTruthinessWithEnumCast()
    {
        // A conditional whose join is enum-typed but whose true arm is a raw
        // comparison result: the bool arm cannot render bare (CS0029) or as a
        // direct enum cast (CS0030); it composes.
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(1, "x", intType),
                new LoadArgument(2, "y", intType)),
            new LoadArgument(3, "e", enumType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [
                new Parameter("c", boolType),
                new Parameter("x", intType),
                new Parameter("y", intType),
                new Parameter("e", enumType),
            ],
            enumType);

        Assert.Contains("(Tiny)(x == y ? 1 : 0)", body);
        AssertCompiles("public static Tiny M(bool c, int x, int y, Tiny e)", body, "public enum Tiny { }");
    }

    [Fact]
    public void UnnamedHighBitConstantArm_KeepsUncheckedCast()
    {
        // The cast half of the name-or-cast rule at the #2076 conditional-arm
        // shape: a negative payload on a uint-backed enum with NO matching member
        // must still take the overflow-aware unchecked cast (CS0221 otherwise) —
        // naming only fires on an exact member match (the named twin lives in
        // EnumCastPrinterTests.EnumConditional_SameAssemblyUnsignedEnum_NamesHighBitMember).
        var enumType = TypeRef.Definition("synthetic", "", "CfgFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(-2147483647, intType),
            new LoadArgument(1, "e", enumType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [new Parameter("c", boolType), new Parameter("e", enumType)],
            enumType,
            underlying: TypeRef.CoreLib("System", "UInt32"),
            // Top keys by the signed int payload (-2147483648), mirroring how the
            // real member map resolved the #2076 fixture's `ldc.i4` constant.
            members: new Dictionary<long, string> { [0] = "None", [-2147483648L] = "Top" });

        Assert.Contains("unchecked((CfgFlags)(-2147483647))", body);
        Assert.DoesNotContain("CfgFlags.Top", body);
        AssertCompiles(
            "public static CfgFlags M(bool c, CfgFlags e)",
            body,
            "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    [Fact]
    public void UnretypedNamedConstant_AtKnownEnumSink_RendersMemberName()
    {
        // A long-payload constant TypedConstantsPass (int-only) never retyped:
        // the sink coercion still spells the member name, not `(LEnum)2`.
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        string body = RenderReturn(
            new Constant(2L, longType),
            enumType,
            [],
            enumType,
            underlying: longType,
            members: new Dictionary<long, string> { [2] = "High" });

        Assert.Contains("return LEnum.High;", body);
        Assert.DoesNotContain("(LEnum)2", body);
        AssertCompiles("public static LEnum M()", body, "public enum LEnum : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void UnretypedUnnamedConstant_AtKnownEnumSink_KeepsOverflowAwareCast()
    {
        // The cast half of the name-or-cast rule is unchanged: no matching
        // member falls back to the overflow-aware enum cast.
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        string body = RenderReturn(
            new Constant(7L, longType),
            enumType,
            [],
            enumType,
            underlying: longType,
            members: new Dictionary<long, string> { [2] = "High" });

        Assert.Contains("return (LEnum)7;", body);
        AssertCompiles("public static LEnum M()", body, "public enum LEnum : long { Low = 0, High = 2 }");
    }

    // #2302 canaries: the join-arm rule's third direction — a primitive arm at
    // a same-family primitive MergedType it cannot reach implicitly. The
    // pre-F1 fold shipped these bare (CS0029/CS0266); the latent class needs
    // synthetic fixtures because F1 cleared the live corpus population.
    [Fact]
    public void NegativeConstantArm_AtUnsignedMergedType_ReintepretsUnchecked()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            new Constant(-1, intType))
        {
            MergedType = uintType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("u", uintType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? u : unchecked((uint)(-1))", body);
        AssertCompiles("public static uint M(bool c, uint u)", body);
    }

    [Fact]
    public void NonConstantIntArm_AtUnsignedMergedType_TakesReinterpretCast()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            new LoadArgument(2, "x", intType))
        {
            MergedType = uintType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("u", uintType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? u : (uint)x", body);
        AssertCompiles("public static uint M(bool c, uint u, int x)", body);
    }

    [Fact]
    public void InRangeConstantArm_AtUnsignedMergedType_StaysBare()
    {
        // C#'s implicit constant conversion covers in-range constants — the
        // masked case the F1 review exposed. It must stay bare (no cast churn).
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            new Constant(0, intType))
        {
            MergedType = uintType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("u", uintType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? u : 0", body);
        Assert.DoesNotContain("(uint)0", body);
        AssertCompiles("public static uint M(bool c, uint u)", body);
    }

    [Fact]
    public void ImplicitlyWideningArm_AtLongMergedType_StaysBare()
    {
        // int -> long is an implicit conversion; NeedsNumericCast gates the
        // third direction so implicitly-reachable arms never gain cast churn.
        var longType = TypeRef.CoreLib("System", "Int64");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "l", longType),
            new LoadArgument(2, "x", intType))
        {
            MergedType = longType,
        };
        string body = RenderReturn(
            conditional,
            longType,
            [new Parameter("c", boolType), new Parameter("l", longType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? l : x", body);
        Assert.DoesNotContain("(long)x", body);
        AssertCompiles("public static long M(bool c, long l, int x)", body);
    }

    // work-2302 review (both reviewers, blocking): the unchecked(...) wrapper
    // must not absorb NESTED checked operations — a checked add renders bare
    // under an ambient checked region, so wrapping its pre-rendered text
    // silenced its overflow check (`unchecked((uint)(a + b))` where the IL
    // demands `unchecked((uint)checked(a + b))`). CheckedSafeCast now renders
    // its operand with the context cleared so nested checked nodes self-wrap.
    [Fact]
    public void CheckedAddUnderCoerce_InCheckedRegion_KeepsItsOwnCheckedWrapper()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var checkedInner = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(1, "a", intType),
            new LoadArgument(2, "b", intType));
        var outer = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "u", uintType),
            new Coerce(uintType, checkedInner));
        string body = RenderReturn(
            outer,
            uintType,
            [new Parameter("u", uintType), new Parameter("a", intType), new Parameter("b", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)checked(a + b))", body);
        AssertCompiles("public static uint M(uint u, int a, int b)", body);
    }

    [Fact]
    public void PlainAddUnderCoerce_InCheckedRegion_StaysInsideTheUncheckedWrapper()
    {
        // The dual: a PLAIN add under the reinterpret needs no checked(...) —
        // the cleared context renders it bare and the unchecked wrapper is its
        // faithful home (no double-wrap noise).
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var plainInner = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(1, "a", intType),
            new LoadArgument(2, "b", intType));
        var outer = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "u", uintType),
            new Coerce(uintType, plainInner));
        string body = RenderReturn(
            outer,
            uintType,
            [new Parameter("u", uintType), new Parameter("a", intType), new Parameter("b", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)(a + b))", body);
        Assert.DoesNotContain("checked(a + b)", body.Replace("unchecked((uint)(a + b))", ""));
        AssertCompiles("public static uint M(uint u, int a, int b)", body);
    }

    [Fact]
    public void CheckedAddArm_AtUnsignedMergedType_InCheckedRegion_KeepsItsCheckedWrapper()
    {
        // The join-arm form of the same finding: the third-direction arm cast
        // must protect checked arithmetic inside the arm.
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var checkedArm = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(2, "a", intType),
            new LoadArgument(3, "b", intType));
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            checkedArm)
        {
            MergedType = uintType,
        };
        var outer = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new Constant(0, uintType),
            conditional);
        string body = RenderReturn(
            outer,
            uintType,
            [
                new Parameter("c", boolType),
                new Parameter("u", uintType),
                new Parameter("a", intType),
                new Parameter("b", intType),
            ],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)checked(a + b))", body);
        AssertCompiles("public static uint M(bool c, uint u, int a, int b)", body);
    }

    // The CI-caught dual pair: an enum->underlying cast in a checked region
    // wraps only when the checked conversion can actually throw. Identity
    // (int-backed -> int) stays bare — EnumUnderlyingCastTests pins that side;
    // cross-signedness (uint-backed -> int) must wrap.
    [Fact]
    public void UnsignedBackedEnumCast_ToInt_InCheckedRegion_WrapsUnchecked()
    {
        var enumType = TypeRef.Definition("synthetic", "", "UFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new Coerce(intType, new LoadArgument(0, "f", enumType)),
            new LoadArgument(1, "x", intType));
        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("f", enumType), new Parameter("x", intType)],
            enumType,
            underlying: uintType);

        Assert.Contains("unchecked((int)f)", body);
        AssertCompiles("public static int M(UFlags f, int x)", body, "public enum UFlags : uint { }");
    }

    // #2301: a cross-signedness reinterpret cast rendered inside a lexical
    // checked region must wrap in unchecked(...) — bare `(uint)x` there
    // recompiles to a conv.ovf.u4 the IL never had (and throws on negative x).
    [Fact]
    public void CrossSignednessCoerce_InsideCheckedBinary_WrapsUnchecked()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "u", uintType),
            new Coerce(uintType, new LoadArgument(1, "x", intType)));
        string body = RenderReturn(
            checkedAdd,
            uintType,
            [new Parameter("u", uintType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)x)", body);
        AssertCompiles("public static uint M(uint u, int x)", body);
    }

    // #2306: an int-MergedType Conditional at a uint sink — the live
    // SpinThenBlockingWait shape (`uint V_1 = c ? 0 : Environment.TickCount;`
    // shipped CS0266). The sink target distributes into the arms: the in-range
    // constant stays bare, the int expression takes the reinterpret cast.
    [Fact]
    public void IntConditional_AtUnsignedSink_DistributesTargetIntoArms()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(0, intType),
            new LoadArgument(1, "x", intType))
        {
            MergedType = intType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? 0 : (uint)x", body);
        AssertCompiles("public static uint M(bool c, int x)", body);
    }

    // A target-typed-valid conditional must NOT distribute: C# 9 converts
    // each arm independently (`sbyte V = c ? 127 : (sbyte)x;` is legal), so
    // adding casts is churn — and an arm carrying the pipeline's own Coerce
    // wrapper is judged by its OPERAND (the corpus audit's
    // `(sbyte)((sbyte)value)` double came from re-casting a stale
    // Coerce{int, Convert sbyte} at an sbyte sink).
    [Fact]
    public void TargetTypedValidConditional_WithStaleCoerceArm_DeclinesDistribution()
    {
        var sbyteType = TypeRef.CoreLib("System", "SByte");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(127, intType),
            new Coerce(intType, new Pipeline.Convert(sbyteType, isChecked: false, isUnsigned: false, new LoadArgument(1, "x", intType))))
        {
            MergedType = intType,
        };
        string body = RenderReturn(
            conditional,
            sbyteType,
            [new Parameter("c", boolType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? 127 : ((sbyte)x)", body);
        Assert.DoesNotContain("(sbyte)((sbyte)", body);
        AssertCompiles("public static sbyte M(bool c, int x)", body);
    }

    // The cross-family refusal: a long-armed conditional at a uint sink must
    // NOT distribute (the cast would be the place that discovers a wrong
    // join); it stays on the merge-node bail.
    [Fact]
    public void LongConditional_AtUnsignedSink_DeclinesDistribution()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var longType = TypeRef.CoreLib("System", "Int64");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "a", longType),
            new LoadArgument(2, "b", longType))
        {
            MergedType = longType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("a", longType), new Parameter("b", longType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.DoesNotContain("(uint)a", body);
        Assert.DoesNotContain("(uint)(c", body);
    }

    static string RenderReturn(
        IrExpression value,
        TypeRef returnType,
        IReadOnlyList<Parameter> parameters,
        TypeRef enumType,
        TypeRef? underlying = null,
        IReadOnlyDictionary<long, string>? members = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = underlying is null
                ? new Dictionary<TypeRef, TypeRef>()
                : new Dictionary<TypeRef, TypeRef> { [enumType] = underlying },
            EnumMembers = members is null
                ? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()
                : new Dictionary<TypeRef, IReadOnlyDictionary<long, string>> { [enumType] = members },
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static void AssertCompiles(string header, string body, string extraDeclarations = "")
    {
        var errors = Recompile(header, body, extraDeclarations)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body, string extraDeclarations)
    {
        string source = $$"""
            using System;
            {{extraDeclarations}}
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
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return references.ToImmutable();
    }
}
