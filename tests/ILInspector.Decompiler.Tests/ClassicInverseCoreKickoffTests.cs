using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Theory]
    [InlineData("early-return")]
    [InlineData("start-before-transfer")]
    [InlineData("wrong-initial-state")]
    [InlineData("duplicate-transfer")]
    public void ClassicInverseRawKickoffRetainsItsOrderedShell(string change)
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        Reconstruct(scope.Request);
        var raw = (IrFunction)scope.Request.KickoffBody.Clone();
        Block block = Assert.Single(raw.Body.Blocks);
        switch (change)
        {
            case "early-return":
            {
                foreach (IrNode node in block.Descendants)
                {
                    if (node.SourceOffset >= 0)
                        node.SetSourceOffset(node.SourceOffset + 2);
                }
                var unreachable = new Block(block.StartOffset + 2);
                foreach (IrNode statement in block.DetachChildren())
                    unreachable.Add(statement);
                block.Add(new Return(
                    new Pipeline.Constant(null, raw.Signature.ReturnType)));
                raw.Body.Add(unreachable);
                Assert.Equal(2, raw.Body.Blocks.Count);
                Assert.IsType<Return>(Assert.Single(block.Children));
                break;
            }
            case "start-before-transfer":
            {
                IrNode[] statements = [.. block.DetachChildren()];
                int start = Array.FindIndex(statements, statement =>
                    statement is ExpressionStatement
                    {
                        Expression: Call { Callee.Name: "Start" },
                    });
                int transfer = Array.FindIndex(statements, statement =>
                    statement is StoreField { Value: LoadArgument });
                Assert.True(start > transfer && transfer >= 0);
                (statements[start], statements[transfer]) =
                    (statements[transfer], statements[start]);
                foreach (IrNode statement in statements)
                    block.Add(statement);
                break;
            }
            case "wrong-initial-state":
            {
                StoreField state = Assert.Single(block.Children.OfType<StoreField>(),
                    store => store.Field.Name == "<>1__state");
                IrExpression original = state.Value;
                var replacement = new Pipeline.Constant(42, state.Field.Type);
                replacement.SetSourceOffset(original.SourceOffset);
                original.ReplaceWith(replacement);
                break;
            }
            case "duplicate-transfer":
            {
                StoreField first = Assert.Single(block.Children.OfType<StoreField>(),
                    store => store.Field.Name == "a");
                StoreField second = Assert.Single(block.Children.OfType<StoreField>(),
                    store => store.Field.Name == "b");
                second.ReplaceWith(first.Clone());
                break;
            }
        }

        bool repaired = false;
        Action<IrFunction, ImmutableArray<IIrPass>> runner = scope.Request.RunPasses!;
        ClassicInverseRequest request = ClassicInverseCore.Request(
            raw,
            scope.Request.StateMachineLocal,
            scope.Request.KickoffSourceOffset,
            scope.Request.ExecutionBody,
            scope.Request.ExecutionImportOffsets,
            SeedOf(scope.Request),
            (body, passes) =>
            {
                if (body.Name == raw.Name)
                {
                    body.SetChild(0, scope.Request.KickoffBody.Body.Clone());
                    repaired = true;
                }
                runner(body, passes);
            });
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(request));
        Assert.Equal(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            decline.Reason);
        Assert.True(repaired);
    }

    [Fact]
    public void ClassicInverseCoherentAwaitCannotAliasAnotherTypedField()
    {
        using RequestScope baseline = OpenRequest("AwaitValue");
        Reconstruct(baseline.Request);
        using RequestScope alternate = OpenMutatedRequest("AwaitValue", body =>
        {
            RetypeExecutionFieldAAsProbeAwaitable(body);
            Call original = Assert.Single(body.Body.Descendants.OfType<Call>(),
                call => call.Callee.Name == "GetAwaiter");
            LoadField receiver = Assert.IsType<LoadField>(
                Assert.Single(original.Arguments));
            Assert.NotEqual(original.Callee.DeclaringType, receiver.Field.Type);
            var replacement = new Call(
                original.Callee with { DeclaringType = receiver.Field.Type },
                original.IsVirtual,
                [.. original.Arguments.Select(argument =>
                    (IrExpression)argument.Clone())]);
            replacement.SetSourceOffset(original.SourceOffset);
            original.ReplaceWith(replacement);
        });
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(alternate.Request);
        ClassicInverseShellFacts shell = ClassicInverseShellFacts.Derive(
            planning.ExecutionBody,
            alternate.Request.ExecutionBody,
            new ClassicInverseBudget());
        Assert.Null(shell.Protocol.Failure);
        Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(alternate.Request));
    }
}
