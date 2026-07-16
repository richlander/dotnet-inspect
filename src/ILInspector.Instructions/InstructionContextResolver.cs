using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

public record ILOffsetInstructionContextInfo(
    int ILOffset,
    string Boundary,
    string Opcode,
    string OperandKind,
    string? Operand,
    string? OperandToken,
    string? BranchTargets,
    int NextOffset,
    int Length,
    int? Block,
    bool TerminatesBlock,
    bool FallsThrough);

public record ILOffsetCallsiteContextInfo(
    int CallOffset,
    string Opcode,
    string CallKind,
    string Callee,
    string? OperandToken,
    int ReturnAddress);

public record ILOffsetReturnAddressContextInfo(
    int ILOffset,
    int CallOffset,
    string Opcode,
    string CallKind,
    string Callee,
    string? OperandToken);

/// <summary>
/// Resolves decoded IL facts at an exact offset. Metadata token spelling is
/// supplied through <see cref="IOperandNameResolver"/>.
/// </summary>
public static class InstructionContextResolver
{
    public static bool TryDecodeMethod(
        PEReader peReader,
        MetadataReader reader,
        int methodToken,
        out MethodInstructions? instructions,
        out string? error)
    {
        instructions = null;
        error = null;

        var handle = MetadataTokens.Handle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            error = $"Token 0x{methodToken:X} is not a MethodDef token.";
            return false;
        }

