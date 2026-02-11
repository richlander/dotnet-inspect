using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

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
    /// </summary>
    public static List<ILInstruction>? Disassemble(PEReader peReader, MetadataReader reader, MethodDefinition method)
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
            string? operand = DecodeOperand(opCode, ilBytes, ref position, reader);

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
            var fullName = reader.GetFullTypeName(typeDef);

            if (fullName != typeName)
                continue;

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

    static string? DecodeOperand(ILOpCode opCode, byte[] ilBytes, ref int position, MetadataReader reader)
    {
        return GetOperandType(opCode) switch
        {
            OperandKind.None => null,
            OperandKind.ShortBrTarget => FormatBranchTarget(ReadSByte(ilBytes, ref position), position),
            OperandKind.BrTarget => FormatBranchTarget(ReadInt32(ilBytes, ref position), position),
            OperandKind.ShortI => ReadSByte(ilBytes, ref position).ToString(),
            OperandKind.I => ReadInt32(ilBytes, ref position).ToString(),
            OperandKind.I8 => ReadInt64(ilBytes, ref position).ToString(),
            OperandKind.ShortR => ReadSingle(ilBytes, ref position).ToString(),
            OperandKind.R => ReadDouble(ilBytes, ref position).ToString(),
            OperandKind.ShortVariable => ReadByte(ilBytes, ref position).ToString(),
            OperandKind.Variable => ReadUInt16(ilBytes, ref position).ToString(),
            OperandKind.String => ResolveString(ReadInt32(ilBytes, ref position), reader),
            OperandKind.Type => ResolveType(ReadInt32(ilBytes, ref position), reader),
            OperandKind.Method => ResolveMethod(ReadInt32(ilBytes, ref position), reader),
            OperandKind.Field => ResolveField(ReadInt32(ilBytes, ref position), reader),
            OperandKind.Tok => ResolveToken(ReadInt32(ilBytes, ref position), reader),
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

    // Token resolution

    static string ResolveString(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.UserStringHandle(token);
            var value = reader.GetUserString(handle);
            return $"\"{EscapeString(value)}\"";
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    static string ResolveType(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return TypeResolver.GetTypeName(reader, handle) ?? $"0x{token:X8}";
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    static string ResolveMethod(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDef(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => FormatMethodSpec(reader, (MethodSpecificationHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    static string ResolveField(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.FieldDefinition => FormatFieldDef(reader, (FieldDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    static string ResolveToken(int token, MetadataReader reader)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => TypeResolver.GetTypeName(reader, handle) ?? $"0x{token:X8}",
                HandleKind.MethodDefinition => FormatMethodDef(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)handle),
                HandleKind.FieldDefinition => FormatFieldDef(reader, (FieldDefinitionHandle)handle),
                _ => $"0x{token:X8}"
            };
        }
        catch
        {
            return $"0x{token:X8}";
        }
    }

    // Formatting helpers

    static string FormatMethodDef(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        string methodName = reader.GetString(method.Name);
        var declaringType = method.GetDeclaringType();
        string typeName = TypeResolver.GetTypeName(reader, declaringType) ?? "?";
        string baseName = $"{typeName}::{methodName}";

        try
        {
            var sig = method.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
            return $"{baseName}({string.Join(", ", sig.ParameterTypes)})";
        }
        catch
        {
            return baseName;
        }
    }

    static string FormatMemberRef(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberRef = reader.GetMemberReference(handle);
        string name = reader.GetString(memberRef.Name);
        string? parentName = TypeResolver.GetTypeName(reader, memberRef.Parent);
        string baseName = parentName is not null ? $"{parentName}::{name}" : name;

        try
        {
            if (memberRef.GetKind() == MemberReferenceKind.Method)
            {
                var genericCtx = BuildGenericContext(reader, memberRef);
                var sig = memberRef.DecodeMethodSignature(SignatureDecoder.Instance, genericCtx);
                return $"{baseName}({string.Join(", ", sig.ParameterTypes)})";
            }
        }
        catch
        {
            // Fall through to baseName
        }

        return baseName;
    }

    static string FormatMethodSpec(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var spec = reader.GetMethodSpecification(handle);
        string baseName = spec.Method.Kind switch
        {
            HandleKind.MethodDefinition => FormatMethodDef(reader, (MethodDefinitionHandle)spec.Method),
            HandleKind.MemberReference => FormatMemberRef(reader, (MemberReferenceHandle)spec.Method),
            _ => $"0x{MetadataTokens.GetToken(spec.Method):X8}"
        };

        try
        {
            var typeArgs = spec.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
            return $"{baseName}<{string.Join(", ", typeArgs)}>";
        }
        catch
        {
            return baseName;
        }
    }

    static string FormatFieldDef(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var field = reader.GetFieldDefinition(handle);
        string fieldName = reader.GetString(field.Name);
        var declaringType = field.GetDeclaringType();
        string typeName = TypeResolver.GetTypeName(reader, declaringType) ?? "?";
        return $"{typeName}::{fieldName}";
    }

    static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// Build a GenericContext from a MemberReference's parent type for accurate
    /// parameter type rendering on generic type members.
    /// </summary>
    static GenericContext? BuildGenericContext(MetadataReader reader, MemberReference memberRef)
    {
        try
        {
            if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
            {
                var typeSpec = reader.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
                var parentType = typeSpec.DecodeSignature(SignatureDecoder.Instance, genericContext: null);
                var typeArgs = ExtractGenericArguments(parentType);
                if (typeArgs.Count > 0)
                    return new GenericContext(typeArgs, []);
            }
        }
        catch { }
        return null;
    }

    static IReadOnlyList<string> ExtractGenericArguments(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        if (openAngle < 0) return [];

        int depth = 0;
        List<string> args = [];
        int argStart = openAngle + 1;

        for (int i = openAngle; i < typeName.Length; i++)
        {
            switch (typeName[i])
            {
                case '<': depth++; break;
                case '>':
                    depth--;
                    if (depth == 0)
                    {
                        var arg = typeName[argStart..i].Trim();
                        if (arg.Length > 0) args.Add(arg);
                        return args;
                    }
                    break;
                case ',' when depth == 1:
                    args.Add(typeName[argStart..i].Trim());
                    argStart = i + 1;
                    break;
            }
        }
        return args;
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
