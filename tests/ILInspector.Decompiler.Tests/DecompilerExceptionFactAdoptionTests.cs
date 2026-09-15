using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class DecompilerExceptionFactAdoptionTests
{
    static string FixturePath => typeof(CfgSampleClass).Assembly.Location;

    [Fact]
    public void ImportAndStructuring_PreserveExactSharedClauseIdentity()
    {
        using var source = MetadataSource.Open(FixturePath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.ChecksThenTry)));
        InstructionExceptionFlowFacts facts = AvailableFacts(function);
        DecompilerExceptionClauseImport imported =
            Assert.Single(function.ExceptionClauseImports);

        Assert.Same(Assert.Single(facts.Clauses), imported.Facts);
        Assert.Same(Assert.Single(function.Regions), imported.Region);
        Assert.Equal(-1, imported.Region.FilterOffset);
        Assert.Same(
            function.ExceptionInstructions!.ExceptionFlow,
            function.ExceptionFlow);

        new EhStructuringPass().Run(function, PassContext.None);

        TryCatch structured = Assert.Single(
            function.Descendants.OfType<TryCatch>());
        CatchClause clause = Assert.Single(structured.Clauses);
        Assert.Equal(
            imported.Facts.ProtectedRegion,
            structured.ExceptionProtectedRegion);
        Assert.Same(imported.Facts, clause.ExceptionClause);
        Assert.Empty(function.Regions);
        Assert.Null(function.ExceptionFactFailure);
    }

    [Fact]
    public void Structuring_PreservesMetadataCatchOrderAndIdentity()
    {
        using var source = MetadataSource.Open(FixturePath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.TwoCatches)));
        InstructionExceptionFlowFacts facts = AvailableFacts(function);

        new EhStructuringPass().Run(function, PassContext.None);

        TryCatch structured = Assert.Single(
            function.Descendants.OfType<TryCatch>());
        Assert.Equal(
            facts.Clauses.Select(clause => clause.Id),
            structured.Clauses.Select(clause =>
                Assert.IsType<InstructionExceptionClause>(
                    clause.ExceptionClause).Id));
    }

    [Fact]
    public void RuntimeTextReaderFinally_ReachesDecompilerWithSharedCleanupIdentity()
    {
        MethodInfo method = typeof(TextReader).GetMethod(
            nameof(TextReader.Read),
            [typeof(Span<char>)])!;
        using var source = MetadataSource.Open(
            typeof(TextReader).Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, method.MetadataToken));
        InstructionExceptionFlowFacts facts = AvailableFacts(function);
        InstructionExceptionClause finallyClause = Assert.Single(
            facts.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Finally);
        DecompilerExceptionClauseImport finallyImport = Assert.Single(
            function.ExceptionClauseImports,
            import => import.Facts.Id == finallyClause.Id);

        InstructionNormalTransfer transfer = Assert.Single(
            Assert.IsType<MethodInstructions>(
                    function.ExceptionInstructions)
                .Instructions
                .Where(instruction => instruction.LeavesRegion)
                .Select(instruction => facts.NormalTransferAt(
                    instruction.Offset,
                    Assert.Single(instruction.BranchTargets)))
                .OfType<InstructionExceptionFlowResult<
                    InstructionNormalTransfer>.Available>()
                .Select(result => result.Value),
            candidate => candidate.CleanupHandlers.Any(
                cleanup => cleanup.Clause == finallyClause.Id));

        Assert.Same(finallyClause, finallyImport.Facts);
        Assert.Contains(
            transfer.CleanupHandlers,
            cleanup => cleanup.Clause == finallyClause.Id
                && cleanup.Handler == finallyClause.HandlerRegion);

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.Single(
            function.Descendants.OfType<TryFinally>(),
            structured => ReferenceEquals(
                structured.ExceptionClause,
                finallyClause));
    }

    [Fact]
    public void RejectedCatchTypeEvidence_DeclinesVisibly()
    {
        byte[] image = File.ReadAllBytes(FixturePath);
        int token = typeof(CfgSampleClass).GetMethod(
            nameof(CfgSampleClass.ChecksThenTry))!.MetadataToken;
        int clauseOffset = FirstExceptionClauseOffset(
            image,
            token,
            out bool fat);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(clauseOffset + (fat ? 20 : 8)),
            0x02FFFFFF);

        using var source = MetadataSource.OpenFromPrefetchedImage(
            "rejected-catch-type.dll",
            ImmutableArray.Create(image));
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, token));

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
        Assert.Contains(
            FidelityRemarks.Collect(function),
            remark => remark.Code == DiagnosticIds.ExceptionFactsUnavailable
                && remark.Reason.Contains(
                    "catch-type evidence is unavailable",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void MissingCorrelatedFlow_DeclinesWithoutRawFallback()
    {
        using var source = MetadataSource.Open(FixturePath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.ChecksThenTry)));
        function.ExceptionInstructions = null;

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
        Assert.Contains(
            FidelityRemarks.Collect(function),
            remark => remark.Code == DiagnosticIds.ExceptionFactsUnavailable
                && remark.Reason.Contains(
                    "no correlated Instructions evidence",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void UnavailableMethodBodyEvidence_IsNotAnImporterCrash()
    {
        byte[] image = File.ReadAllBytes(FixturePath);
        int token = typeof(CfgSampleClass).GetMethod(
            nameof(CfgSampleClass.ChecksThenTry))!.MetadataToken;
        using (var pe = new PEReader(
                   new MemoryStream(image, writable: false)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            MethodDefinition method = reader.GetMethodDefinition(
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(token));
            int bodyOffset = RvaToFileOffset(
                pe.PEHeaders,
                method.RelativeVirtualAddress);
            image[bodyOffset] = 0;
        }

        using var source = MetadataSource.OpenFromPrefetchedImage(
            "unavailable-method-body.dll",
            ImmutableArray.Create(image));
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, token));

        Assert.Contains(
            function.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.ContextUnavailable);
        Assert.DoesNotContain(
            function.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.InternalError);
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "(method-body evidence unavailable)");
    }

    [Fact]
    public void SyntheticRawRegions_KeepLegacyStructuringPath()
    {
        var body = new BlockContainer();
        var tryBlock = new Block(0);
        tryBlock.Add(new Leave(20));
        body.Add(tryBlock);
        var finallyBlock = new Block(10);
        finallyBlock.Add(new EndFinally());
        body.Add(finallyBlock);
        var continuation = new Block(20);
        continuation.Add(new Return(null));
        body.Add(continuation);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            Regions =
            [
                new HandlerRegion(
                    ILInspector.Decompiler.Pipeline.HandlerKind.Finally,
                    TryOffset: 0,
                    TryLength: 10,
                    HandlerOffset: 10,
                    HandlerLength: 10,
                    FilterOffset: 0,
                    CatchType: null),
            ],
        };

        new EhStructuringPass().Run(function, PassContext.None);

        TryFinally structured = Assert.Single(
            function.Descendants.OfType<TryFinally>());
        Assert.Null(structured.ExceptionClause);
        Assert.Empty(function.Regions);
        Assert.Null(function.ExceptionFactFailure);
    }

    [Fact]
    public void ReplacedBodyCorrelation_ClearsAsOneUnit()
    {
        using var source = MetadataSource.Open(FixturePath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.ChecksThenTry)));

        function.ClearImportedExceptionFacts();

        Assert.Null(function.ExceptionInstructions);
        Assert.Null(function.ExceptionFlow);
        Assert.Empty(function.ExceptionClauseImports);
        Assert.Null(function.ExceptionFactFailure);
    }

    static InstructionExceptionFlowFacts AvailableFacts(
        IrFunction function) =>
        Assert.IsType<InstructionExceptionFlowResult<
            InstructionExceptionFlowFacts>.Available>(
                function.ExceptionFlow).Value;

    static int FirstExceptionClauseOffset(
        byte[] image,
        int methodToken,
        out bool fat)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
        int bodyOffset = RvaToFileOffset(
            pe.PEHeaders,
            method.RelativeVirtualAddress);
        ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(bodyOffset, 2));
        int headerSize = (flagsAndSize >> 12) * 4;
        int codeSize = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(bodyOffset + 4, 4));
        int sectionOffset =
            (bodyOffset + headerSize + codeSize + 3) & ~3;
        byte sectionKind = image[sectionOffset];
        fat = (sectionKind & 0x40) != 0;
        return sectionOffset + 4;
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        int sectionIndex = headers.GetContainingSectionIndex(rva);
        Assert.True(sectionIndex >= 0);
        SectionHeader section = headers.SectionHeaders[sectionIndex];
        return checked(
            rva - section.VirtualAddress + section.PointerToRawData);
    }
}
