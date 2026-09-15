using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Instructions.Tests;

public class ExceptionFlowFactsTests
{
    static string SelfPath => typeof(ExceptionFlowFactsTests).Assembly.Location;

    [Fact]
    public void MetadataBackedDecodePreservesBodyAndClauseIdentity()
    {
        (MethodBodyData body, MethodInstructions method) =
            Decode(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        InstructionExceptionFlowFacts facts = AvailableFacts(method);

        Assert.Equal(body.EvidenceId, facts.Body);
        Assert.Equal(
            body.ExceptionRegionCatalog.Clauses.Select(clause => clause.Id),
            facts.Clauses.Select(clause => clause.Id));

        InstructionExceptionClause[] catches =
            [.. facts.Clauses.Where(clause => clause.Kind == ExceptionRegionKind.Catch)];
        Assert.Equal(2, catches.Length);
        Assert.Equal(catches[0].ProtectedRegion, catches[1].ProtectedRegion);
        Assert.NotEqual(catches[0].HandlerRegion, catches[1].HandlerRegion);
        Assert.Equal(
            catches.Select(clause => clause.Id),
            facts.Regions.Single(
                region => region.Id == catches[0].ProtectedRegion).Clauses);

        var rePaired = new MethodInstructions(
            method.Instructions,
            method.Blocks);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    rePaired.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.MissingMetadataEvidence,
            unavailable.Reason);

        InstructionBlock copiedBlock = method.Blocks.Blocks[0] with { };
        var blockMismatch = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable>(
                    facts.LocationAt(copiedBlock));
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.BlockMismatch,
            blockMismatch.Reason);

