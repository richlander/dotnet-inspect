using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial near-miss matrix for ClassicAsyncReconstructionPass (#1290).
//
// The pass raises runtime-async=off async kickoffs back to async bodies. It has
// two recognition entry points, and each leans on compiler-reserved evidence
// that user-authored C# cannot forge:
//
//   * the support-method path (TryAcknowledgeSupportMethod) trusts the
//     `<...>d__N` state-machine type-name shape PLUS the
//     DeclaringTypeCompilerGenerated metadata fact PLUS a `<>t__builder`
//     field reference;
//   * the kickoff path (TryGetKickoff) trusts the `<>t__builder` builder-field
//     name stored into a `<...>d__N` state-machine *local* by address, alongside
//     shape signals (a `Start` call and a `.Task`-named return). Those two shape
//     signals are matched by name only and are NOT independently correlated to
//     the builder; the real builder/MoveNext correlation is enforced downstream,
//     because the pass only reconstructs after importing and structurally
//     recognizing the named state machine's sibling MoveNext.
//
// The `<...>d__N` type name and the `<>t__builder` field name are unspeakable in
// C# source, so the trust line is "compiler-reserved names + a metadata fact",
// not an attacker-forgeable shape. Each test below flips exactly one recognition
// discriminator and asserts the lookalike stays lowered (declined), which pins
// the matcher so a future loosening that drops that discriminator fails loudly.
// These are synthetic-IR pins; the positive reconstruction shapes are covered by
// the Fixtures.ClassicAsync overlay referenced in AwaitRecoveryFacts.
public class ClassicAsyncReconstructionPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Task = TypeRef.CoreLib("System.Threading.Tasks", "Task");

    // Compiler-reserved state-machine type name (`<...>d__N`).
    static readonly TypeRef StateMachine = TypeRef.Definition("Synthetic", "Samples", "Outer+<Fake>d__0");

    // A compiler-generated type whose name is NOT state-machine-shaped (no `>d__`);
    // models a display class or other generated helper.
    static readonly TypeRef NonStateMachine = TypeRef.Definition("Synthetic", "Samples", "Outer+<Fake>e__0");

    // Has the `>d__` infix but lacks the leading `<`; isolates IsStateMachineType's
    // leading-angle-bracket clause.
    static readonly TypeRef NoLeadingAngleType = TypeRef.Definition("Synthetic", "Samples", "Outer+Fake>d__0");

    static readonly TypeRef Builder = TypeRef.Definition("Synthetic", "Samples", "BuilderLike");

    // ---- Support-method acknowledgment path -------------------------------

    [Fact]
    public void CompilerGeneratedMoveNext_IsAcknowledged()
    {
        var function = BuildSupportMethod("MoveNext");

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        Assert.True(IsAcknowledgedEmptyReturn(function));
        Assert.Empty(function.Descendants.OfType<LoadField>());
        Assert.Empty(function.Descendants.OfType<Call>());
    }

    [Fact]
    public void CompilerGeneratedSetStateMachine_IsAcknowledged()
    {
        var function = BuildSupportMethod("SetStateMachine");

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        Assert.True(IsAcknowledgedEmptyReturn(function));
    }

    // Discriminator: declaring-type compiler-generated metadata fact.
    [Fact]
    public void SupportMethodLookalikeWithoutCompilerGeneratedMetadata_IsNotAcknowledged()
    {
        var function = BuildSupportMethod("MoveNext", declaringTypeGenerated: MetadataFactState.No);

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodDeclined(function);
    }

    // Discriminator: method name. A generated state-machine method that is not
    // MoveNext/SetStateMachine must not be hollowed out.
    [Fact]
    public void WrongMethodName_IsNotAcknowledged()
    {
        var function = BuildSupportMethod("Dispose");

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodDeclined(function);
    }

    // Discriminator: declaring-type name shape (`<...>d__N`). Right metadata, right
    // builder field, but the declaring type is not a state-machine name.
    [Fact]
    public void NonStateMachineDeclaringType_IsNotAcknowledged()
    {
        var function = BuildSupportMethod("MoveNext", declaringType: NonStateMachine);

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodDeclined(function);
    }

    // Discriminator: the leading `<` of the state-machine name. A type with the
    // `>d__` infix but no leading `<` (unspeakable-name shape broken) must not match.
    [Fact]
    public void DeclaringTypeWithoutLeadingAngleBracket_IsNotAcknowledged()
    {
        var function = BuildSupportMethod("MoveNext", declaringType: NoLeadingAngleType);

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodDeclined(function);
    }

    // Discriminator: `<>t__builder` field reference. A state-machine MoveNext with
    // builder calls but no builder-field reference (only `<>1__state`) must not be
    // acknowledged just because the declaring type name and metadata match.
    [Fact]
    public void WithoutBuilderFieldReference_IsNotAcknowledged()
    {
        var function = BuildSupportMethod("MoveNext", builderFieldName: "<>1__state");

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        AssertSupportMethodDeclined(function);
    }

    // ---- Kickoff path -----------------------------------------------------
    //
    // The kickoff path needs the cross-method import seam to pull in the sibling
    // MoveNext. We drive the pass with a recording import delegate: if the kickoff
    // shape is recognized, the pass reaches the import (records `attempted`); if a
    // discriminator is off, TryGetKickoff declines before any import. Returning
    // null from the import keeps the body lowered, so recognition is observed
    // purely through whether the import was attempted.

    [Fact]
    public void WellFormedKickoff_ReachesSiblingImport()
    {
        var function = BuildKickoff();

        var attempted = RunWithRecordingImport(function);

        Assert.True(attempted);
    }

    // Discriminator: the `Start` call.
    [Fact]
    public void KickoffMissingStartCall_IsNotRecognized()
    {
        var function = BuildKickoff(includeStart: false);

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // Discriminator: the builder `.Task` return. A kickoff that returns a different
    // property is not a runtime-async kickoff.
    [Fact]
    public void KickoffReturningNonTaskProperty_IsNotRecognized()
    {
        var function = BuildKickoff(returnPropertyName: "get_Result");

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // Discriminator: the state-machine *local* type. A kickoff that initializes a
    // similar (uncorrelated) struct whose type name is not `<...>d__N` must not be
    // treated as a state-machine kickoff.
    [Fact]
    public void KickoffInitializingNonStateMachineStruct_IsNotRecognized()
    {
        var function = BuildKickoff(stateMachineLocal: NonStateMachine);

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // Discriminator: the single-block kickoff shape. A multi-block body is not the
    // trivial kickoff the pass reconstructs.
    [Fact]
    public void KickoffWithMultipleBlocks_IsNotRecognized()
    {
        var function = BuildKickoff(extraBlock: true);

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // Discriminator: the `<>t__builder` builder-field name. A builder-looking store
    // under a different field name is not the compiler-reserved builder.
    [Fact]
    public void KickoffWithNonBuilderFieldName_IsNotRecognized()
    {
        var function = BuildKickoff(builderFieldName: "<>u__awaiter");

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // Discriminator: the builder store must target a state-machine local by address
    // (`LoadLocalAddress`). A `<>t__builder` store whose instance is an argument,
    // not a local address, is not the kickoff's state-machine init.
    [Fact]
    public void KickoffBuilderStoreNotOnLocalAddress_IsNotRecognized()
    {
        var function = BuildKickoff(builderStoreOnLocal: false);

        var attempted = RunWithRecordingImport(function);

        Assert.False(attempted);
    }

    // ---- Builders ---------------------------------------------------------

    static IrFunction BuildSupportMethod(
        string name,
        MetadataFactState declaringTypeGenerated = MetadataFactState.Yes,
        TypeRef? declaringType = null,
        string builderFieldName = "<>t__builder")
    {
        var owner = declaringType ?? StateMachine;

        var block = new Block(0);
        block.Add(new ExpressionStatement(new LoadField(
            new FieldRef(owner, builderFieldName, Builder),
            new LoadArgument(0, "this", owner))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(owner, "SideEffect", Void, [], HasThis: false),
            isVirtual: false,
            [])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            name,
            owner,
            new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0),
            [],
            body)
        {
            DeclaringTypeCompilerGenerated = declaringTypeGenerated,
        };
    }

    static IrFunction BuildKickoff(
        bool includeStart = true,
        string returnPropertyName = "get_Task",
        TypeRef? stateMachineLocal = null,
        bool extraBlock = false,
        string builderFieldName = "<>t__builder",
        bool builderStoreOnLocal = true)
    {
        var localType = stateMachineLocal ?? StateMachine;
        var owner = TypeRef.Definition("Synthetic", "Samples", "Outer");

        IrExpression builderInstance = builderStoreOnLocal
            ? new LoadLocalAddress(0, localType)
            : new LoadArgument(0, "this", owner);

        var block = new Block(0);
        block.Add(new StoreField(
            new FieldRef(localType, builderFieldName, Builder),
            builderInstance,
            new Call(
                new MethodRef(Builder, "Create", Builder, [], HasThis: false),
                isVirtual: false,
                [])));

        if (includeStart)
        {
            block.Add(new ExpressionStatement(new Call(
                new MethodRef(Builder, "Start", Void, [], HasThis: true),
                isVirtual: false,
                [])));
        }

        block.Add(new Return(new LoadProperty(
            new MethodRef(Builder, returnPropertyName, Task, [], HasThis: true),
            new LoadLocalAddress(0, localType),
            [])));

        var body = new BlockContainer();
        body.Add(block);
        if (extraBlock)
        {
            var tail = new Block(1);
            tail.Add(new Return(null));
            body.Add(tail);
        }

        return new IrFunction(
            "KickoffMethod",
            owner,
            new MethodSignature(Task, [], HasThis: false, GenericParameterCount: 0),
            [localType],
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

    static void AssertSupportMethodDeclined(IrFunction function)
    {
        Assert.False(IsAcknowledgedEmptyReturn(function));
        Assert.NotEmpty(function.Descendants.OfType<Call>());
    }

    static bool IsAcknowledgedEmptyReturn(IrFunction function)
        => function.Body.Blocks is [var block]
            && block.Children is [Return { Value: null }];
}
