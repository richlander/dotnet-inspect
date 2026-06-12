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
public sealed class CSharpPrinter
{
    readonly IrFunction _function;

    CSharpPrinter(IrFunction function) => _function = function;

    public static DecompilerResult Print(IrFunction function)
    {
        try
        {
            string output = new CSharpPrinter(function).PrintBody(function);
            return new DecompilerResult(output, function.Fidelity, [.. function.Diagnostics]);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    string PrintBody(IrFunction function)
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
            // The trailing 'return;' trims, current-style — unless it is a
            // labeled block's only statement, where trimming would strand
            // the label as invalid C#.
            bool labeledReturnOnly = labelTargets.Contains(block.StartOffset) && block.Children.Count == 1;
            foreach (var statement in block.Children)
            {
                bool isLast = i == blocks.Count - 1 && ReferenceEquals(statement, block.Children[^1]);
                if (isLast && !labeledReturnOnly && statement is Return { Value: null })
                    break;
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

    string Statement(IrNode node) => node switch
    {
        ExpressionStatement e => e.Expression is UnsupportedNode u
            ? $"/* {u.Describe()} */"
            : $"{Expression(e.Expression)};",
        StoreLocal s => $"V_{s.Index} = {Expression(s.Value)};",
        StoreArgument s => $"{s.Name} = {Expression(s.Value)};",
        StoreStackSlot s => $"S_{s.Slot} = {Expression(s.Value)};",
        StoreField s => $"{FieldTarget(s.Field, s.Instance)} = {Expression(s.Value)};",
        StoreProperty s => $"{PropertyTarget(s.Accessor, s.HasInstance ? s.Instance : null, s.IndexArguments, s.PropertyName)} = {Expression(s.Value)};",
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

    string Expression(IrExpression node) => node switch
    {
        LoadArgument a => a.Name,
        LoadLocal l => $"V_{l.Index}",
        LoadStackSlot s => $"S_{s.Slot}",
        Constant c => ConstantText(c),
        LoadField f => FieldTarget(f.Field, f.Instance),
        Binary b => BinaryText(b),
        Comparison c => ComparisonText(c),
        LogicalNot n => $"!{Operand(n.Operand)}",
        Unary { Kind: UnaryKind.Negate } u => $"-{Operand(u.Operand)}",
        Unary u => $"~{Operand(u.Operand)}",
        Convert v => ConvertText(v),
        Call c => CallText(c),
        LoadProperty p => PropertyTarget(p.Accessor, p.HasInstance ? p.Instance : null, p.IndexArguments, p.PropertyName),
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

    string BinaryText(Binary binary)
    {
        // div.un/rem.un compute on unsigned operands; shr.un shifts an
        // unsigned left operand. Operands that are already unsigned (or
        // float, where .un means unordered, not unsigned) print plain.
        bool castBoth = binary.IsUnsigned && binary.Kind is BinaryKind.Divide or BinaryKind.Remainder;
        bool castLeft = castBoth || (binary.IsUnsigned && binary.Kind is BinaryKind.ShiftRight);
        string left = castLeft ? UnsignedOperand(binary.Left) : Operand(binary.Left);
        string right = castBoth ? UnsignedOperand(binary.Right) : Operand(binary.Right);
        return $"{left} {BinaryOperator(binary)} {right}";
    }

    string ComparisonText(Comparison comparison)
        => comparison.IsUnsigned
            ? $"{UnsignedOperand(comparison.Left)} {ComparisonOperator(comparison.Kind)} {UnsignedOperand(comparison.Right)}"
            : $"{Operand(comparison.Left)} {ComparisonOperator(comparison.Kind)} {Operand(comparison.Right)}";

    /// <summary>Casts a signed-integer operand to its unsigned counterpart; already-unsigned, float (.un = unordered), and unknown-typed operands print plain.</summary>
    string UnsignedOperand(IrExpression operand)
    {
        string? cast = operand.ResultType is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" } type
            ? type.Name switch
            {
                "SByte" or "Int16" or "Int32" => "uint",
                "Int64" => "ulong",
                "IntPtr" => "nuint",
                _ => null,
            }
            : null;
        return cast is null ? Operand(operand) : $"({cast}){Operand(operand)}";
    }

    /// <summary>Conditions render brtrue's raw value as-is; LogicalNot over a comparison folds to the inverse text-free form later (raising work, not printing work).</summary>
    string Condition(IrExpression condition) => condition switch
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
    string Operand(IrExpression node)
    {
        string text = Expression(node);
        bool atomic = node is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField
            or Call or NewObject or ArrayLength or LoadElement or CaughtException or SizeOf or LoadToken;
        return atomic ? text : $"({text})";
    }

    string FieldTarget(FieldRef field, IrExpression? instance) => instance switch
    {
        null => $"{TypeText(field.DeclaringType)}.{field.Name}",
        LoadArgument { Index: 0, Name: "this" } => field.Name,
        _ => $"{ReceiverText(instance)}.{field.Name}",
    };

    string PropertyTarget(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, string name)
    {
        string receiver = instance switch
        {
            null => TypeText(accessor.DeclaringType),
            LoadArgument { Index: 0, Name: "this" } => "",
            _ => ReceiverText(instance),
        };
        string dotted = receiver.Length == 0 ? name : $"{receiver}.{name}";
        return name == "Item" && indexArguments.Count > 0
            ? $"{(receiver.Length == 0 ? "this" : receiver)}[{Arguments(indexArguments)}]"
            : indexArguments.Count == 0 ? dotted : $"{dotted}[{Arguments(indexArguments)}]";
    }

    /// <summary>Member-access receivers: value-type receivers arrive by address in IL; C# spells the place itself, not its address.</summary>
    string ReceiverText(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress a => $"V_{a.Index}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        _ => Operand(receiver),
    };

    string CallText(Call call)
    {
        var arguments = call.Arguments;
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        if (!call.Callee.HasThis)
            return $"{TypeText(call.Callee.DeclaringType)}.{call.Callee.Name}{typeArguments}({Arguments(arguments)})";
        var receiver = arguments[0];
        string rest = Arguments(arguments.Skip(1));
        if (call.Callee.Name == ".ctor" && receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // A this-receiver constructor call is C#'s base(...)/this(...).
            string keyword = Equals(call.Callee.DeclaringType, _function.DeclaringType) ? "this" : "base";
            return $"{keyword}({rest})";
        }
        return receiver is LoadArgument { Index: 0, Name: "this" }
            ? $"{call.Callee.Name}{typeArguments}({rest})"
            : $"{ReceiverText(receiver)}.{call.Callee.Name}{typeArguments}({rest})";
    }

    string Arguments(IEnumerable<IrExpression> arguments)
        => string.Join(", ", arguments.Select(Expression));

    string ConvertText(Convert convert)
    {
        string cast = $"({TypeText(convert.Target)}){Operand(convert.Operand)}";
        return convert.IsChecked ? $"checked({cast})" : cast;
    }

    static string ConstantText(Constant constant) => constant.Value switch
    {
        null => "null",
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        bool b => b ? "true" : "false",
        char c => CharText(c),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        float f => $"{f.ToString("R", CultureInfo.InvariantCulture)}f",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)}d",
        _ => constant.Value.ToString() ?? "?",
    };

    static string CharText(char c) => c switch
    {
        '\\' => "'\\\\'",
        '\'' => "'\\''",
        '\t' => "'\\t'",
        '\n' => "'\\n'",
        '\r' => "'\\r'",
        '\0' => "'\\0'",
        _ when char.IsControl(c) => $"'\\u{(int)c:x4}'",
        _ => $"'{c}'",
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
