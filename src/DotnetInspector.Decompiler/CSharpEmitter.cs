using System.Reflection.Metadata;
using System.Text;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Emits C# source code from the ILAst representation. Maps IL opcodes to
/// C# operators, method calls, field access, casts, and control flow constructs.
/// </summary>
public static class CSharpEmitter
{
    /// <summary>
    /// Emit C# source for a method.
    /// </summary>
    public static string Emit(MethodBodyContext context)
    {
        var cfg = ControlFlowGraph.Create(context);
        var simResult = StackSimulator.Simulate(context, cfg);
        var ast = ILAstBuilder.Build(context, cfg, simResult);
        var structure = StructuredControlFlow.Analyze(context, cfg);
        return Emit(ast, structure, context.Reader, context.HasThis);
    }

    /// <summary>
    /// Emit C# source from pre-computed ILAst and control flow structure.
    /// </summary>
    public static string Emit(ILAstMethod ast, StructuredControlFlow structure, MetadataReader? reader = null, bool hasThis = false)
    {
        var sb = new StringBuilder();
        var emitter = new EmitterContext(ast, structure, sb, reader, hasThis);
        emitter.EmitMethod();
        return sb.ToString();
    }

    sealed class EmitterContext
    {
        readonly ILAstMethod _ast;
        readonly StructuredControlFlow _structure;
        readonly StringBuilder _sb;
        readonly MetadataReader? _reader;
        readonly bool _hasThis;

        // Map block index → ILAstBlock for quick lookup
        readonly Dictionary<int, ILAstBlock> _blockMap;

        // Blocks consumed by structured constructs (don't emit separately)
        readonly HashSet<int> _consumedBlocks;

        // Current block nodes being emitted (for dup resolution)
        List<ILAstNode>? _currentBlockNodes;

        // When emitting inside a null-conditional pattern, the receiver expression string
        string? _nullConditionalReceiver;

        // Set of IL offsets that are goto targets — blocks at these offsets need labels
        readonly HashSet<string> _gotoTargets;

        // Labels already emitted (avoid duplicates for shared blocks)
        readonly HashSet<string> _emittedLabels;

        // Catch handler statements to suppress (blockIndex, nodeIndex) — already emitted in catch clause
        readonly HashSet<(int blockIndex, int nodeIndex)> _catchVariableStatements;

        // Interpolated string handler parts: handler variable name → ordered list of parts
        readonly Dictionary<string, List<InterpolationPart>> _interpolationParts = [];

        // Nodes to skip because they are part of a recognized pattern (interpolation, using)
        readonly HashSet<ILAstNode> _skipNodes = [];

        // Local variable names to suppress from declaration (declared inline by using/etc.)
        readonly HashSet<string> _suppressedLocals = [];

        // Return blocks whose gotos have been inlined — suppress the block itself
        readonly HashSet<string> _inlinedReturnLabels;

        // Map block index → IL start offset (for emitting labels)
        readonly Dictionary<int, int> _blockStartOffset;

        // IL offset labels of loop headers — gotos to these are suppressed (replaced by while)
        readonly HashSet<string> _loopHeaderLabels;

        // IL offset labels consumed by while-loop conditions (body entry points from header branch)
        readonly HashSet<string> _loopConsumedLabels;

        public EmitterContext(ILAstMethod ast, StructuredControlFlow structure, StringBuilder sb, MetadataReader? reader = null, bool hasThis = false)
        {
            _ast = ast;
            _structure = structure;
            _sb = sb;
            _reader = reader;
            _hasThis = hasThis;

            _blockMap = [];
            for (int i = 0; i < ast.Blocks.Count; i++)
                _blockMap[i] = ast.Blocks[i];

            _consumedBlocks = [];

            // Build block start offset map from block's IL offset
            _blockStartOffset = [];
            for (int i = 0; i < ast.Blocks.Count; i++)
                _blockStartOffset[i] = ast.Blocks[i].Offset;

            // Collect all goto targets from branch operands
            _gotoTargets = CollectGotoTargets(ast);
            _emittedLabels = [];
            _catchVariableStatements = [];
            _inlinedReturnLabels = [];

            // Build set of loop header labels for goto suppression
            _loopHeaderLabels = [];
            _loopConsumedLabels = [];
            foreach (var loop in structure.Loops)
            {
                if (_blockStartOffset.TryGetValue(loop.HeaderIndex, out int offset))
                    _loopHeaderLabels.Add($"IL_{offset:X4}");

                // The header's branch target into the body is consumed by while(cond)
                if (loop.HeaderIndex >= 0 && loop.HeaderIndex < ast.Blocks.Count)
                {
                    var headerBlock = ast.Blocks[loop.HeaderIndex];
                    var lastNode = headerBlock.Nodes.LastOrDefault();
                    if (lastNode is ILAstStatement { Expression.Operand: string branchLabel })
                        _loopConsumedLabels.Add(branchLabel);
                }
            }

            // Conditionals consume their then/else/condition block labels (branches are structured)
            foreach (var cond in structure.Conditionals)
            {
                if (_blockStartOffset.TryGetValue(cond.ThenIndex, out int thenOff))
                    _loopConsumedLabels.Add($"IL_{thenOff:X4}");
                if (cond.ElseIndex >= 0 && _blockStartOffset.TryGetValue(cond.ElseIndex, out int elseOff))
                    _loopConsumedLabels.Add($"IL_{elseOff:X4}");
                if (cond.FollowIndex >= 0 && _blockStartOffset.TryGetValue(cond.FollowIndex, out int followOff))
                    _loopConsumedLabels.Add($"IL_{followOff:X4}");
            }

            // Scan all blocks for string interpolation handler patterns
            ScanForInterpolation(ast);

            // Pre-detect using patterns to suppress local declarations
            ScanForUsingPatterns(structure.Root);
        }

        void ScanForInterpolation(ILAstMethod ast)
        {
            const string handlerType = "DefaultInterpolatedStringHandler";

            foreach (var block in ast.Blocks)
            {
                // Find .ctor calls on DefaultInterpolatedStringHandler
                string? handlerVar = null;
                int ctorIdx = -1;

                for (int i = 0; i < block.Nodes.Count; i++)
                {
                    if (block.Nodes[i] is not ILAstStatement { Expression: var expr })
                        continue;
                    if (expr.Operand is not string operand)
                        continue;
                    if (!operand.Contains(handlerType, StringComparison.Ordinal))
                        continue;
                    if (!operand.Contains("::.ctor", StringComparison.Ordinal))
                        continue;

                    // Extract receiver variable name from first argument (ldloca V_x)
                    if (expr.Arguments.Count > 0)
                    {
                        var receiver = expr.Arguments[0];
                        if (receiver.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s
                            or ILOpCode.Ldloc or ILOpCode.Ldloc_s
                            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3)
                        {
                            handlerVar = RenderReceiverName(receiver);
                            ctorIdx = i;
                            break;
                        }
                    }
                }

                if (handlerVar is null || ctorIdx < 0) continue;

                // Collect Append* calls on the same handler variable
                var parts = new List<InterpolationPart>();
                var skipNodes = new List<ILAstNode> { block.Nodes[ctorIdx] };

                for (int i = ctorIdx + 1; i < block.Nodes.Count; i++)
                {
                    if (block.Nodes[i] is not ILAstStatement { Expression: var callExpr })
                        break;
                    if (callExpr.Operand is not string callOp
                        || !callOp.Contains(handlerType, StringComparison.Ordinal))
                        break;

                    // Verify same receiver
                    if (callExpr.Arguments.Count == 0) break;
                    string receiverName = RenderReceiverName(callExpr.Arguments[0]);
                    if (receiverName != handlerVar) break;

                    if (callOp.Contains("::AppendLiteral", StringComparison.Ordinal))
                    {
                        // Literal text is the second argument (first is receiver)
                        string? literal = callExpr.Arguments.Count > 1
                            ? ExtractStringLiteral(callExpr.Arguments[1])
                            : null;
                        parts.Add(new InterpolationPart(true, literal ?? "", null));
                        skipNodes.Add(block.Nodes[i]);
                    }
                    else if (callOp.Contains("::AppendFormatted", StringComparison.Ordinal))
                    {
                        // Formatted expression is the second argument
                        var formatExpr = callExpr.Arguments.Count > 1 ? callExpr.Arguments[1] : null;
                        parts.Add(new InterpolationPart(false, null, formatExpr));
                        skipNodes.Add(block.Nodes[i]);
                    }
                    else
                    {
                        break;
                    }
                }

                if (parts.Count > 0)
                {
                    _interpolationParts[handlerVar] = parts;
                    foreach (var n in skipNodes)
                        _skipNodes.Add(n);
                }
            }
        }

        string RenderReceiverName(ILAstExpression receiver)
        {
            // For ldloc/ldloca, the operand is the variable name string (e.g., "V_0")
            if (receiver.Operand is string name) return name;
            // Fallback for short-form ldloc_N opcodes without explicit operand
            return receiver.OpCode switch
            {
                ILOpCode.Ldloc_0 => "V_0",
                ILOpCode.Ldloc_1 => "V_1",
                ILOpCode.Ldloc_2 => "V_2",
                ILOpCode.Ldloc_3 => "V_3",
                _ => "?"
            };
        }

        static string? ExtractStringLiteral(ILAstExpression expr)
        {
            if (expr.OpCode == ILOpCode.Ldstr && expr.Operand is string s)
            {
                // Operand is stored with surrounding quotes, e.g., "\"Hello, \""
                if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                    return s[1..^1];
                return s;
            }
            return null;
        }

        void ScanForUsingPatterns(StructuredBlock block)
        {
            // Process sequences — look for BasicBlock followed by TryCatchFinally
            if (block.Kind == StructuredBlockKind.Sequence)
            {
                for (int i = 0; i < block.Children.Count; i++)
                {
                    var child = block.Children[i];
                    if (child.Kind == StructuredBlockKind.TryCatchFinally
                        && child.ExceptionRegion is { Kind: ExceptionRegionKind.Finally })
                    {
                        string? disposeVar = TryDetectDisposeVariable(child);
                        if (disposeVar is not null)
                        {
                            _suppressedLocals.Add(disposeVar);

                            // Pre-detect foreach element variable
                            ILAstExpression? initExpr = null;
                            for (int j = i - 1; j >= 0; j--)
                            {
                                var sibling = block.Children[j];
                                if (sibling.Kind == StructuredBlockKind.BasicBlock
                                    && sibling.BlockIndex >= 0
                                    && _blockMap.TryGetValue(sibling.BlockIndex, out var sibAst))
                                {
                                    foreach (var node in sibAst.Nodes)
                                    {
                                        if (node is ILAstStatement { Expression: var stExpr }
                                            && stExpr.OpCode is ILOpCode.Stloc or ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                                                or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s
                                            && stExpr.Operand == disposeVar
                                            && stExpr.Arguments.Count > 0)
                                        {
                                            initExpr = stExpr.Arguments[0];
                                            _skipNodes.Add(node);
                                        }
                                    }
                                }
                            }

                            // If init is GetEnumerator, find and suppress the Current element variable
                            if (initExpr?.Operand is string initOp
                                && initOp.Contains("GetEnumerator", StringComparison.Ordinal))
                            {
                                ScanForCurrentVariable(child, disposeVar);
                            }
                        }
                    }
                }
            }

            if (block.Kind == StructuredBlockKind.TryCatchFinally
                && block.ExceptionRegion is { Kind: ExceptionRegionKind.Finally })
            {
                string? disposeVar = TryDetectDisposeVariable(block);
                if (disposeVar is not null)
                    _suppressedLocals.Add(disposeVar);
            }

            foreach (var c in block.Children)
                ScanForUsingPatterns(c);
            foreach (var c in block.TryChildren)
                ScanForUsingPatterns(c);
            foreach (var c in block.HandlerChildren)
                ScanForUsingPatterns(c);
        }

