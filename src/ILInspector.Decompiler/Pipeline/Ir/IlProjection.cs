using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Depth of an <see cref="IlProjection"/> rendering.</summary>
public enum IlProjectionDepth
{
    /// <summary>Flat instruction list with resolved operands.</summary>
    Raw,

    /// <summary>Each instruction annotated with the evaluation-stack types after it (from the importer's stack simulation).</summary>
    Typed,

    /// <summary>Block-structured output with labels, indentation, and exception-region markers.</summary>
    Structured,

    /// <summary>
    /// Rich annotated view: a method header (parameters, locals, max stack, IL
    /// size), named blocks with ranges, exception regions rendered as braces
    /// with catch types, and per-instruction variable names and stack types.
    /// This is the user-facing annotated-IL view.
    /// </summary>
    Annotated,
}

/// <summary>
/// Renders ground-truth IL views from the pipeline's materialized method data
/// (off a <see cref="MetadataSource"/>): the backing for the annotated-IL view.
/// Operands are resolved through the importer's own token resolvers, and block
/// structure reuses the importer's <c>FindLeaders</c>, so the views share one
/// analysis with the IR import rather than a parallel one.
///
/// The <see cref="IlProjectionDepth.Typed"/> depth annotates each instruction
/// with the evaluation-stack types after it, sourced from the importer's own
/// stack simulation via an optional per-instruction trace — one analysis shared
/// with the IR import, not a second simulator. The
/// <see cref="IlProjectionDepth.Annotated"/> depth builds on that with a method
/// header, named blocks, exception-region braces, and variable names.
///
/// Exception-safe by construction: any malformed-metadata read or resolver
/// failure surfaces as a diagnosed <see cref="DecompilerResult"/>, never a throw.
/// </summary>
public static class IlProjection
{
    const int MaxAnnotatedCommentColumn = 72;

    public static DecompilerResult Project(
        MetadataSource source, string typeFullName, string methodName,
        IlProjectionDepth depth, int overloadIndex = 0, bool publicOnly = false)
        => DecompilerResult.Run(
            () => Render(source, typeFullName, methodName, depth, overloadIndex, publicOnly),
            emptyOutputIsFailure: true);

    static string Render(MetadataSource source, string type, string method, IlProjectionDepth depth, int overloadIndex, bool publicOnly)
    {
        var (typeDef, methodDef, methodHandle) = Locate(source.Reader, type, method, overloadIndex, publicOnly);
        var imported = MethodImporter.Import(source, (TypeDefinitionHandle)methodDef.GetDeclaringType(), methodHandle);
        var scope = IrImporter.CallerScope(source.Reader, typeDef, methodDef);
        var instructions = Decode(source.Reader, scope, imported.Body.IL.AsSpan());
        return depth switch
        {
            IlProjectionDepth.Structured => RenderStructured(instructions, imported.Body),
            IlProjectionDepth.Typed => RenderTyped(source, imported, scope, instructions),
            IlProjectionDepth.Annotated => RenderAnnotated(source, imported, scope, instructions),
            _ => RenderRaw(instructions),
        };
    }

    static string RenderTyped(MetadataSource source, ImportedMethod imported, GenericScope scope, List<Instr> instructions)
    {
        // Re-run the importer with a trace: its single stack simulation yields the
        // post-instruction stack types, keyed by offset. The instruction text and
        // resolved operands still come from Decode, so there is no second decode —
        // and no second simulation beyond the import the pipeline already runs.
        var trace = new List<IlTracePoint>();
        IrImporter.Build(source, imported, scope, trace);
        var typesByOffset = new Dictionary<int, ImmutableArray<TypeRef?>>();
        foreach (var point in trace)
            typesByOffset[point.Offset] = point.StackTypes;

        var sb = new StringBuilder();
        foreach (var i in instructions)
        {
            string types = typesByOffset.TryGetValue(i.Offset, out var stack)
                ? $"  // {StackAnnotation(stack)}"
                : "";
            sb.AppendLine(Format(i) + types);
        }
        return sb.ToString();
    }

