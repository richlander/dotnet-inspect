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
    public void ReferenceJoin_CrossAssemblyBaseChain_TypesTheSlotAndProvesTheWidening()
    {
        // Both arms are CoreLib types the test assembly only references:
        // UTF8Encoding's base chain reaches Encoding through the metadata
        // context, so the join types Encoding (the arm instance, not a
        // decoded copy) and records the UTF8Encoding -> Encoding widening.
        // The CoreLib base chain is reachable only through a metadata context
        // that resolves the trusted platform assemblies, as the CLI's package
        // resolver and the harness corpus context do.
        var metadata = ILInspector.DecompilerHarness.CorpusMetadata.Create([typeof(CfgSampleClass).Assembly.Location]);
        _disposables.Push(metadata);
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, context: metadata);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedCrossAssemblyBaseSlot))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var load = Assert.Single(
            function.Descendants.OfType<LoadStackSlot>(),
            l => l.Type is { Namespace: "System.Text", Name: "Encoding" });
        Assert.Contains(
            function.ProvenReferenceWidenings,
            w => w.From is { Namespace: "System.Text", Name: "UTF8Encoding" }
                && w.To.Equals(load.Type));
        function.CheckInvariant();

        // Materialization admits the UTF8Encoding store into the Encoding slot
        // on the strength of that widening: one typed local, no residual slot.
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.DoesNotContain(function.Descendants, n => n is StoreStackSlot or LoadStackSlot);
        Assert.Contains(function.Locals, l => l is { Namespace: "System.Text", Name: "Encoding" });
    }

    [Fact]
    public void ReferenceJoin_CrossAssemblyInterfaceImplementation_TypesTheCoalesce()
    {
        // IEqualityComparer<string> ?? EqualityComparer<string>.Default: the
        // interface arm is a cross-assembly generic instance, the class arm
        // implements it in another assembly. The merge must resolve to the
        // interface through the metadata context instead of leaving an
        // untyped slot the printer could only spell as `var`.
        var metadata = ILInspector.DecompilerHarness.CorpusMetadata.Create([typeof(CfgSampleClass).Assembly.Location]);
        _disposables.Push(metadata);
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, context: metadata);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CoalescedCrossAssemblyInterface))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), l => l.Type is null);
        Assert.Contains(
            function.ProvenReferenceWidenings,
            w => w.From.ToDisplayString() == "EqualityComparer<string>"
                && w.To.ToDisplayString() == "IEqualityComparer<string>");
        function.CheckInvariant();

        string output = CSharpPrinter.PrintRaised(function).Output!;
        Assert.Contains("comparer ?? EqualityComparer<string>.Default", output);
        Assert.DoesNotContain("var S_", output);
    }

    [Fact]
    public void ReferenceJoin_FacadeForwardedPair_DisjointnessStaysSameAssemblyAndTheCascadeStaysAnIf()
    {
        // XmlTextReader derives from XmlReader; both are referenced through the
        // System.Xml.ReaderWriter facade and defined in System.Private.Xml.
        // The cross-assembly reach belongs to the join merge only:
        // AreProvablyDisjoint keeps its same-assembly contract (a foreign base
        // yields false, never a false "disjoint"), so PatternSwitchExpressionPass
        // must not fold the guarded cascade into a switch expression that would
        // route a guard-failing XmlTextReader to the XmlReader arm.
        var metadata = ILInspector.DecompilerHarness.CorpusMetadata.Create([typeof(CfgSampleClass).Assembly.Location]);
        _disposables.Push(metadata);
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, context: metadata);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GuardedXmlReaderCascade))!;

        var textReader = Assert.Single(function.TypeShapes.Keys, t => t is { Namespace: "System.Xml", Name: "XmlTextReader" });
        var reader = Assert.Single(function.TypeShapes.Keys, t => t is { Namespace: "System.Xml", Name: "XmlReader" });
        Assert.False(source.AreProvablyDisjoint(textReader, reader));
        Assert.False(source.AreProvablyDisjoint(reader, textReader));

        var result = CSharpPrinter.PrintRaised(function);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain("switch", result.Output);
        Assert.Contains("return -1;", result.Output);
    }

    [Fact]
    public void ReferenceJoin_CrossAssemblySiblingArms_TypeTheAncestorUnderTheModuleReference()
    {
        // XElement and XComment are siblings under XNode, all three declared in
        // System.Xml.XDocument (reached through its facade). The function
        // references XNode itself (the local), so the merge types the join as
        // the module's own XNode reference: not ambiguous, Reference shape, and
        // both widenings proven.
        var metadata = ILInspector.DecompilerHarness.CorpusMetadata.Create([typeof(CfgSampleClass).Assembly.Location]);
        _disposables.Push(metadata);
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, context: metadata);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SiblingXNodeJoin))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        var load = Assert.Single(
            function.Descendants.OfType<LoadStackSlot>(),
            l => l.Type is { Namespace: "System.Xml.Linq", Name: "XNode" });
        Assert.DoesNotContain(function.AmbiguousTypeFacts, t => t.Equals(load.Type));
        Assert.Equal(TypeShape.Reference, function.TypeShapes[load.Type!]);
        Assert.Contains(function.ProvenReferenceWidenings, w => w.From is { Name: "XElement" } && w.To.Equals(load.Type));
        Assert.Contains(function.ProvenReferenceWidenings, w => w.From is { Name: "XComment" } && w.To.Equals(load.Type));
        function.CheckInvariant();
    }

    [Fact]
    public void ReferenceJoin_CrossAssemblySiblingArms_UnreferencedAncestorStaysUnknown()
    {
        // XAttribute and XComment meet only at XObject, which this module never
        // references. The identity rule declines the merge instead of admitting
        // a TypeRef none of the function's rows share, so the join stays an
        // honest unknown with its diagnostic.
        var metadata = ILInspector.DecompilerHarness.CorpusMetadata.Create([typeof(CfgSampleClass).Assembly.Location]);
        _disposables.Push(metadata);
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, context: metadata);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.UnreferencedAncestorJoin))!;

        Assert.Contains(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)") && (d.Message ?? "").Contains("XAttribute"));
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(), l => l.Type is { Name: "XObject" });
        Assert.DoesNotContain(function.ProvenReferenceWidenings, w => w.To is { Name: "XObject" });
    }

    [Fact]
    public void ReferenceJoin_NullLiteralArm_PublishesNoWidening()
    {
        // `c ? null : s` adopts string for the null arm. Adoption is not a
        // proven conversion from System.Object, so nothing may be published.
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        _disposables.Push(source);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullArmJoin))!;

        Assert.DoesNotContain(function.Diagnostics, d => (d.Message ?? "").Contains("(join-type)"));
        Assert.Empty(function.ProvenReferenceWidenings);
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
