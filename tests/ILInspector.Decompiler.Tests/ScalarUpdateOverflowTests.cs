using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public sealed class ScalarUpdateOverflowTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.ScalarSelfUpdateSamples";

    [Theory]
    [InlineData("CheckedRhs", "checked { value += unchecked(amount + 1); }")]
    [InlineData("CheckedRhsNegation", "checked { value -= unchecked(-amount); }")]
    [InlineData("CheckedRhsConversion", "checked { value *= unchecked((short)amount); }")]
    [InlineData("CheckedRhsNested", "checked { value += unchecked(amount + checked(step * 2)); }")]
    [InlineData("CheckedRhsAllChecked", "checked { value += amount + 1; }")]
    [InlineData("UncheckedRhsChecked", "value += checked(amount + 1);")]
    [InlineData("CheckedRhsBitwise", "checked { value += amount & 7; }")]
    public void CompilerProducedContextsPreserveTheirOwnOverflow(string method, string expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, method);
            Assert.Contains(expected, CSharpPrinter.Print(function).Output);
            if (method == "CheckedRhsNested")
                Assert.Contains("return value + amount;", CSharpPrinter.Print(function).Output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryAdmittedStorageFamilyUsesTheCheckedContext(bool updated)
    {
        using var source = MetadataSource.Open(FixturePath(updated));
        var function = Raise(source, "CheckedRhsStores");
        var updates = function.Descendants.OfType<ScalarStore>()
            .Where(store => store.UpdateKind is not null).ToArray();
        Assert.Equal(5, updates.Length);
        string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
        Assert.Equal(5, output.Split("unchecked(").Length - 1);

        var indexer = Raise(source, "CheckedRhsIndexer");
        Assert.NotNull(Assert.Single(indexer.Descendants.OfType<StoreProperty>()).UpdateKind);
        Assert.Contains("+= unchecked(amount + 2)", CSharpPrinter.Print(indexer).Output);

        var indirect = Raise(source, "CheckedRhsIndirect");
        Assert.Contains("+= unchecked(amount + 1)", CSharpPrinter.Print(indirect).Output);
    }

    [Theory]
    [InlineData("CheckedLocalHeader")]
    [InlineData("CheckedRhsHeader")]
    public void BothCheckedHeaderPositionsRemainStatementExpressions(string method)
    {
        foreach (bool updated in new[] { false, true })
        {
            using var source = MetadataSource.Open(FixturePath(updated));
            var function = Raise(source, method);
            var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
            Assert.NotNull(Assert.IsAssignableFrom<ScalarStore>(loop.Initializer).UpdateKind);
            Assert.NotNull(Assert.IsAssignableFrom<ScalarStore>(loop.Increment).UpdateKind);
            string output = Assert.IsType<string>(CSharpPrinter.Print(function).Output);
            string header = Assert.Single(output.Split('\n'),
                line => line.TrimStart().StartsWith("for (", StringComparison.Ordinal));
            Assert.DoesNotContain("{", header);
            Assert.Equal(2, header.Split("= checked(").Length - 1);
            if (method == "CheckedRhsHeader")
                Assert.Equal(2, header.Split("unchecked(").Length - 1);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Area", "Fidelity")]
    public async Task MixedContextsAndHeadersRecompileExactlyWithoutTheFloor(bool updated)
    {
        string[] methods =
        [
            "CheckedRhs", "CheckedRhsNegation", "CheckedRhsConversion", "CheckedRhsNested",
            "CheckedRhsAllChecked", "UncheckedRhsChecked", "CheckedRhsBitwise",
            "CheckedRhsStores", "CheckedRhsIndirect", "CheckedLocalHeader", "CheckedRhsHeader",
        ];
        var targets = methods.Select(method => new ReturnToSender.RequestedTarget(FixtureType, method, 0)).ToArray();
        var results = await ReturnToSender.CompileBackTargets(
            FixturePath(updated), targets, sourceIndex: null, applyCompileBackFloor: false);
        Assert.Equal(targets.Length, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
        });
    }

    static string FixturePath(bool updated)
        => (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy).AssemblyPath();

    static IrFunction Raise(MetadataSource source, string method)
    {
        var function = IrImporter.Import(source, FixtureType, method);
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        return function;
    }
}
