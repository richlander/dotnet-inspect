using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ILInspector.ControlFlow;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// First C# projection of the IR: honest lowered output. Structure that has
/// not been raised renders as what it is — flat blocks with labels and
/// gotos — never as guessed sugar. Formatting is deliberately plain (bare
/// this-members, V_N/S_N names, trimmed trailing return) so a diff over the
/// output measures structural distance, not whitespace noise. The raising
/// passes close the goto gap from here; this printer is where completeness
/// starts.
/// </summary>
public sealed partial class CSharpPrinter
{
    readonly IrFunction _function;

    /// <summary>
    /// True when the source module opts into the updated memory-safety rules
    /// (see <see cref="IrFunction.UsesUpdatedMemorySafetyRules"/>). When set, the
    /// printer wraps unsafe operations in explicit <c>unsafe { }</c> blocks.
    /// </summary>
    readonly bool _newMemorySafetyRules;

    /// <summary>
    /// True when the method body skips locals initialization (<see
    /// cref="IrFunction.SkipLocalsInit"/>) — the extra condition that makes a
    /// <c>stackalloc</c>-to-<c>Span</c> conversion unsafe under the new rules.
    /// </summary>
    readonly bool _skipLocalsInit;

    /// <summary>
    /// Nesting depth of emitted <c>unsafe { }</c> blocks. Non-zero means the
    /// current statements are already in an unsafe context, so inner operations
    /// are not wrapped again (no redundant nested blocks).
    /// </summary>
    int _unsafeDepth;

    readonly PrinterOptions _options;
    readonly HashSet<string> _reservedScopeNames;
    readonly List<DecompilerDecision> _decisions = [];
    readonly HashSet<string> _decisionKeys = [];

    CSharpPrinter(
        IrFunction function,
        PrinterOptions? options = null,
        IEnumerable<string>? reservedScopeNames = null,
        StackSlotUnifierTelemetryBuilder? stackSlotTelemetry = null)
    {
        _function = function;
        _options = options ?? PrinterOptions.Default;
        _newMemorySafetyRules = function.UsesUpdatedMemorySafetyRules;
        _skipLocalsInit = function.SkipLocalsInit;
        _reservedScopeNames = reservedScopeNames is null
            ? []
            : new HashSet<string>(reservedScopeNames, StringComparer.Ordinal);
        _stackSlotTelemetry = stackSlotTelemetry;
    }

    // The output-path pass context: stepping off, plus the optional cross-method
    // import seam so a pass can reach a sibling body (lambda raising).
    static PassContext RaiseContext(Func<MethodRef, IrFunction?>? importMethodBody)
        => importMethodBody is null
            ? PassContext.None
            : new PassContext(new Stepper(enabled: false), importMethodBody: importMethodBody);

    /// <summary>The product path: runs the default raising passes, then prints. <see cref="Print"/> alone renders whatever tree it is given — right for stage dumps, wrong for output paths.</summary>
    public static DecompilerResult PrintRaised(IrFunction function)
        => PrintRaised(function, importMethodBody: null);

