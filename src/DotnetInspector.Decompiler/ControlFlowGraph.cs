// Ported from dotnet/runtime src/coreclr/tools/Common/TypeSystem/IL/FlowGraph.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;

namespace DotnetInspector.Decompiler;

/// <summary>
/// A contiguous sequence of IL instructions with a single entry point and single exit point.
/// Ported from dotnet/runtime Internal.IL.BasicBlock.
/// </summary>
public sealed class BasicBlock : IEquatable<BasicBlock>
{
    public BasicBlock(int start, int size)
        => (Start, Size) = (start, size);

    /// <summary>First IL offset of this basic block.</summary>
    public int Start { get; }

    /// <summary>Number of IL bytes in this basic block.</summary>
    public int Size { get; }

    /// <summary>Basic blocks that branch/fall-through into this block.</summary>
    public HashSet<BasicBlock> Sources { get; } = [];

    /// <summary>Basic blocks this block branches/falls-through to.</summary>
    public HashSet<BasicBlock> Targets { get; } = [];

    /// <summary>The explicit branch target (operand of a conditional/unconditional branch), or null.</summary>
    public BasicBlock? BranchTarget { get; set; }

    /// <summary>The fall-through target (next sequential block after a conditional branch), or null.</summary>
    public BasicBlock? FallthroughTarget { get; set; }

    /// <summary>The opcode of the terminating branch instruction, if any.</summary>
    public ILOpCode TerminatingBranch { get; set; }

    public override string ToString() => $"BB[IL_{Start:X4}..IL_{Start + Size:X4})";
    public override bool Equals(object? obj) => Equals(obj as BasicBlock);
    public bool Equals(BasicBlock? other) => other is not null && Start == other.Start;
    public override int GetHashCode() => Start;

    public static bool operator ==(BasicBlock? left, BasicBlock? right)
        => ReferenceEquals(left, right) || (left is not null && left.Equals(right));
    public static bool operator !=(BasicBlock? left, BasicBlock? right) => !(left == right);
}

/// <summary>
/// Control flow graph built from IL bytecode and exception regions.
/// Splits IL into basic blocks and links them with successor/predecessor edges.
/// Ported from dotnet/runtime Internal.IL.FlowGraph, adapted to use
/// <see cref="MethodBodyContext"/> and BCL types.
/// </summary>
public sealed class ControlFlowGraph
{
    private readonly int[] _bbKeys;

    ControlFlowGraph(IEnumerable<BasicBlock> bbs)
    {
        BasicBlocks = bbs.OrderBy(bb => bb.Start).ToList();
        _bbKeys = BasicBlocks.Select(bb => bb.Start).ToArray();
    }

    /// <summary>Basic blocks ordered by start IL offset.</summary>
    public List<BasicBlock> BasicBlocks { get; }

    /// <summary>Find the index of the basic block containing the given IL offset.</summary>
    public int LookupIndex(int ilOffset)
    {
        int index = Array.BinarySearch(_bbKeys, ilOffset);
        if (index < 0)
            index = ~index - 1;

        if (index < 0)
            return -1;

        BasicBlock bb = BasicBlocks[index];
        if (ilOffset >= bb.Start + bb.Size)
            return -1;

        return index;
    }

    /// <summary>Find the basic block containing the given IL offset, or null.</summary>
    public BasicBlock? Lookup(int ilOffset)
        => LookupIndex(ilOffset) switch
        {
            -1 => null,
            int idx => BasicBlocks[idx]
        };

    /// <summary>
    /// Enumerate all basic blocks whose start offsets fall within [ilOffsetStart, ilOffsetEnd].
    /// </summary>
    public IEnumerable<BasicBlock> LookupRange(int ilOffsetStart, int ilOffsetEnd)
    {
        if (BasicBlocks.Count == 0)
            yield break;

        if (ilOffsetStart < BasicBlocks[0].Start)
            ilOffsetStart = BasicBlocks[0].Start;

        if (ilOffsetEnd > BasicBlocks[^1].Start)
            ilOffsetEnd = BasicBlocks[^1].Start;

        int end = LookupIndex(ilOffsetEnd);
        for (int i = LookupIndex(ilOffsetStart); i <= end; i++)
            yield return BasicBlocks[i];
    }