        DecodedInstruction copiedInstruction =
            method.Instructions[0] with { };
        var instructionMismatch = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable>(
                    facts.LocationAt(copiedInstruction));
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InstructionMismatch,
            instructionMismatch.Reason);
    }

    [Fact]
    public void LocationQueriesAreOuterToInnerAndRejectNonInstructionOffsets()
    {
        (_, MethodInstructions method) =
            Decode(nameof(ExceptionFlowFactsSamples.NestedFinally));
        InstructionExceptionFlowFacts facts = AvailableFacts(method);
        DecodedInstruction leave = Assert.Single(
            method.Instructions,
            instruction => instruction.LeavesRegion
                && AvailableTransfer(facts, instruction).CleanupHandlers.Length == 2);

        var location = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Available>(
                    facts.LocationAt(leave.Offset)).Value;
        Assert.Equal(
            location.OrderByDescending(region => region.Extent.Length),
            location);

        var transfer = AvailableTransfer(facts, leave);
        Assert.Equal(InstructionNormalTransferKind.Leave, transfer.Kind);
        Assert.Equal(2, transfer.CleanupHandlers.Length);
        Assert.Equal(
            transfer.CleanupHandlers
                .Select(cleanup => facts.Clauses.Single(
                    clause => clause.Id == cleanup.Clause).Clause.ProtectedExtent.Length)
                .Order(),
            transfer.CleanupHandlers
                .Select(cleanup => facts.Clauses.Single(
                    clause => clause.Id == cleanup.Clause).Clause.ProtectedExtent.Length));

        DecodedInstruction operandInstruction = method.Instructions.First(
            instruction => instruction.Length > 1);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Unavailable>(
                    facts.LocationAt(operandInstruction.Offset + 1));
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.NotInstructionBoundary,
            unavailable.Reason);

        InstructionBlock block = method.Blocks.Blocks[
            method.BlockIndexAt(leave.Offset)];
        var blockLocation = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Available>(
                    facts.LocationAt(block)).Value;
        Assert.Equal(location, blockLocation);
    }

    [Fact]
    public void BranchAndReturnFactsUseCanonicalContinuations()
    {
        (_, MethodInstructions method) =
            Decode(nameof(ExceptionFlowFactsSamples.Branch));
        InstructionExceptionFlowFacts facts = AvailableFacts(method);
        DecodedInstruction branch = Assert.Single(
            method.Instructions,
            instruction => instruction.Branches
                && !instruction.IsUnconditionalBranch);

        int target = Assert.Single(branch.BranchTargets);
        InstructionNormalTransfer taken = AvailableTransfer(
            facts,
            branch,
            target);
        InstructionNormalTransfer fallthrough = AvailableTransfer(
            facts,
            branch,
            branch.NextOffset);
        Assert.Equal(InstructionNormalTransferKind.Branch, taken.Kind);
        Assert.Empty(taken.SourceContext);
        Assert.Empty(taken.DestinationContext);
        Assert.Equal(NormalContinuationKind.Block, taken.Continuation.Kind);
        Assert.Equal(target, taken.Continuation.BlockStart);
        Assert.Equal(branch.NextOffset, fallthrough.Continuation.BlockStart);

        DecodedInstruction directReturn = method.Instructions.First(
            instruction => instruction.OpCode == ILOpCode.Ret);
        InstructionNormalTransfer returned = AvailableTransfer(
            facts,
            directReturn,
            destination: null);
        Assert.Equal(InstructionNormalTransferKind.Return, returned.Kind);
        Assert.Equal(NormalContinuationKind.MethodExit, returned.Continuation.Kind);
        Assert.Equal(-1, returned.Continuation.BlockStart);
    }

    [Fact]
    public void ExplicitBranchTargetDoesNotInheritFallthroughRegionEntry()
    {
        byte[] branchImage = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.ConditionalInTry));
        using var pe = new PEReader(
            new MemoryStream(branchImage, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion clause = Assert.Single(body.ExceptionRegions);
        DecodedInstruction branch = MethodInstructions.Decode(body).Instructions.Single(
            instruction => instruction.Branches
                && !instruction.IsUnconditionalBranch);
        Assert.Equal(OperandKind.ShortInlineBrTarget, branch.Operand);
        Assert.Equal(2, branch.Length);
        int protectedEnd = clause.TryOffset + clause.TryLength;
        Assert.InRange(
            branch.NextOffset,
            clause.TryOffset + 1,
            protectedEnd - 1);

        WriteTryExtent(
            branchImage,
            token,
            clauseOrdinal: 0,
            branch.NextOffset,
            protectedEnd - branch.NextOffset);
        WriteBranchTarget(
            branchImage,
            token,
            branch,
            branch.NextOffset);

        MethodInstructions rejected = MethodInstructions.Decode(
            ReadMutatedBody(branchImage, token));
        Assert.True(rejected.IsComplete, rejected.Blocks.IncompleteReason);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    rejected.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidControlTransfer,
            unavailable.Reason);

        byte[] fallthroughImage = File.ReadAllBytes(SelfPath);
        WriteTryExtent(
            fallthroughImage,
            token,
            clauseOrdinal: 0,
            branch.NextOffset,
            protectedEnd - branch.NextOffset);
        int branchOffset = MethodCodeOffset(fallthroughImage, token) + branch.Offset;
        fallthroughImage[branchOffset] = 0x26;
        fallthroughImage[branchOffset + 1] = 0x00;

        AvailableFacts(MethodInstructions.Decode(
            ReadMutatedBody(fallthroughImage, token)));
    }

    [Fact]
    public void ExceptionalTransfersAreExplicitlyUnavailable()
    {
        (_, MethodInstructions method) =
            Decode(nameof(ExceptionFlowFactsSamples.Throw));
        InstructionExceptionFlowFacts facts = AvailableFacts(method);
        DecodedInstruction throwing = Assert.Single(
            method.Instructions,
            instruction => instruction.OpCode == ILOpCode.Throw);

        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<InstructionNormalTransfer>.Unavailable>(
                facts.NormalTransferAt(throwing.Offset, logicalDestinationOffset: null));
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.UnsupportedTransfer,
            unavailable.Reason);
    }

    [Fact]
    public void FilterAndFinallyKeepTypedRegionRolesAndHalfOpenEnds()
    {
        (_, MethodInstructions method) =
            Decode(nameof(ExceptionFlowFactsSamples.FilterAndFinally));
        InstructionExceptionFlowFacts facts = AvailableFacts(method);
        InstructionExceptionClause filterClause = Assert.Single(
            facts.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Filter);
        Assert.NotNull(filterClause.FilterRegion);
        Assert.Contains(
            facts.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Finally);

        InstructionExceptionRegion filter = facts.Regions.Single(
            region => region.Id == filterClause.FilterRegion);
        InstructionExceptionRegion handler = facts.Regions.Single(
            region => region.Id == filterClause.HandlerRegion);
        var filterContext = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Available>(
                    facts.LocationAt(filter.Extent.Start)).Value;
        Assert.Contains(filterContext, region => region.Id == filter.Id);

        var handlerContext = Assert.IsType<
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Available>(
                    facts.LocationAt(handler.Extent.Start)).Value;
        Assert.DoesNotContain(handlerContext, region => region.Id == filter.Id);
        Assert.Contains(handlerContext, region => region.Id == handler.Id);
    }

    [Fact]
    public void RawBodyDecodeDoesNotInventMetadataEvidence()
    {
        using var stream = File.OpenRead(SelfPath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent))));
        MethodInstructions decoded =
            MethodInstructions.Decode(pe.GetMethodBody(method.RelativeVirtualAddress));

        Assert.True(decoded.IsComplete, decoded.Blocks.IncompleteReason);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    decoded.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.MissingMetadataEvidence,
            unavailable.Reason);
    }

    [Fact]
    public void PrefixInteriorIsNotAnAdmittedControlFlowBoundary()
    {
        byte[] il =
        [
            0x2B, 0x02,       // br.s IL_0004
            0xFE, 0x13,       // volatile.
            0x00,             // nop
            0x2A,             // ret
        ];

        MethodInstructions method = MethodInstructions.Decode(il, il.Length, []);

        Assert.False(method.IsComplete);
        Assert.Contains(
            "does not align with an instruction",
            method.Blocks.IncompleteReason);
    }

    [Fact]
    public void MetadataBackedDecodeRejectsMalformedIl()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        int codeOffset = MethodCodeOffset(image, token);
        image[codeOffset] = 0xFF;

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.DecodeFailure,
            unavailable.Reason);
    }

    [Fact]
    public void MetadataBackedDecodeRejectsRegionInsideInstruction()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion region = body.ExceptionRegions[0];
        DecodedInstruction interior = MethodInstructions.Decode(body).Instructions.First(
            instruction => instruction.Offset > region.TryOffset
                && instruction.Offset < region.TryOffset + region.TryLength
                && instruction.Length > 1);
        int invalidStart = interior.Offset + 1;
        WriteTryExtent(
            image,
            token,
            clauseOrdinal: 0,
            invalidStart,
            region.TryOffset + region.TryLength - invalidStart);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidRegionBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void MetadataBackedDecodeRejectsPrefixInteriorRegionBoundary()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.VolatileCatch));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion region = Assert.Single(body.ExceptionRegions);
        ImmutableArray<DecodedInstruction> instructions =
            MethodInstructions.Decode(body).Instructions;
        int prefixIndex = Enumerable.Range(0, instructions.Length).Single(
            index => instructions[index].OpCode == ILOpCode.Volatile);
        Assert.True(prefixIndex >= 0);
        int invalidStart = instructions[prefixIndex + 1].Offset;
        Assert.True(region.TryOffset <= instructions[prefixIndex].Offset);
        Assert.True(invalidStart < region.TryOffset + region.TryLength);
        WriteTryExtent(
            image,
            token,
            clauseOrdinal: 0,
            invalidStart,
            region.TryOffset + region.TryLength - invalidStart);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidRegionBoundary,
            unavailable.Reason);
    }

    [Fact]
    public void MetadataBackedDecodeRejectsCrossingProtectedRegions()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion first = body.ExceptionRegions[0];
        ExceptionRegion second = body.ExceptionRegions[1];
        Assert.Equal(first.TryOffset, second.TryOffset);
        Assert.Equal(first.TryLength, second.TryLength);

        int originalEnd = first.TryOffset + first.TryLength;
        int crossingStart = MethodInstructions.Decode(body).Instructions.First(
            instruction => instruction.Offset > first.TryOffset
                && instruction.Offset < originalEnd).Offset;
        int crossingEnd = first.HandlerOffset + first.HandlerLength;
        Assert.True(crossingEnd > originalEnd);
        Assert.True(crossingEnd <= second.HandlerOffset);
        WriteTryExtent(
            image,
            token,
            clauseOrdinal: 1,
            crossingStart,
            crossingEnd - crossingStart);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
            unavailable.Reason);
    }

    [Fact]
    public void MetadataBackedDecodeRejectsClauseWithDifferentEnclosingContexts()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.NestedFinally));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion inner = body.ExceptionRegions[0];
        ExceptionRegion outer = body.ExceptionRegions[1];
        Assert.True(inner.TryLength < outer.TryLength);
        Assert.InRange(
            inner.HandlerOffset,
            outer.TryOffset,
            outer.TryOffset + outer.TryLength - 1);

        WriteHandlerExtent(
            image,
            token,
            clauseOrdinal: 0,
            outer.HandlerOffset,
            outer.HandlerLength);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
            unavailable.Reason);
    }

    [Theory]
    [InlineData(nameof(ExceptionFlowFactsSamples.LeaveWithinCatchHandler))]
    [InlineData(nameof(ExceptionFlowFactsSamples.LeaveWithinFinallyHandler))]
    public void LeaveMayRemainInsideAnEnclosingHandler(string methodName)
    {
        (_, MethodInstructions method) = Decode(methodName);
        InstructionExceptionFlowFacts facts = AvailableFacts(method);
        InstructionNormalTransfer[] leaves =
            [.. method.Instructions
                .Where(instruction => instruction.LeavesRegion)
                .Select(instruction => AvailableTransfer(facts, instruction))];

        Assert.Contains(
            leaves,
            transfer => transfer.SourceContext.Any(
                source => source.Id.Role == InstructionExceptionRegionRole.Handler
                    && transfer.DestinationContext.Any(
                        destination => destination.Id == source.Id))
                && transfer.RegionsLeft.All(
                    region => region.Id.Role
                        != InstructionExceptionRegionRole.Handler));
    }

    [Fact]
    public void CatchLeaveMayTargetItsAssociatedProtectedRegion()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion clause = body.ExceptionRegions[0];
        DecodedInstruction leave = MethodInstructions.Decode(body).Instructions.Single(
            instruction => instruction.LeavesRegion
                && instruction.Offset >= clause.HandlerOffset
                && instruction.Offset < clause.HandlerOffset + clause.HandlerLength);
        WriteBranchTarget(
            image,
            token,
            leave,
            clause.TryOffset);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));
        InstructionExceptionFlowFacts facts = AvailableFacts(method);
        InstructionNormalTransfer transfer = Assert.IsType<
            InstructionExceptionFlowResult<InstructionNormalTransfer>.Available>(
                facts.NormalTransferAt(leave.Offset, clause.TryOffset)).Value;

        Assert.Contains(
            transfer.RegionsEntered,
            region => region.Id.Role == InstructionExceptionRegionRole.Protected
                && region.Clauses.Contains(facts.Clauses[0].Id));
        Assert.Empty(transfer.CleanupHandlers);
    }

    [Fact]
    public void BranchCannotReplaceLeaveAcrossAHandlerBoundary()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion clause = body.ExceptionRegions[0];
        DecodedInstruction leave = MethodInstructions.Decode(body).Instructions.Single(
            instruction => instruction.LeavesRegion
                && instruction.Offset >= clause.HandlerOffset
                && instruction.Offset < clause.HandlerOffset + clause.HandlerLength);
        int codeOffset = MethodCodeOffset(image, token);
        image[codeOffset + leave.Offset] = leave.OpCode switch
        {
            ILOpCode.Leave_s => 0x2B,
            ILOpCode.Leave => 0x38,
            _ => throw new InvalidOperationException(),
        };

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.True(method.IsComplete, method.Blocks.IncompleteReason);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidControlTransfer,
            unavailable.Reason);
    }

    [Fact]
    public void ReturnCannotExitAProtectedRegionDirectly()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion clause = body.ExceptionRegions[0];
        DecodedInstruction replacement = MethodInstructions.Decode(body).Instructions.First(
            instruction => instruction.Offset >= clause.TryOffset
                && instruction.Offset < clause.TryOffset + clause.TryLength
                && instruction.Length == 1
                && !instruction.OpCode.IsPrefix());
        image[MethodCodeOffset(image, token) + replacement.Offset] = 0x2A;

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.True(method.IsComplete, method.Blocks.IncompleteReason);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidControlTransfer,
            unavailable.Reason);
    }

    [Fact]
    public void OutOfRangeBranchTargetHasTypedDestinationFailure()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.SharedCatchExtent));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        ExceptionRegion clause = body.ExceptionRegions[0];
        DecodedInstruction leave = MethodInstructions.Decode(body).Instructions.Single(
            instruction => instruction.LeavesRegion
                && instruction.Offset >= clause.HandlerOffset
                && instruction.Offset < clause.HandlerOffset + clause.HandlerLength);
        WriteBranchTarget(
            image,
            token,
            leave,
            body.GetILBytes()!.Length + 1);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.UnknownDestination,
            unavailable.Reason);
    }

    [Fact]
    public void MetadataBackedDecodeRejectsOuterBeforeInnerClauseOrder()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(ExceptionFlowFactsSamples.NestedFinally));
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodBodyBlock body = ReadBody(pe, reader, token);
        Assert.Equal(2, body.ExceptionRegions.Length);
        Assert.True(
            body.ExceptionRegions[0].TryLength
                < body.ExceptionRegions[1].TryLength);

        int sectionOffset = ExceptionSectionOffset(image, token, out bool fat);
        int clauseSize = fat ? 24 : 12;
        byte[] first = image.AsSpan(sectionOffset + 4, clauseSize).ToArray();
        image.AsSpan(sectionOffset + 4 + clauseSize, clauseSize)
            .CopyTo(image.AsSpan(sectionOffset + 4, clauseSize));
        first.CopyTo(image, sectionOffset + 4 + clauseSize);

        MethodInstructions method = MethodInstructions.Decode(
            ReadMutatedBody(image, token));

        Assert.False(method.IsComplete);
        var unavailable = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable>(
                    method.ExceptionFlow);
        Assert.Equal(
            InstructionExceptionFlowUnavailableReason.InvalidRegionTopology,
            unavailable.Reason);
    }

    [Fact]
    public void PreservesRealPlatformFinallyAndFaultKinds()
    {
        MethodInfo textReaderRead = typeof(TextReader).GetMethod(
            nameof(TextReader.Read),
            [typeof(Span<char>)])!;
        using var corelib = AssemblyInspectionSession.Open(
            typeof(TextReader).Assembly.Location);
        MethodBodyData textReaderBody = Assert.IsType<MethodBodyReadResult.Available>(
            corelib.MethodBodies.Read(textReaderRead.MetadataToken)).Body;
        MethodInstructions textReaderInstructions =
            MethodInstructions.Decode(textReaderBody);
        InstructionExceptionFlowFacts textReaderFacts =
            AvailableFacts(textReaderInstructions);
        Assert.Contains(
            textReaderFacts.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Finally);
        InstructionNormalTransfer[] textReaderTransfers =
            [.. textReaderInstructions.Instructions
                .Where(instruction => instruction.LeavesRegion)
                .Select(instruction => AvailableTransfer(
                    textReaderFacts,
                    instruction))];
        InstructionNormalTransfer textReaderLeave = Assert.Single(
            textReaderTransfers,
            transfer => transfer.CleanupHandlers.Length == 1);
        InstructionCleanupHandler textReaderCleanup =
            Assert.Single(textReaderLeave.CleanupHandlers);
        InstructionExceptionClause textReaderFinally =
            textReaderFacts.Clauses.Single(
                clause => clause.Id == textReaderCleanup.Clause);
        Assert.Equal(ExceptionRegionKind.Finally, textReaderFinally.Kind);
        Assert.Equal(
            textReaderFinally.HandlerRegion,
            textReaderCleanup.Handler);
        Assert.Equal(
            NormalContinuationKind.Block,
            textReaderLeave.Continuation.Kind);

        using var regex = AssemblyInspectionSession.Open(
            typeof(System.Text.RegularExpressions.Regex).Assembly.Location);
        MethodBodyMember faultWitness = Assert.Single(
            regex.MethodBodies.EnumerateMethods(),
            method => method.Name == "MoveNext"
                && method.DeclaringType.Contains(
                    "<EnumerateAlternationBranches>d__",
                    StringComparison.Ordinal));
        MethodBodyData faultBody = Assert.IsType<MethodBodyReadResult.Available>(
            regex.MethodBodies.Read(faultWitness.MetadataToken)).Body;
        InstructionExceptionFlowFacts faultFacts =
            AvailableFacts(MethodInstructions.Decode(faultBody));
        Assert.Contains(
            faultFacts.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Fault);
    }

    static (MethodBodyData Body, MethodInstructions Instructions) Decode(
        string methodName)
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodyData body = Assert.IsType<MethodBodyReadResult.Available>(
            session.MethodBodies.Read(TokenOf(methodName))).Body;
        return (body, MethodInstructions.Decode(body));
    }

    static InstructionExceptionFlowFacts AvailableFacts(
        MethodInstructions method)
    {
        Assert.True(method.IsComplete, method.Blocks.IncompleteReason);
        return Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available>(
                    method.ExceptionFlow).Value;
    }

    static InstructionNormalTransfer AvailableTransfer(
        InstructionExceptionFlowFacts facts,
        DecodedInstruction instruction,
        int? destination = null)
    {
        destination ??= instruction.BranchTargets is [int target]
            ? target
            : null;
        return Assert.IsType<
            InstructionExceptionFlowResult<InstructionNormalTransfer>.Available>(
                facts.NormalTransferAt(instruction.Offset, destination)).Value;
    }

    static int TokenOf(string methodName) =>
        typeof(ExceptionFlowFactsSamples)
            .GetMethod(methodName)!
            .MetadataToken;

    static MethodBodyData ReadMutatedBody(byte[] image, int methodToken)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-eh-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            using var session = AssemblyInspectionSession.Open(path);
            return Assert.IsType<MethodBodyReadResult.Available>(
                session.MethodBodies.Read(methodToken)).Body;
        }
        finally
        {
            File.Delete(path);
        }
    }

    static MethodBodyBlock ReadBody(
        PEReader pe,
        MetadataReader reader,
        int methodToken)
    {
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
        return pe.GetMethodBody(method.RelativeVirtualAddress);
    }

    static void WriteTryExtent(
        byte[] image,
        int methodToken,
        int clauseOrdinal,
        int start,
        int length)
    {
        int sectionOffset = ExceptionSectionOffset(
            image,
            methodToken,
            out bool fat);
        int clauseOffset = sectionOffset + 4 + clauseOrdinal * (fat ? 24 : 12);
        if (fat)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(clauseOffset + 4, 4),
                start);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(clauseOffset + 8, 4),
                length);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(clauseOffset + 2, 2),
                checked((ushort)start));
            image[clauseOffset + 4] = checked((byte)length);
        }
    }

    static void WriteHandlerExtent(
        byte[] image,
        int methodToken,
        int clauseOrdinal,
        int start,
        int length)
    {
        int sectionOffset = ExceptionSectionOffset(
            image,
            methodToken,
            out bool fat);
        int clauseOffset = sectionOffset + 4 + clauseOrdinal * (fat ? 24 : 12);
        if (fat)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(clauseOffset + 12, 4),
                start);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(clauseOffset + 16, 4),
                length);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(clauseOffset + 5, 2),
                checked((ushort)start));
            image[clauseOffset + 7] = checked((byte)length);
        }
    }

    static void WriteBranchTarget(
        byte[] image,
        int methodToken,
        DecodedInstruction branch,
        int target)
    {
        int displacement = checked(target - branch.NextOffset);
        Span<byte> operand = image.AsSpan(
            MethodCodeOffset(image, methodToken) + branch.OperandOffset);
        if (branch.Operand == OperandKind.ShortInlineBrTarget)
        {
            operand[0] = unchecked((byte)checked((sbyte)displacement));
        }
        else
        {
            Assert.Equal(OperandKind.InlineBrTarget, branch.Operand);
            BinaryPrimitives.WriteInt32LittleEndian(operand, displacement);
        }
    }

    static int MethodCodeOffset(byte[] image, int methodToken)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
        int bodyOffset = RvaToFileOffset(
            pe.PEHeaders,
            method.RelativeVirtualAddress);
        byte first = image[bodyOffset];
        if ((first & 0x03) == 0x02)
            return bodyOffset + 1;

        ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(bodyOffset, 2));
        Assert.Equal(0x03, flagsAndSize & 0x03);
        return bodyOffset + (flagsAndSize >> 12) * 4;
    }

    static int ExceptionSectionOffset(
        byte[] image,
        int methodToken,
        out bool fat)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
        int bodyOffset = RvaToFileOffset(
            pe.PEHeaders,
            method.RelativeVirtualAddress);
        ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(bodyOffset, 2));
        Assert.Equal(0x03, flagsAndSize & 0x03);
        int headerSize = (flagsAndSize >> 12) * 4;
        int codeSize = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(bodyOffset + 4, 4));
        int sectionOffset = (bodyOffset + headerSize + codeSize + 3) & ~3;
        byte sectionKind = image[sectionOffset];
        Assert.Equal(0x01, sectionKind & 0x3F);
        fat = (sectionKind & 0x40) != 0;
        return sectionOffset;
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        int sectionIndex = headers.GetContainingSectionIndex(rva);
        Assert.True(sectionIndex >= 0);
        SectionHeader section = headers.SectionHeaders[sectionIndex];
        return checked(rva - section.VirtualAddress + section.PointerToRawData);
    }
}