    /// <summary>As <see cref="PrintRaised(IrFunction)"/>, with <paramref name="importMethodBody"/> wiring the cross-method import seam (e.g. for lambda raising); null leaves cross-method passes as no-ops. <paramref name="options"/> defaults to the shipped output.</summary>
    public static DecompilerResult PrintRaised(IrFunction function, Func<MethodRef, IrFunction?>? importMethodBody, PrinterOptions? options = null)
    {
        try
        {
            IrPasses.Run(function, IrPasses.Default, RaiseContext(importMethodBody));
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return Print(function, options);
    }

    /// <summary>
    /// The product path with a statement line map: same output as
    /// <see cref="PrintRaised(IrFunction)"/>, plus a table from each top-level
    /// statement node to its 0-based start line. Line-anchored overlays (the
    /// annotated C# view) splice onto those lines; the printer itself stays
    /// annotation-agnostic. The map is empty on failure.
    /// </summary>
    public static DecompilerResult PrintRaised(IrFunction function, out IReadOnlyDictionary<IrNode, int> statementLines)
        => PrintRaised(function, out statementLines, importMethodBody: null);

    /// <inheritdoc cref="PrintRaised(IrFunction, out IReadOnlyDictionary{IrNode, int})"/>
    public static DecompilerResult PrintRaised(
        IrFunction function, out IReadOnlyDictionary<IrNode, int> statementLines, Func<MethodRef, IrFunction?>? importMethodBody)
    {
        statementLines = new Dictionary<IrNode, int>();
        try
        {
            IrPasses.Run(function, IrPasses.Default, RaiseContext(importMethodBody));
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var sink = new Dictionary<IrNode, int>();
            var printer = new CSharpPrinter(function) { _statementLines = sink };
            string output = printer.PrintBody(function);
            statementLines = sink;
            return printer.Result(output, function);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the <see cref="IrPasses.Lowered"/> pipeline (the default minus the
    /// cosmetic statement-sugar passes), then prints — the lowered-C# view
    /// (issue #636). Like <see cref="PrintRaised"/> this is an output path, so it
    /// owns the pass run; the result is valid, recompilable C# at a lower
    /// altitude than the shipped output.
    /// </summary>
    public static DecompilerResult PrintLowered(IrFunction function)
        => PrintLowered(function, importMethodBody: null);

    /// <summary>As <see cref="PrintLowered(IrFunction)"/>, with <paramref name="importMethodBody"/> wiring the cross-method import seam for non-cosmetic lowered passes such as lambda, local-function, and iterator reconstruction.</summary>
    public static DecompilerResult PrintLowered(IrFunction function, Func<MethodRef, IrFunction?>? importMethodBody)
    {
        try
        {
            IrPasses.Run(function, IrPasses.Lowered, RaiseContext(importMethodBody));
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return Print(function);
    }

    /// <summary>
    /// As <see cref="PrintLowered(IrFunction)"/>, but also yields the
    /// statement-to-output-line table the mixed-source view uses to anchor fact
    /// comments and interleaved IL onto the lowered C# (the lowered analogue of
    /// <see cref="PrintRaised(IrFunction, out IReadOnlyDictionary{IrNode, int})"/>).
    /// </summary>
    public static DecompilerResult PrintLowered(IrFunction function, out IReadOnlyDictionary<IrNode, int> statementLines)
        => PrintLowered(function, out statementLines, importMethodBody: null);

    /// <inheritdoc cref="PrintLowered(IrFunction, out IReadOnlyDictionary{IrNode, int})"/>
    public static DecompilerResult PrintLowered(
        IrFunction function, out IReadOnlyDictionary<IrNode, int> statementLines, Func<MethodRef, IrFunction?>? importMethodBody)
    {
        statementLines = new Dictionary<IrNode, int>();
        try
        {
            IrPasses.Run(function, IrPasses.Lowered, RaiseContext(importMethodBody));
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var sink = new Dictionary<IrNode, int>();
            var printer = new CSharpPrinter(function) { _statementLines = sink };
            string output = printer.PrintBody(function);
            statementLines = sink;
            return printer.Result(output, function);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the print analysis on an already-raised tree purely to capture the
    /// definite-assignment dataflow facts — the per-block <c>in</c>/<c>out</c>
    /// sets that decide which locals keep <c>= default</c>. The same walk that
    /// produces the output fills the sink, so the facts are the shipped
    /// analysis, not a parallel model. The rendered C# is discarded.
    /// </summary>
    public static DataflowFacts CollectDataflowFacts(IrFunction function)
    {
        var facts = new DataflowFacts();
        var printer = new CSharpPrinter(function) { _facts = facts };
        printer.PrintBody(function);
        return facts;
    }

    public static DecompilerResult Print(IrFunction function, PrinterOptions? options = null)
    {
        try
        {
            var printer = new CSharpPrinter(function, options);
            string output = printer.PrintBody(function);
            return printer.Result(output, function);
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    DecompilerResult Result(string output, IrFunction function)
        => new(output, function.Fidelity, [.. function.Diagnostics])
        {
            ConstructorChain = _constructorChain,
            FieldInitializers = _fieldInitializers,
            RequiresAsyncBodyModifier = function.RequiresAsyncBodyModifier,
            RequiresUnsafeBodyModifier = function.Descendants.Prepend(function).Any(NeedsUnsafeContext),
            ContainsAwaitExpression = function.Descendants.OfType<AwaitExpression>().Any(),
            Metadata = new DecompilerResultMetadata(EffectiveDecompilerOptions(), [.. _decisions]),
        };

    DecompilerOptions EffectiveDecompilerOptions()
        => new()
        {
            ReadableLocalNames = _options.ReadableLocalNames,
            PreferFrameworkTypeImports = true,
        };

    void AddDecision(string ruleId, string category, string subject, string detail, string? oldValue = null, string? newValue = null)
    {
        string key = $"{ruleId}\0{category}\0{subject}\0{detail}\0{oldValue}\0{newValue}";
        if (_decisionKeys.Add(key))
        {
            _decisions.Add(new DecompilerDecision(ruleId, category, subject, detail)
            {
                OldValue = oldValue,
                NewValue = newValue,
            });
        }
    }

    /// <summary>Stores that double as declarations: the local's first program-order reference, at statement level in the entry block.</summary>
    readonly HashSet<IrNode> _declaringStores = [];

    /// <summary>Locals that may be read before they are definitely assigned, so their declaration must keep its `= default` zero-initializer (a bare declaration would be CS0165).</summary>
    HashSet<int> _readBeforeAssign = [];

    /// <summary>Optional sink for the definite-assignment dataflow facts; null on the shipped print path (the analysis records nothing then).</summary>
    DataflowFacts? _facts;

    /// <summary>Offsets some surviving goto targets — labels print wherever the block lives, top-level or inside a flat EH body.</summary>
    HashSet<int> _labelTargets = [];

    readonly HashSet<int> _emittedLabels = [];

    readonly Dictionary<SwitchBranch, string> _switchTemps = [];

    /// <summary>
    /// True while rendering inside an emitted <c>checked(...)</c> expression. A
    /// checked operation (overflow binary or conversion) nested in this context
    /// needs no <c>checked</c> wrapper of its own — the enclosing one already
    /// establishes the overflow context, and Roslyn keeps the same <c>.ovf</c>
    /// opcodes — so the printer collapses <c>checked((byte)(checked(a + b)))</c>
    /// to <c>checked((byte)(a + b))</c>. Saved/restored around each checked node.
    /// </summary>
    bool _checkedContext;

    /// <summary>An explicit base/this chain call lifted out of a constructor body to its signature initializer (base/this calls are invalid as body statements).</summary>
    string? _constructorChain;
    IrNode? _chainStatement;

    /// <summary>Field initializers (<c>this.f = value</c> stores preceding the base call) lifted out of a constructor body to the field declarations, keyed in source order.</summary>
    readonly List<(string Field, string Value)> _fieldInitializers = [];
    readonly HashSet<IrNode> _fieldInitStores = [];

    /// <summary>Pinned local slots a <see cref="Fixed"/> statement owns: declared by the fixed header (skipped up front) and read as a pointer of the fixed's element type.</summary>
    readonly HashSet<int> _fixedLocals = [];

    /// <summary>Synthesized stack-slot names a <see cref="Fixed"/> statement owns and declares in its header.</summary>
    readonly HashSet<string> _fixedStackSlotNames = [];

    /// <summary>Resource local slots a <see cref="UsingStatement"/> owns: declared by the using header, not up front.</summary>
    readonly HashSet<int> _usingLocals = [];

    /// <summary>Iteration variable local slots declared by a <see cref="ForeachStatement"/> header.</summary>
    readonly HashSet<int> _foreachLocals = [];

    /// <summary>Pattern variable slots bound by pattern expressions: declared by the pattern, not up front.</summary>
    readonly HashSet<int> _isPatternLocals = [];

    /// <summary>Local slots declared by a tuple deconstruction header.</summary>
    readonly HashSet<int> _deconstructionLocals = [];

    /// <summary>Ref-struct locals whose hoisted declaration must spell <c>scoped</c>: a <c>stackalloc</c>-initialized span whose declaration was split from its assignment (out of the unsafe block) would otherwise warn CS9081. A stackalloc result is always scoped, so this is faithful, not a guess.</summary>
    readonly HashSet<int> _scopedLocals = [];

    /// <summary>Optional sink mapping each printed top-level statement node to its 0-based start line in the output; null on the shipped print path. Drives line-anchored overlays (annotated views) without the printer knowing what they are.</summary>
    Dictionary<IrNode, int>? _statementLines;

    readonly record struct StackSlotRenderKey(int Slot, string TypeKey);

    internal sealed record StackSlotUnifierTelemetry(
        int StoreNodes,
        int LoadNodes,
        int DistinctSlots,
        int CandidateSlots,
        int SingleCandidateSlots,
        int MultiCandidateUnifiedSlots,
        int UnunifiedSplitSlots,
        int EmittedDeclarationNames);

    readonly Dictionary<StackSlotRenderKey, string> _stackSlotNames = [];
    readonly Dictionary<StoreStackSlot, TypeRef?> _stackSlotStoreTypes = [];
    readonly Dictionary<int, TypeRef> _stackSlotUnifiedTypes = [];
    readonly SortedDictionary<(int Slot, int Ordinal), (string Name, TypeRef? Type)> _stackSlotDeclarations = [];
    readonly StackSlotUnifierTelemetryBuilder? _stackSlotTelemetry;
    readonly Dictionary<StoreElement, StoreLocal> _inlineReceiverTempStores = [];
    readonly HashSet<int> _inlineReceiverTempLocals = [];

    internal static StackSlotUnifierTelemetry CollectStackSlotUnifierTelemetry(IrFunction function)
    {
        var telemetry = new StackSlotUnifierTelemetryBuilder();
        var printer = new CSharpPrinter(function, stackSlotTelemetry: telemetry);
        _ = printer.PrintBody(function);
        return telemetry.ToTelemetry();
    }

    string PrintBody(IrFunction function)
    {
        var sb = new StringBuilder();
        _labelTargets = CollectBranchTargets(function);
        foreach (var usingNode in DescendantsOutsideNestedFunctions(function).OfType<UsingStatement>())
            _usingLocals.Add(usingNode.LocalIndex);
        foreach (var foreachNode in DescendantsOutsideNestedFunctions(function).OfType<ForeachStatement>())
            _foreachLocals.Add(foreachNode.LocalIndex);
        foreach (var pattern in DescendantsOutsideNestedFunctions(function).OfType<IsPattern>())
            _isPatternLocals.Add(pattern.LocalIndex);
        foreach (var pattern in DescendantsOutsideNestedFunctions(function).OfType<RecursivePropertyDeclarationPattern>())
            _isPatternLocals.Add(pattern.LocalIndex);
        foreach (var arm in DescendantsOutsideNestedFunctions(function).OfType<UnionSwitchExpressionArm>())
            if (arm.LocalIndex is { } localIndex)
                _isPatternLocals.Add(localIndex);
        foreach (var deconstruction in DescendantsOutsideNestedFunctions(function).OfType<DeconstructionAssignment>())
            foreach (var target in deconstruction.Targets)
                if (target is { Kind: DeconstructionTargetKind.Local, IsDeclared: true })
                    _deconstructionLocals.Add(target.LocalIndex);
        CollectDeclaringStores(function);
        CollectInlineReceiverTempStores(function);
        CollectStackSlotNames(function);
        foreach (var fixedNode in DescendantsOutsideNestedFunctions(function).OfType<Fixed>())
        {
            if (fixedNode.LocalIsStackSlot)
                _fixedStackSlotNames.Add(FixedLocalName(fixedNode));
            else
                _fixedLocals.Add(fixedNode.LocalIndex);
        }
        _readBeforeAssign = DefiniteAssignment.Compute(function, _labelTargets, _facts);
        if (_facts is not null)
            _facts.LocalNames = [.. Enumerable.Range(0, function.Locals.Length).Select(LocalName)];

        // A constructor prologue is leading field-initializer stores
        // (this.f = value) followed by the base(...)/this(...) chain call. C#
        // emits field initializers before the base call, so a this-field store
        // preceding the chain call is a field initializer — not a body
        // assignment — and the chain call is invalid as a body statement
        // (CS0175). Lift both out of the body so they render where they belong:
        // the initializers on the field declarations, the chain on the
        // signature.
        if (function.Body.Blocks is [{ } entry, ..]
            && ChainCallIndex(entry) is { } chainIndex
            && entry.Children.Take(chainIndex).All(IsFieldInitializerStore))
        {
            foreach (var store in entry.Children.Take(chainIndex).Cast<StoreField>())
            {
                _fieldInitStores.Add(store);
                _fieldInitializers.Add((store.Field.Name, Expression(store.Value)));
            }

            var chainCall = (Call)((ExpressionStatement)entry.Children[chainIndex]).Expression;
            if (ConstructorChainText(chainCall.Callee, chainCall) is { } chain)
            {
                _constructorChain = chain.TrimEnd(';');
                _chainStatement = entry.Children[chainIndex];
            }
        }

        // Remaining locals and slots declare up front, current-style.
        foreach (var declaration in CollectDeclarations(function))
            sb.AppendLine(declaration);
        if (sb.Length > 0)
            sb.AppendLine();

        AppendContainer(sb, function.Body, 0, topLevel: true);
        if (NeedsUnsupportedFallbackReturn(function))
            sb.AppendLine("return default;");
        return sb.ToString().TrimEnd() is { Length: > 0 } text ? text + Environment.NewLine : "";
    }

    static bool NeedsUnsupportedFallbackReturn(IrFunction function)
        => NeedsUnsupportedFallbackReturn(function.Signature.ReturnType, function.RequiresAsyncBodyModifier, function);

    static bool AsyncReturnForbidsValue(IrFunction function)
        => AsyncReturnForbidsValue(function.Signature.ReturnType, function.RequiresAsyncBodyModifier);

    static bool NeedsUnsupportedFallbackReturn(TypeRef returnType, bool requiresAsyncBodyModifier, IrNode bodyRoot)
        => returnType is not { Namespace: "System", Name: "Void" }
            && returnType.Kind != TypeRefKind.ByRef
            && !AsyncReturnForbidsValue(returnType, requiresAsyncBodyModifier)
            && !DescendantsOutsideNestedFunctions(bodyRoot).Any(static n => n is YieldReturn or YieldBreak)
            && DescendantsOutsideNestedFunctions(bodyRoot).Any(static n => n is UnsupportedNode)
            && !DescendantsOutsideNestedFunctions(bodyRoot).Any(static n => n is Return);

    static bool AsyncReturnForbidsValue(TypeRef type, bool requiresAsyncBodyModifier)
    {
        if (!requiresAsyncBodyModifier)
            return false;

        if (type is { Kind: TypeRefKind.Definition, Namespace: "System.Threading.Tasks", Name: "Task" or "ValueTask" })
            return true;
        if (type is { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System.Collections.Generic", Name: "IAsyncEnumerable`1" or "IAsyncEnumerator`1" } })
            return true;

        return false;
    }

    void AppendContainer(StringBuilder sb, BlockContainer container, int indent, bool topLevel = false)
    {
        string pad = new(' ', indent * 4);
        var blocks = container.Blocks;
        // A label binds to the next statement, even one in a following block, so
        // an empty labeled block is fine mid-container. It only strands when the
        // container ends with no statement after the label; track that and emit a
        // labeled empty statement (';') to keep the C# valid. A comment-only
        // render (an `// endfinally` marker, an unsupported `/* … */` node) is not
        // a statement, so it does not satisfy a pending label either.
        bool labelPendingStatement = false;
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (_labelTargets.Contains(block.StartOffset))
            {
                AppendLabel(sb, pad, block.StartOffset);
                labelPendingStatement = true;
            }
            // The trailing 'return;' trims, current-style — unless it is a
            // labeled block's only statement, where trimming would strand
            // the label as invalid C#.
            bool labeledReturnOnly = _labelTargets.Contains(block.StartOffset) && block.Children.Count == 1;
            var emit = new List<IrNode>();
            foreach (var statement in block.Children)
            {
                if (ReferenceEquals(statement, _chainStatement) || _fieldInitStores.Contains(statement))
                    continue;   // lifted to the signature initializer / field declarations
                bool isLast = topLevel && i == blocks.Count - 1 && ReferenceEquals(statement, block.Children[^1]);
                if (isLast && !labeledReturnOnly && statement is Return { Value: null })
                    break;
                emit.Add(statement);
            }

            if (emit.Any(n => !RendersAsCommentOnly(n)))
                labelPendingStatement = false;
            AppendStatements(sb, emit, indent);
        }
        if (labelPendingStatement)
            sb.Append(pad).AppendLine(";");
    }

    void AppendLabel(StringBuilder sb, string pad, int offset)
    {
        // First printed occurrence owns the label; structured replacements stamp
        // the enclosing statement so it must render before any same-offset child.
        if (_emittedLabels.Add(offset))
            sb.Append(pad).AppendLine($"IL_{offset:X4}:");
    }

    /// <summary>
    /// A statement node that renders only as a comment — an <c>// endfinally</c>/
    /// <c>// endfilter</c> EH marker or an unsupported <c>/* … */</c> node — so it
    /// is not a real C# statement. A label sitting before one stays unsatisfied:
    /// the trailing empty statement (';') must still follow to keep a labeled
    /// region legal (a label requires a statement; a bare label before a closing
    /// brace is <c>CS1525</c>).
    /// </summary>
    static bool RendersAsCommentOnly(IrNode node) => node switch
    {
        EndFinally or EndFilter => true,
        UnsupportedNode => true,
        ExpressionStatement { Expression: UnsupportedNode } => true,
        _ => false,
    };

    static HashSet<int> CollectBranchTargets(IrFunction function)
    {
        var targets = new HashSet<int>();
        foreach (var node in DescendantsOutsideNestedFunctions(function))
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
        // Catch variables declare in their clause header, not up front.
        var clauseDeclared = function.Descendants.OfType<CatchClause>()
            .Where(clause => clause.VariableIndex is not null)
            .Select(clause => clause.VariableIndex!.Value)
            .ToHashSet();
        foreach (var node in DescendantsOutsideNestedFunctions(function))
        {
            switch (node)
            {
                case LoadLocal l: locals.Add(l.Index); break;
                case StoreLocal s: locals.Add(s.Index); break;
                case LoadLocalAddress a: locals.Add(a.Index); break;
                case NullCoalescingAssignment n: locals.Add(n.LocalIndex); break;
                case ForeachStatement f: locals.Add(f.LocalIndex); break;
                case DeconstructionAssignment d:
                    foreach (var target in d.Targets)
                        if (target.Kind == DeconstructionTargetKind.Local)
                            locals.Add(target.LocalIndex);
                    break;
            }
        }
        int switchIndex = 0;
        foreach (var switchBranch in DescendantsOutsideNestedFunctions(function).OfType<SwitchBranch>())
        {
            string name = ReserveName($"__dotnet_inspect_switch{switchIndex++}", new HashSet<string>(CurrentScopeNames(), StringComparer.Ordinal));
            _switchTemps.TryAdd(switchBranch, name);
            yield return $"int {name} = default;";
        }
        foreach (int index in locals)
        {
            // Fixed/using headers and `is T t` patterns declare their owned
            // locals, not the up-front declaration block.
            if (_fixedLocals.Contains(index) || _usingLocals.Contains(index) || _foreachLocals.Contains(index)
                || _isPatternLocals.Contains(index) || _deconstructionLocals.Contains(index)
                || _inlineReceiverTempLocals.Contains(index))
                continue;
            bool declaredAtStore = _declaringStores.Any(s =>
                s is StoreLocal store && store.Index == index
                || s is InitObject { Address: LoadLocalAddress init } && init.Index == index);
            if (!declaredAtStore && !clauseDeclared.Contains(index))
            {
                // An up-front local is referenced before a defining store, so
                // it relies on IL's zero-initialization of locals (localsinit).
                // Spell that as `= default` — both faithful and what C#'s
                // definite-assignment requires (a bare declaration is CS0165 on
                // any path that reads before assigning). When the local is
                // instead provably assigned on every path before each read, the
                // `= default` is a dead store the IL never had (it leans on
                // localsinit), so drop it and declare bare. A ref local takes
                // neither: `= default` is illegal and a bare declaration is
                // CS8174. IL zero-initializes a managed pointer to a null
                // reference, whose faithful C# spelling is Unsafe.NullRef<T>().
                // Fully qualified so the per-member view compiles without a
                // using; the whole-type hoister shortens it and adds the using.
                var type = function.Locals[index];
                string scoped = _scopedLocals.Contains(index) ? "scoped " : "";
                yield return type.Kind == TypeRefKind.ByRef
                    ? $"{TypeText(type)} {LocalName(index)} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
                    : _readBeforeAssign.Contains(index)
                        ? $"{scoped}{TypeText(type)} {LocalName(index)} = default;"
                        : $"{scoped}{TypeText(type)} {LocalName(index)};";
            }
        }
        foreach (var ((_, _), (name, type)) in _stackSlotDeclarations)
        {
            if (_fixedStackSlotNames.Contains(name))
                continue;
            if (_declaringStores.OfType<StoreStackSlot>().Any(s => StackSlotName(s) == name))
                continue;
            // A ref-typed slot, like a ref-typed local, can't be declared bare
            // (CS8174); spell IL's null-reference zero-init as Unsafe.NullRef<T>().
            yield return type is { Kind: TypeRefKind.ByRef }
                ? $"{TypeText(type)} {name} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
                : $"{(type is null ? "var" : TypeText(type))} {name};";
        }
    }

    void CollectStackSlotNames(IrFunction function)
    {
        _stackSlotNames.Clear();
        _stackSlotStoreTypes.Clear();
        _stackSlotUnifiedTypes.Clear();
        _stackSlotDeclarations.Clear();

        var nodes = DescendantsOutsideNestedFunctions(function).ToList();
        var storesBySlot = new Dictionary<int, List<IrExpression>>();
        var loadsBySlot = new Dictionary<int, List<LoadStackSlot>>();
        var extraLoadTargetsBySlot = new Dictionary<int, List<TypeRef>>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case StoreStackSlot store:
                    (storesBySlot.TryGetValue(store.Slot, out var stores) ? stores : storesBySlot[store.Slot] = []).Add(store.Value);
                    break;
                case LoadStackSlot load:
                    (loadsBySlot.TryGetValue(load.Slot, out var loads) ? loads : loadsBySlot[load.Slot] = []).Add(load);
                    break;
            }
        }
        _stackSlotTelemetry?.RecordNodes(
            storesBySlot.Values.Sum(stores => stores.Count),
            loadsBySlot.Values.Sum(loads => loads.Count));

        foreach (var storeElement in nodes.OfType<StoreElement>())
        {
            if (storeElement is not { Value: LoadStackSlot load, ElementType: { } elementType })
                continue;
            if (!storesBySlot.TryGetValue(load.Slot, out var stores)
                || stores.Count == 0
                || !stores.All(store => store is Conditional conditional && CanRenderConditionalForTarget(conditional, elementType)))
            {
                continue;
            }
            (extraLoadTargetsBySlot.TryGetValue(load.Slot, out var targets) ? targets : extraLoadTargetsBySlot[load.Slot] = []).Add(elementType);
        }

        foreach (int slot in storesBySlot.Keys.Concat(loadsBySlot.Keys).Distinct())
        {
            if (_stackSlotTelemetry is { } telemetry)
            {
                telemetry.RecordCandidate(
                    CandidateCount(
                        storesBySlot.GetValueOrDefault(slot) ?? [],
                        loadsBySlot.GetValueOrDefault(slot) ?? [],
                        extraLoadTargetsBySlot.GetValueOrDefault(slot) ?? []));
            }
            if (TryChooseUnifiedStackSlotType(
                storesBySlot.GetValueOrDefault(slot) ?? [],
                loadsBySlot.GetValueOrDefault(slot) ?? [],
                extraLoadTargetsBySlot.GetValueOrDefault(slot) ?? [],
                out var unifiedType))
            {
                _stackSlotUnifiedTypes[slot] = unifiedType;
                if (_stackSlotTelemetry is { } telemetryAfter)
                    telemetryAfter.RecordUnified(unifiedType);
            }
            else if (_stackSlotTelemetry is { } telemetryAfter)
            {
                telemetryAfter.RecordUnunifiedSplit();
            }
        }

        foreach (var store in nodes.OfType<StoreStackSlot>())
            _stackSlotStoreTypes[store] = StackSlotRenderType(store.Slot, store.Value.ResultType);

        var ordinals = new Dictionary<int, int>();
        var takenNames = CurrentReservedNames(includeLocals: true);

        string NameFor(int slot, TypeRef? type)
        {
            var key = new StackSlotRenderKey(slot, StackSlotTypeKey(type));
            if (_stackSlotNames.TryGetValue(key, out var existing))
                return existing;

            int ordinal = ordinals.GetValueOrDefault(slot);
            ordinals[slot] = ordinal + 1;
            string baseName = ordinal == 0 ? $"S_{slot}" : $"S_{slot}_{ordinal}";
            string name = ReserveName(baseName, takenNames);
            _stackSlotNames[key] = name;
            _stackSlotDeclarations[(slot, ordinal)] = (name, type);
            return name;
        }

        foreach (var node in nodes)
        {
            switch (node)
            {
                case LoadStackSlot load:
                    NameFor(load.Slot, StackSlotRenderType(load.Slot, load.Type));
                    break;
                case StoreStackSlot store:
                    NameFor(store.Slot, StackSlotTargetType(store));
                    break;
            }
        }
        _stackSlotTelemetry?.RecordEmittedDeclarations(EmittedStackSlotDeclarationCount());

        static int CandidateCount(
            IReadOnlyList<IrExpression> stores,
            IReadOnlyList<LoadStackSlot> loads,
            IReadOnlyList<TypeRef> extraLoadTargets)
            => loads.Select(load => load.Type)
                .Concat(extraLoadTargets)
                .Concat(stores.Select(store => store.ResultType))
                .Where(type => type is not null)
                .Cast<TypeRef>()
                .Distinct()
                .Count();
    }

    int EmittedStackSlotDeclarationCount()
    {
        int count = 0;
        foreach (var (_, (name, _)) in _stackSlotDeclarations)
        {
            if (_declaringStores.OfType<StoreStackSlot>().Any(s => StackSlotName(s) == name))
                continue;
            count++;
        }
        return count;
    }

    sealed class StackSlotUnifierTelemetryBuilder
    {
        int _lastCandidateCount;

        public int StoreNodes { get; private set; }
        public int LoadNodes { get; private set; }
        public int CandidateSlots { get; private set; }
        public int SingleCandidateSlots { get; private set; }
        public int MultiCandidateUnifiedSlots { get; private set; }
        public int UnunifiedSplitSlots { get; private set; }
        public int EmittedDeclarationNames { get; private set; }

        public void RecordNodes(int stores, int loads)
        {
            StoreNodes += stores;
            LoadNodes += loads;
        }

        public void RecordCandidate(int candidateCount)
        {
            CandidateSlots++;
            _lastCandidateCount = candidateCount;
            if (candidateCount == 1)
                SingleCandidateSlots++;
        }

        public void RecordUnified(TypeRef _)
        {
            if (_lastCandidateCount > 1)
                MultiCandidateUnifiedSlots++;
        }

        public void RecordUnunifiedSplit() => UnunifiedSplitSlots++;

        public void RecordEmittedDeclarations(int count) => EmittedDeclarationNames += count;

        public StackSlotUnifierTelemetry ToTelemetry()
            => new(
                StoreNodes,
                LoadNodes,
                DistinctSlots: CandidateSlots,
                CandidateSlots,
                SingleCandidateSlots,
                MultiCandidateUnifiedSlots,
                UnunifiedSplitSlots,
                EmittedDeclarationNames);
    }

    bool TryChooseUnifiedStackSlotType(
        IReadOnlyList<IrExpression> stores,
        IReadOnlyList<LoadStackSlot> loads,
        IReadOnlyList<TypeRef> extraLoadTargets,
        out TypeRef unifiedType)
    {
        var candidates = loads.Select(load => load.Type)
            .Concat(extraLoadTargets)
            .Concat(stores.Select(store => store.ResultType))
            .Where(type => type is not null)
            .Cast<TypeRef>()
            .Distinct()
            .ToList();
        foreach (var candidate in candidates)
        {
            if (stores.All(store => CanAssignTo(store, candidate))
                && loads.All(load => CanLoadAsType(candidate, load))
                && extraLoadTargets.All(target => CanAssignType(candidate, target) && !StrictlyNarrowsReference(candidate, target)))
            {
                unifiedType = candidate;
                return true;
            }
        }

        unifiedType = TypeRef.CoreLib("System", "Object");
        return false;
    }

    /// <summary>True when naming the slot <paramref name="candidate"/> would give a load site a narrower reference type than its own (e.g. picking <c>string</c> for an <c>object</c> load), which can silently rebind an overloaded call. Equal or wider candidates are fine.</summary>
    bool StrictlyNarrowsReference(TypeRef candidate, TypeRef load)
        => IsReferenceLike(candidate)
            && !candidate.Equals(load)
            && CanAssignType(candidate, load)
            && !CanAssignType(load, candidate);

    bool CanLoadAsType(TypeRef source, LoadStackSlot load)
    {
        if (load.Type is null)
            return true;
        if (CanAssignType(source, load.Type))
            return !StrictlyNarrowsReference(source, load.Type);
        return TypeFamilies.IsBoolean(source)
            && TypeFamilies.IsIntegerLike(load.Type)
            && StackSlotLoadTargetType(load) is { } target
            && TypeFamilies.IsBoolean(target);
    }

    TypeRef? StackSlotLoadTargetType(LoadStackSlot load)
        => load.Parent switch
        {
            StoreLocal store when ReferenceEquals(store.Value, load) => store.Type,
            StoreArgument store when ReferenceEquals(store.Value, load) => store.Type,
            StoreField store when ReferenceEquals(store.Value, load) => store.Field.Type,
            StoreProperty store when ReferenceEquals(store.Value, load) => StorePropertyTargetType(store),
            StoreElement store when ReferenceEquals(store.Value, load) => store.ElementType,
            StoreIndirect store when ReferenceEquals(store.Value, load) => store.Type,
            Return ret when ReferenceEquals(ret.Value, load) => _function.Signature.ReturnType,
            // A bool-in-int-slot load whose value flows into a boolean operator
            // or condition position (`!S`, `S && x`, `S ? a : b`, `if (S)`,
            // `while (S)`) has a boolean target — C# requires bool there. Without
            // this the slot's bool store and this load get different names (S_1
            // bool vs S_1_1 int) and the consumer reads an unassigned int split
            // (CS0165, #2377).
            LogicalNot not when ReferenceEquals(not.Operand, load) => TypeRef.CoreLib("System", "Boolean"),
            LogicalBinary logical when ReferenceEquals(logical.Left, load) || ReferenceEquals(logical.Right, load) => TypeRef.CoreLib("System", "Boolean"),
            Conditional conditional when ReferenceEquals(conditional.Condition, load) => TypeRef.CoreLib("System", "Boolean"),
            ConditionalBranch branch when ReferenceEquals(branch.Condition, load) => TypeRef.CoreLib("System", "Boolean"),
            IfStatement ifStatement when ReferenceEquals(ifStatement.Condition, load) => TypeRef.CoreLib("System", "Boolean"),
            WhileLoop whileLoop when ReferenceEquals(whileLoop.Condition, load) => TypeRef.CoreLib("System", "Boolean"),
            DoWhileLoop doWhile when ReferenceEquals(doWhile.Condition, load) => TypeRef.CoreLib("System", "Boolean"),
            ForLoop forLoop when ReferenceEquals(forLoop.Condition, load) => TypeRef.CoreLib("System", "Boolean"),
            _ => null,
        };

    bool CanAssignTo(IrExpression value, TypeRef target)
    {
        if (value is Constant { Value: null })
            return IsReferenceLike(target);
        if (value is Conditional conditional)
            // A conditional unifies to a target its arms each satisfy — e.g.
            // `cond ? null : value` (null + string) is assignable to `string`,
            // even though its IL-merged ResultType widened to `object`. Without
            // this the slot's object-typed store and string-typed load get
            // different names (S_1 vs S_1_1) and the consumer reads an unassigned
            // local (#1767). Restricted to reference-like targets: a non-reference
            // target (char/numeric) needs per-arm target rendering that the
            // conditional printer only does for immediate constant arms, so a
            // nested conditional would unify to `char` yet render an `int` ternary
            // (CS0266). The char/enum arm-cast path and the merged-ResultType
            // fallback remain.
            return CanRenderConditionalForTarget(conditional, target)
                || (IsProvenReference(target)
                    && CanAssignTo(conditional.WhenTrue, target)
                    && CanAssignTo(conditional.WhenFalse, target))
                || (conditional.ResultType is { } condType && CanAssignType(condType, target));
        if (value is Coalesce coalesce)
            return CanRenderCoalesceForTarget(coalesce, target)
                || (coalesce.ResultType is { } coalesceType
                    && CanAssignType(coalesceType, target)
                    && !IsReferenceLike(coalesceType));
        if (value is Constant { Value: int or long } constant
            && target.DeclaredValueTypeHint == ValueTypeHint.ValueType
            && CoercionRendering.CanSpellUnknownEnumConstant(constant.ResultType, target, _function.TypeShapes))
            return true;
        return value.ResultType is { } source && CanAssignType(source, target);
    }

    bool CanRenderCoalesceForTarget(Coalesce coalesce, TypeRef target)
    {
        if (!IsProvenReference(target))
            return false;

        bool leftNull = coalesce.Left is Constant { Value: null };
        bool rightNull = coalesce.Right is Constant { Value: null };
        var leftType = EffectiveType(coalesce.Left);
        var rightType = EffectiveType(coalesce.Right);

        if (leftNull && rightNull)
            return false;
        if (leftNull)
            return rightType is { } right && IsProvenReference(right) && CanAssignType(right, target);
        if (rightNull)
            return leftType is { } left && IsProvenReference(left) && CanAssignType(left, target);
        if (leftType is not { } leftNonNull || rightType is not { } rightNonNull)
            return false;
        if (!IsProvenReference(leftNonNull) || !IsProvenReference(rightNonNull))
            return false;
        if (CanAssignType(rightNonNull, leftNonNull))
            return CanAssignType(leftNonNull, target);
        if (CanAssignType(leftNonNull, rightNonNull))
            return CanAssignType(rightNonNull, target);
        return false;
    }

    /// <summary>
    /// A type known to be a reference WITHOUT resolution — a stack-O family
    /// (object/string/array), a signature-declared class, or a same-assembly
    /// reference shape. Unlike <see cref="IsReferenceLike"/> this excludes the
    /// optimistic "a bare cross-assembly definition is probably a class" fallback:
    /// narrowing a slot to an UNPROVEN target would print `MaybeStruct S = a ? null
    /// : value`, which is CS0037 if the type resolves to a struct. Used to gate the
    /// conditional arm-assignability unification so it only narrows to a target a
    /// null arm is provably assignable to.
    /// </summary>
    bool IsProvenReference(TypeRef type)
        => type.Kind is not (TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.FunctionPointer)
            && (TypeFamilies.Of(type) == StackFamily.O
                || type.DeclaredValueTypeHint == ValueTypeHint.ReferenceType
                || _function.TypeShapes.GetValueOrDefault(type) == TypeShape.Reference);

    bool CanAssignType(TypeRef source, TypeRef target)
    {
        if (source.Equals(target))
            return true;
        if (CSharpConversionRules.IsImplicitNumericAssignment(source, target))
            return true;
        if (IsCoreObject(target) && IsReferenceLike(source))
            return true;
        return false;
    }

    bool IsReferenceLike(TypeRef type)
    {
        if (type.Kind is TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.FunctionPointer)
            return false;
        if (TypeFamilies.Of(type) == StackFamily.O)
            return true;
        if (type.DeclaredValueTypeHint == ValueTypeHint.ReferenceType)
            return true;
        if (_function.TypeShapes.GetValueOrDefault(type) == TypeShape.Reference)
            return true;
        return type.Kind is TypeRefKind.Definition or TypeRefKind.GenericInstance
            && type.DeclaredValueTypeHint != ValueTypeHint.ValueType
            && _function.TypeShapes.GetValueOrDefault(type) is not (TypeShape.ValueType or TypeShape.Enum)
            && !TypeFamilies.IsNumericPrimitive(type);
    }

    bool IsKnownReferenceLike(TypeRef type)
    {
        if (type.Kind is TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.FunctionPointer)
            return false;
        if (TypeFamilies.Of(type) == StackFamily.O)
            return true;
        if (type.DeclaredValueTypeHint == ValueTypeHint.ReferenceType)
            return true;
        if (_function.TypeShapes.GetValueOrDefault(type) == TypeShape.Reference)
            return true;
        return type.Kind is TypeRefKind.SzArray or TypeRefKind.Array;
    }

    static bool IsCoreObject(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Object" };

    TypeRef? StackSlotRenderType(int slot, TypeRef? type)
        => _stackSlotUnifiedTypes.TryGetValue(slot, out var unifiedType) ? unifiedType : type;

    static string StackSlotTypeKey(TypeRef? type) => type?.ToDisplayString() ?? "<unknown>";

    TypeRef? StackSlotTargetType(StoreStackSlot store)
        => _stackSlotStoreTypes.TryGetValue(store, out var type) ? type : store.Value.ResultType;

    string StackSlotName(LoadStackSlot load)
        => _stackSlotNames.TryGetValue(new StackSlotRenderKey(load.Slot, StackSlotTypeKey(StackSlotRenderType(load.Slot, load.Type))), out var name)
            ? name
            : $"S_{load.Slot}";

    string StackSlotName(StoreStackSlot store)
    {
        var type = StackSlotTargetType(store);
        return _stackSlotNames.TryGetValue(new StackSlotRenderKey(store.Slot, StackSlotTypeKey(type)), out var name)
            ? name
            : $"S_{store.Slot}";
    }

    string FixedLocalName(Fixed fixedStatement)
        => fixedStatement.LocalIsStackSlot
            ? _stackSlotNames.TryGetValue(new StackSlotRenderKey(
                    fixedStatement.LocalIndex,
                    StackSlotTypeKey(StackSlotRenderType(fixedStatement.LocalIndex, fixedStatement.LocalStackSlotType))), out var name)
                ? name
                : $"S_{fixedStatement.LocalIndex}"
            : LocalName(fixedStatement.LocalIndex);

    IReadOnlySet<string> CurrentScopeNames()
    {
        var names = CurrentReservedNames(includeLocals: true);
        foreach (var (_, (name, _)) in _stackSlotDeclarations)
            names.Add(name);
        foreach (var name in _switchTemps.Values)
            names.Add(name);
        // Synthetic locals (e.g. __stackalloc) are in scope for this body and for
        // any nested lambda/local-function printer built from these names.
        foreach (var name in _syntheticLocalNames)
            names.Add(name);
        return names;
    }

    HashSet<string> CurrentReservedNames(bool includeLocals = false)
    {
        var names = new HashSet<string>(_reservedScopeNames, StringComparer.Ordinal);
        foreach (var parameter in _function.Signature.Parameters)
            names.Add(parameter.Name);
        foreach (var genericParameter in _function.Signature.GenericParameterNames)
            names.Add(genericParameter);
        foreach (var nested in _function.Descendants.OfType<Lambda>())
            foreach (var parameter in nested.Parameters)
                names.Add(parameter.Name);
        foreach (var nested in _function.Descendants.OfType<LocalFunctionStatement>())
        {
            names.Add(nested.Name);
            foreach (var parameter in nested.Parameters)
                names.Add(parameter.Name);
        }
        if (includeLocals)
        {
            for (int i = 0; i < _function.Locals.Length; i++)
                names.Add(LocalName(i));
        }
        return names;
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
        foreach (var store in DescendantsOutsideNestedFunctions(function).OfType<StoreStackSlot>())
            slotStoreCounts[store.Slot] = slotStoreCounts.GetValueOrDefault(store.Slot) + 1;
        foreach (var node in DescendantsOutsideNestedFunctions(function))
        {
            switch (node)
            {
                case StoreLocal store when !seenLocals.Contains(store.Index):
                    seenLocals.Add(store.Index);
                    if (entryStatements.Contains(store)
                        && !StoreValueReferencesLocal(store)
                        && !HasBranchTargetAfterStatement(store))
                        _declaringStores.Add(store);
                    else if (store.Type.Kind == TypeRefKind.ByRef
                        && LocalReferencesStayInBlockAfterStore(function, store))
                    {
                        // A ref local cannot be declared bare up front (CS8174), and
                        // synthesizing Unsafe.NullRef<T>() changes IL. If the first
                        // definition dominates every reference inside one block, declare
                        // at that ref assignment instead.
                        _declaringStores.Add(store);
                    }
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
                case NullCoalescingAssignment assignment: seenLocals.Add(assignment.LocalIndex); break;
                case StoreStackSlot slotStore when !seenSlots.Contains(slotStore.Slot):
                    seenSlots.Add(slotStore.Slot);
                    if (entryStatements.Contains(slotStore) && slotStore.Value.ResultType is not null
                        && slotStoreCounts[slotStore.Slot] == 1)
                    {
                        _declaringStores.Add(slotStore);
                    }
                    else if (StackSlotTargetType(slotStore) is { Kind: TypeRefKind.ByRef }
                        && StackSlotReferencesStayInBlockAfterStore(function, slotStore))
                    {
                        // A ref stack-slot temp cannot be declared bare up front
                        // (CS8174), and synthesizing Unsafe.NullRef<T>() adds IL.
                        // If every reference stays in the assignment block after
                        // the first store, declare at that ref assignment instead.
                        _declaringStores.Add(slotStore);
                    }
                    break;
                case LoadStackSlot slotLoad: seenSlots.Add(slotLoad.Slot); break;
            }
        }

        // Under the updated memory-safety rules a declaring store whose value is
        // an unsafe operation gets wrapped in an `unsafe { }` block. If the local
        // is also read elsewhere, an inline `Type v = <unsafe>` declaration would
        // strand the variable inside that block (out of scope at its uses), so
        // demote the store: the local declares up front and the wrapped statement
        // becomes a plain `v = <unsafe>` assignment.
        if (_newMemorySafetyRules)
        {
            foreach (var store in _declaringStores.OfType<StoreLocal>().ToList())
            {
                if (HasUnsafeOperation(store.Value)
                    && LocalIsRead(function, store.Index)
                    && !LocalReadsStayInsideUnsafeRun(function, store))
                {
                    _declaringStores.Remove(store);
                    // A stackalloc-initialized span loses its inline `scoped`
                    // inference when split from its declaration, so the hoisted
                    // declaration must restore it (else CS9081). A stackalloc result
                    // can never escape, so `scoped` is always correct here.
                    if (store.Value is StackAllocArray)
                        _scopedLocals.Add(store.Index);
                    continue;
                }
                if (!DeclarationIsInsideUnsafeRun(store))
                {
                    continue;
                }
                _declaringStores.Remove(store);
            }
            foreach (var store in _declaringStores.OfType<StoreStackSlot>().ToList())
            {
                if (!HasUnsafeOperation(store.Value) || StackSlotReferencesStayInBlockAfterStore(function, store))
                    continue;
                _declaringStores.Remove(store);
            }
            foreach (var init in _declaringStores.OfType<InitObject>().ToList())
            {
                if (init.Address is not LoadLocalAddress local)
                    continue;
                if (!DeclarationIsInsideUnsafeRun(init))
                {
                    continue;
                }
                _declaringStores.Remove(init);
            }
        }
    }

    /// <summary>True when the local slot is read (loaded by value or address) anywhere in the body.</summary>
    static bool LocalIsRead(IrFunction function, int index)
        => DescendantsOutsideNestedFunctions(function).Any(n =>
            (n is LoadLocal load && load.Index == index)
            || (n is LoadLocalAddress address && address.Index == index));

    bool LocalReadsStayInsideUnsafeRun(IrFunction function, StoreLocal store)
    {
        if (store.Parent is not Block container)
            return false;
        int start = store.ChildIndex;
        if (start < 0 || start >= container.Children.Count)
            return false;

        int end = start;
        while (end + 1 < container.Children.Count && HasUnsafeOperation(container.Children[end + 1]))
            end++;

        foreach (var node in DescendantsOutsideNestedFunctions(function))
        {
            if (node is LoadLocal load && load.Index == store.Index
                || node is LoadLocalAddress address && address.Index == store.Index)
            {
                bool insideRun = false;
                for (int i = start; i <= end; i++)
                    insideRun |= IsDescendantOrSelf(node, container.Children[i]);
                if (!insideRun)
                    return false;
            }
        }
        return true;
    }

    bool DeclarationIsInsideUnsafeRun(IrNode statement)
    {
        if (statement.Parent is not Block block || statement.ChildIndex <= 0)
            return false;
        for (int i = 0; i < statement.ChildIndex; i++)
        {
            if (NeedsUnsafeContext(block.Children[i])
                && UnsafeRunEnd(block.Children, i) > statement.ChildIndex)
            {
                return true;
            }
        }
        return false;
    }

    bool LocalReferencesStayInBlockAfterStore(IrFunction function, StoreLocal store)
    {
        if (store.Parent is not Block block || store.ChildIndex < 0)
            return false;
        if (StoreValueReferencesLocal(store))
            return false;
        if (HasBranchTargetAfterStatement(store))
            return false;
        return LocalReferencesStayInBlockAfterStatement(function, store, store.Index);
    }

    bool LocalReferencesStayInBlockAfterStatement(IrFunction function, IrNode statement, int index)
    {
        if (statement.Parent is not Block block || statement.ChildIndex < 0)
            return false;
        var allowed = block.Children.Skip(statement.ChildIndex).ToList();
        if (HasBranchTargetAfterStatement(statement))
            return false;
        foreach (var candidateBlock in function.Body.Blocks)
        {
            foreach (var node in candidateBlock.Children)
            {
                if (ReferencesLocalIncludingSharedNestedScopes(node, index)
                    && !allowed.Any(statement => IsDescendantOrSelf(node, statement)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    bool StackSlotReferencesStayInBlockAfterStore(IrFunction function, StoreStackSlot store)
    {
        if (store.Parent is not Block block || store.ChildIndex < 0)
            return false;
        if (ReferencesStackSlot(store.Value, store.Slot))
            return false;
        var allowed = block.Children.Skip(store.ChildIndex).ToList();
        if (HasBranchTargetAfterStatement(store))
            return false;
        bool sawLoad = false;
        foreach (var node in DescendantsOutsideNestedFunctions(function))
        {
            if (node is StoreStackSlot s && s.Slot == store.Slot
                || node is LoadStackSlot l && l.Slot == store.Slot)
            {
                if (!allowed.Any(statement => IsDescendantOrSelf(node, statement)))
                    return false;
                sawLoad |= node is LoadStackSlot;
            }
        }
        return sawLoad;
    }

    static bool StoreValueReferencesLocal(StoreLocal store)
        => ReferencesLocal(store.Value, store.Index);

    bool HasBranchTargetAfterStatement(IrNode statement)
    {
        if (statement.Parent is not Block block || statement.ChildIndex < 0)
            return false;
        return block.Children.Skip(statement.ChildIndex + 1)
            .SelectMany(DescendantsAndSelfOutsideNestedFunctions)
            .Any(n => n.SourceOffset >= 0 && _labelTargets.Contains(n.SourceOffset));
    }

    static bool ReferencesLocal(IrNode node, int index)
    {
        if (IsLocalReference(node, index))
            return true;
        return DescendantsOutsideNestedFunctions(node).Any(n => IsLocalReference(n, index));
    }

    static bool ReferencesLocalIncludingSharedNestedScopes(IrNode node, int index)
    {
        if (node is Lambda nestedLambda && NeedsNestedLambdaScope(nestedLambda))
            return false;
        if (node is LocalFunctionStatement nestedLocalFunction && NeedsNestedLocalFunctionScope(nestedLocalFunction))
            return false;
        if (IsLocalReference(node, index))
            return true;
        foreach (var child in node.Children)
        {
            if (child is Lambda lambda && NeedsNestedLambdaScope(lambda))
                continue;
            if (child is LocalFunctionStatement localFunction && NeedsNestedLocalFunctionScope(localFunction))
                continue;
            if (ReferencesLocalIncludingSharedNestedScopes(child, index))
                return true;
        }
        return false;
    }

    static bool ReferencesStackSlot(IrNode node, int slot)
    {
        if (IsStackSlotReference(node, slot))
            return true;
        return DescendantsOutsideNestedFunctions(node).Any(n => IsStackSlotReference(n, slot));
    }

    static bool IsLocalReference(IrNode node, int index)
        => node is StoreLocal store && store.Index == index
            || node is LoadLocal load && load.Index == index
            || node is LoadLocalAddress address && address.Index == index;

    static bool IsStackSlotReference(IrNode node, int slot)
        => node is LoadStackSlot load && load.Slot == slot
            || node is StoreStackSlot store && store.Slot == slot;

    static bool IsDescendantOrSelf(IrNode node, IrNode ancestor)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    static IEnumerable<IrNode> DescendantsAndSelfOutsideNestedFunctions(IrNode node)
    {
        yield return node;
        if (node is Lambda or LocalFunctionStatement)
            yield break;
        foreach (var descendant in DescendantsOutsideNestedFunctions(node))
            yield return descendant;
    }

    void CollectInlineReceiverTempStores(IrFunction function)
    {
        foreach (var block in DescendantsOutsideNestedFunctions(function).OfType<Block>())
        {
            for (int i = 0; i + 1 < block.Children.Count; i++)
            {
                if (block.Children[i] is not StoreLocal store
                    || block.Children[i + 1] is not StoreElement storeElement
                    || !CanInlineReceiverTempStore(function, store, storeElement))
                {
                    continue;
                }

                _inlineReceiverTempStores[storeElement] = store;
                _inlineReceiverTempLocals.Add(store.Index);
            }
        }
    }

    bool CanInlineReceiverTempStore(IrFunction function, StoreLocal store, StoreElement storeElement)
    {
        if (store.Type.Kind == TypeRefKind.ByRef)
            return false;
        if (store.SourceOffset >= 0 && _labelTargets.Contains(store.SourceOffset))
            return false;
        if (storeElement.Value is not Call { Callee: { HasThis: true, Name: "ToString" } callee, Arguments: [LoadLocalAddress receiver] } call
            || receiver.Index != store.Index
            || (storeElement.ElementType is not null && !Equals(callee.ReturnType, storeElement.ElementType)))
        {
            return false;
        }
        if (!CanEvaluateBeforeInlineValue(storeElement.Array, store.Value)
            || !CanEvaluateBeforeInlineValue(storeElement.Index, store.Value))
        {
            return false;
        }

        int stores = 0, addressLoads = 0;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreLocal s when s.Index == store.Index:
                    stores++;
                    if (!ReferenceEquals(s, store))
                        return false;
                    break;
                case LoadLocalAddress a when a.Index == store.Index:
                    addressLoads++;
                    if (!ReferenceEquals(a, receiver))
                        return false;
                    break;
                case LoadLocal l when l.Index == store.Index:
                    return false;
            }
        }
        return stores == 1 && addressLoads == 1 && ReferenceEquals(call.Arguments[0], receiver);
    }

    static bool CanEvaluateBeforeInlineValue(IrExpression expression, IrExpression value) => expression switch
    {
        Constant => true,
        LoadArgument argument => !ReferencesArgument(value, argument.Index),
        LoadLocal local => !ReferencesLocal(value, local.Index),
        _ => false,
    };

    static bool ReferencesArgument(IrNode node, int index)
        => IsArgumentReference(node, index)
            || node.Descendants.Any(n => IsArgumentReference(n, index));

    static bool IsArgumentReference(IrNode node, int index)
        => node is LoadArgument argument && argument.Index == index
            || node is LoadArgumentAddress address && address.Index == index;

    /// <summary>True when the local's last program-order reference sits inside the given subtree.</summary>
    static bool LastReferenceIsInside(IrFunction function, int localIndex, IrNode subtree)
    {
        IrNode? last = null;
        foreach (var node in DescendantsOutsideNestedFunctions(function))
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

    static IEnumerable<IrNode> DescendantsOutsideNestedFunctions(IrNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            if (child is Lambda or LocalFunctionStatement)
                continue;
            foreach (var descendant in DescendantsOutsideNestedFunctions(child))
                yield return descendant;
        }
    }

    static bool NeedsNestedLocalFunctionScope(LocalFunctionStatement localFunction)
        => !localFunction.Locals.IsEmpty
            || localFunction.Body.Descendants.Any(node => node is LoadStackSlot or StoreStackSlot);

    void AppendNestedLocalFunctionBody(StringBuilder sb, LocalFunctionStatement localFunction, int indent)
    {
        var body = (BlockContainer)localFunction.Body.Clone();
        var function = new IrFunction(
            localFunction.Name,
            _function.DeclaringType,
            new MethodSignature(localFunction.ReturnType, localFunction.Parameters, HasThis: false, GenericParameterCount: 0),
            localFunction.Locals,
            body)
        {
            LocalNames = localFunction.LocalNames,
            UsesUpdatedMemorySafetyRules = localFunction.UsesUpdatedMemorySafetyRules,
            SkipLocalsInit = localFunction.SkipLocalsInit,
        };

        string pad = new(' ', indent * 4);
        foreach (var line in new CSharpPrinter(function, _options, CurrentScopeNames(), _stackSlotTelemetry).PrintBody(function).TrimEnd().Split(Environment.NewLine))
            sb.Append(pad).AppendLine(line);
    }

    /// <summary>Recursive statement emission with indentation — structured nodes (IfStatement) nest, flat statements render through <see cref="Statement"/>.</summary>
    void AppendStatement(StringBuilder sb, IrNode node, int indent)
    {
        if (_statementLines is not null)
        {
            int startLine = 0;
            for (int c = 0; c < sb.Length; c++)
                if (sb[c] == '\n')
                    startLine++;
            _statementLines.TryAdd(node, startLine);
        }
        string pad = new(' ', indent * 4);
        if (node is LocalFunctionStatement localFunction)
        {
            string modifier = localFunction.IsStatic ? "static " : "";
            string parameters = string.Join(", ", localFunction.Parameters.Select(p => $"{TypeText(p.Type)} {CSharpNaming.EscapeIdentifier(p.Name)}"));
            string header = $"{modifier}{TypeText(localFunction.ReturnType)} {CSharpNaming.EscapeIdentifier(localFunction.Name)}({parameters})";
            if (localFunction.ExpressionBody is { } body)
            {
                sb.Append(pad).Append(header).Append(" => ").Append(Expression(body)).AppendLine(";");
            }
            else
            {
                sb.Append(pad).AppendLine(header);
                sb.Append(pad).AppendLine("{");
                if (NeedsNestedLocalFunctionScope(localFunction))
                    AppendNestedLocalFunctionBody(sb, localFunction, indent + 1);
                else
                {
                    AppendContainer(sb, localFunction.Body, indent + 1);
                    if (NeedsUnsupportedFallbackReturn(localFunction.ReturnType, requiresAsyncBodyModifier: false, localFunction.Body))
                        sb.Append(new string(' ', (indent + 1) * 4)).AppendLine("return default;");
                }
                sb.Append(pad).AppendLine("}");
            }
            return;
        }
        if (node is Return { Value: SwitchExpression returnedSwitch })
        {
            // A switch expression returned spans several lines, one arm per line,
            // indented under the governing value — the statement context knows the
            // indent the inline Expression() form cannot.
            string inner = pad + "    ";
            var labelEnum = SwitchLabelEnumType(returnedSwitch.Value);
            sb.Append(pad).Append("return ").Append(Operand(returnedSwitch.Value)).AppendLine(" switch");
            sb.Append(pad).AppendLine("{");
            foreach (var arm in returnedSwitch.Arms)
                sb.Append(inner).Append(SwitchArmText(arm, _function.Signature.ReturnType, labelEnum)).AppendLine(",");
            sb.Append(pad).AppendLine("};");
            return;
        }
        if (node is Return { Value: UnionSwitchExpression unionSwitch })
        {
            string inner = pad + "    ";
            sb.Append(pad).Append("return ").Append(UnionSwitchReceiverText(unionSwitch.Value)).AppendLine(" switch");
            sb.Append(pad).AppendLine("{");
            if (unionSwitch.NullValue is { } nullValue)
                sb.Append(inner).Append("null => ").Append(SwitchArmValueText(nullValue, _function.Signature.ReturnType)).AppendLine(",");
            foreach (var arm in unionSwitch.Arms)
                sb.Append(inner).Append(UnionSwitchArmText(arm, _function.Signature.ReturnType)).AppendLine(",");
            if (unionSwitch.DefaultValue is { } defaultValue)
                sb.Append(inner).Append("_ => ").Append(SwitchArmValueText(defaultValue, _function.Signature.ReturnType)).AppendLine(",");
            sb.Append(pad).AppendLine("};");
            return;
        }
        if (node is Return { Value: StackAllocate stackAllocate }
            && _function.Signature.ReturnType is { Kind: TypeRefKind.Pointer } returnPointer)
        {
            string localName = FreshSyntheticLocalName("__stackalloc");
            sb.Append(pad)
                .Append(TypeText(stackAllocate.ResultType!))
                .Append(' ')
                .Append(localName)
                .Append(" = ")
                .Append(Expression(stackAllocate))
                .AppendLine(";");
            string value = returnPointer.Equals(stackAllocate.ResultType)
                ? localName
                : $"({TypeText(returnPointer)}){localName}";
            sb.Append(pad).Append("return ").Append(value).AppendLine(";");
            return;
        }
        if (node is StoreLocal { Value: StackAllocate storeStackAllocate, Type.Kind: TypeRefKind.Pointer } store
            && store.Type is { } storeType
            && storeStackAllocate.ResultType is { } stackAllocType
            && !storeType.Equals(stackAllocType))
        {
            string localName = FreshSyntheticLocalName("__stackalloc");
            sb.Append(pad)
                .Append(TypeText(stackAllocType))
                .Append(' ')
                .Append(localName)
                .Append(" = ")
                .Append(Expression(storeStackAllocate))
                .AppendLine(";");
            sb.Append(pad);
            if (_declaringStores.Contains(store))
                sb.Append(TypeText(storeType)).Append(' ');
            sb.Append(LocalName(store.Index))
                .Append(" = (")
                .Append(TypeText(storeType))
                .Append(')')
                .Append(localName)
                .AppendLine(";");
            return;
        }
        if (node is ForLoop forLoop)
        {
            string initializer = Statement(forLoop.Initializer)?.TrimEnd(';') ?? "";
            string increment = ForLoopIncrementText(forLoop.Increment);
            sb.Append(pad).Append("for (").Append(initializer).Append("; ")
                .Append(Condition(forLoop.Condition)).Append("; ").Append(increment).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendStatements(sb, forLoop.Body.Children, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is WhileLoop whileLoop)
        {
            sb.Append(pad).Append("while (").Append(Condition(whileLoop.Condition)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendStatements(sb, whileLoop.Body.Children, indent + 1);
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
        if (node is Fixed fixedStatement)
        {
            sb.Append(pad)
                .Append("fixed (").Append(TypeText(fixedStatement.ElementType)).Append("* ")
                .Append(FixedLocalName(fixedStatement)).Append(" = ")
                .Append(fixedStatement.SourceIsAddress
                    ? "&" + Deref(fixedStatement.PinSource)
                    : Expression(fixedStatement.PinSource))
                .AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, fixedStatement.Body, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is UsingStatement usingStatement)
        {
            sb.Append(pad)
                .Append(usingStatement.IsAwait ? "await using (" : "using (").Append(TypeText(usingStatement.ResourceType)).Append(' ')
                .Append(LocalName(usingStatement.LocalIndex)).Append(" = ")
                .Append(CoerceText(usingStatement.Resource, usingStatement.ResourceType)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendContainer(sb, usingStatement.Body, indent + 1);
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is ForeachStatement foreachStatement)
        {
            sb.Append(pad)
                .Append(foreachStatement.IsAwait ? "await foreach (" : "foreach (")
                .Append(TypeText(foreachStatement.LocalType)).Append(' ')
                .Append(LocalName(foreachStatement.LocalIndex)).Append(" in ")
                .Append(Expression(foreachStatement.Collection)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            AppendStatements(sb, foreachStatement.Body.Children, indent + 1);
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
            AppendStatements(sb, ifStatement.Then.Children, indent + 1);
            sb.Append(pad).AppendLine("}");
            if (ifStatement.Else is { } elseArm)
            {
                sb.Append(pad).AppendLine("else");
                sb.Append(pad).AppendLine("{");
                AppendStatements(sb, elseArm.Children, indent + 1);
                sb.Append(pad).AppendLine("}");
            }
            return;
        }
        if (node is Switch switchNode)
        {
            sb.Append(pad).Append("switch (").Append(Expression(switchNode.Value)).AppendLine(")");
            sb.Append(pad).AppendLine("{");
            string labelPad = pad + "    ";
            var labelEnum = SwitchLabelEnumType(switchNode.Value);
            foreach (var section in switchNode.Sections)
            {
                foreach (var label in section.Labels)
                    sb.Append(labelPad).Append("case ").Append(SwitchLabelText(label, labelEnum)).AppendLine(":");
                if (section.IsDefault)
                    sb.Append(labelPad).AppendLine("default:");
                AppendContainer(sb, section.Body, indent + 2);
            }
            sb.Append(pad).AppendLine("}");
            return;
        }
        if (node is SwitchBranch switchBranch)
        {
            // The IL switch opcode is a jump table: it branches to
            // targets[value] when 0 <= value < targets.Length and falls through
            // otherwise. A C# switch section cannot goto a label outside the
            // switch, so render an if-chain over a single-evaluated temp instead
            // of `case i: goto IL_xxxx;`. Fall-through preserves out-of-range
            // behavior.
            string temp = _switchTemps.TryGetValue(switchBranch, out var name) ? name : "__dotnet_inspect_switch";
            sb.Append(pad).Append(temp).Append(" = ")
                .Append("(int)(").Append(Expression(switchBranch.Value)).AppendLine(");");
            for (int t = 0; t < switchBranch.TargetOffsets.Length; t++)
                sb.Append(pad).Append("if (").Append(temp).Append(" == ").Append(t)
                    .AppendLine($") goto IL_{switchBranch.TargetOffsets[t]:X4};");
            return;
        }
        if (Statement(node) is { } line)
            sb.Append(pad).AppendLine(line);
    }

    /// <summary>
    /// Emits a sibling statement sequence, wrapping unsafe operations in
    /// explicit <c>unsafe { }</c> blocks when the source module uses the updated
    /// memory-safety rules. Maximal runs of adjacent statements that need an
    /// unsafe context coalesce into a single block, and the block is scoped to
    /// the smallest enclosing statement: a loop or <c>if</c> whose body (not its
    /// header) holds the unsafe op is left unwrapped so the recursion wraps the
    /// inner statement instead. When the module uses legacy rules — or the
    /// statements are already inside an emitted block — this is a plain
    /// per-statement emit, identical to the pre-feature output.
    /// </summary>
    void AppendStatements(StringBuilder sb, IReadOnlyList<IrNode> statements, int indent)
    {
        int i = 0;
        while (i < statements.Count)
        {
            if (statements[i] is StoreLocal inlineStore && _inlineReceiverTempStores.ContainsValue(inlineStore))
            {
                i++;
                continue;
            }

            if (_newMemorySafetyRules && _unsafeDepth == 0 && NeedsUnsafeContext(statements[i]))
            {
                int j = UnsafeRunEnd(statements, i);
                string pad = new(' ', indent * 4);
                sb.Append(pad).AppendLine("unsafe");
                sb.Append(pad).AppendLine("{");
                _unsafeDepth++;
                for (int k = i; k < j; k++)
                {
                    if (statements[k] is StoreLocal unsafeInlineStore && _inlineReceiverTempStores.ContainsValue(unsafeInlineStore))
                        continue;
                    AppendStatementLabel(sb, statements[k], indent + 1);
                    AppendStatement(sb, statements[k], indent + 1);
                }
                _unsafeDepth--;
                sb.Append(pad).AppendLine("}");
                i = j;
            }
            else
            {
                AppendStatementLabel(sb, statements[i], indent);
                AppendStatement(sb, statements[i], indent);
                i++;
            }
        }
    }

    int UnsafeRunEnd(IReadOnlyList<IrNode> statements, int start)
    {
        int end = start + 1;
        while (end < statements.Count && NeedsUnsafeContext(statements[end]))
            end++;

        for (int i = start; i < end; i++)
        {
            int requiredEnd = UnsafeRunRequiredEnd(statements, i, end);
            if (requiredEnd > end)
            {
                end = requiredEnd;
                while (end < statements.Count && NeedsUnsafeContext(statements[end]))
                    end++;
            }
        }

        return end;
    }

    int UnsafeRunRequiredEnd(IReadOnlyList<IrNode> statements, int declarationIndex, int searchStart)
    {
        return statements[declarationIndex] switch
        {
            StoreStackSlot store when _declaringStores.Contains(store)
                => LastReferenceEnd(statements, searchStart, node => ReferencesStackSlot(node, store.Slot)),
            StoreLocal store when _declaringStores.Contains(store) && HasUnsafeOperation(store.Value)
                => LastReferenceEnd(statements, searchStart, node => ReferencesLocalIncludingSharedNestedScopes(node, store.Index)),
            _ => searchStart,
        };
    }

    static int LastReferenceEnd(IReadOnlyList<IrNode> statements, int start, Func<IrNode, bool> hasReference)
    {
        int end = start;
        for (int i = start; i < statements.Count; i++)
            if (hasReference(statements[i]))
                end = i + 1;
        return end;
    }

    void AppendStatementLabel(StringBuilder sb, IrNode statement, int indent)
    {
        if (statement.SourceOffset >= 0 && _labelTargets.Contains(statement.SourceOffset))
            AppendLabel(sb, new string(' ', indent * 4), statement.SourceOffset);
    }

    /// <summary>
    /// Whether a statement must itself sit in an unsafe context. For a compound
    /// statement only its own header expressions are considered — the body is a
    /// separate statement sequence the recursion wraps independently, keeping the
    /// block minimal. A simple statement is tested whole.
    /// </summary>
    bool NeedsUnsafeContext(IrNode node) => node switch
    {
        ForLoop f => HasUnsafeOperation(f.Initializer) || HasUnsafeOperation(f.Condition) || HasUnsafeOperation(f.Increment),
        WhileLoop w => HasUnsafeOperation(w.Condition),
        DoWhileLoop d => HasUnsafeOperation(d.Condition),
        IfStatement s => HasUnsafeOperation(s.Condition),
        Switch s => HasUnsafeOperation(s.Value),
        Lock l => HasUnsafeOperation(l.LockObject),
        Fixed { RequiresUnsafeContext: true } => true,
        Fixed fx => HasUnsafeOperation(fx.PinSource),
        UsingStatement u => HasUnsafeOperation(u.Resource),
        TryCatch t => t.Clauses.Any(c => HasUnsafeOperation(c.Filter)),
        TryFinally => false,
        StoreElement s when _inlineReceiverTempStores.TryGetValue(s, out var store)
            => HasUnsafeOperation(s) || HasUnsafeOperation(store.Value),
        _ => HasUnsafeOperation(node),
    };

    bool HasUnsafeOperation(IrNode? node)
        => node is not null && (IsUnsafeOperation(node) || node.Descendants.Any(IsUnsafeOperation));

    /// <summary>
    /// A single IR operation that requires an unsafe context under the updated
    /// rules: a function-pointer invocation (<c>calli</c>), a read/write through
    /// an unmanaged pointer, a call to a <em>requires-unsafe</em> member (one
    /// stamped with <c>RequiresUnsafeAttribute</c> — declared <c>unsafe</c>/
    /// <c>extern</c> — or, by the compat heuristic, one with a pointer in its
    /// signature), or a <c>stackalloc</c> converted to a <c>Span</c> with no
    /// initializer in a <c>[SkipLocalsInit]</c> body. Dereferencing a managed
    /// reference (<c>ByRef</c>) is safe and excluded. Creating pointers, the
    /// String-pin fixed statements raised through a synthesized stack-slot
    /// pointer need an unsafe context for their header. Creating pointers,
    /// ordinary <c>fixed</c> statements, and <c>sizeof</c> are safe under the new
    /// rules.
    /// </summary>
    bool IsUnsafeOperation(IrNode node) => node switch
    {
        CallIndirect => true,
        StackAllocate => true,
        // A stackalloc-backed Span (raised to `stackalloc T[n]` by
        // StackAllocSpanPass) is governed by the stackalloc rule — unsafe only
        // under [SkipLocalsInit], where the stack space is uninitialized.
        StackAllocArray sa => _skipLocalsInit || sa.ResultType?.Kind == TypeRefKind.Pointer,
        Call c => c.Callee.RequiresUnsafe || SignatureRequiresUnsafe(c.Callee),
        NewObject n => n.Constructor.RequiresUnsafe || SignatureRequiresUnsafe(n.Constructor),
        Binary b => IsPointerArithmetic(b),
        Comparison c => IsPointerComparison(c),
        LoadIndirect l => RendersAsPointerDeref(l.Address),
        StoreIndirect s => RendersAsPointerDeref(s.Address),
        InitObject o => RendersAsPointerDeref(o.Address),
        _ => false,
    };

    static bool IsPointerArithmetic(Binary binary)
        => binary.Kind is BinaryKind.Add or BinaryKind.Subtract
            && (binary.Left.ResultType is { Kind: TypeRefKind.Pointer }
                || binary.Right.ResultType is { Kind: TypeRefKind.Pointer });

    static bool IsPointerComparison(Comparison comparison)
        => comparison.Left.ResultType is { Kind: TypeRefKind.Pointer }
            || comparison.Right.ResultType is { Kind: TypeRefKind.Pointer };

    /// <summary>
    /// Compat-mode requires-unsafe heuristic for a callee whose
    /// <c>RequiresUnsafeAttribute</c> can't be read (a cross-assembly
    /// MemberRef): the member is requires-unsafe if a pointer or function-pointer
    /// type appears anywhere among its parameter or return types — possibly
    /// nested in a non-pointer type such as <c>int*[]</c>. Mirrors the spec's
    /// compat fallback, which keeps such calls unsafe during the migration window
    /// even for callers that haven't opted into the new rules.
    /// </summary>
    static bool SignatureRequiresUnsafe(MethodRef callee)
        => ContainsPointer(callee.ReturnType) || callee.ParameterTypes.Any(ContainsPointer);

    static bool ContainsPointer(TypeRef? type)
        => type is not null
            && (type.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));

    /// <summary>
    /// Whether <see cref="Deref"/> renders this load/store-indirect address with
    /// a leading <c>*</c> — i.e. it is a read/write through an unmanaged pointer
    /// rather than a managed reference. Mirrors the managed-reference cases of
    /// <see cref="Deref"/> exactly: anything not spelled as a place or a
    /// <c>ByRef</c> is a pointer dereference, which requires an unsafe context.
    /// </summary>
    static bool RendersAsPointerDeref(IrExpression address) => address switch
    {
        LoadArgument { Index: 0, Name: "this" } => false,
        LoadLocalAddress => false,
        LoadArgumentAddress => false,
        LoadFieldAddress => false,
        LoadElementAddress => false,
        Conditional { ResultType.Kind: TypeRefKind.ByRef } c
            when c.WhenTrue.ResultType?.Kind == TypeRefKind.ByRef
                && c.WhenFalse.ResultType?.Kind == TypeRefKind.ByRef => false,
        { ResultType.Kind: TypeRefKind.ByRef } => false,
        _ => true,
    };

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
        return $"{(isThis ? "this" : "base")}({Arguments(arguments, callee.ParameterTypes, callee.ParameterRefKinds, chainFidelityCasts: true)});";
    }

    /// <summary>The index of the base/this <c>.ctor</c> chain call in the entry block, or null when the body has none (a struct ctor, a static method, a body that never chains).</summary>
    static int? ChainCallIndex(Block entry)
    {
        for (int i = 0; i < entry.Children.Count; i++)
        {
            if (entry.Children[i] is ExpressionStatement { Expression: Call { Callee: { Name: ".ctor", HasThis: true } } call }
                && call.Arguments is [_, ..])
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>
    /// A <c>this.field = value</c> store whose value is a field initializer:
    /// self-contained (no <c>this</c>, parameter, local, or slot load), so it is
    /// legal in field-declaration context. C# field initializers cannot read the
    /// instance or constructor parameters, which is exactly the place-load ban.
    /// </summary>
    static bool IsFieldInitializerStore(IrNode node)
        => node is StoreField { HasInstance: true, Instance: LoadArgument { Index: 0 } } store
            && !ReferencesPlace(store.Value);

    static bool ReferencesPlace(IrExpression value)
    {
        foreach (var node in (IEnumerable<IrNode>)[value, .. value.Descendants])
        {
            if (node is LoadArgument or LoadLocal or LoadStackSlot or LoadLocalAddress or LoadArgumentAddress)
                return true;
        }
        return false;
    }

    /// <summary>Baseline-style clause headers: bare <c>catch</c> for object (the catch-all), the variable form when the entry store folded into the clause.</summary>
    string CatchHeader(CatchClause clause)
    {
        string header = clause.ExceptionType is { Namespace: "System", Name: "Object" }
            ? "catch"
            : clause.VariableIndex is { } index
                ? $"catch ({TypeText(clause.ExceptionType)} {LocalName(index)})"
                : $"catch ({TypeText(clause.ExceptionType)})";
        return clause.Filter is { } filter ? $"{header} when ({Condition(filter)})" : header;
    }

    /// <summary>Null means the statement has no body spelling: a no-argument base-constructor call is implicit in C#.</summary>
    string? Statement(IrNode node) => node switch
    {
        ExpressionStatement
        {
            Expression: Call { Callee: { Name: ".ctor", HasThis: true } callee } call,
        } when call.Arguments is [_, ..]
            => ConstructorChainText(callee, call),
        ExpressionStatement e => e.Expression switch
        {
            UnsupportedNode u => $"/* {u.Describe()} */",
            // A user-defined checked ++/-- as a statement spells checked(x++),
            // which is CS0201 in statement position; use a checked { ... } block.
            IncrementDecrement { IsChecked: true } id => CheckedIncrementStatement(id),
            // C# requires an expression statement to be an invocation, object
            // creation, await, or inc/decrement. A bare value — a stack slot
            // discarded by an IL `pop`, a comparison, the caught exception, an
            // operator-spelled call (`a != b`) — is CS0201 as a statement, so
            // spell the discard explicitly with `_ =`, which is always valid.
            { } expr when !IsStatementExpression(expr) => $"_ = {Expression(expr)};",
            { } expr => $"{Expression(expr)};",
        },
        // Storing into a ref-typed local rebinds the reference itself (stloc of
        // a managed pointer), not a write-through — that is C#'s ref
        // (re)assignment, which takes `= ref <place>` on both the initial
        // declaration (CS8172) and any later rebind (CS8173). Deref renders the
        // address value as the place it refers to.
        StoreLocal { Type.Kind: TypeRefKind.ByRef } s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} {LocalName(s.Index)} = ref {Deref(s.Value)};"
            : $"{LocalName(s.Index)} = ref {Deref(s.Value)};",
        StoreLocal s => _declaringStores.Contains(s)
            ? $"{DeclarationTypeText(s.Type, s.Value)} {LocalName(s.Index)} = {CoerceText(s.Value, s.Type)};"
            : AssignmentText($"{LocalName(s.Index)}", s.Value, left => left is LoadLocal load && load.Index == s.Index, s.Type),
        DeconstructionAssignment d => $"({string.Join(", ", d.Targets.Select(DeconstructionTargetText))}) = {Expression(d.Source)};",
        NullCoalescingAssignment n => $"{LocalName(n.LocalIndex)} ??= {CoerceText(n.Value, n.LocalType)};",
        NullCoalescingFieldAssignment n => $"{FieldTarget(n.Field, n.Instance)} ??= {CoerceText(n.Value, n.Field.Type)};",
        NullCoalescingPropertyAssignment n => $"{PropertyTarget(n.Setter, n.Instance, n.IndexArguments, n.PropertyName, n.IsVirtual)} ??= {CoerceText(n.Value, n.PropertyType)};",
        StoreArgument s => AssignmentText(CSharpNaming.EscapeIdentifier(s.Name), s.Value, left => left is LoadArgument load && load.Index == s.Index, s.Type),
        // A ref-typed slot stores by rebinding the reference — C#'s ref
        // (re)assignment, exactly as for ref locals above.
        StoreStackSlot s when StackSlotTargetType(s) is { Kind: TypeRefKind.ByRef } refType => _declaringStores.Contains(s)
            ? $"{TypeText(refType)} {StackSlotName(s)} = ref {Deref(s.Value)};"
            : $"{StackSlotName(s)} = ref {Deref(s.Value)};",
        StoreStackSlot s => _declaringStores.Contains(s)
            ? $"{DeclarationTypeText(StackSlotTargetType(s)!, s.Value)} {StackSlotName(s)} = {CoerceText(s.Value, StackSlotTargetType(s))};"
            : AssignmentText(StackSlotName(s), s.Value, left => left is LoadStackSlot load && StackSlotName(load) == StackSlotName(s), StackSlotTargetType(s)),
        StoreField s => AssignmentText(
            FieldTarget(s.Field, s.Instance), s.Value,
            left => left is LoadField load
                && load.Field.Name == s.Field.Name
                && Equals(load.Field.DeclaringType, s.Field.DeclaringType)
                && SamePlace(load.Instance, s.Instance),
            s.Field.Type),
        StoreProperty s => AssignmentText(
            PropertyTarget(s.Accessor, s.HasInstance ? s.Instance : null, s.IndexArguments, s.PropertyName, s.IsVirtual),
            s.Value,
            left => left is LoadProperty load
                && load.PropertyName == s.PropertyName
                && Equals(load.Accessor.DeclaringType, s.Accessor.DeclaringType)
                && SameLValue(load.Instance, s.Instance)
                && PlaceIdentity.SameOperands(load.IndexArguments, s.IndexArguments),
            StorePropertyTargetType(s)),
        EventSubscription e => $"{PropertyTarget(e.Accessor, e.HasInstance ? e.Instance : null, [], e.EventName, e.IsVirtual)} {(e.IsAdd ? "+=" : "-=")} {CoerceText(e.Value, e.Accessor.ParameterTypes[0])};",
        StoreElement s when InlineReceiverTempStoreValue(s) is { } value => $"{Operand(s.Array)}[{Expression(s.Index)}] = {value};",
        StoreElement s => $"{Operand(s.Array)}[{Expression(s.Index)}] = {CoerceText(s.Value, StoreElementTargetType(s))};",
        StoreIndirect s => AssignmentText(
            IndirectTarget(s.Address, IndirectStoreType(s.Address, s.Type)),
            s.Value,
            left => left is LoadIndirect load && SameLValue(load.Address, s.Address),
            IndirectStoreType(s.Address, s.Type)),
        // default-initialization of a named place spells through the place,
        // not its address.
        InitObject { Address: LoadLocalAddress local } init => _declaringStores.Contains(init)
            ? $"{TypeText(init.Type)} {LocalName(local.Index)} = default;"
            : $"{LocalName(local.Index)} = default;",
        InitObject { Address: LoadArgumentAddress argument } => $"{CSharpNaming.EscapeIdentifier(argument.Name)} = default;",
        InitObject { Address: LoadFieldAddress field } o2 => $"{FieldTarget(field.Field, field.Instance)} = default;",
        InitObject o => $"{Deref(o.Address)} = default({TypeText(o.Type)});",
        CopyBlock cb => "/* unsupported cpblk */",
        Return { Value: { } value } => ReturnText(value),
        Return => "return;",
        YieldReturn y => $"yield return {Expression(y.Value)};",
        YieldBreak => "yield break;",
        // The rethrow: the raw caught value thrown back is C#'s bare throw.
        Throw { Value: CaughtException } => "throw;",
        Throw t => $"throw {Expression(t.Value)};",
        Break => "break;",
        Continue => "continue;",
        Branch b => $"goto IL_{b.TargetOffset:X4};",
        ConditionalBranch c => $"if ({Condition(c.Condition)}) goto IL_{c.TargetOffset:X4};",
        SwitchBranch s => $"switch ({Expression(s.Value)}) goto [{string.Join(", ", s.TargetOffsets.Select(t => $"IL_{t:X4}"))}];",
        Leave l => $"goto IL_{l.TargetOffset:X4}; // leave",
        EndFinally => "// endfinally",
        EndFilter f => $"// endfilter({Expression(f.Value)})",
        _ => $"/* {node.Describe()} */",
    };

    string? InlineReceiverTempStoreValue(StoreElement storeElement)
    {
        if (!_inlineReceiverTempStores.TryGetValue(storeElement, out var store)
            || storeElement.Value is not Call call)
        {
            return null;
        }

        string receiver = ReceiverText(store.Value);
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        string rest = Arguments(call.Arguments.Skip(1), call.Callee.ParameterTypes, call.Callee.ParameterRefKinds);
        return $"{receiver}.{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({rest})";
    }

    string ForLoopIncrementText(IrNode node)
        => node is ExpressionStatement { Expression: IncrementDecrement { IsChecked: true } increment }
            ? Expression(increment)
            : Statement(node)?.TrimEnd(';') ?? "";

    string DeconstructionTargetText(DeconstructionTarget target) => target.Kind switch
    {
        DeconstructionTargetKind.Local => target.IsDeclared
            ? $"{TypeText(target.Type)} {LocalName(target.LocalIndex)}"
            : LocalName(target.LocalIndex),
        DeconstructionTargetKind.Property => PropertyTarget(target.Accessor!, target.HasInstance ? target.Instance : null, target.IndexArguments, target.PropertyName, target.IsVirtual),
        DeconstructionTargetKind.Argument => CSharpNaming.EscapeIdentifier(target.ArgumentName),
        DeconstructionTargetKind.Field => FieldTarget(
            target.Field!,
            target.IsThisInstance ? new LoadArgument(0, "this", target.Field!.DeclaringType) : null),
        _ => $"/* {target.Describe()} */",
    };

    static TypeRef? StorePropertyTargetType(StoreProperty store)
        => store.Accessor.ParameterTypes.Length > 0 ? store.Accessor.ParameterTypes[^1] : null;

    // The `stelem` opcode records a storage-primitive element type (e.g. `long` for
    // a long-backed enum array, or `int` for a cross-assembly enum array), which
    // drops an enum-typed integer store below its real element type and prints a
    // bare literal (CS0266). Prefer the array's own element type when it is
    // enum-like — same-assembly (`TypeShape.Enum`) or cross-assembly (an unresolved
    // non-primitive definition), matching `Coerce`'s enum-cast reasoning.
    TypeRef? StoreElementTargetType(StoreElement store)
        => CoercionSinks.StoreElementTarget(store, _function.TypeShapes);

    string Expression(IrExpression node) => node switch
    {
        LoadArgument { Index: 0, Name: "this" } => "this",
        LoadArgument a => CSharpNaming.EscapeIdentifier(a.Name),
        LoadLocal l => $"{LocalName(l.Index)}",
        LoadStackSlot s => StackSlotName(s),
        Constant { Value: int or long } c when EnumMemberName(c) is { } named => named,
        // A retyped enum constant is still that enum whether or not a single
        // member names it — a bare int is CS0266. EnumConstantText owns the
        // name-or-cast decision (the overflow-aware cast wraps an unsigned- or
        // narrow-backed enum's out-of-range/negative value in `unchecked`, e.g.
        // `unchecked((U)(-1))`); naming flag combinations is a later slice. A
        // long-backed enum keeps its `long` payload.
        Constant { Value: int or long, Type: { } enumType } c when _function.TypeShapes.GetValueOrDefault(enumType) == TypeShape.Enum
            => EnumConstantText(c, enumType),
        Constant c => ConstantText(c),
        LoadField f => FieldTarget(f.Field, f.Instance),
        Binary b => BinaryText(b),
        Comparison c => ComparisonText(c),
        // A LogicalNot in value position (a folded `brfalse x; ldc.0/ldc.1` select,
        // e.g. `return y == null`) over a NON-bool operand is the same truthiness
        // test the condition path spells: `!y` on an object is CS0023, the faithful
        // form is `y is null` (and `x == 0` for an integer). A bool operand returns
        // null from Truthiness and keeps the bare `!operand`.
        LogicalNot { Operand: Comparison c } => ComparisonText(
            Conditions.Inverse(c.Kind),
            IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
            c.Left, c.Right),
        LogicalNot { Operand: LogicalBinary logical } when TryPropertyPatternText(logical, negated: true) is { } negatedPattern => negatedPattern,
        LogicalNot { Operand: { } operand } when Truthiness(operand) is { } negated => negated.Inverted,
        LogicalNot n => $"!{Operand(n.Operand)}",
        LogicalBinary l => LogicalText(l),
        Conditional t => ConditionalText(t),
        SwitchExpression se => SwitchExpressionInline(se),
        UnionSwitchExpression se => UnionSwitchExpressionInline(se),
        NullCoalescingFieldAssignmentExpression n => $"{FieldTarget(n.Field, n.Instance)} ??= {CoerceText(n.Value, n.Field.Type)}",
        Coalesce co => CoalesceText(co),
        NullConditional nc => NullConditionalText(nc),
        Unary { Kind: UnaryKind.Negate } u => $"-{Operand(u.Operand)}",
        Unary u => $"~{Operand(u.Operand)}",
        AwaitExpression aw => $"await {Operand(aw.Operand)}",
        IncrementDecrement id => IncrementDecrementText(id),
        // The coercion node renders through the one rule — the node IS the
        // routing guarantee; CoerceText decides cast, unchecked, name, or bare.
        Coerce co => CoerceText(co.Operand, co.Target),
        Convert v => ConvertText(v),
        Call c when MultiDimArrayAccessText(c) is { } text => text,
        Call c => CallText(c),
        CallIndirect ci => $"{FunctionPointerOperand(ci.Pointer)}({Arguments(ci.Arguments, ci.ParameterTypes, CallIndirectRefKinds(ci), explicitIn: true)})",
        DelegateCreation d => $"new {TypeText(d.DelegateType)}({MethodGroupText(d.Method, d.Target)})",
        InterpolatedStringExpression i => InterpolatedStringText(i),
        Lambda lam => LambdaText(lam),
        LocalFunctionInvocation inv => $"{CSharpNaming.EscapeIdentifier(inv.Name)}({Arguments(inv.Arguments)})",
        AddressOfMethod m => AddressOfMethodText(m),
        LoadFunctionPointer p => $"/* {p.Describe()} */",
        LoadProperty p => PropertyTarget(p.Accessor, p.HasInstance ? p.Instance : null, p.IndexArguments, p.PropertyName, p.IsVirtual),
        NewObject n when MultiDimArrayCreationText(n) is { } text => text,
        NewObject n => $"new {TypeText(n.Constructor.DeclaringType)}({Arguments(n.Arguments, n.Constructor.ParameterTypes, n.Constructor.ParameterRefKinds)})",
        TupleExpression t => $"({Arguments(t.Elements)})",
        TupleBinaryExpression t => $"{Operand(t.Left)} {(t.IsEquality ? "==" : "!=")} {Operand(t.Right)}",
        AnonymousObject a => AnonymousObjectText(a),
        ObjectInitializerExpression oi => ObjectInitializerText(oi),
        WithExpression w => WithExpressionText(w),
        InitializerBlock ib => InitializerBodyText(ib.IsCollection, ib.Entries),
        ArrayLength l => $"{Operand(l.Array)}.Length",
        SliceExpression sl => $"{ReceiverText(sl.Receiver)}[{Expression(sl.Range)}]",
        // Endpoints go through Operand(), not Expression(): the range operator `..`
        // binds tighter than `+`/`-`/`*`/… on its operand, so a compound bound must
        // keep its parentheses (`arr[(a + b)..]`, not `arr[a + b..]`, which reparses
        // as `arr[a + (b..)]` — CS0019). Operand() wraps non-atoms and leaves atoms
        // (including nested ranges and `^x`) bare, matching IndexFromEnd below.
        RangeExpression r => $"{(r.HasStart ? Operand(r.Start!) : "")}..{(r.HasEnd ? Operand(r.End!) : "")}",
        IndexFromEnd i => $"^{Operand(i.Offset)}",
        LoadElement e when MultiDimArrayElementText(e) is { } text => text,
        LoadElement e => $"{Operand(e.Array)}[{Expression(e.Index)}]",
        NewArray n => ArrayCreationText(n.ElementType, [n.Length]),
        SpanLiteral s => $"new {TypeText(s.ElementType)}[] {{ {string.Join(", ", s.Elements.Select(Expression))} }}",
        ArrayLiteral a => $"new {TypeText(a.ElementType)}[] {{ {string.Join(", ", a.Elements.Select(Expression))} }}",
        CollectionExpression c => $"[{string.Join(", ", c.Elements.Select(CollectionElementText))}]",
        CollectionSpreadElement s => $"..{Expression(s.Source)}",
        InlineArraySpanConversion c => $"({TypeText(c.SpanType)}){Deref(c.Place)}",
        StackAllocate s => $"stackalloc byte[{Expression(s.Size)}]",
        StackAllocArray s => s.HasInitializer
            ? $"stackalloc {TypeText(s.ElementType)}[] {{ {string.Join(", ", s.Elements.ToArray().Select(e => Expression((IrExpression)e)))} }}"
            : $"stackalloc {TypeText(s.ElementType)}[{Expression(s.Count)}]",
        Box b => CoerceText(b.Operand, b.Type),
        IsInstance i => $"{Operand(i.Operand)} {(IsValueTypeTarget(i.Type) ? "is" : "as")} {TypeText(i.Type)}",
        IsPattern p => $"{TypeTestValueText(p.Value)} is {TypeText(p.Type)} {LocalName(p.LocalIndex)}",
        RecursivePropertyDeclarationPattern p => $"{Operand(p.Value)} is {{ {p.PropertyName}: {TypeText(p.PatternType)} {LocalName(p.LocalIndex)} }}",
        SingleElementListPattern p => $"{Operand(p.Value)} is [{ListPatternAlternativesText(p)}]",
        PositionalPattern p => PositionalPatternText(p),
        CastClass c => $"({TypeText(c.Type)}){Operand(c.Operand)}",
        UnboxAny u => $"({TypeText(u.Type)}){UnboxAnyOperand(u)}",
        Unbox u => $"ref ({TypeText(u.Type)}){Operand(u.Operand)}",
        LoadLocalAddress a => $"ref {LocalName(a.Index)}",
        LoadArgumentAddress a => $"ref {CSharpNaming.EscapeIdentifier(a.Name)}",
        LoadFieldAddress f => $"ref {FieldTarget(f.Field, f.Instance)}",
        LoadElementAddress e when MultiDimArrayElementAddressText(e) is { } text => $"ref {text}",
        LoadElementAddress e => $"ref {Operand(e.Array)}[{Expression(e.Index)}]",
        LoadIndirect l => DerefLoad(l),
        SizeOf s => $"sizeof({TypeText(s.Type)})",
        TypeOf t => $"typeof({TypeOfTypeText(t.Type)})",
        LoadToken t => t.Kind == RuntimeTokenKind.Type && t.Type is not null
            ? $"typeof({TypeOfTypeText(t.Type)})"
            : TokenPlaceholder(t),
        CaughtException => "__exception",
        UnsupportedNode u => $"/* {u.Describe()} */",
        _ => $"/* {node.Describe()} */",
    };

    static string TokenPlaceholder(LoadToken token)
        => token.Kind switch
        {
            RuntimeTokenKind.Field => $"/* {token.Describe()} */ default(System.RuntimeFieldHandle)",
            RuntimeTokenKind.Method => $"/* {token.Describe()} */ default(System.RuntimeMethodHandle)",
            _ => $"/* {token.Describe()} */ null",
        };

    string CoalesceText(Coalesce co, TypeRef? target = null)
    {
        TypeRef? coalesceTarget = target ?? NullableValueType(co.Left.ResultType) ?? co.ResultType;
        // Single-arm join decision for the right side (the #2306 sibling rule):
        // a bare-safe right renders untouched, a non-safe one is spelled —
        // with the node's source type threaded like the conditional/switch
        // consumers (#2345 round-2 threading discipline).
        TypeRef? primitiveCoercionSourceType = null;
        if (coalesceTarget is { } integerTarget && TypeFamilies.IsIntegerLike(integerTarget))
        {
            coalesceTarget = EffectiveJoinTarget(integerTarget, [co.Right]);
            primitiveCoercionSourceType =
                coalesceTarget is not null
                && EffectiveType(co) is { } nodeType
                && !nodeType.Equals(coalesceTarget)
                && CanRenderPrimitiveJoinForTarget(coalesceTarget, nodeType, [co.Right])
                    ? nodeType
                    : null;
        }
        return $"{CoalesceLeftText(co.Left)} ?? {CoalesceRightText(co.Right, coalesceTarget, primitiveCoercionSourceType)}";
    }

    string CoalesceRightText(IrExpression right, TypeRef? target, TypeRef? primitiveCoercionSourceType = null)
        // The `??` right operand demands NullCoalescing (`??` binds tighter
        // than `?:`), so a Conditional-precedence fragment — the bool→int
        // composition, a stale-`Coerce` re-target, a bare conditional right —
        // wraps by the one precedence rule (#2376 phase 1; replaces the #2345
        // rounds 3-8 string scanner). Conditional/switch arms carry no such
        // demand — their `:`/`=>` delimiters already bracket the ternary.
        => CoalesceRightRendered(right, target, primitiveCoercionSourceType).At(Precedence.NullCoalescing);

    Rendered CoalesceRightRendered(IrExpression right, TypeRef? target, TypeRef? primitiveCoercionSourceType)
        => TryCoerceJoinArmRendered(right, target, primitiveCoercionSourceType) is { } coerced
            ? coerced
            // The bool-arm composition, mirroring ConditionalArm and
            // SwitchArmValueText (the #2145 one-rule-in-all-three discipline).
            : target is { } intTarget && TypeFamilies.IsIntegerLike(intTarget)
                && EffectiveType(right) is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
                ? BoolToInteger(right, intTarget)
                // Operand() parenthesizes every non-atom itself (Conditional
                // included), so its output is always effectively primary — the
                // loose fragments reach this context only through the two
                // branches above, which report their own precedence.
                : Rendered.Primary(Operand(right));

    static TypeRef? NullableValueType(TypeRef? type)
        => type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" },
            TypeArguments: [var value],
        }
            ? value
            : null;

    string CoalesceLeftText(IrExpression expression)
        => expression is LoadIndirect
        {
            Type:
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" },
            },
            Address.ResultType.Kind: TypeRefKind.ByRef,
        } load
            ? DerefLoad(load)
            // `??` is right-associative, so the left operand is the equal-precedence
            // hazard side: `(a ?? b) ?? c` must not render as `a ?? b ?? c`.
            : RenderedExpression(expression).At(TighterThan(Precedence.NullCoalescing));

    /// <summary>Conditions render brtrue's raw value as-is; LogicalNot over a comparison folds via the shared type-aware duals (float folds flip the unordered flag).</summary>
    string Condition(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: Comparison c } => ComparisonText(
            Conditions.Inverse(c.Kind),
            IsFloatComparison(c.Left, c.Right) ? !c.IsUnsigned : c.IsUnsigned,
            c.Left, c.Right),
        LogicalNot { Operand: LogicalBinary logical } when TryPropertyPatternText(logical, negated: true) is { } negatedPattern => negatedPattern,
        LogicalNot { Operand: Call { Callee.Name: "op_True", Arguments: [var value] } } => InvertedUserTruthiness(value),
        LogicalNot { Operand: Call { Callee.Name: "op_False", Arguments: [var value] } } => OperatorOperand(value),
        // brtrue/brfalse test any I4/ref value; C# conditions need bool —
        // non-bool operands spell the comparison the branch performs.
        LogicalNot { Operand: { } operand } when Truthiness(operand) is { } negated => negated.Inverted,
        LogicalNot n => $"!{Operand(n.Operand)}",
        Call { Callee.Name: "op_True", Arguments: [var value] } => OperatorOperand(value),
        Call { Callee.Name: "op_False", Arguments: [var value] } => InvertedUserTruthiness(value),
        _ when Truthiness(condition) is { } truthy => truthy.Direct,
        _ => Expression(condition),
    };

    string InvertedUserTruthiness(IrExpression value)
        => $"({OperatorOperand(value)} ? false : true)";

    string TypeTestValueText(IrExpression value)
        => UnionValueReceiverText(value) ?? Operand(value);

    string? UnionValueReceiverText(IrExpression value)
        => value is LoadProperty property ? UnionValueReceiverText(property) : null;

    string? ValueTypeUnionValueReceiverText(IrExpression value)
        => value is LoadProperty property
            && IsValueTypeTarget(NamedDefinition(property.Accessor.DeclaringType))
            ? UnionValueReceiverText(property)
            : null;

    string? UnionValueReceiverText(LoadProperty property)
    {
        if (property.PropertyName != "Value"
            || property.IndexArguments.Count != 0
            || !_function.UnionTypes.Contains(NamedDefinition(property.Accessor.DeclaringType)))
        {
            return null;
        }

        return property.Instance switch
        {
            LoadArgumentAddress argument => CSharpNaming.EscapeIdentifier(argument.Name),
            LoadArgument argument => CSharpNaming.EscapeIdentifier(argument.Name),
            LoadLocalAddress local => LocalName(local.Index),
            LoadLocal local => LocalName(local.Index),
            LoadFieldAddress field => FieldTarget(field.Field, field.Instance),
            LoadField field => FieldTarget(field.Field, field.Instance),
            _ => null,
        };
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;

    /// <summary>
    /// Spellings for a non-bool branch operand: <c>!= 0</c> for integers and
    /// enums, <c>is null</c>/<c>is not null</c> for reference shapes. The
    /// operand is a <c>brfalse</c>/<c>brtrue</c> value, so the CLI constrains
    /// it to int, native int, object reference, or managed pointer — never a
    /// struct value. A generic instance is therefore always a reference type
    /// (generic value types cannot be branch operands, and enums are never
    /// generic), so it null-tests with no resolution. A bare definition is
    /// reference-or-enum; signature CLASS/VALUETYPE hints and the importer's
    /// same-assembly shape resolution tell them apart where they can, and an
    /// unresolved definition with neither hint still prints raw rather than guess.
    /// </summary>
    (string Direct, string Inverted)? Truthiness(IrExpression operand)
    {
        // An `isinst T` tested as a branch condition is the C# type-test operator
        // `obj is T` — valid for any target, reference or value. Spelling it with
        // `as` (the value-context form) is CS0077 on a non-nullable value type
        // whose shape the printer could not resolve (e.g. a cross-assembly struct
        // like Guid or BigInteger), and even for a reference type a bare `obj as T`
        // is not a bool. The pattern is already its own truth value — wrapping it
        // in `!= 0` would be `bool != int` (CS0019); the inverse negates it.
        if (operand is IsInstance ii)
        {
            string valueText = TypeTestValueText(ii.Operand);
            string typeText = TypeText(ii.Type);
            return ($"{valueText} is {typeText}", $"{valueText} is not {typeText}");
        }

        if (operand is IsPattern pattern)
        {
            string valueText = TypeTestValueText(pattern.Value);
            string typeText = TypeText(pattern.Type);
            string direct = $"{valueText} is {typeText} {LocalName(pattern.LocalIndex)}";
            string inverted = ReferenceOwnership.LocalReferencesOnlyWithin(_function, pattern.LocalIndex, [pattern])
                ? $"{valueText} is not {typeText}"
                : $"!({direct})";
            return (direct, inverted);
        }

        // A `ref bool`/`bool*` deref loads via `ldind.u1`, so its IR ResultType is
        // `byte`, but it renders as the C# bool place (`flag`/`*p`). Spelling it
        // `flag != 0`/`flag == 0` is `bool != int` (CS0019); it is its own truth
        // value, so let it render bare/negated like any other boolean.
        if (RendersAsBoolean(operand))
            return null;

        var type = operand.ResultType;
        if (type is null || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return null;

        string text = ValueTypeUnionValueReceiverText(operand) ?? Operand(operand);
        (string, string) reference = ($"{text} is not null", $"{text} is null");
        (string, string) integer = ($"{text} != 0", $"{text} == 0");

        // A bitwise/shift Binary is provably integral — these IL ops only operate
        // on integer or enum operands — even when its nominal result type is a
        // cross-assembly enum (e.g. TypeAttributes) the printer cannot resolve to
        // a stack family or a TypeShape. Spell the branch test `(expr) != 0`
        // rather than leaving a non-bool bitwise expression bare (CS0019). A
        // genuine bool `&`/`|` was filtered by the Boolean guard above.
        if (operand is Binary { Kind: BinaryKind.And or BinaryKind.Or or BinaryKind.Xor or BinaryKind.ShiftLeft or BinaryKind.ShiftRight })
            return integer;

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

        switch (type.DeclaredValueTypeHint)
        {
            case ValueTypeHint.ReferenceType:
                return reference;
            case ValueTypeHint.ValueType:
                return integer;
        }

        return _function.TypeShapes.GetValueOrDefault(type) switch
        {
            TypeShape.Reference => reference,
            TypeShape.Enum => integer,
            // A cross-assembly type is unresolved (Unknown shape): an interface like
            // IDisposable or a framework class is indistinguishable from a framework
            // enum by its TypeRef alone. Fall back to provenance — a value produced
            // by `isinst`/`as` is always a reference (or null), so its truthiness is
            // `is null`/`is not null`, never `!x` (CS0023). Spelling the integer
            // `!= 0` form for a genuine cross-assembly enum is handled above by the
            // operand's resolved type, not by this branch-operand provenance.
            _ => ProducesReference(operand) ? reference : null,
        };
    }

    /// <summary>True when the branch operand provably holds a reference because its sole definition is an <c>isinst</c>/<c>as</c> (which yields a reference or null). Sees through a single-store stack slot or local the value was spilled to.</summary>
    bool ProducesReference(IrExpression operand) => SoleDefinition(operand) is IsInstance;

    IrExpression? SoleDefinition(IrExpression operand)
    {
        switch (operand)
        {
            case IsInstance:
                return operand;
            case LoadStackSlot load:
            {
                // Scope to the current function body: stack-slot numbers are
                // per-imported-function, so a nested local function / lambda can
                // reuse this slot independently and must not count as a second
                // definition (that would disable the provenance and reprint `!x`).
                var stores = DescendantsOutsideNestedFunctions(_function).OfType<StoreStackSlot>().Where(s => s.Slot == load.Slot).ToList();
                return stores.Count == 1 ? stores[0].Value : null;
            }
            case LoadLocal load:
            {
                var stores = DescendantsOutsideNestedFunctions(_function).OfType<StoreLocal>().Where(s => s.Index == load.Index).ToList();
                return stores.Count == 1 ? stores[0].Value : null;
            }
            default:
                return null;
        }
    }

    // `box T; unbox.any U` is the generic-math `(U)(object)x` idiom: the box is an
    // explicit (object) cast, not the transparent implicit boxing of a value into an
    // object slot. `(U)x` over a generic type parameter has no direct conversion and
    // is CS0030 — and even for a concrete type, collapsing box+unbox.any to a plain
    // `(U)x` drops the round-trip the IL actually performs. Keep the intermediary.
    string UnboxAnyOperand(UnboxAny unbox)
    {
        if (unbox.Operand is Box box)
            return $"(object){Operand(box.Operand)}";
        // `isinst T; unbox.any T` on the exact same target is csc's unconstrained/
        // struct-constrained declaration-pattern extraction (#2831): `if (x is T t)`
        // cannot store the isinst result through an `as T` local (illegal for a
        // non-class-constrained T), so csc re-tests and unboxes the value inline
        // instead. The general IsInstance expression printer would then spell this
        // nested test through `is`/`as` — `is` renders a bool (can't cast to T,
        // CS0030), and `as` is CS0413 for a non-class-constrained T — so neither
        // choice is valid here regardless of `IsValueTypeTarget`. Only when the
        // exact same test is proven to have already succeeded (an enclosing
        // `IfStatement` whose condition is the identical, side-effect-free test,
        // with this site inside its `Then` and no intervening write to the tested
        // value) is `isinst` redundant: the extraction then behaves exactly like the
        // box+unbox.any object-bridge idiom above, so it renders the same way. Off
        // that proven path the shape is left to fall through unchanged rather than
        // hide it behind an always-succeeds cast that could rewrite failure
        // semantics (NullReferenceException/false vs. a silently different throw).
        if (unbox.Operand is IsInstance sameTargetTest
            && sameTargetTest.Type.Equals(unbox.Type)
            && IsProvenSuccessfulTypeTest(unbox, sameTargetTest))
        {
            return $"(object){Operand(sameTargetTest.Operand)}";
        }
        if (NeedsObjectBridgeForGenericUnbox(unbox.Type, unbox.Operand.ResultType))
            return $"(object){Operand(unbox.Operand)}";
        return Operand(unbox.Operand);
    }

    /// <summary>
    /// True when <paramref name="site"/> (an <see cref="UnboxAny"/> whose operand is
    /// <paramref name="test"/>) sits inside the guaranteed-true arm of an
    /// <see cref="IfStatement"/> that already proved the identical test, or — when
    /// <see cref="StructuringPass"/> left the region flat — behind an equivalent
    /// <see cref="ConditionalBranch"/> guard reached only on the successful path (see
    /// <see cref="IsProvenByFlatGuard"/>). Both proofs are narrow by design: the
    /// guard's test must be the same type test (same target type, structurally
    /// identical operand) via <see cref="SameTestedValue"/>, the site must be
    /// reachable only through the guard's successful edge (never its failed arm —
    /// the structured proof's <c>Else</c>, or the flat proof's untaken branch), no
    /// loop lies between the guard and the site for the structured proof (a later
    /// iteration could have reassigned the tested value since the guard last ran),
    /// and no store between the guard and the site can have reassigned a local the
    /// tested operand reads. A nested lambda/local-function boundary ends the
    /// structured search: its captured value can diverge from the enclosing scope's
    /// at invocation time.
    /// </summary>
    static bool IsProvenSuccessfulTypeTest(IrNode site, IsInstance test)
    {
        if (!IsReadOnlyOperand(test.Operand))
            return false;
        if (HasAddressTaken(site, test.Operand))
            return false;

        for (var current = site.Parent; current is not null; current = current.Parent)
        {
            if (current is Lambda or LocalFunctionStatement)
                break;
            if (current is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement)
                break;
            if (current is not IfStatement ifStatement)
                continue;
            if (ifStatement.Condition is not IsInstance guard
                || !guard.Type.Equals(test.Type)
                || !SameTestedValue(guard.Operand, test.Operand))
            {
                continue;
            }
            if (!ReferenceOwnership.IsInside(site, ifStatement.Then))
                return false;

            return !HasInterveningWriteAlongPath(ifStatement.Then, site, test.Operand);
        }
        return IsProvenByFlatGuard(site, test);
    }

    /// <summary>
    /// The flat-CFG counterpart of the structured proof above: real-world
    /// declaration-pattern extractions frequently sit in a ternary-shaped merge
    /// region <see cref="StructuringPass"/> left as raw blocks and a
    /// <see cref="ConditionalBranch"/> rather than an <see cref="IfStatement"/>
    /// (e.g. <c>T x = cond ? isinst-extract : default;</c>). The proof mirrors the
    /// structured one over the block graph instead of the statement tree: the
    /// site's enclosing <see cref="Block"/> must be reached, via <see cref="Cfg"/>
    /// edges, from exactly one predecessor — a block whose terminating
    /// <see cref="ConditionalBranch"/> tests the identical value and only reaches
    /// the site's block on the branch's successful arm (fall-through past a
    /// negated test, or the jump target of an unnegated one). An unmodeled edge
    /// (an EH leave, or a branch target outside the container) fails the proof
    /// rather than being reasoned about, matching <see cref="DefiniteAssignment"/>'s
    /// conservative bail on the same shapes.
    /// </summary>
    static bool IsProvenByFlatGuard(IrNode site, IsInstance test)
    {
        IrNode? current = site.Parent;
        while (current is not null && current is not Block)
            current = current.Parent;
        if (current is not Block siteBlock || siteBlock.Parent is not BlockContainer container)
            return false;

        var guardedStatement = TopLevelStatementWithin(site, siteBlock);
        if (guardedStatement is null
            || HasInterveningWrite(siteBlock, guardedStatement.ChildIndex, test.Operand))
        {
            return false;
        }

        var blocks = container.Blocks;
        int siteIndex = -1;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], siteBlock))
            {
                siteIndex = i;
                break;
            }
        }
        // The container's entry block also has an implicit predecessor from
        // outside the region. Cfg models only intra-container edges, so it
        // cannot prove that a backedge into block zero dominates the first
        // execution of that block.
        if (siteIndex <= 0)
            return false;

        var edges = Cfg.Build(blocks);
        if (edges.Any(e => e.LeavesRegion || e.ExternalTargets.Count > 0))
            return false;

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i == siteIndex || blocks[i].Children.Count == 0)
                continue;
            if (blocks[i].Children[^1] is not ConditionalBranch branch)
                continue;

            bool reachesSiteOnSuccess = branch.Condition switch
            {
                // `if (x is not T) goto Fail;` — fall-through (i+1) is the true path.
                // The failure jump must land somewhere else: a target that (degenerately)
                // coincides with the fall-through would mean the guard reaches the site
                // on failure too, so the test would not actually gate the extraction.
                LogicalNot { Operand: IsInstance negatedGuard }
                    when negatedGuard.Type.Equals(test.Type) && SameTestedValue(negatedGuard.Operand, test.Operand)
                    => i + 1 == siteIndex && branch.TargetOffset != siteBlock.StartOffset,
                // `if (x is T) goto Success;` — the jump target is the true path. The
                // fall-through must land somewhere else for the same reason: a
                // fall-through that (degenerately) coincides with the jump target
                // would mean the guard reaches the site on failure too.
                IsInstance guard
                    when guard.Type.Equals(test.Type) && SameTestedValue(guard.Operand, test.Operand)
                    => branch.TargetOffset == siteBlock.StartOffset && i + 1 != siteIndex,
                _ => false,
            };
            if (!reachesSiteOnSuccess)
                continue;

            if (HasSinglePredecessor(edges, siteIndex, i))
                return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="targetIndex"/> is reached from exactly one block in the container, and that block is <paramref name="expectedSourceIndex"/> — the single-entry requirement for the flat-guard proof (no other path into the guarded block could skip the test).</summary>
    static bool HasSinglePredecessor(IReadOnlyList<BlockEdges> edges, int targetIndex, int expectedSourceIndex)
    {
        int count = 0;
        bool matchesExpected = false;
        for (int i = 0; i < edges.Count; i++)
        {
            if (!edges[i].Successors.Contains(targetIndex))
                continue;
            count++;
            if (i == expectedSourceIndex)
                matchesExpected = true;
        }
        return count == 1 && matchesExpected;
    }

    /// <summary>A read with no observable side effect: a local/argument load or a box over one. Matches the exact vocabulary csc emits for a cached declaration-pattern subject (never a call, allocation, or property getter).</summary>
    static bool IsReadOnlyOperand(IrExpression value) => value switch
    {
        LoadLocal or LoadArgument or Constant => true,
        Box box => IsReadOnlyOperand(box.Operand),
        _ => false,
    };

    /// <summary>Structural equality over the narrow <see cref="IsReadOnlyOperand"/> vocabulary — enough to recognize csc's duplicated read of the one tested value, not a general expression comparer.</summary>
    static bool SameTestedValue(IrExpression a, IrExpression b) => (a, b) switch
    {
        (LoadLocal x, LoadLocal y) => x.Index == y.Index && x.Type.Equals(y.Type),
        (LoadArgument x, LoadArgument y) => x.Index == y.Index && x.Type.Equals(y.Type),
        (Constant x, Constant y) => Equals(x.Value, y.Value) && x.Type.Equals(y.Type),
        (Box x, Box y) => x.Type.Equals(y.Type) && SameTestedValue(x.Operand, y.Operand),
        _ => false,
    };

    /// <summary>The direct child of <paramref name="scope"/> that contains <paramref name="node"/> — the statement whose position within the guarded block bounds the intervening-write scan.</summary>
    static IrNode? TopLevelStatementWithin(IrNode node, Block scope)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, scope))
                return current;
        }
        return null;
    }

    /// <summary>
    /// Whether any preceding sibling subtree on the path from
    /// <paramref name="guardedBlock"/> to <paramref name="site"/> could have
    /// reassigned a local or argument the tested value reads.
    /// </summary>
    static bool HasInterveningWriteAlongPath(Block guardedBlock, IrNode site, IrExpression value)
    {
        for (IrNode current = site; current.Parent is { } parent; current = parent)
        {
            if (HasInterveningWrite(parent, current.ChildIndex, value))
                return true;
            if (ReferenceEquals(parent, guardedBlock))
                return false;
        }
        return true;
    }

    /// <summary>Whether any child subtree in <paramref name="scope"/> before index <paramref name="beforeChildIndex"/> could have reassigned a local/argument the read-only <paramref name="value"/> depends on.</summary>
    static bool HasInterveningWrite(IrNode scope, int beforeChildIndex, IrExpression value)
    {
        var locals = new List<int>();
        var arguments = new List<int>();
        CollectReadOnlyReferences(value, locals, arguments);
        if (locals.Count == 0 && arguments.Count == 0)
            return false;

        for (int i = 0; i < beforeChildIndex && i < scope.Children.Count; i++)
        {
            foreach (var node in DescendantsAndSelfOutsideNestedFunctions(scope.Children[i]))
            {
                if (node is StoreLocal store && locals.Contains(store.Index))
                    return true;
                if (node is StoreArgument storeArgument && arguments.Contains(storeArgument.Index))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the tested local or argument has any managed-address use in its
    /// function scope. Once an address exists, a later indirect write can
    /// mutate the value without a direct <see cref="StoreLocal"/> or
    /// <see cref="StoreArgument"/> between the guard and extraction.
    /// </summary>
    static bool HasAddressTaken(IrNode site, IrExpression value)
    {
        var locals = new List<int>();
        var arguments = new List<int>();
        CollectReadOnlyReferences(value, locals, arguments);
        if (locals.Count == 0 && arguments.Count == 0)
            return false;

        foreach (var node in DescendantsAndSelfOutsideNestedFunctions(EnclosingFunctionBody(site)))
        {
            if (node is LoadLocalAddress address && locals.Contains(address.Index))
                return true;
            if (node is LoadArgumentAddress argumentAddress && arguments.Contains(argumentAddress.Index))
                return true;
        }
        return false;
    }

    static IrNode EnclosingFunctionBody(IrNode site)
    {
        for (IrNode? current = site; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case Lambda lambda:
                    return lambda.Body;
                case LocalFunctionStatement localFunction:
                    return localFunction.Body;
                case IrFunction function:
                    return function.Body;
            }
        }
        return site;
    }

    static void CollectReadOnlyReferences(IrExpression value, List<int> locals, List<int> arguments)
    {
        switch (value)
        {
            case LoadLocal load:
                locals.Add(load.Index);
                break;
            case LoadArgument argument:
                arguments.Add(argument.Index);
                break;
            case Box box:
                CollectReadOnlyReferences(box.Operand, locals, arguments);
                break;
        }
    }

    bool NeedsObjectBridgeForGenericUnbox(TypeRef target, TypeRef? source)
    {
        if (target.Kind is not (TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter)
            || source is null
            || source is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Object" })
        {
            return false;
        }

        return TypeFamilies.Of(source) == StackFamily.O
            || source.Kind is TypeRefKind.SzArray or TypeRefKind.Array
            || source.DeclaredValueTypeHint == ValueTypeHint.ReferenceType
            || _function.TypeShapes.GetValueOrDefault(source) == TypeShape.Reference;
    }

    /// <summary>
    /// True when an expression renders as a C# <c>bool</c> regardless of its IR
    /// ResultType. A <c>ref bool</c>/<c>bool*</c> deref loads via <c>ldind.u1</c>
    /// (ResultType <c>byte</c>) but the printer spells it as the underlying bool
    /// place, so a branch over it must negate rather than compare to 0.
    /// </summary>
    bool RendersAsBoolean(IrExpression operand)
        => operand is LoadIndirect { Address.ResultType: { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer, ElementType: { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary } } }
            // A bool-in-int-slot load whose slot unified to bool (its declaration is
            // `bool S = …`) renders as a bool place: spelling `S == 0`/`S != 0`
            // would be `bool == int` (CS0019), so it is its own truth value (#2377).
            || (operand is LoadStackSlot load && TypeFamilies.IsBoolean(StackSlotRenderType(load.Slot, load.Type)));

    Rendered RenderedExpression(IrExpression node)
    {
        string text = Expression(node);
        if (node is Call call && OperatorCallPrecedence(call) is { } operatorPrecedence)
            return new Rendered(text, operatorPrecedence);
        if (IsWholeExpressionWrapper(text, "checked(") || IsWholeExpressionWrapper(text, "unchecked("))
            return Rendered.Primary(text);
        if (node is Coerce && IsSimpleAtomText(text))
            return Rendered.Primary(text);
        return new Rendered(text, CSharpPrecedence.Of(node));
    }

    /// <summary>Parenthesizes compound operands; leaves atoms bare. Conservative until the precedence visitor exists.</summary>
    string Operand(IrExpression node)
    {
        string text = Expression(node);
        // A Call is an atom only when it spells as a method invocation; an
        // operator-spelled call (op_Inequality → `a != b`, op_UnaryNegation →
        // `-x`) renders as a compound expression, so it must parenthesize like
        // any other binary/unary — otherwise an enclosing `!`/`-`/binary
        // misbinds to its first operand (e.g. `!a != b`, CS0023).
        bool atomic = node is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField
            or NewObject or ArrayLength or LoadElement or SliceExpression or RangeExpression or CaughtException or SizeOf or LoadToken
            or LoadProperty or TypeOf or DelegateCreation or InterpolatedStringExpression or TupleExpression or AnonymousObject or ObjectInitializerExpression or WithExpression or InitializerBlock or IndexFromEnd or CallIndirect or AddressOfMethod or NullConditional
            or IncrementDecrement or SpanLiteral or ArrayLiteral or CollectionExpression or CollectionSpreadElement
            || node is Call call && !IsOperatorCall(call)
            // A Binary/Convert that renders as a whole-expression `checked(...)`/
            // `unchecked(...)` is a C# primary expression: the wrapper's own parens
            // already bracket it, so an enclosing operator never misbinds and a
            // second pair (`a + (unchecked(b * 2))`) is pure noise. The wrapper must
            // span the ENTIRE text — a child cast can contribute a leading
            // `unchecked(` (`unchecked((uint)b) / unchecked((uint)c)`) without
            // bracketing the whole expression, and dropping its parens would misbind.
            || node is Binary or Convert or Coerce
                && (IsWholeExpressionWrapper(text, "checked(") || IsWholeExpressionWrapper(text, "unchecked("))
            // A Coerce that rendered as a member name or bare literal is an
            // atom; its cast and `cond ? 1 : 0` forms are NOT — a cast as a
            // member-access receiver misbinds onto the call result
            // (`(E)x.M()` is `(E)(x.M())`), so those keep Operand's parens.
            || node is Coerce && IsSimpleAtomText(text);
        atomic = atomic || node is LoadIndirect load && PointerElementAccessText(load) is not null;
        return atomic ? text : $"({text})";
    }

    string CollectionElementText(IrExpression element)
        => element is CollectionSpreadElement spread ? $"..{Expression(spread.Source)}" : Expression(element);

    /// <summary>
    /// True when rendered text is a bare identifier chain or non-negative
    /// numeric literal (`LEnum.High`, `10`) — safe unparenthesized in any
    /// operand position, including as a member-access receiver. A leading
    /// minus is deliberately NOT an atom: `-1` as a receiver misbinds
    /// (`-1.ToString()` negates the call), so negative literals keep
    /// Operand's parens (#2145).
    /// </summary>
    static bool IsSimpleAtomText(string text)
    {
        if (text.Length == 0)
            return false;
        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('.' or '_' or '@'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="text"/> is a single <paramref name="prefix"/>-wrapped
    /// expression (e.g. <c>unchecked(...)</c>) whose opening paren matches the final
    /// character — so the wrapper brackets the whole expression. A text that merely
    /// starts with the prefix because a child contributed it
    /// (<c>unchecked((uint)b) / unchecked((uint)c)</c>) returns false. Paren counting
    /// is conservative under string/char literals: a miscount only ever yields false
    /// (keep parens), never a wrong true.
    /// </summary>
    static bool IsWholeExpressionWrapper(string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length == 0 || text[^1] != ')')
            return false;
        int depth = 0;
        for (int i = prefix.Length - 1; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return i == text.Length - 1;
        }
        return false;
    }

    // A `&Method` operand cannot be invoked directly — `(&Method)(x)` is invalid
    // C# (CS0149) — so cast it to its delegate* result type first. The
    // CallIndirectSpellabilityPass guarantees the result type is a matching
    // delegate* before such a node survives to print.
    string FunctionPointerOperand(IrExpression pointer)
        => pointer is AddressOfMethod
            ? pointer.ResultType is { Kind: TypeRefKind.FunctionPointer } fp
                ? $"(({TypeText(fp)}){Expression(pointer)})"
                : $"({Expression(pointer)})"
            : Operand(pointer);

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
        // `this` in a value-type instance method is a managed pointer to the
        // value (IL's `ldarg.0` pushes `T&`), but C#'s `this` already denotes
        // the value, so reading through it is just `this` — never `*this`,
        // which is CS0193 (DeclaringType is never an unmanaged pointer).
        LoadArgument { Index: 0, Name: "this" } => "this",
        LoadLocalAddress a => $"{LocalName(a.Index)}",
        LoadArgumentAddress a => CSharpNaming.EscapeIdentifier(a.Name),
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
            => $"({RenderedCondition(c.Condition).At(Precedence.NullCoalescing)} ? ref {Deref(c.WhenTrue)} : ref {Deref(c.WhenFalse)})",
        { ResultType.Kind: TypeRefKind.ByRef } => Operand(address),
        _ => $"*{Operand(address)}",
    };

    /// <summary>
    /// Renders a <c>ldind.&lt;T&gt;</c> read. Most addresses go through
    /// <see cref="Deref"/>, but when the address was reinterpreted to a native
    /// integer (<c>ldarga; conv.u; ldind.u1</c> — the generic
    /// reinterpret-then-read idiom, e.g. <c>Enum.IsDefinedPrimitive&lt;byte&gt;</c>),
    /// <see cref="Deref"/> would render <c>*((nuint)(&amp;value))</c> — a deref of
    /// an integer, CS0193. The faithful unsafe spelling reinterprets the address
    /// as the read's own pointer type and derefs it: <c>*(T*)(&amp;value)</c>. The
    /// <c>(T*)</c> cast subsumes the <c>conv.u</c>, so the read recompiles to the
    /// same <c>ldind</c>.
    /// </summary>
    string DerefLoad(LoadIndirect load)
    {
        if (PointerElementAccessText(load) is { } indexed)
            return indexed;
        if (load.Type is { } element
            && load.Address is Convert { Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" } } conv)
        {
            // An address-of operand keeps its `&place` unsafe spelling (mirroring
            // ConvertText); any other operand is already a pointer/integer value.
            string addr = conv.Operand is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress
                ? $"(&{Deref(conv.Operand)})"
                : Operand(conv.Operand);
            return $"*({TypeText(element)}*){addr}";
        }
        if (load.Type is { } nativeElement && IsNativeInteger(load.Address.ResultType))
            return NativeIntPointerDeref(load.Address, nativeElement);
        return Deref(load.Address);
    }

    string? PointerElementAccessText(LoadIndirect load)
    {
        if (FixedBufferElementAccessText(load) is { } fixedBufferAccess)
            return fixedBufferAccess;

        if (load.Type is not { } element
            || load.Address is not Binary { Kind: BinaryKind.Add } address
            || !TrySplitPointerAdd(address, out var pointer, out var offset)
            || pointer.ResultType is not { Kind: TypeRefKind.Pointer, ElementType: { } pointerElement }
            || !pointerElement.Equals(element)
            || !TryScaledPointerIndex(offset, pointerElement, out var index))
        {
            return null;
        }

        return $"{Operand(pointer)}[{Expression(index)}]";
    }

    string? FixedBufferElementAccessText(LoadIndirect load)
    {
        if (load.Type is not { } element
            || load.Address is not Binary { Kind: BinaryKind.Add } address
            || !TrySplitFixedBufferElementAdd(address, out var elementFieldAddress, out var offset)
            || !elementFieldAddress.Field.Type.Equals(element)
            || !TryScaledPointerIndex(offset, element, out var index))
        {
            return null;
        }

        return $"System.Runtime.CompilerServices.Unsafe.Add(ref {FieldTarget(elementFieldAddress.Field, elementFieldAddress.Instance)}, {Expression(index)})";
    }

    static bool TrySplitFixedBufferElementAdd(Binary add, out LoadFieldAddress fieldAddress, out IrExpression offset)
    {
        if (add.Left is LoadFieldAddress left && IsFixedBufferElementAddress(left))
        {
            fieldAddress = left;
            offset = add.Right;
            return true;
        }
        if (add.Right is LoadFieldAddress right && IsFixedBufferElementAddress(right))
        {
            fieldAddress = right;
            offset = add.Left;
            return true;
        }

        fieldAddress = null!;
        offset = add.Right;
        return false;
    }

    static bool IsFixedBufferElementAddress(LoadFieldAddress address)
        => address.Field.Name == "FixedElementField"
            && address.Instance is LoadFieldAddress;

    static bool TrySplitPointerAdd(Binary add, out IrExpression pointer, out IrExpression offset)
    {
        if (add.Left.ResultType is { Kind: TypeRefKind.Pointer } && add.Right.ResultType is not { Kind: TypeRefKind.Pointer })
        {
            pointer = add.Left;
            offset = add.Right;
            return true;
        }
        if (add.Right.ResultType is { Kind: TypeRefKind.Pointer } && add.Left.ResultType is not { Kind: TypeRefKind.Pointer })
        {
            pointer = add.Right;
            offset = add.Left;
            return true;
        }

        pointer = add.Left;
        offset = add.Right;
        return false;
    }

    static bool TryScaledPointerIndex(IrExpression offset, TypeRef elementType, out IrExpression index)
    {
        if (ByteSize(elementType) is not { } elementSize)
        {
            index = offset;
            return false;
        }

        if (TryConstantMultiple(offset, elementSize, out var multiple))
        {
            index = multiple >= int.MinValue && multiple <= int.MaxValue
                ? new Constant((int)multiple, TypeRef.CoreLib("System", "Int32"))
                : new Constant(multiple, TypeRef.CoreLib("System", "Int64"));
            return true;
        }

        if (offset is Binary { Kind: BinaryKind.Multiply } multiply)
        {
            if (IsConstant(multiply.Left, elementSize))
            {
                index = NativeIntegerOperand(multiply.Right);
                return true;
            }
            if (IsConstant(multiply.Right, elementSize))
            {
                index = NativeIntegerOperand(multiply.Left);
                return true;
            }
        }

        if (elementSize == 1)
        {
            index = NativeIntegerOperand(offset);
            return true;
        }

        index = offset;
        return false;
    }

    static IrExpression NativeIntegerOperand(IrExpression expression)
        => expression is Convert { Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" }, Operand: { } operand }
            ? operand
            : expression;

    static bool IsConstant(IrExpression expression, int value)
        => expression is Constant { Value: int i } && i == value
            || expression is Constant { Value: long l } && l == value;

    static bool TryConstantMultiple(IrExpression expression, int divisor, out long multiple)
    {
        long value = expression switch
        {
            Constant { Value: int i } => i,
            Constant { Value: long l } => l,
            _ => 0,
        };
        if (expression is not Constant { Value: int or long } || divisor == 0 || value % divisor != 0)
        {
            multiple = 0;
            return false;
        }

        multiple = value / divisor;
        return true;
    }

    static int? ByteSize(TypeRef type)
        => type is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "Boolean" or "Byte" or "SByte" => 1,
                "Char" or "Int16" or "UInt16" => 2,
                "Int32" or "UInt32" or "Single" => 4,
                "Int64" or "UInt64" or "Double" => 8,
                _ => null,
            }
            : null;

    string IndirectTarget(IrExpression address, TypeRef? elementType)
        => elementType is not null && IsNativeInteger(address.ResultType)
            ? NativeIntPointerDeref(address, elementType)
            : Deref(address);

    string NativeIntPointerDeref(IrExpression address, TypeRef elementType)
        => $"*({TypeText(TypeRef.Pointer(elementType))}){Operand(address)}";

    string FreshSyntheticLocalName(string baseName)
    {
        var used = new HashSet<string>(
            _function.Signature.Parameters.Select(p => p.Name)
                .Concat(_function.LocalNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!)),
            StringComparer.Ordinal);
        string chosen = baseName;
        if (used.Contains(baseName))
        {
            for (int i = 0; ; i++)
            {
                string candidate = $"{baseName}{i}";
                if (!used.Contains(candidate))
                {
                    chosen = candidate;
                    break;
                }
            }
        }
        // Record every generated synthetic local so a self-static call whose name
        // collides with it (e.g. a suffixed `__stackalloc0`) stays qualified.
        _syntheticLocalNames.Add(chosen);
        return chosen;
    }

    static bool IsNativeInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" or "UIntPtr" };

    /// <summary>
    /// The C# type a store-indirect writes through. A primitive <c>stind</c>
    /// opcode carries its own signed type (<c>stind.i4</c> → <c>int</c>) which
    /// can contradict the pointer it writes through: <c>*(uint*)p = (int)x</c>
    /// is int→uint (CS0266), since the C# lvalue <c>*p</c> is typed by the
    /// pointer (<c>uint</c>), not the opcode. Prefer the element type rooted in
    /// the address — the faithful target — falling back to the opcode type when
    /// the address carries no pointer/managed-ref type (an untyped <c>stind</c>).
    /// </summary>
    TypeRef? IndirectStoreType(IrExpression address, TypeRef? opcodeType) => PointeeType(address) ?? opcodeType;

    /// <summary>The element a pointer/managed-ref address points at, seen through the additive pointer arithmetic (<c>p + i</c>) and conversions that an indexed store roots in.</summary>
    static TypeRef? PointeeType(IrExpression address) => address.ResultType switch
    {
        { Kind: TypeRefKind.Pointer or TypeRefKind.ByRef, ElementType: { } element } => element,
        _ => address switch
        {
            Binary { Kind: BinaryKind.Add or BinaryKind.Subtract } b => PointeeType(b.Left) ?? PointeeType(b.Right),
            Convert c => PointeeType(c.Operand),
            _ => null,
        },
    };

    /// <summary>
    /// Short-circuit composition prints comparisons and nots bare (they bind
    /// tighter than &amp;&amp;/||); same-kind chains associate without parens;
    /// mixed kinds parenthesize.
    /// </summary>
    string LogicalText(LogicalBinary logical)
    {
        if (TryPropertyPatternText(logical) is { } propertyPattern)
            return propertyPattern;

        // Sides are condition positions: Condition() owns truthiness (a
        // string operand spells 'is not null', never '!value') and the
        // negation folds. Same-kind chains associate bare; mixed-kind
        // LogicalBinary parenthesizes. Any other side renders at the
        // operator's demand — a ternary or `??` (or one hidden behind a stale
        // Coerce/Convert, the #2345/#2376 blind spot) is looser than `&&`/`||`
        // and must parenthesize, while comparisons and unary forms out-bind it
        // and stay bare (#2376 round-2: the enum/bool truthiness compositions
        // share the one precedence rule, not just BoolToInteger).
        var demand = logical.Kind == LogicalKind.And ? Precedence.ConditionalAnd : Precedence.ConditionalOr;
        string Side(IrExpression side) => side switch
        {
            LogicalBinary nested when nested.Kind == logical.Kind => LogicalText(nested),
            LogicalBinary nested => $"({LogicalText(nested)})",
            _ => RenderedCondition(side).At(demand),
        };
        string op = logical.Kind == LogicalKind.And ? "&&" : "||";
        return $"{Side(logical.Left)} {op} {Side(logical.Right)}";
    }

    string? TryPropertyPatternText(LogicalBinary logical)
        => TryPropertyPatternText(logical, negated: false);

    string? TryPropertyPatternText(LogicalBinary logical, bool negated)
    {
        if (logical.Kind != LogicalKind.And)
        {
            return null;
        }

        var conjuncts = new List<IrExpression>();
        CollectConjuncts(logical, conjuncts);
        if (conjuncts is not [IsPattern pattern, .. var rest])
            return null;

        var subpatterns = new List<(string PropertyName, string Subpattern)>();
        foreach (var conjunct in rest)
        {
            if (!TryPropertySubpattern(conjunct, pattern.LocalIndex, allowStringEquality: !pattern.PreserveLocalInPropertyPattern, out var propertyName, out var subpattern))
                return null;
            subpatterns.Add((propertyName, subpattern));
        }
        if (subpatterns.Count == 0
            || subpatterns.Select(p => p.PropertyName).Distinct(StringComparer.Ordinal).Count() != subpatterns.Count)
        {
            return null;
        }

        if (negated && pattern.PreserveLocalInPropertyPattern)
            return null;

        string designation = pattern.PreserveLocalInPropertyPattern ? $" {LocalName(pattern.LocalIndex)}" : "";
        string not = negated ? " not" : "";
        return $"{TypeTestValueText(pattern.Value)} is{not} {TypeText(pattern.Type)} {{ {string.Join(", ", subpatterns.Select(p => $"{p.PropertyName}: {p.Subpattern}"))} }}{designation}";
    }

    static void CollectConjuncts(IrExpression expression, List<IrExpression> conjuncts)
    {
        if (expression is LogicalBinary { Kind: LogicalKind.And } logical)
        {
            CollectConjuncts(logical.Left, conjuncts);
            CollectConjuncts(logical.Right, conjuncts);
            return;
        }

        conjuncts.Add(expression);
    }

    /// <summary>
    /// Folds a single comparison of the pattern local's property against a
    /// constant into a property sub-pattern. Equality becomes the bare constant
    /// (<c>{ P: 5 }</c>); the four relational kinds become a relational pattern
    /// (<c>{ P: &gt; 5 }</c>). Floats are excluded: their unordered (NaN)
    /// comparison semantics are not reproduced by a relational pattern.
    /// </summary>
    static bool TryPropertySubpattern(IrExpression expression, int patternLocal, bool allowStringEquality, out string propertyName, out string subpattern)
    {
        propertyName = "";
        subpattern = "";

        if (expression is not Comparison comparison)
        {
            if (expression is LogicalNot { Operand: Call negatedCall }
                && allowStringEquality
                && MemberIdentity.IsStringEquality(negatedCall)
                && negatedCall.Arguments is [var negatedLeft, var negatedRight]
                && TryPropertyConstant(negatedLeft, negatedRight, patternLocal, out propertyName, out var negatedConstant))
            {
                subpattern = $"not {ConstantText(negatedConstant)}";
                return true;
            }
            if (expression is Call call
                && allowStringEquality
                && MemberIdentity.IsStringEquality(call)
                && call.Arguments is [var left, var right]
                && TryPropertyConstant(left, right, patternLocal, out propertyName, out var stringConstant))
            {
                subpattern = ConstantText(stringConstant);
                return true;
            }
            if (expression is Call inequalityCall
                && allowStringEquality
                && MemberIdentity.IsStringInequality(inequalityCall)
                && inequalityCall.Arguments is [var inequalityLeft, var inequalityRight]
                && TryPropertyConstant(inequalityLeft, inequalityRight, patternLocal, out propertyName, out var notConstant))
            {
                subpattern = $"not {ConstantText(notConstant)}";
                return true;
            }
            return false;
        }

        // Orient the comparison so the property is the left operand; mirror the
        // kind when the constant leads (`5 < t.P` reads as `t.P > 5`).
        LoadProperty property;
        Constant constant;
        ComparisonKind kind;
        if (comparison.Left is LoadProperty leftProperty && IsPatternLocalProperty(leftProperty, patternLocal) && comparison.Right is Constant rightConstant)
        {
            property = leftProperty;
            constant = rightConstant;
            kind = comparison.Kind;
        }
        else if (comparison.Right is LoadProperty rightProperty && IsPatternLocalProperty(rightProperty, patternLocal) && comparison.Left is Constant leftConstant)
        {
            property = rightProperty;
            constant = leftConstant;
            kind = Conditions.Mirror(comparison.Kind);
        }
        else
        {
            return false;
        }

        propertyName = property.PropertyName;

        if (kind == ComparisonKind.Equal)
        {
            subpattern = ConstantText(constant);
            return true;
        }

        // Relational sub-patterns require an ordered comparison; floats carry
        // unordered/NaN semantics, and != has no relational pattern form.
        string? relationalOperator = kind switch
        {
            ComparisonKind.LessThan => "<",
            ComparisonKind.LessThanOrEqual => "<=",
            ComparisonKind.GreaterThan => ">",
            ComparisonKind.GreaterThanOrEqual => ">=",
            _ => null,
        };
        if (relationalOperator is null || comparison.IsUnsigned || IsFloatComparison(comparison.Left, comparison.Right))
            return false;

        subpattern = $"{relationalOperator} {ConstantText(constant)}";
        return true;
    }

    static bool TryPropertyConstant(
        IrExpression left,
        IrExpression right,
        int patternLocal,
        out string propertyName,
        out Constant constant)
    {
        if (left is LoadProperty leftProperty && IsPatternLocalProperty(leftProperty, patternLocal) && right is Constant rightConstant)
        {
            propertyName = leftProperty.PropertyName;
            constant = rightConstant;
            return true;
        }

        if (right is LoadProperty rightProperty && IsPatternLocalProperty(rightProperty, patternLocal) && left is Constant leftConstant)
        {
            propertyName = rightProperty.PropertyName;
            constant = leftConstant;
            return true;
        }

        propertyName = "";
        constant = null!;
        return false;
    }

    static bool IsPatternLocalProperty(LoadProperty property, int patternLocal)
        => property.HasInstance
            && property.Instance is LoadLocal local
            && local.Index == patternLocal
            && property.IndexArguments.Count == 0;

    string ListPatternAlternativesText(SingleElementListPattern pattern)
        => string.Join(" or ", pattern.Alternatives.Select(ConstantText));

    string PositionalPatternText(PositionalPattern pattern)
    {
        var constants = pattern.Constants;
        return $"{Operand(pattern.Value)} is ({string.Join(", ", pattern.Subpatterns.Select((subpattern, i) => PositionalSubpatternText(subpattern, constants[i])))})";
    }

    static string PositionalSubpatternText(PositionalPatternSubpattern subpattern, Constant constant)
        => subpattern.Kind switch
        {
            ComparisonKind.Equal => ConstantText(constant),
            ComparisonKind.NotEqual => $"not {ConstantText(constant)}",
            _ => $"{ComparisonOperator(subpattern.Kind)} {ConstantText(constant)}",
        };

    /// <summary>
    /// A return statement. A method that returns by reference (<c>ref T</c>) ends
    /// in <c>return ref place;</c> — the IL <c>ret</c> yields a managed pointer, so
    /// the keyword is required (a bare <c>return place;</c> is CS8150). Falls back
    /// to the by-value spelling for value returns and for the rare ref return whose
    /// value is not a single place (a ref ternary binds <c>ref</c> per arm).
    /// </summary>
    string ReturnText(IrExpression value)
        => _function.Signature.ReturnType is { Kind: TypeRefKind.ByRef } && ArgumentLvalue(value) is { } place
            ? $"return ref {place};"
            : $"return {CoerceText(value, _function.Signature.ReturnType)};";

    /// <summary>
    /// Assignment spelling with compound/increment sugar: when the value is
    /// an unchecked binary whose left operand reads the assignment target,
    /// the runtime style is x++/x-- for ±1 and x op= rest otherwise.
    /// </summary>
    string AssignmentText(string target, IrExpression value, Func<IrExpression, bool> readsTarget, TypeRef? targetType = null)
    {
        if (value is Binary binary && readsTarget(binary.Left))
        {
            // A compound assignment only forms when the value reads the target
            // in same-type arithmetic, so the result already matches the target
            // — no conversion is involved on this path.
            string statement = CompoundStatement(target, binary, targetType);
            // A checked compound (add.ovf/sub.ovf/mul.ovf) cannot be spelled as a
            // statement-level `checked(x += v)` (CS0201), so the overflow context
            // is restored with a single-statement checked block. Only the
            // overflow-honoring operators ever carry IsChecked here.
            return binary.IsChecked ? $"checked {{ {statement} }}" : statement;
        }
        return $"{target} = {CoerceText(value, targetType)};";
    }

    /// <summary>
    /// Spells a compound assignment whose value reads the target: <c>x++</c>/
    /// <c>x--</c> for a ±1 step, <c>x op= rest</c> otherwise. A shift count carries
    /// the compiler's implicit width mask; strip it exactly as the expression form
    /// does so <c>x &lt;&lt;= n</c> does not re-mask on recompile (see ShiftCount).
    /// </summary>
    string CompoundStatement(string target, Binary binary, TypeRef? targetType = null)
    {
        if (targetType is { Kind: TypeRefKind.Pointer, ElementType: { } pointerElement }
            && binary.Kind is BinaryKind.Add or BinaryKind.Subtract)
        {
            if (TryScaledPointerIndex(binary.Right, pointerElement, out var pointerIndex))
            {
                if (pointerIndex is Constant { Value: 1 })
                    return $"{target}{(binary.Kind == BinaryKind.Add ? "++" : "--")};";
                return $"{target} {BinaryOperator(binary)}= {Expression(pointerIndex)};";
            }
            return $"{target} = {CoerceText(binary, targetType)};";
        }
        if (binary.Kind is BinaryKind.Add or BinaryKind.Subtract && binary.Right is Constant { Value: 1 })
            return $"{target}{(binary.Kind == BinaryKind.Add ? "++" : "--")};";
        // The compound runs in the lvalue's type. Prefer the resolved store type
        // (`targetType`) over `binary.Left.ResultType`: an indirect store reads its
        // target through `ldind.i`, which the importer types as the signed native
        // `IntPtr` even for a `ref nuint`, so the bare `binary.Left` type loses the
        // lvalue's real signedness.
        var lvalueType = targetType ?? binary.Left.ResultType;
        string rightText = binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight
            ? ShiftCount(binary)
            // A bitwise &=/|=/^= against an enum lvalue whose right operand is still
            // a bare integer (`result |= 512`) is `enum |= int` — CS0019, the
            // compound sibling of the `enum & int` coercion in BinaryBody.
            // TryCoerceEnumOperand owns the decision (structural enum test for the
            // cross-assembly case, bool composition, member naming). A
            // same-assembly enum already had its operand retyped, so its right
            // type is the enum (not integer-like) and this is skipped.
            : binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor
                && TryCoerceEnumOperand(binary.Right, lvalueType) is { } coercedRight
                ? coercedRight
            // A mixed-sign same-width compound (`nuint -= nint`, `ulong /= long`)
            // has no C# common type, so `target op= right` is CS0034. For the
            // sign-NEUTRAL operators (unchecked +/-/*, bitwise &/|/^) the bit
            // operation is identical either way, so cast the right operand to the
            // lvalue type to make it bind. Sign-sensitive /, % are excluded in the
            // plain binary form (an operand cast flips div/div.un), but a COMPOUND
            // runs in the lvalue's type, so the opcode signedness is already the
            // lvalue's: casting only the right operand is faithful when the opcode
            // signedness matches the lvalue. Checked .ovf compounds stay plain.
            : NeedsCompoundSignCast(binary, lvalueType)
                ? CoerceText(binary.Right, lvalueType)
                : Operand(binary.Right);
        return $"{target} {BinaryOperator(binary)}= {rightText};";
    }

    /// <summary>Structural same-place check for compound-assignment receivers; conservative (this/locals/arguments/static only).</summary>
    static bool SamePlace(IrExpression? a, IrExpression? b) => (a, b) switch
    {
        (null, null) => true,
        (LoadArgument x, LoadArgument y) => x.Index == y.Index,
        (LoadLocal x, LoadLocal y) => x.Index == y.Index,
        _ => false,
    };

    /// <summary>
    /// Structural, side-effect-free equality for compound-assignment lvalues — the
    /// receiver/address an <c>x op= v</c> fold reads on its right and writes on its
    /// left. Restricted to leaves whose re-evaluation is observably free (locals,
    /// arguments, constants, and field/element addresses rooted in those), so
    /// collapsing the two evaluations into one preserves the opcode stream. A
    /// shape with any potential side effect (a call, an arbitrary expression)
    /// falls through to <c>false</c> and keeps the expanded spelling.
    /// </summary>
    static bool SameLValue(IrExpression? a, IrExpression? b) => (a, b) switch
    {
        (null, null) => true,
        (LoadArgument x, LoadArgument y) => x.Index == y.Index,
        (LoadLocal x, LoadLocal y) => x.Index == y.Index,
        (Constant x, Constant y) => Equals(x.Value, y.Value),
        (LoadField x, LoadField y) => x.Field.Name == y.Field.Name
            && Equals(x.Field.DeclaringType, y.Field.DeclaringType) && SameLValue(x.Instance, y.Instance),
        (LoadFieldAddress x, LoadFieldAddress y) => x.Field.Name == y.Field.Name
            && Equals(x.Field.DeclaringType, y.Field.DeclaringType) && SameLValue(x.Instance, y.Instance),
        (LoadElementAddress x, LoadElementAddress y) => SameLValue(x.Array, y.Array) && SameLValue(x.Index, y.Index),
        _ => false,
    };

    /// <summary>True when a non-instance call renders as a C# operator (`a != b`, `-x`) rather than a method invocation — the compound form that must parenthesize as an operand.</summary>
    bool IsOperatorCall(Call call)
        => !call.Callee.HasThis
            && (call.Callee.IsOperator != MetadataFactState.No && call.Callee.IsSpecialName
                || MemberIdentity.IsKnownCoreLibraryOperator(call.Callee))
            && OperatorSpelling(call) is not null;

    /// <summary>
    /// True when an expression is legal as a C# expression statement: an
    /// invocation, object creation, await, or inc/decrement. An operator-spelled
    /// call (`a != b`) renders as a value, not a statement, so it is excluded.
    /// Any other value is CS0201 as a statement and must be discarded with `_ =`.
    /// </summary>
    bool IsStatementExpression(IrExpression expression) => expression switch
    {
        Call call => !IsOperatorCall(call),
        CallIndirect or NewObject or IncrementDecrement or AwaitExpression or LocalFunctionInvocation => true,
        _ => false,
    };

    static Precedence? OperatorCallPrecedence(Call call)
    {
        var arguments = call.Arguments;
        string name = call.Callee.Name;
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
            name = "op_" + name["op_Checked".Length..];

        return arguments.Count switch
        {
            2 => name switch
            {
                "op_Equality" or "op_Inequality" => Precedence.Equality,
                "op_LessThan" or "op_LessThanOrEqual" or "op_GreaterThan" or "op_GreaterThanOrEqual" => Precedence.Relational,
                "op_Addition" or "op_Subtraction" => Precedence.Additive,
                "op_Multiply" or "op_Division" or "op_Modulus" => Precedence.Multiplicative,
                "op_BitwiseAnd" => Precedence.BitwiseAnd,
                "op_BitwiseOr" => Precedence.BitwiseOr,
                "op_ExclusiveOr" => Precedence.BitwiseXor,
                "op_LeftShift" or "op_RightShift" or "op_UnsignedRightShift" => Precedence.Shift,
                _ => null,
            },
            1 => name switch
            {
                "op_UnaryNegation" or "op_UnaryPlus" or "op_LogicalNot" or "op_OnesComplement"
                    or "op_Implicit" or "op_Explicit" => Precedence.Unary,
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>
    /// True when <paramref name="type"/> is a value type, so an <c>isinst</c>
    /// type-test must spell <c>obj is T</c> — <c>obj as T</c> is CS0077 on a
    /// non-nullable value type. Primitives are value types intrinsically;
    /// other definitions resolve through the shape map (enums included).
    /// </summary>
    bool IsValueTypeTarget(TypeRef type)
        => TypeFamilies.IsNumericPrimitive(type)
            || type is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
            || _function.TypeShapes.GetValueOrDefault(type) is TypeShape.ValueType or TypeShape.Enum;

    /// <summary>The operator form of an op_* call, or null when the name has no spelling (op_True/op_False and friends stay as calls).</summary>
    string? OperatorSpelling(Call call)
    {
        var arguments = call.Arguments;

        // User-defined checked operators (C# 11). The metadata name encodes the
        // checked overload (op_CheckedAddition, op_CheckedSubtraction, ...); the
        // faithful spelling wraps the operator form in checked(...) so the same
        // overload is selected, collapsing the wrapper inside an enclosing checked
        // context. Without this the call falls through to a method spelling
        // (T.op_CheckedAddition(a, b)) that is CS0571 "cannot explicitly call
        // operator" — invalid Full. See #1706.
        if (call.Callee.Name.StartsWith("op_Checked", StringComparison.Ordinal))
            return CheckedOperatorSpelling(call);

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
                "op_UnsignedRightShift" => ">>>",
                _ => null,
            };
            return op is null ? null : $"{OperatorOperand(arguments[0])} {op} {OperatorOperand(arguments[1])}";
        }
        if (arguments.Count == 1)
        {
            return call.Callee.Name switch
            {
                "op_UnaryNegation" => $"-{OperatorOperand(arguments[0])}",
                "op_UnaryPlus" => $"+{OperatorOperand(arguments[0])}",
                "op_LogicalNot" => $"!{OperatorOperand(arguments[0])}",
                "op_OnesComplement" => $"~{OperatorOperand(arguments[0])}",
                "op_Implicit" or "op_Explicit" => ConversionOperatorSpelling(call.Callee.ReturnType, arguments[0]),
                _ => null,
            };
        }
        return null;
    }

    /// <summary>
    /// An operand of a user-defined operator call. The operator's parameters may
    /// be <c>in</c>/<c>ref</c>, so the IL passes the operand's address
    /// (<c>ldarga</c>/<c>ldloca</c>/<c>ldflda</c>); C# operator syntax takes that
    /// address implicitly, so the operand is the place itself — <c>a != b</c>, not
    /// the CS1525 <c>(ref a) != (ref b)</c>. Strip the address-of; other operands
    /// render normally.
    /// </summary>
    string OperatorOperand(IrExpression argument)
        => argument is LoadArgumentAddress or LoadLocalAddress or LoadFieldAddress or LoadElementAddress
            ? Deref(argument)
            : Operand(argument);

    string ConversionOperatorSpelling(TypeRef target, IrExpression value)
    {
        string targetText = TypeText(target);
        string operand = CastOperand(OperatorOperand(value), targetText);
        return $"({targetText}){operand}";
    }

    /// <summary>The checked-context spelling of a user-defined checked operator call (op_Checked*), or null when the name has no faithful operator form.</summary>
    string? CheckedOperatorSpelling(Call call)
    {
        var arguments = call.Arguments;

        // checked explicit conversion: checked((T)x).
        if (call.Callee.Name == "op_CheckedExplicit" && arguments.Count == 1)
            return WrapChecked(() => ConversionOperatorSpelling(call.Callee.ReturnType, arguments[0]));

        // The remaining checked operators share their symbol with the unchecked
        // form (op_CheckedAddition → "+"); reuse the single mapping the signature
        // renderer uses. Increment/decrement are folded to ++/-- upstream by
        // IncrementDecrementPass (#1712); a checked increment call that survives
        // to here (an unfolded shape) has no faithful functional spelling, so it
        // falls through to null (a method call).
        string? symbol = OperatorNames.MapBinaryOrUnary(call.Callee.Name["op_Checked".Length..]);
        return (symbol, arguments.Count) switch
        {
            ("+" or "-" or "*" or "/", 2)
                => WrapChecked(() => $"{OperatorOperand(arguments[0])} {symbol} {OperatorOperand(arguments[1])}"),
            ("-", 1) // op_CheckedUnaryNegation
                => WrapChecked(() => $"-{OperatorOperand(arguments[0])}"),
            _ => null,
        };
    }

    /// <summary>Wraps an operator spelling in <c>checked(...)</c>, rendering its operands in a checked context so nested checked operators collapse; an enclosing checked context drops the redundant wrapper.</summary>
    string WrapChecked(Func<string> render)
    {
        if (_checkedContext)
            return render();
        _checkedContext = true;
        try
        {
            return $"checked({render()})";
        }
        finally
        {
            _checkedContext = false;
        }
    }

    HashSet<string>? _localScopeNames;

    string[]? _localDisplayNames;

    /// <summary>
    /// The display name for local slot <paramref name="index"/>: the PDB source
    /// name when present, usable as a C# identifier, and not already taken by a
    /// parameter or an earlier-named local; otherwise the synthetic
    /// <c>V_index</c>. Resolved once per function so every reference to a slot —
    /// declaration, load, address, shadow test — spells it identically.
    /// </summary>
    string LocalName(int index)
    {
        if (_localDisplayNames is null)
        {
            int count = _function.Locals.Length;
            var display = new string[count];
            var sourceNamed = new bool[count];
            for (int i = 0; i < count; i++)
                display[i] = $"V_{i}";

            var taken = CurrentReservedNames();

            var names = _function.LocalNames;
            if (!names.IsDefaultOrEmpty)
            {
                for (int i = 0; i < count && i < names.Length; i++)
                {
                    if (names[i] is { } name && CSharpNaming.IsUsableIdentifier(name) && taken.Add(name))
                    {
                        display[i] = name;
                        sourceNamed[i] = true;
                    }
                }
            }

            // Opt-in readable names: a local with no usable source name gets a
            // synthesized name from IR evidence (its type, loop-counter role),
            // collision-resolved against names already taken. Off by default, so
            // the shipped V_index output is untouched.
            if (_options.ReadableLocalNames)
            {
                var counters = LoopCounterLocals();
                for (int i = 0; i < count; i++)
                {
                    if (sourceNamed[i])
                        continue;
                    var type = i < _function.Locals.Length ? _function.Locals[i] : null;
                    if (LocalNameSynthesizer.Synthesize(type, counters.Contains(i), taken) is { } synthesized)
                    {
                        display[i] = synthesized;
                        taken.Add(synthesized);
                        sourceNamed[i] = true;
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (sourceNamed[i])
                    continue;
                display[i] = ReserveName(display[i], taken);
            }
            _localDisplayNames = display;
        }
        return index >= 0 && index < _localDisplayNames.Length ? _localDisplayNames[index] : $"V_{index}";
    }

    static string ReserveName(string baseName, HashSet<string> taken)
    {
        if (taken.Add(baseName))
            return baseName;
        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName}_{i}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Locals written by a <see cref="ForLoop"/>'s increment — the induction
    /// variables that earn the conventional <c>i</c>/<c>j</c>/<c>k</c> name in the
    /// opt-in readable-names mode. Evidence from the structured tree, not a guess.
    /// </summary>
    HashSet<int> LoopCounterLocals()
    {
        var counters = new HashSet<int>();
        foreach (var loop in DescendantsOutsideNestedFunctions(_function).OfType<ForLoop>())
        {
            var increment = loop.Increment;
            if (increment is StoreLocal direct)
                counters.Add(direct.Index);
            foreach (var node in increment.Descendants)
                if (node is StoreLocal store)
                    counters.Add(store.Index);
        }
        return counters;
    }

    /// <summary>
    /// True when an instance-method parameter or local would shadow a field of
    /// this name, so a bare reference binds to the local rather than the field.
    /// Locals print with their resolved display name; parameters carry their
    /// metadata names.
    /// </summary>
    bool IsShadowedByLocal(string fieldName)
    {
        if (_localScopeNames is null)
        {
            _localScopeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in _function.Signature.Parameters)
                _localScopeNames.Add(parameter.Name);
            for (int i = 0; i < _function.Locals.Length; i++)
                _localScopeNames.Add(LocalName(i));
        }
        return _localScopeNames.Contains(fieldName);
    }

    HashSet<string>? _staticScopeShadowNames;
    readonly HashSet<string> _syntheticLocalNames = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the escaped source spelling of a static-call name is captured by
    /// a parameter or local in scope — the enclosing method's parameters and
    /// locals, names inherited from an enclosing printer, and nested lambda /
    /// local-function parameters — so an unqualified call would bind to that local
    /// rather than the static method. Names are compared in their escaped C#
    /// spelling (a keyword carries the leading <c>@</c>), matching the rendered
    /// call name.
    ///
    /// This is a deliberate over-approximation of lexical scope: it treats every
    /// parameter/local anywhere in the method (including sibling lambdas) as a
    /// shadow, so a rare sibling-lambda name collision keeps a call qualified that
    /// could in principle be bare. That only costs fidelity (the qualified form is
    /// always valid), whereas missing a real shadow would rebind the call and
    /// produce wrong or uncompilable C#; the guard errs toward qualifying.
    /// </summary>
    bool IsStaticCallNameShadowed(string escapedName)
    {
        // Synthetic locals are generated during rendering; check the live set so a
        // name emitted before this call (regardless of the cache below) is seen.
        // These names are never keywords, so their spelling equals escapedName.
        if (_syntheticLocalNames.Contains(escapedName))
            return true;
        if (_staticScopeShadowNames is null)
        {
            _staticScopeShadowNames = new HashSet<string>(StringComparer.Ordinal);
            // CurrentScopeNames aggregates every binder in scope for this printer:
            // the enclosing method's parameters and locals, names inherited from an
            // enclosing printer (when this renders a lambda/local-function body with
            // its own scope), nested lambda/local-function parameters, and the
            // printer's own synthetic locals (stack slots S_n, switch temps). It
            // mixes raw and escaped names; EscapeIdentifier is idempotent, so
            // normalize all to the escaped spelling the rendered call name uses.
            foreach (var name in CurrentScopeNames())
                _staticScopeShadowNames.Add(CSharpNaming.EscapeIdentifier(name));
        }
        return _staticScopeShadowNames.Contains(escapedName);
    }

    string IncrementDecrementText(IncrementDecrement id)
    {
        string op = id.IsIncrement ? "++" : "--";

        // A user-defined checked increment/decrement (op_CheckedIncrement/Decrement)
        // selects its overload from the checked context; force it with checked(...)
        // unless an enclosing checked context already does.
        if (id.IsChecked)
            return WrapChecked(() => id.IsPrefix ? $"{op}{Operand(id.Target)}" : $"{Operand(id.Target)}{op}");

        // ++/-- is a hidden `x = x + 1`; on an integer place inside a checked
        // region that add recompiles as `add.ovf`, an overflow check the original
        // plain increment never had. A user-defined unchecked place inside a
        // checked context would likewise bind to its checked operator overload.
        // Wrap in `unchecked(...)` and clear the context for the place expression.
        bool wrapUnchecked = _checkedContext && (TypeFamilies.IsInteger(id.ResultType) || id.IsUserDefined);
        bool saved = _checkedContext;
        if (wrapUnchecked)
            _checkedContext = false;
        try
        {
            string text = id.IsPrefix ? $"{op}{Operand(id.Target)}" : $"{Operand(id.Target)}{op}";
            return wrapUnchecked ? $"unchecked({text})" : text;
        }
        finally
        {
            _checkedContext = saved;
        }
    }

    /// <summary>A user-defined checked ++/-- in statement position: a <c>checked { x++; }</c> block, since the <c>checked(x++)</c> expression is CS0201 as a statement.</summary>
    string CheckedIncrementStatement(IncrementDecrement id)
    {
        bool saved = _checkedContext;
        _checkedContext = true;
        try
        {
            return $"checked {{ {IncrementDecrementText(id)}; }}";
        }
        finally
        {
            _checkedContext = saved;
        }
    }

    string ConvertText(Convert convert)
    {
        // A checked conversion already inside a checked context drops its own
        // wrapper (the enclosing checked covers it); only the outermost one wraps.
        bool enclosingChecked = _checkedContext;
        if (convert.IsChecked)
        {
            _checkedContext = true;
            try
            {
                return ConvertBody(convert, wrap: !enclosingChecked, uncheckedOverflow: false);
            }
            finally
            {
                _checkedContext = enclosingChecked;
            }
        }
        // The symmetric insert: a plain (non-overflow) narrowing/sign-changing
        // conversion spelled inside a checked region recompiles to a `conv.ovf.*`
        // it never had — `checked(a + unchecked((short)b))` would range-check the
        // inner cast. Wrap it in `unchecked(...)` and clear the context so its
        // operand recompiles plain (a widening conversion never flips, so it is
        // left bare to avoid pointless wrappers).
        bool uncheckedOverflow = enclosingChecked && IsCheckedSensitiveConversion(convert);
        if (uncheckedOverflow)
            _checkedContext = false;
        try
        {
            return ConvertBody(convert, wrap: false, uncheckedOverflow: uncheckedOverflow);
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    /// <summary>
    /// True when recompiling the explicit cast <c>(target)operand</c> inside a
    /// lexical <c>checked</c> region would emit a <c>conv.ovf.*</c> opcode: a
    /// narrowing or sign-changing integer conversion, or any float→integer. A plain
    /// <see cref="Convert"/> matching this must be wrapped in <c>unchecked(...)</c>
    /// when spelled inside a checked context, or it silently acquires overflow
    /// checking it never had. An implicit widening (int→long, byte→int, …) never
    /// flips and is left bare. An unknown or non-numeric source is treated as
    /// sensitive — wrapping is always behavior-preserving, so it is the safe
    /// default.
    /// </summary>
    static bool IsCheckedSensitiveConversion(Convert convert)
    {
        if (!TypeFamilies.IsIntegerLike(convert.Target))
            return false;   // float/non-integer targets have no conv.ovf form
        var source = convert.Operand.ResultType;
        if (source is null || TypeFamilies.IsFloat(source) || !TypeFamilies.IsIntegerLike(source))
            return true;    // float→integer always checks; unknown/pointer source: be safe
        if (source.Equals(convert.Target))
            return false;   // identity: no conv emitted
        return !CSharpConversionRules.IsImplicitIntegerWidening(source, convert.Target);
    }

    string ConvertBody(Convert convert, bool wrap, bool uncheckedOverflow)
    {
        // An address-of node (ldloca/ldarga/ldflda/ldelema) converted to a
        // pointer or native integer (conv.u/conv.i) is C#'s address-of operator,
        // not a `ref` place: `(nuint)(ref x)` is CS1525 — the faithful unsafe
        // spelling is `(nuint)(&x)`. The bare `ref` form is only valid in a
        // ref-return/ref-argument/ref-local position, never as a cast operand.
        if (convert.Operand is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or LoadElementAddress)
        {
            string addressCast = $"({TypeText(convert.Target)})(&{Deref(convert.Operand)})";
            if (wrap)
                return $"checked({addressCast})";
            return uncheckedOverflow ? $"unchecked({addressCast})" : addressCast;
        }
        // Converting an out-of-range integer constant (conv.u8 of ldc.i4.m1 for
        // ulong.MaxValue) is CS0221 as a plain cast; reinterpret its bits with
        // unchecked, matching the constant handling at value boundaries. The
        // unchecked already covers any enclosing checked context.
        if (!convert.IsChecked && convert.Operand is Constant { Value: int or long } c
            && TypeFamilies.IsNumericPrimitive(convert.Target))
        {
            long literal = c.Value is int i ? i : (long)c.Value!;
            if (!CSharpConversionRules.ConstantFits(literal, convert.Target))
            {
                // A widening conversion to an unsigned target ZERO-extends the source
                // (`conv.u8` of `ldc.i4.m1` is 0x00000000FFFFFFFF = uint.MaxValue),
                // where a bare `(ulong)(-1)` sign-extends to ulong.MaxValue — a silent
                // wrong value. Reinterpret the source's bits through its unsigned
                // sibling so the value is faithful and the cast round-trips to the same
                // `conv` opcode (#2101). `conv.i8` (signed target) keeps sign-extension.
                if (literal < 0
                    && TypeFamilies.ZeroExtendingSource(convert.Operand.ResultType, convert.Target) is { } zeroExtendSource)
                    return $"unchecked(({TypeText(convert.Target)})({TypeText(zeroExtendSource)})({Expression(convert.Operand)}))";
                return $"unchecked(({TypeText(convert.Target)})({Expression(convert.Operand)}))";
            }
        }
        // conv.r.un and conv.ovf.*.un interpret the SOURCE as unsigned —
        // a signed operand needs its unsigned cast or the value is wrong.
        string operand = convert.IsUnsigned ? UnsignedOperand(convert.Operand, checkedSafe: !convert.IsChecked) : Operand(convert.Operand);
        // A widening conversion to an unsigned target (`conv.u8`) ZERO-extends the
        // 32-bit stack value, but a bare `(ulong)x` sign-extends a SIGNED operand —
        // a silent wrong value for a negative x. C# elides the sibling cast, so
        // `(ulong)(uint)i` compiles to `ldarg; conv.u8` and the operand is a bare
        // signed `int`. Reinterpret through the unsigned sibling when the operand
        // RENDERS signed. The discriminator is EffectiveType, not the ECMA stack
        // ResultType: an already-unsigned expression such as `(uint)a + b` renders
        // unsigned (its bare `(ulong)` already zero-extends) and must not be
        // re-cast — gating on the rendered type keeps this to genuine corrections.
        // Constants take the value-aware branch above; `.un` operands are already
        // unsigned (#2336, the non-constant sibling of #2101).
        // Only the UNCHECKED widening conv.u8/conv.u zero-extends silently. A
        // checked conv.ovf.u8 of a signed operand throws for a negative value, so
        // the bare `checked((ulong)i)` already matches it — inserting a `(uint)`
        // there would recompile to conv.ovf.u4;conv.u8 (wrong opcodes).
        if (!convert.IsUnsigned && !convert.IsChecked && convert.Operand is not Constant
            && TypeFamilies.WideningZeroExtendSibling(EffectiveType(convert.Operand), convert.Target) is { } zeroExtendWiden)
            operand = $"({TypeText(zeroExtendWiden)}){operand}";
        string targetText = TypeText(convert.Target);
        operand = CastOperand(operand, targetText);
        string cast = $"({targetText}){operand}";
        if (wrap)
            return $"checked({cast})";
        return uncheckedOverflow ? $"unchecked({cast})" : cast;
    }

    static string CastOperand(string operand, string targetText)
        => NeedsCastOperandParentheses(operand, targetText) ? $"({operand})" : operand;

    // A cast whose operand begins with a unary `-`/`+` is parsed as binary
    // subtraction/addition (CS0075) unless the target spelling is a predefined
    // keyword type the parser treats as cast-disambiguating. `nint`/`nuint`
    // are contextual keywords and named types are not, so `(nint)-1` misparses
    // — wrap the operand: `(nint)(-1)`. The parens are opcode-identical.
    static bool NeedsCastOperandParentheses(string operand, string targetText)
        => operand.Length > 0
            && operand[0] is '-' or '+'
            && !s_castDisambiguatingKeywords.Contains(targetText);

    // The predefined-type keyword spellings the C# parser treats as
    // cast-disambiguating: `(int)-1` is a cast, but `(nint)-1` (a contextual
    // keyword) or `(MyEnum)-1` (a named type) parses as subtraction (CS0075).
    static readonly HashSet<string> s_castDisambiguatingKeywords = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint",
        "long", "ulong", "float", "double", "decimal", "string", "object", "void",
    };

    /// <summary>
    /// A retyped enum constant renders <c>EnumType.Member</c> when its value
    /// names exactly one member of the resolved (same-assembly) enum. Composite
    /// flag values and unnamed casts have no exact member and fall through to
    /// the raw integer — naming those is a later slice.
    /// </summary>
    string? EnumMemberName(Constant constant)
        => constant.Value is int or long
            && _function.EnumMembers.TryGetValue(constant.Type, out var members)
            && members.TryGetValue(constant.Value is int i ? i : (long)constant.Value!, out var name)
            ? $"{TypeQualifierText(constant.Type)}.{name}"
            : null;

    /// <summary>
    /// The enum type a <c>switch</c> governing expression carries, or null when it
    /// is not an enum. Mirrors <see cref="TypedConstantsPass"/>'s operand typing
    /// (an <c>ldind</c> through a <c>ref EnumType</c> yields a load typed by the
    /// opcode width, so see through it to the pointee enum) and the cross-assembly
    /// enum reasoning in the numeric cast path: a framework enum like
    /// <c>DateTimeKind</c> resolves to <see cref="TypeShape.Unknown"/> rather than
    /// <see cref="TypeShape.Enum"/> because shape classification only sees the
    /// inspected assembly's own types. Type-safe IL only switches on an integral,
    /// char, or enum, so a non-primitive named governing type is an enum.
    /// </summary>
    TypeRef? SwitchLabelEnumType(IrExpression value)
    {
        var type = value is LoadIndirect { Address.ResultType: { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer, ElementType: { } pointee } }
            ? pointee
            : value.ResultType;
        if (type is null)
            return null;
        if (_function.TypeShapes.GetValueOrDefault(type) == TypeShape.Enum)
            return type;
        return type is { Kind: TypeRefKind.Definition, Name: not ("Boolean" or "String") }
            && _function.TypeShapes.GetValueOrDefault(type) == TypeShape.Unknown
            && !TypeFamilies.IsNumericPrimitive(type)
            ? type
            : null;
    }

    /// <summary>
    /// Renders a <c>switch</c> case label. When the governing expression is an
    /// enum, a bare integer label is CS0266 (only the literal <c>0</c> converts
    /// implicitly), so the label is spelled by member name when resolved or an
    /// explicit enum cast otherwise — matching how enum constants render
    /// elsewhere. A negative value is parenthesized after the cast (CS0075).
    /// </summary>
    string SwitchLabelText(Constant label, TypeRef? enumType)
    {
        if (enumType is null || label.Value is not (int or long))
            return ConstantText(label);
        return EnumConstantText(label, enumType);
    }

    static string ConstantText(Constant constant) => constant.Value switch
    {
        null => "null",
        string s => StringText(s),
        bool b => b ? "true" : "false",
        char c => CharText(c),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        float f => SingleText(f),
        double d => DoubleText(d),
        _ => constant.Value.ToString() ?? "?",
    };

    static string SingleText(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";
        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";
        return $"{value.ToString("R", CultureInfo.InvariantCulture)}f";
    }

    static string DoubleText(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";
        return $"{value.ToString("R", CultureInfo.InvariantCulture)}d";
    }

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
        // U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR are C# line
        // terminators (ECMA-334 §6.3.1) but are not `char.IsControl`, so a raw
        // emit splits the literal across source lines (CS1010 "Newline in
        // constant"). Escape them explicitly.
        '\u2028' => "\\u2028",
        '\u2029' => "\\u2029",
        _ when char.IsControl(c) => $"\\u{(int)c:x4}",
        // A lone surrogate code unit has no valid UTF-8/UTF-16 text form: emitted
        // raw it cannot survive an encode (writers substitute U+FFFD, corrupting
        // the literal \u2014 char.IsHighSurrogate's own bounds rendered as two
        // replacement characters). Always the \u escape.
        _ when char.IsSurrogate(c) => $"\\u{(int)c:x4}",
        _ => c.ToString(),
    };

    string TypeText(TypeRef type)
    {
        // Scope is the method's declaring type: a nested type of a generic is
        // qualified through its declaring chain (ImmutableArray<string>.Builder)
        // unless the reference is made from inside that enclosing type, where the
        // innermost name is in scope (Enumerator inside List<T>.GetEnumerator).
        string text = TypeTextCore(type);
        int tick = text.IndexOf('`');
        string rendered = tick < 0 ? text : text[..tick];
        RecordFrameworkTypeImportDecision(type, rendered);
        return rendered;
    }

    string TypeQualifierText(TypeRef type)
    {
        string rendered = TypeTextCore(type);
        int tick = rendered.IndexOf('`');
        if (tick >= 0)
            rendered = rendered[..tick];

        if (FirstTypeQualifierSegment(rendered) is { } segment && IsStaticCallNameShadowed(segment))
            rendered = FullyQualifiedTypeText(type);

        RecordFrameworkTypeImportDecision(type, rendered);
        return rendered;
    }

    string TypeTextCore(TypeRef type)
        => type.ToDisplayString(_function.DeclaringType);

    static string? FirstTypeQualifierSegment(string rendered)
    {
        if (rendered.Length == 0 || rendered.StartsWith("global::", StringComparison.Ordinal))
            return null;
        int i = rendered[0] == '@' ? 1 : 0;
        if (i >= rendered.Length || !(char.IsLetter(rendered[i]) || rendered[i] == '_'))
            return null;
        while (++i < rendered.Length && (char.IsLetterOrDigit(rendered[i]) || rendered[i] == '_'))
        {
        }
        return rendered[..i];
    }

    static string FullyQualifiedTypeText(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (definition.Kind != TypeRefKind.Definition || definition.Namespace.Length == 0 && definition.Name.Length == 0)
            return type.ToDisplayString();

        string text = type.ToDisplayString(TypeRef.Definition("__dotnet_inspect", "__", "__"));
        int tick = text.IndexOf('`');
        if (tick >= 0)
            text = text[..tick];
        return definition.Namespace.Length == 0
            ? $"global::{text}"
            : $"global::{EscapeNamespace(definition.Namespace)}.{text}";
    }

    static string EscapeNamespace(string ns)
        => string.Join(".", ns.Split('.').Select(CSharpNaming.EscapeIdentifier));

    void RecordFrameworkTypeImportDecision(TypeRef type, string rendered)
    {
        foreach (var nested in DescendantTypes(type))
            RecordFrameworkTypeImportDecisionCore(nested, rendered);
    }

    void RecordFrameworkTypeImportDecisionCore(TypeRef type, string rendered)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (definition is not { Kind: TypeRefKind.Definition, Namespace.Length: > 0 })
            return;
        if (!IsFrameworkNamespace(definition.Namespace))
            return;
        if (HasGenericEnclosingSegment(definition.Name))
        {
            RecordNestedGenericEnclosingImportDecisions(definition, rendered);
            return;
        }

        string fullName = FrameworkMetadataName(definition);
        string simpleName = TypeNamePath(definition.Name);
        if (rendered.Contains(definition.Namespace + ".", StringComparison.Ordinal))
            return;

        AddDecision(
            "type-name.framework-imported",
            "taste",
            fullName,
            $"Rendered framework type '{fullName}' as imported/simple name '{simpleName}'.",
            oldValue: FrameworkSourceName(definition),
            newValue: simpleName);
    }

    void RecordNestedGenericEnclosingImportDecisions(TypeRef definition, string rendered)
    {
        var segments = definition.Name.Split('+');
        var sourceSegments = new List<string>(segments.Length);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            sourceSegments.Add(CSharpNaming.TypeNameSegment(segments[i]));
            if (GenericArity(segments[i]) == 0)
                continue;

            string oldValue = $"{definition.Namespace}.{string.Join(".", sourceSegments)}";
            string newValue = string.Join(".", sourceSegments);
            if (rendered.Contains(definition.Namespace + ".", StringComparison.Ordinal))
                continue;

            AddDecision(
                "type-name.framework-imported",
                "taste",
                $"{definition.Namespace}.{string.Join("+", segments.Take(i + 1))}",
                $"Rendered framework type '{oldValue}' as imported/simple name '{newValue}'.",
                oldValue: oldValue,
                newValue: newValue);
        }
    }

    static IEnumerable<TypeRef> DescendantTypes(TypeRef type)
    {
        yield return type;
        if (type.ElementType is { } element)
        {
            foreach (var descendant in DescendantTypes(element))
                yield return descendant;
        }
        foreach (var argument in type.TypeArguments)
        {
            foreach (var descendant in DescendantTypes(argument))
                yield return descendant;
        }
    }

    static string FrameworkMetadataName(TypeRef type)
        => type.Namespace.Length == 0 ? CSharpNaming.TypeNameSegment(type.Name) : $"{type.Namespace}.{type.Name}";

    static string FrameworkSourceName(TypeRef type)
        => type.Namespace.Length == 0
            ? TypeNamePath(type.Name)
            : $"{type.Namespace}.{TypeNamePath(type.Name)}";

    static string TypeNamePath(string metadataName)
        => string.Join(".", metadataName.Split('+').Select(CSharpNaming.TypeNameSegment));

    static bool HasGenericEnclosingSegment(string metadataName)
    {
        var segments = metadataName.Split('+');
        return segments.Length > 1
            && segments.Take(segments.Length - 1).Any(segment => GenericArity(segment) > 0);
    }

    static int GenericArity(string metadataName)
    {
        int tick = metadataName.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 && int.TryParse(metadataName[(tick + 1)..], out int arity) ? arity : 0;
    }

    static bool IsFrameworkNamespace(string ns)
        => ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);

    string DeclarationTypeText(TypeRef type, IrExpression initializer)
        => initializer is AnonymousObject anonymous && type.Equals(anonymous.Type)
            ? "var"
            : TypeText(type);

    string TypeOfTypeText(TypeRef type)
        => type.Kind == TypeRefKind.Definition && OpenGenericArity(type) is { } arity
            ? $"{TypeText(type)}<{new string(',', arity - 1)}>"
            : TypeText(type);

    static int? OpenGenericArity(TypeRef type)
    {
        var name = type.Name;
        var nested = name.LastIndexOf('+');
        var innermost = nested < 0 ? name : name[(nested + 1)..];
        var tick = innermost.IndexOf('`');
        if (tick < 0)
            return null;
        return int.TryParse(innermost[(tick + 1)..], out var arity) && arity > 0
            ? arity
            : null;
    }
}
