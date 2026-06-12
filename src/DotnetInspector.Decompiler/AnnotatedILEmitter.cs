using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

using DotnetInspector.Metadata;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Depth of annotation for IL output.
/// </summary>
public enum ILAnnotationDepth
{
    /// <summary>Flat instruction list with resolved operands.</summary>
    Raw,

    /// <summary>Instructions annotated with stack types before and after each opcode.</summary>
    Typed,

    /// <summary>Block-structured output with labels, indentation, and exception regions.</summary>
    Structured
}

/// <summary>
/// Emits annotated IL text from the decompiler pipeline. Walks raw IL bytes
/// enriched with CFG structure and stack type analysis.
/// </summary>
public static class AnnotatedILEmitter
{
    /// <summary>
    /// Emits annotated IL with failures expressed as diagnostics rather than
    /// exceptions. On success the fidelity is <see cref="DecompilationFidelity.Full"/>
    /// for this projection (the IL views are ground truth at every depth).
    /// </summary>
    public static DecompilerResult Decompile(MethodBodyContext context, ILAnnotationDepth depth = ILAnnotationDepth.Typed)
        => DecompilerResult.Run(() => Emit(context, depth));

    /// <summary>
    /// Emit annotated IL for a method at the specified depth.
    /// </summary>
    public static string Emit(MethodBodyContext context, ILAnnotationDepth depth = ILAnnotationDepth.Typed)
        => Emit(MethodAnalysis.Create(context), depth);

    /// <summary>
    /// Emits annotated IL from a shared <see cref="MethodAnalysis"/>. Only
    /// the pre-transform stages (CFG, stack simulation) are consumed — the
    /// IL projections stay ground truth regardless of raising transforms.
    /// </summary>
    public static string Emit(MethodAnalysis analysis, ILAnnotationDepth depth = ILAnnotationDepth.Typed)
        => Emit(analysis.Context, analysis.Cfg, analysis.Stack, depth);

    /// <summary>Decompiles annotated IL from a shared <see cref="MethodAnalysis"/>, with failures expressed as diagnostics.</summary>
    public static DecompilerResult Decompile(MethodAnalysis analysis, ILAnnotationDepth depth = ILAnnotationDepth.Typed)
        => DecompilerResult.Run(() => Emit(analysis, depth));

    /// <summary>
    /// Emit annotated IL from pre-computed analysis results.
    /// </summary>
    public static string Emit(
        MethodBodyContext context,
        ControlFlowGraph cfg,
        StackSimulationResult simResult,
        ILAnnotationDepth depth = ILAnnotationDepth.Typed)
    {
        var sb = new StringBuilder();

        if (depth == ILAnnotationDepth.Raw)
        {
            EmitRaw(context, sb);
            return sb.ToString();
        }

        // Compute per-instruction stack states for typed/structured
        var instructionStacks = ComputeInstructionStacks(context, cfg, simResult);

        if (depth == ILAnnotationDepth.Typed)
            EmitTyped(context, cfg, simResult, instructionStacks, sb);
        else
            EmitStructured(context, cfg, simResult, instructionStacks, sb);

        return sb.ToString();
    }

    static void EmitRaw(MethodBodyContext context, StringBuilder sb)
    {
        var reader = new ILReaderLite(context.ILBytes);

        while (reader.HasNext)
        {
            int offset = reader.Offset;
            var opcode = reader.ReadILOpcode();
            string operand = DecodeOperand(context, ref reader, opcode, offset);

            sb.Append($"IL_{offset:X4}: ");
            sb.Append($"{FormatOpCode(opcode),-12}");
            if (operand.Length > 0)
                sb.Append(' ').Append(operand);
            sb.AppendLine();
        }
    }

