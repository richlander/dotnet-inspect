using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial near-miss matrix for ClassicAsyncReconstructionPass (#1290).
//
// The pass raises runtime-async=off async kickoffs back to async bodies. Only
// adapter-issued exact roles grant stage-application authority. Generated
// names and builder-field shapes remain recognition hints inside the
// authenticated declared-kickoff path; they never authorize mutation by
// themselves.
//
// Product imports are governed by ClassicAsyncRequestAdapterResult. The
// synthetic pins below prove generated shape alone neither edits a support
// lookalike nor enters the cross-method kickoff pipeline.
[Trait("Area", "Pass")]
public class ClassicAsyncReconstructionPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Task = TypeRef.CoreLib("System.Threading.Tasks", "Task");

    // Compiler-reserved state-machine type name (`<...>d__N`).
    static readonly TypeRef StateMachine = TypeRef.Definition("Synthetic", "Samples", "Outer+<Fake>d__0");

    static readonly TypeRef Builder = TypeRef.Definition("Synthetic", "Samples", "BuilderLike");

    // ---- Unauthenticated support-method lookalikes ------------------------

    [Fact]
    public void BuilderShapedMoveNextWithoutOwnerRole_IsPreserved()
    {
        var function = BuildSupportMethod("MoveNext");

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodPreserved(function);
    }

    [Fact]
    public void BuilderShapedSetStateMachineWithoutOwnerRole_IsPreserved()
    {
        var function = BuildSupportMethod("SetStateMachine");

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodPreserved(function);
    }

    [Fact]
    public void KickoffShapeWithoutOwnerRole_DoesNotEnterSiblingImport()
    {
        var function = BuildKickoff();

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // ---- Builders ---------------------------------------------------------

    static IrFunction BuildSupportMethod(string name)
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new LoadField(
            new FieldRef(StateMachine, "<>t__builder", Builder),
            new LoadArgument(0, "this", StateMachine))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(
                StateMachine,
                "SideEffect",
                Void,
                [],
                HasThis: false),
            isVirtual: false,
            [])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            name,
            StateMachine,
            new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0),
            [],
            body)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
    }

    static IrFunction BuildKickoff()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Outer");

        var block = new Block(0);
        block.Add(new StoreField(
            new FieldRef(StateMachine, "<>t__builder", Builder),
            new LoadLocalAddress(0, StateMachine),
            new Call(
                new MethodRef(Builder, "Create", Builder, [], HasThis: false),
                isVirtual: false,
                [])));

        block.Add(new ExpressionStatement(new Call(
            new MethodRef(Builder, "Start", Void, [], HasThis: true),
            isVirtual: false,
            [])));

        block.Add(new Return(new LoadProperty(
            new MethodRef(Builder, "get_Task", Task, [], HasThis: true),
            new LoadLocalAddress(0, StateMachine),
            [])));

        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            "KickoffMethod",
            owner,
            new MethodSignature(Task, [], HasThis: false, GenericParameterCount: 0),
            [StateMachine],
            body);
    }

    static bool RunWithRecordingImport(IrFunction function)
    {
        var attempted = false;
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: _ =>
            {
                attempted = true;
                return null;
            });

        new ClassicAsyncReconstructionPass().Run(function, context);
        return attempted;
    }

    static void AssertSupportMethodPreserved(IrFunction function)
    {
        Assert.NotEmpty(function.Descendants.OfType<LoadField>());
        Assert.NotEmpty(function.Descendants.OfType<Call>());
    }
}
