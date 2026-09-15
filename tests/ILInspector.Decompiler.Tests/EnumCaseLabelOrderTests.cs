using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public enum EnumCaseLabelOrderKind
{
    Zebra = 10,
    Charlie = 11,
    Middle = 12,
    Alpha = 13,
    Tango = 14,
    Bravo = 15,
}

public static class EnumCaseLabelOrderSpecimen
{
    public static int Classify(EnumCaseLabelOrderKind kind)
    {
        switch (kind)
        {
            case EnumCaseLabelOrderKind.Zebra:
            case EnumCaseLabelOrderKind.Middle:
            case EnumCaseLabelOrderKind.Tango:
                return 1;
            case EnumCaseLabelOrderKind.Alpha:
            case EnumCaseLabelOrderKind.Bravo:
            case EnumCaseLabelOrderKind.Charlie:
                return 2;
            default:
                return 0;
        }
    }
}

[Trait("Area", "Printer")]
public class EnumCaseLabelOrderTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef EnumType = TypeRef.Definition("Synthetic", "", "OrderKind");

    [Fact]
    public void AlphabeticalDefault_SortsNamedLabelsWithinEachSharedBody()
    {
        var result = RenderCompiled(PrinterOptions.Default);

        AssertBefore(result.Output!, "EnumCaseLabelOrderKind.Middle", "EnumCaseLabelOrderKind.Tango", "EnumCaseLabelOrderKind.Zebra");
        AssertBefore(result.Output!, "EnumCaseLabelOrderKind.Alpha", "EnumCaseLabelOrderKind.Bravo", "EnumCaseLabelOrderKind.Charlie");
        Assert.Equal(2, result.Decisions.Count(d => d.RuleId == "enum-case-label-order"));
        Assert.All(
            result.Decisions.Where(d => d.RuleId == "enum-case-label-order"),
            decision =>
            {
                Assert.Equal(DecompilerDecisionCategories.Taste, decision.Category);
                Assert.Equal("value", decision.OldValue);
                Assert.Equal("alphabetical", decision.NewValue);
            });
        Assert.Equal(EnumCaseLabelOrder.Alphabetical, result.EffectiveOptions.EnumCaseLabelOrder);
    }

    [Fact]
    public void ValueOption_PreservesRecoveredNumericOrder()
    {
        var options = PrinterOptions.Default with { EnumCaseLabelOrder = EnumCaseLabelOrder.Value };

        var result = RenderCompiled(options);

        AssertBefore(result.Output!, "EnumCaseLabelOrderKind.Zebra", "EnumCaseLabelOrderKind.Middle", "EnumCaseLabelOrderKind.Tango");
        AssertBefore(result.Output!, "EnumCaseLabelOrderKind.Charlie", "EnumCaseLabelOrderKind.Alpha", "EnumCaseLabelOrderKind.Bravo");
        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "enum-case-label-order");
        Assert.Equal(EnumCaseLabelOrder.Value, result.EffectiveOptions.EnumCaseLabelOrder);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void AlphabeticalDefault_IsACompileDecompileFixedPoint()
    {
        string first = RenderCompiled(PrinterOptions.Default).Output!;
        string source = $$"""
            namespace ILInspector.Decompiler.Tests;

            public enum EnumCaseLabelOrderKind
            {
                Zebra = 10,
                Charlie = 11,
                Middle = 12,
                Alpha = 13,
                Tango = 14,
                Bravo = 15,
            }

            public static class EnumCaseLabelOrderSpecimen
            {
                public static int Classify(EnumCaseLabelOrderKind kind)
                {
            {{first}}
                }
            }
            """;
        string path = Path.Combine(Path.GetTempPath(), $"enum-case-label-order-{Guid.NewGuid():N}.dll");

        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var compilation = CSharpCompilation.Create(
                "EnumCaseLabelOrderFixedPoint",
                [CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    cancellationToken: cancellationToken)],
                RoslynTestReferences.TrustedPlatform,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));
            var emit = compilation.Emit(path, cancellationToken: cancellationToken);
            Assert.True(
                emit.Success,
                "fixed-point fixture compilation failed:\n"
                    + string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            using var metadata = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(
                metadata,
                typeof(EnumCaseLabelOrderSpecimen).FullName!,
                nameof(EnumCaseLabelOrderSpecimen.Classify));
            Assert.NotNull(function);

            string second = CSharpPrinter.PrintRaised(
                function!,
                method => IrImporter.Import(metadata, method),
                PrinterOptions.Default).Output!;
            Assert.Equal(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MixedNamedAndUnnamedLabels_KeepValueOrder()
    {
        var function = Synthetic(
            EnumType,
            [new Constant(2, Int32), new Constant(1, Int32)],
            new Dictionary<long, string> { [2] = "Zulu" });

        var result = CSharpPrinter.Print(function, PrinterOptions.Default);

        AssertBefore(result.Output!, "OrderKind.Zulu", "(OrderKind)1");
        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "enum-case-label-order");
    }

    [Fact]
    public void NonEnumLabels_KeepValueOrder()
    {
        var function = Synthetic(
            Int32,
            [new Constant(2, Int32), new Constant(1, Int32)]);

        var result = CSharpPrinter.Print(function, PrinterOptions.Default);

        AssertBefore(result.Output!, "case 2:", "case 1:");
        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "enum-case-label-order");
    }

    [Fact]
    public void SharedDefault_RemainsAfterTheSortedLabels()
    {
        var function = Synthetic(
            EnumType,
            [new Constant(2, Int32), new Constant(1, Int32)],
            new Dictionary<long, string>
            {
                [2] = "Zulu",
                [1] = "Alpha",
            },
            sharedDefault: true);

        var result = CSharpPrinter.Print(function, PrinterOptions.Default);

        AssertBefore(result.Output!, "OrderKind.Alpha", "OrderKind.Zulu", "default:");
    }

    [Fact]
    public void GuardedPatternSwitch_IsOutsideTheConstantLabelOrderingSurface()
    {
        var alphabetical = RenderPatternSwitch(PrinterOptions.Default);
        var value = RenderPatternSwitch(
            PrinterOptions.Default with { EnumCaseLabelOrder = EnumCaseLabelOrder.Value });

        Assert.Equal(alphabetical.Output, value.Output);
        Assert.Contains(" when ", alphabetical.Output);
        Assert.DoesNotContain(alphabetical.Decisions, d => d.RuleId == "enum-case-label-order");
    }

    [Fact]
    public void NestedLocalFunction_WithOwnScope_ReportsOrderingDecision()
    {
        var function = SyntheticNestedLocalFunction();

        var result = CSharpPrinter.Print(function, PrinterOptions.Default);

        AssertBefore(result.Output!, "OrderKind.Alpha", "OrderKind.Zulu");
        var decision = Assert.Single(result.Decisions, d => d.RuleId == "enum-case-label-order");
        Assert.Equal("Local", decision.Subject);
    }

    static DecompilerResult RenderCompiled(PrinterOptions options)
    {
        using var source = MetadataSource.Open(typeof(EnumCaseLabelOrderSpecimen).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(EnumCaseLabelOrderSpecimen).FullName!,
            nameof(EnumCaseLabelOrderSpecimen.Classify));
        Assert.NotNull(function);
        return CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method), options);
    }

    static DecompilerResult RenderPatternSwitch(PrinterOptions options)
    {
        using var source = MetadataSource.Open(typeof(PatternSwitchSample).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(PatternSwitchSample).FullName!,
            nameof(PatternSwitchSample.Classify));
        Assert.NotNull(function);
        return CSharpPrinter.PrintRaised(
            function!,
            method => IrImporter.Import(source, method),
            options,
            source.AreProvablyDisjoint);
    }

    static IrFunction Synthetic(
        TypeRef governingType,
        ImmutableArray<Constant> labels,
        IReadOnlyDictionary<long, string>? members = null,
        bool sharedDefault = false)
    {
        var sectionBlock = new Block();
        sectionBlock.Add(new Return(new Constant(1, Int32)));
        var sectionBody = new BlockContainer();
        sectionBody.Add(sectionBlock);

        var statement = new Switch(
            new LoadArgument(0, "kind", governingType),
            [new SwitchSection(labels, sharedDefault, sectionBody)]);
        var block = new Block();
        block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "Holder"),
            new MethodSignature(
                Int32,
                [new Parameter("kind", governingType)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = governingType.Equals(EnumType)
                ? new Dictionary<TypeRef, TypeShape> { [EnumType] = TypeShape.Enum }
                : new Dictionary<TypeRef, TypeShape>(),
            EnumMembers = members is null
                ? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()
                : new Dictionary<TypeRef, IReadOnlyDictionary<long, string>> { [EnumType] = members },
        };
    }

    static IrFunction SyntheticNestedLocalFunction()
    {
        var sectionBlock = new Block();
        sectionBlock.Add(new Return(new Constant(1, Int32)));
        var sectionBody = new BlockContainer();
        sectionBody.Add(sectionBlock);

        var localBlock = new Block();
        localBlock.Add(new Switch(
            new LoadArgument(0, "kind", EnumType),
            [new SwitchSection(
                [new Constant(2, Int32), new Constant(1, Int32)],
                isDefault: true,
                sectionBody)]));
        var localBody = new BlockContainer();
        localBody.Add(localBlock);

        var localFunction = new LocalFunctionStatement(
            "Local",
            Int32,
            [new Parameter("kind", EnumType)],
            isStatic: true,
            locals: [Int32],
            localNames: [null],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);
        var outerBlock = new Block();
        outerBlock.Add(localFunction);
        outerBlock.Add(new Return(null));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);

        return new IrFunction(
            "Outer",
            TypeRef.Definition("Synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            outerBody)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [EnumType] = TypeShape.Enum },
            EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [EnumType] = new Dictionary<long, string>
                {
                    [2] = "Zulu",
                    [1] = "Alpha",
                },
            },
        };
    }

    static void AssertBefore(string text, params string[] values)
    {
        int previous = -1;
        foreach (var value in values)
        {
            int current = text.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{value}' after the prior value.\n{text}");
            previous = current;
        }
    }
}