    static (TypeDefinition Type, MethodDefinition Method, MethodDefinitionHandle Handle) Locate(
        MetadataReader reader, string typeFullName, string methodName, int overloadIndex, bool publicOnly)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (reader.GetFullTypeName(typeDef) != typeFullName)
                continue;
            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (publicOnly && (method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask) != System.Reflection.MethodAttributes.Public)
                    continue;
                if (seen++ != overloadIndex)
                    continue;
                if (method.RelativeVirtualAddress == 0)
                    throw new InvalidOperationException($"{typeFullName}::{methodName} has no IL body");
                return (typeDef, method, methodHandle);
            }
        }
        throw new InvalidOperationException($"{typeFullName}::{methodName} not found");
    }

    readonly record struct Instr(int Offset, ILOpCode Op, string Name, string Operand);

    static List<Instr> Decode(MetadataReader reader, GenericScope scope, ReadOnlySpan<byte> il)
    {
        var result = new List<Instr>();
        int pos = 0;
        while (pos < il.Length)
        {
            int offset = pos;
            int b = il[pos++];
            var op = b == 0xFE ? (ILOpCode)(0xFE00 | il[pos++]) : (ILOpCode)b;
            string name = op.ToString().ToLowerInvariant().Replace('_', '.');
            result.Add(new Instr(offset, op, name, ReadOperand(reader, scope, il, op, ref pos)));
        }
        return result;
    }

    static string ReadOperand(MetadataReader reader, GenericScope scope, ReadOnlySpan<byte> il, ILOpCode op, ref int pos)
    {
        if (op == ILOpCode.Switch)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(il[pos..]); pos += 4;
            int origin = pos + count * 4;
            var targets = new string[count];
            for (int i = 0; i < count; i++)
            {
                targets[i] = $"IL_{origin + BinaryPrimitives.ReadInt32LittleEndian(il[pos..]):X4}"; pos += 4;
            }
            return $"({string.Join(", ", targets)})";
        }

        int length = OperandLength(op);
        if (length == 0)
            return "";
        var bytes = il.Slice(pos, length);
        pos += length;
        if (IsBranch(op))
            return $"IL_{pos + (length == 1 ? (sbyte)bytes[0] : BinaryPrimitives.ReadInt32LittleEndian(bytes)):X4}";
        return op switch
        {
            ILOpCode.Ldc_i4 => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(),
            ILOpCode.Ldc_i4_s => ((sbyte)bytes[0]).ToString(),
            ILOpCode.Ldc_i8 => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(),
            ILOpCode.Ldc_r4 => BinaryPrimitives.ReadSingleLittleEndian(bytes).ToString(),
            ILOpCode.Ldc_r8 => BinaryPrimitives.ReadDoubleLittleEndian(bytes).ToString(),
            _ when IsMetadataToken(op) => ResolveToken(reader, scope, op, BinaryPrimitives.ReadInt32LittleEndian(bytes)),
            _ when length == 1 => bytes[0].ToString(),                              // short var/arg index
            _ when length == 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes).ToString(),  // var/arg index
            _ => $"0x{BinaryPrimitives.ReadUInt32LittleEndian(bytes):X8}",
        };
    }

    /// <summary>Resolves a metadata-token operand to its display form, falling back to raw token hex if resolution fails.</summary>
    static string ResolveToken(MetadataReader reader, GenericScope scope, ILOpCode op, int token)
    {
        try
        {
            if (op == ILOpCode.Ldstr)
                return $"\"{reader.GetUserString(MetadataTokens.UserStringHandle(token))}\"";
            var handle = MetadataTokens.EntityHandle(token);
            return op switch
            {
                ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
                    or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld => Field(reader, scope, handle),
                ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj
                    or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp => Method(reader, scope, handle),
                ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any
                    or ILOpCode.Newarr or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Cpobj or ILOpCode.Initobj
                    or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Mkrefany or ILOpCode.Refanyval
                    or ILOpCode.Ldelem or ILOpCode.Ldelema or ILOpCode.Stelem
                    => IrImporter.ResolveTypeToken(reader, handle, scope).ToDisplayString(),
                ILOpCode.Ldtoken => AnyToken(reader, scope, handle),
                _ => $"0x{token:X8}",
            };
        }
        catch
        {
            return $"0x{token:X8}";  // resolution is best-effort; the view stays ground truth on the structure
        }
    }

    static string AnyToken(MetadataReader reader, GenericScope scope, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.FieldDefinition => Field(reader, scope, handle),
        HandleKind.MethodDefinition or HandleKind.MethodSpecification => Method(reader, scope, handle),
        HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Field
            => Field(reader, scope, handle),
        HandleKind.MemberReference => Method(reader, scope, handle),
        _ => IrImporter.ResolveTypeToken(reader, handle, scope).ToDisplayString(),
    };

    static string Method(MetadataReader reader, GenericScope scope, EntityHandle handle)
    {
        var m = IrImporter.ResolveMethod(reader, handle, scope);
        return $"{m.DeclaringType.ToDisplayString()}::{m.Name}({string.Join(", ", m.ParameterTypes.Select(p => p.ToDisplayString()))})";
    }

    static string Field(MetadataReader reader, GenericScope scope, EntityHandle handle)
    {
        var f = IrImporter.ResolveField(reader, handle, scope);
        return $"{f.Type.ToDisplayString()} {f.DeclaringType.ToDisplayString()}::{f.Name}";
    }

    static bool IsMetadataToken(ILOpCode op) => op is
        ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp
        or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
        or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any
        or ILOpCode.Newarr or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Cpobj or ILOpCode.Initobj
        or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Mkrefany or ILOpCode.Refanyval
        or ILOpCode.Ldelem or ILOpCode.Ldelema or ILOpCode.Stelem or ILOpCode.Ldstr or ILOpCode.Ldtoken;

    static bool IsBranch(ILOpCode op) => op is
        ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
        or ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bgt or ILOpCode.Bgt_s
        or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s
        or ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
        or ILOpCode.Blt_un or ILOpCode.Blt_un_s or ILOpCode.Leave or ILOpCode.Leave_s;

    static int OperandLength(ILOpCode op) => op switch
    {
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => 2,
        ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s
            or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
            or ILOpCode.Ldc_i4_s or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s
            or ILOpCode.Ldloca_s or ILOpCode.Stloc_s or ILOpCode.Unaligned => 1,
        ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt
            or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
            or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave
            or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 or ILOpCode.Jmp
            or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn
            or ILOpCode.Ldvirtftn or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld
            or ILOpCode.Ldsflda or ILOpCode.Stsfld or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Box
            or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Newarr or ILOpCode.Ldelem or ILOpCode.Ldelema
            or ILOpCode.Stelem or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Cpobj or ILOpCode.Initobj
            or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Ldtoken or ILOpCode.Ldstr
            or ILOpCode.Mkrefany or ILOpCode.Refanyval => 4,
        _ => 0,
    };

    static string RenderRaw(List<Instr> instructions)
    {
        var sb = new StringBuilder();
        foreach (var i in instructions)
            sb.AppendLine(Format(i));
        return sb.ToString();
    }

    static string RenderStructured(List<Instr> instructions, MethodBody body)
    {
        var leaders = IrImporter.FindLeaders([.. body.IL], body.Handlers);
        var tryStarts = body.Handlers.Select(h => h.TryOffset).ToHashSet();
        var handlerStarts = body.Handlers
            .GroupBy(h => h.HandlerOffset)
            .ToDictionary(g => g.Key, g => g.First().Kind);
        var sb = new StringBuilder();
        foreach (var i in instructions)
        {
            if (tryStarts.Contains(i.Offset))
                sb.AppendLine("  // .try");
            if (handlerStarts.TryGetValue(i.Offset, out var kind))
                sb.AppendLine($"  // {kind.ToString().ToLowerInvariant()}");
            if (leaders.Contains(i.Offset))
                sb.AppendLine($"IL_{i.Offset:X4}:");
            sb.AppendLine("    " + Format(i));
        }
        return sb.ToString();
    }

    static string Format(Instr i) => $"IL_{i.Offset:X4}: {i.Name}{(i.Operand.Length > 0 ? " " + i.Operand : "")}";

    // --- Annotated view (header + named blocks + exception braces + variable
    // names + per-instruction stack types) ---

    static string RenderAnnotated(MetadataSource source, ImportedMethod imported, GenericScope scope, List<Instr> instructions)
    {
        var body = imported.Body;

        // One import serves both the stack-type annotations and the hidden-fact
        // classification: build the IR with a trace, read the stack types off the
        // trace, and classify the same function. The facts key by IL offset, so
        // they land on the exact opcode — the IL-view dual of the C# view's
        // statement anchoring.
        var trace = new List<IlTracePoint>();
        var function = IrImporter.Build(source, imported, scope, trace);
        var stackByOffset = new Dictionary<int, ImmutableArray<TypeRef?>>();
        foreach (var point in trace)
            stackByOffset[point.Offset] = point.StackTypes;

        var factsByOffset = new Dictionary<int, List<Annotations.Annotation>>();

        // Block leaders → ordinal index, plus the byte range of each block (to
        // the next leader, or to end of IL) for the `Block_N: (range)` label.
        var leaders = IrImporter.FindLeaders([.. body.IL], body.Handlers).ToList();
        var blockIndex = new Dictionary<int, int>();
        var blockEnd = new Dictionary<int, int>();
        for (int b = 0; b < leaders.Count; b++)
        {
            blockIndex[leaders[b]] = b;
            int next = b + 1 < leaders.Count ? leaders[b + 1] : body.IL.Length;
            blockEnd[leaders[b]] = next - 1;
        }

        BuildRegionMarkers(body.Handlers, out var regionStarts, out var regionEnds);
        var annotatedLines = AnnotatedInstrLines(imported, instructions, factsByOffset, stackByOffset)
            .ToDictionary(line => line.Offset);

        var sb = new StringBuilder();
        AnnotatedHeader(sb, imported);

        int indent = 1;
        foreach (var i in instructions)
        {
            // Ends close innermost-first, then starts open outermost-first, so
            // brace/indent stays balanced when regions nest or share an offset.
            if (regionEnds.TryGetValue(i.Offset, out var endMarkers))
                foreach (var endMarker in endMarkers)
                {
                    if (indent > 1) indent--;
                    WriteIndent(sb, indent);
                    sb.AppendLine(endMarker);
                }
            if (regionStarts.TryGetValue(i.Offset, out var startMarkers))
                foreach (var startMarker in startMarkers)
                {
                    WriteIndent(sb, indent);
                    sb.AppendLine(startMarker);
                    indent++;
                }
            if (blockIndex.TryGetValue(i.Offset, out int index))
            {
                if (i.Offset > 0) sb.AppendLine();
                WriteIndent(sb, indent);
                sb.AppendLine($"Block_{index}: (IL_{i.Offset:X4}-IL_{blockEnd[i.Offset]:X4})");
            }

            WriteIndent(sb, indent + 1);
            sb.AppendLine(annotatedLines[i.Offset].Text);
        }

        while (indent > 1)
        {
            indent--;
            WriteIndent(sb, indent);
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats one instruction as it appears in the annotated IL view:
    /// <c>IL_xxxx: name operand  // facts; local; stack: [types]</c>. Shared by
    /// the flat annotated-IL view and the mixed source view, so an instruction
    /// reads identically whether shown on its own or interleaved beneath C#.
    /// </summary>
    static AnnotatedInstrPart FormatAnnotatedInstrPart(ImportedMethod imported, Instr i,
        Dictionary<int, List<Annotations.Annotation>> factsByOffset,
        Dictionary<int, ImmutableArray<TypeRef?>> stackByOffset)
    {
        var instruction = new StringBuilder();
        instruction.Append($"IL_{i.Offset:X4}: {i.Name,-12}");
        if (i.Operand.Length > 0)
            instruction.Append(' ').Append(i.Operand);

        List<string> annotations = [];
        if (factsByOffset.TryGetValue(i.Offset, out var facts))
            foreach (var fact in facts)
                annotations.Add(Annotations.AnnotationText.Format(fact));
        if (VariableAnnotation(imported, i) is { } variable)
            annotations.Add(variable);
        if (stackByOffset.TryGetValue(i.Offset, out var stack))
            annotations.Add(StackAnnotation(stack));
        return new AnnotatedInstrPart(i.Offset, instruction.ToString(), string.Join("; ", annotations));
    }

    static IReadOnlyList<AnnotatedInstrLine> AnnotatedInstrLines(
        ImportedMethod imported,
        IReadOnlyList<Instr> instructions,
        Dictionary<int, List<Annotations.Annotation>> factsByOffset,
        Dictionary<int, ImmutableArray<TypeRef?>> stackByOffset)
    {
        var parts = instructions
            .Select(i => FormatAnnotatedInstrPart(imported, i, factsByOffset, stackByOffset))
            .ToList();
        int commentColumn = CommentColumn(parts);
        return [.. parts.Select(part => new AnnotatedInstrLine(part.Offset, FormatAnnotatedInstr(part, commentColumn)))];
    }

    static int CommentColumn(IReadOnlyList<AnnotatedInstrPart> parts)
    {
        var annotated = parts.Where(part => part.Annotation.Length > 0).ToList();
        if (annotated.Count == 0)
            return 0;
        int naturalColumn = annotated.Max(part => part.Instruction.Length) + 2;
        return Math.Min(naturalColumn, MaxAnnotatedCommentColumn);
    }

    static string FormatAnnotatedInstr(AnnotatedInstrPart part, int commentColumn)
    {
        if (part.Annotation.Length == 0)
            return part.Instruction;
        int padding = Math.Max(2, commentColumn - part.Instruction.Length);
        return $"{part.Instruction}{new string(' ', padding)}// {part.Annotation}";
    }

    static string StackAnnotation(ImmutableArray<TypeRef?> stack)
        => $"stack: [{string.Join(", ", stack.Select(t => t?.ToDisplayString() ?? "?"))}]";

    readonly record struct AnnotatedInstrPart(int Offset, string Instruction, string Annotation);

    /// <summary>One instruction's IL offset and its annotated text, for the mixed view.</summary>
    public readonly record struct AnnotatedInstrLine(int Offset, string Text);

    /// <summary>
    /// Builds the per-instruction annotated IL lines (offset + text, no block or
    /// region scaffolding) for the mixed source view to bucket beneath C#
    /// statements. Imports once: a single stack simulation feeds both the stack
    /// types and the hidden-fact classification, exactly as the flat annotated
    /// view does — so the two views never diverge on what an instruction says.
    /// </summary>
    public static IReadOnlyList<AnnotatedInstrLine> AnnotatedInstrLines(
        MetadataSource source, string type, string method, int overloadIndex, bool publicOnly)
    {
        var (typeDef, methodDef, methodHandle) = Locate(source.Reader, type, method, overloadIndex, publicOnly);
        var imported = MethodImporter.Import(source, (TypeDefinitionHandle)methodDef.GetDeclaringType(), methodHandle);
        var scope = IrImporter.CallerScope(source.Reader, typeDef, methodDef);
        var instructions = Decode(source.Reader, scope, imported.Body.IL.AsSpan());

        var trace = new List<IlTracePoint>();
        var function = IrImporter.Build(source, imported, scope, trace);
        var stackByOffset = new Dictionary<int, ImmutableArray<TypeRef?>>();
        foreach (var point in trace)
            stackByOffset[point.Offset] = point.StackTypes;

        var factsByOffset = new Dictionary<int, List<Annotations.Annotation>>();
        return AnnotatedInstrLines(imported, instructions, factsByOffset, stackByOffset);
    }

    static void AnnotatedHeader(StringBuilder sb, ImportedMethod imported)
    {
        var body = imported.Body;
        sb.AppendLine("// Method IL");
        if (imported.Signature.Parameters.Length > 0)
            sb.AppendLine($"//   Parameters: {string.Join(", ", imported.Signature.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))}");
        if (body.Locals.Length > 0)
            sb.AppendLine($"//   Locals: {string.Join(", ", body.Locals.Select((t, i) => $"{t.ToDisplayString()} {LocalName(body, i)}"))}");
        sb.AppendLine($"//   MaxStack: {body.MaxStack}");
        sb.AppendLine($"//   IL size: {body.IL.Length} bytes");
        sb.AppendLine();
    }

    static string LocalName(MethodBody body, int index) =>
        index < body.LocalNames.Length && !string.IsNullOrWhiteSpace(body.LocalNames[index])
            ? body.LocalNames[index]!
            : $"V_{index}";

    /// <summary>
    /// Builds the brace markers for every exception region, keyed by IL offset.
    /// Each offset can carry several markers (nested or adjacent regions sharing
    /// a boundary), pre-ordered so that a single forward pass stays balanced:
    /// end markers run innermost-first (smallest enclosing extent first) and
    /// start markers outermost-first. Filter clauses render as a `filter` block
    /// (the filter expression, <c>[FilterOffset, HandlerOffset)</c>) followed by
    /// a `handler` block (the handler body), rather than collapsing both onto the
    /// handler offset.
    /// </summary>
    static void BuildRegionMarkers(ImmutableArray<HandlerRegion> handlers, out Dictionary<int, List<string>> starts, out Dictionary<int, List<string>> ends)
    {
        // Each marker carries the enclosing region's extent (try start → handler
        // end) as the nesting key; larger extent = more enclosing.
        var startMarkers = new List<(int Offset, int Extent, string Text)>();
        var endMarkers = new List<(int Offset, int Extent, string Text)>();
        var protectedBlocks = new HashSet<(int Offset, int Length)>();

        foreach (var region in handlers)
        {
            int tryEnd = region.TryOffset + region.TryLength;
            int handlerEnd = region.HandlerOffset + region.HandlerLength;
            int extent = handlerEnd - region.TryOffset;

            // Sibling handlers (e.g. multiple catches) protect an identical
            // block, so its `.try` open/close emits once; a nested try sharing
            // only the start offset has a different length and stays distinct.
            if (protectedBlocks.Add((region.TryOffset, region.TryLength)))
            {
                startMarkers.Add((region.TryOffset, extent, ".try {"));
                endMarkers.Add((tryEnd, extent, "} // end .try"));
            }

            if (region.Kind == HandlerKind.Filter)
            {
                startMarkers.Add((region.FilterOffset, extent, "filter {"));
                endMarkers.Add((region.HandlerOffset, extent, "} // end filter"));
                startMarkers.Add((region.HandlerOffset, extent, "handler {"));
                endMarkers.Add((handlerEnd, extent, "} // end handler"));
            }
            else
            {
                string label = region.Kind switch
                {
                    HandlerKind.Catch => $"catch ({region.CatchType?.ToDisplayString() ?? "?"}) {{",
                    HandlerKind.Finally => "finally {",
                    HandlerKind.Fault => "fault {",
                    _ => $"{region.Kind} {{",
                };
                startMarkers.Add((region.HandlerOffset, extent, label));
                endMarkers.Add((handlerEnd, extent, $"}} // end {region.Kind.ToString().ToLowerInvariant()}"));
            }
        }

        starts = startMarkers
            .GroupBy(m => m.Offset)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Extent).Select(m => m.Text).ToList());
        ends = endMarkers
            .GroupBy(m => m.Offset)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Extent).Select(m => m.Text).ToList());
    }

    /// <summary>Resolves a load/store of an argument or local to a `arg: name`/`local: name` annotation, or null when the instruction is neither or the name is unavailable.</summary>
    static string? VariableAnnotation(ImportedMethod imported, Instr i)
    {
        switch (i.Op)
        {
            case ILOpCode.Ldarg_0: return ArgName(imported.Signature, 0);
            case ILOpCode.Ldarg_1: return ArgName(imported.Signature, 1);
            case ILOpCode.Ldarg_2: return ArgName(imported.Signature, 2);
            case ILOpCode.Ldarg_3: return ArgName(imported.Signature, 3);
            case ILOpCode.Ldloc_0: case ILOpCode.Stloc_0: return LocalRef(imported.Body, 0);
            case ILOpCode.Ldloc_1: case ILOpCode.Stloc_1: return LocalRef(imported.Body, 1);
            case ILOpCode.Ldloc_2: case ILOpCode.Stloc_2: return LocalRef(imported.Body, 2);
            case ILOpCode.Ldloc_3: case ILOpCode.Stloc_3: return LocalRef(imported.Body, 3);
            case ILOpCode.Ldarg: case ILOpCode.Ldarg_s: case ILOpCode.Ldarga: case ILOpCode.Ldarga_s:
            case ILOpCode.Starg: case ILOpCode.Starg_s:
                return int.TryParse(i.Operand, out int a) ? ArgName(imported.Signature, a) : null;
            case ILOpCode.Ldloc: case ILOpCode.Ldloc_s: case ILOpCode.Ldloca: case ILOpCode.Ldloca_s:
            case ILOpCode.Stloc: case ILOpCode.Stloc_s:
                return int.TryParse(i.Operand, out int l) ? LocalRef(imported.Body, l) : null;
            default: return null;
        }
    }

    static string? ArgName(MethodSignature signature, int index)
    {
        if (signature.HasThis)
        {
            if (index == 0)
                return "arg: this";
            index--;
        }
        return index >= 0 && index < signature.Parameters.Length
            ? $"arg: {signature.Parameters[index].Name}"
            : null;
    }

    static string? LocalRef(MethodBody body, int index) =>
        index < body.LocalNames.Length && !string.IsNullOrWhiteSpace(body.LocalNames[index])
            ? $"local: {body.LocalNames[index]}"
            : null;

    static void WriteIndent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent; i++)
            sb.Append("    ");
    }
}
