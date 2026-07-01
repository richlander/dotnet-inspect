using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Metadata;

/// <summary>
/// A single decoded IL instruction with its offset, opcode, and resolved operand.
/// </summary>
public record ILInstruction(int Offset, string OpCodeName, string? Operand = null)
{
    /// <summary>
    /// Formats the instruction as "IL_XXXX: opcode operand".
    /// </summary>
    public override string ToString()
    {
        return Operand is null
            ? $"IL_{Offset:X4}: {OpCodeName}"
            : $"IL_{Offset:X4}: {OpCodeName,-12} {Operand}";
    }
}

/// <summary>
/// Disassembles IL method bodies into human-readable instructions.
/// Uses System.Reflection.Metadata to decode opcodes and resolve metadata tokens.
/// Operand type classification uses a lookup table derived from ILSpy (MIT license).
/// </summary>
public static class ILDisassembler
{
    /// <summary>
    /// Disassembles a method body into a list of IL instructions.
    /// Returns null if the method has no IL body (abstract, extern, etc.).
    /// <paramref name="syntax"/> selects display rendering (default) or canonical
    /// ilasm operand syntax (see <see cref="ILSyntax"/>).
    /// </summary>
    public static List<ILInstruction>? Disassemble(PEReader peReader, MetadataReader reader, MethodDefinition method, ILSyntax syntax = ILSyntax.Display)
    {
        if (method.RelativeVirtualAddress == 0)
            return null;

        MethodBodyBlock body;
        try
        {
            body = peReader.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch
        {
            return null;
        }

        var ilBytes = body.GetILContent().ToArray();
        var decoded = InstructionDecoder.Decode(ilBytes);

        var instructions = new List<ILInstruction>(decoded.Length);
        foreach (var instruction in decoded)
        {
            instructions.Add(new ILInstruction(
                instruction.Offset, GetDisplayName(instruction.OpCode), FormatOperand(instruction, reader, syntax)));
        }

        return instructions;
    }

    /// <summary>
    /// Finds a method by name on a type and disassembles it.
    /// Returns null if the method is not found or has no IL body.
    /// </summary>
    public static List<ILInstruction>? DisassembleMethod(PEReader peReader, string typeName, string methodName)
        => DisassembleMethod(peReader, typeName, methodName, overloadIndex: 0);

    public static List<ILInstruction>? DisassembleMethod(PEReader peReader, string typeName, string methodName, int overloadIndex, bool publicOnly = false)
    {
        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeName)
                continue;

            return DisassembleMethod(peReader, reader, typeDefHandle, methodName, overloadIndex, publicOnly);
        }

