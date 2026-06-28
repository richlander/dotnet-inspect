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