        void ScanForCurrentVariable(StructuredBlock tryBlock, string enumeratorVar)
        {
            // Look through try body blocks for a stloc that calls get_Current on the enumerator
            foreach (var child in tryBlock.TryChildren)
            {
                if (child.BlockIndex >= 0 && _blockMap.TryGetValue(child.BlockIndex, out var astBlock))
                {
                    foreach (var node in astBlock.Nodes)
                    {
                        if (node is ILAstStatement { Expression: var stExpr }
                            && stExpr.Arguments.Count > 0
                            && HasCallInTree(stExpr.Arguments[0], "get_Current")
                            && stExpr.Operand is string varName)
                        {
                            _suppressedLocals.Add(varName);
                            return;
                        }
                    }
                }
            }
        }

        void EmitInterpolatedString(List<InterpolationPart> parts)
        {
            _sb.Append("$\"");
            foreach (var part in parts)
            {
                if (part.IsLiteral)
                {
                    // Escape braces in literal text
                    _sb.Append((part.LiteralText ?? "")
                        .Replace("{", "{{")
                        .Replace("}", "}}"));
                }
                else if (part.FormatExpression is not null)
                {
                    _sb.Append('{');
                    EmitExpression(part.FormatExpression);
                    _sb.Append('}');
                }
            }
            _sb.Append('"');
        }

        static HashSet<string> CollectGotoTargets(ILAstMethod ast)
        {
            HashSet<string> targets = [];
            foreach (var block in ast.Blocks)
            {
                foreach (var node in block.Nodes)
                {
                    if (node is ILAstStatement { Expression: var expr })
                        CollectTargetsFromExpr(expr, targets);
                }
            }
            return targets;
        }

        static void CollectTargetsFromExpr(ILAstExpression expr, HashSet<string> targets)
        {
            if (expr.OpCode is ILOpCode.Br or ILOpCode.Br_s
                or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
                or ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s
                or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bgt or ILOpCode.Bgt_s
                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Blt or ILOpCode.Blt_s
                or ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s
                or ILOpCode.Ble_un or ILOpCode.Ble_un_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s
                or ILOpCode.Leave or ILOpCode.Leave_s)
            {
                if (expr.Operand is string target)
                    targets.Add(target);
            }
        }

        public void EmitMethod()
        {
            // Emit local variable declarations
            if (_ast.Locals.Count > 0)
            {
                foreach (var local in _ast.Locals)
                {
                    // Skip variables consumed by interpolated string or using detection
                    if (_interpolationParts.ContainsKey(local.Name))
                        continue;
                    if (_suppressedLocals.Contains(local.Name))
                        continue;
                    string typeName = SimplifyTypeName(local.TypeName ?? "var");
                    // Skip compiler-generated closure variable declarations
                    if (typeName.Contains("/* closure */", StringComparison.Ordinal))
                        continue;
                    _sb.AppendLine($"{typeName} {local.Name};");
                }
                _sb.AppendLine();
            }

            // Emit the structured body
            EmitStructuredBlock(_structure.Root, 0);

            // Ensure the method ends with a return if the last block has one.
            // Shared return blocks may be consumed by an IfThenElse else branch
            // but still need to appear at method end for other paths.
            EnsureTrailingReturn();
        }

        void EnsureTrailingReturn()
        {
            int lastBlockIdx = _ast.Blocks.Count - 1;
            if (lastBlockIdx < 0 || !_blockMap.TryGetValue(lastBlockIdx, out var lastBlock))
                return;

            // If the block was already emitted as part of the structured tree, skip
            if (_consumedBlocks.Contains(lastBlockIdx))
                return;

            // Only applies if the block has a return statement
            bool hasReturn = lastBlock.Nodes.Any(n =>
                n is ILAstStatement { Expression.OpCode: ILOpCode.Ret });
            if (!hasReturn) return;

            // If this block is a return-only block and all gotos to it were inlined,
            // its label is no longer needed — skip the trailing return
            if (lastBlock.Nodes.Count == 1
                && _blockStartOffset.TryGetValue(lastBlockIdx, out int offset))
            {
                string label = $"IL_{offset:X4}";
                if (!_emittedLabels.Contains(label))
                    return;
            }

            // Check if the last emitted line already ends with a return
            string output = _sb.ToString().TrimEnd();
            if (output.EndsWith("return;") || output.EndsWith(';') &&
                output.LastIndexOf('\n') is int nl && nl >= 0 &&
                output[(nl + 1)..].TrimStart().StartsWith("return "))
                return;

            // Emit the return block — it may have been consumed by an IfThenElse
            // but is also needed at method end for other paths (shared return blocks)
            EmitBasicBlock(lastBlockIdx, 0);
        }

        void EmitStructuredBlock(StructuredBlock block, int indent)
        {
            switch (block.Kind)
            {
                case StructuredBlockKind.Sequence:
                    foreach (var child in block.Children)
                        EmitStructuredBlock(child, indent);
                    break;

                case StructuredBlockKind.BasicBlock:
                    EmitBasicBlock(block.BlockIndex, indent);
                    break;

                case StructuredBlockKind.IfThenElse:
                    EmitIfThenElse(block, indent);
                    break;

                case StructuredBlockKind.Loop:
                    EmitLoop(block, indent);
                    break;

                case StructuredBlockKind.TryCatchFinally:
                    EmitTryCatchFinally(block, indent);
                    break;

                case StructuredBlockKind.Switch:
                    EmitSwitch(block, indent);
                    break;
            }
        }