        return null;
    }

    /// <summary>
    /// Overload for callers that have already resolved the declaring type handle, avoiding a repeated
    /// TypeDefinitions scan per method.
    /// </summary>
    public static List<ILInstruction>? DisassembleMethod(
        PEReader peReader, MetadataReader reader, TypeDefinitionHandle typeHandle, string methodName, int overloadIndex, bool publicOnly = false)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);

        int matchCount = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != methodName)
                continue;

            if (publicOnly && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                continue;

            if (matchCount == overloadIndex)
                return Disassemble(peReader, reader, method);

            matchCount++;
        }

        return null;
    }

    static string? FormatOperand(DecodedInstruction instruction, MetadataReader reader, ILSyntax syntax)
    {
        bool canonical = syntax == ILSyntax.Canonical;
        int token = (int)instruction.OperandValue;
        return instruction.Operand switch
        {
            OperandKind.None => null,
            OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget => $"IL_{instruction.BranchTargets[0]:X4}",
            OperandKind.ShortInlineI => ((sbyte)instruction.OperandValue).ToString(),
            OperandKind.InlineI => ((int)instruction.OperandValue).ToString(),
            OperandKind.InlineI8 => instruction.OperandValue.ToString(),
            // Canonical: bit-exact, culture-free ilasm forms (float32/float64 take
            // the raw bits); display keeps the human-readable decimal rendering.
            OperandKind.ShortInlineR => canonical
                ? $"float32(0x{(int)instruction.OperandValue:X8})"
                : BitConverter.Int32BitsToSingle((int)instruction.OperandValue).ToString(),
            OperandKind.InlineR => canonical
                ? $"float64(0x{instruction.OperandValue:X16})"
                : BitConverter.Int64BitsToDouble(instruction.OperandValue).ToString(),
            OperandKind.ShortInlineVar => ((byte)instruction.OperandValue).ToString(),
            OperandKind.InlineVar => ((ushort)instruction.OperandValue).ToString(),
            OperandKind.InlineString => canonical
                ? CanonicalIL.ResolveString(reader, token)
                : ILTokenResolver.ResolveString(reader, token),
            OperandKind.InlineType => canonical
                ? CanonicalIL.ResolveType(reader, token)
                : ILTokenResolver.ResolveType(reader, token),
            OperandKind.InlineMethod => canonical
                ? CanonicalIL.ResolveMethod(reader, token)
                : ILTokenResolver.ResolveMethod(reader, token),
            OperandKind.InlineField => canonical
                ? CanonicalIL.ResolveField(reader, token)
                : ILTokenResolver.ResolveField(reader, token),
            OperandKind.InlineTok => canonical
                ? CanonicalIL.ResolveToken(reader, token)
                : ILTokenResolver.ResolveToken(reader, token),
            OperandKind.InlineSig => $"0x{token:X8}",
            OperandKind.InlineSwitch => $"({string.Join(", ", instruction.BranchTargets.Select(t => $"IL_{t:X4}"))})",
            _ => null
        };
    }

    static string GetDisplayName(ILOpCode opCode)
    {
        ushort index = (ushort)((((int)opCode & 0x200) >> 1) | ((int)opCode & 0xFF));
        if (index >= s_displayNames.Length)
            return opCode.ToString();
        string name = s_displayNames[index];
        return string.IsNullOrEmpty(name) ? opCode.ToString() : name;
    }

    static readonly string[] s_displayNames = [
        "nop", "break", "ldarg.0", "ldarg.1", "ldarg.2", "ldarg.3", "ldloc.0", "ldloc.1",
        "ldloc.2", "ldloc.3", "stloc.0", "stloc.1", "stloc.2", "stloc.3", "ldarg.s", "ldarga.s",
        "starg.s", "ldloc.s", "ldloca.s", "stloc.s", "ldnull", "ldc.i4.m1", "ldc.i4.0", "ldc.i4.1",
        "ldc.i4.2", "ldc.i4.3", "ldc.i4.4", "ldc.i4.5", "ldc.i4.6", "ldc.i4.7", "ldc.i4.8", "ldc.i4.s",
        "ldc.i4", "ldc.i8", "ldc.r4", "ldc.r8", "", "dup", "pop", "jmp",
        "call", "calli", "ret", "br.s", "brfalse.s", "brtrue.s", "beq.s", "bge.s",
        "bgt.s", "ble.s", "blt.s", "bne.un.s", "bge.un.s", "bgt.un.s", "ble.un.s", "blt.un.s",
        "br", "brfalse", "brtrue", "beq", "bge", "bgt", "ble", "blt",
        "bne.un", "bge.un", "bgt.un", "ble.un", "blt.un", "switch", "ldind.i1", "ldind.u1",
        "ldind.i2", "ldind.u2", "ldind.i4", "ldind.u4", "ldind.i8", "ldind.i", "ldind.r4", "ldind.r8",
        "ldind.ref", "stind.ref", "stind.i1", "stind.i2", "stind.i4", "stind.i8", "stind.r4", "stind.r8",
        "add", "sub", "mul", "div", "div.un", "rem", "rem.un", "and",
        "or", "xor", "shl", "shr", "shr.un", "neg", "not", "conv.i1",
        "conv.i2", "conv.i4", "conv.i8", "conv.r4", "conv.r8", "conv.u4", "conv.u8", "callvirt",
        "cpobj", "ldobj", "ldstr", "newobj", "castclass", "isinst", "conv.r.un", "", "",
        "unbox", "throw", "ldfld", "ldflda", "stfld", "ldsfld", "ldsflda", "stsfld",
        "stobj", "conv.ovf.i1.un", "conv.ovf.i2.un", "conv.ovf.i4.un", "conv.ovf.i8.un",
        "conv.ovf.u1.un", "conv.ovf.u2.un", "conv.ovf.u4.un", "conv.ovf.u8.un",
        "conv.ovf.i.un", "conv.ovf.u.un", "box", "newarr", "ldlen", "ldelema",
        "ldelem.i1", "ldelem.u1", "ldelem.i2", "ldelem.u2", "ldelem.i4", "ldelem.u4",
        "ldelem.i8", "ldelem.i", "ldelem.r4", "ldelem.r8", "ldelem.ref",
        "stelem.i", "stelem.i1", "stelem.i2", "stelem.i4", "stelem.i8", "stelem.r4", "stelem.r8",
        "stelem.ref", "ldelem", "stelem", "unbox.any",
        "", "", "", "", "", "", "", "", "", "", "", "", "",
        "conv.ovf.i1", "conv.ovf.u1", "conv.ovf.i2", "conv.ovf.u2", "conv.ovf.i4", "conv.ovf.u4",
        "conv.ovf.i8", "conv.ovf.u8",
        "", "", "", "", "", "", "",
        "refanyval", "ckfinite", "", "", "mkrefany",
        "", "", "", "", "", "", "", "", "",
        "ldtoken", "conv.u2", "conv.u1", "conv.i", "conv.ovf.i", "conv.ovf.u",
        "add.ovf", "add.ovf.un", "mul.ovf", "mul.ovf.un", "sub.ovf", "sub.ovf.un",
        "endfinally", "leave", "leave.s", "stind.i", "conv.u",
        "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "",
        "prefix7", "prefix6", "prefix5", "prefix4", "prefix3", "prefix2", "prefix1", "prefixref",
        "arglist", "ceq", "cgt", "cgt.un", "clt", "clt.un", "ldftn", "ldvirtftn",
        "", "ldarg", "ldarga", "starg", "ldloc", "ldloca", "stloc",
        "localloc", "", "endfilter", "unaligned.", "volatile.", "tail.",
        "initobj", "constrained.", "cpblk", "initblk", "", "rethrow",
        "", "sizeof", "refanytype", "readonly.",
    ];
}