    static void EmitTyped(
        MethodBodyContext context,
        ControlFlowGraph cfg,
        StackSimulationResult simResult,
        Dictionary<int, StackState> instructionStacks,
        StringBuilder sb)
    {
        // Emit locals header if present
        EmitLocalsHeader(context, simResult, sb);

        var reader = new ILReaderLite(context.ILBytes);

        while (reader.HasNext)
        {
            int offset = reader.Offset;
            var opcode = reader.ReadILOpcode();
            string operand = DecodeOperand(context, ref reader, opcode, offset);
            string? variableAnnotation = GetVariableAnnotation(context, opcode, operand);

            sb.Append($"IL_{offset:X4}: ");
            sb.Append($"{FormatOpCode(opcode),-12}");
            if (operand.Length > 0)
                sb.Append(' ').Append(operand);

            List<string> annotations = [];
            if (variableAnnotation != null)
                annotations.Add(variableAnnotation);

            // Append stack type annotation
            if (instructionStacks.TryGetValue(offset, out var stackBefore))
            {
                string stackStr = FormatStack(stackBefore);
                annotations.Add(stackStr.Length > 0 ? $"stack: [{stackStr}]" : "stack: []");
            }

            if (annotations.Count > 0)
                sb.Append("  // ").Append(string.Join("; ", annotations));

            sb.AppendLine();
        }
    }

    static void EmitStructured(
        MethodBodyContext context,
        ControlFlowGraph cfg,
        StackSimulationResult simResult,
        Dictionary<int, StackState> instructionStacks,
        StringBuilder sb)
    {
        // Emit method header
        EmitMethodHeader(context, simResult, sb);

        // Build lookup: offset → block index
        var blockStarts = new Dictionary<int, int>();
        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
            blockStarts[cfg.BasicBlocks[i].Start] = i;

        // Build exception region markers
        var regionStarts = new Dictionary<int, string>();
        var regionEnds = new Dictionary<int, string>();
        BuildExceptionRegionMarkers(context, regionStarts, regionEnds);

        var reader = new ILReaderLite(context.ILBytes);
        int currentIndent = 1;

        while (reader.HasNext)
        {
            int offset = reader.Offset;

            // Check for exception region end markers (before the instruction)
            if (regionEnds.TryGetValue(offset, out var endMarker))
            {
                currentIndent--;
                WriteIndent(sb, currentIndent);
                sb.AppendLine(endMarker);
            }

            // Check for exception region start markers
            if (regionStarts.TryGetValue(offset, out var startMarker))
            {
                WriteIndent(sb, currentIndent);
                sb.AppendLine(startMarker);
                currentIndent++;
            }

            // Check for block label
            if (blockStarts.TryGetValue(offset, out int blockIndex))
            {
                if (offset > 0) sb.AppendLine();
                WriteIndent(sb, currentIndent);
                var bb = cfg.BasicBlocks[blockIndex];
                string stackInfo = "";
                if (instructionStacks.TryGetValue(offset, out var entryStack) && entryStack.Height > 0)
                    stackInfo = $"  // entry stack: [{FormatStack(entryStack)}]";
                sb.AppendLine($"Block_{blockIndex}: (IL_{offset:X4}-IL_{offset + bb.Size - 1:X4}){stackInfo}");
            }

            var opcode = reader.ReadILOpcode();
            string operand = DecodeOperand(context, ref reader, opcode, offset);
            string? variableAnnotation = GetVariableAnnotation(context, opcode, operand);

            WriteIndent(sb, currentIndent + 1);
            sb.Append($"IL_{offset:X4}: ");
            sb.Append($"{FormatOpCode(opcode),-12}");
            if (operand.Length > 0)
                sb.Append(' ').Append(operand);

            List<string> annotations = [];
            if (variableAnnotation != null)
                annotations.Add(variableAnnotation);

            // Stack annotation
            if (instructionStacks.TryGetValue(offset, out var stackBefore))
            {
                string stackStr = FormatStack(stackBefore);
                annotations.Add($"[{stackStr}]");
            }

            if (annotations.Count > 0)
                sb.Append("  // ").Append(string.Join("; ", annotations));

            sb.AppendLine();
        }

        // Close any remaining open regions
        while (currentIndent > 1)
        {
            currentIndent--;
            WriteIndent(sb, currentIndent);
            sb.AppendLine("}");
        }
    }

    static string? GetVariableAnnotation(MethodBodyContext context, ILOpCode opcode, string operand)
    {
        var reference = GetVariableReference(opcode, operand);
        if (reference is null)
            return null;

        var (kind, index) = reference.Value;
        return kind switch
        {
            VariableReferenceKind.Local => GetLocalDebugName(context, index) is { } name
                ? $"local: {name}"
                : null,
            VariableReferenceKind.Argument => GetArgumentName(context, index) is { } name
                ? $"arg: {name}"
                : null,
            _ => null
        };
    }

