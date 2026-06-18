using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Depth of an <see cref="IlProjection"/> rendering.</summary>
public enum IlProjectionDepth
{
    /// <summary>Flat instruction list with resolved operands.</summary>
    Raw,

    /// <summary>Block-structured output with labels, indentation, and exception-region markers.</summary>
    Structured,
}

/// <summary>
/// Renders ground-truth IL views from the replacement pipeline's materialized
/// method data (off a <see cref="MetadataSource"/>) — the new-pipeline backing
/// for the annotated-IL view, replacing the old
/// <c>MethodBodyContext</c> → <c>MethodAnalysis</c> → <c>AnnotatedILEmitter</c>
/// contract. Operands are resolved through the importer's own token resolvers,
/// and block structure reuses the importer's <c>FindLeaders</c>, so the views
/// share one analysis with the IR import rather than a parallel one.
///
/// The per-instruction stack-typed depth (the old <c>Typed</c> view) is added
/// separately: it sources stack types from the importer's typed operand stack
/// and so rides on a per-instruction trace, not on this token-and-structure
/// projection.
///
/// Exception-safe by construction: any malformed-metadata read or resolver
/// failure surfaces as a diagnosed <see cref="DecompilerResult"/>, never a throw.
/// </summary>
public static class IlProjection
{
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
        return depth == IlProjectionDepth.Structured
            ? RenderStructured(instructions, imported.Body)
            : RenderRaw(instructions);
    }

    static (TypeDefinition Type, MethodDefinition Method, MethodDefinitionHandle Handle) Locate(
        MetadataReader reader, string typeFullName, string methodName, int overloadIndex, bool publicOnly)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            if ((ns.Length == 0 ? name : $"{ns}.{name}") != typeFullName)
                continue;
            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (publicOnly && (method.Attributes & System.Reflection.MethodAttributes.Public) == 0)
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

    readonly record struct Instr(int Offset, string Name, string Operand);

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
            result.Add(new Instr(offset, name, ReadOperand(reader, scope, il, op, ref pos)));
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
}
