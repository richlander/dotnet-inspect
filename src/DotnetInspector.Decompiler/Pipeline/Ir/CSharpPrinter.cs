using System.Globalization;
using System.Text;

namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// First C# projection of the IR: honest lowered output. Structure that has
/// not been raised renders as what it is — flat blocks with labels and
/// gotos — never as guessed sugar. Formatting follows the current emitter's
/// style (bare this-members, V_N/S_N names, trimmed trailing return) so the
/// harness diff measures structural distance, not whitespace noise. The
/// raising passes close the goto gap from here; this printer is the
/// scoreboard's starting line.
/// </summary>
public static class CSharpPrinter
{
    public static DecompilerResult Print(IrFunction function)
    {
        try
        {
            string output = PrintBody(function);
            return new DecompilerResult(output, function.Fidelity, [.. function.Diagnostics]);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    static string PrintBody(IrFunction function)
    {
        var sb = new StringBuilder();
        var blocks = function.Body.Blocks;
        var labelTargets = CollectBranchTargets(function);

        // Locals and slots referenced anywhere declare up front, current-style.
        foreach (var declaration in CollectDeclarations(function))
            sb.AppendLine(declaration);
        if (sb.Length > 0)
            sb.AppendLine();

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (labelTargets.Contains(block.StartOffset))
                sb.AppendLine($"IL_{block.StartOffset:X4}:");
            foreach (var statement in block.Children)
            {
                bool isLast = i == blocks.Count - 1 && ReferenceEquals(statement, block.Children[^1]);
                if (isLast && statement is Return { Value: null })
                    break;  // trailing 'return;' trims, current-style
                sb.AppendLine(Statement(statement));
            }
        }
        return sb.ToString().TrimEnd() is { Length: > 0 } text ? text + Environment.NewLine : "";
    }

    static HashSet<int> CollectBranchTargets(IrFunction function)
    {
        var targets = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Branch branch: targets.Add(branch.TargetOffset); break;
                case ConditionalBranch conditional: targets.Add(conditional.TargetOffset); break;
                case Leave leave: targets.Add(leave.TargetOffset); break;
                case SwitchBranch sw: foreach (int t in sw.TargetOffsets) targets.Add(t); break;
            }
        }
        return targets;
    }

    static IEnumerable<string> CollectDeclarations(IrFunction function)
    {
        var locals = new SortedSet<int>();
        var slots = new SortedDictionary<int, TypeRef?>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal l: locals.Add(l.Index); break;
                case StoreLocal s: locals.Add(s.Index); break;
                case LoadLocalAddress a: locals.Add(a.Index); break;
                case LoadStackSlot ls: slots.TryAdd(ls.Slot, ls.Type); break;
                case StoreStackSlot ss: slots.TryAdd(ss.Slot, ss.Value.ResultType); break;
            }
        }
        foreach (int index in locals)
            yield return $"{TypeText(function.Locals[index])} V_{index};";
        foreach (var (slot, type) in slots)
            yield return $"{(type is null ? "var" : TypeText(type))} S_{slot};";
    }

    static string Statement(IrNode node) => node switch
    {
        ExpressionStatement e => e.Expression is UnsupportedNode u
            ? $"/* {u.Describe()} */"
            : $"{Expression(e.Expression)};",
        StoreLocal s => $"V_{s.Index} = {Expression(s.Value)};",
        StoreArgument s => $"{s.Name} = {Expression(s.Value)};",
        StoreStackSlot s => $"S_{s.Slot} = {Expression(s.Value)};",
        StoreField s => $"{FieldTarget(s.Field, s.Instance)} = {Expression(s.Value)};",
        StoreElement s => $"{Expression(s.Array)}[{Expression(s.Index)}] = {Expression(s.Value)};",
        StoreIndirect s => $"*{Operand(s.Address)} = {Expression(s.Value)};",
        InitObject o => $"*{Operand(o.Address)} = default({TypeText(o.Type)});",
        Return { Value: { } value } => $"return {Expression(value)};",
        Return => "return;",
        Throw t => $"throw {Expression(t.Value)};",
        Branch b => $"goto IL_{b.TargetOffset:X4};",
        ConditionalBranch c => $"if ({Condition(c.Condition)}) goto IL_{c.TargetOffset:X4};",
        SwitchBranch s => $"switch ({Expression(s.Value)}) goto [{string.Join(", ", s.TargetOffsets.Select(t => $"IL_{t:X4}"))}];",
        Leave l => $"goto IL_{l.TargetOffset:X4}; // leave",
        EndFinally => "// endfinally",
        EndFilter f => $"// endfilter({Expression(f.Value)})",
        _ => $"/* {node.Describe()} */",
    };

    static string Expression(IrExpression node) => node switch
    {
        LoadArgument a => a.Name,
        LoadLocal l => $"V_{l.Index}",
        LoadStackSlot s => $"S_{s.Slot}",
        Constant c => ConstantText(c),
        LoadField f => FieldTarget(f.Field, f.Instance),
        Binary b => $"{Operand(b.Left)} {BinaryOperator(b)} {Operand(b.Right)}",
        Comparison c => $"{Operand(c.Left)} {ComparisonOperator(c.Kind)} {Operand(c.Right)}",
        LogicalNot n => $"!{Operand(n.Operand)}",
        Unary { Kind: UnaryKind.Negate } u => $"-{Operand(u.Operand)}",
        Unary u => $"~{Operand(u.Operand)}",
        Convert v => ConvertText(v),
        Call c => CallText(c),
        NewObject n => $"new {TypeText(n.Constructor.DeclaringType)}({Arguments(n.Arguments)})",
        ArrayLength l => $"{Operand(l.Array)}.Length",
        LoadElement e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        NewArray n => $"new {TypeText(n.ElementType)}[{Expression(n.Length)}]",
        Box b => Expression(b.Operand),
        IsInstance i => $"{Operand(i.Operand)} as {TypeText(i.Type)}",
        CastClass c => $"({TypeText(c.Type)}){Operand(c.Operand)}",
        UnboxAny u => $"({TypeText(u.Type)}){Operand(u.Operand)}",
        Unbox u => $"ref ({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocalAddress a => $"ref V_{a.Index}",
        LoadArgumentAddress a => $"ref {a.Name}",
        LoadFieldAddress f => $"ref {FieldTarget(f.Field, f.Instance)}",
        LoadElementAddress e => $"ref {Operand(e.Array)}[{Expression(e.Index)}]",
        LoadIndirect l => $"*{Operand(l.Address)}",
        SizeOf s => $"sizeof({TypeText(s.Type)})",
        LoadToken t => t.Kind == RuntimeTokenKind.Type && t.Type is not null
            ? $"typeof({TypeText(t.Type)})"
            : $"/* {t.Describe()} */",
        CaughtException => "__exception",
        UnsupportedNode u => $"/* {u.Describe()} */",
        _ => $"/* {node.Describe()} */",
    };

    /// <summary>Conditions render brtrue's raw value as-is; LogicalNot over a comparison folds to the inverse text-free form later (raising work, not printing work).</summary>
    static string Condition(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: Comparison c } => $"{Operand(c.Left)} {ComparisonOperator(Inverse(c.Kind))} {Operand(c.Right)}",
        _ => Expression(condition),
    };

    static ComparisonKind Inverse(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => ComparisonKind.NotEqual,
        ComparisonKind.NotEqual => ComparisonKind.Equal,
        ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
        ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
        _ => ComparisonKind.LessThan,
    };

    /// <summary>Parenthesizes compound operands; leaves atoms bare. Conservative until the precedence visitor exists.</summary>
    static string Operand(IrExpression node)
    {
        string text = Expression(node);
        bool atomic = node is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField
            or Call or NewObject or ArrayLength or LoadElement or CaughtException or SizeOf or LoadToken;
        return atomic ? text : $"({text})";
    }

    static string FieldTarget(FieldRef field, IrExpression? instance) => instance switch
    {
        null => $"{TypeText(field.DeclaringType)}.{field.Name}",
        LoadArgument { Index: 0, Name: "this" } => field.Name,
        _ => $"{Operand(instance)}.{field.Name}",
    };

    static string CallText(Call call)
    {
        var arguments = call.Arguments;
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        if (!call.Callee.HasThis)
            return $"{TypeText(call.Callee.DeclaringType)}.{call.Callee.Name}{typeArguments}({Arguments(arguments)})";
        var receiver = arguments[0];
        string rest = Arguments(arguments.Skip(1));
        return receiver is LoadArgument { Index: 0, Name: "this" }
            ? $"{call.Callee.Name}{typeArguments}({rest})"
            : $"{Operand(receiver)}.{call.Callee.Name}{typeArguments}({rest})";
    }

    static string Arguments(IEnumerable<IrExpression> arguments)
        => string.Join(", ", arguments.Select(Expression));

    static string ConvertText(Convert convert)
    {
        string cast = $"({TypeText(convert.Target)}){Operand(convert.Operand)}";
        return convert.IsChecked ? $"checked({cast})" : cast;
    }

    static string ConstantText(Constant constant) => constant.Value switch
    {
        null => "null",
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        float f => $"{f.ToString("R", CultureInfo.InvariantCulture)}f",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)}d",
        _ => constant.Value.ToString() ?? "?",
    };

    static string BinaryOperator(Binary binary) => binary.Kind switch
    {
        BinaryKind.Add => "+",
        BinaryKind.Subtract => "-",
        BinaryKind.Multiply => "*",
        BinaryKind.Divide => "/",
        BinaryKind.Remainder => "%",
        BinaryKind.And => "&",
        BinaryKind.Or => "|",
        BinaryKind.Xor => "^",
        BinaryKind.ShiftLeft => "<<",
        _ => ">>",
    };

    static string ComparisonOperator(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => "==",
        ComparisonKind.NotEqual => "!=",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        _ => ">=",
    };

    static string TypeText(TypeRef type)
    {
        string text = type.ToDisplayString();
        int tick = text.IndexOf('`');
        return tick < 0 ? text : text[..tick];
    }
}
