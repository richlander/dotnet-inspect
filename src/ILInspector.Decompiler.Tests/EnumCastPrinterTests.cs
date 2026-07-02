using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Issues #1766 / #1772: a cross-assembly (framework) enum resolves to
// TypeShape.Unknown, so an integer constant flowing into it renders as a bare
// int — `int->enum` in a conditional arm (CS0266) or `enum |= int` in a bitwise
// compound (CS0019) — while the method is still graded Full. The printer must
// cast the integer to the enum structurally.
public class EnumCastPrinterTests
{
    [Fact]
    public void EnumConstantConditionalArms_IntoCrossAssemblyEnum_CastsEachArm()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumConditional));

        Assert.Contains("(StringComparison)4", body);
        Assert.Contains("(StringComparison)5", body);
        Assert.DoesNotContain("? 4 : 5", body);
        AssertCompiles("public static bool M(string name, bool ci)", body);
    }

    [Fact]
    public void BitwiseCompound_IntoCrossAssemblyFlagsEnum_CastsRightOperand()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumFlagsCompound));

        Assert.Contains("|= (AttributeTargets)4", body);
        Assert.Contains("|= (AttributeTargets)8", body);
        Assert.DoesNotContain("|= 4", body);
        Assert.DoesNotContain("|= 8", body);
        AssertCompiles("public static AttributeTargets M(bool a, bool b)", body);
    }

    [Fact]
    public void EnumConditional_MixedConstantAndNonConstantArms_CastsBoth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumConditionalMixedArm));

        Assert.Contains("(StringComparison)4", body);
        Assert.Contains("(StringComparison)raw", body);
        Assert.DoesNotContain(": raw", body);
        AssertCompiles("public static bool M(string name, bool ci, int raw)", body);
    }

    [Fact]
    public void EnumCompound_NegativeConstant_ForcesUncheckedCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumFlagsCompoundNegative));

        Assert.Contains("unchecked((AttributeTargets)(-5))", body);
        AssertCompiles("public static AttributeTargets M(AttributeTargets seed)", body);
    }

    [Fact]
    public void EnumCoalesce_IntoCrossAssemblyEnum_CastsFallback()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumCoalesce));

        Assert.Contains("?? (StringComparison)4", body);
        Assert.DoesNotContain("?? 4", body);
        AssertCompiles("public static StringComparison M(StringComparison? value)", body);
    }

    [Fact]
    public void EnumSwitchExpression_ReturningCrossAssemblyEnum_CastsArms()
    {
        var enumType = TypeRef.CoreLib("System", "StringComparison");
        var intType = TypeRef.CoreLib("System", "Int32");
        string body = RenderSyntheticSwitchExpression(
            enumType,
            new Constant(4, intType),
            new Constant(5, intType));

        Assert.Contains("=> (StringComparison)4", body);
        Assert.Contains("=> (StringComparison)5", body);
        Assert.DoesNotContain("=> 4", body);
        Assert.DoesNotContain("=> 5", body);
        AssertCompiles("public static StringComparison M(int value)", body);
    }

    [Fact]
    public void SameAssemblyEnumCoalesce_KeepsNamedMember()
    {
        string body = RenderFixture(nameof(EnumCastSamples.SameAssemblyEnumCoalesce));

        Assert.Contains("CfgPriority.High", body);
        Assert.DoesNotContain("(CfgPriority)2", body);
        AssertCompiles("public static CfgPriority M(CfgPriority? value)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void SameAssemblyEnumSwitchExpression_KeepsNamedMembers()
    {
        var enumType = TypeRef.Definition("test", "", "CfgPriority");
        string body = RenderSyntheticSwitchExpression(
            enumType,
            new Constant(2, enumType),
            new Constant(3, enumType),
            new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [enumType] = new Dictionary<long, string> { [2] = "High", [3] = "Critical" },
            });

        Assert.Contains("CfgPriority.High", body);
        Assert.Contains("CfgPriority.Critical", body);
        Assert.DoesNotContain("(CfgPriority)2", body);
        Assert.DoesNotContain("(CfgPriority)3", body);
        AssertCompiles("public static CfgPriority M(int value)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void RetypedUnsignedEnumConstant_ForcesUncheckedCast()
    {
        // A same-assembly unsigned-enum constant retyped by TypedConstantsPass with
        // no named member, in comparison / bitwise / coalesce positions.
        const string declaration = "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }";

        string comparison = RenderFixture(nameof(EnumCastSamples.UnsignedEnumConstantComparison));
        Assert.Contains("unchecked((CfgFlags)(-1))", comparison);
        AssertCompiles("public static bool M(CfgFlags f)", comparison, declaration);

        string bitwise = RenderFixture(nameof(EnumCastSamples.UnsignedEnumConstantBitwise));
        Assert.Contains("unchecked((CfgFlags)(-1))", bitwise);
        AssertCompiles("public static CfgFlags M(CfgFlags f)", bitwise, declaration);

        string coalesce = RenderFixture(nameof(EnumCastSamples.UnsignedEnumConstantCoalesce));
        Assert.Contains("unchecked((CfgFlags)(-1))", coalesce);
        AssertCompiles("public static CfgFlags M(CfgFlags? f)", coalesce, declaration);
    }

    [Fact]
    public void EnumConditional_SameAssemblyUnsignedEnum_NamesHighBitMember()
    {
        // #2076: `c ? CfgFlags.Top : e` where CfgFlags : uint. Top (0x80000000u)
        // is emitted as `ldc.i4` int.MinValue, so the conditional slot's importer
        // type is unknown; the fold anchors the enum. The name-or-cast rule
        // (EnumConstantText) resolves the payload to the member — `CfgFlags.Top`,
        // never a bare `-2147483648` (CS0029) or an unchecked cast of the raw
        // literal. The unnamed-value fallback to `unchecked` is pinned by
        // CoerceChokePointTests.UnnamedHighBitConstantArm_KeepsUncheckedCast.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.UnsignedEnumConditionalArm));

        Assert.Contains("CfgFlags.Top", body);
        Assert.DoesNotContain(": -2147483648", body);
        Assert.DoesNotContain("(CfgFlags)(-2147483648)", body);
        AssertCompiles(
            "public static bool M(bool c, CfgFlags e)",
            body,
            "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    [Fact]
    public void KnownEnumPositiveConstantOutOfRange_ForcesUncheckedCast()
    {
        // A same-assembly enum with a known narrow underlying type: an out-of-range
        // constant cast is CS0221 unless wrapped, while an in-range one stays bare.
        string outOfRange = RenderKnownEnumReturnConstant(300, TypeRef.CoreLib("System", "Byte"));
        Assert.Contains("return unchecked((Tiny)300);", outOfRange);
        AssertCompiles("public static Tiny M()", outOfRange, "public enum Tiny : byte { }");

        string inRange = RenderKnownEnumReturnConstant(4, TypeRef.CoreLib("System", "Byte"));
        Assert.Contains("return (Tiny)4;", inRange);
        Assert.DoesNotContain("unchecked", inRange);
        AssertCompiles("public static Tiny M()", inRange, "public enum Tiny : byte { }");
    }

    [Fact]
    public void EnumWithUnresolvedBackingWidth_AssumesIntBacking()
    {
        // An enum classified TypeShape.Enum but whose underlying width is not in the
        // map (e.g. no value__ field) assumes C#'s default `int` backing: an
        // int-range negative constant stays a bare cast (matching ExactMember), and
        // only a genuinely out-of-int value would be wrapped.
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new Constant(-1, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            // EnumUnderlyingTypes intentionally left empty.
        };

        string body = CSharpPrinter.Print(function).Output!.Trim();
        Assert.Contains("(Tiny)(-1)", body);
        Assert.DoesNotContain("unchecked", body);
    }

    [Fact]
    public void UnknownEnumPositiveConstantThatMayOverflow_ForcesUncheckedCast()
    {
        string sbyteBody = RenderUnknownEnumReturnConstant(128);
        string byteBody = RenderUnknownEnumReturnConstant(300);

        Assert.Contains("return unchecked((Tiny)128);", sbyteBody);
        Assert.DoesNotContain("return (Tiny)128;", sbyteBody);
        AssertCompiles("public static Tiny M()", sbyteBody, "public enum Tiny : sbyte { }");

        Assert.Contains("return unchecked((Tiny)300);", byteBody);
        Assert.DoesNotContain("return (Tiny)300;", byteBody);
        AssertCompiles("public static Tiny M()", byteBody, "public enum Tiny : byte { }");
    }

    [Fact]
    public void UnknownEnumSwitchLabelPositiveConstantThatMayOverflow_ForcesUncheckedCast()
    {
        string body = RenderUnknownEnumSwitchLabel(128);

        Assert.Contains("case unchecked((Tiny)128):", body);
        Assert.DoesNotContain("case (Tiny)128:", body);
        AssertCompiles("public static void M(Tiny value)", body, "public enum Tiny : sbyte { }");
    }

    [Fact]
    public void UnknownEnumSwitchExpressionLabelPositiveConstantThatMayOverflow_ForcesUncheckedCast()
    {
        string body = RenderUnknownEnumSwitchExpressionLabel(128);

        Assert.Contains("unchecked((Tiny)128) => 1", body);
        Assert.DoesNotContain("(Tiny)128 => 1", body);
        AssertCompiles("public static int M(Tiny value)", body, "public enum Tiny : sbyte { }");
    }

    [Fact]
    public void IntegerNullableCoalesce_IntoUnknownEnum_CastsWholeCoalesce()
    {
        string body = RenderIntegerNullableCoalesceIntoUnknownEnum();

        Assert.Contains("return (Tiny)(value ?? 4);", body);
        Assert.DoesNotContain("value ?? (Tiny)4", body);
        AssertCompiles("public static Tiny M(int? value)", body, "public enum Tiny { }");
    }

    [Fact]
    public void EnumSwitchLabel_LongConstant_CastsInsteadOfBareLiteral()
    {
        // #2076 (review): a long case label on a long-backed enum switch must cast
        // (`case (LEnum)...:`), not render a bare `case 1311768467463790320:`
        // (CS0266). Member names still win when the value is named.
        const long value = 1311768467463790320L;
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Switch(
            new LoadArgument(0, "value", enumType),
            [
                new SwitchSection(ImmutableArray.Create(new Constant(value, longType)), isDefault: false, SingleReturnContainer()),
                new SwitchSection(ImmutableArray<Constant>.Empty, isDefault: true, SingleReturnContainer()),
            ]));
        body.Add(block);
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("value", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = longType },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();
        Assert.Contains("case (LEnum)1311768467463790320:", output);
        Assert.DoesNotContain("case 1311768467463790320:", output);
    }

    [Fact]
    public void CrossAssemblyEnumArray_CastsElementStore()
    {
        // A cross-assembly enum array element store must cast to the enum, not emit
        // a bare `int` (CS0266) off the `stelem.i4` storage type.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.CrossAssemblyEnumArray));
        Assert.Contains("(StringComparison)4", body);
        Assert.DoesNotContain("= 4;", body);
        AssertCompiles("public static System.StringComparison[] M()", body);
    }

    [Fact]
    public void UnsignedLongEnumMaxConstant_ConvertWrapped_NamesMember()
    {
        // ulong.MaxValue lowers as `ldc.i4.m1; conv.i8`; TypedConstantsPass folds
        // the widening into an enum-typed constant (sign-extended payload -1
        // matches the member map's keying), so the value renders by name — the
        // ideal spelling for leak case #6, replacing
        // `unchecked((CfgULong)((long)(-1)))`. The unnamed-value unchecked
        // fallback stays pinned by the Unknown-enum and CoerceChokePoint tests.
        const string declaration = "public enum CfgULong : ulong { None = 0, All = 18446744073709551615UL }";

        string boxed = RenderRaisedFixture(nameof(EnumCastSamples.ULongEnumBoxedMax));
        Assert.Contains("CfgULong.All", boxed);
        Assert.DoesNotContain("(long)(-1)", boxed);
        AssertCompiles("public static System.Enum M()", boxed, declaration);

        string array = RenderRaisedFixture(nameof(EnumCastSamples.ULongEnumArrayMax));
        Assert.Contains("CfgULong.All", array);
        AssertCompiles("public static CfgULong[] M()", array, declaration);
    }

    [Fact]
    public void LongBackedEnumConstants_InArrayAndBox_CastOrName()
    {
        // Array elements: an unnamed long payload renders as the enum cast, never
        // a bare `long` (CS0266). AssertCompiles is the real validity gate.
        string array = RenderRaisedFixture(nameof(EnumCastSamples.LongEnumArray));
        Assert.Contains("(CfgLongPriority)5000000000", array);
        Assert.DoesNotContain("= 5000000000;", array);
        AssertCompiles(
            "public static CfgLongPriority[] M()",
            array,
            "public enum CfgLongPriority : long { Low = 0, High = 2 }");

        // Box target: the enum value must keep its type (bare long is CS0029 for
        // System.Enum). The small value arrives as `Convert(long, ...)`;
        // TypedConstantsPass folds it, so the named member renders.
        string boxed = RenderRaisedFixture(nameof(EnumCastSamples.LongEnumBoxed));
        Assert.Contains("CfgLongPriority.High", boxed);
        Assert.DoesNotContain("return (long)", boxed);
        AssertCompiles(
            "public static System.Enum M()",
            boxed,
            "public enum CfgLongPriority : long { Low = 0, High = 2 }");
    }

    static string RenderFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(EnumCastSamples).FullName!, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    // As RenderFixture, but for a body that is only Partial at import (e.g. a slot
    // whose int/enum join the importer cannot type) and is raised to valid C# by
    // the pipeline — so it skips the import-time Full precondition.
    static string RenderRaisedFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(EnumCastSamples).FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static string RenderSyntheticSwitchExpression(
        TypeRef returnType,
        IrExpression firstArm,
        IrExpression defaultArm,
        IReadOnlyDictionary<TypeRef, TypeShape>? typeShapes = null,
        IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>>? enumMembers = null)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "value", intType),
            [
                new SwitchExpressionArm(ImmutableArray.Create(0), isDefault: false, firstArm),
                new SwitchExpressionArm(ImmutableArray<int>.Empty, isDefault: true, defaultArm),
            ]);
        var block = new Block(0);
        block.Add(new Return(switchExpression));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [new Parameter("value", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = typeShapes ?? new Dictionary<TypeRef, TypeShape>(),
            EnumMembers = enumMembers ?? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>(),
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderUnknownEnumReturnConstant(int value)
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderKnownEnumReturnConstant(int value, TypeRef underlying)
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = underlying },
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderUnknownEnumSwitchLabel(int value)
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Switch(
            new LoadArgument(0, "value", enumType),
            [
                new SwitchSection(ImmutableArray.Create(new Constant(value, intType)), isDefault: false, SingleReturnContainer()),
                new SwitchSection(ImmutableArray<Constant>.Empty, isDefault: true, SingleReturnContainer()),
            ]));
        body.Add(block);
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("value", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderUnknownEnumSwitchExpressionLabel(int value)
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "value", enumType),
            [
                new SwitchExpressionArm(ImmutableArray.Create(value), isDefault: false, new Constant(1, intType)),
                new SwitchExpressionArm(ImmutableArray<int>.Empty, isDefault: true, new Constant(0, intType)),
            ]);
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(switchExpression));
        body.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("value", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderIntegerNullableCoalesceIntoUnknownEnum()
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            ImmutableArray.Create(intType));
        var coalesce = new Coalesce(new LoadArgument(0, "value", nullableInt), new Constant(4, intType));
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(coalesce));
        body.Add(block);
        var signature = new MethodSignature(
            enumType,
            [new Parameter("value", nullableInt)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static BlockContainer SingleReturnContainer()
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(null));
        container.Add(block);
        return container;
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