    static (VariableReferenceKind Kind, int Index)? GetVariableReference(ILOpCode opcode, string operand)
    {
        return opcode switch
        {
            ILOpCode.Ldloc_0 or ILOpCode.Stloc_0 => (VariableReferenceKind.Local, 0),
            ILOpCode.Ldloc_1 or ILOpCode.Stloc_1 => (VariableReferenceKind.Local, 1),
            ILOpCode.Ldloc_2 or ILOpCode.Stloc_2 => (VariableReferenceKind.Local, 2),
            ILOpCode.Ldloc_3 or ILOpCode.Stloc_3 => (VariableReferenceKind.Local, 3),

            ILOpCode.Ldarg_0 => (VariableReferenceKind.Argument, 0),
            ILOpCode.Ldarg_1 => (VariableReferenceKind.Argument, 1),
            ILOpCode.Ldarg_2 => (VariableReferenceKind.Argument, 2),
            ILOpCode.Ldarg_3 => (VariableReferenceKind.Argument, 3),

            ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldloca_s
                or ILOpCode.Ldloc or ILOpCode.Stloc or ILOpCode.Ldloca
                => TryParseVariableIndex(operand, out int localIndex)
                    ? (VariableReferenceKind.Local, localIndex)
                    : null,

            ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldarga_s
                or ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldarga
                => TryParseVariableIndex(operand, out int argumentIndex)
                    ? (VariableReferenceKind.Argument, argumentIndex)
                    : null,

            _ => null
        };
    }

    static bool TryParseVariableIndex(string operand, out int index) =>
        int.TryParse(operand, out index) && index >= 0;

