using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ILDiff;
using Inspector.Findings;

namespace ILInspector.ILDiff.Tests;

/// <summary>
/// The IL census cursor (<c>docs/design/finding-census.md#the-cursor</c>) is
/// the one producer of canonical IL: draining it over raw IL yields the atoms
/// and terminal a whole read through <see cref="IlBodyDiff.TryCanonicalize(MethodInstructions, MetadataReader?, IlBodyDiffNormalization, out ImmutableArray{CanonicalIlOperation}, out string?)"/>
/// yields, in canonical order, with every atom before a fault kept and the
/// fault's IL offset as the terminal's position.
/// </summary>
public class IlCensusCursorTests
{
    const byte Nop = 0x00, Ldc0 = 0x16, Ldc1 = 0x17, Ldc4 = 0x20, Pop = 0x26, Ret = 0x2A, BrS = 0x2B, Call = 0x28;
    const byte Undefined = 0x24;

    static readonly FindingSubject Subject = new("M", "M");

    /// <summary>
    /// Completeness over real images: for every method of two assemblies, the
    /// raw-IL cursor, the cursor over the decoded body, and canonicalization
    /// agree atom for atom and terminal for terminal.
    /// </summary>
    [Theory]
    [InlineData(typeof(IlCensusCursorTests))]
    [InlineData(typeof(IlBodyDiff))]
    public void RawAndDecodedSources_DrainAlike_ForEveryMethod(Type anchor)
    {
        using var stream = File.OpenRead(anchor.Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        int complete = 0;
        int absent = 0;
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            var subject = new FindingSubject($"0x{MetadataTokens.GetToken(handle):X8}", reader.GetString(reader.GetMethodDefinition(handle).Name));
            FindingInspection<CanonicalIlOperation> raw = IlCensusCursor.Create(pe, reader, handle, subject, IlBodyDiffPolicy.Body).Drain();

            int rva = reader.GetMethodDefinition(handle).RelativeVirtualAddress;
            MethodInstructions? body = rva == 0 ? null : MethodInstructions.Decode(pe.GetMethodBody(rva));
            FindingInspection<CanonicalIlOperation> decoded = IlCensusCursor.Create(body, reader, subject, IlBodyDiffPolicy.Body).Drain();

            Assert.Equal(decoded, raw);
            switch (raw.Value)
            {
                case FindingInspection<CanonicalIlOperation>.Complete census:
                    Assert.True(IlBodyDiff.TryCanonicalize(body!, reader, IlBodyDiffPolicy.Body, out var operations, out _));
                    Assert.Equal<CanonicalIlOperation>(operations, census.Findings.Select(atom => atom.Payload));
                    Assert.Equal(Enumerable.Range(0, operations.Length), census.Findings.Select(atom => atom.Ordinal!.Value));
                    Assert.DoesNotContain(operations, operation => CanonicalIlOperation.IndexesOf(operation).Contains(CanonicalIlOperation.UndecidedTarget));
                    complete++;
                    break;
                case FindingInspection<CanonicalIlOperation>.Absent:
                    Assert.Equal(0, rva);
                    absent++;
                    break;
                case FindingInspection<CanonicalIlOperation>.Failed failed:
                    Assert.False(IlBodyDiff.TryCanonicalize(body!, reader, IlBodyDiffPolicy.Body, out _, out string? failure));
                    Assert.Equal(failure, failed.Error.Reason);
                    break;
            }
        }

        Assert.True(complete > 100, $"{complete} complete censuses, {absent} absent");
    }

