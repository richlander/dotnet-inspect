using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// <see cref="PrologueGuardReturnPass"/> folds a leading <c>if (c) goto L;
/// return X; L:</c> prologue guard that <see cref="StructuringPass"/> leaves flat
/// only because the rest of the body is EH-entangled (a surviving <c>Leave</c>
/// makes a later block a leave-target). The fold must touch only the pristine
/// prologue and stand down whenever the arm could reach into the EH residue.
/// </summary>
public class PrologueGuardReturnPassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Str = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    enum Arm { Return, FallsThrough, Branch }

    static IrFunction Build(Arm arm = Arm.Return, bool survivingLeave = true)
    {
        var body = new BlockContainer();

        // Prologue guard: V_0 = …; if (V_0 == null) goto IL_0018; return V_0;
        var entry = new Block(0x0000);
        entry.Add(new StoreLocal(0, Str, new Constant(null, Str)));
        entry.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.Equal, false, new LoadLocal(0, Str), new Constant(null, Str)),
            0x0018));
        body.Add(entry);

        var armBlock = new Block(0x0008);
        switch (arm)
        {
            case Arm.Return:
                armBlock.Add(new Return(new LoadLocal(0, Str)));
                break;
            case Arm.FallsThrough:
                armBlock.Add(new StoreLocal(1, Int32, new Constant(0, Int32)));
                break;
            case Arm.Branch:
                armBlock.Add(new Branch(0x0018));
                break;
        }
        body.Add(armBlock);

        // EH-entangled residue: a loop header reached by a surviving Leave
        // back-edge, exactly the leave-target-in-container StructuringPass stop.
        var loopHeader = new Block(0x0018);
        loopHeader.Add(new StoreLocal(1, Int32, new Constant(1, Int32)));
        body.Add(loopHeader);

        var loopBody = new Block(0x0020);
        loopBody.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        loopBody.Add(survivingLeave ? new Leave(0x0018) : new Return(new LoadLocal(0, Str)));
        body.Add(loopBody);

        return new IrFunction(
            "M", Holder,
            new MethodSignature(Str, [], HasThis: false, GenericParameterCount: 0),
            [Str, Int32],
            body);
    }

    [Fact]
    public void PrologueGuard_InEhEntangledBody_Folds()
    {
        var function = Build();

        new PrologueGuardReturnPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<IfStatement>());
        // Arm consumed into the guard; the leave-target loop header stays flat.
        Assert.DoesNotContain(function.Body.Blocks, b => b.StartOffset == 0x0008);
        Assert.Contains(function.Body.Blocks, b => b.StartOffset == 0x0018);
        Assert.NotEmpty(function.Descendants.OfType<Leave>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("if (V_0 is not null)", output);
        Assert.DoesNotContain("goto", output.Split("if (V_0 is not null)")[0]);
    }

    [Fact]
    public void NoSurvivingLeave_StandsDown()
    {
        // Without a leave-target the container is StructuringPass's to own.
        var function = Build(survivingLeave: false);

        new PrologueGuardReturnPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.Contains(function.Body.Blocks, b => b.StartOffset == 0x0008);
    }

    [Fact]
    public void ArmFallsIntoResidue_StandsDown()
    {
        // The arm does not terminate, so folding it would change which code runs
        // when the guard is not taken — it must decline.
        var function = Build(arm: Arm.FallsThrough);

        new PrologueGuardReturnPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.Contains(function.Body.Blocks, b => b.StartOffset == 0x0008);
    }

    [Fact]
    public void ArmWithControlFlow_StandsDown()
    {
        // A branch inside the arm is out of the straight-line prologue slice.
        var function = Build(arm: Arm.Branch);

        new PrologueGuardReturnPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.Contains(function.Body.Blocks, b => b.StartOffset == 0x0008);
    }
}
