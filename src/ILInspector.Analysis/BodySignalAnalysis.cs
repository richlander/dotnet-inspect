using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Analysis;

internal static class BodySignalAnalysis
{
    internal static BodySignals Collect(
        MethodBodyAnalysisContext context,
        Func<int, bool> isAllocatingValueTypeBox)
    {
        int newarr = 0;
        int throws = 0;
        int boxes = 0;
        bool allocInLoop = false;
        var arrayOffsets = ImmutableArray.CreateBuilder<int>();
        var throwOffsets = ImmutableArray.CreateBuilder<int>();
        var boxOffsets = ImmutableArray.CreateBuilder<int>();
        var throwPathNewObjectOffsets =
            ImmutableArray.CreateBuilder<int>();
        try
        {
            foreach (var instruction in context.Instructions.Instructions)
            {
                int offset = instruction.Offset;
                switch (instruction.OpCode)
                {
                    case ILOpCode.Newarr:
                        newarr++;
                        arrayOffsets.Add(offset);
                        allocInLoop |= IsInLoopRegion(
                            offset,
                            context.LoopRegions);
                        break;
                    case ILOpCode.Throw or ILOpCode.Rethrow:
                        throws++;
                        throwOffsets.Add(offset);
                        break;
                    case ILOpCode.Newobj:
                        if (MethodBodyFlowProbe.NewObjectFeedsThrowSoon(
                                context.Instructions,
                                instruction.NextOffset))
                        {
                            throwPathNewObjectOffsets.Add(offset);
                        }
                        break;
                    case ILOpCode.Box:
                        int token = checked((int)instruction.OperandValue);
                        if (isAllocatingValueTypeBox(token))
                        {
                            boxes++;
                            boxOffsets.Add(offset);
                            allocInLoop |= IsInLoopRegion(
                                offset,
                                context.LoopRegions);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
            when (ex is
                BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            // Preserve partial signals collected before malformed evidence.
        }

        int catches = 0;
        int finallys = 0;
        foreach (var region in context.ExceptionRegions)
        {
            switch (region.Kind)
            {
                case ExceptionRegionKind.Catch
                    or ExceptionRegionKind.Filter:
                    catches++;
                    break;
                case ExceptionRegionKind.Finally
                    or ExceptionRegionKind.Fault:
                    finallys++;
                    break;
            }
        }

        return new BodySignals(
            newarr,
            throws,
            catches,
            finallys,
            arrayOffsets.ToImmutable(),
            throwOffsets.ToImmutable(),
            boxes,
            boxOffsets.ToImmutable(),
            allocInLoop,
            throwPathNewObjectOffsets.ToImmutable());
    }

    static bool IsInLoopRegion(
        int offset,
        IReadOnlyList<(int Start, int End)> regions)
        => regions.Any(
            region => offset >= region.Start && offset <= region.End);
}
