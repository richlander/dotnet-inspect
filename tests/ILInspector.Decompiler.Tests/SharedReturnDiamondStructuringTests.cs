using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SharedReturnDiamondStructuringTests
{
    static readonly TypeRef s_bool =
        TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_int =
        TypeRef.CoreLib("System", "Int32");

    static IrFunction Build(
        bool firstArmTerminates = true,
        bool stateMachineMoveNext = false)
    {
        var body = new BlockContainer();

        var head = new Block(0);
        head.Add(new ConditionalBranch(
            new LoadArgument(0, "outer", s_bool),
            30));
        body.Add(head);

        var firstGuard = new Block(10);
        firstGuard.Add(new ConditionalBranch(
            new LoadArgument(1, "inner", s_bool),
            50));
        body.Add(firstGuard);

        var firstBody = new Block(20);
        firstBody.Add(firstArmTerminates
            ? new Return(new Constant(1, s_int))
            : new StoreLocal(0, s_int, new Constant(1, s_int)));
        body.Add(firstBody);

        var elseGuard = new Block(30);
        elseGuard.Add(new ConditionalBranch(
            new LoadArgument(2, "fallback", s_bool),
            50));
        body.Add(elseGuard);

        var elseBody = new Block(40);
        elseBody.Add(new StoreLocal(0, s_int, new Constant(2, s_int)));
        body.Add(elseBody);

        var join = new Block(50);
        join.Add(new Return(new Constant(0, s_int)));
        body.Add(join);

        return new IrFunction(
            stateMachineMoveNext ? "MoveNext" : "M",
            TypeRef.Definition(
                "Synthetic",
                "",
                stateMachineMoveNext ? "<M>d__1" : "Holder"),
            new MethodSignature(
                s_int,
                [
                    new Parameter("outer", s_bool),
                    new Parameter("inner", s_bool),
                    new Parameter("fallback", s_bool),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [s_int],
            body);
    }

    [Fact]
    public void TerminatingFirstArmAndFallingSibling_StructureAsSharedJoinDiamond()
    {
        var function = Build();

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var outer = Assert.Single(
            function.Body.Descendants.OfType<IfStatement>(),
            statement => statement.HasElse);
        Assert.Single(outer.Then.Descendants.OfType<IfStatement>());
        Assert.Single(outer.Else!.Descendants.OfType<IfStatement>());
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
    }

    [Fact]
    public void FirstArmFallingIntoSibling_DoesNotClaimSharedJoinDiamond()
    {
        var function = Build(firstArmTerminates: false);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.DoesNotContain(
            function.Descendants.OfType<IfStatement>(),
            statement => statement.HasElse);
    }

    [Fact]
    public void StateMachineMoveNext_DoesNotClaimSourceOrientedSharedJoinDiamond()
    {
        var function = Build(stateMachineMoveNext: true);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.DoesNotContain(
            function.Descendants.OfType<IfStatement>(),
            statement => statement.HasElse);
        Assert.NotEmpty(function.Descendants.OfType<ConditionalBranch>());
    }
}
