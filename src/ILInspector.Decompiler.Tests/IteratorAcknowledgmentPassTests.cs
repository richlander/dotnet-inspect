using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IteratorAcknowledgmentPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void IteratorKickoff_ReplacesHandoffWithHonestMarker()
    {
        var function = Raised(nameof(CfgSampleClass.YieldTwo));

        // The misleading `return new <YieldTwo>d__0(-2);` handoff is gone.
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.DoesNotContain(function.Descendants.OfType<Return>(), _ => true);

        // An honest iterator marker stands in its place.
        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
        Assert.Contains("yield body", marker.Reason);
        Assert.Contains(">d__", marker.Reason);
    }

    [Fact]
    public void IteratorKickoff_CapsFidelityAtPartial()
    {
        Assert.Equal(DecompilationFidelity.Partial, Raised(nameof(CfgSampleClass.YieldTwo)).Fidelity);
    }

    [Fact]
    public void IteratorKickoff_RendersMarkerComment_NotAStub()
    {
        var output = Print(nameof(CfgSampleClass.YieldTwo));

        Assert.Contains("iterator", output);
        Assert.Contains("not reconstructed", output);
        // No plausible-but-meaningless state-machine construction stub.
        Assert.DoesNotContain("return new", output);
    }

    [Fact]
    public void ParameterizedIterator_IsAlsoAcknowledged()
    {
        var function = Raised(nameof(CfgSampleClass.YieldRange));

        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void NonIteratorReturningEnumerable_IsNotAcknowledged()
    {
        var function = Raised(nameof(CfgSampleClass.NotAnIterator));

        // No iterator marker: the method has no state machine to acknowledge.
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Contains("source", Print(nameof(CfgSampleClass.NotAnIterator)));
    }

    [Fact]
    public void StateMachineMoveNext_PreservesStateFieldWrites()
    {
        // #1011 stepper-audit invariant (state-machine scaffolds): the MoveNext body
        // is decompiled honestly (Partial) rather than reconstructed, and no pass may
        // delete a state-field write — including the `<>1__state = N` written right
        // before a yield/await suspend `return`, which looks dead within a single
        // MoveNext call but is observable on the next. Enumerated by shape (a
        // `>d__` state-machine type's MoveNext with state writes), so it covers
        // every iterator/async fixture in the test assembly without a brittle name.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        int audited = 0;
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (methodName != "MoveNext" || !typeName.Contains(">d__"))
                continue;
            int before = function.Descendants.OfType<StoreField>().Count(s => s.Field.Name.Contains("__state"));
            if (before == 0)
                continue;
            IrPasses.Run(function);
            function.CheckInvariant();
            int after = function.Descendants.OfType<StoreField>().Count(s => s.Field.Name.Contains("__state"));
            Assert.Equal(before, after);   // every state write survives — no illegal scaffold deletion
            audited++;
        }
        Assert.True(audited > 0, "expected at least one state-machine MoveNext fixture to audit");
    }

    [Fact]
    public void StateMachineNameLookalikeWithoutCompilerGeneratedMetadata_IsNotAcknowledged()
    {
        var function = BuildStateMachineNameLookalike();

        IrPasses.Run(function);

        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void SideEffectBeforeHandoff_IsNotAcknowledged_AndPreservesSideEffect()
    {
        // #1362: the kickoff body is not the narrow handoff scaffold — a side
        // effect runs before `return new <M>d__0(-2)`. Acknowledging would replace
        // the whole body with a marker and silently drop the call. The pass must
        // decline; the side effect and the (residual) handoff stay visible.
        var function = BuildKickoffWithLeadingSideEffect();

        IrPasses.Run(function);

        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "SideEffect");
        function.CheckInvariant();
    }

    [Fact]
    public void NarrowHandoffWithGeneratedEvidence_IsAcknowledged()
    {
        // Positive canary: the same generated state-machine evidence in a *pure*
        // handoff body (just `return new <M>d__0(-2)`) still acknowledges.
        var function = BuildNarrowKickoff();

        IrPasses.Run(function);

        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
        Assert.Empty(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void SideEffectInHandoffConstructorArgument_IsNotAcknowledged()
    {
        // #1362 (adversarial review): the side effect hides inside the handoff
        // construction's own argument — `return new <M>d__0(Side());` — instead of
        // a preceding statement. Acknowledging would still drop the call; the gate
        // must reject a non-constant/effectful constructor argument.
        var function = BuildKickoffWithSideEffectingConstructorArgument();

        IrPasses.Run(function);

        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "SideEffect");
        function.CheckInvariant();
    }

    static IrFunction BuildKickoffWithSideEffectingConstructorArgument()
    {
        var sideEffect = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "C"),
            "SideEffect",
            TypeRef.CoreLib("System", "Int32"),
            [],
            HasThis: false);
        return BuildKickoff(new Return(new NewObject(
            GeneratedKickoffConstructor(),
            [new Call(sideEffect, isVirtual: false, [])])));
    }

    static MethodRef GeneratedKickoffConstructor()
    {
        var stateMachine = TypeRef.Definition("Synthetic", "Samples", "C+<M>d__0");
        return new MethodRef(
            stateMachine,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Int32")],
            HasThis: false)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
    }

    static IrFunction BuildKickoff(params IrNode[] statements)
    {
        var enumerable = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            [TypeRef.CoreLib("System", "Int32")]);
        var block = new Block();
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "C"),
            new MethodSignature(enumerable, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction BuildNarrowKickoff()
        => BuildKickoff(new Return(new NewObject(
            GeneratedKickoffConstructor(),
            [new Constant(-2, TypeRef.CoreLib("System", "Int32"))])));

    static IrFunction BuildKickoffWithLeadingSideEffect()
    {
        var sideEffect = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "C"),
            "SideEffect",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: false);
        return BuildKickoff(
            new ExpressionStatement(new Call(sideEffect, isVirtual: false, [])),
            new Return(new NewObject(
                GeneratedKickoffConstructor(),
                [new Constant(-2, TypeRef.CoreLib("System", "Int32"))])));
    }

    static IrFunction BuildStateMachineNameLookalike()
    {
        var enumerable = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            [TypeRef.CoreLib("System", "Int32")]);
        var lookalike = TypeRef.Definition("Synthetic", "Samples", "Outer+<M>d__0");
        var ctor = new MethodRef(
            lookalike,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Int32")],
            HasThis: false)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.No,
        };

        var block = new Block();
        block.Add(new Return(new NewObject(ctor, [new Constant(-2, TypeRef.CoreLib("System", "Int32"))])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            new MethodSignature(enumerable, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
