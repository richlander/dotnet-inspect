using CSharpText.Tests;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class PdbLocalDeclarationScopeTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.Definition("Tests", "Samples", "Scopes");

    [Theory]
    [InlineData(nameof(PdbScopeFixtures.DisjointScopeLocals))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocals))]
    [InlineData(nameof(PdbScopeFixtures.LambdaScopes))]
    [InlineData(nameof(PdbScopeFixtures.LocalFunctionScopes))]
    public void DisjointCompilerScopes_PreserveBothExactNames(string method)
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            method)!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.True(result.Fidelity == DecompilationFidelity.Full,
            $"{method}: {result.Output}\n{CSharpSpellability.InspectUnrepresentableMetadataName(function)}\n{IrPrinter.Dump(function)}");
        Assert.Contains("int same = value;", result.Output);
        Assert.Contains("string same = value.ToString();", result.Output);
        Assert.Contains("Increment(ref same);", result.Output);
        Assert.Contains("KeepAlive(ref same);", result.Output);
        Assert.DoesNotContain("V_", result.Output);
    }

    [Fact]
    public void NoPdb_DoesNotIntroduceLexicalBlocks()
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.DisjointScopeLocals))!;
        IrPasses.Run(function, [.. IrPasses.Default.Where(pass => pass is not PdbLocalScopePass)]);
        string before = CSharpPrinter.Print(function).Output!;

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        Assert.DoesNotContain("same", before);
    }

    [Fact]
    public void SequentialValueTypeCompilerScopes_PreserveBothExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SequentialValueTypeScopeLocals))!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "Guid same = default;", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, result.Output.Split(
            "KeepGuidAlive(ref same);", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Guid V_", result.Output);
    }

    [Fact]
    public void SiblingBlocks_PreserveNamesWithoutAdditionalWrapping()
    {
        var function = Siblings();
        int blocks = function.Descendants.OfType<Block>().Count();

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(blocks, function.Descendants.OfType<Block>().Count());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("int same = 1;", result.Output);
        Assert.Contains("int same = 2;", result.Output);
    }

    [Fact]
    public void OverlappingUses_KeepCollisionVisible()
    {
        var function = Siblings();
        function.Body.Blocks[0].Add(Observe(0));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Contains("V_1", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void ParameterReservation_IsNotBypassedByDisjointScopes()
    {
        var function = Siblings(new Parameter("same", Boolean));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int V_0 = 1;", output);
        Assert.Contains("int V_1 = 2;", output);
    }

    [Fact]
    public void ScopedPass_IsIdempotent()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.DisjointScopeLocals))!;
        IrPasses.Run(function);
        string before = CSharpPrinter.Print(function).Output!;

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(before, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void InlinedStackCarry_DoesNotPretendScopesAreDisjoint()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SequentialStackCarry))!;
        IrPasses.Run(function, [.. IrPasses.Default.Where(pass => pass is not PdbLocalScopePass)]);
        string before = CSharpPrinter.Print(function).Output!;

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    static IrFunction Siblings(Parameter? parameter = null)
    {
        parameter ??= new Parameter("condition", Boolean);
        var then = new Block();
        then.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        then.Add(Observe(0));
        var otherwise = new Block();
        otherwise.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        otherwise.Add(Observe(1));
        var entry = new Block();
        entry.Add(new IfStatement(new LoadArgument(0, parameter), then, otherwise));
        var body = new BlockContainer();
        body.Add(entry);
        return new IrFunction("M", Owner, new MethodSignature(Void, [parameter], false, 0), [Int32, Int32], body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };
    }

    static IrNode Observe(int index)
        => new ExpressionStatement(new Call(
            new MethodRef(Owner, "Observe", Void, [Int32], false),
            false,
            [new LoadLocal(index, Int32)]));
}