        void EmitBasicBlock(int blockIndex, int indent)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var astBlock))
                return;

            // Suppress return-only blocks whose gotos were all inlined
            if (astBlock.Nodes.Count == 1
                && astBlock.Nodes[0] is ILAstStatement { Expression.OpCode: ILOpCode.Ret }
                && _blockStartOffset.TryGetValue(blockIndex, out int retOffset)
                && _inlinedReturnLabels.Contains($"IL_{retOffset:X4}"))
            {
                _consumedBlocks.Add(blockIndex);
                return;
            }

            _consumedBlocks.Add(blockIndex);
            _currentBlockNodes = astBlock.Nodes;

            // Emit IL offset label if this block is a goto target
            TryEmitLabel(blockIndex);

            for (int nodeIdx = 0; nodeIdx < astBlock.Nodes.Count; nodeIdx++)
            {
                if (_catchVariableStatements.Contains((blockIndex, nodeIdx)))
                    continue;
                if (_skipNodes.Contains(astBlock.Nodes[nodeIdx]))
                    continue;

                var node = astBlock.Nodes[nodeIdx];
                switch (node)
                {
                    case ILAstAssignment assign:
                        WriteIndent(indent);
                        string assignType = SimplifyTypeName(assign.Variable.TypeName ?? "var");
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                        break;

                    case ILAstStatement stmt:
                        EmitStatement(stmt.Expression, indent);
                        break;
                }
            }

            _currentBlockNodes = null;
        }

        void EmitIfThenElse(StructuredBlock block, int indent)
        {
            // The condition block's last expression is the branch condition
            string condition = "/* condition */";
            ILAstExpression? branchExpression = null;
            if (block.ConditionBlockIndex >= 0 && _blockMap.TryGetValue(block.ConditionBlockIndex, out var condBlock))
            {
                _currentBlockNodes = condBlock.Nodes;

                // Emit IL label if this condition block is a goto target
                TryEmitLabel(block.ConditionBlockIndex);

                // Emit any statements before the branch
                for (int i = 0; i < condBlock.Nodes.Count - 1; i++)
                {
                    if (condBlock.Nodes[i] is ILAstStatement stmt)
                        EmitStatement(stmt.Expression, indent);
                    else if (condBlock.Nodes[i] is ILAstAssignment assign)
                    {
                        WriteIndent(indent);
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                    }
                }

                // Extract condition from the last node (branch)
                var lastNode = condBlock.Nodes.LastOrDefault();
                if (lastNode is ILAstStatement branchStmt)
                {
                    branchExpression = branchStmt.Expression;
                    condition = BranchConditionToString(branchExpression);
                }
            }

            // Detect null-conditional pattern: brtrue with dup + trivial else (pop + br.s)
            if (branchExpression is not null && IsNullConditionalPattern(branchExpression, block))
            {
                EmitNullConditional(branchExpression, block, indent);
                return;
            }

            // Detect ternary pattern: both then and else produce a single S_0 value
            if (block.ThenBlock is not null && block.ElseBlock is not null)
            {
                var thenValue = TryExtractTernaryValue(block.ThenBlock);
                var elseValue = TryExtractTernaryValue(block.ElseBlock);
                if (thenValue is not null && elseValue is not null)
                {
                    // Apply negation if the conditional detector swapped then/else
                    if (block.NegateCondition)
                        condition = NegateConditionString(condition);

                    // Emit as: S_in_0 = cond ? thenValue : elseValue
                    // The follow block's S_in_0 references will resolve naturally
                    WriteIndent(indent);
                    _sb.Append("S_in_0 = ");
                    _sb.Append(condition);
                    _sb.Append(" ? ");
                    _sb.Append(ExpressionToString(thenValue));
                    _sb.Append(" : ");
                    _sb.Append(ExpressionToString(elseValue));
                    _sb.AppendLine(";");

                    // Mark then/else blocks as consumed
                    if (block.ThenBlock.BlockIndex >= 0)
                        _consumedBlocks.Add(block.ThenBlock.BlockIndex);
                    if (block.ElseBlock.BlockIndex >= 0)
                        _consumedBlocks.Add(block.ElseBlock.BlockIndex);
                    return;
                }
            }

            // Apply negation if the conditional detector swapped then/else
            if (block.NegateCondition)
                condition = NegateConditionString(condition);

            WriteIndent(indent);
            _sb.AppendLine($"if ({condition})");
            WriteIndent(indent);
            _sb.AppendLine("{");
            if (block.ThenBlock is not null)
                EmitStructuredBlock(block.ThenBlock, indent + 1);
            WriteIndent(indent);
            _sb.AppendLine("}");

            if (block.ElseBlock is not null)
            {
                // If the then block ends with throw/return, skip the else wrapper
                // (guard clause pattern — the else body just falls through)
                if (BlockEndsWithNoFallthrough(block.ThenBlock))
                {
                    EmitStructuredBlock(block.ElseBlock, indent);
                }
                else
                {
                    WriteIndent(indent);
                    _sb.AppendLine("else");
                    WriteIndent(indent);
                    _sb.AppendLine("{");
                    EmitStructuredBlock(block.ElseBlock, indent + 1);
                    WriteIndent(indent);
                    _sb.AppendLine("}");
                }
            }
        }

        /// <summary>
        /// Detect the null-conditional pattern: dup + brtrue where else is just pop + br.s.
        /// Pattern: obj.field → dup → brtrue(then) / pop+br(else) → then: S_in_0.Method()
        /// </summary>
        bool IsNullConditionalPattern(ILAstExpression branchExpr, StructuredBlock block)
        {
            // Must be brtrue (non-null takes the branch)
            if (branchExpr.OpCode is not (ILOpCode.Brtrue or ILOpCode.Brtrue_s))
                return false;

            // The branch argument must be a dup (or contain one)
            if (branchExpr.Arguments.Count != 1)
                return false;
            var arg = branchExpr.Arguments[0];
            if (arg.OpCode != ILOpCode.Dup)
                return false;

            // Else block must be trivial: pop (+ optional br.s)
            if (block.ElseBlock is null)
                return false;
            int elseIdx = block.ElseBlock.Kind == StructuredBlockKind.BasicBlock
                ? block.ElseBlock.BlockIndex
                : -1;
            if (elseIdx < 0 || !_blockMap.TryGetValue(elseIdx, out var elseAstBlock))
                return false;

            // Check the else block is just pop/br operations
            foreach (var node in elseAstBlock.Nodes)
            {
                if (node is ILAstStatement stmt)
                {
                    var op = stmt.Expression.OpCode;
                    if (op is not (ILOpCode.Pop or ILOpCode.Br or ILOpCode.Br_s))
                        return false;
                }
                else return false;
            }

            return true;
        }

        /// <summary>
        /// Emit a null-conditional expression: receiver?.Method()
        /// </summary>
        void EmitNullConditional(ILAstExpression branchExpr, StructuredBlock block, int indent)
        {
            // Extract the receiver from the dup argument
            var dupExpr = branchExpr.Arguments[0];
            string receiver = dupExpr.Arguments.Count > 0
                ? ExpressionToString(dupExpr.Arguments[0])
                : "/* dup */";

            // Set the null-conditional receiver so S_in_0 resolves to it
            _nullConditionalReceiver = receiver;

            // Emit the then block content (the non-null path)
            if (block.ThenBlock is not null)
                EmitStructuredBlock(block.ThenBlock, indent);

            _nullConditionalReceiver = null;
        }

        /// <summary>
        /// If a block has exactly one S_* assignment (stack slot) and optional br/nop statements,
        /// returns the assigned value expression. Used for ternary pattern detection.
        /// </summary>
        ILAstExpression? TryExtractTernaryValue(StructuredBlock block)
        {
            if (block.BlockIndex < 0 || !_blockMap.TryGetValue(block.BlockIndex, out var astBlock))
                return null;

            ILAstExpression? value = null;
            foreach (var node in astBlock.Nodes)
            {
                if (node is ILAstAssignment assign && assign.Variable.Kind == ILVariableKind.StackSlot)
                {
                    if (value is not null) return null; // multiple values — not a ternary
                    value = assign.Value;
                }
                else if (node is ILAstStatement stmt)
                {
                    // Allow br/nop/leave statements (they're control flow to the join point)
                    var op = stmt.Expression.OpCode;
                    if (op is not (ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Nop
                        or ILOpCode.Leave or ILOpCode.Leave_s))
                        return null;
                }
                else return null;
            }

            return value;
        }

        void EmitLoop(StructuredBlock block, int indent)
        {
            int headerIdx = block.LoopHeaderIndex;
            if (headerIdx < 0 || !_blockMap.TryGetValue(headerIdx, out var headerBlock))
            {
                // Fallback: emit blocks sequentially
                foreach (var child in block.Children)
                    EmitStructuredBlock(child, indent);
                return;
            }

            // Find the NaturalLoop for this header
            var loop = _structure.Loops.FirstOrDefault(l => l.HeaderIndex == headerIdx);
            if (loop is null)
            {
                foreach (var child in block.Children)
                    EmitStructuredBlock(child, indent);
                return;
            }

            // Try to extract the condition from the header block's last node
            string? condition = null;
            bool negateCondition = false;
            var lastNode = headerBlock.Nodes.LastOrDefault();
            if (lastNode is ILAstStatement branchStmt && IsBranchOpCode(branchStmt.Expression.OpCode))
            {
                var branchExpr = branchStmt.Expression;
                string? branchTarget = branchExpr.Operand as string;

                // Determine if the branch goes into the loop body or exits
                bool branchGoesIntoLoop = false;
                if (branchTarget is not null)
                {
                    foreach (int bodyIdx in loop.BodyIndices)
                    {
                        if (_blockStartOffset.TryGetValue(bodyIdx, out int offset)
                            && branchTarget == $"IL_{offset:X4}")
                        {
                            branchGoesIntoLoop = true;
                            break;
                        }
                    }
                }

                negateCondition = !branchGoesIntoLoop;
                condition = BranchConditionToString(branchExpr);
                if (negateCondition)
                    condition = NegateConditionString(condition);
            }

            // Collect body block indices (exclude header)
            var bodyIndices = loop.BodyIndices
                .Where(idx => idx != headerIdx)
                .OrderBy(x => x)
                .ToList();

            if (condition is not null)
            {
                // Do-while detection: if header IS the only body block (self-loop)
                // or all body blocks are empty, the body code lives in the header
                // before the condition — emit as do { body } while (cond)
                bool isDoWhile = bodyIndices.Count == 0 && headerBlock.Nodes.Count > 1;

                if (isDoWhile)
                {
                    _consumedBlocks.Add(headerIdx);
                    _currentBlockNodes = headerBlock.Nodes;

                    WriteIndent(indent);
                    _sb.AppendLine("do");
                    WriteIndent(indent);
                    _sb.AppendLine("{");

                    // Emit all header statements except the last (the branch condition)
                    for (int i = 0; i < headerBlock.Nodes.Count - 1; i++)
                    {
                        var node = headerBlock.Nodes[i];
                        switch (node)
                        {
                            case ILAstAssignment assign:
                                WriteIndent(indent + 1);
                                _sb.Append($"{assign.Variable.Name} = ");
                                EmitExpression(assign.Value);
                                _sb.AppendLine(";");
                                break;
                            case ILAstStatement stmt:
                                EmitStatement(stmt.Expression, indent + 1);
                                break;
                        }
                    }

                    WriteIndent(indent);
                    _sb.AppendLine("}");
                    WriteIndent(indent);
                    _sb.AppendLine($"while ({condition});");

                    _currentBlockNodes = null;
                }
                else
                {
                    // For-loop detection: check if last body statement is an increment
                    // of a variable used in the condition
                    string? increment = TryExtractForIncrement(bodyIndices, condition);

                    EmitHeaderStatements(headerIdx, indent);

                    if (increment is not null)
                    {
                        WriteIndent(indent);
                        _sb.AppendLine($"for (; {condition}; {increment})");
                    }
                    else
                    {
                        WriteIndent(indent);
                        _sb.AppendLine($"while ({condition})");
                    }
                    WriteIndent(indent);
                    _sb.AppendLine("{");

                    foreach (int bodyIdx in bodyIndices)
                        EmitBasicBlockForLoop(bodyIdx, indent + 1, loop);

                    WriteIndent(indent);
                    _sb.AppendLine("}");

                    _consumedBlocks.Add(headerIdx);
                }
            }
            else
            {
                // Fallback: emit as before
                foreach (var child in block.Children)
                    EmitStructuredBlock(child, indent);
            }
        }

        /// <summary>
        /// Emit non-branch statements from the loop header block (e.g., increment).
        /// </summary>
        void EmitHeaderStatements(int headerIdx, int indent)
        {
            if (!_blockMap.TryGetValue(headerIdx, out var headerBlock))
                return;

            _currentBlockNodes = headerBlock.Nodes;

            // Emit all statements except the last (the branch condition)
            for (int i = 0; i < headerBlock.Nodes.Count - 1; i++)
            {
                var node = headerBlock.Nodes[i];
                switch (node)
                {
                    case ILAstAssignment assign:
                        WriteIndent(indent);
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                        break;
                    case ILAstStatement stmt:
                        EmitStatement(stmt.Expression, indent);
                        break;
                }
            }

            _currentBlockNodes = null;
        }

        /// <summary>
        /// Check if the last body block's last non-branch statement is an increment
        /// of a variable used in the condition. Returns the increment expression string
        /// (e.g., "V_1 = V_1 + 1") and marks it for suppression in the body emission.
        /// </summary>
        string? TryExtractForIncrement(List<int> bodyIndices, string condition)
        {
            if (bodyIndices.Count == 0) return null;

            int lastBodyIdx = bodyIndices[^1];
            if (!_blockMap.TryGetValue(lastBodyIdx, out var lastBody))
                return null;

            // Find the last non-branch node
            for (int i = lastBody.Nodes.Count - 1; i >= 0; i--)
            {
                var node = lastBody.Nodes[i];

                // Skip branch statements at the end of the block
                if (node is ILAstStatement stmt && (IsBranchOpCode(stmt.Expression.OpCode)
                    || stmt.Expression.OpCode is ILOpCode.Br or ILOpCode.Br_s))
                    continue;

                // Check for V = V + const pattern via ILAstAssignment
                if (node is ILAstAssignment assign
                    && assign.Value.OpCode is ILOpCode.Add or ILOpCode.Sub
                    && assign.Value.Arguments.Count >= 2)
                {
                    return TryMatchIncrement(assign.Variable.Name, assign.Value, lastBodyIdx, i, condition);
                }

                // Check for stloc(add(ldloc, const)) pattern via ILAstStatement
                if (node is ILAstStatement stloc
                    && stloc.Expression.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                        or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
                        or ILOpCode.Stloc_s or ILOpCode.Stloc
                    && stloc.Expression.Arguments.Count == 1
                    && stloc.Expression.Arguments[0].OpCode is ILOpCode.Add or ILOpCode.Sub
                    && stloc.Expression.Arguments[0].Arguments.Count >= 2)
                {
                    string varName = stloc.Expression.Operand ?? GetLocalName(stloc.Expression.OpCode);
                    return TryMatchIncrement(varName, stloc.Expression.Arguments[0], lastBodyIdx, i, condition);
                }

                break;
            }

            return null;
        }

        string? TryMatchIncrement(string varName, ILAstExpression addExpr, int blockIdx, int nodeIdx, string condition)
        {
            var lhs = addExpr.Arguments[0];

            // LHS of the add/sub must be the same variable
            bool sameVar = lhs.Operand == varName
                || (lhs.OpCode, varName) switch
                {
                    (ILOpCode.Ldloc_0, "V_0") => true,
                    (ILOpCode.Ldloc_1, "V_1") => true,
                    (ILOpCode.Ldloc_2, "V_2") => true,
                    (ILOpCode.Ldloc_3, "V_3") => true,
                    _ => false
                };

            if (!sameVar) return null;

            // The variable must appear in the condition
            if (!condition.Contains(varName)) return null;

            // Build the increment string
            string rhsStr = ExpressionToString(addExpr.Arguments[1]);
            string op = addExpr.OpCode == ILOpCode.Sub ? "-" : "+";

            // Mark this node for suppression during body emission
            _forIncrementStatements.Add((blockIdx, nodeIdx));

            return $"{varName} = {varName} {op} {rhsStr}";
        }

        // Set of (blockIndex, nodeIndex) for for-loop increment statements to suppress
        readonly HashSet<(int blockIndex, int nodeIndex)> _forIncrementStatements = [];

        /// <summary>
        /// Emit a basic block inside a loop, converting gotos to break/continue.
        /// </summary>
        void EmitBasicBlockForLoop(int blockIndex, int indent, NaturalLoop loop)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var astBlock))
                return;

            _consumedBlocks.Add(blockIndex);
            _currentBlockNodes = astBlock.Nodes;

            TryEmitLabel(blockIndex);

            for (int nodeIdx = 0; nodeIdx < astBlock.Nodes.Count; nodeIdx++)
            {
                if (_catchVariableStatements.Contains((blockIndex, nodeIdx)))
                    continue;
                if (_forIncrementStatements.Contains((blockIndex, nodeIdx)))
                    continue;
                if (_skipNodes.Contains(astBlock.Nodes[nodeIdx]))
                    continue;

                var node = astBlock.Nodes[nodeIdx];
                switch (node)
                {
                    case ILAstAssignment assign:
                        WriteIndent(indent);
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                        break;

                    case ILAstStatement stmt:
                        // Convert unconditional gotos to break/continue
                        if (stmt.Expression.OpCode is ILOpCode.Br or ILOpCode.Br_s
                            && stmt.Expression.Operand is string gotoTarget)
                        {
                            if (IsLoopHeaderTarget(gotoTarget, loop))
                            {
                                WriteIndent(indent);
                                _sb.AppendLine("continue;");
                            }
                            else if (!IsInsideLoop(gotoTarget, loop))
                            {
                                WriteIndent(indent);
                                _sb.AppendLine("break;");
                            }
                            else
                            {
                                EmitStatement(stmt.Expression, indent);
                            }
                        }
                        // Convert conditional branches: if(cond) goto header → if(cond) continue
                        else if (IsBranchOpCode(stmt.Expression.OpCode)
                            && stmt.Expression.Operand is string condTarget)
                        {
                            if (IsLoopHeaderTarget(condTarget, loop))
                            {
                                WriteIndent(indent);
                                _sb.Append("if (");
                                EmitBranchCondition(stmt.Expression);
                                _sb.AppendLine(") continue;");
                            }
                            else if (!IsInsideLoop(condTarget, loop))
                            {
                                WriteIndent(indent);
                                _sb.Append("if (");
                                EmitBranchCondition(stmt.Expression);
                                _sb.AppendLine(") break;");
                            }
                            else
                            {
                                EmitStatement(stmt.Expression, indent);
                            }
                        }
                        else
                        {
                            EmitStatement(stmt.Expression, indent);
                        }
                        break;
                }
            }

            _currentBlockNodes = null;
        }

        bool IsLoopHeaderTarget(string target, NaturalLoop loop)
        {
            return _blockStartOffset.TryGetValue(loop.HeaderIndex, out int headerOffset)
                && target == $"IL_{headerOffset:X4}";
        }

        bool IsInsideLoop(string target, NaturalLoop loop)
        {
            foreach (int bodyIdx in loop.BodyIndices)
            {
                if (_blockStartOffset.TryGetValue(bodyIdx, out int offset)
                    && target == $"IL_{offset:X4}")
                    return true;
            }
            return false;
        }

        static bool IsBranchOpCode(ILOpCode op) => op is
            ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s or
            ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s or
            ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bgt or ILOpCode.Bgt_s or
            ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Blt or ILOpCode.Blt_s or
            ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or
            ILOpCode.Ble_un or ILOpCode.Ble_un_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s;

        /// <summary>
        /// If the goto target is a block containing only a return statement,
        /// emit the return directly and return true. Otherwise return false.
        /// </summary>
        bool TryEmitInlinedReturn(string targetLabel, int indent)
        {
            // Find the block with this IL offset
            foreach (var (blockIdx, offset) in _blockStartOffset)
            {
                if ($"IL_{offset:X4}" != targetLabel) continue;
                if (!_blockMap.TryGetValue(blockIdx, out var targetBlock)) return false;

                // Block must have exactly one node: a return statement
                if (targetBlock.Nodes.Count != 1) return false;
                if (targetBlock.Nodes[0] is not ILAstStatement { Expression: var retExpr }) return false;
                if (retExpr.OpCode != ILOpCode.Ret) return false;

                // Track that we inlined this return
                _inlinedReturnLabels.Add(targetLabel);

                // Emit the return inline
                WriteIndent(indent);
                if (retExpr.Arguments.Count > 0)
                {
                    _sb.Append("return ");
                    EmitExpression(retExpr.Arguments[0]);
                    _sb.AppendLine(";");
                }
                else
                {
                    _sb.AppendLine("return;");
                }
                return true;
            }
            return false;
        }

        void EmitTryCatchFinally(StructuredBlock block, int indent)
        {
            if (block.ExceptionRegion is not { } region) return;

            // Detect using pattern: try { body } finally { if (v != null) v.Dispose(); }
            if (region.Kind == ExceptionRegionKind.Finally
                && TryDetectDisposeVariable(block) is { } disposeVar)
            {
                EmitUsingBlock(block, disposeVar, indent);
                return;
            }

            WriteIndent(indent);
            _sb.AppendLine("try");
            WriteIndent(indent);
            _sb.AppendLine("{");

            // Emit all try body blocks
            if (block.TryChildren.Count > 0)
            {
                foreach (var child in block.TryChildren)
                    EmitStructuredBlock(child, indent + 1);
            }
            else
            {
                EmitBasicBlock(block.BlockIndex, indent + 1);
            }

            WriteIndent(indent);
            _sb.AppendLine("}");

            // Emit the primary handler
            EmitHandler(region, block.HandlerChildren, block, indent);

            // Emit additional handlers (multiple catch)
            foreach (var addl in block.AdditionalHandlers)
                EmitHandler(addl.Region, addl.HandlerChildren, null, indent);
        }

        void EmitHandler(ExceptionRegion region, List<StructuredBlock> handlerChildren, StructuredBlock? block, int indent)
        {
            if (region.Kind == ExceptionRegionKind.Catch)
            {
                string exType = "Exception";
                if (!region.CatchType.IsNil && _reader is not null)
                {
                    var resolved = Metadata.TypeResolver.GetTypeName(
                        _reader, region.CatchType);
                    if (resolved is not null)
                        exType = SimplifyTypeName(resolved);
                }

                // Check if the first handler statement stores the exception to a local
                string? catchVarName = block is not null ? TryExtractCatchVariable(block) : null;

                WriteIndent(indent);
                if (exType is "object" or "System.Object")
                    _sb.AppendLine("catch");
                else if (catchVarName is not null)
                    _sb.AppendLine($"catch ({exType} {catchVarName})");
                else
                    _sb.AppendLine($"catch ({exType})");
                WriteIndent(indent);
                _sb.AppendLine("{");
                foreach (var child in handlerChildren)
                    EmitStructuredBlock(child, indent + 1);
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
            else if (region.Kind == ExceptionRegionKind.Finally)
            {
                WriteIndent(indent);
                _sb.AppendLine("finally");
                WriteIndent(indent);
                _sb.AppendLine("{");
                foreach (var child in handlerChildren)
                    EmitStructuredBlock(child, indent + 1);
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
        }

        /// <summary>
        /// Detect the dispose pattern in a finally handler:
        /// IfThenElse { if (var != null) { var.Dispose(); } } + endfinally
        /// Returns the variable name being disposed, or null.
        /// </summary>
        string? TryDetectDisposeVariable(StructuredBlock block)
        {
            foreach (var child in block.HandlerChildren)
            {
                if (child.Kind == StructuredBlockKind.IfThenElse && child.ThenBlock is { } thenBlock)
                {
                    // The then block should call Dispose on a variable
                    if (thenBlock.BlockIndex >= 0 && _blockMap.TryGetValue(thenBlock.BlockIndex, out var thenAst))
                    {
                        foreach (var node in thenAst.Nodes)
                        {
                            if (node is ILAstStatement { Expression: var expr }
                                && expr.Operand is string operand
                                && operand.Contains("Dispose", StringComparison.Ordinal)
                                && !expr.IsStaticCall
                                && expr.Arguments.Count > 0)
                            {
                                return RenderReceiverName(expr.Arguments[0]);
                            }
                        }
                    }
                }
            }
            return null;
        }

        void EmitUsingBlock(StructuredBlock block, string disposeVar, int indent)
        {
            _suppressedLocals.Add(disposeVar);
            // Find the initialization assignment for the disposed variable
            // It should be in the sequence block immediately before this try/catch
            var parent = FindParentSequence(block);
            int initBlockIndex = -1;

            if (parent is not null)
            {
                // Look backwards through siblings for an assignment to disposeVar
                for (int i = parent.Children.Count - 1; i >= 0; i--)
                {
                    var sibling = parent.Children[i];
                    if (sibling == block) continue;
                    if (sibling.Kind == StructuredBlockKind.BasicBlock
                        && sibling.BlockIndex >= 0
                        && _blockMap.TryGetValue(sibling.BlockIndex, out var sibAst))
                    {
                        for (int j = sibAst.Nodes.Count - 1; j >= 0; j--)
                        {
                            if (sibAst.Nodes[j] is ILAstStatement { Expression: var stExpr }
                                && stExpr.OpCode is ILOpCode.Stloc or ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                                    or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s
                                && stExpr.Operand == disposeVar
                                && stExpr.Arguments.Count > 0)
                            {
                                // Found the stloc that initializes the variable
                                initBlockIndex = sibling.BlockIndex;
                                break;
                            }
                        }
                        if (initBlockIndex >= 0) break;
                    }
                }
            }

            // Emit: using (type var = expr) or using var var = expr;
            ILAstExpression? initExpression = null;
            if (initBlockIndex >= 0 && _blockMap.TryGetValue(initBlockIndex, out var initBlock))
            {
                // Find the initialization expression
                foreach (var node in initBlock.Nodes)
                {
                    if (node is ILAstStatement { Expression: var stExpr }
                        && stExpr.OpCode is ILOpCode.Stloc or ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                            or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s
                        && stExpr.Operand == disposeVar
                        && stExpr.Arguments.Count > 0)
                    {
                        _skipNodes.Add(node);
                        initExpression = stExpr.Arguments[0];
                        break;
                    }
                }
            }

            // Check for foreach pattern: GetEnumerator + MoveNext loop + Current
            if (initExpression is not null
                && initExpression.Operand is string initOp
                && initOp.Contains("GetEnumerator", StringComparison.Ordinal)
                && TryEmitForeach(block, disposeVar, initExpression, indent))
            {
                // Handler blocks consumed
                MarkHandlerConsumed(block);
                return;
            }

            // Regular using emission
            if (initExpression is not null)
            {
                WriteIndent(indent);
                _sb.Append($"using var {disposeVar} = ");
                EmitExpression(initExpression);
                _sb.AppendLine(";");
            }
            else
            {
                WriteIndent(indent);
                _sb.AppendLine($"using ({disposeVar})");
            }

            // Emit the try body (without the try/finally wrapper)
            if (block.TryChildren.Count > 0)
            {
                foreach (var child in block.TryChildren)
                    EmitStructuredBlock(child, indent);
            }
            else
            {
                EmitBasicBlock(block.BlockIndex, indent);
            }

            MarkHandlerConsumed(block);
        }

        void MarkHandlerConsumed(StructuredBlock block)
        {
            // Mark handler blocks as consumed (don't emit the finally)
            foreach (var child in block.HandlerChildren)
            {
                if (child.BlockIndex >= 0) _consumedBlocks.Add(child.BlockIndex);
                if (child.ThenBlock?.BlockIndex >= 0) _consumedBlocks.Add(child.ThenBlock.BlockIndex);
                if (child.ElseBlock?.BlockIndex >= 0) _consumedBlocks.Add(child.ElseBlock.BlockIndex);
                if (child.ConditionBlockIndex >= 0) _consumedBlocks.Add(child.ConditionBlockIndex);
            }
        }

        bool TryEmitForeach(StructuredBlock block, string enumeratorVar, ILAstExpression getEnumeratorExpr, int indent)
        {
            // Find the MoveNext loop and Current access in try body blocks
            // Pattern: loop with MoveNext condition, body has Current assignment
            var tryBlockIndices = block.TryChildren
                .Where(c => c.BlockIndex >= 0)
                .Select(c => c.BlockIndex)
                .ToHashSet();

            // Find the loop within the try body
            NaturalLoop? foreachLoop = null;
            foreach (var loop in _structure.Loops)
            {
                if (tryBlockIndices.Contains(loop.HeaderIndex))
                {
                    foreachLoop = loop;
                    break;
                }
            }
            if (foreachLoop is null) return false;

            // Verify the loop header has a MoveNext call
            if (!_blockMap.TryGetValue(foreachLoop.HeaderIndex, out var headerBlock))
                return false;

            bool hasMoveNext = false;
            foreach (var node in headerBlock.Nodes)
            {
                if (node is ILAstStatement { Expression: var expr }
                    && HasCallInTree(expr, "MoveNext"))
                {
                    hasMoveNext = true;
                    break;
                }
            }
            if (!hasMoveNext) return false;

            // Find Current access in the loop body — look for .Current property get
            string? elementVar = null;
            int currentBlockIdx = -1;
            int currentNodeIdx = -1;

            foreach (int bodyIdx in foreachLoop.BodyIndices.OrderBy(x => x))
            {
                if (bodyIdx == foreachLoop.HeaderIndex) continue;
                if (!_blockMap.TryGetValue(bodyIdx, out var bodyBlock)) continue;

                for (int ni = 0; ni < bodyBlock.Nodes.Count; ni++)
                {
                    var node = bodyBlock.Nodes[ni];
                    if (node is ILAstStatement { Expression: var stExpr }
                        && stExpr.Arguments.Count > 0
                        && HasCallInTree(stExpr.Arguments[0], "get_Current"))
                    {
                        elementVar = stExpr.Operand;
                        currentBlockIdx = bodyIdx;
                        currentNodeIdx = ni;
                        break;
                    }
                }
                if (elementVar is not null) break;
            }
            if (elementVar is null) return false;

            // Extract collection from GetEnumerator receiver
            ILAstExpression? collection = null;
            if (!getEnumeratorExpr.IsStaticCall && getEnumeratorExpr.Arguments.Count > 0)
                collection = getEnumeratorExpr.Arguments[0];

            // Get element type from the local's type
            string elementType = "var";
            foreach (var local in _ast.Locals)
            {
                if (local.Name == elementVar && local.TypeName is not null)
                {
                    elementType = SimplifyTypeName(local.TypeName);
                    break;
                }
            }

            // Suppress element variable and enumerator variable declarations
            _suppressedLocals.Add(elementVar);

            // Emit: foreach (type element in collection)
            WriteIndent(indent);
            _sb.Append($"foreach ({elementType} {elementVar} in ");
            if (collection is not null)
                EmitExpression(collection);
            else
                _sb.Append(enumeratorVar); // fallback
            _sb.AppendLine(")");
            WriteIndent(indent);
            _sb.AppendLine("{");

            // Emit loop body blocks (excluding header/MoveNext and Current assignment)
            _skipNodes.Add(headerBlock.Nodes[^1]); // Skip MoveNext branch
            if (currentBlockIdx >= 0 && currentNodeIdx >= 0
                && _blockMap.TryGetValue(currentBlockIdx, out var curBlock))
                _skipNodes.Add(curBlock.Nodes[currentNodeIdx]); // Skip Current assignment

            foreach (int bodyIdx in foreachLoop.BodyIndices.OrderBy(x => x))
            {
                if (bodyIdx == foreachLoop.HeaderIndex) continue;
                _consumedBlocks.Remove(bodyIdx); // Ensure body blocks can be emitted
                EmitBasicBlock(bodyIdx, indent + 1);
            }

            WriteIndent(indent);
            _sb.AppendLine("}");

            // Mark all try blocks as consumed, suppress leave targets
            foreach (var child in block.TryChildren)
            {
                if (child.BlockIndex >= 0)
                {
                    _consumedBlocks.Add(child.BlockIndex);
                    // Suppress leave target labels
                    if (_blockMap.TryGetValue(child.BlockIndex, out var tryAst2))
                    {
                        foreach (var node in tryAst2.Nodes)
                        {
                            if (node is ILAstStatement { Expression: { OpCode: ILOpCode.Leave or ILOpCode.Leave_s, Operand: string leaveTarget } })
                                _loopConsumedLabels.Add(leaveTarget);
                        }
                    }
                }
            }

            return true;
        }

        static bool HasCallInTree(ILAstExpression expr, string methodName)
        {
            if (expr.Operand is string op && op.Contains(methodName, StringComparison.Ordinal))
                return true;
            foreach (var arg in expr.Arguments)
                if (HasCallInTree(arg, methodName))
                    return true;
            return false;
        }

        StructuredBlock? FindParentSequence(StructuredBlock target)
        {
            return FindParent(_structure.Root, target);

            static StructuredBlock? FindParent(StructuredBlock current, StructuredBlock target)
            {
                foreach (var child in current.Children)
                {
                    if (child == target) return current;
                    var found = FindParent(child, target);
                    if (found is not null) return found;
                }
                return null;
            }
        }

        void EmitSwitch(StructuredBlock block, int indent)
        {
            // Extract the switch value expression from the switch block's last node
            string switchValue = "/* value */";
            if (block.SwitchBlockIndex >= 0 && _blockMap.TryGetValue(block.SwitchBlockIndex, out var switchBlock))
            {
                _consumedBlocks.Add(block.SwitchBlockIndex);
                _currentBlockNodes = switchBlock.Nodes;

                // Emit any statements before the switch instruction
                for (int i = 0; i < switchBlock.Nodes.Count - 1; i++)
                {
                    if (switchBlock.Nodes[i] is ILAstStatement stmt)
                        EmitStatement(stmt.Expression, indent);
                    else if (switchBlock.Nodes[i] is ILAstAssignment assign)
                    {
                        WriteIndent(indent);
                        _sb.Append($"{assign.Variable.Name} = ");
                        EmitExpression(assign.Value);
                        _sb.AppendLine(";");
                    }
                }

                // Extract switch value from the last node (the switch instruction)
                var lastNode = switchBlock.Nodes.LastOrDefault();
                if (lastNode is ILAstStatement { Expression: var switchExpr }
                    && switchExpr.OpCode == ILOpCode.Switch
                    && switchExpr.Arguments.Count > 0)
                {
                    switchValue = ExpressionToString(switchExpr.Arguments[0]);
                }

                _currentBlockNodes = null;
            }

            WriteIndent(indent);
            _sb.AppendLine($"switch ({switchValue})");
            WriteIndent(indent);
            _sb.AppendLine("{");

            // Group cases that target the same block
            var blockToCases = new Dictionary<int, List<int>>();
            foreach (var (caseValue, targetIdx) in block.SwitchCases)
            {
                if (!blockToCases.TryGetValue(targetIdx, out var cases))
                {
                    cases = [];
                    blockToCases[targetIdx] = cases;
                }
                cases.Add(caseValue);
            }

            // Emit case groups in order of first case value
            foreach (var (targetIdx, cases) in blockToCases.OrderBy(kv => kv.Value[0]))
            {
                // Skip cases that target the default block (they fall through to default)
                if (targetIdx == block.SwitchDefaultIndex)
                    continue;

                foreach (int caseVal in cases)
                {
                    WriteIndent(indent + 1);
                    _sb.AppendLine($"case {caseVal}:");
                }
                EmitBasicBlock(targetIdx, indent + 2);
                // Add break if block doesn't end with return/throw
                if (!BlockEndsWithReturn(targetIdx))
                {
                    WriteIndent(indent + 2);
                    _sb.AppendLine("break;");
                }
            }

            // Emit default case
            if (block.SwitchDefaultIndex >= 0)
            {
                WriteIndent(indent + 1);
                _sb.AppendLine("default:");
                EmitBasicBlock(block.SwitchDefaultIndex, indent + 2);
                if (!BlockEndsWithReturn(block.SwitchDefaultIndex))
                {
                    WriteIndent(indent + 2);
                    _sb.AppendLine("break;");
                }
            }

            WriteIndent(indent);
            _sb.AppendLine("}");
        }

        bool BlockEndsWithReturn(int blockIndex)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var astBlock))
                return false;
            var lastNode = astBlock.Nodes.LastOrDefault();
            return lastNode is ILAstStatement { Expression.OpCode:
                ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow
                or ILOpCode.Br or ILOpCode.Br_s };
        }

        // --- Expression emission ---

        void EmitStatement(ILAstExpression expr, int indent)
        {
            switch (expr.OpCode)
            {
                case ILOpCode.Ret:
                    WriteIndent(indent);
                    if (expr.Arguments.Count > 0)
                    {
                        _sb.Append("return ");
                        EmitExpression(expr.Arguments[0]);
                        _sb.AppendLine(";");
                    }
                    else
                    {
                        _sb.AppendLine("return;");
                    }
                    break;

                case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or
                     ILOpCode.Stloc_s or ILOpCode.Stloc:
                {
                    string varName = expr.Operand ?? GetLocalName(expr.OpCode);
                    WriteIndent(indent);
                    _sb.Append($"{varName} = ");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    else
                        _sb.Append("/* value */");
                    _sb.AppendLine(";");
                    break;
                }

                case ILOpCode.Stfld or ILOpCode.Stsfld:
                {
                    WriteIndent(indent);
                    if (expr.OpCode == ILOpCode.Stfld && expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append($".{ExtractMemberName(expr.Operand)} = ");
                        EmitExpression(expr.Arguments[1]);
                    }
                    else if (expr.OpCode == ILOpCode.Stsfld && expr.Arguments.Count >= 1)
                    {
                        _sb.Append($"{expr.Operand} = ");
                        EmitExpression(expr.Arguments[0]);
                    }
                    else
                    {
                        _sb.Append($"{expr.Operand} = /* value */");
                    }
                    _sb.AppendLine(";");
                    break;
                }

                // Array element store: array[index] = value;
                case ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or
                     ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
                     ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref:
                {
                    WriteIndent(indent);
                    if (expr.Arguments.Count >= 3)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append("] = ");
                        EmitExpression(expr.Arguments[2]);
                    }
                    else
                    {
                        _sb.Append("/* stelem */");
                    }
                    _sb.AppendLine(";");
                    break;
                }

                case ILOpCode.Call or ILOpCode.Callvirt:
                    WriteIndent(indent);
                    EmitCallExpression(expr);
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Throw:
                    WriteIndent(indent);
                    _sb.Append("throw ");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Rethrow:
                    WriteIndent(indent);
                    _sb.AppendLine("throw;");
                    break;

                case ILOpCode.Starg_s or ILOpCode.Starg:
                    WriteIndent(indent);
                    _sb.Append($"{RemapArg(expr.Operand, expr.OpCode)} = ");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Initobj:
                    WriteIndent(indent);
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.AppendLine($" = default({SimplifyTypeName(expr.Operand ?? "?")});");
                    }
                    else
                    {
                        _sb.AppendLine($"/* initobj {SimplifyTypeName(expr.Operand ?? "?")} */");
                    }
                    break;

                // Indirect stores: *addr = value
                case ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or
                     ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or
                     ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref or
                     ILOpCode.Stobj:
                    WriteIndent(indent);
                    if (expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append(" = ");
                        EmitExpression(expr.Arguments[1]);
                    }
                    _sb.AppendLine(";");
                    break;

                case ILOpCode.Pop:
                    // Suppress pop of catch handler exception (S_in_0)
                    if (expr.Arguments.Count > 0
                        && expr.Arguments[0].Operand is string popOp
                        && popOp.StartsWith("S_in_", StringComparison.Ordinal))
                        break;
                    // Typically a discarded expression — emit as expression statement
                    if (expr.Arguments.Count > 0)
                    {
                        WriteIndent(indent);
                        EmitExpression(expr.Arguments[0]);
                        _sb.AppendLine(";");
                    }
                    break;

                // Branches become comments in the initial output
                case ILOpCode.Br or ILOpCode.Br_s:
                    // Suppress gotos into loop headers (the while condition replaces them)
                    if (expr.Operand is string brTarget && _loopHeaderLabels.Contains(brTarget))
                        break;
                    // Inline goto-to-return: if target block only has a return, emit return directly
                    if (expr.Operand is string brRetTarget && TryEmitInlinedReturn(brRetTarget, indent))
                        break;
                    WriteIndent(indent);
                    _sb.AppendLine($"goto {expr.Operand};");
                    break;

                case ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s:
                case ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s:
                case ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bgt or ILOpCode.Bgt_s:
                case ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Blt or ILOpCode.Blt_s:
                case ILOpCode.Bge_un or ILOpCode.Bge_un_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s:
                case ILOpCode.Ble_un or ILOpCode.Ble_un_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s:
                    // Conditional branches — when not consumed by structuring
                    WriteIndent(indent);
                    _sb.Append($"if (");
                    EmitBranchCondition(expr);
                    _sb.AppendLine($") goto {expr.Operand};");
                    break;

                case ILOpCode.Leave or ILOpCode.Leave_s:
                    // leave exits a try/catch block — emit goto if target is a labeled block
                    if (expr.Operand is string leaveTarget && _gotoTargets.Contains(leaveTarget))
                    {
                        // Inline leave-to-return when possible
                        if (!TryEmitInlinedReturn(leaveTarget, indent))
                        {
                            WriteIndent(indent);
                            _sb.AppendLine($"goto {leaveTarget};");
                        }
                    }
                    break;

                case ILOpCode.Endfinally:
                    break;

                default:
                    WriteIndent(indent);
                    _sb.Append("/* ");
                    expr.WriteTo(_sb, 0);
                    _sb.AppendLine(" */");
                    break;
            }
        }

        void EmitExpression(ILAstExpression expr)
        {
            switch (expr.OpCode)
            {
                // Constants
                case ILOpCode.Ldc_i4_m1: _sb.Append("-1"); break;
                case ILOpCode.Ldc_i4_0: _sb.Append('0'); break;
                case ILOpCode.Ldc_i4_1: _sb.Append('1'); break;
                case ILOpCode.Ldc_i4_2: _sb.Append('2'); break;
                case ILOpCode.Ldc_i4_3: _sb.Append('3'); break;
                case ILOpCode.Ldc_i4_4: _sb.Append('4'); break;
                case ILOpCode.Ldc_i4_5: _sb.Append('5'); break;
                case ILOpCode.Ldc_i4_6: _sb.Append('6'); break;
                case ILOpCode.Ldc_i4_7: _sb.Append('7'); break;
                case ILOpCode.Ldc_i4_8: _sb.Append('8'); break;
                case ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4:
                    _sb.Append(expr.Operand ?? "0");
                    break;
                case ILOpCode.Ldc_i8:
                    _sb.Append($"{expr.Operand ?? "0"}L");
                    break;
                case ILOpCode.Ldc_r4:
                    _sb.Append($"{expr.Operand ?? "0"}f");
                    break;
                case ILOpCode.Ldc_r8:
                    _sb.Append(expr.Operand ?? "0.0");
                    break;
                case ILOpCode.Ldnull:
                    _sb.Append("null");
                    break;
                case ILOpCode.Ldstr:
                    _sb.Append(expr.Operand ?? "\"\"");
                    break;

                // Argument/local loads
                case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or
                     ILOpCode.Ldarg_s or ILOpCode.Ldarg:
                    _sb.Append(RemapArg(expr.Operand, expr.OpCode));
                    break;
                case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or
                     ILOpCode.Ldloc_s or ILOpCode.Ldloc:
                    _sb.Append(expr.Operand ?? GetLocalName(expr.OpCode));
                    break;

                // Binary arithmetic/logic operators
                case ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un:
                    EmitBinary(expr, "+"); break;
                case ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un:
                    EmitBinary(expr, "-"); break;
                case ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un:
                    EmitBinary(expr, "*"); break;
                case ILOpCode.Div or ILOpCode.Div_un:
                    EmitBinary(expr, "/"); break;
                case ILOpCode.Rem or ILOpCode.Rem_un:
                    EmitBinary(expr, "%"); break;
                case ILOpCode.And: EmitBinary(expr, "&"); break;
                case ILOpCode.Or: EmitBinary(expr, "|"); break;
                case ILOpCode.Xor: EmitBinary(expr, "^"); break;
                case ILOpCode.Shl: EmitBinary(expr, "<<"); break;
                case ILOpCode.Shr or ILOpCode.Shr_un:
                    EmitBinary(expr, ">>"); break;

                // Comparison operators
                case ILOpCode.Ceq: EmitBinary(expr, expr.Operand ?? "=="); break;
                case ILOpCode.Cgt or ILOpCode.Cgt_un: EmitBinary(expr, ">"); break;
                case ILOpCode.Clt or ILOpCode.Clt_un: EmitBinary(expr, "<"); break;

                // Unary operators
                case ILOpCode.Neg:
                    _sb.Append('-');
                    EmitParenthesized(expr, 0);
                    break;
                case ILOpCode.Not:
                    _sb.Append('~');
                    EmitParenthesized(expr, 0);
                    break;

                // Conversions
                case ILOpCode.Conv_i1 or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i1_un:
                    EmitCast(expr, "sbyte"); break;
                case ILOpCode.Conv_u1 or ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un:
                    EmitCast(expr, "byte"); break;
                case ILOpCode.Conv_i2 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i2_un:
                    EmitCast(expr, "short"); break;
                case ILOpCode.Conv_u2 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u2_un:
                    EmitCast(expr, "char"); break;
                case ILOpCode.Conv_i4 or ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i4_un:
                    EmitCast(expr, "int"); break;
                case ILOpCode.Conv_u4 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u4_un:
                    EmitCast(expr, "uint"); break;
                case ILOpCode.Conv_i8 or ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_i8_un:
                    EmitCast(expr, "long"); break;
                case ILOpCode.Conv_u8 or ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_u8_un:
                    EmitCast(expr, "ulong"); break;
                case ILOpCode.Conv_r4: EmitCast(expr, "float"); break;
                case ILOpCode.Conv_r8 or ILOpCode.Conv_r_un: EmitCast(expr, "double"); break;
                case ILOpCode.Conv_i or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_i_un:
                    EmitCast(expr, "nint"); break;
                case ILOpCode.Conv_u or ILOpCode.Conv_ovf_u or ILOpCode.Conv_ovf_u_un:
                    EmitCast(expr, "nuint"); break;

                // Type casts
                case ILOpCode.Castclass:
                    _sb.Append($"({SimplifyTypeName(expr.Operand ?? "object")})");
                    EmitParenthesized(expr, 0);
                    break;
                case ILOpCode.Isinst:
                    EmitParenthesized(expr, 0);
                    _sb.Append($" as {SimplifyTypeName(expr.Operand ?? "object")}");
                    break;

                // Object creation
                case ILOpCode.Newobj:
                {
                    string typeName = ExtractTypeName(expr.Operand);
                    string simplified = SimplifyTypeName(typeName);

                    // Delegate construction with closure lambda: new Func<T,R>(closure, <Method>b__N)
                    // Simplify to lambda annotation
                    if (expr.Arguments.Count == 2
                        && expr.Arguments[1] is { Operand: string lambdaName }
                        && lambdaName.Contains(">b__", StringComparison.Ordinal))
                    {
                        string cleanLambda = SimplifyLambdaName(lambdaName);
                        _sb.Append($"/* {cleanLambda} */");
                        break;
                    }

                    _sb.Append($"new {simplified}(");
                    // Skip 'this' argument (first arg for instance constructor)
                    for (int i = 0; i < expr.Arguments.Count; i++)
                    {
                        if (i > 0) _sb.Append(", ");
                        EmitExpression(expr.Arguments[i]);
                    }
                    _sb.Append(')');
                    break;
                }

                case ILOpCode.Newarr:
                    _sb.Append($"new {SimplifyTypeName(expr.Operand ?? "object")}[");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.Append(']');
                    break;

                // Field access
                case ILOpCode.Ldfld:
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('.');
                    }
                    _sb.Append(ExtractMemberName(expr.Operand));
                    break;
                case ILOpCode.Ldsfld:
                    _sb.Append(expr.Operand ?? "/* field */");
                    break;

                // Method calls
                case ILOpCode.Call or ILOpCode.Callvirt:
                    EmitCallExpression(expr);
                    break;

                // Indirect loads: *addr → value (pass through address expression)
                case ILOpCode.Ldind_i or ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or
                     ILOpCode.Ldind_i4 or ILOpCode.Ldind_i8 or
                     ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4 or
                     ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref or
                     ILOpCode.Ldobj:
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    break;

                // Boxing
                case ILOpCode.Box:
                    EmitParenthesized(expr, 0);
                    _sb.Append($" /* box {SimplifyTypeName(expr.Operand ?? "?")} */");
                    break;
                case ILOpCode.Unbox_any:
                    _sb.Append($"({SimplifyTypeName(expr.Operand ?? "object")})");
                    EmitParenthesized(expr, 0);
                    break;

                // Array operations
                case ILOpCode.Ldlen:
                    EmitParenthesized(expr, 0);
                    _sb.Append(".Length");
                    break;
                case ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or
                     ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or
                     ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref or
                     ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4:
                    if (expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append(']');
                    }
                    break;
                case ILOpCode.Ldelema:
                    if (expr.Arguments.Count >= 2)
                    {
                        _sb.Append("ref ");
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append(']');
                    }
                    break;

                // Dup (pass through, or reconstruct from preceding expression in block)
                case ILOpCode.Dup:
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    else if (_currentBlockNodes is not null)
                    {
                        // Find the preceding expression that produced the dup'd value
                        var preceding = FindPrecedingValue(_currentBlockNodes, expr);
                        if (preceding is not null)
                            EmitExpression(preceding);
                        else
                            _sb.Append("/* dup */");
                    }
                    else
                        _sb.Append("/* dup */");
                    break;

                // Address operations
                case ILOpCode.Ldarga_s or ILOpCode.Ldarga:
                    _sb.Append($"ref {RemapArg(expr.Operand, expr.OpCode)}");
                    break;
                case ILOpCode.Ldloca_s or ILOpCode.Ldloca:
                    _sb.Append($"{expr.Operand}");
                    break;
                case ILOpCode.Ldflda:
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('.');
                    }
                    _sb.Append(ExtractMemberName(expr.Operand));
                    break;

                case ILOpCode.Sizeof:
                    _sb.Append($"sizeof({SimplifyTypeName(expr.Operand ?? "?")})");
                    break;

                case ILOpCode.Ldtoken:
                    _sb.Append($"typeof({SimplifyTypeName(expr.Operand ?? "?")})");
                    break;

                // Function pointer (delegate creation pattern)
                case ILOpCode.Ldftn:
                    if (expr.Operand is not null)
                    {
                        int ci = expr.Operand.IndexOf("::", StringComparison.Ordinal);
                        _sb.Append(ci >= 0 ? expr.Operand[(ci + 2)..] : expr.Operand);
                    }
                    break;
                case ILOpCode.Ldvirtftn:
                    if (expr.Arguments.Count > 0 && expr.Operand is not null)
                    {
                        EmitExpression(expr.Arguments[0]);
                        int ci = expr.Operand.IndexOf("::", StringComparison.Ordinal);
                        _sb.Append($".{(ci >= 0 ? expr.Operand[(ci + 2)..] : expr.Operand)}");
                    }
                    break;

                case ILOpCode.Localloc:
                    _sb.Append("stackalloc byte[");
                    if (expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    _sb.Append(']');
                    break;

                // Block-entry stack values (synthetic ldloc for cross-block values)
                case ILOpCode.Nop when expr.Operand is not null:
                    if (_nullConditionalReceiver is not null
                        && expr.Operand.StartsWith("S_in_", StringComparison.Ordinal))
                        _sb.Append(_nullConditionalReceiver);
                    else
                        _sb.Append(expr.Operand);
                    break;

                default:
                    _sb.Append("/* ");
                    expr.WriteTo(_sb, 0);
                    _sb.Append(" */");
                    break;
            }
        }

        void EmitCallArgument(ILAstExpression arg)
        {
            if (arg.OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca
                or ILOpCode.Ldarga_s or ILOpCode.Ldarga)
                _sb.Append("ref ");
            EmitExpression(arg);
        }

        void EmitCallExpression(ILAstExpression expr)
        {
            string? methodName = expr.Operand;
            if (methodName is null)
            {
                _sb.Append("/* call */");
                return;
            }

            // Parse "TypeName::MethodName()" format
            string typePart = "";
            string memberPart = methodName;
            int colonIdx = methodName.IndexOf("::", StringComparison.Ordinal);
            if (colonIdx >= 0)
            {
                typePart = methodName[..colonIdx];
                memberPart = methodName[(colonIdx + 2)..].TrimEnd('(', ')');
            }

            // String interpolation: ToStringAndClear() on a detected handler → $"..."
            if (memberPart is "ToStringAndClear" or "ToString"
                && typePart.Contains("DefaultInterpolatedStringHandler", StringComparison.Ordinal)
                && !expr.IsStaticCall && expr.Arguments.Count > 0)
            {
                string receiverName = RenderReceiverName(expr.Arguments[0]);
                if (_interpolationParts.TryGetValue(receiverName, out var parts))
                {
                    EmitInterpolatedString(parts);
                    return;
                }
            }

            // Base/chaining constructor call: this..ctor() → /* base..ctor() */
            if (memberPart == ".ctor" && !expr.IsStaticCall && expr.Arguments.Count > 0
                && expr.Arguments[0].OpCode is ILOpCode.Ldarg_0)
            {
                _sb.Append($"/* base({SimplifyTypeName(typePart)}) */");
                return;
            }

            bool isStatic = expr.IsStaticCall;

            if (isStatic && typePart.Length > 0)
            {
                // Static property getter sugar: Type.get_XXX() → Type.XXX
                if (memberPart.StartsWith("get_", StringComparison.Ordinal) && expr.Arguments.Count == 0)
                {
                    _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart[4..]}");
                }
                // Static property setter sugar: Type.set_XXX(value) → Type.XXX = value
                else if (memberPart.StartsWith("set_", StringComparison.Ordinal) && expr.Arguments.Count == 1)
                {
                    _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart[4..]} = ");
                    EmitCallArgument(expr.Arguments[0]);
                }
                // Operator sugar: op_Equality → ==, op_Inequality → !=, etc.
                else if (memberPart.StartsWith("op_", StringComparison.Ordinal) && expr.Arguments.Count == 2)
                {
                    string? opSymbol = memberPart switch
                    {
                        "op_Equality" => "==",
                        "op_Inequality" => "!=",
                        "op_GreaterThan" => ">",
                        "op_LessThan" => "<",
                        "op_GreaterThanOrEqual" => ">=",
                        "op_LessThanOrEqual" => "<=",
                        "op_Addition" => "+",
                        "op_Subtraction" => "-",
                        "op_Multiply" => "*",
                        "op_Division" => "/",
                        "op_Modulus" => "%",
                        "op_BitwiseAnd" => "&",
                        "op_BitwiseOr" => "|",
                        "op_ExclusiveOr" => "^",
                        "op_LeftShift" => "<<",
                        "op_RightShift" => ">>",
                        _ => null
                    };
                    if (opSymbol is not null)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append($" {opSymbol} ");
                        EmitExpression(expr.Arguments[1]);
                    }
                    else
                    {
                        _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart}(");
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append(", ");
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append(')');
                    }
                }
                else
                {
                    // Static call: TypeName.Method(args)
                    _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart}(");
                    for (int i = 0; i < expr.Arguments.Count; i++)
                    {
                        if (i > 0) _sb.Append(", ");
                        EmitCallArgument(expr.Arguments[i]);
                    }
                    _sb.Append(')');
                }
            }
            else if (!isStatic && expr.Arguments.Count > 0)
            {
                // Instance call: receiver.Method(args) or receiver?.Method(args)
                bool isNullConditionalCall = _nullConditionalReceiver is not null
                    && expr.Arguments[0] is { OpCode: ILOpCode.Nop, Operand: { } op }
                    && op.StartsWith("S_in_", StringComparison.Ordinal);
                string dot = isNullConditionalCall ? "?." : ".";

                // Indexer getter: get_Item(key) → [key]
                if (memberPart == "get_Item" && expr.Arguments.Count == 2)
                {
                    EmitExpression(expr.Arguments[0]);
                    _sb.Append('[');
                    EmitCallArgument(expr.Arguments[1]);
                    _sb.Append(']');
                }
                // Indexer setter: set_Item(key, value) → [key] = value
                else if (memberPart == "set_Item" && expr.Arguments.Count == 3)
                {
                    EmitExpression(expr.Arguments[0]);
                    _sb.Append('[');
                    EmitCallArgument(expr.Arguments[1]);
                    _sb.Append("] = ");
                    EmitCallArgument(expr.Arguments[2]);
                }
                // Property getter sugar: get_XXX() → .XXX
                else if (memberPart.StartsWith("get_", StringComparison.Ordinal) && expr.Arguments.Count == 1)
                {
                    EmitExpression(expr.Arguments[0]);
                    _sb.Append($"{dot}{memberPart[4..]}");
                }
                // Property setter sugar: set_XXX(value) → .XXX = value
                else if (memberPart.StartsWith("set_", StringComparison.Ordinal) && expr.Arguments.Count == 2)
                {
                    EmitExpression(expr.Arguments[0]);
                    _sb.Append($"{dot}{memberPart[4..]} = ");
                    EmitCallArgument(expr.Arguments[1]);
                }
                else
                {
                    EmitExpression(expr.Arguments[0]);
                    _sb.Append($"{dot}{memberPart}(");
                    for (int i = 1; i < expr.Arguments.Count; i++)
                    {
                        if (i > 1) _sb.Append(", ");
                        EmitCallArgument(expr.Arguments[i]);
                    }
                    _sb.Append(')');
                }
            }
            else
            {
                // Fallback: no receiver or args
                if (memberPart.StartsWith("get_", StringComparison.Ordinal))
                    _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart[4..]}");
                else
                    _sb.Append($"{SimplifyTypeName(typePart)}.{memberPart}()");
            }
        }

        void EmitBranchCondition(ILAstExpression expr)
        {
            switch (expr.OpCode)
            {
                case ILOpCode.Brfalse or ILOpCode.Brfalse_s:
                    if (expr.Arguments.Count > 0)
                    {
                        var arg = expr.Arguments[0];

                        // If the argument is a comparison call (op_Equality, ceq, etc.),
                        // negate the comparison operator instead of prepending !
                        if (TryEmitNegatedComparison(arg))
                            break;

                        // For object references, emit "expr == null" instead of "!expr"
                        if (arg.ResultType.Kind is StackValueKind.ObjRef)
                        {
                            EmitExpression(arg);
                            _sb.Append(" == null");
                        }
                        else if (IsNonBooleanNumeric(arg))
                        {
                            EmitExpression(arg);
                            _sb.Append(" == 0");
                        }
                        else
                        {
                            // Boolean result — negate directly
                            _sb.Append('!');
                            EmitParenthesized(expr, 0);
                        }
                    }
                    break;
                case ILOpCode.Brtrue or ILOpCode.Brtrue_s:
                    if (expr.Arguments.Count > 0)
                    {
                        var btArg = expr.Arguments[0];
                        if (btArg.ResultType.Kind is StackValueKind.ObjRef)
                        {
                            EmitExpression(btArg);
                            _sb.Append(" != null");
                        }
                        else if (IsNonBooleanNumeric(btArg))
                        {
                            EmitExpression(btArg);
                            _sb.Append(" != 0");
                        }
                        else
                        {
                            EmitExpression(btArg);
                        }
                    }
                    break;
                case ILOpCode.Beq or ILOpCode.Beq_s:
                    EmitBinaryCondition(expr, "=="); break;
                case ILOpCode.Bne_un or ILOpCode.Bne_un_s:
                    EmitBinaryCondition(expr, "!="); break;
                case ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s:
                    EmitBinaryCondition(expr, ">="); break;
                case ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s:
                    EmitBinaryCondition(expr, ">"); break;
                case ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s:
                    EmitBinaryCondition(expr, "<="); break;
                case ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s:
                    EmitBinaryCondition(expr, "<"); break;
                default:
                    EmitExpression(expr);
                    break;
            }
        }

        /// <summary>
        /// For brfalse, try to negate the argument's comparison instead of prepending !.
        /// E.g., op_Equality → op_Inequality, ceq → emit !=.
        /// Returns true if it emitted the negated form.
        /// </summary>
        bool TryEmitNegatedComparison(ILAstExpression arg)
        {
            // Handle call to op_Equality → emit as !=
            if (arg.OpCode is ILOpCode.Call or ILOpCode.Callvirt && arg.Operand is string operand)
            {
                if (operand.Contains("::op_Equality"))
                {
                    // Emit as arg0 != arg1 using the sugar path
                    if (arg.Arguments.Count >= 2)
                    {
                        EmitExpression(arg.Arguments[0]);
                        _sb.Append(" != ");
                        EmitExpression(arg.Arguments[1]);
                        return true;
                    }
                }
            }

            // Handle ceq → emit as !=
            if (arg.OpCode is ILOpCode.Ceq && arg.Arguments.Count >= 2)
            {
                EmitExpression(arg.Arguments[0]);
                _sb.Append(" != ");
                EmitExpression(arg.Arguments[1]);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the expression produces a numeric (non-boolean) Int32 value.
        /// Conservative: only returns true for known numeric patterns (lengths, counts, arithmetic).
        /// </summary>
        static bool IsNonBooleanNumeric(ILAstExpression expr)
        {
            if (expr.ResultType.Kind is not (StackValueKind.Int32 or StackValueKind.NativeInt))
                return false;

            // Comparison instructions produce boolean results
            if (expr.OpCode is ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Clt
                or ILOpCode.Cgt_un or ILOpCode.Clt_un)
                return false;

            // Arithmetic and conversion produce numeric results
            if (expr.OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div
                or ILOpCode.Rem or ILOpCode.Conv_i4 or ILOpCode.Conv_i or ILOpCode.Conv_u4
                or ILOpCode.Ldlen)
                return true;

            // Local/arg loads of Int32 are numeric when the source is known numeric
            if (expr.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloc_s
                or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
                or ILOpCode.Ldarg or ILOpCode.Ldarg_s
                or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3)
                return true;

            // Properties named Count, Length, Size — known numeric
            if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt && expr.Operand is string opName)
            {
                var member = opName;
                int sep = member.LastIndexOf("::", StringComparison.Ordinal);
                if (sep >= 0) member = member[(sep + 2)..];

                if (member.StartsWith("get_", StringComparison.Ordinal))
                {
                    var prop = member[4..];
                    if (prop is "Count" or "Length" or "Size" or "Rank" or "Capacity")
                        return true;
                }
            }

            // Field loads of Int32 are numeric (e.g., state machine <>1__state)
            if (expr.OpCode is ILOpCode.Ldfld or ILOpCode.Ldsfld)
                return true;

            // Default: assume boolean (conservative — avoids false != 0)
            return false;
        }

        void EmitBinaryCondition(ILAstExpression expr, string op)
        {
            if (expr.Arguments.Count >= 2)
            {
                EmitExpression(expr.Arguments[0]);
                _sb.Append($" {op} ");
                EmitExpression(expr.Arguments[1]);
            }
        }

        // --- Helpers ---

        void EmitBinary(ILAstExpression expr, string op)
        {
            if (expr.Arguments.Count >= 2)
            {
                EmitParenthesized(expr, 0);
                _sb.Append($" {op} ");
                EmitParenthesized(expr, 1);
            }
            else
            {
                _sb.Append($"/* {op} */");
            }
        }

        void EmitParenthesized(ILAstExpression parent, int argIndex)
        {
            if (argIndex >= parent.Arguments.Count) return;
            var arg = parent.Arguments[argIndex];
            bool needsParens = NeedsParentheses(arg);
            if (needsParens) _sb.Append('(');
            EmitExpression(arg);
            if (needsParens) _sb.Append(')');
        }

        void EmitCast(ILAstExpression expr, string typeName)
        {
            _sb.Append($"({typeName})");
            EmitParenthesized(expr, 0);
        }

        void WriteIndent(int indent)
        {
            for (int i = 0; i < indent; i++)
                _sb.Append("    ");
        }

        /// <summary>
        /// Emit an IL offset label if this block is a goto target.
        /// Labels are unindented (column 0) like C labels.
        /// </summary>
        void TryEmitLabel(int blockIndex)
        {
            if (_blockStartOffset.TryGetValue(blockIndex, out int startOffset))
            {
                string label = $"IL_{startOffset:X4}";
                // Suppress labels consumed by while-loop conditions or loop headers
                if (_loopConsumedLabels.Contains(label) || _loopHeaderLabels.Contains(label))
                    return;
                if (_gotoTargets.Contains(label) && _emittedLabels.Add(label))
                    _sb.AppendLine($"{label}:");
            }
        }

        /// <summary>
        /// Find the value expression that a dup instruction duplicated by looking
        /// at the preceding node in the same block. Returns the load/field expression
        /// if found, or null.
        /// </summary>
        static ILAstExpression? FindPrecedingValue(List<ILAstNode> nodes, ILAstExpression dupExpr)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var expr = nodes[i] switch
                {
                    ILAstStatement s => s.Expression,
                    ILAstAssignment a => a.Value,
                    _ => null
                };

                if (expr is null) continue;

                // Look for the dup inside this expression's argument tree
                if (ContainsDup(expr, dupExpr))
                {
                    // The preceding node's stored value is what was dup'd
                    if (i > 0)
                    {
                        return nodes[i - 1] switch
                        {
                            ILAstAssignment prevAssign => new ILAstExpression
                            {
                                OpCode = ILOpCode.Ldloc_0,
                                Operand = prevAssign.Variable.Name,
                                ResultType = prevAssign.Value.ResultType,
                                Offset = dupExpr.Offset
                            },
                            ILAstStatement prevStmt when prevStmt.Expression.OpCode is
                                ILOpCode.Ldfld or ILOpCode.Ldsfld or
                                ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or
                                ILOpCode.Ldloc_s or ILOpCode.Ldloc or
                                ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or
                                ILOpCode.Ldarg_s or ILOpCode.Ldarg
                                => prevStmt.Expression,
                            _ => null
                        };
                    }
                }

                // Also check if this expression IS a branch with a dup argument
                if (expr.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                {
                    if (expr.Arguments.Count > 0 && expr.Arguments[0].OpCode == ILOpCode.Dup && expr.Arguments[0] == dupExpr)
                    {
                        // The dup's source is whatever was loaded before in this block
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (nodes[j] is ILAstAssignment a)
                            {
                                return new ILAstExpression
                                {
                                    OpCode = ILOpCode.Ldloc_0,
                                    Operand = a.Variable.Name,
                                    ResultType = a.Value.ResultType,
                                    Offset = dupExpr.Offset
                                };
                            }
                        }
                    }
                }
            }

            return null;
        }

        static bool ContainsDup(ILAstExpression expr, ILAstExpression target)
        {
            if (ReferenceEquals(expr, target)) return true;
            foreach (var arg in expr.Arguments)
            {
                if (ContainsDup(arg, target)) return true;
            }
            return false;
        }

        string ExpressionToString(ILAstExpression expr)
        {
            var sb = new StringBuilder();
            var saved = _sb;
            // Use a temp context with the new StringBuilder
            var tempCtx = new EmitterContext(_ast, _structure, sb, _reader, _hasThis);
            tempCtx.EmitExpression(expr);
            return sb.ToString();
        }

        /// <summary>
        /// Render a branch expression's condition as a string, handling both
        /// single-argument (brfalse/brtrue) and two-argument (beq/blt/ble/etc.) branches.
        /// </summary>
        string BranchConditionToString(ILAstExpression branchExpr)
        {
            // For single-argument branches (brfalse/brtrue), use ExtractCondition
            if (branchExpr.Arguments.Count == 1)
                return ExpressionToString(ExtractCondition(branchExpr));

            // For comparison-and-branch opcodes (beq, blt, ble, etc.), render via EmitBranchCondition
            var sb = new StringBuilder();
            var tempCtx = new EmitterContext(_ast, _structure, sb, _reader, _hasThis);
            tempCtx.EmitBranchCondition(branchExpr);
            return sb.ToString();
        }

        static string NegateConditionString(string condition)
        {
            // Flip comparison operators if present
            if (condition.Contains(" != "))
                return condition.Replace(" != ", " == ");
            if (condition.Contains(" == "))
                return condition.Replace(" == ", " != ");
            if (condition.Contains(" > ") && !condition.Contains(" >= "))
                return condition.Replace(" > ", " <= ");
            if (condition.Contains(" < ") && !condition.Contains(" <= "))
                return condition.Replace(" < ", " >= ");
            if (condition.Contains(" >= "))
                return condition.Replace(" >= ", " < ");
            if (condition.Contains(" <= "))
                return condition.Replace(" <= ", " > ");

            // Simple negation
            if (condition.StartsWith("!(") && condition.EndsWith(')'))
                return condition[2..^1];
            if (condition.StartsWith('!'))
                return condition[1..];
            return $"!{condition}";
        }

        static ILAstExpression ExtractCondition(ILAstExpression branchExpr)
        {
            // For conditional branches, the condition is the arguments
            if (branchExpr.Arguments.Count == 1)
            {
                var arg = branchExpr.Arguments[0];

                // For brfalse/brtrue on reference types, emit explicit null comparison
                bool isRefType = arg.ResultType.Kind is StackValueKind.ObjRef
                    || (arg.ResultType.TypeName is not null && !IsPrimitiveType(arg.ResultType.TypeName));

                if (isRefType && branchExpr.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s
                    or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                {
                    // The ConditionalDetector assigns then/else based on branch semantics,
                    // so always emit != null (then = non-null path for both brtrue and brfalse)
                    return new ILAstExpression
                    {
                        OpCode = ILOpCode.Ceq,
                        ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                        Operand = "!=",
                        Arguments =
                        {
                            arg,
                            new ILAstExpression
                            {
                                OpCode = ILOpCode.Ldnull,
                                ResultType = StackValue.CreateObjRef(null),
                                Offset = arg.Offset
                            }
                        }
                    };
                }

                // For non-boolean numeric types, emit explicit != 0
                // The ConditionalDetector maps then=true-path, so always use != 0
                if (IsNonBooleanNumeric(arg) && branchExpr.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s
                    or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                {
                    return new ILAstExpression
                    {
                        OpCode = ILOpCode.Ceq,
                        ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                        Operand = "!=",
                        Arguments =
                        {
                            arg,
                            new ILAstExpression
                            {
                                OpCode = ILOpCode.Ldc_i4_0,
                                Operand = "0",
                                ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                                Offset = arg.Offset
                            }
                        }
                    };
                }

                return arg;
            }
            if (branchExpr.Arguments.Count == 2)
            {
                // Binary comparison branch — reconstruct as comparison expression
                return new ILAstExpression
                {
                    OpCode = branchExpr.OpCode switch
                    {
                        ILOpCode.Beq or ILOpCode.Beq_s => ILOpCode.Ceq,
                        ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s => ILOpCode.Cgt,
                        ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s => ILOpCode.Clt,
                        _ => ILOpCode.Ceq
                    },
                    ResultType = StackValue.CreatePrimitive(StackValueKind.Int32),
                    Arguments = { branchExpr.Arguments[0], branchExpr.Arguments[1] }
                };
            }
            return branchExpr;
        }

        static bool IsPrimitiveType(string typeName) => typeName is
            "System.Boolean" or "bool" or "System.Byte" or "byte" or
            "System.SByte" or "sbyte" or "System.Int16" or "short" or
            "System.UInt16" or "ushort" or "System.Int32" or "int" or "Int32" or
            "System.UInt32" or "uint" or "System.Int64" or "long" or
            "System.UInt64" or "ulong" or "System.Single" or "float" or
            "System.Double" or "double" or "System.Char" or "char" or
            "System.IntPtr" or "nint" or "System.UIntPtr" or "nuint";

        /// <summary>
        /// Check if a structured block ends with a throw or return (no fall-through).
        /// Used to detect guard clauses and suppress unnecessary else wrappers.
        /// </summary>
        bool BlockEndsWithNoFallthrough(StructuredBlock? block)
        {
            if (block is null) return false;

            int idx = block.BlockIndex;
            if (idx >= 0 && _blockMap.TryGetValue(idx, out var astBlock))
            {
                var lastNode = astBlock.Nodes.LastOrDefault();
                if (lastNode is ILAstStatement stmt)
                {
                    return stmt.Expression.OpCode is ILOpCode.Throw or ILOpCode.Rethrow
                        or ILOpCode.Ret;
                }
            }

            // Check children recursively (e.g., sequence ending with throw)
            if (block.Children.Count > 0)
                return BlockEndsWithNoFallthrough(block.Children[^1]);

            return false;
        }

        static bool NeedsParentheses(ILAstExpression expr) => expr.OpCode switch
        {
            ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or
            ILOpCode.Rem or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
            ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Ceq or ILOpCode.Cgt or
            ILOpCode.Clt => true,
            _ => false
        };

        string RemapArg(string? operand, ILOpCode opcode)
        {
            if (_hasThis && operand is not null && operand.StartsWith("P_")
                && int.TryParse(operand.AsSpan(2), out int idx))
            {
                if (idx == 0) return "this";
                return $"P_{idx - 1}";
            }
            return operand ?? GetArgName(opcode, _hasThis);
        }

        static string GetArgName(ILOpCode opcode, bool hasThis = false)
        {
            int idx = opcode switch
            {
                ILOpCode.Ldarg_0 => 0,
                ILOpCode.Ldarg_1 => 1,
                ILOpCode.Ldarg_2 => 2,
                ILOpCode.Ldarg_3 => 3,
                _ => -1
            };
            if (hasThis)
            {
                if (idx == 0) return "this";
                idx--;
            }
            return idx >= 0 ? $"P_{idx}" : "arg";
        }

        static string GetLocalName(ILOpCode opcode) => opcode switch
        {
            ILOpCode.Ldloc_0 or ILOpCode.Stloc_0 => "V_0",
            ILOpCode.Ldloc_1 or ILOpCode.Stloc_1 => "V_1",
            ILOpCode.Ldloc_2 or ILOpCode.Stloc_2 => "V_2",
            ILOpCode.Ldloc_3 or ILOpCode.Stloc_3 => "V_3",
            _ => "loc"
        };

        static string ExtractTypeName(string? qualifiedName)
        {
            if (qualifiedName is null) return "object";
            int colonIdx = qualifiedName.IndexOf("::", StringComparison.Ordinal);
            return colonIdx >= 0 ? qualifiedName[..colonIdx] : qualifiedName;
        }

        /// <summary>
        /// If the first handler statement is <c>stloc V_X = S_in_0</c>, returns the variable name
        /// and marks the statement for suppression. This allows <c>catch (ExType V_X)</c> syntax.
        /// </summary>
        string? TryExtractCatchVariable(StructuredBlock block)
        {
            if (block.HandlerChildren.Count == 0) return null;
            var firstChild = block.HandlerChildren[0];
            if (firstChild.BlockIndex < 0 || !_blockMap.TryGetValue(firstChild.BlockIndex, out var astBlock))
                return null;
            if (astBlock.Nodes.Count == 0) return null;

            // Look for stloc.s/stloc with S_in_0 as the value
            if (astBlock.Nodes[0] is ILAstStatement { Expression: var expr }
                && expr.OpCode is ILOpCode.Stloc_s or ILOpCode.Stloc
                    or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
                && expr.Arguments.Count == 1
                && expr.Arguments[0].Operand is string op && op.StartsWith("S_in_", StringComparison.Ordinal))
            {
                _catchVariableStatements.Add((firstChild.BlockIndex, 0));
                return expr.Operand;
            }

            return null;
        }

        static string ExtractMemberName(string? qualifiedName)
        {
            if (qualifiedName is null) return "member";
            int colonIdx = qualifiedName.IndexOf("::", StringComparison.Ordinal);
            if (colonIdx >= 0)
            {
                string name = qualifiedName[(colonIdx + 2)..];
                return name.TrimEnd('(', ')');
            }
            return qualifiedName;
        }

        /// <summary>
        /// Simplify compiler-generated lambda method names like "&lt;ClosureCapture&gt;b__0"
        /// to readable form like "lambda: ClosureCapture".
        /// </summary>
        static string SimplifyLambdaName(string name)
        {
            // Extract the enclosing method name from "<>c__DisplayClass::<MethodName>b__N" 
            // or just "<MethodName>b__N"
            int lastOpen = name.LastIndexOf('<');
            int lastClose = name.IndexOf('>', lastOpen + 1);
            if (lastOpen >= 0 && lastClose > lastOpen + 1)
            {
                string methodName = name[(lastOpen + 1)..lastClose];
                return $"lambda: {methodName}";
            }
            return $"lambda";
        }

        static string SimplifyTypeName(string typeName)
        {
            // Compiler-generated closure types
            if (typeName.Contains("<>c__DisplayClass", StringComparison.Ordinal))
                return "/* closure */";
            if (typeName.Contains("<>c", StringComparison.Ordinal) && !typeName.Contains("__DisplayClass"))
                return "/* static closure */";

            return typeName switch
            {
                "System.Void" or "void" => "void",
                "System.Boolean" or "bool" => "bool",
                "System.Byte" or "byte" => "byte",
                "System.SByte" or "sbyte" => "sbyte",
                "System.Int16" or "short" => "short",
                "System.UInt16" or "ushort" => "ushort",
                "System.Int32" or "int" => "int",
                "System.UInt32" or "uint" => "uint",
                "System.Int64" or "long" => "long",
                "System.UInt64" or "ulong" => "ulong",
                "System.Single" or "float" => "float",
                "System.Double" or "double" => "double",
                "System.Decimal" or "decimal" => "decimal",
                "System.Char" or "char" => "char",
                "System.String" or "string" => "string",
                "System.Object" or "object" => "object",
                "System.IntPtr" or "nint" => "nint",
                "System.UIntPtr" or "nuint" => "nuint",
                _ => typeName
            };
        }
    }

    record InterpolationPart(bool IsLiteral, string? LiteralText, ILAstExpression? FormatExpression);
}
