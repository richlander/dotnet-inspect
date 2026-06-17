using System.Globalization;
using System.Text;

namespace ILInspector.Decompiler.Pipeline;

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
            var printer = new CSharpPrinter(function);
            string output = printer.PrintBody(function);
            return new DecompilerResult(output, function.Fidelity, [.. function.Diagnostics])
            {
                ConstructorChain = printer._constructorChain,
            };
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Stores that double as declarations: the local's first program-order reference, at statement level in the entry block.</summary>
    readonly HashSet<IrNode> _declaringStores = [];

    /// <summary>Offsets some surviving goto targets — labels print wherever the block lives, top-level or inside a flat EH body.</summary>
    HashSet<int> _labelTargets = [];

    /// <summary>An explicit base/this chain call lifted out of a constructor body to its signature initializer (base/this calls are invalid as body statements).</summary>
    string? _constructorChain;
    IrNode? _chainStatement;

    string PrintBody(IrFunction function)
    {
        var sb = new StringBuilder();
        _labelTargets = CollectBranchTargets(function);
        CollectDeclaringStores(function);

        // An explicit base(...)/this(...) chain opens a constructor body; lift
        // it to a signature initializer so the body stays valid C# (a base/this
        // call as a body statement is CS0175).
        if (function.Body.Blocks is [{ Children: [var first, ..] }, ..]
            && first is ExpressionStatement { Expression: Call { Callee: { Name: ".ctor", HasThis: true } callee } call }
            && call.Arguments is [LoadArgument { Index: 0 }, ..]
            && ConstructorChainText(callee, call) is { } chain)
        {
            _constructorChain = chain.TrimEnd(';');
            _chainStatement = first;
        }

        // Remaining locals and slots declare up front, current-style.
        foreach (var declaration in CollectDeclarations(function))
            sb.AppendLine(declaration);
        if (sb.Length > 0)
            sb.AppendLine();

        AppendContainer(sb, function.Body, 0, topLevel: true);
        return sb.ToString().TrimEnd() is { Length: > 0 } text ? text + Environment.NewLine : "";
    }

    void AppendContainer(StringBuilder sb, BlockContainer container, int indent, bool topLevel = false)
    {
        string pad = new(' ', indent * 4);
        var blocks = container.Blocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (_labelTargets.Contains(block.StartOffset))
                sb.Append(pad).AppendLine($"IL_{block.StartOffset:X4}:");
            // The trailing 'return;' trims, current-style — unless it is a
            // labeled block's only statement, where trimming would strand
            // the label as invalid C#.
            bool labeledReturnOnly = _labelTargets.Contains(block.StartOffset) && block.Children.Count == 1;
            foreach (var statement in block.Children)
            {
                if (ReferenceEquals(statement, _chainStatement))
                    continue;   // lifted to the signature initializer
                bool isLast = topLevel && i == blocks.Count - 1 && ReferenceEquals(statement, block.Children[^1]);
                if (isLast && !labeledReturnOnly && statement is Return { Value: null })
                    break;
                AppendStatement(sb, statement, indent);
            }
        }
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
        var slotLoadTypes = new Dictionary<int, TypeRef?>();
        var slotStoreTypes = new Dictionary<int, TypeRef?>();
        // Catch variables declare in their clause header, not up front.
        var clauseDeclared = function.Descendants.OfType<CatchClause>()
            .Where(clause => clause.VariableIndex is not null)
            .Select(clause => clause.VariableIndex!.Value)
            .ToHashSet();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal l: locals.Add(l.Index); break;
                case StoreLocal s: locals.Add(s.Index); break;
                case LoadLocalAddress a: locals.Add(a.Index); break;
                // A slot's declared type is the type it is loaded AS — the merged
                // join type every predecessor's store is assignable to. A store
                // value can be a subtype at a join (object slot fed a string),
                // so store types are only a fallback when the slot is never
                // loaded with a known type.
                case LoadStackSlot ls: slotLoadTypes.TryAdd(ls.Slot, ls.Type); break;
                case StoreStackSlot ss: slotStoreTypes.TryAdd(ss.Slot, ss.Value.ResultType); break;
            }
        }
        foreach (int slot in slotLoadTypes.Keys.Concat(slotStoreTypes.Keys).Distinct())
        {
            slots[slot] = slotLoadTypes.TryGetValue(slot, out var loaded) && loaded is not null
                ? loaded
                : slotStoreTypes.TryGetValue(slot, out var stored) ? stored : null;
        }
        foreach (int index in locals)
        {
            bool declaredAtStore = _declaringStores.Any(s =>
                s is StoreLocal store && store.Index == index
                || s is InitObject { Address: LoadLocalAddress init } && init.Index == index);
            if (!declaredAtStore && !clauseDeclared.Contains(index))
            {
                // An up-front local is referenced before a defining store, so
                // it relies on IL's zero-initialization of locals (localsinit).
                // Spell that as `= default` — both faithful and what C#'s
                // definite-assignment requires (a bare declaration is CS0165 on
                // any path that reads before assigning). A ref local takes
                // neither: `= default` is illegal and a bare declaration is
                // CS8174. IL zero-initializes a managed pointer to a null
                // reference, whose faithful C# spelling is Unsafe.NullRef<T>().
                // Fully qualified so the per-member view compiles without a
                // using; the whole-type hoister shortens it and adds the using.
                var type = function.Locals[index];
                yield return type.Kind == TypeRefKind.ByRef
                    ? $"{TypeText(type)} V_{index} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
                    : $"{TypeText(type)} V_{index} = default;";
            }
        }
        foreach (var (slot, type) in slots)
        {
            if (_declaringStores.OfType<StoreStackSlot>().Any(s => s.Slot == slot))
                continue;
            // A ref-typed slot, like a ref-typed local, can't be declared bare
            // (CS8174); spell IL's null-reference zero-init as Unsafe.NullRef<T>().
            yield return type is { Kind: TypeRefKind.ByRef }
                ? $"{TypeText(type)} S_{slot} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
                : $"{(type is null ? "var" : TypeText(type))} S_{slot};";
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
        // A slot stored on more than one path is a join slot: it must declare
        // once, up front, with its merged type — declaring it at one store would
        // type it from that branch's value and strand the other branch's store.
        var slotStoreCounts = new Dictionary<int, int>();
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            slotStoreCounts[store.Slot] = slotStoreCounts.GetValueOrDefault(store.Slot) + 1;
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
                    if (entryStatements.Contains(slotStore) && slotStore.Value.ResultType is not null
                        && slotStoreCounts[slotStore.Slot] == 1)
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
        if (node is DoWhileLoop doWhile)
        {
            sb.Append(pad).AppendLine("do");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, doWhile.Body, indent + 1);
            sb.Append(pad).Append("}").Append(Environment.NewLine).Append(pad)
                .Append("while (").Append(Condition(doWhile.Condition)).AppendLine(");");
            return;
        }
        if (node is TryCatch tryCatch)
        {
            sb.Append(pad).AppendLine("try");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, tryCatch.TryBody, indent + 1);
            sb.Append(pad).AppendLine("}");
            foreach (var clause in tryCatch.Clauses)
            {
                sb.Append(pad).AppendLine(CatchHeader(clause));
                sb.Append(pad).AppendLine("{");
                AppendContainer(sb, clause.Body, indent + 1);
                sb.Append(pad).AppendLine("}");
            }
            return;
        }
        if (node is Lock lockStatement)
        {
            sb.Append(pad).Append("lock (").Append(Expression(lockStatement.LockObject)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, lockStatement.Body, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is TryFinally tryFinally)
        {
            sb.Append(pad).AppendLine("try");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, tryFinally.TryBody, indent + 1);
            sb.Append(pad).AppendLine("}");
            sb.Append(pad).AppendLine("finally");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, tryFinally.FinallyBody, indent + 1);
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
        if (node is Switch switchNode)
        {
            sb.Append(pad).Append("switch (").Append(Expression(switchNode.Value)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            string labelPad = pad + "    ";
            foreach (var section in switchNode.Sections)
            {
                if (section.IsDefault)
                    sb.Append(labelPad).AppendLine("default:");
                else
                    foreach (int label in section.Labels)
                        sb.Append(labelPad).Append("case ").Append(label).AppendLine(":");
                AppendContainer(sb, section.Body, indent + 2);
            }
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (Statement(node) is { } line)
            sb.Append(pad).AppendLine(line);
    }

    /// <summary>
    /// A constructor-chain call renders as a <c>base(args)</c> / <c>this(args)</c>
    /// body statement (the current emitter's placement, not a header
    /// initializer). The implicit parameterless base call — every default
    /// chain — is suppressed.
    /// </summary>
    string? ConstructorChainText(MethodRef callee, Call call)
    {
        bool isThis = Equals(callee.DeclaringType, _function.DeclaringType);
        var arguments = call.Arguments.Skip(1).ToList();
        if (!isThis && arguments.Count == 0)
            return null;  // implicit base()
        return $"{(isThis ? "this" : "base")}({Arguments(arguments, callee.ParameterTypes)});";
    }

    /// <summary>Baseline-style clause headers: bare <c>catch</c> for object (the catch-all), the variable form when the entry store folded into the clause.</summary>
    string CatchHeader(CatchClause clause)
        => clause.ExceptionType is { Namespace: "System", Name: "Object" }
            ? "catch"
            : clause.VariableIndex is { } index
                ? $"catch ({TypeText(clause.ExceptionType)} V_{index})"
                : $"catch ({TypeText(clause.ExceptionType)})";

    /// <summary>Null means the statement has no body spelling: a no-argument base-constructor call is implicit in C#.</summary>
    string? Statement(IrNode node) => node switch
    {
        ExpressionStatement
        {
            Expression: Call { Callee: { Name: ".ctor", HasThis: true } callee } call,
        } when call.Arguments is [LoadArgument { Index: 0 }, ..]
            => ConstructorChainText(callee, call),
        ExpressionStatement e => e.Expression is UnsupportedNode u
            ? $"/* {u.Describe()} */"
            : $"{Expression(e.Expression)};",
        // Storing into a ref-typed local rebinds the reference itself (stloc of
        // a managed pointer), not a write-through — that is C#'s ref
        // (re)assignment, which takes `= ref <place>` on both the initial
        // declaration (CS8172) and any later rebind (CS8173). Deref renders the
        // address value as the place it refers to.
        StoreLocal { Type.Kind: TypeRefKind.ByRef } s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} V_{s.Index} = ref {Deref(s.Value)};"
            : $"V_{s.Index} = ref {Deref(s.Value)};",
        StoreLocal s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} V_{s.Index} = {CastValue(s.Value, s.Type)};"
            : AssignmentText($"V_{s.Index}", s.Value, left => left is LoadLocal load && load.Index == s.Index, s.Type),
        StoreArgument s => AssignmentText(s.Name, s.Value, left => left is LoadArgument load && load.Index == s.Index, s.Type),
        // A ref-typed slot stores by rebinding the reference — C#'s ref
        // (re)assignment, exactly as for ref locals above.
        StoreStackSlot { Value.ResultType.Kind: TypeRefKind.ByRef } s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Value.ResultType!)} S_{s.Slot} = ref {Deref(s.Value)};"
            : $"S_{s.Slot} = ref {Deref(s.Value)};",
        StoreStackSlot s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Value.ResultType!)} S_{s.Slot} = {Expression(s.Value)};"
            : AssignmentText($"S_{s.Slot}", s.Value, left => left is LoadStackSlot load && load.Slot == s.Slot),
        StoreField s => AssignmentText(
            FieldTarget(s.Field, s.Instance), s.Value,
            left => left is LoadField load
                && load.Field.Name == s.Field.Name
                && Equals(load.Field.DeclaringType, s.Field.DeclaringType)
                && SamePlace(load.Instance, s.Instance),
            s.Field.Type),
        StoreProperty s => $"{PropertyTarget(s.Accessor, s.HasInstance ? s.Instance : null, s.IndexArguments, s.PropertyName, s.IsVirtual)} = {Expression(s.Value)};",
        StoreElement s => $"{Expression(s.Array)}[{Expression(s.Index)}] = {CastValue(s.Value, s.ElementType)};",
        StoreIndirect s => $"{Deref(s.Address)} = {CastValue(s.Value, s.Type)};",
        // default-initialization of a named place spells through the place,
        // not its address.
        InitObject { Address: LoadLocalAddress local } init => _declaringStores.Contains(init)
            ? $"{TypeText(init.Type)} V_{local.Index} = default;"
            : $"V_{local.Index} = default;",
        InitObject { Address: LoadArgumentAddress argument } => $"{argument.Name} = default;",
        InitObject { Address: LoadFieldAddress field } o2 => $"{FieldTarget(field.Field, field.Instance)} = default;",
        InitObject o => $"{Deref(o.Address)} = default({TypeText(o.Type)});",
        Return { Value: { } value } => $"return {CastValue(value, _function.Signature.ReturnType)};",
        Return => "return;",
        // The rethrow: the raw caught value thrown back is C#'s bare throw.
        Throw { Value: CaughtException } => "throw;",
        Throw t => $"throw {Expression(t.Value)};",
        Break => "break;",
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
        Constant { Value: int } c when EnumMemberName(c) is { } named => named,
        // A retyped enum constant with no single named member (a composite flag
        // value, or one outside the resolved member map) is still that enum — a
        // bare int is CS0266. Cast it; naming flag combinations is a later slice.
        // A negative value must be parenthesized after the cast (else CS0075).
        Constant { Value: int value, Type: { } enumType } when _function.TypeShapes.GetValueOrDefault(enumType) == TypeShape.Enum
            => $"({TypeText(enumType)}){(value < 0 ? $"({value})" : value.ToString(CultureInfo.InvariantCulture))}",
        Constant c => ConstantText(c),
        LoadField f => FieldTarget(f.Field, f.Instance),
        Binary b => BinaryText(b),
        Comparison c => ComparisonText(c),
        LogicalNot n => $"!{Operand(n.Operand)}",
        LogicalBinary l => LogicalText(l),
        Conditional t => $"{Condition(t.Condition)} ? {Operand(t.WhenTrue)} : {Operand(t.WhenFalse)}",
        Coalesce co => $"{Operand(co.Left)} ?? {Operand(co.Right)}",
        NullConditional nc => NullConditionalText(nc),
        Unary { Kind: UnaryKind.Negate } u => $"-{Operand(u.Operand)}",
        Unary u => $"~{Operand(u.Operand)}",
        Convert v => ConvertText(v),
        Call c => CallText(c),
        CallIndirect ci => $"{Operand(ci.Pointer)}({Arguments(ci.Arguments)})",
        DelegateCreation d => $"new {TypeText(d.DelegateType)}({MethodGroupText(d.Method, d.Target)})",
        AddressOfMethod m => AddressOfMethodText(m),
        LoadFunctionPointer p => $"/* {p.Describe()} */",
        LoadProperty p => PropertyTarget(p.Accessor, p.HasInstance ? p.Instance : null, p.IndexArguments, p.PropertyName, p.IsVirtual),
        NewObject n => $"new {TypeText(n.Constructor.DeclaringType)}({Arguments(n.Arguments, n.Constructor.ParameterTypes)})",
        ArrayLength l => $"{Operand(l.Array)}.Length",
        LoadElement e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        NewArray n => $"new {TypeText(n.ElementType)}[{Expression(n.Length)}]",
        StackAllocate s => $"stackalloc byte[{Expression(s.Size)}]",
        Box b => Expression(b.Operand),
        IsInstance i => $"{Operand(i.Operand)} as {TypeText(i.Type)}",
        CastClass c => $"({TypeText(c.Type)}){Operand(c.Operand)}",
        UnboxAny u => $"({TypeText(u.Type)}){Operand(u.Operand)}",
        Unbox u => $"ref ({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocalAddress a => $"ref V_{a.Index}",
        LoadArgumentAddress a => $"ref {a.Name}",
        LoadFieldAddress f => $"ref {FieldTarget(f.Field, f.Instance)}",
        LoadElementAddress e => $"ref {Operand(e.Array)}[{Expression(e.Index)}]",
        LoadIndirect l => Deref(l.Address),
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
    /// Spellings for a non-bool branch operand: <c>!= 0</c> for integers and
    /// enums, <c>is null</c>/<c>is not null</c> for reference shapes. The
    /// operand is a <c>brfalse</c>/<c>brtrue</c> value, so the CLI constrains
    /// it to int, native int, object reference, or managed pointer — never a
    /// struct value. A generic instance is therefore always a reference type
    /// (generic value types cannot be branch operands, and enums are never
    /// generic), so it null-tests with no resolution. A bare definition is
    /// reference-or-enum; the importer's same-assembly shape resolution tells
    /// them apart where it can, and an unresolved (cross-assembly) definition
    /// still prints raw rather than guess.
    /// </summary>
    (string Direct, string Inverted)? Truthiness(IrExpression operand)
    {
        var type = operand.ResultType;
        if (type is null || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return null;

        string text = Operand(operand);
        (string, string) reference = ($"{text} is not null", $"{text} is null");
        (string, string) integer = ($"{text} != 0", $"{text} == 0");

        switch (TypeFamilies.Of(type))
        {
            // Boolean was filtered above, so an I4 family here is a real integer (or char).
            case StackFamily.I4 or StackFamily.I8 or StackFamily.I:
                return integer;
            case StackFamily.O:
                return reference;
            case StackFamily.F:
                return null;   // a float is never a branch operand
        }

        // No primitive family. A generic instance is provably a reference; a
        // bare definition resolves by its same-assembly shape.
        if (type.Kind == TypeRefKind.GenericInstance)
            return reference;

        return _function.TypeShapes.GetValueOrDefault(type) switch
        {
            TypeShape.Reference => reference,
            TypeShape.Enum => integer,
            _ => null,   // a struct cannot be a branch operand; unknown stays raw
        };
    }

    /// <summary>Parenthesizes compound operands; leaves atoms bare. Conservative until the precedence visitor exists.</summary>
    string Operand(IrExpression node)
    {
        string text = Expression(node);
        bool atomic = node is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField
            or Call or NewObject or ArrayLength or LoadElement or CaughtException or SizeOf or LoadToken
            or LoadProperty or TypeOf or DelegateCreation or CallIndirect or AddressOfMethod or NullConditional;
        return atomic ? text : $"({text})";
    }

    /// <summary>
    /// The C# place a load/store-indirect reads or writes. Dereferencing a
    /// managed reference is implicit in C#: the address of a place (ref local,
    /// ref argument, ref field, ref element) reads back as the place, and a
    /// ref/out parameter or ref local reads as itself — no <c>*</c>. Only a
    /// genuine unmanaged pointer takes the <c>*</c>; an unknown reference keeps
    /// it rather than guess.
    /// </summary>
    string Deref(IrExpression address) => address switch
    {
        LoadLocalAddress a => $"V_{a.Index}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        LoadElementAddress e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        { ResultType.Kind: TypeRefKind.Pointer } => $"*{Operand(address)}",
        // A ref-typed conditional is a ref ternary: the `ref` binds each arm
        // (`cond ? ref a : ref b`), not the expression as a whole — placing it
        // outside is CS8173 in a `= ref` position. This spelling applies only
        // when both arms are themselves references. A conditional typed ref by
        // an upstream merge that carries a non-reference arm is an inexpressible
        // merge — no valid `= ref` form exists for it — so it falls to the
        // generic spelling as a best effort, not a correctness guarantee. Only
        // BooleanFoldingPass.FoldSlotDiamond produces these, and an asymmetric
        // ref/value slot merge is not seen in non-synthetic IL.
        Conditional { ResultType.Kind: TypeRefKind.ByRef } c
            when c.WhenTrue.ResultType?.Kind == TypeRefKind.ByRef
                && c.WhenFalse.ResultType?.Kind == TypeRefKind.ByRef
            => $"({Condition(c.Condition)} ? ref {Deref(c.WhenTrue)} : ref {Deref(c.WhenFalse)})",
        { ResultType.Kind: TypeRefKind.ByRef } => Operand(address),
        _ => $"*{Operand(address)}",
    };

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
    string AssignmentText(string target, IrExpression value, Func<IrExpression, bool> readsTarget, TypeRef? targetType = null)
    {
        if (value is Binary { IsChecked: false } binary && readsTarget(binary.Left))
        {
            // A compound assignment only forms when the value reads the target
            // in same-type arithmetic, so the result already matches the target
            // — no conversion is involved on this path.
            if (binary.Kind is BinaryKind.Add or BinaryKind.Subtract && binary.Right is Constant { Value: 1 })
                return $"{target}{(binary.Kind == BinaryKind.Add ? "++" : "--")};";
            return $"{target} {BinaryOperator(binary)}= {Operand(binary.Right)};";
        }
        return $"{target} = {CastValue(value, targetType)};";
    }

    /// <summary>
    /// Renders <paramref name="value"/> for a position typed <paramref name="target"/>,
    /// inserting the explicit numeric cast C# requires when the value's type
    /// would not implicitly convert (the missing-cast case behind CS0266). The
    /// cast reinterprets bits the evaluation stack already carries (same family),
    /// so it is faithful to the IL — see <see cref="TypeFamilies.NeedsNumericCast"/>.
    /// </summary>
    string CastValue(IrExpression value, TypeRef? target)
    {
        // Cast only off a value whose rendered C# type reliably equals its IR
        // result type. A merge node (ternary/coalesce) reports a merged type the
        // arms may not actually share, and a stack slot's type is the join of
        // every store — both diverge from the rendered type in slot-confused or
        // generic bodies, where a keyed cast would be illegal (CS0030). Leave
        // those to render as-is.
        if (value is Conditional or Coalesce or LoadStackSlot)
            return Expression(value);
        // A constant carries an exact value: C# converts an in-range one to the
        // target type implicitly (render bare), while an out-of-range one — a
        // negative into unsigned, a bitmask wider than the target — does not
        // convert bare and is CS0266/CS0221, so reinterpret its bits with an
        // unchecked cast (uint.MaxValue's `ldc.i4.m1` → unchecked((uint)(-1))).
        if (value is Constant { Value: int or long } konst && target is { } t && TypeFamilies.IsNumericPrimitive(t))
            return NumericConstant(konst, t);
        if (!TypeFamilies.NeedsNumericCast(EffectiveType(value), target))
            return Expression(value);
        // A plain conversion to a same-width sibling (conv.u2 → ushort feeding a
        // char slot) is subsumed by the boundary cast: emit one cast to the
        // target on the conversion's operand, not (char)((ushort)x). An
        // out-of-range constant operand still needs the unchecked spelling.
        if (value is Convert { IsChecked: false, IsUnsigned: false } conv && TypeFamilies.SameWidth(conv.Target, target))
            return conv.Operand is Constant { Value: int or long } convConst
                ? NumericConstant(convConst, target!)
                : $"({TypeText(target!)}){Operand(conv.Operand)}";
        return $"({TypeText(target!)}){Operand(value)}";
    }

    /// <summary>
    /// An integer constant rendered for a numeric target: bare when in range (C#
    /// converts it implicitly), reinterpreted with an unchecked cast when out of
    /// range (a negative into unsigned, a mask wider than the target — CS0031/
    /// CS0221 as a bare or plain-cast literal).
    /// </summary>
    string NumericConstant(Constant konst, TypeRef target)
    {
        long literal = konst.Value is int i ? i : (long)konst.Value!;
        return TypeFamilies.ConstantFits(literal, target)
            ? Expression(konst)
            : $"unchecked(({TypeText(target)})({Expression(konst)}))";
    }

    /// <summary>
    /// The C# type the rendered expression actually has. For an unsigned
    /// div/rem/shr the printer casts the operands to their unsigned type, so the
    /// rendered result is unsigned even though the node's ECMA binary-promotion
    /// ResultType keeps the signed operand type; reflect that so a boundary into
    /// the matching unsigned type does not redundantly re-cast.
    /// </summary>
    static TypeRef? EffectiveType(IrExpression value)
        => value is Binary { IsUnsigned: true, Kind: BinaryKind.Divide or BinaryKind.Remainder or BinaryKind.ShiftRight }
            && TypeFamilies.UnsignedCounterpart(value.ResultType) is { } unsigned
            ? unsigned
            : value.ResultType;

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

    string PropertyTarget(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, string name, bool isVirtual = true)
    {
        string receiver = instance switch
        {
            // A NON-virtual this-receiver access to a base-declared member is
            // C#'s base. — the call opcode deliberately skips dispatch.
            LoadArgument { Index: 0, Name: "this" } when !isVirtual && IsCrossType(accessor.DeclaringType) => "base",
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

    /// <summary>True when the member's declaring DEFINITION differs from the function's — self-calls in generic types arrive as instantiations (List&lt;!0&gt;) and must not count as cross-type.</summary>
    bool IsCrossType(TypeRef memberDeclaringType)
    {
        static TypeRef Definition(TypeRef type)
            => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;
        return !Equals(Definition(memberDeclaringType), Definition(_function.DeclaringType));
    }

    /// <summary>Member-access receivers: value-type receivers arrive by address in IL; C# spells the place itself, not its address.</summary>
    string ReceiverText(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress a => $"V_{a.Index}",
        LoadArgumentAddress a => a.Name,
        LoadFieldAddress f => FieldTarget(f.Field, f.Instance),
        _ => Operand(receiver),
    };

    /// <summary>
    /// A method group for a delegate creation: a null target is a static
    /// method group (Type.Method); a this-receiver drops the qualifier to match
    /// instance-call spelling; any other receiver qualifies the name.
    /// </summary>
    string MethodGroupText(MethodRef method, IrExpression target)
    {
        string name = MethodName(method.Name);
        if (target is Constant { Value: null })
            return $"{TypeText(method.DeclaringType)}.{name}";
        if (target is LoadArgument { Index: 0, Name: "this" })
            return name;
        return $"{ReceiverText(target)}.{name}";
    }

    /// <summary>
    /// <c>&amp;Method</c> for a static method group. A same-type target — every
    /// local function, and members of the function's own type — needs no
    /// qualifier; a cross-type static method is qualified by its declaring type.
    /// Generic methods carry their type arguments (<c>&amp;Method&lt;int&gt;</c>).
    /// </summary>
    string AddressOfMethodText(AddressOfMethod node)
    {
        var method = node.Method;
        string typeArguments = method.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", method.TypeArguments.Select(TypeText))}>";
        string name = $"{MethodName(method.Name)}{typeArguments}";
        return IsCrossType(method.DeclaringType)
            ? $"&{TypeText(method.DeclaringType)}.{name}"
            : $"&{name}";
    }

    /// <summary>
    /// The source name of a call target. A compiler-generated local function
    /// carries the metadata name <c>&lt;Enclosing&gt;g__Local|N_M</c>, which is
    /// not a valid C# identifier; the source name is the segment between
    /// <c>g__</c> and the <c>|</c> ordinal suffix.
    /// </summary>
    static string MethodName(string name)
    {
        if (!name.StartsWith('<'))
            return name;
        int marker = name.IndexOf("g__", StringComparison.Ordinal);
        if (marker < 0)
            return name;
        int start = marker + 3;
        int bar = name.IndexOf('|', start);
        return bar > start ? name[start..bar] : name[start..];
    }

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
            return $"{TypeText(call.Callee.DeclaringType)}.{MethodName(call.Callee.Name)}{typeArguments}({Arguments(arguments, call.Callee.ParameterTypes)})";
        }
        var receiver = arguments[0];
        string rest = Arguments(arguments.Skip(1), call.Callee.ParameterTypes);
        if (call.Callee.Name == ".ctor" && receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // A this-receiver constructor call is C#'s base(...)/this(...).
            string keyword = Equals(call.Callee.DeclaringType, _function.DeclaringType) ? "this" : "base";
            return $"{keyword}({rest})";
        }
        if (receiver is LoadArgument { Index: 0, Name: "this" })
        {
            // Non-virtual this-receiver call to a base-declared method is
            // C#'s base.M() — the call opcode deliberately skips dispatch.
            return !call.IsVirtual && IsCrossType(call.Callee.DeclaringType)
                ? $"base.{MethodName(call.Callee.Name)}{typeArguments}({rest})"
                : $"{MethodName(call.Callee.Name)}{typeArguments}({rest})";
        }
        return $"{ReceiverText(receiver)}.{MethodName(call.Callee.Name)}{typeArguments}({rest})";
    }

    string Arguments(IEnumerable<IrExpression> arguments)
        => string.Join(", ", arguments.Select(Expression));

    /// <summary>
    /// Arguments paired positionally with the callee's parameter types, casting
    /// each to its parameter type where C# needs it (CS0266) — the call-site
    /// counterpart of the return/store boundary casts. Callers pass arguments
    /// that already align 1:1 with the parameters (the receiver of an instance
    /// call is dropped first), so index i maps to parameterTypes[i].
    /// </summary>
    string Arguments(IEnumerable<IrExpression> arguments, IReadOnlyList<TypeRef> parameterTypes)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var argument in arguments)
        {
            parts.Add(i < parameterTypes.Count ? CastValue(argument, parameterTypes[i]) : Expression(argument));
            i++;
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// <c>target?.Member</c>: the member's receiver child is the target, and the
    /// member's name/arguments form the suffix after <c>?</c>. Mirrors the
    /// instance spellings of <see cref="CallText"/>, <see cref="PropertyTarget"/>,
    /// and <see cref="FieldTarget"/>, minus their receiver — the <c>?.</c> owns it.
    /// </summary>
    string NullConditionalText(NullConditional node)
    {
        var member = node.Member;
        var receiver = NullConditionalReceiver(member);
        return $"{ReceiverText(receiver)}?{NullConditionalSuffix(member)}";
    }

    static IrExpression NullConditionalReceiver(IrExpression member) => member switch
    {
        Call call => call.Arguments[0],
        LoadProperty property => property.Instance!,
        LoadField field => field.Instance!,
        _ => member,
    };

    string NullConditionalSuffix(IrExpression member) => member switch
    {
        LoadField field => $".{field.Field.Name}",
        LoadProperty property when property.IndexArguments.Count > 0 => $"[{Arguments(property.IndexArguments)}]",
        LoadProperty property => $".{property.PropertyName}",
        Call call => NullConditionalCallSuffix(call),
        _ => $".{member.Describe()}",
    };

    string NullConditionalCallSuffix(Call call)
    {
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        return $".{MethodName(call.Callee.Name)}{typeArguments}({Arguments(call.Arguments.Skip(1), call.Callee.ParameterTypes)})";
    }

    string ConvertText(Convert convert)
    {
        // Converting an out-of-range integer constant (conv.u8 of ldc.i4.m1 for
        // ulong.MaxValue) is CS0221 as a plain cast; reinterpret its bits with
        // unchecked, matching the constant handling at value boundaries.
        if (!convert.IsChecked && convert.Operand is Constant { Value: int or long } c
            && TypeFamilies.IsNumericPrimitive(convert.Target))
        {
            long literal = c.Value is int i ? i : (long)c.Value!;
            if (!TypeFamilies.ConstantFits(literal, convert.Target))
                return $"unchecked(({TypeText(convert.Target)})({Expression(convert.Operand)}))";
        }
        // conv.r.un and conv.ovf.*.un interpret the SOURCE as unsigned —
        // a signed operand needs its unsigned cast or the value is wrong.
        string operand = convert.IsUnsigned ? UnsignedOperand(convert.Operand) : Operand(convert.Operand);
        string cast = $"({TypeText(convert.Target)}){operand}";
        return convert.IsChecked ? $"checked({cast})" : cast;
    }

    /// <summary>
    /// A retyped enum constant renders <c>EnumType.Member</c> when its value
    /// names exactly one member of the resolved (same-assembly) enum. Composite
    /// flag values and unnamed casts have no exact member and fall through to
    /// the raw integer — naming those is a later slice.
    /// </summary>
    string? EnumMemberName(Constant constant)
        => constant.Value is int value
            && _function.EnumMembers.TryGetValue(constant.Type, out var members)
            && members.TryGetValue(value, out var name)
            ? $"{TypeText(constant.Type)}.{name}"
            : null;

    static string ConstantText(Constant constant) => constant.Value switch
    {
        null => "null",
        string s => StringText(s),
        bool b => b ? "true" : "false",
        char c => CharText(c),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        float f => $"{f.ToString("R", CultureInfo.InvariantCulture)}f",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)}d",
        _ => constant.Value.ToString() ?? "?",
    };

    static string CharText(char c) => $"'{EscapeChar(c, inString: false)}'";

    /// <summary>A C# string literal with every char that needs escaping escaped — control chars, quotes, backslashes — so the output always compiles.</summary>
    static string StringText(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (char c in value)
            sb.Append(EscapeChar(c, inString: true));
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// The single home for C# character escaping, shared by char and string
    /// literals. The active delimiter is escaped (<c>"</c> in a string,
    /// <c>'</c> in a char); every control character gets a recognized escape
    /// or a <c>\u</c> sequence so a raw newline or tab never reaches the output.
    /// </summary>
    static string EscapeChar(char c, bool inString) => c switch
    {
        '\\' => "\\\\",
        '"' when inString => "\\\"",
        '\'' when !inString => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        _ when char.IsControl(c) => $"\\u{(int)c:x4}",
        _ => c.ToString(),
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
