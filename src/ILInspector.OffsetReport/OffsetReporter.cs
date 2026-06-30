using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.OffsetReport;

/// <summary>
/// Resolves the structural <see cref="OffsetReport"/> at a (method token, IL offset). SRM-only and
/// PDB-free: it decodes the one method body through the <c>ILInspector.Instructions</c> substrate and
/// resolves operand/handler names via <see cref="ILTokenResolver"/>. No analysis index, no decompiler
/// IR, no inspected-assembly loading. Semantic facts (allocations, callee semantics, lifetimes) and
/// PDB source are separate overlays that join onto this spine by offset.
/// </summary>
public static class OffsetReporter
{
    /// <summary>
    /// The structural facts at (<paramref name="methodToken"/>, <paramref name="ilOffset"/>) in the
    /// assembly at <paramref name="assemblyPath"/>. Null when the token is not a method definition, the
    /// method has no body, or the offset is not an instruction boundary.
    /// </summary>
    public static OffsetReport? Resolve(string assemblyPath, int methodToken, int ilOffset)
    {
        ArgumentNullException.ThrowIfNull(assemblyPath);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return null;
            return Resolve(pe, pe.GetMetadataReader(), methodToken, ilOffset);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>Resolve against an already-open PE/reader. Same null contract as the path overload.</summary>
    public static OffsetReport? Resolve(PEReader pe, MetadataReader reader, int methodToken, int ilOffset)
    {
        ArgumentNullException.ThrowIfNull(pe);
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            var handle = MetadataTokens.EntityHandle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return null;

            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
                return null; // abstract/extern: no body to name an instruction in

            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var mi = MethodInstructions.Decode(body);

            var at = mi.InstructionAt(ilOffset);
            if (at is null)
                return null; // offset is not an instruction boundary

            return new OffsetReport(
                ILTokenResolver.ResolveMethod(reader, methodToken),
                methodToken,
                ilOffset,
                at.OpCode,
                OpName(at.OpCode),
                ResolveOperand(reader, at),
                ExceptionContextAt(reader, body, ilOffset),
                ReturnAddressAt(reader, mi, ilOffset),
                IsBranchTarget(mi, ilOffset));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    static OffsetOperand? ResolveOperand(MetadataReader reader, DecodedInstruction at)
    {
        switch (at.Operand)
        {
            case OperandKind.InlineMethod:
                return new(OffsetOperandKind.Method, ILTokenResolver.ResolveMethod(reader, (int)at.OperandValue));
            case OperandKind.InlineField:
                return new(OffsetOperandKind.Field, ILTokenResolver.ResolveField(reader, (int)at.OperandValue));
            case OperandKind.InlineType:
                return new(OffsetOperandKind.Type, ILTokenResolver.ResolveType(reader, (int)at.OperandValue));
            case OperandKind.InlineString:
                return new(OffsetOperandKind.String, ILTokenResolver.ResolveString(reader, (int)at.OperandValue));
            case OperandKind.InlineTok:
                return new(OffsetOperandKind.Token, ILTokenResolver.ResolveToken(reader, (int)at.OperandValue));
            case OperandKind.InlineSig:
                return new(OffsetOperandKind.IndirectSignature, $"calli signature 0x{(int)at.OperandValue:X8}");
            case OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget:
                return new(OffsetOperandKind.BranchTargets,
                    string.Join(", ", at.BranchTargets.Select(t => $"IL_{t:X4}")));
            case OperandKind.InlineSwitch:
                return new(OffsetOperandKind.BranchTargets,
                    $"{at.BranchTargets.Length} targets: {string.Join(", ", at.BranchTargets.Select(t => $"IL_{t:X4}"))}");
            case OperandKind.ShortInlineVar or OperandKind.InlineVar:
                bool isArg = at.OpCode is ILOpCode.Ldarg or ILOpCode.Ldarg_s or ILOpCode.Ldarga or ILOpCode.Ldarga_s
                    or ILOpCode.Starg or ILOpCode.Starg_s;
                return new(isArg ? OffsetOperandKind.Argument : OffsetOperandKind.Local,
                    $"{(isArg ? "arg" : "local")} {at.OperandValue}");
            case OperandKind.ShortInlineI or OperandKind.InlineI or OperandKind.InlineI8:
                return new(OffsetOperandKind.Constant, at.OperandValue.ToString(CultureInfo.InvariantCulture));
            case OperandKind.ShortInlineR:
                return new(OffsetOperandKind.Constant,
                    BitConverter.Int32BitsToSingle((int)at.OperandValue).ToString(CultureInfo.InvariantCulture));
            case OperandKind.InlineR:
                return new(OffsetOperandKind.Constant,
                    BitConverter.Int64BitsToDouble(at.OperandValue).ToString(CultureInfo.InvariantCulture));
            default:
                return null;
        }
    }

    static OffsetExceptionContext? ExceptionContextAt(MetadataReader reader, MethodBodyBlock body, int offset)
    {
        // Innermost (smallest) containing region wins. The catch type comes from the body's SRM regions —
        // the substrate's normalized BlockGraph regions drop it.
        OffsetExceptionContext? best = null;
        int bestSpan = int.MaxValue;
        foreach (var region in body.ExceptionRegions)
        {
            string? position;
            int start, end;
            if (offset >= region.TryOffset && offset < region.TryOffset + region.TryLength)
            {
                position = "try";
                start = region.TryOffset;
                end = region.TryOffset + region.TryLength;
            }
            else if (offset >= region.HandlerOffset && offset < region.HandlerOffset + region.HandlerLength)
            {
                position = region.Kind switch
                {
                    ExceptionRegionKind.Catch => "catch",
                    ExceptionRegionKind.Finally => "finally",
                    ExceptionRegionKind.Fault => "fault",
                    ExceptionRegionKind.Filter => "filter-handler",
                    _ => "handler",
                };
                start = region.HandlerOffset;
                end = region.HandlerOffset + region.HandlerLength;
            }
            else if (region.Kind == ExceptionRegionKind.Filter && offset >= region.FilterOffset && offset < region.HandlerOffset)
            {
                position = "filter";
                start = region.FilterOffset;
                end = region.HandlerOffset;
            }
            else
            {
                continue;
            }

            int span = end - start;
            if (span >= bestSpan)
                continue;

            string? catchType = region.Kind == ExceptionRegionKind.Catch && !region.CatchType.IsNil
                ? ILTokenResolver.ResolveType(reader, MetadataTokens.GetToken(region.CatchType))
                : null;
            best = new OffsetExceptionContext(MapKind(region.Kind), position, start, end, catchType);
            bestSpan = span;
        }
        return best;
    }

    static ReturnAddressSite? ReturnAddressAt(MetadataReader reader, MethodInstructions mi, int offset)
    {
        var prev = mi.InstructionBefore(offset);
        if (prev is null || !IsCall(prev.OpCode))
            return null;
        string callee = prev.OpCode == ILOpCode.Calli
            ? $"calli signature 0x{(int)prev.OperandValue:X8}"
            : ILTokenResolver.ResolveMethod(reader, (int)prev.OperandValue);
        return new ReturnAddressSite(prev.OpCode, prev.Offset, callee);
    }

    static bool IsBranchTarget(MethodInstructions mi, int offset)
    {
        foreach (var instruction in mi.Instructions)
            if (instruction.BranchTargets.Contains(offset))
                return true;
        return false;
    }

    static HandlerKind MapKind(ExceptionRegionKind kind) => kind switch
    {
        ExceptionRegionKind.Catch => HandlerKind.Catch,
        ExceptionRegionKind.Filter => HandlerKind.Filter,
        ExceptionRegionKind.Finally => HandlerKind.Finally,
        ExceptionRegionKind.Fault => HandlerKind.Fault,
        _ => HandlerKind.Catch,
    };

    static bool IsCall(ILOpCode op) =>
        op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli;

    static string OpName(ILOpCode op) =>
        op.ToString().Replace('_', '.').ToLowerInvariant();
}