    /// <summary>
    /// Builds a control flow graph from a method body context.
    /// </summary>
    public static ControlFlowGraph Create(MethodBodyContext context)
    {
        var ilBytes = context.ILBytes.AsSpan();
        HashSet<int> bbStarts = GetBasicBlockStarts(context);

        List<BasicBlock> bbs = [];
        int prevStart = 0;

        foreach (int ofs in bbStarts.Order())
        {
            if (ofs - prevStart > 0)
                bbs.Add(new BasicBlock(prevStart, ofs - prevStart));
            prevStart = ofs;
        }

        if (ilBytes.Length - prevStart > 0)
            bbs.Add(new BasicBlock(prevStart, ilBytes.Length - prevStart));

        var cfg = new ControlFlowGraph(bbs);

        // Link basic blocks by analyzing branch targets and fall-through
        var reader = new ILReaderLite(ilBytes);
        foreach (BasicBlock bb in cfg.BasicBlocks)
        {
            reader.Seek(bb.Start);
            while (reader.HasNext)
            {
                if (cfg.Lookup(reader.Offset) != bb)
                    break;

                ILOpCode opc = reader.ReadILOpcode();

                if (opc.IsBranch())
                {
                    int target = reader.ReadBranchDestination(opc);
                    var targetBB = cfg.Lookup(target);
                    if (targetBB is not null)
                    {
                        bb.Targets.Add(targetBB);
                        bb.BranchTarget = targetBB;
                    }
                    bb.TerminatingBranch = opc;
                    if (!opc.IsUnconditionalBranch())
                    {
                        var fallthrough = cfg.Lookup(reader.Offset);
                        if (fallthrough is not null)
                        {
                            bb.Targets.Add(fallthrough);
                            bb.FallthroughTarget = fallthrough;
                        }
                    }
                    break;
                }

                if (opc == ILOpCode.Switch)
                {
                    uint numCases = reader.ReadILUInt32();
                    int jmpBase = reader.Offset + checked((int)(numCases * 4));
                    var defaultBB = cfg.Lookup(jmpBase);
                    if (defaultBB is not null)
                        bb.Targets.Add(defaultBB);

                    for (uint i = 0; i < numCases; i++)
                    {
                        int caseOfs = jmpBase + (int)reader.ReadILUInt32();
                        var caseBB = cfg.Lookup(caseOfs);
                        if (caseBB is not null)
                            bb.Targets.Add(caseBB);
                    }
                    break;
                }

                if (opc is ILOpCode.Ret or ILOpCode.Endfinally or ILOpCode.Endfilter
                    or ILOpCode.Throw or ILOpCode.Rethrow)
                {
                    break;
                }

                if (!reader.TrySkip(opc))
                    break;
                if (reader.HasNext)
                {
                    BasicBlock? nextBB = cfg.Lookup(reader.Offset);
                    if (nextBB is not null && nextBB != bb)
                    {
                        bb.Targets.Add(nextBB);
                        break;
                    }
                }
            }
        }

        // Build reverse edges
        foreach (BasicBlock bb in cfg.BasicBlocks)
        {
            foreach (BasicBlock target in bb.Targets)
                target.Sources.Add(bb);
        }

        return cfg;
    }

    /// <summary>
    /// Identifies IL offsets where basic blocks begin: offset 0, branch targets,
    /// instructions after conditional branches, exception region boundaries.
    /// </summary>
    static HashSet<int> GetBasicBlockStarts(MethodBodyContext context)
    {
        var ilBytes = context.ILBytes.AsSpan();
        var reader = new ILReaderLite(ilBytes);
        HashSet<int> bbStarts = [0];

        while (reader.HasNext)
        {
            ILOpCode opc = reader.ReadILOpcode();

            if (opc.IsBranch())
            {
                int target = reader.ReadBranchDestination(opc);
                bbStarts.Add(target);
                if (!opc.IsUnconditionalBranch())
                    bbStarts.Add(reader.Offset);
            }
            else if (opc == ILOpCode.Switch)
            {
                uint numCases = reader.ReadILUInt32();
                int jmpBase = reader.Offset + checked((int)(numCases * 4));
                bbStarts.Add(jmpBase);
                for (uint i = 0; i < numCases; i++)
                {
                    int caseOfs = jmpBase + (int)reader.ReadILUInt32();
                    bbStarts.Add(caseOfs);
                }
            }
            else if (opc is ILOpCode.Ret or ILOpCode.Endfinally or ILOpCode.Endfilter
                     or ILOpCode.Throw or ILOpCode.Rethrow)
            {
                if (reader.HasNext)
                    bbStarts.Add(reader.Offset);
            }
            else
            {
                if (!reader.TrySkip(opc))
                    break;
            }
        }

        // Exception region boundaries create basic block splits
        foreach (var region in context.ExceptionRegions)
        {
            bbStarts.Add(region.TryOffset);
            bbStarts.Add(region.TryOffset + region.TryLength);
            bbStarts.Add(region.HandlerOffset);
            bbStarts.Add(region.HandlerOffset + region.HandlerLength);
            if (region.Kind == ExceptionRegionKind.Filter)
                bbStarts.Add(region.FilterOffset);
        }

        // Filter out offsets beyond IL range
        int ilLength = ilBytes.Length;
        bbStarts.RemoveWhere(o => o < 0 || o >= ilLength);

        return bbStarts;
    }
}
