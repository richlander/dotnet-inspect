using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    const string ReceiverFixtureType = "ILInspector.Decompiler.Fixtures.ClassicAsync.ReceiverAliasFixtures";

    [Theory]
    [InlineData("AwaitReceiver")]
    [InlineData("AwaitReceiverResult")]
    public void ClassicInverseRecoversProvenReferenceReceiverAlias(string method)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, method, ownsSource: false, fixtureType: ReceiverFixtureType);
        Reconstruct(scope.Request);
        var function = IrImporter.Import(source, ReceiverFixtureType, method)!;
        var printed = CSharpPrinter.PrintRaised(function, reference => IrImporter.Import(source, reference));
        Assert.True(printed.Succeeded, string.Join("; ", printed.Diagnostics));
        Assert.Equal(DecompilationFidelity.Full, printed.Fidelity);
        Assert.Contains("await ", printed.Output);
        Assert.DoesNotContain("g__", printed.Output);
        function.CheckInvariant();
    }

    [Theory]
    [InlineData("write")]
    [InlineData("address")]
    [InlineData("name")]
    public void ClassicInverseDeclinesReceiverAliasRebindingAddressOrSourceName(string mutation)
    {
        using var source = OpenClassicFixture();
        using var scope = OpenRequest(source, "AwaitReceiver", ownsSource: false,
            mutateExecution: body =>
            {
                var alias = body.Descendants.OfType<StoreLocal>().Single(store => store.Value is LoadField { Field.Name: "<>4__this" });
                if (mutation == "name")
                {
                    body.ResetLocals(body.Locals, body.LocalNames.SetItem(alias.Index, "receiver"));
                    return;
                }
                var block = (Block)alias.Parent!;
                var statements = block.DetachChildren();
                foreach (var statement in statements)
                {
                    block.Add(statement);
                    if (ReferenceEquals(statement, alias))
                    {
                        IrNode added = mutation == "address"
                            ? new ExpressionStatement(new LoadLocalAddress(alias.Index, alias.Type))
                            : new StoreLocal(alias.Index, alias.Type, new Constant(null, alias.Type));
                        added.SetSourceOffset(alias.SourceOffset);
                        block.Add(added);
                    }
                }
            }, fixtureType: ReceiverFixtureType);
        Assert.IsType<ClassicInverseDecision.Decline>(ClassicInverseCore.Decide(scope.Request));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public void ReferenceReceiverAliases_CompileBackExact()
    {
        using var source = OpenClassicFixture();
        var results = FidelityCheck.Evaluate(source.Path, lowered: false, FidelityCheck.ClusterMode.Off,
            type => type == ReceiverFixtureType, method => method.Method.StartsWith("AwaitReceiver", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact && result.UsedProductWholeMember,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}
