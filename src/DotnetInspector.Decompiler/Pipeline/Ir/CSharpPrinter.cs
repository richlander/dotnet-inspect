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

    /// <summary>The product path: runs the default raising passes, then prints. <see cref="Print"/> alone renders whatever tree it is given — right for stage dumps, wrong for output paths.</summary>
    public static DecompilerResult PrintRaised(IrFunction function)
    {
        try
        {
            IrPasses.Run(function);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return Print(function);
    }

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

    /// <summary>Stores that double as declarations: the local's first program-order reference, at statement level in the entry block.</summary>
    readonly HashSet<IrNode> _declaringStores = [];

    string PrintBody(IrFunction function)
    {
        var sb = new StringBuilder();
        var blocks = function.Body.Blocks;
        var labelTargets = CollectBranchTargets(function);
        CollectDeclaringStores(function);

        // Remaining locals and slots declare up front, current-style.
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
                AppendStatement(sb, statement, 0);
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

    IEnumerable<string> CollectDeclarations(IrFunction function)
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
        {
            bool declaredAtStore = _declaringStores.Any(s =>
                s is StoreLocal store && store.Index == index
                || s is InitObject { Address: LoadLocalAddress init } && init.Index == index);
            if (!declaredAtStore)
                yield return $"{TypeText(function.Locals[index])} V_{index};";
        }
        foreach (var (slot, type) in slots)
        {
            if (!_declaringStores.OfType<StoreStackSlot>().Any(s => s.Slot == slot))
                yield return $"{(type is null ? "var" : TypeText(type))} S_{slot};";
        }
    }

    /// <summary>
    /// A local declares at its store when that store is the local's first
    /// program-order reference and sits at statement level in the entry
    /// block — the current emitter's merged-declaration shape.
    /// </summary>
    void CollectDeclaringStores(IrFunction function)
    {
        if (function.Body.Blocks.Count == 0)
            return;
        var entryStatements = new HashSet<IrNode>(function.Body.Blocks[0].Children);
        var seenLocals = new HashSet<int>();
        var seenSlots = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreLocal store when !seenLocals.Contains(store.Index):
                    seenLocals.Add(store.Index);
                    if (entryStatements.Contains(store))
                        _declaringStores.Add(store);
                    else if (store is { Parent: ForLoop forLoop, ChildIndex: 0 }
                        && LastReferenceIsInside(function, store.Index, forLoop))
                    {
                        // A for-initializer declares its variable only when
                        // every reference lives inside the loop — otherwise
                        // C# scoping demands the declaration stay outside.
                        _declaringStores.Add(store);
                    }
                    break;
                case InitObject { Address: LoadLocalAddress initTarget } init when !seenLocals.Contains(initTarget.Index):
                    // Descendants yields the InitObject before its address
                    // child, so this fires before the address marks the
                    // local as seen.
                    seenLocals.Add(initTarget.Index);
                    if (entryStatements.Contains(init))
                        _declaringStores.Add(init);
                    break;
                case LoadLocal load: seenLocals.Add(load.Index); break;
                case LoadLocalAddress address: seenLocals.Add(address.Index); break;
                case StoreStackSlot slotStore when !seenSlots.Contains(slotStore.Slot):
                    seenSlots.Add(slotStore.Slot);
                    if (entryStatements.Contains(slotStore) && slotStore.Value.ResultType is not null)
                        _declaringStores.Add(slotStore);
                    break;
                case LoadStackSlot slotLoad: seenSlots.Add(slotLoad.Slot); break;
            }
        }
    }

    /// <summary>True when the local's last program-order reference sits inside the given subtree.</summary>
    static bool LastReferenceIsInside(IrFunction function, int localIndex, IrNode subtree)
    {
        IrNode? last = null;
        foreach (var node in function.Descendants)
        {
            if (node is LoadLocal load && load.Index == localIndex
                || node is StoreLocal store && store.Index == localIndex
                || node is LoadLocalAddress address && address.Index == localIndex)
            {
                last = node;
            }
        }
        for (var current = last; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, subtree))
                return true;
        }
        return false;
    }

    /// <summary>Recursive statement emission with indentation — structured nodes (IfStatement) nest, flat statements render through <see cref="Statement"/>.</summary>
    void AppendStatement(StringBuilder sb, IrNode node, int indent)
    {
        string pad = new(' ', indent * 4);
        if (node is ForLoop forLoop)
        {
            string initializer = Statement(forLoop.Initializer)?.TrimEnd(';') ?? "";
            string increment = Statement(forLoop.Increment)?.TrimEnd(';') ?? "";
            sb.Append(pad).Append("for (").Append(initializer).Append("; ")
                .Append(Condition(forLoop.Condition)).Append("; ").Append(increment).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            foreach (var statement in forLoop.Body.Children)
                AppendStatement(sb, statement, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is WhileLoop whileLoop)
        {
            sb.Append(pad).Append("while (").Append(Condition(whileLoop.Condition)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            foreach (var statement in whileLoop.Body.Children)
                AppendStatement(sb, statement, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is IfStatement ifStatement)
        {
            sb.Append(pad).Append("if (").Append(Condition(ifStatement.Condition)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            foreach (var statement in ifStatement.Then.Children)
                AppendStatement(sb, statement, indent + 1);
            sb.Append(pad).AppendLine("}");
            if (ifStatement.Else is { } elseArm)
            {
                sb.Append(pad).AppendLine("else");
                sb.Append(pad).AppendLine("{");
                foreach (var statement in elseArm.Children)
                    AppendStatement(sb, statement, indent + 1);
                sb.Append(pad).AppendLine("}");
            }
            return;
        }
        if (Statement(node) is { } line)
            sb.Append(pad).AppendLine(line);
    }

    /// <summary>Null means the statement has no body spelling: a no-argument base-constructor call is implicit in C#.</summary>
    string? Statement(IrNode node) => node switch
    {
        ExpressionStatement
        {
            Expression: Call
            {
                Callee: { Name: ".ctor", HasThis: true, ParameterTypes.IsEmpty: true } callee,
            } call,
        } when call.Arguments[0] is LoadArgument { Index: 0, Name: "this" }
            && !Equals(callee.DeclaringType, _function.DeclaringType)
            => null,
        ExpressionStatement e => e.Expression is UnsupportedNode u
            ? $"/* {u.Describe()} */"
            : $"{Expression(e.Expression)};",
        StoreLocal s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} V_{s.Index} = {Expression(s.Value)};"
            : AssignmentText($"V_{s.Index}", s.Value, left => left is LoadLocal load && load.Index == s.Index),
        StoreArgument s => AssignmentText(s.Name, s.Value, left => left is LoadArgument load && load.Index == s.Index),
        StoreStackSlot s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Value.ResultType!)} S_{s.Slot} = {Expression(s.Value)};"
            : AssignmentText($"S_{s.Slot}", s.Value, left => left is LoadStackSlot load && load.Slot == s.Slot),
        StoreField s => AssignmentText(
            FieldTarget(s.Field, s.Instance), s.Value,
            left => left is LoadField load
                && load.Field.Name == s.Field.Name
                && Equals(load.Field.DeclaringType, s.Field.DeclaringType)
                && SamePlace(load.Instance, s.Instance)),
        StoreProperty s => $"{PropertyTarget(s.Accessor, s.HasInstance ? s.Instance : null, s.IndexArguments, s.PropertyName)} = {Expression(s.Value)};",
        StoreElement s => $"{Expression(s.Array)}[{Expression(s.Index)}] = {Expression(s.Value)};",
        StoreIndirect s => $"*{Operand(s.Address)} = {Expression(s.Value)};",
        // default-initialization of a named place spells through the place,
        // not its address.
        InitObject { Address: LoadLocalAddress local } init => _declaringStores.Contains(init)
            ? $"{TypeText(init.Type)} V_{local.Index} = default;"
            : $"V_{local.Index} = default;",
        InitObject { Address: LoadArgumentAddress argument } => $"{argument.Name} = default;",
        InitObject { Address: LoadFieldAddress field } o2 => $"{FieldTarget(field.Field, field.Instance)} = default;",
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
        LogicalBinary l => LogicalText(l),
        Conditional t => $"{Condition(t.Condition)} ? {Operand(t.WhenTrue)} : {Operand(t.WhenFalse)}",
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
        TypeOf t => $"typeof({TypeText(t.Type)})",
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
        => ComparisonText(comparison.Kind, comparison.IsUnsigned, comparison.Left, comparison.Right);

    string ComparisonText(ComparisonKind kind, bool isUnsigned, IrExpression left, IrExpression right)
    {
        // On floats .un means UNORDERED, and C#'s ordering operators are
        // ordered — 'a >= b unordered' must print as !(a < b) or NaN inputs
        // execute the wrong path. Equality needs no special form: C#'s ==
        // is beq and != is bne.un already.
        if (isUnsigned && IsFloatComparison(left, right)
            && kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual)
        {
            return $"!({Operand(left)} {ComparisonOperator(Conditions.Inverse(kind))} {Operand(right)})";
        }
        // Null tests render the is-form (taste doc): it is always the
        // reference test the IL performs, where == could round-trip to an
        // op_Equality call under operator overloads.
        if (kind is ComparisonKind.Equal or ComparisonKind.NotEqual
            && (left is Constant { Value: null } || right is Constant { Value: null }))
        {
            var operand = right is Constant { Value: null } ? left : right;
            return kind == ComparisonKind.Equal
                ? $"{Operand(operand)} is null"
                : $"{Operand(operand)} is not null";
        }
        return isUnsigned
            ? $"{UnsignedOperand(left)} {ComparisonOperator(kind)} {UnsignedOperand(right)}"
            : $"{Operand(left)} {ComparisonOperator(kind)} {Operand(right)}";
    }

    static bool IsFloatComparison(IrExpression left, IrExpression right)
        => TypeFamilies.IsFloat(left.ResultType) || TypeFamilies.IsFloat(right.ResultType);

    /// <summary>Casts a signed-integer operand to its unsigned counterpart; already-unsigned, float (.un = unordered), and unknown-typed operands print plain.</summary>
    string UnsignedOperand(IrExpression operand)
    {
        string? cast = TypeFamilies.UnsignedCastKeyword(operand.ResultType);
        return cast is null ? Operand(operand) : $"({cast}){Operand(operand)}";
    }

    /// <summary>Conditions render brtrue's raw value as-is; LogicalNot over a comparison folds via the shared type-aware duals (float folds flip the unordered flag).</summary>
    string Condition(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: Comparison c } => ComparisonText(
            Conditions.Inverse(c.Kind),
            IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
            c.Left, c.Right),
        // brtrue/brfalse test any I4/ref value; C# conditions need bool —
        // non-bool operands spell the comparison the branch performs.
        LogicalNot { Operand: { } operand } when Truthiness(operand) is { } negated => negated.Inverted,
        LogicalNot n => $"!{Operand(n.Operand)}",
        _ when Truthiness(condition) is { } truthy => truthy.Direct,
        _ => Expression(condition),
    };

    /// <summary>
    /// Spellings for a non-bool branch operand: <c>!= 0</c> for known integer
    /// families, <c>!= null</c> for KNOWN reference shapes only (arrays,
    /// string, object). A bare definition could be a struct or an enum —
    /// TypeRef cannot yet tell — so unknowns return null and print as the
    /// raw value rather than a guessed comparison that might not compile.
    /// </summary>
    (string Direct, string Inverted)? Truthiness(IrExpression operand)
    {
        var type = operand.ResultType;
        if (type is null || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return null;

        string text = Operand(operand);
        return TypeFamilies.Of(type) switch
        {
            // Boolean was filtered above, so an I4 family here is a real integer (or char).
            StackFamily.I4 or StackFamily.I8 or StackFamily.I => ($"{text} != 0", $"{text} == 0"),
            StackFamily.O => ($"{text} is not null", $"{text} is null"),
            _ => null,
        };
    }

    /// <summary>Parenthesizes compound operands; leaves atoms bare. Conservative until the precedence visitor exists.</summary>
    string Operand(IrExpression node)
    {
        string text = Expression(node);
        bool atomic = node is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField
            or Call or NewObject or ArrayLength or LoadElement or CaughtException or SizeOf or LoadToken
            or LoadProperty or TypeOf;
        return atomic ? text : $"({text})";
    }

    /// <summary>
    /// Short-circuit composition prints comparisons and nots bare (they bind
    /// tighter than &amp;&amp;/||); same-kind chains associate without parens;
    /// mixed kinds parenthesize.
    /// </summary>
    string LogicalText(LogicalBinary logical)
    {
        // Sides are condition positions: Condition() owns truthiness (a
        // string operand spells 'is not null', never '!value') and the
        // negation folds. Same-kind chains associate bare; mixed kinds
        // and ternaries parenthesize.
        string Side(IrExpression side) => side switch
        {
            LogicalBinary nested when nested.Kind == logical.Kind => LogicalText(nested),
            LogicalBinary nested => $"({LogicalText(nested)})",
            Conditional => $"({Expression(side)})",
            _ => Condition(side),
        };
        string op = logical.Kind == LogicalKind.And ? "&&" : "||";
        return $"{Side(logical.Left)} {op} {Side(logical.Right)}";
    }

    /// <summary>
    /// Assignment spelling with compound/increment sugar: when the value is
    /// an unchecked binary whose left operand reads the assignment target,
    /// the runtime style is x++/x-- for ±1 and x op= rest otherwise.
    /// </summary>
    string AssignmentText(string target, IrExpression value, Func<IrExpression, bool> readsTarget)
    {
        if (value is Binary { IsChecked: false } binary && readsTarget(binary.Left))
        {
            if (binary.Kind is BinaryKind.Add or BinaryKind.Subtract && binary.Right is Constant { Value: 1 })
                return $"{target}{(binary.Kind == BinaryKind.Add ? "++" : "--")};";
            return $"{target} {BinaryOperator(binary)}= {Operand(binary.Right)};";
        }
        return $"{target} = {Expression(value)};";
    }

    /// <summary>Structural same-place check for compound-assignment receivers; conservative (this/locals/arguments/static only).</summary>
    static bool SamePlace(IrExpression? a, IrExpression? b) => (a, b) switch
    {
        (null, null) => true,
        (LoadArgument x, LoadArgument y) => x.Index == y.Index,
        (LoadLocal x, LoadLocal y) => x.Index == y.Index,
        _ => false,
    };

    /// <summary>The operator form of an op_* call, or null when the name has no spelling (op_True/op_False and friends stay as calls).</summary>
    string? OperatorSpelling(Call call)
    {
        var arguments = call.Arguments;
        if (arguments.Count == 2)
        {
            string? op = call.Callee.Name switch
            {
                "op_Equality" => "==", "op_Inequality" => "!=",
                "op_LessThan" => "<", "op_LessThanOrEqual" => "<=",
                "op_GreaterThan" => ">", "op_GreaterThanOrEqual" => ">=",
                "op_Addition" => "+", "op_Subtraction" => "-",
                "op_Multiply" => "*", "op_Division" => "/", "op_Modulus" => "%",
                "op_BitwiseAnd" => "&", "op_BitwiseOr" => "|", "op_ExclusiveOr" => "^",
                "op_LeftShift" => "<<", "op_RightShift" => ">>",
                _ => null,
            };
            return op is null ? null : $"{Operand(arguments[0])} {op} {Operand(arguments[1])}";
        }
        if (arguments.Count == 1)
        {
            return call.Callee.Name switch
            {
                "op_UnaryNegation" => $"-{Operand(arguments[0])}",
                "op_UnaryPlus" => $"+{Operand(arguments[0])}",
                "op_LogicalNot" => $"!{Operand(arguments[0])}",
                "op_OnesComplement" => $"~{Operand(arguments[0])}",
                "op_Implicit" or "op_Explicit" => $"({TypeText(call.Callee.ReturnType)}){Operand(arguments[0])}",
                _ => null,
            };
        }
        return null;
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
        // An instance property accessor with index arguments IS an indexer,
        // whatever its metadata name (String's is Chars, not Item).
        if (instance is not null && indexArguments.Count > 0)
            return $"{(receiver.Length == 0 ? "this" : receiver)}[{Arguments(indexArguments)}]";
        string dotted = receiver.Length == 0 ? name : $"{receiver}.{name}";
        return indexArguments.Count == 0 ? dotted : $"{dotted}[{Arguments(indexArguments)}]";
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
        {
            // C# compiles user-defined operators TO these calls; the
            // operator spelling is the faithful inverse.
            if (call.Callee.IsSpecialName && OperatorSpelling(call) is { } op)
                return op;
            return $"{TypeText(call.Callee.DeclaringType)}.{call.Callee.Name}{typeArguments}({Arguments(arguments)})";
        }
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
        // conv.r.un and conv.ovf.*.un interpret the SOURCE as unsigned —
        // a signed operand needs its unsigned cast or the value is wrong.
        string operand = convert.IsUnsigned ? UnsignedOperand(convert.Operand) : Operand(convert.Operand);
        string cast = $"({TypeText(convert.Target)}){operand}";
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
