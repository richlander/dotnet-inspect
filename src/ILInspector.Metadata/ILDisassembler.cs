using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
        List<ILInstruction> instructions = [];
        int position = 0;

        while (position < ilBytes.Length)
        {
            int offset = position;
            var opCode = DecodeOpCode(ilBytes, ref position);
            string? operand = DecodeOperand(opCode, ilBytes, ref position, reader, syntax);

            instructions.Add(new ILInstruction(offset, GetDisplayName(opCode), operand));
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

    static ILOpCode DecodeOpCode(byte[] ilBytes, ref int position)
    {
        byte first = ilBytes[position++];
        if (first == 0xFE && position < ilBytes.Length)
        {
            byte second = ilBytes[position++];
            return (ILOpCode)(0xFE00 | second);
        }
        return (ILOpCode)first;
    }

    static string? DecodeOperand(ILOpCode opCode, byte[] ilBytes, ref int position, MetadataReader reader, ILSyntax syntax = ILSyntax.Display)
    {
        bool canonical = syntax == ILSyntax.Canonical;
        return GetOperandType(opCode) switch
        {
            OperandKind.None => null,
            OperandKind.ShortBrTarget => FormatBranchTarget(ReadSByte(ilBytes, ref position), position),
            OperandKind.BrTarget => FormatBranchTarget(ReadInt32(ilBytes, ref position), position),
            OperandKind.ShortI => ReadSByte(ilBytes, ref position).ToString(),
            OperandKind.I => ReadInt32(ilBytes, ref position).ToString(),
            OperandKind.I8 => ReadInt64(ilBytes, ref position).ToString(),
            // Canonical: bit-exact, culture-free ilasm forms (float32/float64 take
            // the raw bits); display keeps the human-readable decimal rendering.
            OperandKind.ShortR => canonical
                ? $"float32(0x{BitConverter.SingleToInt32Bits(ReadSingle(ilBytes, ref position)):X8})"
                : ReadSingle(ilBytes, ref position).ToString(),
            OperandKind.R => canonical
                ? $"float64(0x{BitConverter.DoubleToInt64Bits(ReadDouble(ilBytes, ref position)):X16})"
                : ReadDouble(ilBytes, ref position).ToString(),
            OperandKind.ShortVariable => ReadByte(ilBytes, ref position).ToString(),
            OperandKind.Variable => ReadUInt16(ilBytes, ref position).ToString(),
            OperandKind.String => canonical
                ? CanonicalIL.ResolveString(reader, ReadInt32(ilBytes, ref position))
                : ILTokenResolver.ResolveString(reader, ReadInt32(ilBytes, ref position)),
            OperandKind.Type => canonical
                ? CanonicalIL.ResolveType(reader, ReadInt32(ilBytes, ref position))
                : ILTokenResolver.ResolveType(reader, ReadInt32(ilBytes, ref position)),
            OperandKind.Method => canonical
                ? CanonicalIL.ResolveMethod(reader, ReadInt32(ilBytes, ref position))
                : ILTokenResolver.ResolveMethod(reader, ReadInt32(ilBytes, ref position)),
            OperandKind.Field => canonical
                ? CanonicalIL.ResolveField(reader, ReadInt32(ilBytes, ref position))
                : ILTokenResolver.ResolveField(reader, ReadInt32(ilBytes, ref position)),
            OperandKind.Tok => canonical
                ? CanonicalIL.ResolveToken(reader, ReadInt32(ilBytes, ref position))
                : ILTokenResolver.ResolveToken(reader, ReadInt32(ilBytes, ref position)),
            OperandKind.Sig => $"0x{ReadInt32(ilBytes, ref position):X8}",
            OperandKind.Switch => DecodeSwitch(ilBytes, ref position),
            _ => null
        };
    }

    static string FormatBranchTarget(int delta, int positionAfterOperand)
    {
        int target = positionAfterOperand + delta;
        return $"IL_{target:X4}";
    }

    static string DecodeSwitch(byte[] ilBytes, ref int position)
    {
        int count = ReadInt32(ilBytes, ref position);
        int baseOffset = position + count * 4;
        List<string> targets = [];
        for (int i = 0; i < count; i++)
        {
            int delta = ReadInt32(ilBytes, ref position);
            targets.Add($"IL_{(baseOffset + delta):X4}");
        }
        return $"({string.Join(", ", targets)})";
    }

    // Primitive readers

    static byte ReadByte(byte[] bytes, ref int pos) => bytes[pos++];
    static sbyte ReadSByte(byte[] bytes, ref int pos) => (sbyte)bytes[pos++];

    static ushort ReadUInt16(byte[] bytes, ref int pos)
    {
        var val = BitConverter.ToUInt16(bytes, pos);
        pos += 2;
        return val;
    }

    static int ReadInt32(byte[] bytes, ref int pos)
    {
        var val = BitConverter.ToInt32(bytes, pos);
        pos += 4;
        return val;
    }

    static long ReadInt64(byte[] bytes, ref int pos)
    {
        var val = BitConverter.ToInt64(bytes, pos);
        pos += 8;
        return val;
    }

    static float ReadSingle(byte[] bytes, ref int pos)
    {
        var val = BitConverter.ToSingle(bytes, pos);
        pos += 4;
        return val;
    }

    static double ReadDouble(byte[] bytes, ref int pos)
    {
        var val = BitConverter.ToDouble(bytes, pos);
        pos += 8;
        return val;
    }

    // Operand type lookup table and display names from ILSpy (MIT license, Daniel Grunwald).
    // Index computation: ((opCode & 0x200) >> 1) | (opCode & 0xFF)

    enum OperandKind : byte
    {
        BrTarget,
        Field,
        I,
        I8,
        Method,
        None,
        R = 7,
        Sig = 9,
        String,
        Switch,
        Tok,
        Type,
        Variable,
        ShortBrTarget,
        ShortI,
        ShortR,
        ShortVariable
    }

    static OperandKind GetOperandType(ILOpCode opCode)
    {
        ushort index = (ushort)((((int)opCode & 0x200) >> 1) | ((int)opCode & 0xFF));
        if (index >= s_operandTypes.Length)
            return OperandKind.None;
        return (OperandKind)s_operandTypes[index];
    }

    static string GetDisplayName(ILOpCode opCode)
    {
        ushort index = (ushort)((((int)opCode & 0x200) >> 1) | ((int)opCode & 0xFF));
        if (index >= s_displayNames.Length)
            return opCode.ToString();
        string name = s_displayNames[index];
        return string.IsNullOrEmpty(name) ? opCode.ToString() : name;
    }

    static readonly byte[] s_operandTypes = [
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.ShortVariable, (byte)OperandKind.ShortVariable,
        (byte)OperandKind.ShortVariable, (byte)OperandKind.ShortVariable, (byte)OperandKind.ShortVariable, (byte)OperandKind.ShortVariable, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.ShortI,
        (byte)OperandKind.I, (byte)OperandKind.I8, (byte)OperandKind.ShortR, (byte)OperandKind.R, 255, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.Method,
        (byte)OperandKind.Method, (byte)OperandKind.Sig, (byte)OperandKind.None, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget,
        (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.ShortBrTarget,
        (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget,
        (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.BrTarget, (byte)OperandKind.Switch, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.Method,
        (byte)OperandKind.Type, (byte)OperandKind.Type, (byte)OperandKind.String, (byte)OperandKind.Method, (byte)OperandKind.Type, (byte)OperandKind.Type, (byte)OperandKind.None, 255,
        255, (byte)OperandKind.Type, (byte)OperandKind.None, (byte)OperandKind.Field, (byte)OperandKind.Field, (byte)OperandKind.Field, (byte)OperandKind.Field, (byte)OperandKind.Field,
        (byte)OperandKind.Field, (byte)OperandKind.Type, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.Type, (byte)OperandKind.Type, (byte)OperandKind.None, (byte)OperandKind.Type,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.Type, (byte)OperandKind.Type, (byte)OperandKind.Type, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, 255, 255, 255, 255, 255,
        255, 255, (byte)OperandKind.Type, (byte)OperandKind.None, 255, 255, (byte)OperandKind.Type, 255,
        255, 255, 255, 255, 255, 255, 255, 255,
        (byte)OperandKind.Tok, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.BrTarget, (byte)OperandKind.ShortBrTarget, (byte)OperandKind.None,
        (byte)OperandKind.None, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None,
        (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.Method, (byte)OperandKind.Method,
        255, (byte)OperandKind.Variable, (byte)OperandKind.Variable, (byte)OperandKind.Variable, (byte)OperandKind.Variable, (byte)OperandKind.Variable, (byte)OperandKind.Variable, (byte)OperandKind.None,
        255, (byte)OperandKind.None, (byte)OperandKind.ShortI, (byte)OperandKind.None, (byte)OperandKind.None, (byte)OperandKind.Type, (byte)OperandKind.Type, (byte)OperandKind.None,
        (byte)OperandKind.None, 255, (byte)OperandKind.None, 255, (byte)OperandKind.Type, (byte)OperandKind.None, (byte)OperandKind.None,
    ];

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