    /// <summary>
    /// A sealed census outlives its side's reader: the operand text a view
    /// shows is projected at <see cref="IlCensusCursor.Drain"/>, so reading it
    /// after the image is disposed (and, over a file, unmapped) is safe and
    /// equal to the text read while the reader lived.
    /// </summary>
    [Fact]
    public void SealedCensus_KeepsItsOperandText_AfterTheReaderIsDisposed()
    {
        string path = typeof(IlBodyDiff).Assembly.Location;
        var expected = new Dictionary<int, string>();
        FindingInspection<CanonicalIlOperation> retained;
        MethodDefinitionHandle method;
        using (var pe = new PEReader(File.OpenRead(path)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            method = reader.MethodDefinitions.First(handle =>
                reader.GetMethodDefinition(handle).RelativeVirtualAddress != 0
                && MethodInstructions.Decode(pe.GetMethodBody(reader.GetMethodDefinition(handle).RelativeVirtualAddress))
                    .Instructions.Count(instruction => instruction.OpCode == ILOpCode.Call) >= 2);
            IlCensusSide side = IlCensusSide.Create(reader, IlBodyDiffPolicy.Body);
            retained = IlFindings.Inspect(side, pe, method, Subject);
            using var again = new PEReader(File.OpenRead(path));
            var check = IlFindings.Inspect(IlCensusSide.Create(again.GetMetadataReader(), IlBodyDiffPolicy.Body), again, method, Subject);
            foreach (var atom in ((FindingInspection<CanonicalIlOperation>.Complete)check.Value).Findings)
            {
                if (atom.Payload.Operand is { } operand)
                    expected[atom.Payload.Offset] = operand.Value;
            }
        }

        var census = Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(retained.Value);
        int calls = 0;
        foreach (var atom in census.Findings)
        {
            if (atom.Payload.Operand is not { } operand)
                continue;
            Assert.Equal(expected[atom.Payload.Offset], operand.Value);
            if (atom.Payload.OpcodeFamily == "call")
            {
                Assert.Contains("::", operand.Value, StringComparison.Ordinal);
                calls++;
            }
        }
        Assert.True(calls >= 2, $"{calls} call atoms");
    }

    [Fact]
    public void Drain_IsRepeatable_AndCanonicalOrderIsInstructionsThenClauses()
    {
        (byte[] image, int body) = IlBodyDiffExceptionRegionTests.Image(IlBodyDiffExceptionRegionTests.Shape.NestedCatchInFinally);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method = MetadataTokens.MethodDefinitionHandle(2);

        var first = Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(IlCensusCursor.Create(pe, reader, method, Subject).Drain().Value);
        var second = Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(IlCensusCursor.Create(pe, reader, method, Subject).Drain().Value);

        Assert.Equal(first, second);
        int instructions = MethodInstructions.Decode(pe.GetMethodBody(body)).Instructions.Length;
        Assert.Equal(instructions + 2, first.Findings.Length);
        Assert.All(first.Findings.Take(instructions), atom => Assert.False(atom.Payload.IsExceptionClause));
        Assert.Equal(["catch", "finally"], first.Findings.Skip(instructions).Select(atom => atom.Payload.OpcodeFamily));
        // Each clause boundary's index is the instruction it starts, or the
        // body end, which only the end aligns with.
        MethodInstructions decoded = MethodInstructions.Decode(pe.GetMethodBody(body));
        int ilLength = decoded.Instructions[^1].NextOffset;
        Assert.All(first.Findings.Skip(instructions), atom =>
        {
            Assert.Equal(4, atom.Payload.TargetIndexes.Length);
            Assert.Equal<int>(
                atom.Payload.Targets.Select(offset => offset == ilLength
                    ? CanonicalIlOperation.BodyEndTarget
                    : decoded.Instructions.ToList().FindIndex(instruction => instruction.Offset == offset)),
                atom.Payload.TargetIndexes);
        });
    }

    [Fact]
    public void Advance_YieldsOneAtomPerCall_AndNeverPassesItsTerminal()
    {
        IlCensusCursor cursor = Raw([Ldc0, Ldc1, Pop, Ret]);

        Assert.Null(cursor.Terminal);
        Assert.Equal(0, cursor.Position);
        Assert.True(cursor.Advance());
        Assert.Equal("ldc.i4", cursor.Current!.OpcodeFamily);
        Assert.Equal(1, cursor.Position);
        Assert.True(cursor.Advance());
        Assert.True(cursor.Advance());
        Assert.True(cursor.Advance());
        Assert.Equal("ret", cursor.Current!.OpcodeFamily);
        Assert.False(cursor.Advance());
        Assert.Null(cursor.Current);
        Assert.Equal(IlCensusTerminalKind.Complete, cursor.Terminal!.Kind);
        Assert.Equal(4, cursor.Terminal.Position);
        Assert.False(cursor.Advance());
        Assert.Equal(4, cursor.Position);
        Assert.Throws<InvalidOperationException>(cursor.Drain);
    }

    [Fact]
    public void BranchTargets_AreInstructionIndexes_AndShiftedOffsetsAlign()
    {
        // br.s +1 over nop: the target is the first ret at offset 3, index 2.
        var old = Drain([BrS, 0x01, Nop, Ret, Nop, Ret]);
        // The same body with a nop inserted first: the target's offset and
        // index both move, and only the alignment relates them.
        var inserted = Drain([Nop, BrS, 0x01, Nop, Ret, Nop, Ret]);
        // The same body with the branch re-encoded long: the offset moves,
        // the index does not, so the atoms are equal in canonical bytes.
        var reencoded = Drain([0x38, 0x01, 0x00, 0x00, 0x00, Nop, Ret, Nop, Ret]);

        Assert.Equal<int>([2], old.Findings[0].Payload.TargetIndexes);
        Assert.Equal<int>([3], old.Findings[0].Payload.Targets);
        Assert.Equal<int>([3], inserted.Findings[1].Payload.TargetIndexes);
        Assert.Equal<int>([4], inserted.Findings[1].Payload.Targets);
        Assert.Equal<int>([2], reencoded.Findings[0].Payload.TargetIndexes);
        Assert.Equal<int>([6], reencoded.Findings[0].Payload.Targets);
        Assert.True(IlBodyDiff.AtomsEqual(old.Findings[0].Payload, reencoded.Findings[0].Payload));
        Assert.False(IlBodyDiff.AtomsEqual(old.Findings[0].Payload, inserted.Findings[1].Payload));
        Assert.True(IlFindings.Compare(old, reencoded).IsExact);

        var comparison = Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(IlFindings.Compare(old, inserted).Value);
        Assert.DoesNotContain(comparison.Pairs, pair => pair.Kind == PairKind.Changed);
        Assert.Equal(1, comparison.Pairs.Count(pair => pair.Kind == PairKind.Added));
    }

    [Fact]
    public void Retarget_IsCaughtByTargetValidation_WithoutBodies()
    {
        var old = Drain([BrS, 0x03, Nop, Ret, Nop, Ret]);
        var retargeted = Drain([BrS, 0x01, Nop, Ret, Nop, Ret]);

        var comparison = Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(IlFindings.Compare(old, retargeted).Value);
        var changed = Assert.Single(comparison.Pairs, pair => pair.Kind == PairKind.Changed);
        Assert.Equal("branch retargeted", changed.Detail);
    }

    /// <summary>
    /// Fault order: every atom before an undefined opcode is yielded, and the
    /// terminal is failed at the opcode's offset.
    /// </summary>
    [Fact]
    public void UndefinedOpcode_YieldsThePrefix_ThenFailsAtItsOffset()
    {
        IlCensusCursor cursor = Raw([Ldc0, Ldc1, Undefined, Ret]);

        Assert.True(cursor.Advance());
        Assert.True(cursor.Advance());
        Assert.False(cursor.Advance());
        Assert.Equal(IlCensusTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(IlCensusFailureStage.Reading, cursor.Terminal.Stage);
        Assert.Equal(2, cursor.Terminal.Position);
        Assert.Contains("IL_0002", cursor.Terminal.Reason);

        var failed = Assert.IsType<FindingInspection<CanonicalIlOperation>.Failed>(Drain([Ldc0, Ldc1, Undefined, Ret], expectComplete: false).Value);
        Assert.Equal(cursor.Terminal.Reason, failed.Error.Reason);
        Assert.Equal(IlFindings.InspectionDescriptor, failed.Error.Descriptor);
    }

    [Fact]
    public void TrailingPrefix_FailsAtThePrefix()
    {
        IlCensusCursor cursor = Raw([Nop, 0xFE, 0x13]); // nop; volatile.

        Assert.True(cursor.Advance());
        Assert.False(cursor.Advance());
        Assert.Equal(IlCensusTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(1, cursor.Terminal.Position);
    }

    [Fact]
    public void TruncatedOperand_FailsBeforeAnyAtom()
    {
        IlCensusCursor cursor = Raw([Ldc4, 0x01]);

        Assert.False(cursor.Advance());
        Assert.Equal(0, cursor.Position);
        Assert.Equal(IlCensusTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(0, cursor.Terminal.Position);
    }

    /// <summary>
    /// A target the decoder rejects is carried undecided, every atom is still
    /// yielded, and the stream ends failed at the offset of the first such
    /// atom instead of complete.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { BrS, 0x02, Ldc4, 0x00, 0x00, 0x00, 0x00, Pop, Ret }, 4)] // into an operand
    [InlineData(new byte[] { BrS, 0x02, 0xFE, 0x13, 0x4A, Pop, Ret }, 5)] // into a prefixed instruction
    [InlineData(new byte[] { BrS, 0x02, Nop, Ret }, 3)] // to the end of the body
    [InlineData(new byte[] { BrS, 0x7F, Nop, Ret }, 3)] // past the end of the body
    public void UndecidableTarget_IsCarriedUndecided_AndTheStreamEndsFailed(byte[] il, int atoms)
    {
        IlCensusCursor cursor = Raw(il);

        for (int i = 0; i < atoms; i++)
            Assert.True(cursor.Advance(), $"atom {i}");
        Assert.False(cursor.Advance());
        Assert.Equal(IlCensusTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(0, cursor.Terminal.Position);

        IlCensusCursor again = Raw(il);
        Assert.True(again.Advance());
        Assert.Equal<int>([CanonicalIlOperation.UndecidedTarget], again.Current!.TargetIndexes);
        Assert.False(MethodInstructions.Decode(il, il.Length, Array.Empty<ExceptionRegion>()).IsComplete);
    }

    [Fact]
    public void DifferenceBeforeAFault_IsVisibleInTheAtoms()
    {
        // The two streams differ at their first atom and then both hold an
        // undefined opcode: each yields that atom, so a lockstep reader decides
        // before either fault.
        IlCensusCursor a = Raw([Ldc0, Undefined, Ret]);
        IlCensusCursor b = Raw([Ldc1, Undefined, Ret]);

        Assert.True(a.Advance());
        Assert.True(b.Advance());
        Assert.NotEqual(a.Current!.Operand, b.Current!.Operand);
        Assert.False(a.Advance());
        Assert.False(b.Advance());
        Assert.Equal(1, a.Terminal!.Position);
        Assert.Equal(1, b.Terminal!.Position);
    }

    [Fact]
    public void MetadataTokenWithoutAReader_FailsAtTheOperand()
    {
        IlCensusCursor cursor = Raw([Nop, Call, 0x01, 0x00, 0x00, 0x06, Ret]);

        Assert.True(cursor.Advance());
        Assert.False(cursor.Advance());
        Assert.Equal(1, cursor.Terminal!.Position);
        Assert.Contains("requires a MetadataReader-backed comparison", cursor.Terminal.Reason);
    }

    /// <summary>
    /// A body the image cannot read after a non-zero RVA is present and
    /// unreadable: a Reading failure at position zero, never an identity one,
    /// so a lockstep reader decides Changed against an absent side.
    /// </summary>
    [Fact]
    public void UnreadableBodyHeader_IsAReadingFailure_NotAnIdentityFailure()
    {
        (byte[] image, int rva) = IlBodyDiffExceptionRegionTests.Image(IlBodyDiffExceptionRegionTests.Shape.CatchInvalidOperation);
        using (var probe = new PEReader(new MemoryStream(image)))
        {
            var section = probe.PEHeaders.SectionHeaders.Single(section =>
                rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData);
            image[section.PointerToRawData + rva - section.VirtualAddress] = 0; // neither a tiny nor a fat header
        }
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method = MetadataTokens.MethodDefinitionHandle(2);
        Assert.Throws<BadImageFormatException>(() => pe.GetMethodBody(reader.GetMethodDefinition(method).RelativeVirtualAddress));

        IlCensusCursor cursor = IlCensusCursor.Create(pe, reader, method, Subject);

        Assert.False(cursor.Advance());
        Assert.Equal(IlCensusTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(IlCensusFailureStage.Reading, cursor.Terminal.Stage);
        Assert.Equal(0, cursor.Terminal.Position);
        Assert.IsType<FindingInspection<CanonicalIlOperation>.Failed>(IlFindings.Inspect(pe, reader, method, Subject).Value);
    }

    [Fact]
    public void NoBody_IsAbsentBeforeAnyAtom()
    {
        IlCensusCursor cursor = IlCensusCursor.Create(body: null, reader: null, Subject);

        Assert.False(cursor.Advance());
        Assert.Equal(IlCensusTerminalKind.Absent, cursor.Terminal!.Kind);
        Assert.Equal(FindingInspectionAbsenceKind.NoApplicableInput, cursor.Terminal.AbsenceKind);
        var absent = Assert.IsType<FindingInspection<CanonicalIlOperation>.Absent>(IlCensusCursor.Create(body: null, reader: null, Subject).Drain().Value);
        Assert.Equal(FindingInspectionAbsenceKind.NoApplicableInput, absent.Kind);
    }

    [Fact]
    public void IncompleteDecodedBody_FailsAtPositionZero_WithTheDecodeReason()
    {
        MethodInstructions incomplete = MethodInstructions.Decode([Ldc0, Undefined, Ret], 3, Array.Empty<ExceptionRegion>());
        Assert.False(incomplete.IsComplete);

        IlCensusCursor cursor = IlCensusCursor.Create(incomplete, reader: null, Subject);

        Assert.False(cursor.Advance());
        Assert.Equal(IlCensusTerminalKind.Failed, cursor.Terminal!.Kind);
        Assert.Equal(0, cursor.Terminal.Position);
        Assert.Equal(incomplete.Blocks.IncompleteReason, cursor.Terminal.Reason);
    }

    [Fact]
    public void Policy_IsFixedAtCreation_AndTwoSidedNormalizationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IlCensusCursor.Create([Ret], Array.Empty<ExceptionRegion>(), reader: null, Subject, IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals));
        Assert.Equal(IlBodyDiffPolicy.Body, Raw([Ret], IlBodyDiffPolicy.Body).Policy);
    }

    static IlCensusCursor Raw(byte[] il, IlBodyDiffNormalization policy = IlBodyDiffNormalization.None)
        => IlCensusCursor.Create(il, Array.Empty<ExceptionRegion>(), reader: null, Subject, policy);

    static FindingInspection<CanonicalIlOperation>.Complete Drain(byte[] il)
        => Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(Drain(il, expectComplete: true).Value);

    static FindingInspection<CanonicalIlOperation> Drain(byte[] il, bool expectComplete)
    {
        FindingInspection<CanonicalIlOperation> inspection = Raw(il).Drain();
        Assert.Equal(expectComplete, inspection is FindingInspection<CanonicalIlOperation>.Complete);
        return inspection;
    }
}
