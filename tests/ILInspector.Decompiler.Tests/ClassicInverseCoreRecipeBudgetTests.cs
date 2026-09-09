using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed partial class ClassicInverseCoreTests
{
    [Fact]
    public void ClassicInverseRecipeIndexChargesSnapshotAndQueries()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(scope.Request);
        ClassicInverseShellFacts shell = ClassicInverseShellFacts.Derive(
            planning.ExecutionBody,
            scope.Request.ExecutionBody,
            new ClassicInverseBudget());
        Assert.Null(shell.Protocol.Failure);

        int nodes = planning.ExecutionBody.Body.Descendants.Count() + 1;
        int buildUnits = checked(2 * nodes);
        var buildBudget = new ClassicInverseBudget(buildUnits);
        ClassicInverseRecipes.RecipeIndex index =
            Assert.IsType<ClassicInverseRecipes.RecipeIndex>(
                ClassicInverseRecipes.RecipeIndex.Build(
                    planning.ExecutionBody,
                    shell,
                    buildBudget));
        Assert.Equal(nodes, index.NodeCount);
        Assert.Equal(buildUnits, buildBudget.Consumed);

        var shortBuild = new ClassicInverseBudget(buildUnits - 1);
        Assert.Null(ClassicInverseRecipes.RecipeIndex.Build(
            planning.ExecutionBody,
            shell,
            shortBuild));
        Assert.True(shortBuild.Exhausted);

        int storeCount =
            planning.ExecutionBody.Body.Descendants.OfType<StoreLocal>().Count();
        var queryBudget = new ClassicInverseBudget(storeCount + 1);
        Assert.True(index.TryFind<StoreLocal>(
            planning.ExecutionBody.Body,
            static _ => true,
            queryBudget,
            out List<StoreLocal> stores));
        Assert.Equal(storeCount, stores.Count);
        Assert.Equal(storeCount + 1, queryBudget.Consumed);

        var shortQuery = new ClassicInverseBudget(storeCount);
        Assert.False(index.TryFind<StoreLocal>(
            planning.ExecutionBody.Body,
            static _ => true,
            shortQuery,
            out List<StoreLocal> partial));
        Assert.Empty(partial);
        Assert.True(shortQuery.Exhausted);

        Call getResult = index.GetResults[0];
        StoreLocal owner = Assert.Single(
            stores,
            store => ReferenceEquals(store.Value, getResult)
                || store.Value.Descendants.Any(
                    node => ReferenceEquals(node, getResult)));

        var containsBudget = new ClassicInverseBudget(1);
        Assert.True(index.Contains(owner.Value, getResult, containsBudget));
        Assert.Equal(1, containsBudget.Consumed);

        var operandBudget = new ClassicInverseBudget(1);
        Assert.NotNull(index.AwaitedOperand(getResult, operandBudget));
        Assert.Equal(1, operandBudget.Consumed);

        var blockBudget = new ClassicInverseBudget(1);
        Assert.NotNull(index.EnclosingBlock(getResult, blockBudget));
        Assert.Equal(1, blockBudget.Consumed);
    }

    [Fact]
    public void ClassicInverseExpandedRecipeSnapshotIsLoadBearingEndToEnd()
    {
        using RequestScope scope = OpenRequest("TwoSequentialAwaits");
        int expansion = 0;
        ClassicInverseRequest expanded = CopyRequest(
            scope.Request,
            runPasses: (body, passes) =>
            {
                scope.Request.RunPasses!(body, passes);
                if (body.Name != "MoveNext")
                    return;

                int before = body.Body.Descendants.Count() + 1;
                Call setResult = Assert.Single(
                    body.Body.Descendants.OfType<Call>(),
                    call => call.Callee.Name == "SetResult"
                        && ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                            call.Callee.DeclaringType));
                ExpressionStatement completion =
                    Assert.IsType<ExpressionStatement>(setResult.Parent);
                var padding = new Block();
                for (int i = 0; i < 64; i++)
                {
                    padding.Add(new ExpressionStatement(
                        new Constant(
                            i,
                            TypeRef.CoreLib("System", "Int32"))));
                }
                var replacement = new IfStatement(
                    new Constant(
                        true,
                        TypeRef.CoreLib("System", "Boolean")),
                    padding,
                    elseArm: null);
                replacement.InheritSourceOffset(completion);
                completion.ReplaceWith(replacement);
                expansion =
                    body.Body.Descendants.Count() + 1 - before;
            });

        int originalNodes = ClassicInversePlanningView.Derive(scope.Request)
            .ExecutionBody.Body.Descendants.Count() + 1;
        var planningBudget = new ClassicInverseBudget();
        ClassicInversePlanningView planning =
            ClassicInversePlanningView.Derive(expanded, planningBudget);
        int planningNodes =
            planning.ExecutionBody.Body.Descendants.Count() + 1;
        Assert.True(expansion >= 64);
        Assert.Equal(originalNodes + expansion, planningNodes);

        var shellBudget = new ClassicInverseBudget();
        _ = ClassicInverseShellFacts.Derive(
            planning.ExecutionBody,
            expanded.ExecutionBody,
            shellBudget);
        int admissionUnits =
            expanded.KickoffBody.Body.Descendants.Count() + 1
            + expanded.ExecutionBody.Body.Descendants.Count() + 1;
        int recipeSnapshotUnits = checked(2 * planningNodes);
        int exactUnits = checked(
            admissionUnits + planningBudget.Consumed + shellBudget.Consumed + recipeSnapshotUnits);

        var exactBudget = new ClassicInverseBudget(exactUnits);
        var decline = Assert.IsType<ClassicInverseDecision.Decline>(
            ClassicInverseCore.Decide(expanded, exactBudget));
        Assert.Equal(
            ClassicInverseDeclineReason.NoRecipeMatched,
            decline.Reason);
        Assert.Equal(exactUnits, exactBudget.Consumed);

        var shortBudget = new ClassicInverseBudget(exactUnits - 1);
        var failure = Assert.IsType<ClassicInverseDecision.Failed>(
            ClassicInverseCore.Decide(expanded, shortBudget));
        Assert.Equal(
            ClassicInverseFailureKind.BudgetExhausted,
            failure.Failure.Kind);
        Assert.Contains(
            "recipe matching exhausted",
            failure.Failure.Detail,
            StringComparison.Ordinal);
        Assert.True(shortBudget.Exhausted);
    }
}
