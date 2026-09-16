using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

public class JoinTypeConflictTests : IDisposable
{
    readonly Stack<IDisposable> _disposables = new();

    public void Dispose()
    {
        while (_disposables.Count > 0)
            _disposables.Pop().Dispose();
    }

    IrFunction BuildSynthetic(byte[] il)
    {
        var source = MetadataSource.Open(typeof(object).Assembly.Location);
        _disposables.Push(source);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var method = new ImportedMethod(
            TypeRef.CoreLib("Synthetic", "T"), "M", signature,
            new MethodBody([.. il], MaxStack: 8, Locals: [], LocalNames: [], Handlers: []));
        return IrImporter.Build(source, method, GenericScope.Empty);
    }

    [Fact]
    public void TrailingLabeledReturn_IsNotTrimmedToADanglingLabel()
    {
        // br.s to the final 'return;' — the return is a branch target's only
        // statement, so trimming it would strand the label as invalid C#.
        var function = BuildSynthetic([0x2B, 0x00, 0x2A]);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("IL_0002:\nreturn;", output);
    }

    [Fact]
    public void JoinTypeConflict_BeforeJoinIsBuilt_MergesToHonestUnknown()
    {
        // ldc.i4.1; brtrue.s L1; ldc.i4.0; br.s J; L1: ldnull; J: pop; ret
        // Two forward edges carry int and object into J: pre-build conflict
        // merges to null (honest unknown), never a guessed type.
        var function = BuildSynthetic([0x17, 0x2D, 0x03, 0x16, 0x2B, 0x01, 0x14, 0x26, 0x2A]);

        // The join-type diagnostic records the disagreement; the only
        // consumer of the unknown value was a pop of a pure load, which
        // elides — so no unknown-typed expression survives into the tree
        // and fidelity stays Full while the diagnostic preserves the trace.
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("(join-type)", diagnostic.Message);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(),
            l => l.Parent is ExpressionStatement);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void DisjointStackCarryTargets_DoNotSharePositionSlot()
    {
        // Two unrelated forward targets each carry one stack value. They both use
        // stack position 0, but they are different entry stacks: sharing S_0 would
        // force the later long store into the earlier int slot (CS0266).
        var function = BuildSynthetic(
        [
            0x17,                         // ldc.i4.1
            0x2B, 0x00,                   // br.s IL_0003
            0x26,                         // pop
            0x21, 0x02, 0, 0, 0, 0, 0, 0, 0, // ldc.i8 2
            0x2B, 0x00,                   // br.s IL_000F
            0x26,                         // pop
            0x2A,                         // ret
        ]);

        var stores = function.Descendants.OfType<StoreStackSlot>().ToArray();

        Assert.Contains(stores, s => s is { Slot: 0, Value.ResultType.Name: "Int32" });
        Assert.Contains(stores, s => s is { Slot: 1, Value.ResultType.Name: "Int64" });
        Assert.DoesNotContain(stores, s => s is { Slot: 0, Value.ResultType.Name: "Int64" });

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("int S_0 = 1;", output);
        Assert.Contains("long S_1;", output);
        Assert.DoesNotContain("S_0 = 2L;", output);
        function.CheckInvariant();
    }

    IrFunction BuildSyntheticWithRegion(byte[] il, HandlerRegion region)
    {
        var source = MetadataSource.Open(typeof(object).Assembly.Location);
        _disposables.Push(source);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var method = new ImportedMethod(
            TypeRef.CoreLib("Synthetic", "T"), "M", signature,
            new MethodBody([.. il], MaxStack: 8, Locals: [], LocalNames: [], Handlers: [region]));
        return IrImporter.Build(source, method, GenericScope.Empty);
    }

    [Fact]
    public void Endfinally_NonEmptyStack_IsMalformed_StopsHonestly()
    {
        // try { leave } finally { ldc.i4.1; endfinally }  — ECMA requires an
        // empty stack at endfinally; the stray value must not import as Full.
        var function = BuildSyntheticWithRegion(
            [0xDE, 0x02, 0x17, 0xDC, 0x2A],
            new HandlerRegion(HandlerKind.Finally, 0, 2, 2, 2, 0, null));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("endfinally", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void Endfilter_MoreThanVerdict_IsMalformed_StopsHonestly()
    {
        // Filter code that never consumes the CLR-pushed exception: after
        // popping the verdict the exception remains — malformed per ECMA.
        var function = BuildSyntheticWithRegion(
            [0xDE, 0x06, 0x17, 0xFE, 0x11, 0x26, 0xDE, 0x00, 0x2A],
            new HandlerRegion(HandlerKind.Filter, 0, 2, 5, 3, 2, null));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("filter verdict", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void JoinTypeConflict_AfterJoinIsBuilt_StopsHonestly()
    {
        // ldc.i4.0; br.s J; J: pop; ret; (unreachable) ldnull; br.s J
        // The join is built with int before the object-carrying edge arrives;
        // already-emitted loads cannot be retyped, so the import stops.
        var function = BuildSynthetic([0x16, 0x2B, 0x00, 0x26, 0x2A, 0x14, 0x2B, 0xFB]);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedConstruct, diagnostic.Id);
        Assert.Contains("types disagree", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_ToCommonBase_ImportsAtFullFidelity()
    {
        // Two ternary branches carry JoinDerived and JoinBase into one slot.
        // The merge resolves to JoinBase — an ancestor of JoinDerived reached
        // by walking the same-assembly base chain — so the body imports Full
        // with no join-type diagnostic, unlike the unresolvable conflicts above.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedReferenceSlot))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_ToImplementedInterface_ImportsAtFullFidelity()
    {
        // One ternary arm is cast to IJoinShape; the other implements it. The
        // merge resolves to IJoinShape — an interface one arm already carries
        // and the other implements — so the body imports Full with no
        // join-type diagnostic, exercising the interface arm of the merge.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedInterfaceSlot))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_NullLiteralAdoptsCrossAssemblyReferenceType()
    {
        // The null arm of a ?. lowering is the null literal, not a hard object
        // value. When the other arm carries a cross-assembly reference type such
        // as System.Type, the join keeps that type instead of reporting
        // object-vs-Type as an unknown join.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullConditionalCrossAssemblyReference))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }
}
