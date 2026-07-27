using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// A rendered projection of one decoded IL instruction: opcode display/canonical name plus
/// formatted operand text. This is deliberately <b>not</b> a second decoded-instruction model —
/// offset identity, opcode enum, operand kind/value, branch targets, and control-flow facts live
/// solely on <see cref="DecodedInstruction"/> / <see cref="MethodInstructions"/>
/// (<c>ILInspector.Instructions</c>); this record carries only rendered text and its offset.
/// </summary>
public record ILInstructionText(int Offset, string OpCodeName, string? Operand = null)
{
    /// <summary>Formats the instruction as "IL_XXXX: opcode operand".</summary>
    public override string ToString()
    {
        return Operand is null
            ? $"IL_{Offset:X4}: {OpCodeName}"
            : $"IL_{Offset:X4}: {OpCodeName,-12} {Operand}";
    }
}

/// <summary>
/// Renders the sole decoded-body model, <see cref="MethodInstructions"/>, into human-readable or
/// canonical ilasm text. Operand type classification uses a lookup table derived from ILSpy
/// (MIT license).
/// </summary>
public static class InstructionProducer
{
    /// <summary>
    /// Renders an already-decoded body. Throws <see cref="BadImageFormatException"/> when the
    /// body failed to decode (malformed IL) — the same fail-closed contract
    /// <see cref="MethodInstructions.Decode(MethodBodyBlock)"/> uses for its throwing callers.
    /// </summary>
    public static List<ILInstructionText> Render(MethodInstructions body, IOperandNameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(resolver);
        if (!body.IsComplete)
            throw new BadImageFormatException(body.Blocks.IncompleteReason ?? "IL body decode failed.");

        var instructions = new List<ILInstructionText>(body.Instructions.Length);
        foreach (var instruction in body.Instructions)
        {
            instructions.Add(new ILInstructionText(
                instruction.Offset, GetDisplayName(instruction.OpCode), FormatOperand(instruction, resolver)));
        }

        return instructions;
    }

    /// <summary>
    /// Decodes and renders a method body. Returns null if the method has no IL body (abstract,
    /// extern, etc.). Malformed IL that decodes to an incomplete body still throws
    /// <see cref="BadImageFormatException"/> (via <see cref="Render"/>) — a null result is
    /// reserved for the honest "no body" case, never a decode failure.
    /// The resolver selects display or canonical ilasm operand syntax.
    /// </summary>
    public static List<ILInstructionText>? Disassemble(
        PEReader peReader,
        MethodDefinition method,
        IOperandNameResolver resolver)
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

        return Render(MethodInstructions.Decode(body), resolver);
    }

    /// <summary>
    /// Overload for callers that have already resolved the declaring type handle, avoiding a repeated
    /// TypeDefinitions scan per method.
    /// </summary>
    public static List<ILInstructionText>? DisassembleMethod(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string methodName,
        int overloadIndex,
        IOperandNameResolver resolver,
        bool publicOnly = false)
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
                return Disassemble(peReader, method, resolver);

            matchCount++;
        }

        return null;
    }

    /// <summary>
    /// Handle-addressed disassembly: the caller already holds the method's own
    /// <see cref="MethodDefinitionHandle"/> (see docs/design/member-body-substrate.md),
    /// bypassing the name+overload-ordinal walk and its drift.
    /// </summary>
    public static List<ILInstructionText>? DisassembleMethod(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        IOperandNameResolver resolver)
        => Disassemble(peReader, reader.GetMethodDefinition(methodHandle), resolver);

    static string? FormatOperand(DecodedInstruction instruction, IOperandNameResolver resolver)
    {
        bool canonical = resolver.Syntax == ILSyntax.Canonical;
        int token = (int)instruction.OperandValue;
        var operand = instruction.Operand switch
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
            OperandKind.InlineString => resolver.ResolveString(token),
            OperandKind.InlineType => resolver.ResolveType(token),
            OperandKind.InlineMethod => resolver.ResolveMethod(token),
            OperandKind.InlineField => resolver.ResolveField(token),
            OperandKind.InlineTok => resolver.ResolveToken(token),
            OperandKind.InlineSig => $"0x{token:X8}",
            OperandKind.InlineSwitch => $"({string.Join(", ", instruction.BranchTargets.Select(t => $"IL_{t:X4}"))})",
            _ => null
        };

        // Display operands are embedded in line-oriented output, including C#
        // side comments in Annotated Source. Metadata names are untrusted and may
        // contain any C# line terminator, so fold them at the display producer
        // before they can escape the containing line.
        //
        // Canonical is deliberately exempt, and not because it escapes
        // terminators — CanonicalIL.QuoteName escapes only \ and ' inside the
        // SQSTRING. The ilasm grammar permits a literal newline inside a quoted
        // identifier, so leaving it intact is what preserves the metadata name
        // through an ilasm round trip; folding it would silently rename the
        // member. Canonical is consumed by the IL round-trip scaffold only and
        // reaches no Markdown or C# surface, so the injection vector does not
        // apply to it. That exposure boundary is the safety argument, and it is
        // the thing to recheck if canonical is ever surfaced by the CLI.
        // Gate: ILDisassemblerEmitTests pins both sides of this split.
        return canonical ? operand : operand?.ReplaceLineEndings(" ");
    }

    public static string GetDisplayName(ILOpCode opCode)
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
