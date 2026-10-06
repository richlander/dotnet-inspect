using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.ControlFlow;
using ILInspector.Metadata;
using Inspector.Text;
using static ILInspector.Decompiler.Pipeline.PointerArithmetic;
using static ILInspector.Decompiler.Pipeline.PlaceIdentity;
using DecisionKey = (string RuleId, string Category, string Subject, string Detail, string OldValue, string NewValue, string DedupDiscriminator);

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Statement emission: containers, labels, statement cores, statement layout,
/// spacing, constructor chains, and assignment statements. Spelling of decided
/// structure: it defines none of the #2095 decision classes, though it calls
/// the coercion-routing and unsafe-context ones at its sites.
/// </summary>
public sealed partial class CSharpPrinter
{
    void AppendContainer(StringBuilder sb, BlockContainer container, int indent, bool topLevel = false)
    {
        string pad = new(' ', indent * 4);
        var blocks = container.Blocks;
        IrNode? lastStatementBeforeTrailingLocalFunctions = null;
        if (topLevel && blocks.Count > 0)
        {
            // Local-function declarations execute nothing and may legally follow
            // the host's terminal return, so they do not make that return non-terminal.
            var statements = blocks[^1].Children;
            for (int i = statements.Count - 1; i >= 0; i--)
            {
                if (statements[i] is LocalFunctionStatement)
                    continue;
                lastStatementBeforeTrailingLocalFunctions = statements[i];
                break;
            }
        }

        var statementsByBlock = new List<List<IrNode>>(blocks.Count);
        foreach (var block in blocks)
        {
            bool labeledReturnOnly = _labelTargets.Contains(block.StartOffset)
                && block.Children.Count(statement => statement is not LocalFunctionStatement) == 1;
            var emit = new List<IrNode>();
            foreach (var statement in block.Children)
            {
                if (ReferenceEquals(statement, _chainStatement) || _fieldInitStores.Contains(statement))
                    continue;
                bool isLastStatement = ReferenceEquals(statement, lastStatementBeforeTrailingLocalFunctions);
                bool statementOwnsLabel = statement.OwnsSourceLabel
                    && statement.SourceOffset >= 0
                    && _labelTargets.Contains(statement.SourceOffset);
                if (isLastStatement && !labeledReturnOnly && !statementOwnsLabel
                    && statement is Return { Value: null })
                    continue;
                emit.Add(statement);
            }
            statementsByBlock.Add(emit);
        }
        bool semanticSpacing = HasSemanticSpacingCandidates(
                statementsByBlock.SelectMany(static statements => statements),
                statementsByBlock.Any(HasGeneratedUnsafeSetupBoundary))
            && blocks.All(block => !_labelTargets.Contains(block.StartOffset));
        var spacingState = new SemanticSpacingState(semanticSpacing);

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
                if (topLevel)
                    _topLevelHasLabel = true;
            }
            var emit = statementsByBlock[i];

