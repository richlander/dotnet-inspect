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
    public void IteratorKickoff_WithPrecedingSideEffect_DeclinesAndPreservesSideEffect()
    {
        // A user side effect before the iterator handoff is observable work. The pass must
        // not collapse the whole body to a marker (which would drop it); it declines and
        // leaves the lowered body visible (#1362).
        var function = BuildKickoffWithSideEffect();

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "SideEffect");
        // The handoff construction is still present (lowered), not replaced by a marker.
        Assert.Single(function.Descendants.OfType<NewObject>());
    }

    static IrFunction BuildKickoffWithSideEffect()
    {
        var enumerable = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            [TypeRef.CoreLib("System", "Int32")]);
        var stateMachine = TypeRef.Definition("Synthetic", "Samples", "Outer+<M>d__0");
        var ctor = new MethodRef(
            stateMachine,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Int32")],
            HasThis: false)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
        var sideEffect = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            "SideEffect",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: false);

        var block = new Block();
        block.Add(new ExpressionStatement(new Call(sideEffect, isVirtual: false, [])));
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

    [Fact]
    public void IteratorKickoff_WithSideEffectingHandoffArgument_DeclinesAndPreservesSideEffect()
    {
        // The body is a single `return new <M>d__0(SideEffect())` — narrow by shape, but the
        // construction consumes a side-effecting call. Acknowledging would drop that call, so
        // the pass must decline (#1362, GPT-5.5 adversarial finding).
        var function = BuildKickoffWithSideEffectingArgument();

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "SideEffectInt");
    }

    static IrFunction BuildKickoffWithSideEffectingArgument()
    {
        var enumerable = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            [TypeRef.CoreLib("System", "Int32")]);
        var stateMachine = TypeRef.Definition("Synthetic", "Samples", "Outer+<M>d__0");
        var ctor = new MethodRef(
            stateMachine,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Int32")],
            HasThis: false)
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.Yes,
        };
        var sideEffect = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            "SideEffectInt",
            TypeRef.CoreLib("System", "Int32"),
            [],
            HasThis: false);

        var block = new Block();
        block.Add(new Return(new NewObject(ctor, [new Call(sideEffect, isVirtual: false, [])])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Outer"),
            new MethodSignature(enumerable, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
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