    static string? GetLocalDebugName(MethodBodyContext context, int index)
    {
        if (index >= context.LocalNames.Count)
            return null;

        var name = context.LocalNames[index];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    static string? GetArgumentName(MethodBodyContext context, int index)
    {
        if (context.HasThis)
        {
            if (index == 0)
                return "this";
            index--;
        }

        if (index < 0 || index >= context.ParameterNames.Count)
            return null;

        var name = context.ParameterNames[index];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    enum VariableReferenceKind
    {
        Local,
        Argument
    }

    // --- Header emission ---

    static void EmitLocalsHeader(MethodBodyContext context, StackSimulationResult simResult, StringBuilder sb)
    {
        if (simResult.Locals.Count == 0 && simResult.Parameters.Count == 0)
            return;

        if (simResult.Parameters.Count > 0)
        {
            sb.Append("// Parameters: ");
            sb.AppendLine(string.Join(", ", simResult.Parameters.Select(p =>
            {
                string name = GetArgumentName(context, p.Index) ?? p.Name;
                return p.TypeName is not null ? $"{p.TypeName} {name}" : name;
            })));
        }

        if (simResult.Locals.Count > 0)
        {
            sb.Append("// Locals: ");
            sb.AppendLine(string.Join(", ", simResult.Locals.Select(l => $"{l.TypeName} {l.Name}")));
        }

        sb.AppendLine();
    }

    static void EmitMethodHeader(
        MethodBodyContext context,
        StackSimulationResult simResult,
        StringBuilder sb)
    {
        sb.AppendLine("// Method IL");

        if (simResult.Parameters.Count > 0)
        {
            sb.Append("//   Parameters: ");
            sb.AppendLine(string.Join(", ", simResult.Parameters.Select(p =>
            {
                string name = GetArgumentName(context, p.Index) ?? p.Name;
                return p.TypeName is not null ? $"{p.TypeName} {name}" : name;
            })));
        }

        if (simResult.Locals.Count > 0)
        {
            sb.Append("//   Locals: ");
            sb.AppendLine(string.Join(", ", simResult.Locals.Select(l => $"{l.TypeName} {l.Name}")));
        }

        sb.AppendLine($"//   MaxStack: {context.MaxStack}");
        sb.AppendLine($"//   IL size: {context.ILBytes.Length} bytes");
        sb.AppendLine();
    }

    // --- Exception region markers ---

    static void BuildExceptionRegionMarkers(
        MethodBodyContext context,
        Dictionary<int, string> starts,
        Dictionary<int, string> ends)
    {
        foreach (var region in context.ExceptionRegions)
        {
            int tryEnd = region.TryOffset + region.TryLength;
            int handlerEnd = region.HandlerOffset + region.HandlerLength;

            // Try block start (only if not already marked by another region)
            if (!starts.ContainsKey(region.TryOffset))
                starts[region.TryOffset] = ".try {";

            // Handler start
            string handlerLabel = region.Kind switch
            {
                ExceptionRegionKind.Catch =>
                    $"catch ({ResolveTypeName(context, region.CatchType)}) {{",
                ExceptionRegionKind.Filter => "filter {",
                ExceptionRegionKind.Finally => "finally {",
                ExceptionRegionKind.Fault => "fault {",
                _ => $"{region.Kind} {{"
            };

            // Try end = handler start for catch/finally
            if (!ends.ContainsKey(tryEnd))
                ends[tryEnd] = $"}} // end .try";

            starts[region.HandlerOffset] = handlerLabel;
            ends[handlerEnd] = $"}} // end {region.Kind.ToString().ToLowerInvariant()}";
        }
    }

    // --- Stack computation ---

    static Dictionary<int, StackState> ComputeInstructionStacks(
        MethodBodyContext context,
        ControlFlowGraph cfg,
        StackSimulationResult simResult)
    {
        var allStacks = new Dictionary<int, StackState>();

        foreach (var block in cfg.BasicBlocks)
        {
            simResult.BlockEntryStacks.TryGetValue(block.Start, out var entryState);
            entryState ??= StackState.Empty;

            var blockStacks = StackSimulator.SimulateBlockDetailed(context, block, entryState);
            foreach (var kvp in blockStacks)
                allStacks[kvp.Key] = kvp.Value;
        }

        return allStacks;
    }

    // --- Operand decoding (mirrors ILDisassembler) ---

    static string DecodeOperand(MethodBodyContext context, ref ILReaderLite reader, ILOpCode opcode, int instructionOffset)
    {
        switch (opcode)
        {
            // No operand
            case ILOpCode.Nop or ILOpCode.Break or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or
                 ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or
                 ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or
                 ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Ldnull or
                 ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or
                 ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or
                 ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Dup or ILOpCode.Pop or
                 ILOpCode.Ret or ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or
                 ILOpCode.Ldind_u2 or ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4 or ILOpCode.Ldind_i8 or
                 ILOpCode.Ldind_i or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref or
                 ILOpCode.Stind_ref or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4 or
                 ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or
                 ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un or
                 ILOpCode.Rem or ILOpCode.Rem_un or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
                 ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Neg or ILOpCode.Not or
                 ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8 or
                 ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8 or
                 ILOpCode.Conv_r_un or ILOpCode.Throw or ILOpCode.Conv_ovf_i1_un or
                 ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un or
                 ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un or
                 ILOpCode.Conv_ovf_u8_un or ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un or
                 ILOpCode.Ldlen or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_u1 or
                 ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_i4 or
                 ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i or
                 ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref or
                 ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or
                 ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or
                 ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref or
                 ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_i2 or
                 ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_u4 or
                 ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_i or
                 ILOpCode.Conv_ovf_u or ILOpCode.Ckfinite or ILOpCode.Conv_u2 or
                 ILOpCode.Conv_u1 or ILOpCode.Conv_i or ILOpCode.Conv_u or
                 ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or ILOpCode.Mul_ovf or
                 ILOpCode.Mul_ovf_un or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un or
                 ILOpCode.Endfinally or ILOpCode.Stind_i or
                 ILOpCode.Arglist or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or
                 ILOpCode.Clt or ILOpCode.Clt_un or ILOpCode.Localloc or ILOpCode.Endfilter or
                 ILOpCode.Volatile or ILOpCode.Tail or ILOpCode.Cpblk or ILOpCode.Initblk or
                 ILOpCode.Rethrow or ILOpCode.Sizeof or ILOpCode.Refanytype or ILOpCode.Readonly:
                return "";

            // Short branch (1 byte signed)
            case ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or
                 ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or
                 ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or
                 ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s:
            {
                int target = reader.ReadBranchDestination(opcode);
                return $"IL_{target:X4}";
            }

            // Long branch (4 byte signed)
            case ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or
                 ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or
                 ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un or
                 ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave:
            {
                int target = reader.ReadBranchDestination(opcode);
                return $"IL_{target:X4}";
            }

            // Short variable (1 byte index)
            case ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldloca_s or
                 ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldarga_s:
                return reader.ReadILByte().ToString();

            // Long variable (2 byte index)
            case ILOpCode.Ldloc or ILOpCode.Stloc or ILOpCode.Ldloca or
                 ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldarga:
                return reader.ReadILUInt16().ToString();

            // Short integer (1 byte)
            case ILOpCode.Ldc_i4_s or ILOpCode.Unaligned:
                return ((sbyte)reader.ReadILByte()).ToString();

            // Integer (4 bytes)
            case ILOpCode.Ldc_i4:
                return ((int)reader.ReadILUInt32()).ToString();

            // Long integer (8 bytes)
            case ILOpCode.Ldc_i8:
                return reader.ReadILUInt64().ToString();

            // Float (4 bytes)
            case ILOpCode.Ldc_r4:
            {
                uint bits = reader.ReadILUInt32();
                float value = BitConverter.Int32BitsToSingle((int)bits);
                return value.ToString("R");
            }

            // Double (8 bytes)
            case ILOpCode.Ldc_r8:
            {
                ulong bits = reader.ReadILUInt64();
                double value = BitConverter.Int64BitsToDouble((long)bits);
                return value.ToString("R");
            }

            // Token-based operands
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or
                 ILOpCode.Ldvirtftn or ILOpCode.Jmp:
            {
                int token = reader.ReadILToken();
                return Metadata.ILTokenResolver.ResolveMethod(context.Reader, token);
            }

            case ILOpCode.Ldfld or ILOpCode.Ldsfld or ILOpCode.Stfld or ILOpCode.Stsfld or
                 ILOpCode.Ldflda or ILOpCode.Ldsflda:
            {
                int token = reader.ReadILToken();
                return Metadata.ILTokenResolver.ResolveField(context.Reader, token);
            }

            case ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Newarr or ILOpCode.Box or
                 ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Ldobj or ILOpCode.Stobj or
                 ILOpCode.Initobj or ILOpCode.Cpobj or ILOpCode.Mkrefany or ILOpCode.Refanyval or
                 ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Constrained:
            {
                int token = reader.ReadILToken();
                return Metadata.ILTokenResolver.ResolveType(context.Reader, token, context.GenericContext);
            }

            case ILOpCode.Ldstr:
            {
                int token = reader.ReadILToken();
                return Metadata.ILTokenResolver.ResolveString(context.Reader, token);
            }

            case ILOpCode.Ldtoken:
            {
                int token = reader.ReadILToken();
                return Metadata.ILTokenResolver.ResolveToken(context.Reader, token, context.GenericContext);
            }

            case ILOpCode.Calli:
            {
                int token = reader.ReadILToken();
                return $"sig:0x{token:X8}";
            }

            case ILOpCode.Switch:
            {
                uint count = reader.ReadILUInt32();
                int baseOffset = reader.Offset + (int)(count * 4);
                List<string> targets = [];
                for (uint i = 0; i < count; i++)
                {
                    int delta = (int)reader.ReadILUInt32();
                    targets.Add($"IL_{(baseOffset + delta):X4}");
                }
                return $"({string.Join(", ", targets)})";
            }

            default:
                reader.TrySkip(opcode);
                return $"<unknown:{opcode}>";
        }
    }

    // --- Token resolution ---

    /// <summary>
    /// Resolves a type entity handle (e.g. a catch-clause type) to a display name, honoring the
    /// method's generic context. Operand token resolution lives in <see cref="Metadata.ILTokenResolver"/>.
    /// </summary>
    static string ResolveTypeName(MethodBodyContext context, EntityHandle handle)
    {
        try
        {
            return Metadata.TypeResolver.GetTypeName(context.Reader, handle, context.GenericContext)
                ?? $"type:0x{MetadataTokens.GetToken(handle):X8}";
        }
        // Fallback to raw token when metadata is malformed or token is unresolvable
        catch { return $"type:0x{MetadataTokens.GetToken(handle):X8}"; }
    }

    static string FormatOpCode(ILOpCode opcode)
    {
        string name = opcode.ToString().ToLowerInvariant();
        // Replace underscores with dots for standard IL formatting
        return name.Replace('_', '.');
    }

    static string FormatStack(StackState stack)
    {
        if (stack.Height == 0) return "";

        var parts = new string[stack.Height];
        for (int i = 0; i < stack.Height; i++)
        {
            var val = stack.Peek(stack.Height - 1 - i);
            parts[i] = val.TypeName ?? val.Kind.ToString();
        }
        return string.Join(", ", parts);
    }

    static void WriteIndent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent; i++)
            sb.Append("    ");
    }
}
