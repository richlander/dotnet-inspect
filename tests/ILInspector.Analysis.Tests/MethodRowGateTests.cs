using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Release gates for the method-row gate, field demand, and the
/// budget-exhaustion abort
/// (docs/design/producer-planning.md#the-source-gate-owns-safety and
/// #budget-exhaustion-aborts-the-execution). All PR-fast: fixtures are built
/// in memory and bounded.
/// </summary>
public sealed class MethodRowGateTests
{
    // ---- Field demand ----

    [Fact]
    public void FieldDemand_UndeclaredFieldAccessFails()
    {
        ImmutableArray<byte> image = Ordinary().Build();
        var producer = new GateProducer<NameAPredicate>("FlagsOnly", MethodDefinitionLayers.Flags);

        ProducerContractException ex = Assert.Throws<ProducerContractException>(() =>
            Run(image, Plan(new ProducerRequest(producer))));
        Assert.Contains("NameComparison", ex.Message);
    }

    [Fact]
    public void FieldDemand_RawRowsNeedADomainLayer()
    {
        ImmutableArray<byte> image = Ordinary().Build();
        var producer = new GateProducer<RawRowPredicate>("Raw", MethodDefinitionLayers.Flags);

        Assert.Throws<ProducerContractException>(() =>
            Run(image, Plan(new ProducerRequest(producer))));
    }

    [Fact]
    public void FieldDemand_ExplainsWhatAPlanReads()
    {
        var classifier = new ScopeClassifier();
        var producer = new GateProducer<PointerPredicate>(
            "Pointer",
            MethodDefinitionLayers.SignatureShape,
            new SourceGateGuard(classifier, 0b11));

        MethodDefinitionLayers fields =
            MethodDefinitionExecution.FieldsRead(Plan(new ProducerRequest(producer)));

        Assert.Equal(
            MethodDefinitionLayers.Declaration
                | MethodDefinitionLayers.SignatureShape
                | MethodDefinitionLayers.Flags
                | MethodDefinitionLayers.NameComparison,
            fields);
    }

    // ---- Identity budget ----

    [Theory]
    [InlineData(ProducerTerminal.Complete)]
    [InlineData(ProducerTerminal.Exists)]
    public void IdentityBudget_CountAndExistsChargeNothing(ProducerTerminal terminal)
    {
        ImmutableArray<byte> image = Ordinary().Build();
        var pointer = new GateProducer<PointerPredicate>("Pointer", MethodDefinitionLayers.SignatureShape);
        var names = new GateProducer<NameAPredicate>("NamesA", MethodDefinitionLayers.NameComparison);

        MethodDefinitionExecution execution = Run(
            image,
            Plan(new ProducerRequest(pointer, terminal), new ProducerRequest(names, terminal)));

        Assert.False(execution.Receipt.IdentityBudgetArmed);
        Assert.Equal(0, execution.Receipt.IdentityWorkCharged);
        Assert.True(execution.ResultOf(pointer).HasValue);
        Assert.True(execution.ResultOf(names).HasValue);
    }

