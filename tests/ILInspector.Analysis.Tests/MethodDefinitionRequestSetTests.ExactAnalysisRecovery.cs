using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void MethodAnalysis_PreservesIndependentEvidenceWhenCallsFail()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ExactMethodAnalysisFixture).Assembly.Location);
        MethodDefinitionHandle method;
        using (var stream = new MemoryStream(image, writable: false))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            method = FindMethod(
                reader,
                nameof(ExactMethodAnalysisFixture),
                nameof(ExactMethodAnalysisFixture.Target));
            MethodDefinition definition =
                reader.GetMethodDefinition(method);
            MethodBodyBlock body =
                pe.GetMethodBody(definition.RelativeVirtualAddress);
            DecodedInstruction call =
                InstructionDecoder
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

        ImmutableArray<byte> input = ImmutableArray.Create(image);
        MethodDefinitionSourceAssociation association =
            Association(
                ExactMethodAnalysisProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method),
                new(
                    maximumBodies: 1,
                    maximumEncodedIlBytes: int.MaxValue));
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(
                AcceptedPlan([association]),
                input);
        ExactMethodAnalysisBody result = Assert.Single(
            ResultOf<ExactMethodAnalysisProducerResult>(
                execution,
                association).Value!.Bodies);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "call operand is a FieldDefinition",
                StringComparison.Ordinal));
        UnsafetyOccurrence occurrence = Assert.Single(
            result.UnsafeOccurrences,
            occurrence =>
                occurrence.Kind == UnsafetyKind.StackAlloc);
        Assert.Equal("byte*", occurrence.Detail);
        Assert.Single(result.ExceptionRegions);
        Assert.Contains(
            result.SafetyEvidence,
            evidence => evidence.Detail == "localloc");

        MethodDefinitionSourceAssociation callGraphAssociation =
            Association(
                LibraryCallGraphProducer.Instance,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth
                    .AllDefinitions
                    .IncludeGeneratedExecutionBodies());
        MethodDefinitionSourceRequestSetExecution callGraphExecution =
            Execute(
                AcceptedPlan([callGraphAssociation]),
                input);
        LibraryCallGraphProducerResult produced =
            Assert.IsType<LibraryCallGraphProducerResult>(
                ResultOf<LibraryCallGraphProducerResult>(
                    callGraphExecution,
                    callGraphAssociation).Value);
        MethodDefinitionSourceRequestResult sourceResult =
            callGraphExecution.ResultOf(callGraphAssociation);
        LibraryCallGraphAnalysisResult graph =
            LibraryCallGraphAnalysisResult.FromProducer(
                "malformed call fixture",
                produced,
                sourceResult.SourceReceipt,
                sourceResult.WorkReceipt);
        try
        {
            Assert.Contains(
                graph.Diagnostics,
                diagnostic => diagnostic.Message.Contains(
                    "call operand is a FieldDefinition",
                    StringComparison.Ordinal));
            MethodSignals signals =
                graph.MethodSignals[MetadataTokens.GetToken(method)];
            Assert.True(signals.Catches > 0);
        }
        finally
        {
            graph.ReleaseCaches();
        }
    }
}
