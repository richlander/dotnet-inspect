using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    public static TheoryData<string> CallTargetParitySubjects() =>
    [
        FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath(),
        typeof(MethodDefinitionRequestSetTests).Assembly.Location,
        typeof(ImmutableArray).Assembly.Location,
        typeof(Enumerable).Assembly.Location,
    ];

    [Theory]
    [MemberData(nameof(CallTargetParitySubjects))]
    public void MethodQuerySource_CallTargetsMatchLibraryBodyAnalysis(
        string path)
    {
        LibraryCallGraphAnalysisResult graph =
            LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence))
                .CallGraph;

        ImmutableDictionary<CallSiteKey, MethodCallTarget> actual =
            ProbeCallTargets(path);

        AssertCallTargetParity(graph, actual);
    }

    [Fact]
    public void MethodQuerySource_CallTargetsAbsentWhenUndeclared()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([undeclared]));

        Assert.Same(
            SameModuleCallTargetCoverage.Empty,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.CallTargets);
        Assert.Same(
            SameModuleCallTargetCoverage.Empty,
            execution.ResultOf(undeclared)
                .SourceReceipt.Coverage.CallTargets);
    }

    [Fact]
    public void MethodQuerySource_CallTargetsRequireDeclaration()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                UndeclaredCallTargetProbe.Instance,
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(AcceptedPlan([undeclared])));

        Assert.Contains(
            "call-target support",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MethodQuerySource_CallTargetsReceiptExactMethodWork()
    {
        string path =
            FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle caller =
            FindMethod(reader, "Consumer", "UseClosedGeneric");

        // The oracle resolves the same call sites, in IL order, on a fresh lookup.
        SameModuleCallTargetLookup oracle =
            SameModuleCallTargetLookup.Create(reader);
        var expected = ImmutableDictionary.CreateBuilder<CallSiteKey, MethodCallTarget>();
        foreach ((CallSiteKey key, CallKind kind) in CallSites(pe, reader, caller))
        {
            expected.Add(
                key,
                oracle.Resolve(caller, kind, key.OperandToken));
        }

        MethodDefinitionSourceAssociation exact =
            Association(
                CallTargetProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(caller));
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodDefinitionSourceCoverage coverage =
            execution.ResultOf(exact).SourceReceipt.Coverage;

        Assert.NotEmpty(expected);
        Assert.Equal(
            expected.ToImmutable(),
            ResultOf<ImmutableDictionary<CallSiteKey, MethodCallTarget>>(
                    execution,
                    exact)
                .Value!);
        Assert.Contains(
            expected.Values,
            target => target is MethodCallTarget.CurrentModule);
        Assert.Equal(
            new SameModuleCallTargetCoverage(
                Constructed: true,
                oracle.RecordedWork,
                Queries: expected.Count,
                coverage.CallTargets.QueryingMethods),
            coverage.CallTargets);
        Assert.Equal(1, coverage.CallTargets.QueryingMethods.Count);
        Assert.True(coverage.CallTargets.QueryingMethods.Contains(caller));
        Assert.Equal(1, coverage.MethodsSelected.Count);
        Assert.Equal(0, coverage.ModuleLookupMethods.Count);

        // A call through a generic instantiation binds by signature, which
        // indexes the module's TypeDefs once and decodes candidate signatures.
        Assert.Equal(
            reader.TypeDefinitions.Count,
            coverage.CallTargets.QueryWork.TypeDefinitionRows);
        Assert.True(coverage.CallTargets.QueryWork.SignatureBytes > 0);
    }

    [Fact]
    public void MethodQuerySource_CallTargetsBindDefinitionTokensWithoutTypeIndex()
    {
        string path =
            FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle caller =
            FindMethod(reader, "Consumer", "UseDirect");

        MethodDefinitionSourceAssociation exact =
            Association(
                CallTargetProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(caller));
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        SameModuleCallTargetCoverage coverage =
            execution.ResultOf(exact).SourceReceipt.Coverage.CallTargets;
        ImmutableDictionary<CallSiteKey, MethodCallTarget> targets =
            ResultOf<ImmutableDictionary<CallSiteKey, MethodCallTarget>>(
                    execution,
                    exact)
                .Value!;

        Assert.All(
            targets,
            target =>
            {
                Assert.Equal(
                    HandleKind.MethodDefinition,
                    MetadataTokens.EntityHandle(target.Key.OperandToken).Kind);
                Assert.IsType<MethodCallTarget.CurrentModule>(target.Value);
            });
        Assert.NotEmpty(targets);
        Assert.Equal(targets.Count, coverage.Queries);
        Assert.Equal(0, coverage.QueryWork.TypeDefinitionRows);

        // Binding by token decodes no signature; it reads only each
        // operand's signature-depth guard.
        Assert.Equal(
            targets.Keys.Sum(key =>
                (long)reader.GetBlobReader(
                        reader.GetMethodDefinition(
                                (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                                    key.OperandToken))
                            .Signature)
                    .Length),
            coverage.QueryWork.SignatureBytes);
    }

    [Fact]
    public void MethodQuerySource_CallTargetsSharedOnceAcrossLanes()
    {
        string path =
            FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath();
        MethodDefinitionSourceAssociation first =
            Association(
                CallTargetProbe.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation second =
            Association(
                CallTargetProbe.Second,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([first, second, undeclared]), path);
        SameModuleCallTargetCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.CallTargets;
        SameModuleCallTargetCoverage firstCoverage =
            execution.ResultOf(first).SourceReceipt.Coverage.CallTargets;
        SameModuleCallTargetCoverage secondCoverage =
            execution.ResultOf(second).SourceReceipt.Coverage.CallTargets;
        ImmutableDictionary<CallSiteKey, MethodCallTarget> firstTargets =
            ResultOf<ImmutableDictionary<CallSiteKey, MethodCallTarget>>(
                    execution,
                    first)
                .Value!;

        Assert.Equal(
            firstTargets,
            ResultOf<ImmutableDictionary<CallSiteKey, MethodCallTarget>>(
                    execution,
                    second)
                .Value!);

        // One lookup, constructed by the first lane's first query.
        Assert.True(physical.Constructed);
        Assert.True(firstCoverage.Constructed);
        Assert.False(secondCoverage.Constructed);

        // Each lane receipts its own queries. The TypeDef index and candidate
        // signatures are built once, by the query that first needed them.
        Assert.Equal(firstTargets.Count, firstCoverage.Queries);
        Assert.Equal(firstTargets.Count, secondCoverage.Queries);
        Assert.Equal(2 * firstTargets.Count, physical.Queries);
        Assert.Equal(
            firstCoverage.QueryWork.Plus(secondCoverage.QueryWork),
            physical.QueryWork);
        Assert.True(firstCoverage.QueryWork.TypeDefinitionRows > 0);
        Assert.Equal(0, secondCoverage.QueryWork.TypeDefinitionRows);
        Assert.Same(
            SameModuleCallTargetCoverage.Empty,
            execution.ResultOf(undeclared).SourceReceipt.Coverage.CallTargets);
    }

    internal static void AssertCallTargetParity(
        LibraryCallGraphAnalysisResult graph,
        ImmutableDictionary<CallSiteKey, MethodCallTarget> actual)
    {
        Assert.NotEmpty(graph.DirectCalls);
        var physicalBodies = new HashSet<int>();
        foreach (DirectCall call in graph.DirectCalls)
        {
            CallSiteKey key = CallTargetProbe.Key(call);
            physicalBodies.Add(key.EvidenceMethodToken);
            Assert.True(
                actual.TryGetValue(key, out MethodCallTarget? target),
                $"No source target for {key}.");
            Assert.Equal(CallTargetProbe.Expected(graph, call), target);
        }

        // The source resolves no call site the analysis did not publish in
        // the bodies it analyzed.
        Assert.Equal(
            graph.DirectCalls.Length,
            actual.Count(site =>
                physicalBodies.Contains(site.Key.EvidenceMethodToken)));
    }

    internal static ImmutableDictionary<CallSiteKey, MethodCallTarget> ProbeCallTargets(
        string path)
    {
        MethodDefinitionSourceAssociation association =
            Association(CallTargetProbe.Instance, ProducerTerminal.Rows);
        return ResultOf<ImmutableDictionary<CallSiteKey, MethodCallTarget>>(
                ExecuteAt(AcceptedPlan([association]), path),
                association)
            .Value!;
    }

    internal static ImmutableDictionary<CallSiteKey, MethodCallTarget> ProbeCallTargets(
        ImmutableArray<byte> image)
    {
        MethodDefinitionSourceAssociation association =
            Association(CallTargetProbe.Instance, ProducerTerminal.Rows);
        return ResultOf<ImmutableDictionary<CallSiteKey, MethodCallTarget>>(
                Execute(AcceptedPlan([association]), image),
                association)
            .Value!;
    }

    /// <summary>The call-target receipt of one exact caller in a synthetic image.</summary>
    internal static SameModuleCallTargetCoverage ProbeCallTargetCoverage(
        ImmutableArray<byte> image,
        MethodDefinitionHandle caller)
    {
        MethodDefinitionSourceAssociation association =
            Association(
                CallTargetProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(caller));
        return Execute(AcceptedPlan([association]), image)
            .ResultOf(association)
            .SourceReceipt.Coverage.CallTargets;
    }

    static MethodDefinitionSourceRequestSetExecution ExecuteAt(
        MethodDefinitionSourceRequestSetPlan plan,
        string path)
    {
        using PdbContext context = PdbContext.OpenMetadataOnly(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        AssemblyAnalysisRequestSetOperation operation =
            AssemblyAnalysisRequestSetOperation.Create(path, plan);
        return Assert.IsType<
                AssemblyAnalysisRequestSetServiceResult.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access =>
                            AssemblyAnalysisService.Instance.Execute(
                                operation,
                                access)))
            .Execution
            .MethodDefinitionExecution();
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName) =>
        Assert.Single(
            reader.MethodDefinitions,
            handle =>
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                return reader.StringComparer.Equals(method.Name, methodName)
                    && reader.StringComparer.Equals(
                        reader.GetTypeDefinition(method.GetDeclaringType()).Name,
                        typeName);
            });

    static IEnumerable<(CallSiteKey Key, CallKind Kind)> CallSites(
        PEReader pe,
        MetadataReader reader,
        MethodDefinitionHandle method)
    {
        int token = MetadataTokens.GetToken(method);
        MethodBodyBlock body = pe.GetMethodBody(
            reader.GetMethodDefinition(method).RelativeVirtualAddress);
        foreach (var instruction
            in Instructions.InstructionDecoder.Decode(body.GetILBytes()!))
        {
            CallKind? kind = instruction.OpCode switch
            {
                ILOpCode.Call => CallKind.Call,
                ILOpCode.Callvirt => CallKind.CallVirtual,
                ILOpCode.Newobj => CallKind.NewObject,
                ILOpCode.Ldftn => CallKind.LoadFunction,
                ILOpCode.Ldvirtftn => CallKind.LoadVirtualFunction,
                ILOpCode.Calli => CallKind.CallIndirect,
                _ => null,
            };
            if (kind is { } callKind)
            {
                yield return (
                    new CallSiteKey(
                        token,
                        instruction.Offset,
                        checked((int)instruction.OperandValue)),
                    callKind);
            }
        }
    }

    sealed class UndeclaredCallTargetProbe
        : MethodDefinitionProducer<int, int, int>
    {
        UndeclaredCallTargetProbe()
            : base(
                "UndeclaredCallTargetProbe",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Body)
        {
        }

        public static UndeclaredCallTargetProbe Instance { get; } = new();

        internal override int Visit(scoped MethodDefinitionView view)
        {
            _ = view.ResolveCallTarget(
                CallKind.Call,
                MetadataTokens.GetToken(view.MethodHandle));
            return 1;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }
}