    [Fact]
    public void IdentityBudget_IdentityTextArmsAndCharges()
    {
        ImmutableArray<byte> image = Ordinary().Build();
        var rows = new GateProducer<IdentityPredicate>("Rows", MethodDefinitionLayers.IdentityText);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(rows)));

        Assert.True(execution.Receipt.IdentityBudgetArmed);
        Assert.True(execution.Receipt.IdentityWorkCharged > 0);
        Assert.Equal(ProducerOutcome.Complete, execution.ResultOf(rows).Outcome);
    }

    [Fact]
    public void IdentityBudget_HostileIdentitiesAbortRowsButNotCount()
    {
        // Legacy's hostile identity fixture: every method's identity is near
        // or over the per-anchor limit, so Rows exhausts the identity budget.
        // Count reads no identity text, so the same image completes.
        ImmutableArray<byte> image = HostileIdentities(methodCount: 64, parameterCount: 2_000, genericArity: 2_030);
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);
        var rows = new GateProducer<IdentityPredicate>("Rows", MethodDefinitionLayers.IdentityText);

        MethodDefinitionExecution counted = Run(image, Plan(new ProducerRequest(count)));
        Assert.Equal(ProducerOutcome.Complete, counted.ResultOf(count).Outcome);
        Assert.Equal(0, counted.Receipt.IdentityWorkCharged);

        MethodDefinitionExecution listed = Run(image, Plan(new ProducerRequest(rows)));
        ProducerResult<int> aborted = listed.ResultOf(rows);
        Assert.Equal(ProducerOutcome.Aborted, aborted.Outcome);
        Assert.Equal(MethodRowGate.Owner, aborted.Critical!.Owner);
        Assert.Contains(
            aborted.Critical.Budget,
            new[] { MethodRowGate.IdentityWork, MethodRowGate.IdentityDecodeFailures });
        Assert.Same(aborted.Critical, listed.Receipt.Critical);
    }

    // ---- Abort ----

    [Fact]
    public void Abort_PublishesNothingAndReadsNoLaterUnit()
    {
        ImmutableArray<byte> image = HostileIdentities(methodCount: 64, parameterCount: 2_000, genericArity: 2_030);
        var rows = new GateProducer<IdentityPredicate>("Rows", MethodDefinitionLayers.IdentityText);
        var logged = new GateProducer<LoggingPredicate>("Logged", MethodDefinitionLayers.Flags);
        LoggingPredicate.Tokens.Clear();

        MethodDefinitionExecution execution = Run(
            image,
            Plan(new ProducerRequest(rows), new ProducerRequest(logged)));

        CriticalFailure critical = Assert.IsType<CriticalFailure>(execution.Receipt.Critical);
        foreach (ProducerDeclaration producer in new ProducerDeclaration[] { rows, logged })
        {
            ProducerParticipation participation = execution.Receipt.For(producer);
            Assert.Equal(ProducerOutcome.Aborted, participation.Outcome);
        }

        ProducerResult<int> rowsResult = execution.ResultOf(rows);
        ProducerResult<int> loggedResult = execution.ResultOf(logged);
        Assert.False(rowsResult.HasValue);
        Assert.False(loggedResult.HasValue);
        Assert.Equal(0, rowsResult.Value);
        Assert.Equal(0, loggedResult.Value);
        Assert.Same(critical, rowsResult.Critical);
        Assert.Same(critical, loggedResult.Critical);
        Assert.All(LoggingPredicate.Tokens, token => Assert.True(token <= critical.UnitToken));
    }

    [Fact]
    public void Abort_PerRowSignatureCapAbortsWithoutIdentityText()
    {
        // One signature with more type nodes than the per-row cap.
        GateFixtureImage builder = Ordinary();
        var wide = new BlobBuilder();
        wide.WriteByte(0x00);
        int parameters = MetadataSafetyPolicy.MaxSignatureTypeNodes + 16;
        wide.WriteCompressedInteger(parameters);
        wide.WriteByte(0x01);
        for (int i = 0; i < parameters; i++)
            wide.WriteByte(0x08);
        builder.Type("N", "Wide").Method("TooWide", wide);
        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        ProducerResult<int> result = execution.ResultOf(count);
        Assert.Equal(ProducerOutcome.Aborted, result.Outcome);
        Assert.Equal(MethodRowGate.SignatureShapeCap, result.Critical!.Budget);
        Assert.False(execution.Receipt.IdentityBudgetArmed);
    }

    [Fact]
    public void Abort_IsNotContainedByTheKernel()
    {
        ImmutableArray<byte> image = HostileIdentities(methodCount: 8, parameterCount: 2_000, genericArity: 2_030);
        var rows = new GateProducer<IdentityPredicate>("Rows", MethodDefinitionLayers.IdentityText, kernel: true);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(rows)));

        Assert.Equal(ProducerOutcome.Aborted, execution.ResultOf(rows).Outcome);
    }

    // ---- Signature shape ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SignatureShape_SharedDeepTypeSpecsStayLinear(bool pointerAtLeaf)
    {
        // A doubling tree of TypeSpecs: level k instantiates G<level k-1,
        // level k-1>. Unmemoized, one signature walks 2^depth nodes; many
        // methods multiply that again. Memoized, every TypeSpec and blob is
        // walked once.
        const int depth = 18;
        const int methods = 200;
        GateFixtureImage builder = Ordinary();
        TypeReferenceHandle generic = builder.TypeRef("N", "G`2");
        var leaf = new BlobBuilder();
        if (pointerAtLeaf)
            leaf.WriteByte(0x0F); // PTR
        leaf.WriteByte(0x08); // I4 (a bare I4 TypeSpec when no pointer)
        TypeSpecificationHandle level = builder.TypeSpec(leaf);
        for (int k = 0; k < depth; k++)
        {
            var node = new BlobBuilder();
            node.WriteByte(0x15); // GENERICINST
            node.WriteByte(0x12); // CLASS
            node.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(generic));
            node.WriteCompressedInteger(2);
            for (int a = 0; a < 2; a++)
            {
                // A TypeSpec token is legal only as a modifier.
                node.WriteByte(0x20); // CMOD_OPT <TypeSpec>
                node.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(level));
                node.WriteByte(0x08);
            }

            level = builder.TypeSpec(node);
        }

        GateFixtureImage.FixtureType type = builder.Type("N", "Deep");
        for (int m = 0; m < methods; m++)
        {
            // Distinct signatures, so the blob memo does not hide the work.
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteCompressedInteger(m + 1);
            signature.WriteByte(0x01);
            for (int p = 0; p < m; p++)
                signature.WriteByte(0x08);
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(level));
            signature.WriteByte(0x08);
            type.Method($"M{m}", signature);
        }

        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        ProducerResult<int> result = execution.ResultOf(count);
        Assert.Equal(ProducerOutcome.Complete, result.Outcome);
        Assert.Equal(pointerAtLeaf ? methods : 0, result.Value - OrdinaryPointerMethods);
        using var peReader = new PEReader(image);
        int blobHeap = peReader.GetMetadataReader().GetHeapSize(HeapIndex.Blob);
        Assert.True(
            execution.Receipt.SignatureShapeNodesWalked <= blobHeap,
            $"Walked {execution.Receipt.SignatureShapeNodesWalked} nodes; the #Blob heap is {blobHeap} bytes.");
    }

    [Fact]
    public void SignatureShape_AnswersEqualLegacyWhereNoGuardRefuses()
    {
        // A shallow shared tree the legacy probe can walk within its budget:
        // the gate's pointer rows must name exactly the legacy pointer rows.
        GateFixtureImage builder = Ordinary();
        TypeReferenceHandle generic = builder.TypeRef("N", "G`2");
        TypeSpecificationHandle pointerLeaf = builder.TypeSpec(Bytes(0x0F, 0x08));
        TypeSpecificationHandle plainLeaf = builder.TypeSpec(Bytes(0x08));
        GateFixtureImage.FixtureType type = builder.Type("N", "Parity");
        foreach ((string name, TypeSpecificationHandle leaf) in new[] { ("WithPointer", pointerLeaf), ("Without", plainLeaf) })
        {
            TypeSpecificationHandle level = leaf;
            for (int k = 0; k < 4; k++)
            {
                var node = new BlobBuilder();
                node.WriteByte(0x15);
                node.WriteByte(0x12);
                node.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(generic));
                node.WriteCompressedInteger(2);
                for (int a = 0; a < 2; a++)
                {
                    node.WriteByte(0x20);
                    node.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(level));
                    node.WriteByte(0x08);
                }

                level = builder.TypeSpec(node);
            }

            type.Method(name, ParameterOf(level));
        }

        type.Method("FunctionPointer", GateFixtureImage.VoidSignature(t => t.FunctionPointer().Parameters(0, r => r.Void(), _ => { })))
            .Method("ByRefPointer", GateFixtureImage.VoidSignature(t => t.Pointer().Int32()));
        ImmutableArray<byte> image = builder.Build();

        using var peReader = new PEReader(image);
        HashSet<string> legacy = MethodClassificationScanner.Scan(peReader)
            .Where(static row => row.Classification == MethodClassification.Unsafe)
            .Select(static row => row.MethodName)
            .ToHashSet();
        var names = new GateProducer<PointerNamePredicate>(
            "PointerNames", MethodDefinitionLayers.SignatureShape | MethodDefinitionLayers.IdentityText);
        PointerNamePredicate.Seen.Clear();
        Run(image, Plan(new ProducerRequest(names)));
        HashSet<string> gate = PointerNamePredicate.Seen
            .Where(static entry => entry.Value)
            .Select(static entry => entry.Key)
            .ToHashSet();

        Assert.Contains("WithPointer", legacy);
        Assert.DoesNotContain("Without", legacy);
        Assert.Equal(legacy.Order(), gate.Where(legacy.Contains).Order());
        Assert.Equal(legacy, gate.Where(name => name is not "Hidden").ToHashSet());
    }

    [Fact]
    public void SignatureShape_ReentryBeyondTheGuardDepthAborts()
    {
        GateFixtureImage builder = Ordinary();
        TypeSpecificationHandle level = builder.TypeSpec(Bytes(0x08));
        for (int k = 0; k < TypeSpecGuard.MaxDepth + 4; k++)
            level = builder.TypeSpec(ModifiedBy(level, 0x08));
        builder.Type("N", "Chain").Method("Deep", ParameterOf(level));
        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        Assert.Equal(MethodRowGate.TypeSpecificationGuard, execution.ResultOf(count).Critical!.Budget);
    }

    [Fact]
    public void SignatureShape_MemoizedSpecReusedTooDeepAbortsAsLegacyWouldRefuse()
    {
        // S is a chain of 200 TypeSpecs, admitted from the top. Reached again
        // under 100 more levels, legacy's guard refuses at depth 256; the
        // memoized answer must not hide that.
        GateFixtureImage builder = Ordinary();
        TypeSpecificationHandle s = builder.TypeSpec(Bytes(0x08));
        for (int k = 1; k < 200; k++)
            s = builder.TypeSpec(ModifiedBy(s, 0x08));
        TypeSpecificationHandle outer = s;
        for (int k = 0; k < 100; k++)
            outer = builder.TypeSpec(ModifiedBy(outer, 0x08));
        builder.Type("N", "Reuse")
            .Method("A", ParameterOf(s))
            .Method("B", ParameterOf(outer));
        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        Assert.Equal(MethodRowGate.TypeSpecificationGuard, execution.ResultOf(count).Critical!.Budget);
    }

    [Fact]
    public void SignatureShape_AModifierCycleAborts()
    {
        GateFixtureImage builder = Ordinary();
        // A TypeSpec whose modifier names itself: row 1 is the next TypeSpec row.
        var cyclic = new BlobBuilder();
        cyclic.WriteByte(0x20); // CMOD_OPT
        cyclic.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(MetadataTokens.TypeSpecificationHandle(1)));
        cyclic.WriteByte(0x08);
        TypeSpecificationHandle self = builder.TypeSpec(cyclic);
        Assert.Equal(1, MetadataTokens.GetRowNumber(self));
        builder.Type("N", "Cycle").Method("C", ParameterOf(self));
        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        Assert.Equal(MethodRowGate.TypeSpecificationGuard, execution.ResultOf(count).Critical!.Budget);
    }

    // ---- Round-1 review gates (#8790) ----

    [Fact]
    public void SignatureShape_TypeSpecAliasesDecodeTheirBlobOnce()
    {
        // 256 TypeSpec rows share one 2,048-argument blob. Memoized per handle
        // only, each alias re-decoded the blob: 525,824 nodes on a ~4 KB heap.
        GateFixtureImage builder = Ordinary();
        TypeReferenceHandle t = builder.TypeRef("N", "T");
        TypeReferenceHandle g = builder.TypeRef("N", "G");
        var wide = new BlobBuilder();
        wide.WriteByte(0x15);
        wide.WriteByte(0x12);
        wide.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(g));
        wide.WriteCompressedInteger(2_048);
        for (int i = 0; i < 2_048; i++)
            wide.WriteByte(0x08);
        var aliases = new List<TypeSpecificationHandle>();
        for (int i = 0; i < 256; i++)
            aliases.Add(builder.TypeSpec(Copy(wide)));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(aliases.Count);
        signature.WriteByte(0x01);
        foreach (TypeSpecificationHandle alias in aliases)
        {
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(alias));
            signature.WriteByte(0x08);
        }

        builder.Type("N", "Aliases").Method("Many", signature);
        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        Assert.Equal(ProducerOutcome.Complete, execution.ResultOf(count).Outcome);
        using var peReader = new PEReader(image);
        int blobHeap = peReader.GetMetadataReader().GetHeapSize(HeapIndex.Blob);
        Assert.True(
            execution.Receipt.SignatureShapeNodesWalked <= blobHeap,
            $"Walked {execution.Receipt.SignatureShapeNodesWalked} nodes; the #Blob heap is {blobHeap} bytes.");
    }

    [Fact]
    public void IdentityBudget_ModuleNamesAreChargedAndAbortRows()
    {
        // 512 P/Invoke methods, each importing from its own 32,768-character
        // module name: 16.7M characters that were decoded with nothing charged.
        GateFixtureImage builder = Ordinary();
        GateFixtureImage.FixtureType type = builder.Type("N", "Imports");
        for (int i = 0; i < 512; i++)
        {
            type.Method(
                $"Import{i}",
                attributes: MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
                implAttributes: MethodImplAttributes.PreserveSig,
                moduleName: new string((char)('a' + (i % 26)), 32_760) + i.ToString("D5"));
        }

        ImmutableArray<byte> image = builder.Build();
        var rows = new GateProducer<ModulePredicate>("ModuleRows", MethodDefinitionLayers.IdentityText);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(rows)));

        ProducerResult<int> result = execution.ResultOf(rows);
        Assert.Equal(ProducerOutcome.Aborted, result.Outcome);
        Assert.Equal(MethodRowGate.IdentityWork, result.Critical!.Budget);
    }

    [Fact]
    public void IdentityBudget_ParameterNamesInDisplaySignaturesAreCharged()
    {
        // 256 P/Invoke methods, each with a 65,536-character parameter name:
        // the display signature decoded 16.8M characters with 135,532 charged.
        GateFixtureImage builder = Ordinary();
        GateFixtureImage.FixtureType type = builder.Type("N", "Named");
        for (int i = 0; i < 256; i++)
        {
            type.Method(
                $"Import{i}",
                GateFixtureImage.VoidSignature(static t => t.Int32()),
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
                MethodImplAttributes.PreserveSig,
                parameterNames: [new string((char)('a' + (i % 26)), 65_531) + i.ToString("D5")]);
        }

        ImmutableArray<byte> image = builder.Build();
        var rows = new GateProducer<IdentityPredicate>("Rows", MethodDefinitionLayers.IdentityText);

        long before = GC.GetAllocatedBytesForCurrentThread();
        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(rows)));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        ProducerResult<int> result = execution.ResultOf(rows);
        Assert.Equal(ProducerOutcome.Aborted, result.Outcome);
        Assert.Equal(MethodRowGate.IdentityWork, result.Critical!.Budget);
        Assert.True(allocated < 48 * 1024 * 1024, $"Allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void IdentityBudget_OneOversizedModuleNameAbortsBeforeAllocating()
    {
        // One 16.7M-character module name: it must be charged, and refused,
        // before SRM allocates its text.
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "Import").Method(
            "Huge",
            attributes: MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            implAttributes: MethodImplAttributes.PreserveSig,
            moduleName: new string('m', 16_700_000));
        ImmutableArray<byte> image = builder.Build();
        var rows = new GateProducer<ModulePredicate>("ModuleRows", MethodDefinitionLayers.IdentityText);

        long before = GC.GetAllocatedBytesForCurrentThread();
        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(rows)));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        ProducerResult<int> result = execution.ResultOf(rows);
        Assert.Equal(ProducerOutcome.Aborted, result.Outcome);
        Assert.Equal(MethodRowGate.IdentityWork, result.Critical!.Budget);
        Assert.True(allocated < 8 * 1024 * 1024, $"Allocated {allocated:N0} bytes before aborting.");
    }

    [Fact]
    public void Abort_FailureIsBuiltFromContentFreeCoordinates()
    {
        // A 32,768-character method name on an over-cap row: raising the abort
        // must not decode it, and nothing is charged to the identity budget.
        GateFixtureImage builder = Ordinary();
        builder.Type("N", new string('T', 32_768))
            .Method(new string('M', 32_768), WideSignature());
        ImmutableArray<byte> image = builder.Build();
        var count = new GateProducer<PointerPredicate>("PointerCount", MethodDefinitionLayers.SignatureShape);

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(count)));

        CriticalFailure critical = execution.ResultOf(count).Critical!;
        Assert.Equal(MethodRowGate.SignatureShapeCap, critical.Budget);
        Assert.True(critical.Unit.Length <= 32, critical.Unit);
        Assert.True(critical.Message.Length <= 256);
        Assert.Equal(0, execution.Receipt.IdentityWorkCharged);
        Assert.False(execution.Receipt.IdentityBudgetArmed);
    }

    [Fact]
    public void SignatureShape_MalformedSignatureFailsOnlyTheReader()
    {
        // A truncated signature (00 01 01: one parameter declared, none
        // present) is malformed, not a bound: it fails the pointer producer,
        // and an independent flags-only producer completes.
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "Truncated").Method("Short", Bytes(0x00, 0x01, 0x01));
        ImmutableArray<byte> image = builder.Build();
        var pointer = new GateProducer<PointerPredicate>("Pointer", MethodDefinitionLayers.SignatureShape);
        var flags = new GateProducer<AlwaysPredicate>("Flags", MethodDefinitionLayers.Flags);

        MethodDefinitionExecution execution = Run(
            image,
            Plan(new ProducerRequest(pointer), new ProducerRequest(flags)));

        Assert.Null(execution.Receipt.Critical);
        ProducerResult<int> failed = execution.ResultOf(pointer);
        Assert.Equal(ProducerOutcome.Failed, failed.Outcome);
        Assert.StartsWith("MethodDef 0x", failed.Failure!.Unit);
        Assert.Equal(ProducerOutcome.Complete, execution.ResultOf(flags).Outcome);
    }

    [Fact]
    public void RecoverableFailure_LabelReadsNoNameText()
    {
        // A malformed signature on a method with a 16.7M-character name, in a
        // plan with no IdentityText: the failure is recorded by token only.
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "Named").Method(new string('n', 16_700_000), Bytes(0x00, 0x01, 0x01));
        ImmutableArray<byte> image = builder.Build();
        var pointer = new GateProducer<PointerPredicate>("Pointer", MethodDefinitionLayers.SignatureShape);

        foreach (bool kernel in new[] { true, false })
        {
            var producer = new GateProducer<PointerPredicate>("Pointer", MethodDefinitionLayers.SignatureShape, kernel: kernel);
            long before = GC.GetAllocatedBytesForCurrentThread();
            MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(producer)));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            ProducerResult<int> result = execution.ResultOf(producer);
            Assert.Equal(ProducerOutcome.Failed, result.Outcome);
            Assert.True(result.Failure!.Unit.Length <= 32, result.Failure.Unit);
            Assert.True(result.Failure.Message.Length <= ProducerFailure.MaxMessageLength);
            Assert.True(allocated < 8 * 1024 * 1024, $"Allocated {allocated:N0} bytes.");
        }
    }

    [Fact]
    public void FailureLabel_OversizedNameIsCappedInertAndCheap()
    {
        // A presented failure resolves its one method's name with a
        // length-checked read: a 16.7M-character name shows at most 256
        // characters and an ellipsis, and costs O(cap).
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "Named").Method("\u001B" + new string('n', 16_700_000), Bytes(0x00, 0x01, 0x01));
        ImmutableArray<byte> image = builder.Build();
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        int token = 0;
        foreach (MethodDefinitionHandle method in reader.MethodDefinitions)
            token = MetadataTokens.GetToken(method);

        long before = GC.GetAllocatedBytesForCurrentThread();
        InertText.InertString label = MethodRowProjection.FailureLabel(reader, token);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        string text = label.ToString();
        Assert.StartsWith("N.Named::", text);
        Assert.Contains("nnn", text);
        Assert.EndsWith("\u2026", text);
        Assert.DoesNotContain('\u001B', text);
        Assert.True(text.Length <= 2 * MethodRowProjection.MaxFailureLabelPartLength + 16, $"{text.Length} characters.");
        Assert.True(allocated < 64 * 1024, $"Allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void IdentityText_MalformedMetadataFailsTheReaderOnly()
    {
        // A type whose namespace handle points past the #Strings heap. Budget
        // remains, so this is malformed metadata, not exhaustion: the identity
        // producer fails and an independent flags-only producer completes.
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "Broken", namespaceOverride: MetadataTokens.StringHandle(0xFFFF))
            .Method("Imported", attributes: MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl, implAttributes: MethodImplAttributes.PreserveSig);
        ImmutableArray<byte> image = builder.Build();
        var rows = new GateProducer<IdentityPredicate>("Rows", MethodDefinitionLayers.IdentityText);
        var flags = new GateProducer<AlwaysPredicate>("Flags", MethodDefinitionLayers.Flags);

        MethodDefinitionExecution execution = Run(
            image,
            Plan(new ProducerRequest(rows), new ProducerRequest(flags)));

        Assert.Null(execution.Receipt.Critical);
        Assert.Equal(ProducerOutcome.Failed, execution.ResultOf(rows).Outcome);
        Assert.Equal(ProducerOutcome.Complete, execution.ResultOf(flags).Outcome);
    }

    [Fact]
    public void IdentityText_EveryReturnedTextIsInert()
    {
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "Esc").Method("Run\u001B[31mRed", GateFixtureImage.VoidSignature(static t => t.Int32()));
        ImmutableArray<byte> image = builder.Build();
        var texts = new GateProducer<AllTextPredicate>("Texts", MethodDefinitionLayers.IdentityText);
        AllTextPredicate.Seen.Clear();

        Run(image, Plan(new ProducerRequest(texts)));

        Assert.Contains(AllTextPredicate.Seen, static text => text.Contains("Red", StringComparison.Ordinal));
        Assert.All(AllTextPredicate.Seen, static text => Assert.DoesNotContain('\u001B', text));
    }

    [Fact]
    public void Abort_KernelAndInterpretedReceiptsMatch()
    {
        // One ordinary method, then an over-cap one.
        var builder = new GateFixtureImage();
        builder.Type("N", "Order")
            .Method("Ordinary")
            .Method("OverCap", WideSignature());
        ImmutableArray<byte> image = builder.Build();
        var kernel = new GateProducer<PointerPredicate>("Kernel", MethodDefinitionLayers.SignatureShape, kernel: true);
        var interpreted = new GateProducer<PointerPredicate>("Interpreted", MethodDefinitionLayers.SignatureShape, kernel: false);

        WorkReceipt k = Run(image, Plan(new ProducerRequest(kernel))).Receipt;
        WorkReceipt i = Run(image, Plan(new ProducerRequest(interpreted))).Receipt;

        Assert.NotNull(k.Critical);
        Assert.NotNull(i.Critical);
        Assert.Equal(i.UnitsVisited, k.UnitsVisited);
        Assert.Equal(2, k.UnitsVisited);
        ProducerParticipation kp = k.For(kernel);
        ProducerParticipation ip = i.For(interpreted);
        Assert.Equal(
            (ip.Outcome, ip.UnitsAttempted, ip.UnitsCompleted, ip.UnitsFailed),
            (kp.Outcome, kp.UnitsAttempted, kp.UnitsCompleted, kp.UnitsFailed));
        Assert.Equal((2, 1, 0), (kp.UnitsAttempted, kp.UnitsCompleted, kp.UnitsFailed));
    }

    static BlobBuilder WideSignature()
    {
        var wide = new BlobBuilder();
        wide.WriteByte(0x00);
        int parameters = MetadataSafetyPolicy.MaxSignatureTypeNodes + 16;
        wide.WriteCompressedInteger(parameters);
        wide.WriteByte(0x01);
        for (int i = 0; i < parameters; i++)
            wide.WriteByte(0x08);
        return wide;
    }

    // ---- Source-gate guards ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceGate_GuardAddsNoDependencyAndSkipsExcludedUnits(bool kernel)
    {
        ImmutableArray<byte> image = Ordinary().Build();
        var classifier = new ScopeClassifier();
        var guarded = new GateProducer<AlwaysPredicate>(
            "ClassA",
            MethodDefinitionLayers.Flags,
            new SourceGateGuard(classifier, 0b01),
            kernel);

        WorkDescription description = Plan(new ProducerRequest(guarded));
        MethodDefinitionExecution execution = Run(image, description);

        Assert.Empty(guarded.Dependencies);
        Assert.Equal(ProducerOutcome.Complete, execution.ResultOf(guarded).Outcome);
        // Public methods on N.Sample whose names start with "A": Alpha, Apex.
        Assert.Equal(2, execution.ResultOf(guarded).Value);
        ProducerParticipation participation = execution.Receipt.For(guarded);
        Assert.Equal(2, participation.UnitsAttempted);
        Assert.Equal(0, participation.UnitsFailed);
    }

    [Fact]
    public void SourceGate_KernelEqualsTheInterpretedExecutor()
    {
        ImmutableArray<byte> image = Ordinary().Build();
        var classifier = new ScopeClassifier();
        foreach (ProducerTerminal terminal in new[] { ProducerTerminal.Complete, ProducerTerminal.Exists })
        {
            var kernel = new GateProducer<PointerPredicate>(
                "Kernel", MethodDefinitionLayers.SignatureShape, new SourceGateGuard(classifier, 0b11), kernel: true);
            var interpreted = new GateProducer<PointerPredicate>(
                "Interpreted", MethodDefinitionLayers.SignatureShape, new SourceGateGuard(classifier, 0b11), kernel: false);

            MethodDefinitionExecution k = Run(image, Plan(new ProducerRequest(kernel, terminal)));
            MethodDefinitionExecution i = Run(image, Plan(new ProducerRequest(interpreted, terminal)));

            Assert.Equal(i.ResultOf(interpreted).Outcome, k.ResultOf(kernel).Outcome);
            Assert.Equal(i.ResultOf(interpreted).Value, k.ResultOf(kernel).Value);
            Assert.Equal(i.Receipt.For(interpreted).UnitsAttempted, k.Receipt.For(kernel).UnitsAttempted);
        }
    }

    [Fact]
    public void SourceGate_TypeScopeExcludesCompilerGeneratedTypes()
    {
        GateFixtureImage builder = Ordinary();
        builder.Type("N", "<Generated>").Method("Alpha2");
        ImmutableArray<byte> image = builder.Build();
        var classifier = new ScopeClassifier();
        var guarded = new GateProducer<AlwaysPredicate>(
            "All", MethodDefinitionLayers.Flags, new SourceGateGuard(classifier, 0b11));

        MethodDefinitionExecution execution = Run(image, Plan(new ProducerRequest(guarded)));

        // Only N.Sample's public methods; <Module> and <Generated> are excluded.
        Assert.Equal(OrdinaryPublicMethods, execution.ResultOf(guarded).Value);
    }

    // ---- Fixtures ----

    // N.Sample's public methods: Alpha, Apex, Beta, Pointer; plus a private one.
    const int OrdinaryPublicMethods = 4;
    const int OrdinaryPointerMethods = 1;

    static GateFixtureImage Ordinary()
    {
        var builder = new GateFixtureImage();
        builder.Type("N", "Sample")
            .Method("Alpha")
            .Method("Apex")
            .Method("Beta")
            .Method("Pointer", GateFixtureImage.VoidSignature(t => t.Pointer().Int32()))
            .Method("Hidden", attributes: MethodAttributes.Private | MethodAttributes.Static);
        return builder;
    }

    static ImmutableArray<byte> HostileIdentities(int methodCount, int parameterCount, int genericArity)
    {
        var builder = new GateFixtureImage();
        TypeReferenceHandle t = builder.TypeRef("N", "T");
        TypeReferenceHandle g = builder.TypeRef("N", "G");
        var spec = new BlobBuilder();
        spec.WriteByte(0x15);
        spec.WriteByte(0x12);
        spec.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(g));
        spec.WriteCompressedInteger(genericArity);
        for (int i = 0; i < genericArity; i++)
        {
            spec.WriteByte(0x12);
            spec.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(t));
        }

        TypeSpecificationHandle wide = builder.TypeSpec(spec);
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(wide));
            signature.WriteByte(0x08);
        }

        GateFixtureImage.FixtureType type = builder.Type("", "C");
        for (int i = 0; i < methodCount; i++)
            type.Method($"M{i}", Copy(signature));
        return builder.Build();
    }

    static BlobBuilder Copy(BlobBuilder source)
    {
        var copy = new BlobBuilder();
        copy.WriteBytes(source.ToArray());
        return copy;
    }

    static BlobBuilder Bytes(params byte[] bytes)
    {
        var blob = new BlobBuilder();
        blob.WriteBytes(bytes);
        return blob;
    }

    /// <summary>A TypeSpec that modifies <paramref name="type"/> with the previous TypeSpec.</summary>
    static BlobBuilder ModifiedBy(TypeSpecificationHandle modifier, byte type)
    {
        var blob = new BlobBuilder();
        blob.WriteByte(0x20); // CMOD_OPT
        blob.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(modifier));
        blob.WriteByte(type);
        return blob;
    }

    static BlobBuilder ParameterOf(TypeSpecificationHandle type)
    {
        var blob = new BlobBuilder();
        blob.WriteByte(0x00);
        blob.WriteCompressedInteger(1);
        blob.WriteByte(0x01);
        blob.WriteByte(0x20); // CMOD_OPT <TypeSpec>
        blob.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(type));
        blob.WriteByte(0x08);
        return blob;
    }

    static WorkDescription Plan(params ProducerRequest[] requests) =>
        Assert.IsType<ProducerPlanResult.Accepted>(
            ProducerPlanner.Plan(requests)).Description;

    static MethodDefinitionExecution Run(ImmutableArray<byte> image, WorkDescription description)
    {
        using var peReader = new PEReader(image);
        return MethodDefinitionExecution.Execute(description, "GateFixture.dll", peReader);
    }

    // ---- Producers ----

    sealed class GateProducer<TPredicate>(
        string identity,
        MethodDefinitionLayers layers,
        SourceGateGuard? guard = null,
        bool kernel = true)
        : MethodDefinitionPredicateProducer<TPredicate>(identity, version: 1, tier: 0, layers)
        where TPredicate : struct, IMethodDefinitionPredicate
    {
        internal override SourceGateGuard? SourceGate => guard;

        internal override bool AllowsKernel => kernel;
    }

    /// <summary>Public methods on non-compiler-generated types: class 0 when the name starts with "A", else 1.</summary>
    sealed class ScopeClassifier()
        : MethodRowClassifier("Scope", MethodDefinitionLayers.Flags | MethodDefinitionLayers.NameComparison)
    {
        internal override bool TypeInScope(scoped MethodRowTypeView type) => !type.NameStartsWith("<");

        internal override int Classify(scoped MethodDefinitionView row)
        {
            if ((row.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                return -1;
            return row.NameStartsWith("A") ? 0 : 1;
        }
    }

    struct AlwaysPredicate : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => true;
    }

    struct NameAPredicate : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => view.NameStartsWith("A");
    }

    struct RawRowPredicate : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => view.MethodDefinition.RelativeVirtualAddress != 0;
    }

    struct PointerPredicate : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => view.SignatureHasPointer;
    }

    struct AllTextPredicate : IMethodDefinitionPredicate
    {
        public static readonly List<string> Seen = [];

        public readonly bool Test(scoped MethodDefinitionView view)
        {
            MethodRowIdentity identity = view.Identity;
            Seen.Add(identity.MethodName.ToString());
            Seen.Add(identity.DeclaringType.ToString());
            Seen.Add(identity.Namespace.ToString());
            Seen.Add(identity.Signature.ToString());
            if (identity.ReturnType is { } returnType)
                Seen.Add(returnType.ToString());
            if (identity.Anchor is { } anchor)
            {
                Seen.Add(anchor.StableSelector.ToString());
                Seen.Add(anchor.CanonicalSignature.ToString());
                Seen.Add(anchor.TypeFullName.ToString());
                Seen.Add(anchor.MemberName.ToString());
            }

            return true;
        }
    }

    struct ModulePredicate : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => view.PInvokeModuleName is not null;
    }

    struct IdentityPredicate : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => view.Identity.Signature.ToString().Length > 0;
    }

    struct LoggingPredicate : IMethodDefinitionPredicate
    {
        public static readonly List<int> Tokens = [];

        public readonly bool Test(scoped MethodDefinitionView view)
        {
            Tokens.Add(view.Token);
            return true;
        }
    }

    struct PointerNamePredicate : IMethodDefinitionPredicate
    {
        public static readonly Dictionary<string, bool> Seen = [];

        public readonly bool Test(scoped MethodDefinitionView view)
        {
            bool hasPointer = view.SignatureHasPointer;
            Seen[view.Identity.MethodName.ToString()] = hasPointer;
            return hasPointer;
        }
    }
}