            if (topLevel)
                _topLevelStatements.AddRange(emit);
            if (emit.Any(n => !RendersAsCommentOnly(n)))
                labelPendingStatement = false;
            AppendStatements(
                sb,
                emit,
                indent,
                ref spacingState);
        }
        if (labelPendingStatement)
            sb.Append(pad).AppendLf(";");
    }

    void AppendLabel(StringBuilder sb, string pad, int offset)
    {
        // First printed occurrence owns the label; structured replacements stamp
        // the enclosing statement so it must render before any same-offset child.
        if (_emittedLabels.Add(offset))
            sb.Append(pad).Append(LabelName(offset)).AppendLf(":");
    }

    string LabelName(int offset) => $"IL_{offset:X4}{_labelScopeSuffix}";

    string AllocateNestedLabelScopeSuffix()
        => $"{_labelScopeSuffix}_scope{++_nextNestedLabelScopeOrdinal}";

    LabelScopeState EnterNestedLabelScope(IrNode functionScope)
    {
        var state = new LabelScopeState(
            _labelTargets,
            _emittedLabels,
            _labelScopeSuffix);
        _labelTargets = CollectBranchTargets(functionScope);
        _emittedLabels = [];
        _labelScopeSuffix = AllocateNestedLabelScopeSuffix();
        return state;
    }

    void RestoreLabelScope(LabelScopeState state)
    {
        _labelTargets = state.Targets;
        _emittedLabels = state.Emitted;
        _labelScopeSuffix = state.Suffix;
    }

    readonly record struct LabelScopeState(
        HashSet<int> Targets,
        HashSet<int> Emitted,
        string Suffix);

    /// <summary>
    /// A statement node that renders only as a comment — an <c>// endfinally</c>/
    /// <c>// endfilter</c> EH marker, residual <c>cpblk</c>, or an unsupported
    /// <c>/* … */</c> node — so it is not a real C# statement. A label sitting
    /// before one stays unsatisfied: the trailing empty statement (';') must
    /// still follow to keep a labeled region legal (a label requires a statement;
    /// a bare label before a closing brace is <c>CS1525</c>).
    /// </summary>
    static bool RendersAsCommentOnly(IrNode node) => node switch
    {
        EndFinally or EndFilter => true,
        CopyBlock => true,
        UnsupportedNode => true,
        ExpressionStatement { Expression: UnsupportedNode } => true,
        _ => false,
    };

    static HashSet<int> CollectBranchTargets(IrNode functionScope)
        => ReferenceOwnership.CollectBranchTargets(functionScope);

    void AppendNestedLocalFunctionBody(StringBuilder sb, LocalFunctionStatement localFunction, int indent)
    {
        string pad = new(' ', indent * 4);
        foreach (var line in NestedLocalFunctionBodyText(localFunction).Split("\n"))
            sb.Append(pad).AppendLf(line);
    }

    string NestedLocalFunctionBodyText(LocalFunctionStatement localFunction)
    {
        var body = localFunction.Body;
        body.Detach();
        try
        {
            var function = new IrFunction(
                localFunction.Name,
                _function.DeclaringType,
                new MethodSignature(localFunction.ReturnType, localFunction.Parameters, HasThis: false, GenericParameterCount: 0),
                localFunction.Locals,
                body)
            {
                LocalNames = localFunction.LocalNames,
                SynthesizedLocalNames = localFunction.SynthesizedLocalNames,
                LocalDeclaredInNestedScope = localFunction.LocalDeclaredInNestedScope,
                LocalDeclarationBindings = localFunction.LocalDeclarationBindings,
                PdbLocalNameCandidates = localFunction.PdbLocalNameCandidates,
                LocalNameImportCauses = localFunction.LocalNameImportCauses,
                UsesUpdatedMemorySafetyRules = localFunction.UsesUpdatedMemorySafetyRules,
                SkipLocalsInit = localFunction.SkipLocalsInit,
                // The nested scope is metadata-free like the enclosing one; carry the
                // enclosing function's resolved type maps so an enum constant renders
                // by member name, not a bare int (issue #2983). LocalFunctionRaisingPass
                // merges each raised body's maps into the enclosing function, so these
                // include the definitions this local function references.
            };
            function.RestoreMaterializedStackSlotLocals(
                localFunction.MaterializedStackSlotLocals);
            function.RestoreResidualSlotBindings(
                localFunction.ResidualSlotBindings);
            function.ZeroInitializedLocals = localFunction.ZeroInitializedLocals;
            function.CopyTypeFactsFrom(_function);

            var nestedPrinter = new CSharpPrinter(
                function,
                _options,
                CurrentScopeNames(),
                decisions: _decisions,
                decisionKeys: _decisionKeys,
                fullyQualifyTypeNames: _fullyQualifyTypeNames)
            {
                _labelScopeSuffix = AllocateNestedLabelScopeSuffix(),
            };
            return nestedPrinter.PrintBody(function).TrimEnd();
        }
        finally
        {
            body.Detach();
            localFunction.ResetBody(body);
        }
    }

    /// <summary>
    /// Records the characters <paramref name="node"/> emits, then emits them.
    /// The range is taken from the builder's length either side of the call, so
    /// it costs nothing to capture and is exact by construction; recovering it
    /// afterwards would mean guessing. Wrapping rather than recording inline is
    /// what makes the end offset correct across every exit of the emission body.
    /// </summary>
    void AppendStatement(StringBuilder sb, IrNode node, int indent)
    {
        if (_printedRanges is null)
        {
            AppendStatementCore(sb, node, indent, out _);
            return;
        }
        int start = sb.Length;
        var enclosingContextRanges = _contextRanges;
        var enclosingContextualExpressions = _contextualExpressions;
        _contextRanges = null;
        _contextualExpressions = null;
        int? statementStartOverride;
        List<(IrNode Node, int Start, int End)>? contextRanges;
        Dictionary<(IrExpression Operand, string Kind), ContextualExpressionCapture>?
            contextualExpressions;
        try
        {
            AppendStatementCore(sb, node, indent, out statementStartOverride);
            contextRanges = _contextRanges;
            contextualExpressions = _contextualExpressions;
        }
        finally
        {
            _contextRanges = enclosingContextRanges;
            _contextualExpressions = enclosingContextualExpressions;
        }
        RecordExpressionRanges(sb, node, start, contextualExpressions);
        if (contextRanges is not null)
        {
            foreach (var contextRange in contextRanges)
                _printedRanges.Record(contextRange.Node, contextRange.Start, contextRange.End);
        }
        if (statementStartOverride is not null)
            _printedRanges.SetLineAnchor(node, start);
        int statementStart = statementStartOverride ?? start;
        // The wrapper contributes only the inner comment, not its indentation.
        if (node is ExpressionStatement { Expression: UnsupportedNode unsupported }
            && _printedRanges.TryGetRange(unsupported, out var unsupportedRange))
        {
            statementStart = unsupportedRange.Start.GetOffset(sb.Length);
        }
        _printedRanges.Record(node, statementStart, sb.Length);
        if (HasNamedRegions(node))
            _printedRanges.RecordRegion(PrintedRegionRole.Construct, start, sb.Length);
    }

    static bool HasNamedRegions(IrNode node)
        => node is ForLoop
            or WhileLoop
            or DoWhileLoop
            or TryCatch
            or Lock
            or Fixed
            or UsingStatement
            or ForeachStatement
            or TryFinally
            or IfStatement
            or Switch
            or SwitchBranch;

    /// <summary>
    /// Binds each expression under <paramref name="statement"/> to the characters
    /// it contributed, now that the statement sits at a known offset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A statement records its range by bracketing the builder, which is exact.
    /// An expression cannot: the expression printer composes text bottom-up by
    /// returning strings, so a node's characters do not exist at a known offset
    /// until an enclosing statement has been appended. What is knowable during
    /// composition is the text each node produced, which
    /// <see cref="Expression(IrExpression)"/> captures, and this rebases it.
    /// </para>
    /// <para>
    /// The binding is by unique occurrence <em>within the characters the node's
    /// parent claimed</em>. Searching the whole statement is not sound: emission
    /// can reformat a statement after its expressions were composed -- a fluent
    /// chain past the width budget is re-broken one call per line -- so a node's
    /// captured spelling may no longer occur at the node's own site, while an
    /// unrelated occurrence (inside a string literal, say) survives and becomes
    /// the only match. That yields a range that is precise and wrong, the one
    /// outcome worse than the coarse whole-statement underline this replaces.
    /// Constraining each node to its parent's window makes a claim structurally
    /// consistent by construction: reformatting breaks the chain at the first
    /// ancestor whose text no longer occurs, and every node beneath it refuses.
    /// </para>
    /// <para>
    /// Refusal is also the answer to ambiguity. Where a spelling repeats within
    /// the window -- <c>x + x</c>, two calls spelled identically -- composition
    /// order does not say which occurrence belongs to which node, so no range is
    /// recorded, the node stays absent from the map, and a consumer falls back to
    /// the enclosing statement, which is what it had before. A node that never
    /// had text captured claims nothing but does not block its children, since
    /// its silence is an absence of evidence rather than evidence of a rewrite.
    /// </para>
    /// <para>
    /// Claims are collected parent-first (<see cref="IrNode.Descendants"/> is
    /// pre-order, and a window must exist before anything is measured against it)
    /// and recorded so that a node follows every descendant. Siblings come out in
    /// <em>child</em> order, which is what walking <see cref="IrNode.Children"/>
    /// produces; it is <em>not</em> the order their characters appear, and the
    /// map's contract deliberately promises neither. Post-ordering holds for
    /// expression-inside-expression nesting, not merely for expressions
    /// inside their statement. Nested statements record earlier still -- they are
    /// appended while this statement's body runs, and <c>Record</c> keeps the
    /// first range a node is given -- so an expression inside a nested block is
    /// claimed against that block, where it is more likely to be unique. That is
    /// also why a structured statement's body enumerates before its condition.
    /// </para>
    /// <para>
    /// A target context can add a visible wrapper that has no IR owner while
    /// leaving the operand visible inside it. Such roots use a print-only
    /// identity and search within the operand parent's proven window. They are
    /// inserted after contained claims and before the first containing claim.
    /// </para>
    /// </remarks>
    void RecordExpressionRanges(
        StringBuilder sb,
        IrNode statement,
        int start,
        IReadOnlyDictionary<(IrExpression Operand, string Kind), ContextualExpressionCapture>?
            contextualExpressions)
    {
        if (sb.Length <= start
            || ((_expressionText is null || _expressionText.Count == 0)
                && contextualExpressions is not { Count: > 0 }))
            return;

        string text = sb.ToString(start, sb.Length - start);
        var windows = new Dictionary<IrNode, (int Start, int End)>();
        HashSet<IrNode>? refused = null;

        foreach (var descendant in statement.Descendants)
        {
            int windowStart = 0, windowEnd = text.Length;
            bool blocked = false;
            for (var parent = descendant.Parent;
                 parent is not null && !ReferenceEquals(parent, statement);
                 parent = parent.Parent)
            {
                if (windows.TryGetValue(parent, out var window))
                {
                    (windowStart, windowEnd) = window;
                    break;
                }
                if (refused?.Contains(parent) == true)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                (refused ??= []).Add(descendant);
                continue;
            }

            if (_expressionText is null
                || !_expressionText.TryGetValue(descendant, out string? printed)
                || printed.Length == 0)
                continue;

            int at = text.IndexOf(printed, windowStart, windowEnd - windowStart, StringComparison.Ordinal);
            if (at < 0
                || (at + 1 < windowEnd
                    && text.IndexOf(printed, at + 1, windowEnd - at - 1, StringComparison.Ordinal) >= 0))
            {
                (refused ??= []).Add(descendant);
                continue;
            }

            windows[descendant] = (at, at + printed.Length);
        }

        // Windows had to be computed parent-first, but a node must be recorded
        // after every descendant. Siblings come out in child order -- not in the
        // order their characters appear. Pushing children forward pops them
        // right-first, so reversing the walk yields exactly that.
        var pending = new Stack<IrNode>();
        var completion = new List<IrNode>();
        pending.Push(statement);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            completion.Add(node);
            foreach (var child in node.Children)
                pending.Push(child);
        }

        var claims = new List<(IrNode Node, int Start, int End)>();
        for (int i = completion.Count - 1; i >= 0; i--)
        {
            if (windows.TryGetValue(completion[i], out var claim))
                claims.Add((completion[i], claim.Start, claim.End));
        }

        if (contextualExpressions is { Count: > 0 })
        {
            var contextualClaims = new List<(IrNode Node, int Start, int End)>();
            foreach (var capture in contextualExpressions.Values)
            {
                int windowStart = 0, windowEnd = text.Length;
                bool blocked = false;
                for (var parent = capture.Operand.Parent;
                     parent is not null && !ReferenceEquals(parent, statement);
                     parent = parent.Parent)
                {
                    if (windows.TryGetValue(parent, out var window))
                    {
                        (windowStart, windowEnd) = window;
                        break;
                    }
                    if (refused?.Contains(parent) == true)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                    continue;

                int at = text.IndexOf(
                    capture.Text,
                    windowStart,
                    windowEnd - windowStart,
                    StringComparison.Ordinal);
                if (at < 0
                    || (at + 1 < windowEnd
                        && text.IndexOf(
                            capture.Text,
                            at + 1,
                            windowEnd - at - 1,
                            StringComparison.Ordinal) >= 0))
                {
                    continue;
                }

                contextualClaims.Add((capture.Node, at, at + capture.Text.Length));
            }

            foreach (var contextual in contextualClaims
                .OrderBy(claim => claim.End - claim.Start))
            {
                int insertion = claims.FindIndex(claim =>
                    claim.Start <= contextual.Start
                    && claim.End >= contextual.End
                    && (claim.Start < contextual.Start || claim.End > contextual.End));
                if (insertion < 0)
                    claims.Add(contextual);
                else
                    claims.Insert(insertion, contextual);
            }
        }

        foreach (var claim in claims)
        {
            _printedRanges!.Record(
                claim.Node,
                start + claim.Start,
                start + claim.End);
        }
    }

    /// <summary>Recursive statement emission with indentation — structured nodes (IfStatement) nest, flat statements render through <see cref="Statement"/>.</summary>
    void AppendStatementCore(
        StringBuilder sb,
        IrNode node,
        int indent,
        out int? statementStartOverride)
    {
        statementStartOverride = null;
        _statementIndent = indent;
        string pad = new(' ', indent * 4);
        if (node is Block lexicalBlock)
        {
            sb.Append(pad).AppendLf("{");
            AppendStatements(sb, lexicalBlock.Children, indent + 1);
            sb.Append(pad).AppendLf("}");
            return;
        }
        if (node is Return && IsSharedScopeLambdaReturn(node))
        {
            sb.Append(pad).AppendLf(LambdaStatement(node)!);
            return;
        }
        if (node is LocalFunctionStatement localFunction)
        {
            string modifier = $"{(localFunction.IsStatic ? "static " : "")}{(localFunction.RequiresUnsafe ? "unsafe " : "")}";
            string parameters = string.Join(
                ", ",
                localFunction.Parameters.Select((parameter, index) =>
                    $"{ParameterTypeText(parameter, index < localFunction.ParameterRefKinds.Length ? localFunction.ParameterRefKinds[index] : ArgumentRefKind.Value)} {CSharpNaming.ContainedIdentifier(parameter.DisplayName)}"));
            string header = $"{modifier}{TypeText(localFunction.ReturnType)} {CSharpNaming.ContainedIdentifier(localFunction.Name)}({parameters})";
            IrExpression? expressionBody = localFunction.ExpressionBody;
            bool expressionNeedsUnsafeContext = expressionBody is not null
                && EmitsExplicitUnsafeContexts
                && (NeedsExplicitUnsafeContext(expressionBody)
                    || (localFunction.ReturnType.Kind == TypeRefKind.ByRef && RendersAsPointerDeref(expressionBody)));
            bool expressionNeedsUnsafeBlock = expressionNeedsUnsafeContext
                && (!_newMemorySafetyRules
                    || localFunction.ReturnType is { Namespace: "System", Name: "Void" }
                    || !UnsafeExpressionCompilerSupports(expressionBody!));
            if (localFunction.ExpressionBody is { } body && !expressionNeedsUnsafeBlock)
            {
                string expressionText = localFunction.ReturnType.Kind == TypeRefKind.ByRef
                    && ArgumentLvalue(body) is { } place
                        ? $"ref {UnsafeExpressionText(
                            body,
                            place,
                            force: RendersAsPointerDeref(body))}"
                        : UnsafeExpressionText(body, Expression(body));
                sb.Append(pad).Append(header).Append(" => ").Append(expressionText).AppendLf(";");
            }
            else
            {
                sb.Append(pad).AppendLf(header);
                sb.Append(pad).AppendLf("{");
                if (localFunction.NeedsIsolatedLocalScope)
                    AppendNestedLocalFunctionBody(sb, localFunction, indent + 1);
                else
                {
                    var enclosingReturnType = _returnTypeOverride;
                    var enclosingLabelScope = EnterNestedLabelScope(localFunction);
                    _returnTypeOverride = localFunction.ReturnType;
                    try
                    {
                        AppendContainer(sb, localFunction.Body, indent + 1);
                        if (NeedsUnsupportedFallbackReturn(localFunction.ReturnType, requiresAsyncBodyModifier: false, localFunction.Body))
                            sb.Append(new string(' ', (indent + 1) * 4)).AppendLf("return default;");
                    }
                    finally
                    {
                        _returnTypeOverride = enclosingReturnType;
                        RestoreLabelScope(enclosingLabelScope);
                    }
                }
                sb.Append(pad).AppendLf("}");
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
            sb.Append(pad).Append("return ");
            int switchStart = sb.Length;
            sb.Append(Operand(returnedSwitch.Value)).AppendLf(" switch");
            sb.Append(pad).AppendLf("{");
            foreach (var arm in returnedSwitch.Arms)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(SwitchArmText(arm, CurrentReturnType, labelEnum));
                CaptureContextRange(arm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            sb.Append(pad).Append("}");
            CaptureContextRange(returnedSwitch, switchStart, sb.Length);
            sb.AppendLf(";");
            return;
        }
        if (node is Return { Value: UnionSwitchExpression unionSwitch })
        {
            string inner = pad + "    ";
            sb.Append(pad).Append("return ");
            int switchStart = sb.Length;
            sb.Append(UnionSwitchReceiverText(unionSwitch.Value)).AppendLf(" switch");
            sb.Append(pad).AppendLf("{");
            if (unionSwitch.NullArm is { } nullArm)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(SynthesizedSwitchArmText(nullArm, CurrentReturnType));
                CaptureContextRange(nullArm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            foreach (var arm in unionSwitch.Arms)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(UnionSwitchArmText(arm, CurrentReturnType));
                CaptureContextRange(arm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            if (unionSwitch.DefaultArm is { } defaultArm)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(SynthesizedSwitchArmText(defaultArm, CurrentReturnType));
                CaptureContextRange(defaultArm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            sb.Append(pad).Append("}");
            CaptureContextRange(unionSwitch, switchStart, sb.Length);
            sb.AppendLf(";");
            return;
        }
        if (node is Return { Value: PatternSwitchExpression patternSwitch })
        {
            // Mirrors the UnionSwitchExpression return-position form above: one
            // arm per line, indented under the governing receiver, with an
            // optional trailing `_ => default` arm.
            string inner = pad + "    ";
            sb.Append(pad).Append("return ");
            int switchStart = sb.Length;
            sb.Append(Operand(patternSwitch.Value)).AppendLf(" switch");
            sb.Append(pad).AppendLf("{");
            foreach (var arm in patternSwitch.Arms)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(PatternSwitchArmText(arm, CurrentReturnType));
                CaptureContextRange(arm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            if (patternSwitch.DefaultArm is { } defaultArm)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(SynthesizedSwitchArmText(defaultArm, CurrentReturnType));
                CaptureContextRange(defaultArm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            sb.Append(pad).Append("}");
            CaptureContextRange(patternSwitch, switchStart, sb.Length);
            sb.AppendLf(";");
            return;
        }
        if (node is Return { Value: TupleSwitchExpression tupleSwitch })
        {
            // Mirrors the SwitchExpression/UnionSwitchExpression return-position
            // forms above: one arm per line, indented under the governing tuple.
            string inner = pad + "    ";
            var componentTypes = TupleSwitchComponentTypes(tupleSwitch);
            sb.Append(pad).Append("return ");
            int switchStart = sb.Length;
            sb.Append(TupleSwitchGoverningValueText(tupleSwitch)).AppendLf(" switch");
            sb.Append(pad).AppendLf("{");
            foreach (var arm in tupleSwitch.Arms)
            {
                sb.Append(inner);
                int armStart = sb.Length;
                sb.Append(TupleSwitchArmText(arm, componentTypes, CurrentReturnType));
                CaptureContextRange(arm, armStart, sb.Length);
                sb.AppendLf(",");
            }
            sb.Append(pad).Append("}");
            CaptureContextRange(tupleSwitch, switchStart, sb.Length);
            sb.AppendLf(";");
            return;
        }
        if (node is Return { Value: StackAllocate stackAllocate }
            && CurrentReturnType is { Kind: TypeRefKind.Pointer } returnPointer)
        {
            string localName = FreshSyntheticLocalName("__stackalloc");
            sb.Append(pad)
                .Append(TypeText(stackAllocate.ResultType!))
                .Append(' ')
                .Append(localName)
                .Append(" = ")
                .Append(Expression(stackAllocate))
                .AppendLf(";");
            string value = returnPointer.Equals(stackAllocate.ResultType)
                ? localName
                : $"({TypeText(returnPointer)}){localName}";
            statementStartOverride = sb.Length;
            sb.Append(pad).Append("return ").Append(value).AppendLf(";");
            return;
        }
        if (node is StoreLocal { Value: StackAllocate storeStackAllocate, Type.Kind: TypeRefKind.Pointer } store
            && store.Type is { } storeType
            && storeStackAllocate.ResultType is { } stackAllocType)
        {
            string localName = FreshSyntheticLocalName("__stackalloc");
            sb.Append(pad)
                .Append(TypeText(stackAllocType))
                .Append(' ')
                .Append(localName)
                .Append(" = ")
                .Append(Expression(storeStackAllocate))
                .AppendLf(";");
            statementStartOverride = sb.Length;
            sb.Append(pad);
            if (_declaringStores.Contains(store))
                sb.Append(TypeText(storeType)).Append(' ');

            string cast = storeType.Equals(stackAllocType) ? "" : $"({TypeText(storeType)})";

            sb.Append(LocalName(store.Index))
                .Append(" = ")
                .Append(cast)
                .Append(localName)
                .AppendLf(";");
            return;
        }
        if (node is ForLoop forLoop)
        {
            string initializer = forLoop.Initializer is PointerCompoundAssignment update
                ? PointerUpdateText(update, statement: false)
                : Statement(forLoop.Initializer, forHeader: true)?.TrimEnd(';') ?? "";
            string increment = ForLoopIncrementText(forLoop.Increment);
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append("for (").Append(initializer).Append("; ")
                .Append(UnsafeConditionText(forLoop.Condition)).Append("; ").Append(increment).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendStatements(sb, forLoop.Body.Children, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            return;
        }
        if (node is WhileLoop whileLoop)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append("while (").Append(UnsafeConditionText(whileLoop.Condition)).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendStatements(sb, whileLoop.Body.Children, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            return;
        }
        if (node is DoWhileLoop doWhile)
        {
            sb.Append(pad).AppendLf("do");
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendContainer(sb, doWhile.Body, indent + 1);
            // The body's own AppendStatement calls left _statementIndent at the
            // deepest nested statement's level; restore it to this statement's
            // own indent before the condition (itself part of this statement,
            // not the body) renders, so a lambda inside it aligns correctly.
            _statementIndent = indent;
            sb.Append(pad).Append("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            sb.Append("\n").Append(pad);
            int headerStart = sb.Length;
            sb.Append("while (").Append(UnsafeConditionText(doWhile.Condition)).AppendLf(");");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            return;
        }
        if (node is TryCatch tryCatch)
        {
            sb.Append(pad).AppendLf("try");
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendContainer(sb, tryCatch.TryBody, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            foreach (var clause in tryCatch.Clauses)
            {
                // As in the do/while condition above, a preceding body (the try
                // body, or an earlier catch's body) leaves _statementIndent
                // deeper than this statement's own indent; restore it before
                // CatchHeader (which may render a filter's `when (...)` lambda).
                _statementIndent = indent;
                sb.Append(pad);
                int catchStart = sb.Length;
                sb.AppendLf(CatchHeader(clause));
                sb.Append(pad).AppendLf("{");
                AppendContainer(sb, clause.Body, indent + 1);
                sb.Append(pad).AppendLf("}");
                _printedRanges?.RecordRegion(PrintedRegionRole.Catch, catchStart, sb.Length);
            }
            return;
        }
        if (node is Lock lockStatement)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append("lock (").Append(UnsafeExpressionText(
                lockStatement.LockObject,
                Expression(lockStatement.LockObject))).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendContainer(sb, lockStatement.Body, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            return;
        }
        if (node is Fixed fixedStatement)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append("fixed (").Append(TypeText(fixedStatement.ElementType)).Append("* ")
                .Append(FixedLocalName(fixedStatement)).Append(" = ")
                .Append(UnsafeExpressionText(
                    fixedStatement.PinSource,
                    fixedStatement.SourceIsAddress
                    ? "&" + Deref(fixedStatement.PinSource)
                    : Expression(fixedStatement.PinSource)))
                .AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendContainer(sb, fixedStatement.Body, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            return;
        }
        if (node is UsingStatement usingStatement)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append(usingStatement.IsAwait ? "await using (" : "using (");
            if (usingStatement.DeclaresResourceVariable)
                sb.Append(TypeText(usingStatement.ResourceType)).Append(' ').Append(LocalName(usingStatement.LocalIndex)).Append(" = ");
            sb.Append(UnsafeExpressionText(
                usingStatement.Resource,
                UsingResourceText(usingStatement))).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendContainer(sb, usingStatement.Body, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            return;
        }
        if (node is ForeachStatement foreachStatement)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append(foreachStatement.IsAwait ? "await foreach (" : "foreach (")
                .Append(TypeText(foreachStatement.LocalType)).Append(' ')
                .Append(LocalName(foreachStatement.LocalIndex)).Append(" in ")
                .Append(UnsafeExpressionText(
                    foreachStatement.Collection,
                    Expression(foreachStatement.Collection))).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendStatements(sb, foreachStatement.Body.Children, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            return;
        }
        if (node is TryFinally tryFinally)
        {
            sb.Append(pad).AppendLf("try");
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendContainer(sb, tryFinally.TryBody, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            sb.Append(pad);
            int finallyStart = sb.Length;
            sb.AppendLf("finally");
            sb.Append(pad).AppendLf("{");
            AppendContainer(sb, tryFinally.FinallyBody, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Finally, finallyStart, sb.Length);
            return;
        }
        if (node is IfStatement ifStatement)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append("if (").Append(UnsafeConditionText(ifStatement.Condition)).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            AppendStatements(sb, ifStatement.Then.Children, indent + 1);
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
            if (ifStatement.Else is { } elseArm)
            {
                sb.Append(pad);
                int elseStart = sb.Length;
                sb.AppendLf("else");
                sb.Append(pad).AppendLf("{");
                AppendStatements(sb, elseArm.Children, indent + 1);
                sb.Append(pad).AppendLf("}");
                _printedRanges?.RecordRegion(PrintedRegionRole.Else, elseStart, sb.Length);
            }
            return;
        }
        if (node is Switch switchNode)
        {
            sb.Append(pad);
            int headerStart = sb.Length;
            sb.Append("switch (").Append(UnsafeExpressionText(
                switchNode.Value,
                Expression(switchNode.Value))).AppendLf(")");
            _printedRanges?.RecordRegion(PrintedRegionRole.Header, headerStart, sb.Length);
            sb.Append(pad);
            int bodyStart = sb.Length;
            sb.AppendLf("{");
            string labelPad = pad + "    ";
            var labelEnum = SwitchLabelEnumType(switchNode.Value);
            foreach (var section in switchNode.Sections)
            {
                // A prior section's body (AppendContainer below) leaves
                // _statementIndent deeper than this statement's own indent;
                // restore it before this section's own labels render (a `when`
                // pattern guard could, in principle, contain a lambda).
                _statementIndent = indent;
                int caseStart = -1;
                foreach (var label in SwitchLabelsForRendering(section, labelEnum))
                {
                    sb.Append(labelPad);
                    if (caseStart < 0)
                        caseStart = sb.Length;
                    sb.Append("case ").Append(SwitchLabelText(label, labelEnum)).AppendLf(":");
                }
                if (section.IsDefault)
                {
                    sb.Append(labelPad);
                    if (caseStart < 0)
                        caseStart = sb.Length;
                    sb.AppendLf("default:");
                }
                if (caseStart < 0)
                    caseStart = sb.Length;
                AppendContainer(sb, section.Body, indent + 2);
                _printedRanges?.RecordRegion(PrintedRegionRole.Case, caseStart, sb.Length);
            }
            sb.Append(pad).AppendLf("}");
            _printedRanges?.RecordRegion(PrintedRegionRole.Body, bodyStart, sb.Length);
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
            string temp = _switchTemps.TryGetValue(switchBranch, out var name) ? name : "__switchValue";
            string value = UnsafeExpressionText(
                switchBranch.Value,
                Expression(switchBranch.Value));
            sb.Append(pad).Append(temp).Append(" = ")
                .Append("(int)(").Append(value).AppendLf(");");
            for (int t = 0; t < switchBranch.TargetOffsets.Length; t++)
            {
                sb.Append(pad);
                int caseStart = sb.Length;
                sb.Append("if (").Append(temp).Append(" == ").Append(t)
                    .Append(") goto ").Append(LabelName(switchBranch.TargetOffsets[t])).AppendLf(";");
                _printedRanges?.RecordRegion(PrintedRegionRole.Case, caseStart, sb.Length);
            }
            return;
        }
        if (Statement(node) is { } line)
        {
            if (!TryAppendFluentChain(sb, node, line, indent)
                && !TryAppendSplittableExpression(sb, node, line, indent)
                && !TryAppendBitwiseChain(sb, node, line, indent)
                && !TryAppendBraceBody(sb, node, line, indent))
                sb.Append(pad).AppendLf(line);
        }
    }

    /// <summary>
    /// Emits a sibling statement sequence. Updated-rules statements whose unsafe
    /// work is fully contained by one rendered value/header expression use
    /// <c>unsafe(expr)</c>; the remaining statements coalesce into minimal
    /// <c>unsafe { }</c> runs that preserve scope and data flow. A loop or
    /// <c>if</c> whose body (not its header) holds the unsafe op is left
    /// unwrapped so recursion contains the inner statement instead.
    /// </summary>
    void AppendStatements(StringBuilder sb, IReadOnlyList<IrNode> statements, int indent)
    {
        bool hasGeneratedUnsafeSetupBoundary =
            HasGeneratedUnsafeSetupBoundary(statements);
        bool semanticSpacing = HasSemanticSpacingCandidates(
                statements,
                hasGeneratedUnsafeSetupBoundary)
            && (statements.FirstOrDefault()?.Parent is not Block block
                || !_labelTargets.Contains(block.StartOffset));
        var spacingState = new SemanticSpacingState(semanticSpacing);
        AppendStatements(
            sb,
            statements,
            indent,
            ref spacingState);
    }

    void AppendStatements(
        StringBuilder sb,
        IReadOnlyList<IrNode> statements,
        int indent,
        ref SemanticSpacingState spacingState)
    {
        int i = 0;
        int separatorAlreadyAppliedAt = -1;
        while (i < statements.Count)
        {
            if (EmitsExplicitUnsafeContexts
                && _unsafeDepth == 0
                && NeedsUnsafeBlock(statements[i]))
            {
                int j = UnsafeRunEnd(statements, i);
                string pad = new(' ', indent * 4);
                int firstVisible = -1;
                for (int k = i; k < j; k++)
                {
                    if (IsVisibleStatement(statements[k]))
                    {
                        firstVisible = k;
                        break;
                    }
                }
                if (firstVisible >= 0)
                {
                    AppendSemanticSeparator(
                        sb,
                        statements[firstVisible],
                        spacingState);
                }
                else
                {
                    for (int k = j; k < statements.Count; k++)
                    {
                        if (!IsVisibleStatement(statements[k]))
                            continue;

                        AppendSemanticSeparator(
                            sb,
                            statements[k],
                            spacingState);
                        separatorAlreadyAppliedAt = k;
                        break;
                    }
                }
                sb.Append(pad).AppendLf("unsafe");
                sb.Append(pad).AppendLf("{");
                _unsafeDepth++;
                for (int k = i; k < j; k++)
                {
                    bool visible = IsVisibleStatement(statements[k]);
                    if (visible && k != firstVisible)
                    {
                        AppendSemanticSeparator(
                            sb,
                            statements[k],
                            spacingState);
                    }
                    AppendStatementLabel(sb, statements[k], indent + 1);
                    AppendStatement(sb, statements[k], indent + 1);
                    if (visible)
                        UpdateSemanticSpacingState(statements[k], ref spacingState);
                }
                _unsafeDepth--;
                sb.Append(pad).AppendLf("}");
                i = j;
            }
            else
            {
                bool visible = IsVisibleStatement(statements[i]);
                if (visible && i != separatorAlreadyAppliedAt)
                {
                    AppendSemanticSeparator(
                        sb,
                        statements[i],
                        spacingState);
                }
                AppendStatementLabel(sb, statements[i], indent);
                AppendStatement(sb, statements[i], indent);
                if (visible)
                    UpdateSemanticSpacingState(statements[i], ref spacingState);
                i++;
            }
        }
    }

    bool IsVisibleStatement(IrNode statement)
    {
        if (RendersAsCommentOnly(statement))
            return false;

        return statement switch
        {
            ExpressionStatement { Expression: Call call }
                when IsImplicitParameterlessBaseCall(call) => false,
            _ => true,
        };
    }

    bool RendersSourceLabel(IrNode statement)
        => statement.OwnsSourceLabel
            && statement.SourceOffset >= 0
            && _labelTargets.Contains(statement.SourceOffset);

    bool HasSemanticSpacingCandidates(
        IEnumerable<IrNode> statements,
        bool hasGeneratedUnsafeSetupBoundary)
    {
        int visibleStatements = 0;
        foreach (var statement in statements)
        {
            if (RendersSourceLabel(statement))
                return false;
            if (IsVisibleStatement(statement))
                visibleStatements++;
        }
        return visibleStatements >= 5 || hasGeneratedUnsafeSetupBoundary;
    }

    // Five visible siblings keeps compact bodies compact. Longer structured
    // lists separate leading/terminating one-arm conditionals, substantial setup
    // prefixes, and adjacent major constructs. Generated unsafe blocks with the
    // same setup contour also qualify. Labeled lowered output declines the
    // policy because its control-flow contour is explicit.
    void AppendSemanticSeparator(
        StringBuilder sb,
        IrNode current,
        SemanticSpacingState state)
    {
        if (!state.Enabled || state.Previous is null)
            return;

        if (state.PreviousCompletedConditional
            || IsMajorControlFlow(current)
                && (IsMajorControlFlow(state.Previous)
                    || state.ConsecutiveSetupStatements >= 2))
        {
            sb.AppendLf();
        }
    }

    bool HasGeneratedUnsafeSetupBoundary(IReadOnlyList<IrNode> statements)
    {
        if (!EmitsExplicitUnsafeContexts || _unsafeDepth != 0)
            return false;

        int i = 0;
        while (i < statements.Count)
        {
            if (!NeedsUnsafeBlock(statements[i]))
            {
                i++;
                continue;
            }

            int end = UnsafeRunEnd(statements, i);
            int setupStatements = 0;
            for (int k = i; k < end; k++)
            {
                if (!IsVisibleStatement(statements[k]))
                    continue;

                if (IsMajorControlFlow(statements[k]))
                {
                    if (setupStatements >= 2)
                        return true;
                    setupStatements = 0;
                }
                else
                {
                    setupStatements++;
                }
            }
            for (int k = end; setupStatements >= 2 && k < statements.Count; k++)
            {
                if (!IsVisibleStatement(statements[k]))
                    continue;
                if (IsMajorControlFlow(statements[k]))
                    return true;
                break;
            }
            i = end;
        }

        return false;
    }

    static bool IsCompletedConditionalGroup(IrNode statement, bool leading)
        => statement is IfStatement { HasElse: false } conditional
            && (leading
                || conditional.Then.Children.LastOrDefault()
                    is Return
                        or Throw
                        or Break
                        or Continue
                        or Branch
                        or Leave
                        or YieldBreak);

    static bool IsMajorControlFlow(IrNode statement)
        => statement is IfStatement
            or Switch
            or WhileLoop
            or DoWhileLoop
            or ForLoop
            or TryCatch
            or TryFinally
            or Lock
            or Fixed
            or UsingStatement
            or ForeachStatement;

    void UpdateSemanticSpacingState(
        IrNode statement,
        ref SemanticSpacingState state)
    {
        state.PreviousCompletedConditional =
            IsCompletedConditionalGroup(statement, state.EmittedStatements == 0);
        state.ConsecutiveSetupStatements = IsMajorControlFlow(statement)
            ? 0
            : state.ConsecutiveSetupStatements + 1;
        state.Previous = statement;
        state.EmittedStatements++;
    }

    struct SemanticSpacingState(bool enabled)
    {
        public bool Enabled { get; } = enabled;
        public IrNode? Previous { get; set; }
        public bool PreviousCompletedConditional { get; set; }
        public int ConsecutiveSetupStatements { get; set; }
        public int EmittedStatements { get; set; }
    }

    void AppendStatementLabel(StringBuilder sb, IrNode statement, int indent)
    {
        if (statement.OwnsSourceLabel
            && statement.SourceOffset >= 0
            && _labelTargets.Contains(statement.SourceOffset))
            AppendLabel(sb, new string(' ', indent * 4), statement.SourceOffset);
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
        if (IsImplicitParameterlessBaseCall(call))
            return null;  // implicit base()
        var arguments = call.Arguments.Skip(1).ToList();
        return $"{(isThis ? "this" : "base")}({Arguments(
            arguments,
            callee.ParameterTypes,
            callee.ParameterRefKinds,
            explicitIn: true,
            chainFidelityCasts: true,
            unsafeExpressions: true)});";
    }

    bool IsImplicitParameterlessBaseCall(Call call)
        => call.Callee is { Name: ".ctor", HasThis: true } callee
            && !Equals(callee.DeclaringType, _function.DeclaringType)
            && call.Arguments.Count == 1;

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
            if (node is LoadArgument or LoadLocal or LoadLocalAddress or LoadArgumentAddress)
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
        return clause.Filter is { } filter ? $"{header} when ({UnsafeConditionText(filter)})" : header;
    }

    /// <summary>Null means the statement has no body spelling: a no-argument base-constructor call is implicit in C#.</summary>
    /// <summary>
    /// Emits <paramref name="node"/> as a broken fluent chain (one call per line)
    /// when it is a chain-valued statement long enough to wrap, returning true
    /// after appending; false to fall through to the flat single-line emit. The
    /// broken form is only chosen when the flat statement <paramref name="line"/>
    /// is exactly <c>prefix + chain + ";"</c>, so any coercion cast, compound
    /// assignment, ref rebind, or discard the renderer added around the chain
    /// keeps the statement inline — breaking never drops or reshapes a token.
    /// </summary>
    bool TryAppendFluentChain(StringBuilder sb, IrNode node, string line, int indent)
    {
        if (_options.DisableOneLinerWrapping)
            return false;
        if (!TryFluentChainStatement(node, out var root, out var prefix))
            return false;
        if (line != prefix + CallText(root) + ";")
            return false;
        if (FluentChainLines(root, prefix, ";", indent) is not { } broken)
            return false;
        sb.AppendLf(broken);
        return true;
    }

    /// <summary>
    /// Recognizes the statement positions whose value is a bare instance-call
    /// chain — an expression statement, a <c>return</c>, or a (non-ref) local or
    /// stack-slot store — and yields the chain root plus the exact statement
    /// prefix the flat renderer prints before it. The caller re-derives the flat
    /// text and only breaks the chain when it matches, so the prefix here need
    /// only cover the common (cast-free) spelling.
    /// </summary>
    bool TryFluentChainStatement(IrNode node, out Call root, out string prefix)
    {
        switch (node)
        {
            case ExpressionStatement { Expression: Call { Callee.Name: not ".ctor" } call } when IsStatementExpression(call):
                root = call;
                prefix = "";
                return true;
            case Return { Value: Call call }:
                root = call;
                prefix = "return ";
                return true;
            case StoreLocal { Type.Kind: not TypeRefKind.ByRef, Value: Call call } store:
                root = call;
                prefix = _declaringStores.Contains(store)
                    ? $"{DeclarationTypeText(store.Type, store.Value)} {LocalName(store.Index)} = "
                    : $"{LocalName(store.Index)} = ";
                return true;
            default:
                root = null!;
                prefix = "";
                return false;
        }
    }

    /// <summary>
    /// Emits <paramref name="node"/> as a wrapped brace-bodied expression — an
    /// object/collection initializer (<c>new T(...) { ... }</c>), a record
    /// <c>with</c> expression (<c>receiver with { ... }</c>), or an anonymous object
    /// (<c>new { ... }</c>) — with the head on the first line, an Allman <c>{</c>/<c>}</c>
    /// pair on their own lines, and one entry per line, when it is a brace-bodied
    /// statement whose flat single-line form would exceed
    /// <see cref="FluentChainWrapWidth"/>, returning true after appending; false to
    /// fall through to the flat single-line emit. The broken form is only chosen
    /// when the flat statement <paramref name="line"/> is exactly
    /// <c>prefix + body + ";"</c>, so any coercion cast the renderer added around the
    /// body keeps the statement inline — breaking never drops or reshapes a token.
    /// </summary>
    bool TryAppendBraceBody(StringBuilder sb, IrNode node, string line, int indent)
    {
        if (_options.DisableOneLinerWrapping)
            return false;
        if (!TryBraceBodyStatement(node, out var value, out var prefix))
            return false;
        string flat = value switch
        {
            ObjectInitializerExpression initializer => ObjectInitializerText(initializer),
            WithExpression with => WithExpressionText(with),
            AnonymousObject anonymous => AnonymousObjectText(anonymous),
            _ => null!,
        };
        if (line != prefix + flat + ";")
            return false;
        string? broken = value switch
        {
            ObjectInitializerExpression initializer => ObjectInitializerLines(initializer, prefix, ";", indent),
            WithExpression with => WithExpressionLines(with, prefix, ";", indent),
            AnonymousObject anonymous => AnonymousObjectLines(anonymous, prefix, ";", indent),
            _ => null,
        };
        if (broken is null)
            return false;
        sb.AppendLf(broken);
        return true;
    }

    /// <summary>
    /// Recognizes the statement positions whose value is a bare brace-bodied
    /// expression — an object/collection initializer, a record <c>with</c>
    /// expression, or an anonymous object — in a <c>return</c> or a (non-ref) local
    /// or stack-slot store, and yields the value plus the exact statement prefix the
    /// flat renderer prints before it. The caller re-derives the flat text and only
    /// breaks the body when it matches, so the prefix here need only cover the common
    /// (cast-free) spelling.
    /// </summary>
    bool TryBraceBodyStatement(IrNode node, out IrExpression value, out string prefix)
    {
        switch (node)
        {
            case Return { Value: ObjectInitializerExpression or WithExpression or AnonymousObject } ret:
                value = ret.Value!;
                prefix = "return ";
                return true;
            case StoreLocal { Type.Kind: not TypeRefKind.ByRef, Value: ObjectInitializerExpression or WithExpression or AnonymousObject } store:
                value = store.Value;
                prefix = _declaringStores.Contains(store)
                    ? $"{DeclarationTypeText(store.Type, store.Value)} {LocalName(store.Index)} = "
                    : $"{LocalName(store.Index)} = ";
                return true;
            default:
                value = null!;
                prefix = "";
                return false;
        }
    }

    /// <summary>
    /// Emits <paramref name="node"/> as a wrapped short-circuit
    /// <c>&amp;&amp;</c>/<c>||</c> chain (one operand per line) when
    /// <see cref="PrinterOptions.WrapSplittableExpressions"/> is set and the chain
    /// is long enough, returning true after appending; false to fall through to
    /// the flat single-line emit. Chosen only when the flat statement
    /// <paramref name="line"/> is exactly <c>prefix + chain + ";"</c>, so any
    /// coercion cast, compound assignment, or property-pattern rewrite the
    /// renderer applied around the chain keeps the statement inline — wrapping
    /// never drops or reshapes a token.
    /// </summary>
    bool TryAppendSplittableExpression(StringBuilder sb, IrNode node, string line, int indent)
    {
        if (!_options.WrapSplittableExpressions)
            return false;
        if (!TryLogicalChainStatement(node, out var root, out var prefix))
            return false;
        if (line != prefix + LogicalText(root) + ";")
            return false;
        if (LogicalChainLines(root, prefix, ";", indent) is not { } broken)
            return false;
        AddDecision(
            "expression.wrap-splittable-chain",
            "taste",
            _function.Name,
            $"Wrapped a long short-circuit {(root.Kind == LogicalKind.And ? "&&" : "||")} chain across continuation lines.");
        sb.AppendLf(broken);
        return true;
    }

    /// <summary>
    /// The bitwise analog of <see cref="TryAppendSplittableExpression"/>: breaks a
    /// long top-level <c>|</c>/<c>&amp;</c>/<c>^</c> chain one operand per line, the
    /// operator <em>leading</em> each continuation line (the flags-accumulation
    /// house style), when the opt-in <see cref="PrinterOptions.WrapSplittableExpressions"/>
    /// taste is on. Unlike the short-circuit wrapper it splices the broken chain into
    /// the already-rendered statement <paramref name="line"/> in place, so it handles
    /// a bare chain (<c>return a | b | …</c>), an assignment, and a chain inside a
    /// value-preserving cast (<c>return (int)(a | b | …)</c>) uniformly — the flat
    /// chain text is located verbatim in the line and only its interior separators
    /// are broken, so the wrapped form is a pure whitespace variant of the inline
    /// form (same tokens, unchanged IL).
    /// </summary>
    bool TryAppendBitwiseChain(StringBuilder sb, IrNode node, string line, int indent)
    {
        if (!_options.WrapSplittableExpressions)
            return false;
        if (FindBitwiseChainRoot(node) is not { } root)
            return false;
        if (BitwiseChainLines(root, line, indent) is not { } broken)
            return false;
        AddDecision(
            "expression.wrap-splittable-chain",
            "taste",
            _function.Name,
            $"Wrapped a long bitwise {BinaryOperator(root)} chain across continuation lines.");
        sb.AppendLf(broken);
        return true;
    }

    /// <summary>
    /// Yields the top-level associative bitwise <c>|</c>/<c>&amp;</c>/<c>^</c> chain
    /// root of a statement whose value is such a chain — a <c>return</c> or a
    /// (non-ref) local or stack-slot store — seeing through any value-preserving cast
    /// wrappers (<c>return (int)(a | b | …)</c>, common when a fully-raised flags-enum
    /// accumulation is returned as its underlying integer). Arithmetic
    /// <c>+</c>/<c>-</c>/… are excluded: they are not the flags-accumulation idiom
    /// this wraps, and subtraction is not associative for a one-operand-per-line
    /// display. Returns null otherwise.
    /// </summary>
    Binary? FindBitwiseChainRoot(IrNode node)
    {
        IrExpression? value = node switch
        {
            Return { Value: { } returned } => returned,
            StoreLocal { Type.Kind: not TypeRefKind.ByRef, Value: { } stored } => stored,
            _ => null,
        };
        while (value is Convert convert)
            value = convert.Operand;
        return value is Binary { Kind: BinaryKind.Or or BinaryKind.And or BinaryKind.Xor } binary ? binary : null;
    }

    /// <summary>
    /// Recognizes the statement positions whose value is a top-level short-circuit
    /// <c>&amp;&amp;</c>/<c>||</c> chain — a <c>return</c> or a (non-ref) local or
    /// stack-slot store — and yields the chain root plus the exact statement prefix
    /// the flat renderer prints before it. (A bare chain as an expression statement
    /// is CS0201, so there is no expression-statement case.) The caller re-derives
    /// the flat text and only wraps when it matches, so the prefix here need only
    /// cover the common (cast-free) spelling.
    /// </summary>
    bool TryLogicalChainStatement(IrNode node, out LogicalBinary root, out string prefix)
    {
        switch (node)
        {
            case Return { Value: LogicalBinary logical }:
                root = logical;
                prefix = "return ";
                return true;
            case StoreLocal { Type.Kind: not TypeRefKind.ByRef, Value: LogicalBinary logical } store:
                root = logical;
                prefix = _declaringStores.Contains(store)
                    ? $"{DeclarationTypeText(store.Type, store.Value)} {LocalName(store.Index)} = "
                    : $"{LocalName(store.Index)} = ";
                return true;
            default:
                root = null!;
                prefix = "";
                return false;
        }
    }

    string? Statement(IrNode node, bool forHeader = false) => node switch
    {
        LabelAnchor => ";",
        ExpressionStatement
        {
            Expression: Call { Callee: { Name: ".ctor", HasThis: true } callee } call,
        } when call.Arguments is [_, ..]
            => ConstructorChainText(callee, call),
        ExpressionStatement e => e.Expression switch
        {
            UnsupportedNode u => UnsupportedStatement(e, u),
            { } expr when ShouldDiscardForUnsafeExpression(expr)
                => DiscardStatement(e, expr),
            // A safe user-defined checked ++/-- as a statement spells
            // checked(x++), which is CS0201 in statement position; use a
            // checked { ... } block. Unsafe-required operators take the
            // discard-expression path above so one wrapper contains both
            // contexts.
            IncrementDecrement { IsChecked: true } id => CheckedIncrementStatement(e, id),
            // C# requires an expression statement to be an invocation, object
            // creation, await, or inc/decrement. A bare value — a stack slot
            // discarded by an IL `pop`, a comparison, the caught exception, an
            // operator-spelled call (`a != b`) — is CS0201 as a statement, so
            // spell the discard explicitly with `_ =`, which is always valid.
            { } expr when !IsStatementExpression(expr) => DiscardStatement(e, expr),
            { } expr => $"{Expression(expr)};",
        },
        // Storing into a ref-typed local rebinds the reference itself (stloc of
        // a managed pointer), not a write-through — that is C#'s ref
        // (re)assignment, which takes `= ref <place>` on both the initial
        // declaration (CS8172) and any later rebind (CS8173). Deref renders the
        // address value as the place it refers to.
        StoreLocal { Type.Kind: TypeRefKind.ByRef } s => _declaringStores.Contains(s)
            ? $"{TypeText(s.Type)} {LocalName(s.Index)} = ref {UnsafeExpressionText(s.Value, Deref(s.Value), force: RendersAsPointerDeref(s.Value))};"
            : $"{LocalName(s.Index)} = ref {UnsafeExpressionText(s.Value, Deref(s.Value), force: RendersAsPointerDeref(s.Value))};",
        StoreLocal s => _declaringStores.Contains(s)
            ? $"{DeclarationTypeText(s.Type, s.Value)} {LocalName(s.Index)} = {UnsafeExpressionText(s.Value, DeclarationInitializerText(s.Type, s.Value))};"
            : AssignmentText(s, s.Value, s.UpdateKind, s.Type, forHeader: forHeader),
        DeconstructionAssignment d => $"({string.Join(", ", d.Targets.Select(DeconstructionTargetText))}) = {UnsafeExpressionText(d.Source, Expression(d.Source))};",
        ChainedAssignment c => $"{string.Join(" = ", c.Targets.Select(ChainedAssignmentTargetText))} = {UnsafeExpressionText(c.Value, CoerceText(c.Value, c.InnermostTargetType))};",
        NullCoalescingAssignment n => $"{LocalName(n.LocalIndex)} ??= {UnsafeExpressionText(n.Value, CoerceText(n.Value, n.LocalType))};",
        NullCoalescingFieldAssignment n => $"{FieldTarget(n.Field, n.Instance)} ??= {UnsafeExpressionText(n.Value, CoerceText(n.Value, n.Field.Type))};",
        NullCoalescingPropertyAssignment n => $"{PropertyTarget(n.Setter, n.Instance, n.IndexArguments, n.PropertyName, n.IsVirtual)} ??= {UnsafeExpressionText(n.Value, CoerceText(n.Value, n.PropertyType))};",
        StoreArgument s => AssignmentText(
            s,
            s.Value,
            s.UpdateKind,
            s.Type, forHeader: forHeader),
        StoreField s => AssignmentText(
            s,
            s.Value,
            s.UpdateKind,
            s.Field.Type, forHeader: forHeader),
        StoreProperty s => AssignmentText(
            s,
            s.Value,
            s.UpdateKind,
            StorePropertyTargetType(s), forHeader: forHeader),
        EventSubscription e => $"{PropertyTarget(e.Accessor, e.HasInstance ? e.Instance : null, [], e.EventName, e.IsVirtual, isEvent: true)} {(e.IsAdd ? "+=" : "-=")} {UnsafeExpressionText(e.Value, CoerceText(e.Value, e.Accessor.ParameterTypes[0]))};",
        StoreElement s => $"{Operand(s.Array)}[{ArrayIndexText(s.Index)}] = {UnsafeExpressionText(s.Value, InitializerText(s.Value, StoreElementTargetType(s), StoreElementNewTarget(s)))};",
        PointerElementCompoundAssignment s => $"{Operand(s.Pointer)}[{Expression(s.Index)}] {BinaryOperator(s.Operation)}= {Expression(s.Value)};",
        PointerCompoundAssignment s => PointerUpdateText(s, statement: true),
        StoreIndirect s => AssignmentText(
            s,
            s.Value,
            s.UpdateKind,
            IndirectStoreType(s.Address, s.Type),
            parenthesizeIncrementTarget: RendersAsPointerDeref(s.Address),
            forHeader: forHeader),
        // default-initialization of a named place spells through the place,
        // not its address.
        InitObject { Address: LoadLocalAddress local } init => _declaringStores.Contains(init)
            ? $"{TypeText(init.Type)} {LocalName(local.Index)} = default;"
            : $"{LocalName(local.Index)} = default;",
        InitObject { Address: LoadArgumentAddress argument } => $"{CSharpNaming.ContainedIdentifier(argument.Name)} = default;",
        InitObject { Address: LoadFieldAddress field } o2 => $"{FieldTarget(field.Field, field.Instance)} = default;",
        InitObject o => $"{Deref(o.Address)} = default({TypeText(o.Type)});",
        CopyBlock cb => "/* unsupported cpblk */",
        Return { Value: { } value } => ReturnText(value),
        Return => "return;",
        YieldReturn y => $"yield return {UnsafeExpressionText(y.Value, Expression(y.Value))};",
        YieldBreak => "yield break;",
        // The rethrow: the raw caught value thrown back is C#'s bare throw.
        Throw { Value: CaughtException } => "throw;",
        Throw t => $"throw {UnsafeExpressionText(t.Value, Expression(t.Value))};",
        Break => "break;",
        Continue => "continue;",
        Branch b => $"goto {LabelName(b.TargetOffset)};",
        ConditionalBranch c => $"if ({UnsafeConditionText(c.Condition)}) goto {LabelName(c.TargetOffset)};",
        SwitchBranch s => $"switch ({UnsafeExpressionText(s.Value, Expression(s.Value))}) goto [{string.Join(", ", s.TargetOffsets.Select(LabelName))}];",
        Leave l => $"goto {LabelName(l.TargetOffset)}; // leave",
        EndFinally => "// endfinally",
        EndFilter f => $"// endfilter({CommentExpressionText(f.Value)})",
        _ => $"/* {node.Describe()} */",
    };

    string ForLoopIncrementText(IrNode node)
    {
        if (node is PointerCompoundAssignment update)
            return PointerUpdateText(update, statement: false);
        return node is ExpressionStatement { Expression: IncrementDecrement { IsChecked: true } increment }
            ? Expression(increment)
            : Statement(node, forHeader: true)?.TrimEnd(';') ?? "";
    }

    string PointerUpdateText(PointerCompoundAssignment update, bool statement)
    {
        bool enclosingChecked = _checkedContext;
        _checkedContext = update.IsChecked;
        try
        {
            string target = update.Target switch
            {
                LoadIndirect load => IndirectTarget(load.Address, update.PointerType),
                LoadProperty load => PropertyTarget(update.Setter!, load.Instance, load.IndexArguments, load.PropertyName, load.IsVirtual),
                _ => Expression(update.Target),
            };
            string? context = update.IsChecked ? "checked" : enclosingChecked ? "unchecked" : null;
            if (!statement && context is not null)
            {
                string op = update.Kind is PointerUpdateKind.Add or PointerUpdateKind.Increment ? "+" : "-";
                _printedRangeMetadata?.SetNodeKind(update, "AssignmentStatement");
                return $"{target} = {context}({target} {op} {Operand(update.Index)})";
            }
            string incrementTarget = update.Target is LoadIndirect indirect && RendersAsPointerDeref(indirect.Address)
                ? $"({target})" : target;
            string text = update.Kind switch
            {
                PointerUpdateKind.Increment => $"{incrementTarget}++",
                PointerUpdateKind.Decrement => $"{incrementTarget}--",
                PointerUpdateKind.Add => $"{target} += {Expression(update.Index)}",
                PointerUpdateKind.Subtract => $"{target} -= {Expression(update.Index)}",
                _ => throw new InvalidOperationException($"Unknown pointer update: {update.Kind}"),
            };
            if (context is null)
                return statement ? $"{text};" : text;
            _printedRangeMetadata?.SetNodeKind(update, "CheckedStatement");
            return $"{context} {{ {text}; }}";
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    string DeconstructionTargetText(DeconstructionTarget target) => target.Kind switch
    {
        DeconstructionTargetKind.Local => target.IsDeclared
            ? $"{TypeText(target.Type)} {LocalName(target.LocalIndex)}"
            : LocalName(target.LocalIndex),
        DeconstructionTargetKind.Property => PropertyTarget(target.Accessor!, target.HasInstance ? target.Instance : null, target.IndexArguments, target.PropertyName, target.IsVirtual),
        DeconstructionTargetKind.Argument => CSharpNaming.ContainedIdentifier(target.ArgumentName),
        DeconstructionTargetKind.Field => FieldTarget(
            target.Field!,
            target.IsThisInstance ? new LoadArgument(0, "this", target.Field!.DeclaringType) : null),
        _ => $"/* {target.Describe()} */",
    };

    string ChainedAssignmentTargetText(ChainedAssignmentTarget target) => target.Kind switch
    {
        ChainedAssignmentTargetKind.StaticProperty => PropertyTarget(target.Accessor!, null, [], target.PropertyName, target.IsVirtual),
        ChainedAssignmentTargetKind.StaticField => FieldTarget(target.Field!, null),
        _ => $"/* {target.Kind} */",
    };

    /// <summary>
    /// A return statement. A method that returns by reference (<c>ref T</c>) ends
    /// in <c>return ref place;</c> — the IL <c>ret</c> yields a managed pointer, so
    /// the keyword is required (a bare <c>return place;</c> is CS8150). Falls back
    /// to the by-value spelling for value returns and for the rare ref return whose
    /// value is not a single place (a ref ternary binds <c>ref</c> per arm).
    /// </summary>
    string ReturnText(IrExpression value)
        => CurrentReturnType is { Kind: TypeRefKind.ByRef } && ArgumentLvalue(value) is { } place
            ? $"return ref {UnsafeExpressionText(value, place, force: RendersAsPointerDeref(value))};"
            : $"return {UnsafeExpressionText(value, CoerceText(value, CurrentReturnType))};";

    string AssignmentText(
        IrNode owner,
        IrExpression value,
        ScalarUpdateKind? updateKind,
        TypeRef? targetType = null,
        bool parenthesizeIncrementTarget = false,
        bool forHeader = false)
    {
        bool checkedUpdate = updateKind is not null && value is Binary { IsChecked: true };
        bool enclosingChecked = _checkedContext;
        if (checkedUpdate && !forHeader)
            _checkedContext = true;
        try
        {
            string target = AssignmentTargetText(owner);
            if (updateKind is { } kind && !(checkedUpdate && forHeader))
            {
                var binary = (Binary)value;
                string statement = CompoundStatement(
                    target,
                    binary,
                    targetType,
                    kind,
                    parenthesizeIncrementTarget);
                _printedRangeMetadata?.SetNodeKind(
                    owner,
                    binary.IsChecked
                        ? "CheckedStatement"
                        : kind is ScalarUpdateKind.Increment or ScalarUpdateKind.Decrement
                            ? "IncrementOrDecrementExpression"
                            : "AssignmentStatement");
                // A checked expression is not a statement expression. Render the
                // whole compound in its block context, including the target.
                return binary.IsChecked ? $"checked {{ {statement} }}" : statement;
            }
            // A for header cannot contain a checked block; the retained binary
            // carries its own context inside an ordinary assignment.
            return $"{target} = {UnsafeExpressionText(value, InitializerText(value, targetType))};";
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    string AssignmentTargetText(IrNode owner) => owner switch
    {
        StoreLocal s => LocalName(s.Index),
        StoreArgument s => CSharpNaming.ContainedIdentifier(s.Name),
        StoreField s => FieldTarget(s.Field, s.Instance),
        StoreProperty s => PropertyTarget(s.Accessor, s.HasInstance ? s.Instance : null, s.IndexArguments, s.PropertyName, s.IsVirtual),
        StoreIndirect s => IndirectTarget(s.Address, IndirectStoreType(s.Address, s.Type)),
        _ => throw new InvalidOperationException($"Unsupported assignment target: {owner.GetType().Name}"),
    };

    /// <summary>
    /// Spells a compound assignment whose value reads the target: <c>x++</c>/
    /// <c>x--</c> for a ±1 step, <c>x op= rest</c> otherwise. A shift count carries
    /// the compiler's implicit width mask; strip it exactly as the expression form
    /// does so <c>x &lt;&lt;= n</c> does not re-mask on recompile (see ShiftCount).
    /// </summary>
    string CompoundStatement(
        string target,
        Binary binary,
        TypeRef? targetType,
        ScalarUpdateKind updateKind,
        bool parenthesizeIncrementTarget)
    {
        string incrementTarget = parenthesizeIncrementTarget
            ? $"({target})"
            : target;
        if (updateKind is ScalarUpdateKind.Increment or ScalarUpdateKind.Decrement)
            return $"{incrementTarget}{(updateKind == ScalarUpdateKind.Increment ? "++" : "--")};";
        // The compound runs in the lvalue's type. Prefer the resolved store type
        // (`targetType`) over `binary.Left.ResultType`: an indirect store reads its
        // target through `ldind.i`, which the importer types as the signed native
        // `IntPtr` even for a `ref nuint`, so the bare `binary.Left` type loses the
        // lvalue's real signedness.
        var lvalueType = targetType ?? binary.Left.ResultType;
        // C# has no compound shift operator on an enum lvalue (CS0019, the compound
        // sibling of the enum-shift expression fix): an int-backed `flags <<= n`
        // folds to `store flags = shl(load flags, n)` with a bare enum left operand,
        // yet C# rejects `flags <<= n`. Decompose to a plain assignment that
        // reinterprets the enum to its shift integer (BinaryBody spells the left
        // operand and count-mask) and casts the shift result back to the enum:
        // `flags = (E)((int)flags >> (n & 31))`. The int->enum cast is a reinterpret
        // that never overflows, so it stays a bare cast even inside `checked`. A
        // cross-assembly enum lvalue has no resolved backing; decompose only when the
        // inner shift will render validly — its width is recoverable from the count
        // mask (a constant-count compound has no mask, so it stays visibly invalid).
        if (binary.Kind is BinaryKind.ShiftLeft or BinaryKind.ShiftRight
            && lvalueType is not null
            && (EnumUnderlyingType(lvalueType) is not null
                || (IsEnumLikeInteger(lvalueType) && ShiftCountMaskWidthBytes(binary) is not null)))
        {
            return $"{target} = ({TypeText(lvalueType)}){RenderedExpression(binary).At(Precedence.Unary)};";
        }
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
                ? coercedRight.Text
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
                : Expression(binary.Right);
        rightText = UnsafeExpressionText(binary.Right, rightText);
        return $"{target} {BinaryOperator(binary)}= {rightText};";
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
    string CheckedIncrementStatement(ExpressionStatement owner, IncrementDecrement id)
    {
        _printedRangeMetadata?.SetNodeKind(owner, "CheckedStatement");
        bool saved = _checkedContext;
        _checkedContext = true;
        try
        {
            return $"checked {{ {Expression(id)}; }}";
        }
        finally
        {
            _checkedContext = saved;
        }
    }

    string UnsupportedStatement(ExpressionStatement owner, UnsupportedNode unsupported)
    {
        _printedRangeMetadata?.SetNodeKind(owner, "UnsupportedExpression");
        return Expression(unsupported);
    }

    string DiscardStatement(ExpressionStatement owner, IrExpression expression)
    {
        _printedRangeMetadata?.SetNodeKind(owner, "AssignmentStatement");
        return $"_ = {UnsafeExpressionText(expression, Expression(expression))};";
    }

    /// <summary>Renders a diagnostic comment payload without publishing it as surface syntax.</summary>
    string CommentExpressionText(IrExpression expression)
    {
        var printedRanges = _printedRanges;
        var printedRangeMetadata = _printedRangeMetadata;
        var expressionText = _expressionText;
        _printedRanges = null;
        _printedRangeMetadata = null;
        _expressionText = null;
        try
        {
            return Expression(expression);
        }
        finally
        {
            _printedRanges = printedRanges;
            _printedRangeMetadata = printedRangeMetadata;
            _expressionText = expressionText;
        }
    }
}