public static class ExceptionFlowFactsSamples
{
    static int s_sink;
    static volatile int s_volatile = 1;

    public static int SharedCatchExtent(int value)
    {
        try
        {
            return checked(100 / value);
        }
        catch (DivideByZeroException)
        {
            return -1;
        }
        catch (OverflowException)
        {
            return -2;
        }
    }

    public static int NestedFinally(int value)
    {
        try
        {
            try
            {
                return value + 1;
            }
            finally
            {
                Sink(value);
            }
        }
        finally
        {
            Sink(value + 1);
        }
    }

    public static int FilterAndFinally(int value)
    {
        try
        {
            if (value < 0)
                throw new InvalidOperationException("negative");
            return value + 1;
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Length > 0)
        {
            return -1;
        }
        finally
        {
            Sink(value);
        }
    }

    public static int VolatileCatch()
    {
        try
        {
            return s_volatile;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public static int Branch(bool condition) =>
        condition ? 1 : 2;

    public static int ConditionalInTry(bool condition)
    {
        try
        {
            return condition ? 1 : 2;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public static int LeaveWithinCatchHandler(int value)
    {
        int result;
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            try
            {
                result = value;
            }
            finally
            {
                Sink(value);
            }
            result++;
        }
        return result;
    }

    public static int LeaveWithinFinallyHandler(int value)
    {
        int result = 0;
        try
        {
            result = value;
        }
        finally
        {
            try
            {
                result += value;
            }
            finally
            {
                Sink(value);
            }
            result++;
        }
        return result;
    }

    public static void Throw() =>
        throw new InvalidOperationException();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Sink(int value) => s_sink += value;
}
