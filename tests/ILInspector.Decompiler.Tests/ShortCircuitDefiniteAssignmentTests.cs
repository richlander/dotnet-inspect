using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ShortCircuitDefiniteAssignmentTests
{
    static readonly TypeRef Holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    [Theory]
    [InlineData(LogicalKind.And, true, false)]
    [InlineData(LogicalKind.And, false, true)]
    [InlineData(LogicalKind.Or, true, true)]
    [InlineData(LogicalKind.Or, false, false)]
    public void OutcomeKeepsOnlyAssignmentsOnEveryReachingPath(
        LogicalKind kind, bool readOnTrue, bool needsDefault)
    {
        var condition = new LogicalBinary(kind,
            new LoadArgument(0, "choose", Bool), OutCall());
        var read = Returning(new LoadLocal(0, Int32));
        var other = Returning(new Constant(0, Int32));
        var block = new Block(0);
        block.Add(new IfStatement(condition,
            readOnTrue ? read : other,
            readOnTrue ? other : read));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Equal(needsDefault, function.ZeroInitializedLocals!.Contains(0));
    }

    [Theory]
    [InlineData(ArgumentRefKind.Ref, ParameterRefKindFacts.Known)]
    [InlineData(ArgumentRefKind.Out, ParameterRefKindFacts.Unknown)]
    public void UnverifiedOrInputReferenceKeepsInitialization(
        ArgumentRefKind refKind, ParameterRefKindFacts facts)
    {
        var block = new Block(0);
        block.Add(new IfStatement(
            new LogicalBinary(LogicalKind.And,
                new LoadArgument(0, "choose", Bool), OutCall(refKind, facts)),
            Returning(new LoadLocal(0, Int32)),
            Returning(new Constant(0, Int32))));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Contains(0, function.ZeroInitializedLocals!);
    }

    [Fact]
    public void ReturningGuardCarriesNegatedFalseOutcomeToContinuation()
    {
        var block = new Block(0);
        block.Add(new IfStatement(
            new LogicalBinary(LogicalKind.Or,
                new LoadArgument(0, "choose", Bool), new LogicalNot(OutCall())),
            Returning(new Constant(0, Int32)), null));
        block.Add(new Return(new LoadLocal(0, Int32)));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Empty(function.ZeroInitializedLocals!);
    }

    [Fact]
    public void NestedAndUsesTrueOutcomeBeforeReadingRightOperand()
    {
        var block = new Block(0);
        block.Add(new IfStatement(new LogicalBinary(LogicalKind.And,
            new LogicalBinary(LogicalKind.And,
                new LoadArgument(0, "choose", Bool), OutCall()),
            new Comparison(ComparisonKind.GreaterThan, false,
                new LoadLocal(0, Int32), new Constant(0, Int32))),
            Returning(new LoadLocal(0, Int32)),
            Returning(new Constant(0, Int32))));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Empty(function.ZeroInitializedLocals!);
    }

    [Fact]
    public void SkippedCallDoesNotAssignTheFallthroughContinuation()
    {
        var block = new Block(0);
        block.Add(new IfStatement(new LogicalBinary(LogicalKind.And,
            new LoadArgument(0, "choose", Bool), OutCall()),
            Returning(new Constant(0, Int32)), null));
        block.Add(new Return(new LoadLocal(0, Int32)));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Contains(0, function.ZeroInitializedLocals!);
    }

    [Fact]
    public void ExternalBranchRetainsConservativeInitialization()
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(OutCall()));
        block.Add(new Branch(999));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Contains(0, function.ZeroInitializedLocals!);
    }

    [Fact]
    public void HandlerEntryDoesNotInheritProtectedBodyAssignment()
    {
        var protectedBlock = new Block(0);
        protectedBlock.Add(new IfStatement(new LogicalBinary(LogicalKind.And,
            new LoadArgument(0, "choose", Bool), OutCall()),
            Returning(new LoadLocal(0, Int32)),
            Returning(new Constant(0, Int32))));
        var protectedBody = new BlockContainer();
        protectedBody.Add(protectedBlock);
        var handler = new BlockContainer();
        handler.Add(Returning(new LoadLocal(0, Int32)));
        var block = new Block(0);
        block.Add(new TryCatch(protectedBody,
            [new CatchClause(TypeRef.CoreLib("System", "Exception"), handler)]));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Contains(0, function.ZeroInitializedLocals!);
    }

    [Fact]
    public void IsolatedNestedBodyDoesNotInheritHostLocalAssignment()
    {
        var nestedBody = new BlockContainer();
        nestedBody.Add(Returning(new LoadLocal(0, Int32)));
        var nested = new LocalFunctionStatement("Inner", Int32, [],
            isStatic: true, [Int32], [], usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false, nestedBody);
        var block = new Block(0);
        block.Add(nested);
        block.Add(new IfStatement(new LogicalBinary(LogicalKind.And,
            new LoadArgument(0, "choose", Bool), OutCall()),
            Returning(new LoadLocal(0, Int32)),
            Returning(new Constant(0, Int32))));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Empty(function.ZeroInitializedLocals!);
        Assert.Contains(0, nested.ZeroInitializedLocals!);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConditionalExpressionUsesItsOwnOutcomeAssignments(
        bool readOnTrue, bool needsDefault)
    {
        var block = new Block(0);
        IrExpression value = new LoadLocal(0, Int32);
        IrExpression zero = new Constant(0, Int32);
        block.Add(new Return(new Conditional(new LogicalBinary(LogicalKind.And,
            new LoadArgument(0, "choose", Bool), OutCall()),
            readOnTrue ? value : zero, readOnTrue ? zero : value)));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        Assert.Equal(needsDefault, function.ZeroInitializedLocals!.Contains(0));
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(LogicalKind.And, true)]
    [InlineData(LogicalKind.And, false)]
    [InlineData(LogicalKind.Or, true)]
    [InlineData(LogicalKind.Or, false)]
    public void RenderedOutcomeAssignmentsBind(LogicalKind kind, bool readOnTrue)
    {
        var read = Returning(new LoadLocal(0, Int32));
        var other = Returning(new Constant(0, Int32));
        var block = new Block(0);
        block.Add(new IfStatement(new LogicalBinary(kind,
            new LoadArgument(0, "choose", Bool), OutCall()),
            readOnTrue ? read : other, readOnTrue ? other : read));
        var function = Function(block);

        new DefiniteAssignmentPass().Run(function, PassContext.None);

        function.CheckInvariant();
        AssertBinds(function);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CompilerProducedGuardCompilesBackExactlyUnderNativeRts()
    {
        var type = typeof(ShortCircuitAssignmentSample);
        string path = type.Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(
            source, type.FullName!, nameof(ShortCircuitAssignmentSample.Parse)));
        var results = await FidelityCheck.EvaluateChangedMethodTargetsForTesting([path],
            [new FidelityCheck.CompileBackTarget(path, type.FullName!,
                nameof(ShortCircuitAssignmentSample.Parse), 0,
                CorpusMethodIdentity.SignatureText(function.Signature))]);

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, Assert.Single(results).Status);
    }

    [Fact]
    public void CompilerProducedReturningGuardHasNoDefaultStore()
    {
        var type = typeof(ShortCircuitAssignmentSample);
        using var metadata = CorpusMetadata.Create([type.Assembly.Location]);
        using var source = MetadataSource.Open(type.Assembly.Location, context: metadata);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(
            source, type.FullName!, nameof(ShortCircuitAssignmentSample.Parse)));
        var result = CSharpPrinter.PrintRaised(function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        function.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain("= default", result.Output);
    }

    [Fact]
    public void PinnedTypeRefDisplayNameHasNoDefaultStore()
    {
        string path = Path.Combine(AppContext.BaseDirectory,
            "RealAssets", "SwitchSection", "ILInspector.Decompiler.dll");
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(source,
            "ILInspector.Decompiler.Pipeline.TypeRef", "DisplayName"));
        var result = CSharpPrinter.PrintRaised(function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        function.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain("= default", result.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompilerProducedUserTruthinessKeepsInitializationAndBinds()
    {
        var type = typeof(ShortCircuitTruthinessSample);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(source,
            type.FullName!, nameof(ShortCircuitTruthinessSample.NestedGuard)));
        var result = CSharpPrinter.PrintRaised(function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);

        function.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        string body = Assert.IsType<string>(result.Output);
        Assert.Contains("&&", body);
        Assert.Contains("= default", body);
        string text = $$"""
            using ILInspector.Decompiler.Tests;
            class Shell
            {
                static ShortCircuitTruthValue Read(out int value)
                {
                    value = 42;
                    return new ShortCircuitTruthValue(true);
                }
                static int M(ShortCircuitTruthValue input)
                {
                    {{body}}
                }
            }
            """;
        var compilation = CSharpCompilation.Create("truthiness-decline-binding",
            [CSharpSyntaxTree.ParseText(text,
                cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestReferences.TrustedPlatform.Add(
                MetadataReference.CreateFromFile(type.Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    static void AssertBinds(IrFunction function, ArgumentRefKind kind = ArgumentRefKind.Out)
    {
        string body = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        string refKind = kind == ArgumentRefKind.Ref ? "ref" : "out";
        string text = $$"""
            namespace Samples;
            static class Holder
            {
                static bool TryGet({{refKind}} int value) { value = 42; return true; }
                static int M(bool choose)
                {
                    {{body}}
                }
            }
            """;
        var compilation = CSharpCompilation.Create("assignment-gate",
            [CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview))],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    static Call OutCall(ArgumentRefKind kind = ArgumentRefKind.Out,
        ParameterRefKindFacts facts = ParameterRefKindFacts.Known)
        => new(new MethodRef(Holder, "TryGet", Bool, [TypeRef.ByRef(Int32)], HasThis: false)
        {
            ParameterRefKinds = [kind],
            ParameterRefKindsFacts = facts,
        }, isVirtual: false, [new LoadLocalAddress(0, Int32)]);

    static Block Returning(IrExpression value)
    {
        var block = new Block(1);
        block.Add(new Return(value));
        return block;
    }

    static IrFunction Function(Block block)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("M", Holder,
            new MethodSignature(Int32, [new Parameter("choose", Bool)],
                HasThis: false, GenericParameterCount: 0), [Int32], body);
    }
}

public static class ShortCircuitAssignmentSample
{
    public static int Parse(string text)
    {
        if (text is null || !int.TryParse(text, out int value))
            return 0;
        return value;
    }
}

public readonly struct ShortCircuitTruthValue
{
    readonly bool _value;

    public ShortCircuitTruthValue(bool value) => _value = value;
    public static bool operator true(ShortCircuitTruthValue value) => value._value;
    public static bool operator false(ShortCircuitTruthValue value) => !value._value;
    public static ShortCircuitTruthValue operator &(
        ShortCircuitTruthValue left, ShortCircuitTruthValue right)
        => new(left._value && right._value);
}

public static class ShortCircuitTruthinessSample
{
    static ShortCircuitTruthValue Read(out int value)
    {
        value = 42;
        return new ShortCircuitTruthValue(true);
    }

    public static int NestedGuard(ShortCircuitTruthValue input)
    {
        if (input)
        {
            if (Read(out int value))
                return value;
        }
        return 0;
    }
}
