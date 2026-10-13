using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Instructions;
using QuerySpace.Composition;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    public static TheoryData<string> CallsParitySubjects() =>
        CallTargetParitySubjects();

    [Theory]
    [MemberData(nameof(CallsParitySubjects))]
    public void MethodQuerySource_CallsMatchLibraryBodyAnalysis(
        string path)
    {
        LibraryCallGraphAnalysisResult graph =
            LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence))
                .CallGraph;
        MethodDefinitionSourceAssociation association =
            Association(
                MethodCallsProbe.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);
        ImmutableDictionary<CallSiteKey, MethodCallSite> actual =
            CompletedValue<
                ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                    execution,
                    association);

        Assert.NotEmpty(graph.DirectCalls);
        var physicalBodies = new HashSet<int>();
        foreach (DirectCall call in graph.DirectCalls)
        {
            CallSiteKey key = CallTargetProbe.Key(call);
            physicalBodies.Add(key.EvidenceMethodToken);
            Assert.True(
                actual.TryGetValue(key, out MethodCallSite? site),
                $"No source call for {key}.");
            Assert.Equal(call.Kind, site.Kind);
            Assert.Equal(call.Callee, site.Callee);
            Assert.Equal(call.ReturnAddress, site.ReturnAddress);
            Assert.Equal(CallTargetProbe.Expected(graph, call), site.Target);
        }

        Assert.Equal(
            graph.DirectCalls.Length,
            actual.Count(site =>
                physicalBodies.Contains(
                    site.Key.EvidenceMethodToken)));
    }

    [Fact]
    public void MethodQuerySource_CallsAbsentWhenUndeclared()
    {
        MethodDefinitionSourceAssociation count =
            Association(
                MethodCallCountProducer.CallSites,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([count]));

        Assert.Same(
            MethodCallMetadataCoverage.Empty,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.Calls);
        Assert.Same(
            MethodCallMetadataCoverage.Empty,
            execution.ResultOf(count)
                .SourceReceipt.Coverage.Calls);
        Assert.Same(
            SameModuleCallTargetCoverage.Empty,
            execution.ResultOf(count)
                .SourceReceipt.Coverage.CallTargets);
    }

    [Fact]
    public void MethodQuerySource_CallsRequireDeclaration()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                UndeclaredMethodCallsProbe.Instance,
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(AcceptedPlan([undeclared])));

        Assert.Contains(
            "Method call support",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MethodQuerySource_CallsReceiptExactMethodWork()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            FindMethod(
                reader,
                nameof(MethodCallMetadataProbeTarget),
                nameof(MethodCallMetadataProbeTarget.Exercise));
        MethodDefinitionSourceAssociation exact =
            Association(
                MethodCallMetadataProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodDefinitionSourceRequestResult result =
            execution.ResultOf(exact);
        MethodCallMetadataProbeFact fact =
            Assert.Single(
                CompletedValue<
                    ImmutableArray<MethodCallMetadataProbeFact>>(
                        execution,
                        exact));
        MethodCallMetadataCoverage coverage =
            result.SourceReceipt.Coverage.Calls;

        Assert.Equal(
            System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method),
            fact.MethodToken);
        Assert.NotEmpty(fact.Calls.Sites);
        Assert.NotEmpty(fact.Locals.Types);
        Assert.NotEmpty(fact.Fields);
        Assert.Contains(
            fact.Fields,
            field => field
                .Operand is MethodFieldOperand.Resolved
                {
                    CurrentModuleDefinitionToken: not null,
                });
        Assert.True(coverage.Constructed);
        Assert.Equal(1, coverage.CallReads);
        Assert.Equal(1, coverage.LocalSignatureReads);
        Assert.Equal(fact.Fields.Length, coverage.FieldOperandReads);
        Assert.Equal(1, coverage.QueryingMethods.Count);
        Assert.True(coverage.QueryingMethods.Contains(method));
        Assert.Equal(
            fact.Calls.Sites.Length,
            coverage.Work.CallSites);
        Assert.True(coverage.Work.MethodOperands > 0);
        Assert.Equal(
            fact.Fields
                .Select(static field => field.OperandToken)
                .Distinct()
                .Count(),
            coverage.Work.FieldOperands);
        Assert.Equal(1, coverage.Work.LocalSignatures);
        Assert.True(coverage.Work.SignatureBytes > 0);
        Assert.Equal(
            fact.Calls.Sites.Length,
            result.SourceReceipt.Coverage.CallTargets.Queries);
        Assert.Equal(0, result.SourceReceipt.ModuleLookups);
        Assert.True(
            result.SourceReceipt.DeclaredLayers.HasFlag(
                MethodDefinitionLayers.Calls));
        Assert.True(
            result.SourceReceipt.DeclaredLayers.HasFlag(
                MethodDefinitionLayers.Body));
        Assert.True(
            result.SourceReceipt.DeclaredLayers.HasFlag(
                MethodDefinitionLayers.CallTargets));
    }

    [Fact]
    public void MethodQuerySource_ClosedGenericFieldTypeIsInstantiated()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            FindMethod(
                reader,
                nameof(MethodCallMetadataProbeTarget),
                nameof(MethodCallMetadataProbeTarget.ReadClosedGenericField));
        MethodDefinitionSourceAssociation exact =
            Association(
                MethodCallMetadataProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodCallMetadataProbeFact fact =
            Assert.Single(
                CompletedValue<
                    ImmutableArray<MethodCallMetadataProbeFact>>(
                        execution,
                        exact));
        ImmutableArray<MethodFieldOperandProbe> fields =
        [
            .. fact.Fields.Where(field =>
                field.Operand is MethodFieldOperand.Resolved
                {
                    Name: nameof(
                        MethodCallMetadataProbeClosedGenericFieldBox<string>
                            .Item),
                }),
        ];
        TypeRef expected = TypeRef.CoreLib("System", "String");

        Assert.NotEmpty(fields);
        Assert.All(
            fields,
            field =>
            {
                MethodFieldOperand.Resolved resolved =
                    Assert.IsType<MethodFieldOperand.Resolved>(
                        field.Operand);
                Assert.Equal(expected, resolved.FieldType);
                Assert.Equal(
                    [expected],
                    resolved.DeclaringType.TypeArguments);
            });
        MethodCallMetadataCoverage coverage =
            execution.ResultOf(exact).SourceReceipt.Coverage.Calls;
        Assert.Equal(fact.Fields.Length, coverage.FieldOperandReads);
        Assert.Equal(
            fact.Fields
                .Select(static field => field.OperandToken)
                .Distinct()
                .Count(),
            coverage.Work.FieldOperands);
    }

    [Fact]
    public void MethodQuerySource_CalleeIdentityPreservesPlatformContractHit()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            FindMethod(
                reader,
                nameof(UnsafeEvidenceFixtures),
                nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
        MethodDefinitionSourceAssociation exact =
            Association(
                MethodCallsProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodCallSite site = Assert.Single(
            CompletedValue<
                    ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                    execution,
                    exact)
                .Values);
        DirectCall legacy = Assert.Single(
            LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence))
                .CallGraph.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                == MetadataTokens.GetToken(method));

        Assert.Equal(legacy.Callee, site.Callee);
        Assert.True(
            PlatformCallerUnsafeContracts.Embedded.Contains(
                legacy.Callee));
        Assert.True(
            PlatformCallerUnsafeContracts.Embedded.Contains(
                site.Callee));
    }

    [Fact]
    public void MethodQuerySource_CallsAreDecodedOnceAcrossLanes()
    {
        string path =
            FixtureCatalog.Get(
                    FixtureIds.AnalysisCalleeResolution)
                .AssemblyPath();
        MethodDefinitionSourceAssociation first =
            Association(
                MethodCallsProbe.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation second =
            Association(
                MethodCallsProbe.Second,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(
                AcceptedPlan([first, second]),
                path);
        MethodCallMetadataCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.Calls;
        MethodCallMetadataCoverage firstCoverage =
            execution.ResultOf(first)
                .SourceReceipt.Coverage.Calls;
        MethodCallMetadataCoverage secondCoverage =
            execution.ResultOf(second)
                .SourceReceipt.Coverage.Calls;
        ImmutableDictionary<CallSiteKey, MethodCallSite> firstCalls =
            CompletedValue<
                ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                    execution,
                    first);

        Assert.NotEmpty(firstCalls);
        Assert.Equal(
            firstCalls,
            CompletedValue<
                ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                    execution,
                    second));
        Assert.True(physical.Constructed);
        Assert.True(firstCoverage.Constructed);
        Assert.False(secondCoverage.Constructed);
        Assert.Equal(
            firstCoverage.CallReads
                + secondCoverage.CallReads,
            physical.CallReads);
        Assert.Equal(
            firstCoverage.QueryingMethods.Count,
            firstCoverage.CallReads);
        Assert.Equal(
            secondCoverage.QueryingMethods.Count,
            secondCoverage.CallReads);
        Assert.Equal(physical.Work, firstCoverage.Work);
        Assert.Equal(firstCoverage.Work, secondCoverage.Work);
        Assert.Equal(
            physical.QueryingMethods.Count,
            firstCoverage.QueryingMethods.Count);
        Assert.Equal(
            firstCoverage.QueryingMethods.Count,
            secondCoverage.QueryingMethods.Count);
    }

    [Fact]
    public void MethodQuerySource_FieldOperandsAreDecodedOnceAcrossLanes()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        MethodDefinitionSourceAssociation first =
            Association(
                MethodCallMetadataProbe.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation second =
            Association(
                MethodCallMetadataProbe.Second,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([first, second]), path);
        ImmutableArray<MethodCallMetadataProbeFact> facts =
            CompletedValue<
                ImmutableArray<MethodCallMetadataProbeFact>>(
                    execution,
                    first);
        MethodCallMetadataCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.Calls;
        MethodCallMetadataCoverage firstCoverage =
            execution.ResultOf(first).SourceReceipt.Coverage.Calls;
        MethodCallMetadataCoverage secondCoverage =
            execution.ResultOf(second).SourceReceipt.Coverage.Calls;

        Assert.Contains(
            facts,
            fact => fact.Fields.Length
                > fact.Fields
                    .Select(static field => field.OperandToken)
                    .Distinct()
                    .Count());
        Assert.Equal(physical.Work, firstCoverage.Work);
        Assert.Equal(firstCoverage.Work, secondCoverage.Work);
        Assert.Equal(
            firstCoverage.FieldOperandReads
                + secondCoverage.FieldOperandReads,
            physical.FieldOperandReads);
        Assert.Equal(
            firstCoverage.CallReads
                + secondCoverage.CallReads,
            physical.CallReads);
        Assert.Equal(
            firstCoverage.LocalSignatureReads
                + secondCoverage.LocalSignatureReads,
            physical.LocalSignatureReads);
        Assert.Equal(
            firstCoverage.FieldOperandReads,
            secondCoverage.FieldOperandReads);
        Assert.True(
            firstCoverage.FieldOperandReads > 0);
    }

    [Fact]
    public void MethodQuerySource_MalformedCallOperandFailsBody()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodDefinitionRequestSetTests).Assembly.Location);
        MethodDefinitionHandle method;
        using (var stream = new MemoryStream(image, writable: false))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            method =
                FindMethod(
                    reader,
                    nameof(MethodCallMetadataProbeTarget),
                    nameof(MethodCallMetadataProbeTarget.Exercise));
            MethodDefinition definition =
                reader.GetMethodDefinition(method);
            MethodBodyBlock body =
                pe.GetMethodBody(definition.RelativeVirtualAddress);
            DecodedInstruction call =
                Instructions.InstructionDecoder
                    .Decode(body.GetILBytes()!)
                    .First(static instruction =>
                        instruction.OpCode == ILOpCode.Call);
            int bodyOffset = RvaToFileOffset(
                pe.PEHeaders,
                definition.RelativeVirtualAddress);
            int headerSize = (image[bodyOffset] & 3) == 2
                ? 1
                : ((BinaryPrimitives.ReadUInt16LittleEndian(
                        image.AsSpan(bodyOffset, sizeof(ushort)))
                    >> 12)
                    & 0xF) * sizeof(uint);
            int fieldToken = MetadataTokens.GetToken(
                reader.FieldDefinitions.First());
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    bodyOffset + headerSize + call.OperandOffset,
                    sizeof(int)),
                fieldToken);
        }
        MethodDefinitionSourceAssociation association =
            Association(
                MethodCallsProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([association]),
                ImmutableArray.Create(image));
        ProducerResult<
            ImmutableDictionary<CallSiteKey, MethodCallSite>> result =
                ResultOf<
                    ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                        execution,
                        association);

        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Contains(
            "call operand is a FieldDefinition",
            result.Failure?.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            execution.ResultOf(association)
                .SourceReceipt.Completion);
    }

    [Fact]
    public void MethodQuerySource_FieldMemberReferenceCallFailsBody()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodDefinitionRequestSetTests).Assembly.Location);
        MethodDefinitionHandle method;
        using (var stream = new MemoryStream(image, writable: false))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            method =
                FindMethod(
                    reader,
                    nameof(MethodCallMetadataProbeTarget),
                    nameof(MethodCallMetadataProbeTarget.Exercise));
            MethodDefinition definition =
                reader.GetMethodDefinition(method);
            MethodBodyBlock body =
                pe.GetMethodBody(definition.RelativeVirtualAddress);
            DecodedInstruction call =
                Instructions.InstructionDecoder
                    .Decode(body.GetILBytes()!)
                    .First(static instruction =>
                        instruction.OpCode == ILOpCode.Call);
            MemberReferenceHandle field = reader.MemberReferences
                .First(handle =>
                    reader.GetMemberReference(handle).GetKind()
                        == MemberReferenceKind.Field);
            int bodyOffset = RvaToFileOffset(
                pe.PEHeaders,
                definition.RelativeVirtualAddress);
            int headerSize = (image[bodyOffset] & 3) == 2
                ? 1
                : ((BinaryPrimitives.ReadUInt16LittleEndian(
                        image.AsSpan(bodyOffset, sizeof(ushort)))
                    >> 12)
                    & 0xF) * sizeof(uint);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    bodyOffset + headerSize + call.OperandOffset,
                    sizeof(int)),
                MetadataTokens.GetToken(field));
        }
        MethodDefinitionSourceAssociation association =
            Association(
                MethodCallsProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([association]),
                ImmutableArray.Create(image));
        ProducerResult<
            ImmutableDictionary<CallSiteKey, MethodCallSite>> result =
                ResultOf<
                    ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                        execution,
                        association);
        MethodCallMetadataCoverage coverage =
            execution.ResultOf(association).SourceReceipt.Coverage.Calls;

        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Contains(
            "member-reference signature is malformed",
            result.Failure?.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, coverage.CallReads);
        Assert.True(coverage.Work.SignatureBytes > 0);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            execution.ResultOf(association)
                .SourceReceipt.Completion);
    }

    [Fact]
    public void MethodQuerySource_MalformedCalliSignatureFailsBody()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MethodDefinitionRequestSetTests).Assembly.Location);
        MethodDefinitionHandle method;
        using (var stream = new MemoryStream(image, writable: false))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            method =
                FindMethod(
                    reader,
                    nameof(MethodCallMetadataProbeTarget),
                    nameof(MethodCallMetadataProbeTarget.Exercise));
            MethodDefinition definition =
                reader.GetMethodDefinition(method);
            MethodBodyBlock body =
                pe.GetMethodBody(definition.RelativeVirtualAddress);
            Assert.False(body.LocalSignature.IsNil);
            DecodedInstruction call =
                Instructions.InstructionDecoder
                    .Decode(body.GetILBytes()!)
                    .First(static instruction =>
                        instruction.OpCode == ILOpCode.Call);
            int bodyOffset = RvaToFileOffset(
                pe.PEHeaders,
                definition.RelativeVirtualAddress);
            int headerSize = (image[bodyOffset] & 3) == 2
                ? 1
                : ((BinaryPrimitives.ReadUInt16LittleEndian(
                        image.AsSpan(bodyOffset, sizeof(ushort)))
                    >> 12)
                    & 0xF) * sizeof(uint);
            image[bodyOffset + headerSize + call.Offset] =
                (byte)ILOpCode.Calli;
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    bodyOffset + headerSize + call.OperandOffset,
                    sizeof(int)),
                MetadataTokens.GetToken(body.LocalSignature));
        }
        MethodDefinitionSourceAssociation association =
            Association(
                MethodCallsProbe.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([association]),
                ImmutableArray.Create(image));
        ProducerResult<
            ImmutableDictionary<CallSiteKey, MethodCallSite>> result =
                ResultOf<
                    ImmutableDictionary<CallSiteKey, MethodCallSite>>(
                        execution,
                        association);
        MethodCallMetadataCoverage coverage =
            execution.ResultOf(association).SourceReceipt.Coverage.Calls;

        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.Contains(
            "calli signature is malformed",
            result.Failure?.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, coverage.CallReads);
        Assert.True(coverage.Work.SignatureBytes > 0);
        Assert.Equal(
            MethodDefinitionSourceCompletion.ProducerFailed,
            execution.ResultOf(association)
                .SourceReceipt.Completion);
    }

    [Fact]
    public void MethodQuerySource_RepeatedCachedReadsRemainLogicalReads()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            FindMethod(
                reader,
                nameof(MethodCallMetadataProbeTarget),
                nameof(MethodCallMetadataProbeTarget.Exercise));
        MethodDefinitionSourceAssociation association =
            Association(
                RepeatedMethodCallMetadataProbe.Instance,
                ProducerTerminal.Count,
                MethodDefinitionSourceBreadth.ExactMethods(method));

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([association]), path);
        MethodCallMetadataCoverage coverage =
            execution.ResultOf(association).SourceReceipt.Coverage.Calls;

        Assert.Equal(2, coverage.CallReads);
        Assert.Equal(2, coverage.LocalSignatureReads);
        Assert.Equal(1, coverage.Work.LocalSignatures);
        Assert.Equal(1, coverage.QueryingMethods.Count);
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        SectionHeader section =
            headers.SectionHeaders.Single(header =>
                rva >= header.VirtualAddress
                && rva < header.VirtualAddress
                    + Math.Max(
                        header.VirtualSize,
                        header.SizeOfRawData));
        return checked(
            rva - section.VirtualAddress + section.PointerToRawData);
    }

    static TResult CompletedValue<TResult>(
        MethodDefinitionSourceRequestSetExecution execution,
        MethodDefinitionSourceAssociation association)
    {
        ProducerResult<TResult> result =
            ResultOf<TResult>(execution, association);
        Assert.True(
            result.Outcome == ProducerOutcome.Complete,
            result.Failure?.Message
                ?? result.Critical?.Message
                ?? $"Unexpected producer outcome {result.Outcome}.");
        return result.Value!;
    }
}

public static class MethodCallMetadataProbeTarget
{
    public static int Value;

    public static string ReadClosedGenericField()
    {
        var box =
            new MethodCallMetadataProbeClosedGenericFieldBox<string>
            {
                Item = "value",
            };
        return box.Item;
    }

    public static int Exercise(int input)
    {
        ReadOnlySpan<byte> bytes = [1, 2, 3, 4];
        Span<int> values = stackalloc int[1];
        values[0] = Math.Abs(input) + bytes[0];
        Value = values[0];
        return Value;
    }
}

public sealed class MethodCallMetadataProbeClosedGenericFieldBox<T>
{
    public T Item = default!;
}