        try
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                error = $"Method token 0x{methodToken:X} has no IL body.";
                return false;
            }

            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            instructions = MethodInstructions.Decode(body);
            if (!instructions.IsComplete)
            {
                error = $"Could not decode IL for token 0x{methodToken:X}: {instructions.Blocks.IncompleteReason}";
                instructions = null;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            error = $"Could not decode IL for token 0x{methodToken:X}.";
            return false;
        }
    }

    public static ILOffsetInstructionContextInfo? ResolveInstructionContext(
        MethodInstructions instructions,
        int methodToken,
        int ilOffset,
        IOperandNameResolver resolver,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(resolver);
        error = null;
        var instruction = instructions.InstructionAt(ilOffset);
        if (instruction is null)
        {
            error = $"IL offset 0x{ilOffset:X} is not an instruction boundary for token 0x{methodToken:X}.";
            return null;
        }

        var operand = ResolveInstructionOperand(instruction, resolver);
        var blockIndex = instructions.BlockIndexAt(ilOffset);
        return new ILOffsetInstructionContextInfo(
            ILOffset: ilOffset,
            Boundary: "Exact",
            Opcode: InstructionProducer.GetDisplayName(instruction.OpCode),
            OperandKind: operand.Kind,
            Operand: operand.Value,
            OperandToken: operand.Token,
            BranchTargets: FormatTargets(instruction),
            NextOffset: instruction.NextOffset,
            Length: instruction.Length,
            Block: blockIndex >= 0 ? blockIndex : null,
            TerminatesBlock: instruction.TerminatesBlock,
            FallsThrough: instruction.FallsThrough);
    }

    public static ILOffsetCallsiteContextInfo? ResolveCallsiteContext(
        MethodInstructions instructions,
        int methodToken,
        int ilOffset,
        IOperandNameResolver resolver,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(resolver);
        error = null;
        var instruction = instructions.InstructionAt(ilOffset);
        if (instruction is null)
        {
            error = $"IL offset 0x{ilOffset:X} is not an instruction boundary for token 0x{methodToken:X}.";
            return null;
        }

        if (!IsCallLike(instruction.OpCode))
            return null;

        var operand = ResolveInstructionOperand(instruction, resolver);
        return new ILOffsetCallsiteContextInfo(
            CallOffset: instruction.Offset,
            Opcode: InstructionProducer.GetDisplayName(instruction.OpCode),
            CallKind: GetCallKind(instruction.OpCode),
            Callee: operand.Value ?? operand.Token ?? "",
            OperandToken: operand.Token,
            ReturnAddress: instruction.NextOffset);
    }

    public static ILOffsetReturnAddressContextInfo? ResolveReturnAddressContext(
        MethodInstructions instructions,
        int methodToken,
        int ilOffset,
        IOperandNameResolver resolver,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(resolver);
        error = null;
        var current = instructions.InstructionAt(ilOffset);
        var previous = instructions.InstructionBefore(ilOffset);
        if (current is null && previous is null)
        {
            error = $"IL offset 0x{ilOffset:X} is not an instruction boundary for token 0x{methodToken:X}.";
            return null;
        }

        if (previous is null || !IsCallReturning(previous.OpCode))
            return null;

        var operand = ResolveInstructionOperand(previous, resolver);
        return new ILOffsetReturnAddressContextInfo(
            ILOffset: ilOffset,
            CallOffset: previous.Offset,
            Opcode: InstructionProducer.GetDisplayName(previous.OpCode),
            CallKind: GetCallKind(previous.OpCode),
            Callee: operand.Value ?? operand.Token ?? "",
            OperandToken: operand.Token);
    }

    static (string Kind, string? Value, string? Token) ResolveInstructionOperand(
        DecodedInstruction instruction,
        IOperandNameResolver resolver)
    {
        int token = (int)instruction.OperandValue;
        return instruction.Operand switch
        {
            OperandKind.None => ("None", null, null),
            OperandKind.InlineMethod => ("Method", resolver.ResolveMethod(token), FormatToken(token)),
            OperandKind.InlineField => ("Field", resolver.ResolveField(token), FormatToken(token)),
            OperandKind.InlineType => ("Type", resolver.ResolveType(token), FormatToken(token)),
            OperandKind.InlineString => ("String", resolver.ResolveString(token), FormatToken(token)),
            OperandKind.InlineTok => ("Token", resolver.ResolveToken(token), FormatToken(token)),
            OperandKind.InlineSig => ("Signature", FormatToken(token), FormatToken(token)),
            OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget
                => ("Branch Target", FormatTargets(instruction), null),
            OperandKind.InlineSwitch => ("Switch Targets", FormatTargets(instruction), null),
            OperandKind.ShortInlineVar or OperandKind.InlineVar
                => (IsArgumentOpcode(instruction.OpCode) ? "Argument" : "Local", instruction.OperandValue.ToString(CultureInfo.InvariantCulture), null),
            OperandKind.ShortInlineI or OperandKind.InlineI or OperandKind.InlineI8
                => ("Constant", instruction.OperandValue.ToString(CultureInfo.InvariantCulture), null),
            OperandKind.ShortInlineR
                => ("Constant", BitConverter.Int32BitsToSingle((int)instruction.OperandValue).ToString(CultureInfo.InvariantCulture), null),
            OperandKind.InlineR
                => ("Constant", BitConverter.Int64BitsToDouble(instruction.OperandValue).ToString(CultureInfo.InvariantCulture), null),
            _ => (instruction.Operand.ToString(), instruction.OperandValue.ToString(CultureInfo.InvariantCulture), null)
        };
    }

    static bool IsCallLike(ILOpCode opcode)
        => opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj
            or ILOpCode.Calli or ILOpCode.Ldftn or ILOpCode.Ldvirtftn;

    static bool IsCallReturning(ILOpCode opcode)
        => opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli;

    static string GetCallKind(ILOpCode opcode)
        => opcode switch
        {
            ILOpCode.Call => "direct",
            ILOpCode.Callvirt => "virtual",
            ILOpCode.Newobj => "constructor",
            ILOpCode.Calli => "function pointer",
            ILOpCode.Ldftn => "method pointer",
            ILOpCode.Ldvirtftn => "virtual method pointer",
            _ => "call"
        };

    static bool IsArgumentOpcode(ILOpCode opcode)
        => opcode is ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3;

    static string? FormatTargets(DecodedInstruction instruction)
        => instruction.BranchTargets.IsDefaultOrEmpty
            ? null
            : string.Join(", ", instruction.BranchTargets.Select(FormatILOffset));

    static string FormatILOffset(int offset) => $"IL_{offset:X4}";

    static string FormatToken(int token) => $"0x{token:X8}";
}
