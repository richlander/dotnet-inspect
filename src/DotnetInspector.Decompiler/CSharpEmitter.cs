using System.Reflection.Metadata;
using System.Text;

using DotnetInspector.Metadata;

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
        var inliner = new ExpressionInliner();
        var inlinedLocals = inliner.Inline(ast);
        var structure = StructuredControlFlow.Analyze(context, cfg);
        return Emit(ast, structure, context.Reader, context.HasThis, context.ReturnType, context.ParameterNames, inlinedLocals);
    }

    /// <summary>
    /// Emit C# source from pre-computed ILAst and control flow structure.
    /// </summary>
    public static string Emit(ILAstMethod ast, StructuredControlFlow structure, MetadataReader? reader = null, bool hasThis = false, string? returnType = null, IReadOnlyList<string>? parameterNames = null, HashSet<string>? inlinedLocals = null)
    {
        var sb = new StringBuilder();
        var emitter = new EmitterContext(ast, structure, sb, reader, hasThis, returnType, parameterNames, inlinedLocals);
        emitter.EmitMethod();
        return sb.ToString();
    }

    sealed class EmitterContext
    {
        static readonly HashSet<string> s_knownExtensionMethodTypes =
        [
            "Enumerable", "Queryable",
        ];

        readonly ILAstMethod _ast;
        readonly StructuredControlFlow _structure;
        readonly StringBuilder _sb;
        readonly MetadataReader? _reader;
        readonly bool _hasThis;
        readonly bool _returnsBool;
        readonly string? _returnTypeName;
        readonly IReadOnlyList<string>? _paramNames;

        // Map block index → ILAstBlock for quick lookup
        readonly Dictionary<int, ILAstBlock> _blockMap;

        // Blocks consumed by structured constructs (don't emit separately)
        readonly HashSet<int> _consumedBlocks;

        // Current block nodes being emitted (for dup resolution)
        List<ILAstNode>? _currentBlockNodes;
        int _currentBlockIndex = -1;

        // Tracks the current ret argument being emitted (for bool context inference)
        ILAstExpression? _currentReturnArg;

        // Set when emitting an expression in a boolean context (stloc to bool local, etc.)
        bool _emitBoolContext;

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

        // Local variables whose type is bool (for true/false literal emission)
        readonly HashSet<string> _boolLocals;

        // Return blocks whose gotos have been inlined — suppress the block itself
        readonly HashSet<string> _inlinedReturnLabels;

        // Map block index → IL start offset (for emitting labels)
        readonly Dictionary<int, int> _blockStartOffset;

        // IL offset labels of loop headers — gotos to these are suppressed (replaced by while)
        readonly HashSet<string> _loopHeaderLabels;

        // IL offset labels consumed by while-loop conditions (body entry points from header branch)
        readonly HashSet<string> _loopConsumedLabels;

        // Synthetic variable substitutions (e.g., S_in_0 → "x > 0 && y > 0")
        readonly Dictionary<string, string> _syntheticSubstitutions = [];

        // Array initializer elements: newarr IL offset → collected element values
        readonly Dictionary<int, SortedDictionary<int, ILAstExpression>> _arrayInitValues = [];

        // Collection construction temporaries: newobj IL offset → synthesized local name.
        readonly Dictionary<int, string> _collectionTemps = [];
        int _nextCollectionTemp;

        // Inline-array collection expression temporaries: local name → element index/value map.
        readonly Dictionary<string, SortedDictionary<int, ILAstExpression>> _inlineArrayInitValues = [];

        // Variables inlined by ExpressionInliner (suppress declarations)
        readonly HashSet<string> _inlinedLocals;

        // Item 2: Locals whose declaration is merged with first assignment
        // Maps variable name → init expression string
        readonly Dictionary<string, string> _mergedLocals = [];

        // Try/finally regions that are compiler-lowered lock statements.
        readonly Dictionary<StructuredBlock, LockPattern> _lockPatterns = [];

        // Runtime-async custom awaits lower through an awaiter temp:
        // awaitable.GetAwaiter(); if (!awaiter.IsCompleted) AsyncHelpers.AwaitAwaiter(awaiter); awaiter.GetResult().
        readonly Dictionary<string, ILAstExpression> _runtimeCustomAwaitSources = [];

        public EmitterContext(ILAstMethod ast, StructuredControlFlow structure, StringBuilder sb, MetadataReader? reader = null, bool hasThis = false, string? returnType = null, IReadOnlyList<string>? parameterNames = null, HashSet<string>? inlinedLocals = null)
        {
            _ast = ast;
            _structure = structure;
            _sb = sb;
            _reader = reader;
            _hasThis = hasThis;
            _returnsBool = returnType is "bool" or "System.Boolean";
            _returnTypeName = returnType;
            _paramNames = parameterNames;
            _inlinedLocals = inlinedLocals ?? [];

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

            // Build set of bool-typed locals for true/false literal emission
            _boolLocals = [];
            foreach (var local in ast.Locals)
                if (local.TypeName is "bool" or "System.Boolean" or "Boolean")
                    _boolLocals.Add(local.Name);

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

            // C# collection expressions targeting spans can lower through compiler-generated
            // inline-array helpers. Scan early so their helper locals aren't declared.
            ScanInlineArrayInitializers(ast);

            // Pre-detect lock patterns so compiler-generated temporaries are suppressed before
            // their initialization blocks are emitted.
            ScanForLockPatterns(structure.Root);

            // Pre-detect runtime-async custom awaiter patterns so awaiter temps and await
            // scheduling guards can be rendered as source-level await expressions.
            ScanForRuntimeCustomAwaitPatterns(structure.Root);

            // Pre-detect using patterns to suppress local declarations
            ScanForUsingPatterns(structure.Root);

            // Pre-detect exception-filter bool temps (declarations print before
            // the filter is rendered as a when clause).
            ScanForFilterLocals(structure.Root);
        }

        sealed class LockPattern
        {
            public required ILAstExpression LockExpression { get; init; }
            public List<ILAstNode> SkipNodes { get; } = [];
            public List<string> SuppressedLocals { get; } = [];
        }

        sealed class InlineArrayInitializerCandidate
        {
            public SortedDictionary<int, ILAstExpression> Elements { get; } = [];
            public List<ILAstNode> StoreNodes { get; } = [];
            public List<ILAstNode> InitNodes { get; } = [];
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
                    // Skip nop instructions (Debug builds insert them between calls)
                    if (callExpr.OpCode == ILOpCode.Nop)
                    {
                        skipNodes.Add(block.Nodes[i]);
                        continue;
                    }
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

        void ScanForRuntimeCustomAwaitPatterns(StructuredBlock block)
        {
            if (block.Kind == StructuredBlockKind.IfThenElse
                && TryMatchRuntimeCustomAwaitGuard(block, out string? awaiterVar, out var awaitedExpression, out var storeNode))
            {
                _runtimeCustomAwaitSources[awaiterVar] = awaitedExpression;
                _skipNodes.Add(storeNode);
                _suppressedLocals.Add(awaiterVar);
            }

            foreach (var c in block.Children)
                ScanForRuntimeCustomAwaitPatterns(c);
            foreach (var c in block.TryChildren)
                ScanForRuntimeCustomAwaitPatterns(c);
            foreach (var c in block.HandlerChildren)
                ScanForRuntimeCustomAwaitPatterns(c);
            if (block.ThenBlock is not null)
                ScanForRuntimeCustomAwaitPatterns(block.ThenBlock);
            if (block.ElseBlock is not null)
                ScanForRuntimeCustomAwaitPatterns(block.ElseBlock);
        }

        bool TryMatchRuntimeCustomAwaitGuard(
            StructuredBlock block,
            out string awaiterVar,
            out ILAstExpression awaitedExpression,
            out ILAstNode storeNode)
        {
            awaiterVar = "";
            awaitedExpression = null!;
            storeNode = null!;

            if (block.ElseBlock is not null || block.ConditionBlockIndex < 0)
                return false;
            if (!_blockMap.TryGetValue(block.ConditionBlockIndex, out var conditionBlock))
                return false;
            if (conditionBlock.Nodes.LastOrDefault() is not ILAstStatement { Expression: var branchExpr })
                return false;
            if (!TryMatchNotCompletedAwaitGuard(branchExpr, block.NegateCondition, out awaiterVar))
                return false;
            if (!BlockContainsSingleRuntimeAwaitHelper(block.ThenBlock, awaiterVar))
                return false;
            if (!TryFindAwaiterStore(awaiterVar, out storeNode, out awaitedExpression))
                return false;
            if (!HasGetResultUse(awaiterVar))
                return false;

            return true;
        }

        bool TryMatchNotCompletedAwaitGuard(ILAstExpression branchExpr, bool negateCondition, out string awaiterVar)
        {
            awaiterVar = "";
            if (branchExpr.Arguments.Count != 1)
                return false;
            if (!TryGetIsCompletedAwaiter(branchExpr.Arguments[0], out awaiterVar))
                return false;

            return branchExpr.OpCode switch
            {
                ILOpCode.Brtrue or ILOpCode.Brtrue_s => negateCondition,
                ILOpCode.Brfalse or ILOpCode.Brfalse_s => negateCondition,
                _ => false
            };
        }

        bool TryGetIsCompletedAwaiter(ILAstExpression expr, out string awaiterVar)
        {
            awaiterVar = "";

            if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && expr.Operand is string operand
                && ExtractMemberName(operand) == "get_IsCompleted"
                && expr.Arguments.Count > 0
                && GetLocalReferenceName(expr.Arguments[0]) is { } local)
            {
                awaiterVar = local;
                return true;
            }

            return false;
        }

        bool BlockContainsSingleRuntimeAwaitHelper(StructuredBlock? block, string awaiterVar)
        {
            if (block is null)
                return false;

            bool sawHelper = false;
            foreach (var (_, node) in EnumerateStructuredNodes(block))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (expr.OpCode is ILOpCode.Nop or ILOpCode.Br or ILOpCode.Br_s
                    or ILOpCode.Leave or ILOpCode.Leave_s)
                    continue;

                if (IsRuntimeAwaiterHelperCall(expr, awaiterVar))
                {
                    if (sawHelper)
                        return false;
                    sawHelper = true;
                    continue;
                }

                return false;
            }

            return sawHelper;
        }

        static bool IsRuntimeAwaiterHelperCall(ILAstExpression expr, string awaiterVar)
        {
            if (expr.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt)
                || expr.Operand is not string operand)
                return false;

            if (!operand.StartsWith("System.Runtime.CompilerServices.AsyncHelpers::", StringComparison.Ordinal))
                return false;

            string memberName = ExtractMemberName(operand);
            if (memberName is not ("AwaitAwaiter" or "UnsafeAwaitAwaiter"))
                return false;

            return expr.Arguments.Count == 1 && IsLoadOf(expr.Arguments[0], awaiterVar);
        }

        bool TryFindAwaiterStore(string awaiterVar, out ILAstNode storeNode, out ILAstExpression awaitedExpression)
        {
            storeNode = null!;
            awaitedExpression = null!;
            bool found = false;

            foreach (var block in _ast.Blocks)
            {
                foreach (var node in block.Nodes)
                {
                    if (node is not ILAstStatement { Expression: var expr }
                        || !IsStoreToLocal(expr, awaiterVar)
                        || expr.Arguments.Count != 1)
                    {
                        continue;
                    }

                    if (found)
                        return false;
                    if (!TryGetAwaitedExpressionFromGetAwaiter(expr.Arguments[0], out awaitedExpression))
                        return false;

                    storeNode = node;
                    found = true;
                }
            }

            return found;
        }

        static bool TryGetAwaitedExpressionFromGetAwaiter(ILAstExpression getAwaiterExpr, out ILAstExpression awaitedExpression)
        {
            awaitedExpression = null!;
            if (getAwaiterExpr.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt)
                || getAwaiterExpr.Operand is not string operand
                || ExtractMemberName(operand) != "GetAwaiter"
                || getAwaiterExpr.Arguments.Count == 0)
            {
                return false;
            }

            awaitedExpression = NormalizeAwaitedExpression(getAwaiterExpr.Arguments[0]);
            return true;
        }

        static ILAstExpression NormalizeAwaitedExpression(ILAstExpression receiver)
        {
            if (receiver.OpCode is ILOpCode.Ldarga or ILOpCode.Ldarga_s)
            {
                return new ILAstExpression
                {
                    OpCode = ILOpCode.Ldarg,
                    Operand = receiver.Operand,
                    ResultType = StackValue.CreateUnknown(),
                    Offset = receiver.Offset
                };
            }

            if (receiver.OpCode == ILOpCode.Ldelema && receiver.Arguments.Count >= 2)
            {
                var value = new ILAstExpression
                {
                    OpCode = ILOpCode.Ldelem,
                    Operand = receiver.Operand,
                    ResultType = StackValue.CreateUnknown(),
                    Offset = receiver.Offset
                };
                value.Arguments.AddRange(receiver.Arguments);
                return value;
            }

            return receiver;
        }

        bool HasGetResultUse(string awaiterVar)
        {
            foreach (var block in _ast.Blocks)
            {
                foreach (var node in block.Nodes)
                {
                    if (NodeExpression(node) is { } expr && HasGetResultUse(expr, awaiterVar))
                        return true;
                }
            }

            return false;
        }

        static bool HasGetResultUse(ILAstExpression expr, string awaiterVar)
        {
            if (IsGetResultCallOnAwaiter(expr, awaiterVar))
                return true;

            foreach (var arg in expr.Arguments)
            {
                if (HasGetResultUse(arg, awaiterVar))
                    return true;
            }

            return false;
        }

        static bool IsGetResultCallOnAwaiter(ILAstExpression expr, string awaiterVar)
            => expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && !expr.IsStaticCall
                && expr.Operand is string operand
                && ExtractMemberName(operand) == "GetResult"
                && expr.Arguments.Count > 0
                && IsLoadOf(expr.Arguments[0], awaiterVar);

        void ScanForLockPatterns(StructuredBlock block)
        {
            if (block.Kind == StructuredBlockKind.TryCatchFinally
                && block.ExceptionRegion is { Kind: ExceptionRegionKind.Finally }
                && TryDetectLockPattern(block, out var pattern))
            {
                _lockPatterns[block] = pattern;
                foreach (var node in pattern.SkipNodes)
                    _skipNodes.Add(node);
                foreach (string local in pattern.SuppressedLocals)
                    _suppressedLocals.Add(local);
            }

            foreach (var c in block.Children)
                ScanForLockPatterns(c);
            foreach (var c in block.TryChildren)
                ScanForLockPatterns(c);
            foreach (var c in block.HandlerChildren)
                ScanForLockPatterns(c);
            if (block.ThenBlock is not null)
                ScanForLockPatterns(block.ThenBlock);
            if (block.ElseBlock is not null)
                ScanForLockPatterns(block.ElseBlock);
        }

        bool TryDetectLockPattern(StructuredBlock block, out LockPattern pattern)
        {
            if (TryDetectSystemThreadingLockPattern(block, out pattern))
                return true;

            return TryDetectMonitorLockPattern(block, out pattern);
        }

        bool TryDetectSystemThreadingLockPattern(StructuredBlock block, out LockPattern pattern)
        {
            pattern = null!;

            if (!TryDetectDirectDisposeOnly(block, out string? scopeVar))
                return false;

            if (!TryFindPreviousStore(block, scopeVar, out var initNode, out var initExpression))
                return false;
            if (HasUnexpectedPreviousNodesAfterFirstExpected(block, allowedStoreLocal: null, initNode))
            {
                return false;
            }

            if (initExpression.Operand is not string initOperand
                || !initOperand.Contains("System.Threading.Lock::EnterScope", StringComparison.Ordinal)
                || initExpression.IsStaticCall
                || initExpression.Arguments.Count == 0)
            {
                return false;
            }

            pattern = new LockPattern { LockExpression = initExpression.Arguments[0] };
            pattern.SkipNodes.Add(initNode);
            pattern.SuppressedLocals.Add(scopeVar);
            return true;
        }

        bool TryDetectMonitorLockPattern(StructuredBlock block, out LockPattern pattern)
        {
            pattern = null!;

            ILAstNode? enterNode = null;
            ILAstExpression? enterExpression = null;
            foreach (var (_, node) in EnumerateStructuredNodes(block.TryChildren))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (IsIgnorableLockTryNode(expr))
                    continue;
                if (expr.Operand is string operand
                    && operand.Contains("System.Threading.Monitor::Enter", StringComparison.Ordinal)
                    && expr.IsStaticCall
                    && expr.Arguments.Count > 0)
                {
                    enterNode = node;
                    enterExpression = expr;
                    break;
                }

                return false;
            }

            if (enterNode is null || enterExpression is null)
                return false;

            string? lockVar = GetLocalReferenceName(enterExpression.Arguments[0]);
            if (lockVar is null)
                return false;

            if (!TryFindPreviousStore(block, lockVar, out var initNode, out var lockExpression))
                return false;

            string? lockTakenVar = enterExpression.Arguments.Count > 1
                ? GetLocalReferenceName(enterExpression.Arguments[1]) ?? enterExpression.Arguments[1].Operand
                : null;
            if (!HandlerMatchesMonitorExitOnly(block, lockVar, lockTakenVar))
                return false;

            ILAstNode? lockTakenInitNode = null;
            if (lockTakenVar is not null)
                TryFindPreviousStore(block, lockTakenVar, out lockTakenInitNode, out _);

            if (HasUnexpectedPreviousNodesAfterFirstExpected(block, lockTakenVar, initNode, lockTakenInitNode))
                return false;

            pattern = new LockPattern { LockExpression = lockExpression };
            pattern.SkipNodes.Add(enterNode);
            pattern.SkipNodes.Add(initNode);
            pattern.SuppressedLocals.Add(lockVar);

            if (lockTakenVar is not null)
            {
                pattern.SuppressedLocals.Add(lockTakenVar);
                if (lockTakenInitNode is not null)
                    pattern.SkipNodes.Add(lockTakenInitNode);
            }

            return true;
        }

        static bool IsIgnorableLockTryNode(ILAstExpression expr)
            => expr.OpCode == ILOpCode.Nop;

        bool HasUnexpectedPreviousNodesAfterFirstExpected(
            StructuredBlock block,
            string? allowedStoreLocal,
            params ILAstNode?[] expectedNodes)
        {
            if (!TryGetPreviousSiblingNodes(block, out var nodes))
                return true;

            var expected = expectedNodes.OfType<ILAstNode>().ToHashSet();
            if (expected.Count == 0 || !expected.All(nodes.Contains))
                return true;

            int firstExpectedIndex = nodes.FindIndex(expected.Contains);
            for (int i = firstExpectedIndex; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (expected.Contains(node))
                    continue;
                if (NodeExpression(node) is not { } expr || IsIgnorableLockTryNode(expr))
                    continue;
                if (allowedStoreLocal is not null && IsStoreToLocal(expr, allowedStoreLocal))
                    continue;
                return true;
            }

            return false;
        }

        bool TryGetPreviousSiblingNodes(StructuredBlock block, out List<ILAstNode> nodes)
        {
            nodes = [];
            var parent = FindParentSequence(block);
            if (parent is null)
                return false;

            int blockIndex = -1;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (ReferenceEquals(parent.Children[i], block))
                {
                    blockIndex = i;
                    break;
                }
            }

            if (blockIndex <= 0)
                return false;

            for (int i = 0; i < blockIndex; i++)
                nodes.AddRange(EnumerateStructuredNodes(parent.Children[i]).Select(item => item.Node));
            return nodes.Count > 0;
        }

        bool HandlerMatchesMonitorExitOnly(StructuredBlock block, string lockVar, string? lockTakenVar)
        {
            if (lockTakenVar is not null)
                return HandlerMatchesGuardedMonitorExitOnly(block, lockVar, lockTakenVar);

            bool sawExit = false;
            foreach (var (_, node) in EnumerateStructuredNodes(block.HandlerChildren))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (expr.OpCode is ILOpCode.Nop or ILOpCode.Endfinally)
                    continue;
                if (expr.Operand is string operand
                    && operand.Contains("System.Threading.Monitor::Exit", StringComparison.Ordinal)
                    && expr.IsStaticCall
                    && expr.Arguments.Count > 0
                    && IsLoadOf(expr.Arguments[0], lockVar))
                {
                    if (sawExit)
                        return false;
                    sawExit = true;
                    continue;
                }

                return false;
            }

            return sawExit;
        }

        bool HandlerMatchesGuardedMonitorExitOnly(StructuredBlock block, string lockVar, string lockTakenVar)
        {
            if (HandlerMatchesStructuredGuardedMonitorExitOnly(block, lockVar, lockTakenVar))
                return true;

            bool sawGuard = false;
            bool sawExit = false;

            foreach (var (_, node) in EnumerateStructuredNodes(block.HandlerChildren))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (expr.OpCode == ILOpCode.Nop)
                    continue;
                if (expr.OpCode == ILOpCode.Endfinally && sawExit)
                    continue;

                if (!sawGuard
                    && (expr.OpCode is ILOpCode.Brfalse or ILOpCode.Brfalse_s
                        or ILOpCode.Brtrue or ILOpCode.Brtrue_s)
                    && expr.Arguments.Count == 1
                    && IsLoadOf(expr.Arguments[0], lockTakenVar))
                {
                    sawGuard = true;
                    continue;
                }

                if (sawGuard
                    && !sawExit
                    && expr.Operand is string operand
                    && operand.Contains("System.Threading.Monitor::Exit", StringComparison.Ordinal)
                    && expr.IsStaticCall
                    && expr.Arguments.Count > 0
                    && IsLoadOf(expr.Arguments[0], lockVar))
                {
                    sawExit = true;
                    continue;
                }

                return false;
            }

            return sawGuard && sawExit;
        }

        bool HandlerMatchesStructuredGuardedMonitorExitOnly(StructuredBlock block, string lockVar, string lockTakenVar)
        {
            bool sawGuard = false;
            foreach (var child in block.HandlerChildren)
            {
                if (child.Kind == StructuredBlockKind.IfThenElse)
                {
                    if (sawGuard
                        || child.ElseBlock is not null
                        || !BlockContainsSingleMonitorExit(child.ThenBlock, lockVar))
                    {
                        return false;
                    }

                    sawGuard = true;
                    continue;
                }

                if (!BlockContainsOnlyIgnorableFinallyNodes(child))
                    return false;
            }

            return sawGuard;
        }

        bool BlockContainsSingleMonitorExit(StructuredBlock? block, string lockVar)
        {
            if (block is null)
                return false;

            bool sawExit = false;
            foreach (var (_, node) in EnumerateStructuredNodes(block))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (expr.OpCode == ILOpCode.Nop)
                    continue;
                if (expr.Operand is string operand
                    && operand.Contains("System.Threading.Monitor::Exit", StringComparison.Ordinal)
                    && expr.IsStaticCall
                    && expr.Arguments.Count > 0
                    && IsLoadOf(expr.Arguments[0], lockVar))
                {
                    if (sawExit)
                        return false;
                    sawExit = true;
                    continue;
                }

                return false;
            }

            return sawExit;
        }

        bool BlockContainsOnlyIgnorableFinallyNodes(StructuredBlock block)
        {
            foreach (var (_, node) in EnumerateStructuredNodes(block))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (expr.OpCode is ILOpCode.Nop or ILOpCode.Endfinally)
                    continue;
                return false;
            }

            return true;
        }

        bool TryDetectDirectDisposeOnly(StructuredBlock block, out string disposeVar)
        {
            disposeVar = "";
            bool sawDispose = false;

            foreach (var (_, node) in EnumerateStructuredNodes(block.HandlerChildren))
            {
                var expr = NodeExpression(node);
                if (expr is null)
                    continue;
                if (expr.OpCode is ILOpCode.Nop or ILOpCode.Endfinally)
                    continue;

                if (!sawDispose
                    && expr.Operand is string operand
                    && operand.Contains("Dispose", StringComparison.Ordinal)
                    && !expr.IsStaticCall
                    && expr.Arguments.Count > 0)
                {
                    disposeVar = RenderReceiverName(expr.Arguments[0]);
                    sawDispose = true;
                    continue;
                }

                return false;
            }

            return sawDispose;
        }

        IEnumerable<(int BlockIndex, ILAstNode Node)> EnumerateStructuredNodes(IEnumerable<StructuredBlock> blocks)
        {
            foreach (var block in blocks)
            {
                foreach (var item in EnumerateStructuredNodes(block))
                    yield return item;
            }
        }

        IEnumerable<(int BlockIndex, ILAstNode Node)> EnumerateStructuredNodes(StructuredBlock block)
        {
            if (block.BlockIndex >= 0 && _blockMap.TryGetValue(block.BlockIndex, out var astBlock))
            {
                foreach (var node in astBlock.Nodes)
                    yield return (block.BlockIndex, node);
            }

            foreach (var c in block.Children)
                foreach (var item in EnumerateStructuredNodes(c))
                    yield return item;
            foreach (var c in block.TryChildren)
                foreach (var item in EnumerateStructuredNodes(c))
                    yield return item;
            foreach (var c in block.HandlerChildren)
                foreach (var item in EnumerateStructuredNodes(c))
                    yield return item;
            if (block.ThenBlock is not null)
                foreach (var item in EnumerateStructuredNodes(block.ThenBlock))
                    yield return item;
            if (block.ElseBlock is not null)
                foreach (var item in EnumerateStructuredNodes(block.ElseBlock))
                    yield return item;
        }

        static ILAstExpression? NodeExpression(ILAstNode node) => node switch
        {
            ILAstStatement { Expression: var expr } => expr,
            ILAstAssignment { Value: var value } => value,
            _ => null
        };

        bool TryFindPreviousStore(
            StructuredBlock block,
            string varName,
            out ILAstNode storeNode,
            out ILAstExpression value)
        {
            storeNode = null!;
            value = null!;

            var parent = FindParentSequence(block);
            if (parent is null)
                return false;

            int blockIndex = -1;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (ReferenceEquals(parent.Children[i], block))
                {
                    blockIndex = i;
                    break;
                }
            }

            if (blockIndex < 0)
                return false;

            for (int i = blockIndex - 1; i >= 0; i--)
            {
                foreach (var (_, node) in EnumerateStructuredNodes(parent.Children[i]).Reverse())
                {
                    if (node is ILAstAssignment assign
                        && assign.Variable.Name == varName)
                    {
                        storeNode = node;
                        value = assign.Value;
                        return true;
                    }

                    if (node is ILAstStatement { Expression: var expr }
                        && IsStoreToLocal(expr, varName)
                        && expr.Arguments.Count > 0)
                    {
                        storeNode = node;
                        value = expr.Arguments[0];
                        return true;
                    }
                }
            }

            return false;
        }

        static bool IsStoreToLocal(ILAstExpression expr, string varName)
        {
            if (expr.OpCode is not (ILOpCode.Stloc or ILOpCode.Stloc_s
                or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3))
                return false;

            string name = expr.Operand ?? GetLocalName(expr.OpCode);
            return name == varName;
        }

        static string? GetLocalReferenceName(ILAstExpression expr)
        {
            if (expr.Operand is { } operand
                && expr.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloc_s
                    or ILOpCode.Ldloca or ILOpCode.Ldloca_s
                    or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3)
            {
                return operand;
            }

            return expr.OpCode switch
            {
                ILOpCode.Ldloc_0 => "V_0",
                ILOpCode.Ldloc_1 => "V_1",
                ILOpCode.Ldloc_2 => "V_2",
                ILOpCode.Ldloc_3 => "V_3",
                _ => null
            };
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

        /// <summary>
        /// Item 2: Find locals whose first use is a store in the entry block (block 0),
        /// with no prior load. These can be declared inline: type name = expr;
        /// </summary>
        void DetectMergedLocals()
        {
            if (_ast.Blocks.Count == 0) return;

            var entryBlock = _ast.Blocks[0];
            var usedBefore = new HashSet<string>();

            foreach (var node in entryBlock.Nodes)
            {
                if (node is ILAstStatement { Expression: var expr })
                {
                    // Check for stloc to a local that hasn't been loaded yet
                    if (expr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                            or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc
                        && expr.Arguments.Count > 0)
                    {
                        string varName = expr.Operand ?? GetLocalName(expr.OpCode);
                        if (!usedBefore.Contains(varName)
                            && _ast.Locals.Any(l => l.Name == varName)
                            && !_interpolationParts.ContainsKey(varName)
                            && !_suppressedLocals.Contains(varName)
                            && !_inlinedLocals.Contains(varName)
                            && !HasSideEffects(expr.Arguments[0]))
                        {
                            _mergedLocals[varName] = varName; // marker
                        }
                    }

                    // Track all loads in this expression
                    CollectLoadedLocals(expr, usedBefore);
                }
                else if (node is ILAstAssignment assign)
                {
                    string varName = assign.Variable.Name;
                    if (!usedBefore.Contains(varName)
                        && _ast.Locals.Any(l => l.Name == varName)
                        && !_interpolationParts.ContainsKey(varName)
                        && !_suppressedLocals.Contains(varName)
                        && !_inlinedLocals.Contains(varName)
                        && !HasSideEffects(assign.Value))
                    {
                        _mergedLocals[varName] = varName;
                    }
                    CollectLoadedLocals(assign.Value, usedBefore);
                }
            }
        }

        static void CollectLoadedLocals(ILAstExpression expr, HashSet<string> locals)
        {
            if (expr.OpCode is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
                    or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc)
            {
                string name = expr.Operand ?? GetLocalName(expr.OpCode);
                locals.Add(name);
            }
            foreach (var arg in expr.Arguments)
                CollectLoadedLocals(arg, locals);
        }

        /// <summary>
        /// Returns true for expressions that should block merged-local declarations.
        /// Call/Callvirt/Newobj are allowed because the merge doesn't reorder anything —
        /// it just combines "type x; x = expr;" into "type x = expr;" at the same position.
        /// </summary>
        static bool HasSideEffects(ILAstExpression expr)
        {
            if (expr.OpCode is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            foreach (var arg in expr.Arguments)
                if (HasSideEffects(arg))
                    return true;
            return false;
        }

        public void EmitMethod()
        {
            // Item 2: Pre-scan for locals whose first use is a store in the entry block
            DetectMergedLocals();

            // Emit local variable declarations
            if (_ast.Locals.Count > 0)
            {
                bool anyEmitted = false;
                foreach (var local in _ast.Locals)
                {
                    // Skip variables consumed by interpolated string or using detection
                    if (_interpolationParts.ContainsKey(local.Name))
                        continue;
                    if (_suppressedLocals.Contains(local.Name))
                        continue;
                    if (_inlinedLocals.Contains(local.Name))
                        continue;
                    // Skip locals that will be declared inline with their first assignment
                    if (_mergedLocals.ContainsKey(local.Name))
                        continue;
                    string typeName = SimplifyTypeName(local.TypeName ?? "var");
                    // Skip compiler-generated closure variable declarations
                    if (typeName.Contains("/* closure */", StringComparison.Ordinal))
                        continue;
                    _sb.AppendLine($"{typeName} {local.Name};");
                    anyEmitted = true;
                }
                if (anyEmitted)
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

            // Skip blocks already consumed by structured constructs
            if (_consumedBlocks.Contains(blockIndex))
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
            _currentBlockIndex = blockIndex;
            _currentBlockNodes = astBlock.Nodes;

            // Pre-scan for array initializer patterns (stelem + dup + newarr)
            ScanArrayInitializers(astBlock.Nodes);

            // Emit IL offset label if this block is a goto target
            TryEmitLabel(blockIndex);

            for (int nodeIdx = 0; nodeIdx < astBlock.Nodes.Count; nodeIdx++)
            {
                if (_catchVariableStatements.Contains((blockIndex, nodeIdx)))
                    continue;
                if (_skipNodes.Contains(astBlock.Nodes[nodeIdx]))
                    continue;

                var node = astBlock.Nodes[nodeIdx];

                // Peephole: stloc V_x = expr; ret(ldloc V_x) → return expr;
                if (TryEmitStoreReturn(astBlock, nodeIdx, indent))
                {
                    nodeIdx++; // skip the ret node
                    continue;
                }

                switch (node)
                {
                    case ILAstAssignment assign:
                        if (TryEmitCompoundAssignment(assign.Variable.Name, assign.Value, indent))
                            break;
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
            // A consumed condition block means the construct was already rendered
            // by a recognized pattern (e.g. an exception filter's blocks become a
            // when clause) — emitting it again would leak the lowered form.
            if (block.ConditionBlockIndex >= 0 && _consumedBlocks.Contains(block.ConditionBlockIndex))
            {
                if (block.ThenBlock is not null)
                    ConsumeStructuredBlock(block.ThenBlock);
                if (block.ElseBlock is not null)
                    ConsumeStructuredBlock(block.ElseBlock);
                return;
            }

            // Skip constant-condition branches (Debug stepping markers like brtrue [ldc.i4.1])
            if (block.ConditionBlockIndex >= 0 && _blockMap.TryGetValue(block.ConditionBlockIndex, out var earlyCondBlock))
            {
                var lastNode = earlyCondBlock.Nodes.LastOrDefault();
                if (lastNode is ILAstStatement { Expression: var earlyExpr } && IsConstantBranch(earlyExpr))
                {
                    // Determine if branch is always taken or never taken
                    bool constVal = IsNonZeroConstant(earlyExpr.Arguments[0]);
                    bool branchTaken = earlyExpr.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s
                        ? constVal : !constVal;

                    if (branchTaken)
                    {
                        // Always branches to then — emit then body as flat code
                        if (block.ThenBlock is not null)
                            EmitStructuredBlock(block.ThenBlock, indent);
                    }
                    else
                    {
                        // Never branches — emit else body (fallthrough) as flat code
                        if (block.ElseBlock is not null)
                            EmitStructuredBlock(block.ElseBlock, indent);
                    }
                    return;
                }
            }

            // The condition block's last expression is the branch condition
            string condition = "/* condition */";
            ILAstExpression? branchExpression = null;
            if (block.ConditionBlockIndex >= 0 && _blockMap.TryGetValue(block.ConditionBlockIndex, out var condBlock))
            {
                _currentBlockNodes = condBlock.Nodes;

                // Early detect null-coalescing: brtrue(dup(value)) + S_0 assignment in cond block.
                // Must check before emitting cond block statements, since the brtrue isn't the last
                // node (dup leaves a post-branch assignment) and would be emitted as a goto.
                if (block.NegateCondition && block.ThenBlock is not null
                    && TryEmitNullCoalescing(block, indent))
                {
                    return;
                }

                // Emit IL label if this condition block is a goto target
                TryEmitLabel(block.ConditionBlockIndex);

                // Emit any statements before the branch
                for (int i = 0; i < condBlock.Nodes.Count - 1; i++)
                {
                    if (_skipNodes.Contains(condBlock.Nodes[i]))
                        continue;

                    if (condBlock.Nodes[i] is ILAstStatement stmt)
                        EmitStatement(stmt.Expression, indent);
                    else if (condBlock.Nodes[i] is ILAstAssignment assign)
                    {
                        if (!TryEmitCompoundAssignment(assign.Variable.Name, assign.Value, indent))
                        {
                            WriteIndent(indent);
                            _sb.Append($"{assign.Variable.Name} = ");
                            EmitExpression(assign.Value);
                            _sb.AppendLine(";");
                        }
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
                    if (block.NegateCondition)
                        condition = NegateConditionString(condition);

                    string? shortCircuit = TryBuildShortCircuit(condition, thenValue, elseValue);
                    string resultExpr = shortCircuit
                        ?? $"{condition} ? {ExpressionToString(thenValue)} : {ExpressionToString(elseValue)}";

                    _syntheticSubstitutions["S_in_0"] = resultExpr;

                    if (block.ThenBlock.BlockIndex >= 0)
                    {
                        _consumedBlocks.Add(block.ThenBlock.BlockIndex);
                        RemoveGotoTargetsForConsumedBlock(block.ThenBlock.BlockIndex);
                    }
                    if (block.ElseBlock.BlockIndex >= 0)
                    {
                        _consumedBlocks.Add(block.ElseBlock.BlockIndex);
                        RemoveGotoTargetsForConsumedBlock(block.ElseBlock.BlockIndex);
                    }
                    return;
                }
            }

            // Detect ternary/short-circuit with no else but follow block assigns S_0
            if (block.ThenBlock is not null && block.ElseBlock is null)
            {
                var thenValue = TryExtractTernaryValue(block.ThenBlock);
                var followValue = TryExtractFollowTernaryValue(block);
                if (thenValue is not null && followValue.expr is not null)
                {
                    if (block.NegateCondition)
                        condition = NegateConditionString(condition);

                    string? shortCircuit = TryBuildShortCircuit(condition, thenValue, followValue.expr);
                    string resultExpr = shortCircuit
                        ?? $"{condition} ? {ExpressionToString(thenValue)} : {ExpressionToString(followValue.expr)}";

                    _syntheticSubstitutions["S_in_0"] = resultExpr;

                    if (block.ThenBlock.BlockIndex >= 0)
                    {
                        _consumedBlocks.Add(block.ThenBlock.BlockIndex);
                        RemoveGotoTargetsForConsumedBlock(block.ThenBlock.BlockIndex);
                    }
                    if (followValue.blockIdx >= 0)
                    {
                        _consumedBlocks.Add(followValue.blockIdx);
                        RemoveGotoTargetsForConsumedBlock(followValue.blockIdx);
                    }
                    return;
                }
            }

            // Apply negation if the conditional detector swapped then/else
            if (block.NegateCondition)
                condition = NegateConditionString(condition);

            if (TryEmitRuntimeCustomAwaitGuard(block))
                return;

            if (TryEmitNullConditionalAssignment(block, condition, indent))
                return;

            // Item 1: Short-circuit return: if(cond) return expr; return false/true;
            if (TryEmitShortCircuitReturn(block, condition, indent))
                return;

            // Item 6: Ternary return: if(cond) return e1; return e2;
            if (TryEmitTernaryReturn(block, condition, indent))
                return;

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

        bool TryEmitRuntimeCustomAwaitGuard(StructuredBlock block)
        {
            if (!TryMatchRuntimeCustomAwaitGuard(block, out string awaiterVar, out _, out _))
                return false;
            if (!_runtimeCustomAwaitSources.ContainsKey(awaiterVar))
                return false;

            if (block.ThenBlock is not null)
                ConsumeStructuredBlock(block.ThenBlock);
            if (block.ElseBlock is not null)
                ConsumeStructuredBlock(block.ElseBlock);

            return true;
        }

        bool TryEmitNullConditionalAssignment(StructuredBlock block, string condition, int indent)
        {
            if (block.ElseBlock is not null && !IsTerminalNullPath(block, block.ElseBlock))
                return false;
            if (!TryParseNotNullCondition(condition, out string? receiver))
                return false;
            if (receiver is null)
                return false;
            if (!TryExtractNullConditionalAssignment(
                    block.ThenBlock,
                    receiver,
                    out string? targetSuffix,
                    out string? assignmentOperator,
                    out ILAstExpression? value,
                    out string? expectedType,
                    out int consumedBlockIndex))
            {
                return false;
            }
            if (targetSuffix is null || assignmentOperator is null || value is null)
                return false;

            WriteIndent(indent);
            _sb.Append(receiver);
            _sb.Append(targetSuffix);
            _sb.Append(' ');
            _sb.Append(assignmentOperator);
            _sb.Append(' ');
            EmitExpression(value, expectedType);
            _sb.AppendLine(";");

            if (consumedBlockIndex >= 0)
            {
                _consumedBlocks.Add(consumedBlockIndex);
                RemoveGotoTargetsForConsumedBlock(consumedBlockIndex);
            }
            if (block.ElseBlock is not null)
                ConsumeStructuredBlock(block.ElseBlock);

            return true;
        }

        static bool TryParseNotNullCondition(string condition, out string? receiver)
        {
            receiver = null;
            const string suffix = " != null";
            if (!condition.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            receiver = condition[..^suffix.Length].Trim();
            return receiver.Length > 0;
        }

        bool TryExtractNullConditionalAssignment(
            StructuredBlock? block,
            string receiver,
            out string? targetSuffix,
            out string? assignmentOperator,
            out ILAstExpression? value,
            out string? expectedType,
            out int consumedBlockIndex)
        {
            targetSuffix = null;
            assignmentOperator = null;
            value = null;
            expectedType = null;
            consumedBlockIndex = -1;

            if (block is null)
                return false;

            int blockIdx = block.BlockIndex;
            if (block.Kind == StructuredBlockKind.Sequence && block.Children.Count == 1)
                blockIdx = block.Children[0].BlockIndex;
            if (blockIdx < 0 || !_blockMap.TryGetValue(blockIdx, out var astBlock))
                return false;

            var receiverAliases = new HashSet<string> { receiver };
            var consumedReceiverAliases = new HashSet<string>();
            foreach (var node in astBlock.Nodes)
            {
                if (node is ILAstStatement { Expression: var expr })
                {
                    if (IsIgnorableNullConditionalAssignmentNode(expr))
                        continue;

                    if (TryAddReceiverAlias(expr, receiverAliases, out string? receiverAlias))
                    {
                        consumedReceiverAliases.Add(receiverAlias);
                        continue;
                    }

                    if (value is not null)
                        return false;
                    if (!TryFormatNullConditionalAssignmentTarget(
                            expr,
                            receiverAliases,
                            out targetSuffix,
                            out assignmentOperator,
                            out value,
                            out expectedType))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (value is null)
                return false;
            if (consumedReceiverAliases.Count > 0
                && (ReferencesAnyLocal(value, consumedReceiverAliases)
                    || (targetSuffix is not null && ContainsAnyAliasText(targetSuffix, consumedReceiverAliases))
                    || AliasUsedOutsideBlock(blockIdx, consumedReceiverAliases)))
            {
                return false;
            }

            consumedBlockIndex = blockIdx;
            return true;
        }

        bool IsTerminalNullPath(StructuredBlock ifBlock, StructuredBlock elseBlock)
        {
            return TryGetTerminalNullPathTargetIndex(elseBlock, out int terminalBlockIndex)
                && HasNoMeaningfulFollowingSiblings(ifBlock, terminalBlockIndex);
        }

        bool TryGetTerminalNullPathTargetIndex(StructuredBlock block, out int terminalBlockIndex)
        {
            terminalBlockIndex = -1;
            if (!TryGetSingleBlockIndex(block, out int blockIdx)
                || !_blockMap.TryGetValue(blockIdx, out var astBlock))
            {
                return false;
            }

            if (blockIdx == _ast.Blocks.Count - 1 && BlockIsVoidReturnOnly(astBlock))
            {
                terminalBlockIndex = blockIdx;
                return true;
            }

            if (BlockBranchesOnlyToTerminalReturn(astBlock, out int targetBlockIndex))
            {
                terminalBlockIndex = targetBlockIndex;
                return true;
            }

            return false;
        }

        bool TryGetSingleBlockIndex(StructuredBlock block, out int blockIdx)
        {
            blockIdx = block.BlockIndex;
            if (blockIdx >= 0)
                return true;

            if (block.Kind == StructuredBlockKind.Sequence && block.Children.Count == 1)
            {
                blockIdx = block.Children[0].BlockIndex;
                return blockIdx >= 0;
            }

            return false;
        }

        bool HasNoMeaningfulFollowingSiblings(StructuredBlock block, int terminalBlockIndex)
        {
            var parent = FindParentSequence(block);
            if (parent is null)
                return false;

            int blockIndex = -1;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (ReferenceEquals(parent.Children[i], block))
                {
                    blockIndex = i;
                    break;
                }
            }

            if (blockIndex < 0)
                return false;

            for (int i = blockIndex + 1; i < parent.Children.Count; i++)
            {
                var sibling = parent.Children[i];
                if (TryGetSingleBlockIndex(sibling, out int siblingBlockIndex)
                    && siblingBlockIndex == terminalBlockIndex
                    && _blockMap.TryGetValue(siblingBlockIndex, out var terminalBlock)
                    && BlockIsVoidReturnOnly(terminalBlock))
                {
                    continue;
                }

                if (!BlockContainsOnlyNops(sibling))
                    return false;
            }

            return true;
        }

        bool BlockContainsOnlyNops(StructuredBlock block)
        {
            foreach (var (_, node) in EnumerateStructuredNodes(block))
            {
                if (NodeExpression(node) is { OpCode: not ILOpCode.Nop })
                    return false;
            }

            return true;
        }

        bool BlockIsVoidReturnOnly(ILAstBlock astBlock)
        {
            bool sawReturn = false;
            foreach (var node in astBlock.Nodes)
            {
                if (node is not ILAstStatement { Expression: var expr })
                    return false;
                if (expr.OpCode == ILOpCode.Nop)
                    continue;
                if (expr.OpCode == ILOpCode.Ret && expr.Arguments.Count == 0)
                {
                    if (sawReturn)
                        return false;
                    sawReturn = true;
                    continue;
                }

                return false;
            }

            return sawReturn;
        }

        bool BlockBranchesOnlyToTerminalReturn(ILAstBlock astBlock, out int targetBlockIndex)
        {
            targetBlockIndex = -1;
            string? target = null;
            foreach (var node in astBlock.Nodes)
            {
                if (node is not ILAstStatement { Expression: var expr })
                    return false;
                if (expr.OpCode == ILOpCode.Nop)
                    continue;
                if (expr.OpCode is ILOpCode.Br or ILOpCode.Br_s
                        or ILOpCode.Leave or ILOpCode.Leave_s
                    && expr.Operand is string branchTarget)
                {
                    if (target is not null)
                        return false;
                    target = branchTarget;
                    continue;
                }

                return false;
            }

            if (target is null)
                return false;

            foreach (var (blockIdx, offset) in _blockStartOffset)
            {
                if ($"IL_{offset:X4}" != target)
                    continue;
                if (blockIdx == _ast.Blocks.Count - 1
                    && _blockMap.TryGetValue(blockIdx, out var targetBlock)
                    && BlockIsVoidReturnOnly(targetBlock))
                {
                    targetBlockIndex = blockIdx;
                    return true;
                }

                return false;
            }

            return false;
        }

        void ConsumeStructuredBlock(StructuredBlock block)
        {
            if (block.BlockIndex >= 0)
            {
                _consumedBlocks.Add(block.BlockIndex);
                RemoveGotoTargetsForConsumedBlock(block.BlockIndex);
            }
            foreach (var child in block.Children)
                ConsumeStructuredBlock(child);
            foreach (var child in block.TryChildren)
                ConsumeStructuredBlock(child);
            foreach (var child in block.HandlerChildren)
                ConsumeStructuredBlock(child);
            if (block.ThenBlock is not null)
                ConsumeStructuredBlock(block.ThenBlock);
            if (block.ElseBlock is not null)
                ConsumeStructuredBlock(block.ElseBlock);
        }

        bool TryAddReceiverAlias(ILAstExpression expr, HashSet<string> receiverAliases, out string aliasName)
        {
            aliasName = "";
            if (expr.OpCode is not (ILOpCode.Stloc or ILOpCode.Stloc_s
                or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3))
                return false;
            if (expr.Arguments.Count != 1)
                return false;
            if (!TryGetSimpleReceiverString(expr.Arguments[0], out string? assignedReceiver)
                || assignedReceiver is null
                || !receiverAliases.Contains(assignedReceiver))
                return false;

            aliasName = expr.Operand ?? GetLocalName(expr.OpCode);
            receiverAliases.Add(aliasName);
            return true;
        }

        static bool ReferencesAnyLocal(ILAstExpression expr, HashSet<string> localNames)
        {
            if (GetLocalReferenceName(expr) is { } localName && localNames.Contains(localName))
                return true;

            foreach (var arg in expr.Arguments)
            {
                if (ReferencesAnyLocal(arg, localNames))
                    return true;
            }

            return false;
        }

        static bool ContainsAnyAliasText(string text, HashSet<string> aliases)
            => aliases.Any(alias => text.Contains(alias, StringComparison.Ordinal));

        static bool IsSideEffectFreeIndexExpression(ILAstExpression expr)
        {
            if (IsLdcI4(expr.OpCode))
                return true;
            if (expr.OpCode is ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2
                or ILOpCode.Ldarg_3 or ILOpCode.Ldarg_s or ILOpCode.Ldarg
                or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
                or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc)
            {
                return true;
            }

            if (expr.OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul
                or ILOpCode.Div or ILOpCode.Rem
                or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un
                or ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4
                or ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_u4
                or ILOpCode.Conv_i8 or ILOpCode.Conv_u8)
            {
                return expr.Arguments.All(IsSideEffectFreeIndexExpression);
            }

            return false;
        }

        bool AliasUsedOutsideBlock(int consumedBlockIndex, HashSet<string> aliases)
        {
            for (int i = 0; i < _ast.Blocks.Count; i++)
            {
                if (i == consumedBlockIndex)
                    continue;

                foreach (var node in _ast.Blocks[i].Nodes)
                {
                    if (NodeReferencesAnyLocal(node, aliases))
                        return true;
                }
            }

            return false;
        }

        static bool NodeReferencesAnyLocal(ILAstNode node, HashSet<string> aliases) => node switch
        {
            ILAstAssignment assign => aliases.Contains(assign.Variable.Name) || ReferencesAnyLocal(assign.Value, aliases),
            ILAstStatement { Expression: var expr } => IsStoreToAnyLocal(expr, aliases) || ReferencesAnyLocal(expr, aliases),
            _ => false
        };

        static bool IsStoreToAnyLocal(ILAstExpression expr, HashSet<string> aliases)
        {
            if (expr.OpCode is not (ILOpCode.Stloc or ILOpCode.Stloc_s
                or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3))
                return false;

            string name = expr.Operand ?? GetLocalName(expr.OpCode);
            return aliases.Contains(name);
        }

        static bool IsIgnorableNullConditionalAssignmentNode(ILAstExpression expr)
            => expr.OpCode is ILOpCode.Nop or ILOpCode.Br or ILOpCode.Br_s
                or ILOpCode.Leave or ILOpCode.Leave_s;

        bool TryFormatNullConditionalAssignmentTarget(
            ILAstExpression expr,
            HashSet<string> receiverAliases,
            out string? targetSuffix,
            out string? assignmentOperator,
            out ILAstExpression? value,
            out string? expectedType)
        {
            targetSuffix = null;
            assignmentOperator = null;
            value = null;
            expectedType = null;

            if (expr.OpCode == ILOpCode.Stfld && expr.Arguments.Count >= 2)
            {
                if (!TryGetSimpleReceiverString(expr.Arguments[0], out string? actualReceiver)
                    || actualReceiver is null
                    || !receiverAliases.Contains(actualReceiver))
                    return false;

                targetSuffix = $"?.{ExtractMemberName(expr.Operand)}";
                expectedType = TryResolveFieldType(expr.Operand);
                if (TryExtractCompoundAssignmentValue(
                        expr.Arguments[1],
                        receiverAliases,
                        memberName: ExtractMemberName(expr.Operand),
                        indexExpression: null,
                        out assignmentOperator,
                        out value))
                {
                    return true;
                }

                assignmentOperator = "=";
                value = expr.Arguments[1];
                return true;
            }

            if (IsStelemOpCode(expr.OpCode) && expr.Arguments.Count >= 3)
            {
                if (!TryGetSimpleReceiverString(expr.Arguments[0], out string? actualReceiver)
                    || actualReceiver is null
                    || !receiverAliases.Contains(actualReceiver))
                    return false;

                targetSuffix = $"?[{ExpressionToString(expr.Arguments[1])}]";
                assignmentOperator = "=";
                value = expr.Arguments[2];
                return true;
            }

            if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && !expr.IsStaticCall
                && expr.Operand is string operand)
            {
                string memberName = ExtractMemberName(operand);
                if (memberName.StartsWith("set_", StringComparison.Ordinal)
                    && expr.Arguments.Count >= 2
                    && TryGetSimpleReceiverString(expr.Arguments[0], out string? actualReceiver)
                && actualReceiver is not null
                && receiverAliases.Contains(actualReceiver))
                {
                if (memberName == "set_Item" && expr.Arguments.Count >= 3)
                {
                    if (!IsSideEffectFreeIndexExpression(expr.Arguments[1]))
                        return false;

                    string indexExpression = ExpressionToString(expr.Arguments[1]);
                    targetSuffix = $"?[{indexExpression}]";
                    if (TryExtractCompoundAssignmentValue(
                            expr.Arguments[2],
                            receiverAliases,
                            memberName: "Item",
                            indexExpression,
                            out assignmentOperator,
                            out value))
                    {
                        return true;
                    }

                    assignmentOperator = "=";
                    value = expr.Arguments[2];
                }
                else
                {
                    string propertyName = memberName[4..];
                    targetSuffix = $"?.{propertyName}";
                    if (TryExtractCompoundAssignmentValue(
                            expr.Arguments[1],
                            receiverAliases,
                            propertyName,
                            indexExpression: null,
                            out assignmentOperator,
                                out value))
                        {
                            return true;
                        }

                        assignmentOperator = "=";
                        value = expr.Arguments[1];
                    }

                    return true;
                }
            }

            return false;
        }

        bool TryExtractCompoundAssignmentValue(
            ILAstExpression assignedValue,
            HashSet<string> receiverAliases,
            string memberName,
            string? indexExpression,
            out string? assignmentOperator,
            out ILAstExpression? rhs)
        {
            assignmentOperator = null;
            rhs = null;

            if (assignedValue.Arguments.Count < 2)
                return false;
            if (CompoundAssignmentOperator(assignedValue.OpCode) is not { } op)
                return false;
            if (!IsSameMemberRead(assignedValue.Arguments[0], receiverAliases, memberName, indexExpression))
                return false;

            assignmentOperator = $"{op}=";
            rhs = assignedValue.Arguments[1];
            return true;
        }

        bool IsSameMemberRead(ILAstExpression expr, HashSet<string> receiverAliases, string memberName, string? indexExpression)
        {
            string rendered = ExpressionToString(expr);
            if (indexExpression is null
                && receiverAliases.Any(receiver => rendered == $"{receiver}.{memberName}"))
            {
                return true;
            }
            if (indexExpression is not null
                && receiverAliases.Any(receiver => rendered == $"{receiver}[{indexExpression}]"))
            {
                return true;
            }

            if (expr.OpCode == ILOpCode.Ldfld
                && indexExpression is null
                && ExtractMemberName(expr.Operand) == memberName
                && expr.Arguments.Count > 0
                && TryGetSimpleReceiverString(expr.Arguments[0], out string? fieldReceiver)
                && fieldReceiver is not null
                && receiverAliases.Contains(fieldReceiver))
            {
                return true;
            }

            if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && !expr.IsStaticCall
                && expr.Operand is string operand
                && expr.Arguments.Count > 0)
            {
                string readMember = ExtractMemberName(operand);
                if (indexExpression is null
                    && readMember == $"get_{memberName}"
                    && TryGetSimpleReceiverString(expr.Arguments[0], out string? propertyReceiver)
                    && propertyReceiver is not null
                    && receiverAliases.Contains(propertyReceiver))
                {
                    return true;
                }
                if (indexExpression is not null
                    && readMember is "get_Item" or "get_Chars"
                    && expr.Arguments.Count >= 2
                    && TryGetSimpleReceiverString(expr.Arguments[0], out string? indexerReceiver)
                    && indexerReceiver is not null
                    && receiverAliases.Contains(indexerReceiver)
                    && ExpressionToString(expr.Arguments[1]) == indexExpression)
                {
                    return true;
                }
            }

            return false;
        }

        static string? CompoundAssignmentOperator(ILOpCode op) => op switch
        {
            ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un => "+",
            ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un => "-",
            ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un => "*",
            ILOpCode.Div => "/",
            ILOpCode.Rem => "%",
            ILOpCode.And => "&",
            ILOpCode.Or => "|",
            ILOpCode.Xor => "^",
            ILOpCode.Shl => "<<",
            ILOpCode.Shr => ">>",
            ILOpCode.Shr_un => ">>>",
            _ => null
        };

        bool TryGetSimpleReceiverString(ILAstExpression expr, out string? receiver)
        {
            receiver = null;
            if (expr.OpCode is not (ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2
                or ILOpCode.Ldarg_3 or ILOpCode.Ldarg_s or ILOpCode.Ldarg
                or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
                or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc))
            {
                return false;
            }

            receiver = ExpressionToString(expr);
            return receiver.Length > 0;
        }

        /// <summary>
        /// Item 1: Detect short-circuit return pattern:
        /// if(cond) { return expr; } return false → return cond &amp;&amp; expr;
        /// if(cond) { return true; } return expr → return cond || expr;
        /// </summary>
        bool TryEmitShortCircuitReturn(StructuredBlock block, string condition, int indent)
        {
            if (!_returnsBool) return false;

            string? thenReturn = TryExtractSingleReturnExprBool(block.ThenBlock);
            if (thenReturn is null) return false;

            // Get the follow/else return expression (without consuming)
            string? elseReturn;
            int followBlockIdx = -1;
            if (block.ElseBlock is not null)
            {
                elseReturn = TryExtractSingleReturnExprBool(block.ElseBlock);
            }
            else
            {
                bool wasBool = _emitBoolContext;
                _emitBoolContext = true;
                var follow = TryExtractFollowReturnExprNoConsume(block);
                _emitBoolContext = wasBool;
                if (follow is null) return false;
                elseReturn = follow.Value.expr;
                followBlockIdx = follow.Value.blockIdx;
            }
            if (elseReturn is null) return false;

            // Pattern: if(cond) return expr; return false → return cond && expr
            if (elseReturn is "false" or "0")
            {
                WriteIndent(indent);
                _sb.AppendLine(thenReturn is "true" or "1"
                    ? $"return {condition};"
                    : $"return {condition} && {thenReturn};");
                ConsumeReturnBlocks(block);
                if (followBlockIdx >= 0) _consumedBlocks.Add(followBlockIdx);
                return true;
            }

            // Pattern: if(cond) return true; return expr → return cond || expr
            if (thenReturn is "true" or "1")
            {
                WriteIndent(indent);
                _sb.AppendLine(elseReturn is "false" or "0"
                    ? $"return {condition};"
                    : $"return {condition} || {elseReturn};");
                ConsumeReturnBlocks(block);
                if (followBlockIdx >= 0) _consumedBlocks.Add(followBlockIdx);
                return true;
            }

            // Pattern: if(cond) return false; return expr → return !cond && expr
            if (thenReturn is "false" or "0")
            {
                string negCond = NegateConditionString(condition);
                WriteIndent(indent);
                _sb.AppendLine(elseReturn is "true" or "1"
                    ? $"return {negCond};"
                    : $"return {negCond} && {elseReturn};");
                ConsumeReturnBlocks(block);
                if (followBlockIdx >= 0) _consumedBlocks.Add(followBlockIdx);
                return true;
            }

            // Pattern: if(cond) return expr; return true → return !cond || expr
            if (elseReturn is "true" or "1")
            {
                string negCond = NegateConditionString(condition);
                WriteIndent(indent);
                _sb.AppendLine(thenReturn is "false" or "0"
                    ? $"return {negCond};"
                    : $"return {negCond} || {thenReturn};");
                ConsumeReturnBlocks(block);
                if (followBlockIdx >= 0) _consumedBlocks.Add(followBlockIdx);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Like TryExtractSingleReturnExpr but sets bool context for integer→bool normalization.
        /// </summary>
        string? TryExtractSingleReturnExprBool(StructuredBlock? block)
        {
            bool wasBool = _emitBoolContext;
            _emitBoolContext = true;
            var result = TryExtractSingleReturnExpr(block);
            _emitBoolContext = wasBool;
            return result;
        }

        // (TryExtractFollowReturnExprBool removed — inlined into TryEmitShortCircuitReturn)

        /// <summary>
        /// Item 6: Detect ternary return pattern:
        /// if(cond) { return e1; } return e2 → return cond ? e1 : e2;
        /// </summary>
        bool TryEmitTernaryReturn(StructuredBlock block, string condition, int indent)
        {
            string? thenReturn = TryExtractSingleReturnExpr(block.ThenBlock);
            if (thenReturn is null) return false;

            string? elseReturn;
            if (block.ElseBlock is not null)
            {
                elseReturn = TryExtractSingleReturnExpr(block.ElseBlock);
            }
            else
            {
                var follow = TryExtractFollowReturnExprNoConsume(block);
                if (follow is null) return false;
                elseReturn = follow.Value.expr;
                _consumedBlocks.Add(follow.Value.blockIdx);
            }
            if (elseReturn is null) return false;

            WriteIndent(indent);
            _sb.AppendLine($"return {condition} ? {thenReturn} : {elseReturn};");
            ConsumeReturnBlocks(block);
            return true;
        }

        /// <summary>
        /// Extract the return expression from a then/else block that contains only a single return.
        /// </summary>
        string? TryExtractSingleReturnExpr(StructuredBlock? block)
        {
            if (block is null) return null;

            int blockIdx = block.BlockIndex;
            if (block.Kind == StructuredBlockKind.Sequence && block.Children.Count == 1)
                blockIdx = block.Children[0].BlockIndex;

            if (blockIdx < 0 || !_blockMap.TryGetValue(blockIdx, out var astBlock))
                return null;

            // Block must contain exactly one return (possibly preceded by a stloc+ret that we can fold)
            if (astBlock.Nodes.Count == 1
                && astBlock.Nodes[0] is ILAstStatement { Expression: var retExpr }
                && retExpr.OpCode == ILOpCode.Ret
                && retExpr.Arguments.Count > 0)
            {
                return ExpressionToString(retExpr.Arguments[0]);
            }

            // Also handle stloc + ret pattern (return-temp)
            if (astBlock.Nodes.Count == 2
                && astBlock.Nodes[0] is ILAstStatement { Expression: var storeExpr }
                && storeExpr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                    or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc
                && storeExpr.Arguments.Count > 0
                && astBlock.Nodes[1] is ILAstStatement { Expression: var retExpr2 }
                && retExpr2.OpCode == ILOpCode.Ret)
            {
                return ExpressionToString(storeExpr.Arguments[0]);
            }

            return null;
        }

        /// <summary>
        /// Extract the return expression from the follow-through block after an if-then.
        /// Returns the expression and block index without consuming the block.
        /// </summary>
        (string expr, int blockIdx)? TryExtractFollowReturnExprNoConsume(StructuredBlock block)
        {
            int condBlockIdx = block.ConditionBlockIndex;
            if (condBlockIdx < 0) return null;

            int thenBlockIdx = block.ThenBlock?.BlockIndex ?? -1;
            if (thenBlockIdx < 0 && block.ThenBlock?.Kind == StructuredBlockKind.Sequence
                && block.ThenBlock.Children.Count > 0)
                thenBlockIdx = block.ThenBlock.Children[0].BlockIndex;
            if (thenBlockIdx < 0) return null;

            for (int i = 0; i < _ast.Blocks.Count; i++)
            {
                if (i == condBlockIdx || i == thenBlockIdx) continue;
                if (_consumedBlocks.Contains(i)) continue;
                if (i <= thenBlockIdx && i <= condBlockIdx) continue;

                var astBlock = _ast.Blocks[i];

                if (astBlock.Nodes.Count >= 1
                    && astBlock.Nodes[^1] is ILAstStatement { Expression: var retExpr }
                    && retExpr.OpCode == ILOpCode.Ret
                    && retExpr.Arguments.Count > 0)
                {
                    if (astBlock.Nodes.Count == 2
                        && astBlock.Nodes[0] is ILAstStatement { Expression: var storeExpr }
                        && storeExpr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                            or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc
                        && storeExpr.Arguments.Count > 0)
                    {
                        return (ExpressionToString(storeExpr.Arguments[0]), i);
                    }

                    if (astBlock.Nodes.Count == 1)
                    {
                        return (ExpressionToString(retExpr.Arguments[0]), i);
                    }
                }
                break;
            }
            return null;
        }

        void ConsumeReturnBlocks(StructuredBlock block)
        {
            if (block.ThenBlock is not null)
            {
                int idx = block.ThenBlock.BlockIndex;
                if (idx < 0 && block.ThenBlock.Kind == StructuredBlockKind.Sequence
                    && block.ThenBlock.Children.Count > 0)
                    idx = block.ThenBlock.Children[0].BlockIndex;
                if (idx >= 0)
                {
                    _consumedBlocks.Add(idx);
                    RemoveGotoTargetsForConsumedBlock(idx);
                }
            }
            if (block.ElseBlock is not null)
            {
                int idx = block.ElseBlock.BlockIndex;
                if (idx < 0 && block.ElseBlock.Kind == StructuredBlockKind.Sequence
                    && block.ElseBlock.Children.Count > 0)
                    idx = block.ElseBlock.Children[0].BlockIndex;
                if (idx >= 0)
                {
                    _consumedBlocks.Add(idx);
                    RemoveGotoTargetsForConsumedBlock(idx);
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
        /// Detect null-coalescing pattern: brtrue(dup(value)) where condition block assigns
        /// S_0 = value (non-null path) and then block has pop + S_0 = alternative (null path).
        /// Emits: value ?? alternative
        /// </summary>
        bool TryEmitNullCoalescing(StructuredBlock block, int indent)
        {
            // Scan the condition block for brtrue(dup(value))
            if (block.ConditionBlockIndex < 0
                || !_blockMap.TryGetValue(block.ConditionBlockIndex, out var condBlock))
                return false;

            ILAstExpression? branchExpr = null;
            ILAstExpression? nonNullValue = null;
            ILVariable? stackSlotVar = null;

            foreach (var node in condBlock.Nodes)
            {
                if (node is ILAstStatement stmt
                    && stmt.Expression.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s
                    && stmt.Expression.Arguments.Count == 1
                    && stmt.Expression.Arguments[0].OpCode == ILOpCode.Dup)
                {
                    branchExpr = stmt.Expression;
                }
                else if (node is ILAstAssignment assign
                    && assign.Variable.Kind == ILVariableKind.StackSlot)
                {
                    if (stackSlotVar is not null) return false; // multiple stack values — not null-coalescing
                    stackSlotVar = assign.Variable;
                    nonNullValue = assign.Value;
                }
            }

            if (branchExpr is null || nonNullValue is null)
                return false;

            // Then block (the negated/null path) should have pop + S_N = alternative
            var altValue = TryExtractNullCoalesceAlternative(block.ThenBlock!);
            if (altValue is null)
                return false;

            string lhs = ExpressionToString(nonNullValue);
            string rhs = ExpressionToString(altValue);
            // The pattern produces exactly one value at the merge point, so entry stack
            // position is always 0 → S_in_0. Same invariant as ternary pattern detection.
            _syntheticSubstitutions["S_in_0"] = $"{lhs} ?? {rhs}";

            if (block.ThenBlock!.BlockIndex >= 0)
            {
                _consumedBlocks.Add(block.ThenBlock.BlockIndex);
                RemoveGotoTargetsForConsumedBlock(block.ThenBlock.BlockIndex);
            }
            return true;
        }

        /// <summary>
        /// Extract the alternative value from a null-coalescing null path block.
        /// The block should contain: pop (discard dup), S_0 = alternative, optional br.
        /// </summary>
        ILAstExpression? TryExtractNullCoalesceAlternative(StructuredBlock? block)
        {
            if (block is null || block.BlockIndex < 0
                || !_blockMap.TryGetValue(block.BlockIndex, out var astBlock))
                return null;

            ILAstExpression? value = null;
            foreach (var node in astBlock.Nodes)
            {
                if (node is ILAstAssignment assign
                    && assign.Variable.Kind == ILVariableKind.StackSlot)
                {
                    if (value is not null) return null;
                    value = assign.Value;
                }
                else if (node is ILAstStatement stmt)
                {
                    var op = stmt.Expression.OpCode;
                    if (op is ILOpCode.Pop or ILOpCode.Br or ILOpCode.Br_s
                        or ILOpCode.Nop or ILOpCode.Leave or ILOpCode.Leave_s)
                        continue;
                    return null;
                }
                else return null;
            }
            return value;
        }

        /// <summary>
        /// Scan all blocks for C# collection expressions that lower through compiler-generated
        /// inline-array helper calls:
        /// InlineArrayElementRef(ref inlineArray, index) = value;
        /// InlineArrayAsReadOnlySpan(ref inlineArray, length)
        /// </summary>
        void ScanInlineArrayInitializers(ILAstMethod ast)
        {
            var candidates = new Dictionary<string, InlineArrayInitializerCandidate>();
            var spanConsumers = new HashSet<string>();

            foreach (var block in ast.Blocks)
            {
                foreach (var node in block.Nodes)
                {
                    if (TryGetInlineArrayElementStore(node, out string? inlineArrayLocal, out int index, out var value))
                    {
                        if (!candidates.TryGetValue(inlineArrayLocal, out var candidate))
                        {
                            candidate = new InlineArrayInitializerCandidate();
                            candidates[inlineArrayLocal] = candidate;
                        }

                        candidate.Elements[index] = value;
                        candidate.StoreNodes.Add(node);
                    }
                    else if (TryGetInlineArrayDefaultInit(node, out string? initializedLocal))
                    {
                        if (!candidates.TryGetValue(initializedLocal, out var candidate))
                        {
                            candidate = new InlineArrayInitializerCandidate();
                            candidates[initializedLocal] = candidate;
                        }

                        candidate.InitNodes.Add(node);
                    }

                    if (NodeExpression(node) is { } expr)
                    {
                        foreach (string consumedLocal in FindInlineArraySpanConsumers(expr))
                            spanConsumers.Add(consumedLocal);
                    }
                }
            }

            foreach (var (local, candidate) in candidates)
            {
                if (!spanConsumers.Contains(local) || HasUnexpectedInlineArrayUse(ast, local))
                    continue;

                _inlineArrayInitValues[local] = candidate.Elements;
                _suppressedLocals.Add(local);
                foreach (var node in candidate.StoreNodes)
                    _skipNodes.Add(node);
                foreach (var node in candidate.InitNodes)
                    _skipNodes.Add(node);
            }
        }

        IEnumerable<string> FindInlineArraySpanConsumers(ILAstExpression expr)
        {
            if (IsInlineArrayAsSpanCall(expr)
                && expr.Arguments.Count > 0
                && GetLocalReferenceName(expr.Arguments[0]) is { } local)
            {
                yield return local;
            }

            foreach (var arg in expr.Arguments)
            {
                foreach (string consumedLocal in FindInlineArraySpanConsumers(arg))
                    yield return consumedLocal;
            }
        }

        static bool IsInlineArrayAsSpanCall(ILAstExpression expr)
            => expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && expr.Operand is string operand
                && (operand.Contains("InlineArrayAsReadOnlySpan", StringComparison.Ordinal)
                    || operand.Contains("InlineArrayAsSpan", StringComparison.Ordinal));

        bool HasUnexpectedInlineArrayUse(ILAstMethod ast, string local)
        {
            foreach (var block in ast.Blocks)
            {
                foreach (var node in block.Nodes)
                {
                    if (NodeExpression(node) is { } expr
                        && HasUnexpectedInlineArrayUse(expr, local))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        bool HasUnexpectedInlineArrayUse(ILAstExpression expr, string local)
        {
            if (IsInlineArrayAsSpanCall(expr)
                || IsInlineArrayElementRefCall(expr))
            {
                for (int i = 0; i < expr.Arguments.Count; i++)
                {
                    if (i == 0 && GetLocalReferenceName(expr.Arguments[i]) == local)
                        continue;
                    if (HasUnexpectedInlineArrayUse(expr.Arguments[i], local))
                        return true;
                }

                return false;
            }

            if (expr.OpCode == ILOpCode.Initobj && expr.Arguments.Count > 0)
            {
                for (int i = 0; i < expr.Arguments.Count; i++)
                {
                    if (i == 0 && GetLocalReferenceName(expr.Arguments[i]) == local)
                        continue;
                    if (HasUnexpectedInlineArrayUse(expr.Arguments[i], local))
                        return true;
                }

                return false;
            }

            if (GetLocalReferenceName(expr) == local)
                return true;

            foreach (var arg in expr.Arguments)
            {
                if (HasUnexpectedInlineArrayUse(arg, local))
                    return true;
            }

            return false;
        }

        bool TryGetInlineArrayElementStore(
            ILAstNode node,
            out string inlineArrayLocal,
            out int index,
            out ILAstExpression value)
        {
            inlineArrayLocal = "";
            index = -1;
            value = null!;

            if (node is not ILAstStatement { Expression: var expr })
                return false;
            if (expr.OpCode is not (ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2
                or ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or ILOpCode.Stind_r4
                or ILOpCode.Stind_r8 or ILOpCode.Stind_ref or ILOpCode.Stobj))
                return false;
            if (expr.Arguments.Count < 2)
                return false;

            var valueExpr = expr.Arguments[0];
            var addressExpr = expr.Arguments[1];
            if (!IsInlineArrayElementRefCall(addressExpr) || addressExpr.Arguments.Count < 2)
                return false;

            if (GetLocalReferenceName(addressExpr.Arguments[0]) is not { } local)
                return false;
            if (!TryGetConstantIndex(addressExpr.Arguments[1], out int elementIndex))
                return false;

            inlineArrayLocal = local;
            index = elementIndex;
            value = valueExpr;
            return true;
        }

        static bool IsInlineArrayElementRefCall(ILAstExpression expr)
            => expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && expr.Operand is string operand
                && operand.Contains("InlineArrayElementRef", StringComparison.Ordinal);

        static bool TryGetInlineArrayDefaultInit(ILAstNode node, out string inlineArrayLocal)
        {
            inlineArrayLocal = "";
            if (node is not ILAstStatement { Expression: var expr })
                return false;
            if (expr.OpCode != ILOpCode.Initobj || expr.Arguments.Count == 0)
                return false;
            if (GetLocalReferenceName(expr.Arguments[0]) is not { } local)
                return false;

            inlineArrayLocal = local;
            return true;
        }

        /// <summary>
        /// Scan a block's nodes for array initializer patterns: stelem(dup(newarr), index, value).
        /// Collects element values per newarr IL offset and marks stelem nodes for skipping.
        /// </summary>
        void ScanArrayInitializers(IList<ILAstNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is not ILAstStatement stmt) continue;
                var expr = stmt.Expression;
                if (!IsStelemOpCode(expr.OpCode) || expr.Arguments.Count < 3) continue;

                // Check for stelem(dup(newarr T(N)), constIndex, value)
                var arrayArg = expr.Arguments[0];
                if (arrayArg.OpCode != ILOpCode.Dup || arrayArg.Arguments.Count < 1) continue;
                var newarr = arrayArg.Arguments[0];
                if (newarr.OpCode != ILOpCode.Newarr) continue;
                if (newarr.Arguments.Count == 0 || !TryGetConstantIndex(newarr.Arguments[0], out int size) || size < 0)
                    continue;

                if (!TryGetConstantIndex(expr.Arguments[1], out int idx) || idx < 0) continue;

                if (!_arrayInitValues.TryGetValue(newarr.Offset, out var elements))
                {
                    elements = [];
                    _arrayInitValues[newarr.Offset] = elements;
                }

                elements[idx] = expr.Arguments[2];
                _skipNodes.Add(node);
            }
        }

        static bool IsStelemOpCode(ILOpCode op) => op is
            ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or
            ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
            ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref;

        void TryEnsureCollectionTempForMutation(ILAstExpression expr, int indent)
        {
            if (!TryGetCollectionMutationReceiver(expr, out var newObj))
                return;
            if (_collectionTemps.ContainsKey(newObj.Offset))
                return;

            string tempName = CreateCollectionTempName();

            WriteIndent(indent);
            _sb.Append($"{SimplifyTypeName(ExtractTypeName(newObj.Operand))} {tempName} = ");
            EmitNewObjectExpression(newObj);
            _sb.AppendLine(";");

            _collectionTemps[newObj.Offset] = tempName;
        }

        bool TryGetCollectionMutationReceiver(ILAstExpression expr, out ILAstExpression newObj)
        {
            if (expr.OpCode == ILOpCode.Pop && expr.Arguments.Count == 1)
                return TryGetCollectionMutationReceiver(expr.Arguments[0], out newObj);

            newObj = null!;
            if (expr.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt))
                return false;
            if (expr.IsStaticCall || expr.Arguments.Count == 0)
                return false;

            string memberName = ExtractMemberName(expr.Operand);
            if (memberName is not ("Add" or "AddRange"))
                return false;

            var receiver = expr.Arguments[0];
            if (receiver.OpCode != ILOpCode.Dup || receiver.Arguments.Count != 1)
                return false;
            if (receiver.Arguments[0].OpCode != ILOpCode.Newobj)
                return false;

            newObj = receiver.Arguments[0];
            return true;
        }

        string CreateCollectionTempName()
        {
            while (true)
            {
                string name = $"__collection{_nextCollectionTemp++}";
                if (_ast.Locals.Any(l => l.Name == name))
                    continue;
                if (_paramNames is not null && _paramNames.Contains(name))
                    continue;
                return name;
            }
        }

        static bool TryGetConstantIndex(ILAstExpression expr, out int value)
        {
            value = 0;
            return expr.Operand is string s && int.TryParse(s, out value);
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

        /// <summary>
        /// For a no-else conditional, look at the next sibling block in the parent
        /// sequence for an S_0 assignment that serves as the "else" value.
        /// </summary>
        (ILAstExpression? expr, int blockIdx) TryExtractFollowTernaryValue(StructuredBlock ifBlock)
        {
            var parent = FindParentSequence(ifBlock);
            if (parent is null) return (null, -1);

            int myIndex = -1;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (ReferenceEquals(parent.Children[i], ifBlock))
                { myIndex = i; break; }
            }

            if (myIndex < 0 || myIndex + 1 >= parent.Children.Count)
                return (null, -1);

            var followBlock = parent.Children[myIndex + 1];
            if (followBlock.BlockIndex < 0 || !_blockMap.TryGetValue(followBlock.BlockIndex, out var astBlock))
                return (null, -1);

            ILAstExpression? value = null;
            foreach (var node in astBlock.Nodes)
            {
                if (node is ILAstAssignment assign && assign.Variable.Kind == ILVariableKind.StackSlot)
                {
                    if (value is not null) return (null, -1);
                    value = assign.Value;
                }
                else if (node is ILAstStatement stmt)
                {
                    var op = stmt.Expression.OpCode;
                    if (op is not (ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Nop
                        or ILOpCode.Leave or ILOpCode.Leave_s))
                        return (null, -1);
                }
                else return (null, -1);
            }
            return (value, followBlock.BlockIndex);
        }

        /// <summary>
        /// Detect short-circuit &amp;&amp; and || patterns from ternary values.
        /// </summary>
        string? TryBuildShortCircuit(string condition, ILAstExpression thenExpr, ILAstExpression elseExpr)
        {
            bool thenIsTrue = IsTrueLiteral(thenExpr);
            bool thenIsFalse = IsFalseLiteral(thenExpr);
            bool elseIsTrue = IsTrueLiteral(elseExpr);
            bool elseIsFalse = IsFalseLiteral(elseExpr);

            string thenStr = ExpressionToString(thenExpr);
            string elseStr = ExpressionToString(elseExpr);

            if (elseIsFalse && !thenIsFalse && !thenIsTrue)
                return $"{condition} && {thenStr}";
            if (thenIsTrue && !elseIsTrue && !elseIsFalse)
                return $"{condition} || {elseStr}";
            if (elseIsTrue && !thenIsTrue && !thenIsFalse)
                return $"{NegateConditionString(condition)} || {thenStr}";
            if (thenIsFalse && !elseIsFalse && !elseIsTrue)
                return $"{NegateConditionString(condition)} && {elseStr}";

            return null;
        }

        static bool IsTrueLiteral(ILAstExpression expr) =>
            expr.OpCode == ILOpCode.Ldc_i4_1 || (expr.OpCode == ILOpCode.Ldc_i4_s && expr.Operand == "1");

        static bool IsFalseLiteral(ILAstExpression expr) =>
            expr.OpCode == ILOpCode.Ldc_i4_0;

        /// <summary>
        /// Remove goto targets that only originated from the specified consumed block.
        /// </summary>
        void RemoveGotoTargetsForConsumedBlock(int blockIndex)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var block))
                return;

            foreach (var node in block.Nodes)
            {
                if (node is ILAstStatement { Expression: var expr }
                    && expr.OpCode is ILOpCode.Br or ILOpCode.Br_s
                    && expr.Operand is string target)
                {
                    bool otherRef = false;
                    foreach (var (otherIdx, otherBlock) in _blockMap)
                    {
                        if (otherIdx == blockIndex || _consumedBlocks.Contains(otherIdx))
                            continue;
                        foreach (var otherNode in otherBlock.Nodes)
                        {
                            if (otherNode is ILAstStatement { Expression: var otherExpr }
                                && otherExpr.Operand is string otherTarget && otherTarget == target)
                            { otherRef = true; break; }
                        }
                        if (otherRef) break;
                    }
                    if (!otherRef) _gotoTargets.Remove(target);
                }
            }
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
                                if (!TryEmitCompoundAssignment(assign.Variable.Name, assign.Value, indent + 1))
                                {
                                    WriteIndent(indent + 1);
                                    _sb.Append($"{assign.Variable.Name} = ");
                                    EmitExpression(assign.Value);
                                    _sb.AppendLine(";");
                                }
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
                        // Try to extract loop init from the preceding emitted line
                        string? init = TryExtractForInit(condition);

                        WriteIndent(indent);
                        if (init is not null)
                            _sb.AppendLine($"for ({init}; {condition}; {increment})");
                        else
                            _sb.AppendLine($"for (; {condition}; {increment})");
                    }
                    else
                    {
                        WriteIndent(indent);
                        _sb.AppendLine($"while ({condition})");
                    }
                    WriteIndent(indent);
                    _sb.AppendLine("{");

                    EmitLoopBody(bodyIndices, indent + 1, loop);

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
                        if (!TryEmitCompoundAssignment(assign.Variable.Name, assign.Value, indent))
                        {
                            WriteIndent(indent);
                            _sb.Append($"{assign.Variable.Name} = ");
                            EmitExpression(assign.Value);
                            _sb.AppendLine(";");
                        }
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
            bool sameVar = IsLoadOf(lhs, varName);

            if (!sameVar) return null;

            // The variable must appear in the condition
            if (!condition.Contains(varName)) return null;

            // Mark this node for suppression during body emission
            _forIncrementStatements.Add((blockIdx, nodeIdx));

            // Use compact compound format
            var compound = FormatCompoundAssignment(varName, addExpr);
            return compound ?? $"{varName} = {varName} {(addExpr.OpCode == ILOpCode.Sub ? "-" : "+")} {ExpressionToString(addExpr.Arguments[1])}";
        }

        // Set of (blockIndex, nodeIndex) for for-loop increment statements to suppress
        readonly HashSet<(int blockIndex, int nodeIndex)> _forIncrementStatements = [];

        /// <summary>
        /// Try to extract a loop initializer from the already-emitted output.
        /// Looks for the last line matching <c>V_X = expr;</c> where V_X appears in the condition.
        /// If found, removes it from the StringBuilder and returns the init expression (e.g., "V_1 = 0").
        /// </summary>
        string? TryExtractForInit(string condition)
        {
            // Find the last newline to get the last emitted line
            string current = _sb.ToString();
            int lastNewline = current.TrimEnd().LastIndexOf('\n');
            if (lastNewline < 0) return null;

            string lastLine = current[(lastNewline + 1)..].Trim();

            // Must be a simple assignment: V_X = expr;
            if (!lastLine.EndsWith(';')) return null;
            string withoutSemicolon = lastLine[..^1].Trim();
            int eqIdx = withoutSemicolon.IndexOf(" = ", StringComparison.Ordinal);
            if (eqIdx < 0) return null;

            string lhs = withoutSemicolon[..eqIdx].Trim();
            // Handle merged declarations: "int V_1 = 0" → extract "V_1" as the variable name
            string varName = lhs;
            int lastSpace = lhs.LastIndexOf(' ');
            if (lastSpace >= 0)
                varName = lhs[(lastSpace + 1)..];
            // Variable must appear in the condition
            if (!condition.Contains(varName, StringComparison.Ordinal)) return null;
            // Variable must look like a local name (V_N or named)
            if (varName.Length == 0) return null;

            // Remove the line from the StringBuilder
            _sb.Length = lastNewline + 1;
            // Also trim any trailing blank line
            string updated = _sb.ToString();
            if (updated.EndsWith("\n\n"))
                _sb.Length = _sb.Length - 1;

            return withoutSemicolon;
        }

        /// <summary>
        /// Emit loop body blocks, detecting and emitting inner loops as structured constructs.
        /// </summary>
        void EmitLoopBody(List<int> bodyIndices, int indent, NaturalLoop outerLoop)
        {
            // Build map of inner loop headers within this body
            var innerLoopByHeader = new Dictionary<int, NaturalLoop>();
            foreach (var innerLoop in _structure.Loops)
            {
                if (innerLoop.HeaderIndex == outerLoop.HeaderIndex) continue;
                if (bodyIndices.Contains(innerLoop.HeaderIndex)
                    && outerLoop.BodyIndices.IsSupersetOf(innerLoop.BodyIndices))
                {
                    innerLoopByHeader.TryAdd(innerLoop.HeaderIndex, innerLoop);
                }
            }

            var consumedByInner = new HashSet<int>();

            for (int bi = 0; bi < bodyIndices.Count; bi++)
            {
                int bodyIdx = bodyIndices[bi];
                if (consumedByInner.Contains(bodyIdx)) continue;

                // Check if this block starts an inner loop body (precedes inner loop header)
                NaturalLoop? innerLoop = null;
                foreach (var (hdr, loop) in innerLoopByHeader)
                {
                    if (loop.BodyIndices.Contains(bodyIdx) && !consumedByInner.Contains(hdr))
                    {
                        innerLoop = loop;
                        break;
                    }
                }

                if (innerLoop is not null)
                {
                    // Emit the inner loop as a structured construct
                    EmitInnerLoop(innerLoop, indent, outerLoop);
                    foreach (int idx in innerLoop.BodyIndices)
                        consumedByInner.Add(idx);
                    continue;
                }

                EmitBasicBlockForLoop(bodyIdx, indent, outerLoop);
            }
        }

        /// <summary>
        /// Emit an inner loop found within an outer loop's body.
        /// </summary>
        void EmitInnerLoop(NaturalLoop innerLoop, int indent, NaturalLoop outerLoop)
        {
            int headerIdx = innerLoop.HeaderIndex;
            if (!_blockMap.TryGetValue(headerIdx, out var headerBlock))
                return;

            // Extract condition from header's last node
            string? condition = null;
            bool negateCondition = false;
            var lastNode = headerBlock.Nodes.LastOrDefault();
            if (lastNode is ILAstStatement branchStmt && IsBranchOpCode(branchStmt.Expression.OpCode))
            {
                var branchExpr = branchStmt.Expression;
                string? branchTarget = branchExpr.Operand as string;

                bool branchGoesIntoLoop = false;
                if (branchTarget is not null)
                {
                    foreach (int bodyIdx in innerLoop.BodyIndices)
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

            var bodyIndices = innerLoop.BodyIndices
                .Where(idx => idx != headerIdx)
                .OrderBy(x => x)
                .ToList();

            if (condition is not null)
            {
                string? increment = TryExtractForIncrement(bodyIndices, condition);

                EmitHeaderStatements(headerIdx, indent);

                if (increment is not null)
                {
                    string? init = TryExtractForInit(condition);
                    WriteIndent(indent);
                    if (init is not null)
                        _sb.AppendLine($"for ({init}; {condition}; {increment})");
                    else
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
                    EmitBasicBlockForLoop(bodyIdx, indent + 1, innerLoop);

                WriteIndent(indent);
                _sb.AppendLine("}");
            }
            else
            {
                // Fallback: emit blocks sequentially
                EmitBasicBlock(headerIdx, indent);
                foreach (int bodyIdx in bodyIndices)
                    EmitBasicBlockForLoop(bodyIdx, indent, outerLoop);
            }

            _consumedBlocks.Add(headerIdx);
            // Suppress labels for inner loop header
            if (_blockStartOffset.TryGetValue(headerIdx, out int hdrOff))
                _loopHeaderLabels.Add($"IL_{hdrOff:X4}");
        }

        /// <summary>
        /// Emit a basic block inside a loop, converting gotos to break/continue.
        /// </summary>
        void EmitBasicBlockForLoop(int blockIndex, int indent, NaturalLoop loop)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var astBlock))
                return;

            _consumedBlocks.Add(blockIndex);
            _currentBlockIndex = blockIndex;
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
                        if (!TryEmitCompoundAssignment(assign.Variable.Name, assign.Value, indent))
                        {
                            WriteIndent(indent);
                            _sb.Append($"{assign.Variable.Name} = ");
                            EmitExpression(assign.Value);
                            _sb.AppendLine(";");
                        }
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
                            else if (nodeIdx == astBlock.Nodes.Count - 1
                                && TryEmitLoopGuardClause(stmt.Expression, condTarget, indent, loop))
                            {
                                // Item 7: Guard clause handled — fallthrough blocks emitted inside if body
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

        /// <summary>
        /// Item 7: When a conditional branch inside a loop body jumps to the increment/continuation
        /// block, the fallthrough blocks between here and there form a guard clause body.
        /// The fallthrough blocks may not be part of the loop (they exit via break/return).
        /// Emits: if (negated_cond) { fallthrough_body; }
        /// </summary>
        bool TryEmitLoopGuardClause(ILAstExpression branchExpr, string condTarget, int indent, NaturalLoop loop)
        {
            // Find the target block index
            int targetBlockIdx = -1;
            foreach (var (blockIdx, offset) in _blockStartOffset)
            {
                if ($"IL_{offset:X4}" == condTarget)
                {
                    targetBlockIdx = blockIdx;
                    break;
                }
            }
            if (targetBlockIdx < 0) return false;

            // Find unconsumed blocks between current block and target (these are the fallthrough)
            var fallthroughBlocks = new List<int>();
            for (int i = 0; i < _ast.Blocks.Count; i++)
            {
                if (i <= _currentBlockIndex || i >= targetBlockIdx) continue;
                if (_consumedBlocks.Contains(i)) continue;
                fallthroughBlocks.Add(i);
            }
            if (fallthroughBlocks.Count == 0) return false;

            // Check that the fallthrough blocks end with break/return (exit the loop)
            int lastFtIdx = fallthroughBlocks[^1];
            if (!_blockMap.TryGetValue(lastFtIdx, out var lastFtBlock)) return false;
            var lastFtNode = lastFtBlock.Nodes.LastOrDefault();
            bool endsWithExit = false;
            string? exitLabel = null;
            if (lastFtNode is ILAstStatement { Expression: var ftExpr })
            {
                if (ftExpr.OpCode == ILOpCode.Ret)
                    endsWithExit = true;
                else if (ftExpr.OpCode is ILOpCode.Br or ILOpCode.Br_s
                    && ftExpr.Operand is string ftTarget
                    && !IsInsideLoop(ftTarget, loop))
                {
                    endsWithExit = true;
                    exitLabel = ftTarget;
                }
            }
            if (!endsWithExit) return false;

            // Suppress the break target label (it's consumed by the break statement)
            if (exitLabel is not null)
                _loopConsumedLabels.Add(exitLabel);

            // Emit: if (fall_through_cond) { ... }
            // BranchConditionToString returns the raw VALUE for brfalse/brtrue (via ExtractCondition)
            // and the branch-taken CONDITION for binary branches (beq/blt/etc via EmitBranchCondition).
            // For brfalse: branch taken when value is FALSE, fall-through when TRUE → use condition directly
            // For brtrue/binary: branch taken when TRUE/condition met, fall-through when FALSE → negate
            string condition = BranchConditionToString(branchExpr);
            string guardCondition = branchExpr.OpCode is ILOpCode.Brfalse or ILOpCode.Brfalse_s
                ? condition
                : NegateConditionString(condition);
            WriteIndent(indent);
            _sb.AppendLine($"if ({guardCondition})");
            WriteIndent(indent);
            _sb.AppendLine("{");
            foreach (int ftIdx in fallthroughBlocks)
                EmitBasicBlockForLoop(ftIdx, indent + 1, loop);
            WriteIndent(indent);
            _sb.AppendLine("}");
            return true;
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
        /// Detect constant-condition branches used as Debug stepping markers.
        /// e.g., brtrue [ldc.i4.1] — always taken, brfalse [ldc.i4.1] — never taken.
        /// These are compiler-generated no-ops and should be silently skipped.
        /// </summary>
        static bool IsConstantBranch(ILAstExpression expr)
        {
            if (expr.OpCode is not (ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s))
                return false;
            if (expr.Arguments.Count != 1) return false;
            var arg = expr.Arguments[0];
            return arg.OpCode is ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1
                or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4;
        }

        static bool IsNonZeroConstant(ILAstExpression expr) => expr.OpCode switch
        {
            ILOpCode.Ldc_i4_0 => false,
            ILOpCode.Ldc_i4_1 => true,
            // ldc.i4.s and ldc.i4 carry the value in Operand
            ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => expr.Operand is not null && expr.Operand != "0",
            _ => false
        };

        /// <summary>
        /// Detect guard clause pattern for conditional branches outside of loops:
        /// if (cond) goto TARGET; body; → if (negated_cond) { body; }
        /// where body ends with return/throw/unconditional branch (no fallthrough).
        /// </summary>
        bool TryEmitGuardClause(ILAstExpression branchExpr, string condTarget, int indent)
        {
            // Find the target block index
            int targetBlockIdx = -1;
            foreach (var (blockIdx, offset) in _blockStartOffset)
            {
                if ($"IL_{offset:X4}" == condTarget)
                {
                    targetBlockIdx = blockIdx;
                    break;
                }
            }
            if (targetBlockIdx < 0) return false;

            // Find unconsumed blocks between current block and target (fallthrough body)
            var fallthroughBlocks = new List<int>();
            for (int i = 0; i < _ast.Blocks.Count; i++)
            {
                if (i <= _currentBlockIndex || i >= targetBlockIdx) continue;
                if (_consumedBlocks.Contains(i)) continue;
                fallthroughBlocks.Add(i);
            }
            if (fallthroughBlocks.Count == 0) return false;

            // Check that the fallthrough blocks end with return/throw/br (no fallthrough)
            int lastFtIdx = fallthroughBlocks[^1];
            if (!_blockMap.TryGetValue(lastFtIdx, out var lastFtBlock)) return false;
            var lastFtNode = lastFtBlock.Nodes.LastOrDefault();
            bool endsWithExit = false;
            if (lastFtNode is ILAstStatement { Expression: var ftExpr })
            {
                if (ftExpr.OpCode is ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow)
                    endsWithExit = true;
                else if (ftExpr.OpCode is ILOpCode.Br or ILOpCode.Br_s)
                    endsWithExit = true;
            }
            if (!endsWithExit) return false;

            // Emit: if (fall_through_cond) { fallthrough body }
            // BranchConditionToString returns the raw VALUE for brfalse/brtrue (via ExtractCondition)
            // and the branch-taken CONDITION for binary branches (beq/blt/etc via EmitBranchCondition).
            // For brfalse: branch taken when value is FALSE, fall-through when TRUE → use condition directly
            // For brtrue/binary: branch taken when TRUE/condition met, fall-through when FALSE → negate
            string condition = BranchConditionToString(branchExpr);
            string guardCondition = branchExpr.OpCode is ILOpCode.Brfalse or ILOpCode.Brfalse_s
                ? condition
                : NegateConditionString(condition);
            WriteIndent(indent);
            _sb.AppendLine($"if ({guardCondition})");
            WriteIndent(indent);
            _sb.AppendLine("{");
            foreach (int ftIdx in fallthroughBlocks)
                EmitBasicBlock(ftIdx, indent + 1);
            WriteIndent(indent);
            _sb.AppendLine("}");
            return true;
        }

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
                    bool wasBool = _emitBoolContext;
                    if (_returnsBool)
                        _emitBoolContext = true;
                    EmitExpression(retExpr.Arguments[0], _returnTypeName);
                    _emitBoolContext = wasBool;
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

        /// <summary>
        /// Peephole: stloc V_x = expr; ret(ldloc V_x) → return expr;
        /// </summary>
        bool TryEmitStoreReturn(ILAstBlock block, int nodeIdx, int indent)
        {
            if (nodeIdx + 1 >= block.Nodes.Count) return false;

            var nextNode = block.Nodes[nodeIdx + 1];
            if (nextNode is not ILAstStatement { Expression: var retExpr }) return false;
            if (retExpr.OpCode != ILOpCode.Ret || retExpr.Arguments.Count == 0) return false;

            var retArg = retExpr.Arguments[0];
            string? retVarName = retArg.OpCode switch
            {
                ILOpCode.Ldloc_0 => "V_0", ILOpCode.Ldloc_1 => "V_1",
                ILOpCode.Ldloc_2 => "V_2", ILOpCode.Ldloc_3 => "V_3",
                ILOpCode.Ldloc_s or ILOpCode.Ldloc => retArg.Operand,
                _ => null
            };
            if (retVarName is null) return false;

            ILAstExpression? valueExpr = null;
            var curNode = block.Nodes[nodeIdx];
            if (curNode is ILAstStatement { Expression: var stExpr }
                && stExpr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                    or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc
                && stExpr.Arguments.Count > 0)
            {
                string? storeVar = stExpr.Operand ?? GetLocalName(stExpr.OpCode);
                if (storeVar == retVarName)
                    valueExpr = stExpr.Arguments[0];
            }

            if (valueExpr is null) return false;

            WriteIndent(indent);
            _sb.Append("return ");
            bool wasBool = _emitBoolContext;
            if (_returnsBool) _emitBoolContext = true;
            EmitExpression(valueExpr, _returnTypeName);
            _emitBoolContext = wasBool;
            _sb.AppendLine(";");
            return true;
        }

        void EmitTryCatchFinally(StructuredBlock block, int indent)
        {
            if (block.ExceptionRegion is not { } region) return;

            if (region.Kind == ExceptionRegionKind.Finally
                && TryGetLockPattern(block, out var lockPattern))
            {
                EmitLockBlock(block, lockPattern, indent);
                return;
            }

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

        bool TryGetLockPattern(StructuredBlock block, out LockPattern pattern)
        {
            if (_lockPatterns.TryGetValue(block, out pattern!))
                return true;

            if (!TryDetectLockPattern(block, out pattern))
                return false;

            _lockPatterns[block] = pattern;
            foreach (var node in pattern.SkipNodes)
                _skipNodes.Add(node);
            foreach (string local in pattern.SuppressedLocals)
                _suppressedLocals.Add(local);
            return true;
        }

        void EmitLockBlock(StructuredBlock block, LockPattern pattern, int indent)
        {
            WriteIndent(indent);
            _sb.Append("lock (");
            EmitExpression(pattern.LockExpression);
            _sb.AppendLine(")");
            WriteIndent(indent);
            _sb.AppendLine("{");

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

            MarkHandlerConsumed(block);
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
            else if (region.Kind == ExceptionRegionKind.Filter)
            {
                WriteIndent(indent);
                _sb.AppendLine(BuildFilterCatchHeader(region));
                WriteIndent(indent);
                _sb.AppendLine("{");
                foreach (var child in handlerChildren)
                    EmitStructuredBlock(child, indent + 1);
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
            else if (region.Kind == ExceptionRegionKind.Fault)
            {
                WriteIndent(indent);
                _sb.AppendLine("finally /* fault: runs only when an exception escapes the try */");
                WriteIndent(indent);
                _sb.AppendLine("{");
                foreach (var child in handlerChildren)
                    EmitStructuredBlock(child, indent + 1);
                WriteIndent(indent);
                _sb.AppendLine("}");
            }
        }

        /// <summary>
        /// Builds a <c>catch (T name) when (cond)</c> header from a filter region
        /// and consumes the filter code blocks so they don't leak into the output.
        /// The C# compiler's filter shape is stable: <c>isinst T</c> on the
        /// exception, a store to the catch local, the user condition normalized
        /// with <c>ldc.i4.0 cgt.un</c>, then <c>endfilter</c>. Anything that
        /// doesn't match falls back to a placeholder condition comment.
        /// </summary>
        string BuildFilterCatchHeader(ExceptionRegion region)
        {
            // Collect (and consume) the filter blocks: [FilterOffset, HandlerOffset).
            var filterNodes = new List<ILAstNode>();
            foreach (var (blockIdx, offset) in _blockStartOffset.OrderBy(kv => kv.Value))
            {
                if (offset < region.FilterOffset || offset >= region.HandlerOffset)
                    continue;
                _consumedBlocks.Add(blockIdx);
                RemoveGotoTargetsForConsumedBlock(blockIdx);
                if (_blockMap.TryGetValue(blockIdx, out var astBlock))
                    filterNodes.AddRange(astBlock.Nodes);
            }

            string? exType = null;
            string? catchVar = null;
            ILAstExpression? conditionExpr = null;

            foreach (var node in filterNodes)
            {
                if (NodeExpression(node) is not { } expr)
                    continue;

                // Exception type test: first isinst in the filter.
                if (exType is null && FindOpcodeInTree(expr, ILOpCode.Isinst) is { Operand: string typeOp })
                    exType = SimplifyTypeName(typeOp);

                // Catch local: a store whose value involves the type-tested exception.
                if (catchVar is null
                    && node is ILAstStatement { Expression: var stExpr }
                    && stExpr.OpCode is ILOpCode.Stloc or ILOpCode.Stloc_s
                        or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
                    && stExpr.Arguments.Count == 1
                    && (FindOpcodeInTree(stExpr.Arguments[0], ILOpCode.Isinst) is not null
                        || stExpr.Arguments[0].Operand is string ld && ld.StartsWith("S_in_", StringComparison.Ordinal)))
                {
                    catchVar = stExpr.Operand ?? GetLocalName(stExpr.OpCode);
                }

                // User condition: bool-normalized as cgt.un(cond, 0) before endfilter.
                if (expr.OpCode is ILOpCode.Cgt_un
                    && expr.Arguments.Count == 2
                    && IsZeroLiteral(expr.Arguments[1]))
                {
                    conditionExpr = expr.Arguments[0];
                }
            }

            // The normalized condition is often just a load of a bool temp the
            // filter assigned earlier — substitute the temp's defining expression.
            if (conditionExpr is not null && GetLocalReferenceName(conditionExpr) is { } condLocal)
            {
                foreach (var node in filterNodes)
                {
                    if (node is ILAstStatement { Expression: var stExpr }
                        && IsStoreToLocal(stExpr, condLocal)
                        && stExpr.Arguments.Count == 1)
                    {
                        conditionExpr = stExpr.Arguments[0];
                    }
                }
                _suppressedLocals.Add(condLocal);
            }

            // The handler refers to the exception as the synthetic incoming stack
            // value; give it the catch variable's name (or one we invent).
            string varName = catchVar ?? "ex";
            _syntheticSubstitutions["S_in_0"] = varName;

            string condition = conditionExpr is not null
                ? ExpressionToString(conditionExpr)
                : $"/* filter at IL_{region.FilterOffset:X4} */";
            string header = exType is null ? $"catch ({varName})" : $"catch ({exType} {varName})";
            return $"{header} when ({condition})";
        }

        /// <summary>
        /// Suppresses declarations of bool temps that exception filters normalize
        /// through (cgt.un(temp, 0) before endfilter) — the when-clause rendering
        /// inlines their defining expression.
        /// </summary>
        void ScanForFilterLocals(StructuredBlock block)
        {
            var regions = new List<ExceptionRegion>();
            if (block.ExceptionRegion is { Kind: ExceptionRegionKind.Filter } filterRegion)
                regions.Add(filterRegion);
            foreach (var addl in block.AdditionalHandlers)
                if (addl.Region.Kind == ExceptionRegionKind.Filter)
                    regions.Add(addl.Region);

            foreach (var region in regions)
            {
                foreach (var (blockIdx, offset) in _blockStartOffset)
                {
                    if (offset < region.FilterOffset || offset >= region.HandlerOffset)
                        continue;
                    if (!_blockMap.TryGetValue(blockIdx, out var astBlock))
                        continue;
                    foreach (var node in astBlock.Nodes)
                    {
                        if (NodeExpression(node) is { OpCode: ILOpCode.Cgt_un, Arguments.Count: 2 } expr
                            && IsZeroLiteral(expr.Arguments[1])
                            && GetLocalReferenceName(expr.Arguments[0]) is { } condLocal)
                        {
                            _suppressedLocals.Add(condLocal);
                        }
                    }
                }
            }

            foreach (var c in block.Children)
                ScanForFilterLocals(c);
            foreach (var c in block.TryChildren)
                ScanForFilterLocals(c);
            foreach (var c in block.HandlerChildren)
                ScanForFilterLocals(c);
            if (block.ThenBlock is not null)
                ScanForFilterLocals(block.ThenBlock);
            if (block.ElseBlock is not null)
                ScanForFilterLocals(block.ElseBlock);
        }

        /// <summary>Depth-first search for an opcode in an expression tree.</summary>
        static ILAstExpression? FindOpcodeInTree(ILAstExpression expr, ILOpCode opCode)
        {
            if (expr.OpCode == opCode)
                return expr;
            foreach (var arg in expr.Arguments)
            {
                if (FindOpcodeInTree(arg, opCode) is { } found)
                    return found;
            }
            return null;
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
                        if (!TryEmitCompoundAssignment(assign.Variable.Name, assign.Value, indent))
                        {
                            WriteIndent(indent);
                            _sb.Append($"{assign.Variable.Name} = ");
                            EmitExpression(assign.Value);
                            _sb.AppendLine(";");
                        }
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

                // Item 9: If default block is a bare goto, consume its fallthrough
                // instead of emitting the goto
                int defaultEmittedBlock = -1;
                if (_blockMap.TryGetValue(block.SwitchDefaultIndex, out var defAstBlock))
                {
                    var defLastNode = defAstBlock.Nodes.LastOrDefault();
                    if (defLastNode is ILAstStatement { Expression: { OpCode: ILOpCode.Br or ILOpCode.Br_s, Operand: string defTarget } })
                    {
                        // Find the fallthrough block and suppress its goto label
                        _loopConsumedLabels.Add(defTarget);
                        foreach (var (blockIdx, off) in _blockStartOffset)
                        {
                            if ($"IL_{off:X4}" != defTarget) continue;
                            if (_consumedBlocks.Contains(blockIdx)) break;

                            _consumedBlocks.Add(block.SwitchDefaultIndex);
                            EmitBasicBlock(blockIdx, indent + 2);
                            defaultEmittedBlock = blockIdx;
                            break;
                        }
                    }
                }

                if (defaultEmittedBlock < 0)
                {
                    EmitBasicBlock(block.SwitchDefaultIndex, indent + 2);
                    if (!BlockEndsWithReturn(block.SwitchDefaultIndex))
                    {
                        WriteIndent(indent + 2);
                        _sb.AppendLine("break;");
                    }
                }
                else if (!BlockEndsWithReturn(defaultEmittedBlock))
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

        bool BlockEndsWithReturnOrThrow(int blockIndex)
        {
            if (blockIndex < 0 || !_blockMap.TryGetValue(blockIndex, out var astBlock))
                return false;
            var lastNode = astBlock.Nodes.LastOrDefault();
            return lastNode is ILAstStatement { Expression.OpCode:
                ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow };
        }

        // --- Expression emission ---

        void EmitStatement(ILAstExpression expr, int indent)
        {
            TryEnsureCollectionTempForMutation(expr, indent);

            switch (expr.OpCode)
            {
                case ILOpCode.Ret:
                    WriteIndent(indent);
                    if (expr.Arguments.Count > 0)
                    {
                        _sb.Append("return ");
                        bool wasBool = _emitBoolContext;
                        if (_returnsBool)
                            _emitBoolContext = true;
                        _currentReturnArg = expr.Arguments[0];
                        EmitExpression(expr.Arguments[0], _returnTypeName);
                        _currentReturnArg = null;
                        _emitBoolContext = wasBool;
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
                    if (expr.Arguments.Count > 0 && TryEmitCompoundAssignment(varName, expr.Arguments[0], indent))
                        break;
                    WriteIndent(indent);
                    // Item 2: Merge declaration with first assignment
                    if (_mergedLocals.Remove(varName))
                    {
                        var local = _ast.Locals.FirstOrDefault(l => l.Name == varName);
                        string typeName = SimplifyTypeName(local?.TypeName ?? "var");
                        _sb.Append($"{typeName} {varName} = ");
                    }
                    else
                    {
                        _sb.Append($"{varName} = ");
                    }
                    if (expr.Arguments.Count > 0)
                    {
                        bool wasBool = _emitBoolContext;
                        if (_boolLocals.Contains(varName))
                            _emitBoolContext = true;
                        EmitExpression(expr.Arguments[0], _ast.Locals.FirstOrDefault(l => l.Name == varName)?.TypeName);
                        _emitBoolContext = wasBool;
                    }
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
                        EmitExpression(expr.Arguments[1], TryResolveFieldType(expr.Operand));
                    }
                    else if (expr.OpCode == ILOpCode.Stsfld && expr.Arguments.Count >= 1)
                    {
                        _sb.Append($"{expr.Operand} = ");
                        EmitExpression(expr.Arguments[0], TryResolveFieldType(expr.Operand));
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
                        _sb.AppendLine(" = default;");
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
                        // stind pops: value (arg0), address (arg1) — emit as address = value
                        var addrArg = expr.Arguments[1];
                        var valArg = expr.Arguments[0];
                        if (addrArg.OpCode is ILOpCode.Ldarga_s or ILOpCode.Ldarga)
                            _sb.Append(RemapArg(addrArg.Operand, addrArg.OpCode));
                        else if (addrArg.OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca)
                            _sb.Append(addrArg.Operand ?? "loc");
                        else
                            EmitExpression(addrArg);
                        _sb.Append(" = ");
                        // Propagate bool context for stind.i1 — stores a byte, almost always bool in C#
                        bool wasBool = _emitBoolContext;
                        if (expr.OpCode is ILOpCode.Stind_i1)
                            _emitBoolContext = true;
                        EmitExpression(valArg);
                        _emitBoolContext = wasBool;
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
                    // Skip constant-condition branches (Debug stepping markers like brtrue [ldc.i4.1])
                    if (IsConstantBranch(expr))
                        break;
                    // Conditional branches — when not consumed by structuring
                    // Try guard clause pattern: if (cond) goto TARGET; body → if (negated) { body; }
                    if (expr.Operand is string condTarget && TryEmitGuardClause(expr, condTarget, indent))
                        break;
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

                case ILOpCode.Nop when expr.Operand is null:
                    break;

                default:
                    WriteIndent(indent);
                    _sb.Append("/* ");
                    expr.WriteTo(_sb, 0);
                    _sb.AppendLine(" */");
                    break;
            }
        }

        void EmitExpression(ILAstExpression expr, string? expectedType = null)
        {
            // Consume bool context: only applies to this direct expression, not sub-expressions
            bool boolCtx = _emitBoolContext;
            _emitBoolContext = false;

            // Merge emission-context expectedType with AST-level ExpectedType (from BuildCall annotations)
            string? resolvedType = expectedType ?? expr.ExpectedType;

            // A bool-typed parameter/target means 0/1 integer literals are really false/true.
            boolCtx = boolCtx || resolvedType is "bool" or "System.Boolean" or "Boolean";

            // Enum constant resolution for integer literals with known parameter types
            if (IsLdcI4(expr.OpCode) && resolvedType is not null
                && TryResolveEnumName(resolvedType, GetI4Value(expr), out string? enumName))
            {
                _sb.Append(enumName);
                return;
            }

            switch (expr.OpCode)
            {
                // Constants
                case ILOpCode.Ldc_i4_m1: _sb.Append("-1"); break;
                case ILOpCode.Ldc_i4_0:
                    _sb.Append(IsBoolContext(expr, boolCtx) ? "false" : "0");
                    break;
                case ILOpCode.Ldc_i4_1:
                    _sb.Append(IsBoolContext(expr, boolCtx) ? "true" : "1");
                    break;
                case ILOpCode.Ldc_i4_2: _sb.Append('2'); break;
                case ILOpCode.Ldc_i4_3: _sb.Append('3'); break;
                case ILOpCode.Ldc_i4_4: _sb.Append('4'); break;
                case ILOpCode.Ldc_i4_5: _sb.Append('5'); break;
                case ILOpCode.Ldc_i4_6: _sb.Append('6'); break;
                case ILOpCode.Ldc_i4_7: _sb.Append('7'); break;
                case ILOpCode.Ldc_i4_8: _sb.Append('8'); break;
                case ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4:
                    if (IsBoolContext(expr, boolCtx))
                    {
                        var val = expr.Operand?.ToString();
                        if (val is "0") { _sb.Append("false"); break; }
                        if (val is "1") { _sb.Append("true"); break; }
                    }
                    _sb.Append(expr.Operand ?? "0");
                    break;
                case ILOpCode.Ldc_i8:
                    _sb.Append($"{expr.Operand ?? "0"}L");
                    break;
                case ILOpCode.Ldc_r4:
                {
                    string fval = expr.Operand ?? "0";
                    if (fval == "NaN") _sb.Append("float.NaN");
                    else if (fval == "Infinity") _sb.Append("float.PositiveInfinity");
                    else if (fval == "-Infinity") _sb.Append("float.NegativeInfinity");
                    else _sb.Append($"{fval}f");
                    break;
                }
                case ILOpCode.Ldc_r8:
                {
                    string dval = expr.Operand ?? "0";
                    if (dval == "NaN") _sb.Append("double.NaN");
                    else if (dval == "Infinity") _sb.Append("double.PositiveInfinity");
                    else if (dval == "-Infinity") _sb.Append("double.NegativeInfinity");
                    // Ensure double literals have a decimal point so they aren't mistaken for int
                    else if (!dval.Contains('.') && !dval.Contains('E') && !dval.Contains('e')
                        && char.IsAsciiDigit(dval[^1]))
                        _sb.Append($"{dval}.0d");
                    else
                        _sb.Append($"{dval}d");
                    break;
                }
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
                case ILOpCode.Add: EmitBinary(expr, "+"); break;
                case ILOpCode.Add_ovf or ILOpCode.Add_ovf_un:
                    EmitCheckedBinary(expr, "+"); break;
                case ILOpCode.Sub: EmitBinary(expr, "-"); break;
                case ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un:
                    EmitCheckedBinary(expr, "-"); break;
                case ILOpCode.Mul: EmitBinary(expr, "*"); break;
                case ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un:
                    EmitCheckedBinary(expr, "*"); break;
                case ILOpCode.Div: EmitBinary(expr, "/"); break;
                case ILOpCode.Div_un: EmitUnsignedArithmetic(expr, "/"); break;
                case ILOpCode.Rem: EmitBinary(expr, "%"); break;
                case ILOpCode.Rem_un: EmitUnsignedArithmetic(expr, "%"); break;
                case ILOpCode.And: EmitBinary(expr, "&"); break;
                case ILOpCode.Or: EmitBinary(expr, "|"); break;
                case ILOpCode.Xor: EmitBinary(expr, "^"); break;
                case ILOpCode.Shl: EmitBinary(expr, "<<"); break;
                case ILOpCode.Shr: EmitBinary(expr, ">>"); break;
                // shr.un on any operand is C#'s unsigned right shift.
                case ILOpCode.Shr_un: EmitBinary(expr, ">>>"); break;

                // Comparison operators
                case ILOpCode.Ceq:
                    // ">=u"/"<=u" markers carry bge.un/ble.un semantics from
                    // ExtractCondition (there are no cge/cle opcodes to map to).
                    if (expr.Operand == ">=u") { EmitUnsignedComparison(expr, ">=", "<"); break; }
                    if (expr.Operand == "<=u") { EmitUnsignedComparison(expr, "<=", ">"); break; }
                    if (!TryEmitNegatedComparisonZero(expr))
                        EmitBinary(expr, expr.Operand ?? "==");
                    break;
                case ILOpCode.Cgt: EmitBinary(expr, ">"); break;
                case ILOpCode.Cgt_un: EmitUnsignedComparison(expr, ">", "<="); break;
                case ILOpCode.Clt: EmitBinary(expr, "<"); break;
                case ILOpCode.Clt_un: EmitUnsignedComparison(expr, "<", ">="); break;

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
                case ILOpCode.Conv_i1: EmitCast(expr, "sbyte"); break;
                case ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i1_un:
                    EmitCheckedCast(expr, "sbyte"); break;
                case ILOpCode.Conv_u1: EmitCast(expr, "byte"); break;
                case ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un:
                    EmitCheckedCast(expr, "byte"); break;
                case ILOpCode.Conv_i2: EmitCast(expr, "short"); break;
                case ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i2_un:
                    EmitCheckedCast(expr, "short"); break;
                case ILOpCode.Conv_u2: EmitCast(expr, PreferredUInt16Type(resolvedType)); break;
                case ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u2_un:
                    EmitCheckedCast(expr, PreferredUInt16Type(resolvedType)); break;
                case ILOpCode.Conv_i4:
                    // Suppress (int) cast on ldlen — Array.Length is int in C#
                    if (expr.Arguments.Count > 0 && expr.Arguments[0].OpCode == ILOpCode.Ldlen)
                        EmitExpression(expr.Arguments[0]);
                    else
                        EmitCast(expr, "int");
                    break;
                case ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_i4_un:
                    EmitCheckedCast(expr, "int"); break;
                case ILOpCode.Conv_u4: EmitCast(expr, "uint"); break;
                case ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_u4_un:
                    EmitCheckedCast(expr, "uint"); break;
                case ILOpCode.Conv_i8:
                    // Emit suffixed literal instead of cast for constants: (long)3 → 3L
                    if (expr.Arguments.Count > 0 && IsLdcI4(expr.Arguments[0].OpCode))
                    {
                        int i4Val = GetI4Value(expr.Arguments[0]);
                        // For ulong return type, negative constants need unchecked cast
                        if (resolvedType is "ulong" or "System.UInt64" && i4Val < 0)
                            _sb.Append($"unchecked((ulong){i4Val})");
                        else
                            _sb.Append($"{i4Val}L");
                    }
                    else
                        EmitCast(expr, "long");
                    break;
                case ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_i8_un:
                    EmitCheckedCast(expr, "long"); break;
                case ILOpCode.Conv_u8:
                    if (expr.Arguments.Count > 0 && IsLdcI4(expr.Arguments[0].OpCode))
                    {
                        int i4Val = GetI4Value(expr.Arguments[0]);
                        if (i4Val >= 0)
                            _sb.Append($"{i4Val}UL");
                        else
                            _sb.Append($"unchecked((ulong){i4Val})");
                    }
                    else
                        EmitCast(expr, "ulong");
                    break;
                case ILOpCode.Conv_ovf_u8 or ILOpCode.Conv_ovf_u8_un:
                    EmitCheckedCast(expr, "ulong"); break;
                case ILOpCode.Conv_r4:
                    if (expr.Arguments.Count > 0 && IsLdcI4(expr.Arguments[0].OpCode))
                        _sb.Append($"{GetI4Value(expr.Arguments[0])}.0f");
                    else
                        EmitCast(expr, "float");
                    break;
                case ILOpCode.Conv_r8 or ILOpCode.Conv_r_un:
                    if (expr.Arguments.Count > 0 && IsLdcI4(expr.Arguments[0].OpCode))
                        _sb.Append($"{GetI4Value(expr.Arguments[0])}.0d");
                    else
                        EmitCast(expr, "double");
                    break;
                case ILOpCode.Conv_i: EmitCast(expr, "nint"); break;
                case ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_i_un:
                    EmitCheckedCast(expr, "nint"); break;
                case ILOpCode.Conv_u:
                    if (expr.Arguments.Count > 0 && IsAddressExpression(expr.Arguments[0]))
                    {
                        if (!IsPointerType(resolvedType))
                            _sb.Append("(nuint)");
                        EmitAddressExpression(expr.Arguments[0]);
                    }
                    else
                        EmitCast(expr, "nuint");
                    break;
                case ILOpCode.Conv_ovf_u or ILOpCode.Conv_ovf_u_un:
                    EmitCheckedCast(expr, "nuint"); break;

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
                        if (_collectionTemps.TryGetValue(expr.Offset, out string? collectionTemp))
                            _sb.Append(collectionTemp);
                        else if (!TryEmitSpanCollectionExpression(expr, resolvedType))
                            EmitNewObjectExpression(expr);
                        break;
                    }

                case ILOpCode.Newarr:
                    if (_arrayInitValues.TryGetValue(expr.Offset, out var initElements))
                    {
                        int size = 0;
                        if (expr.Arguments.Count > 0 && TryGetConstantIndex(expr.Arguments[0], out size)) { }
                        _sb.Append($"new {SimplifyTypeName(expr.Operand ?? "object")}[] {{ ");
                        for (int ai = 0; ai < size; ai++)
                        {
                            if (ai > 0) _sb.Append(", ");
                            if (initElements.TryGetValue(ai, out var elemExpr))
                                EmitExpression(elemExpr);
                            else
                                _sb.Append("default");
                        }
                        _sb.Append(" }");
                    }
                    else
                    {
                        _sb.Append($"new {SimplifyTypeName(expr.Operand ?? "object")}[");
                        if (expr.Arguments.Count > 0)
                            EmitExpression(expr.Arguments[0]);
                        _sb.Append(']');
                    }
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
                    if (!TryEmitPointerDereference(expr) && expr.Arguments.Count > 0)
                        EmitExpression(expr.Arguments[0]);
                    break;

                // Boxing - emit as cast to object (boxing is implicit in C#)
                case ILOpCode.Box:
                    _sb.Append("(object)");
                    EmitParenthesized(expr, 0);
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
                        EmitExpression(expr.Arguments[0], resolvedType);
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
                {
                    string tokenOp = expr.Operand ?? "?";
                    if (TryFormatTokenExpression(expr, tokenOp, out string? tokenExpr))
                        _sb.Append(tokenExpr);
                    else
                        _sb.Append($"typeof({SimplifyTypeName(tokenOp)})");
                    break;
                }

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
                    else if (_syntheticSubstitutions.TryGetValue(expr.Operand, out var subst))
                        _sb.Append(subst);
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

        void EmitNewObjectExpression(ILAstExpression expr)
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
                return;
            }

            _sb.Append($"new {simplified}(");
            for (int i = 0; i < expr.Arguments.Count; i++)
            {
                if (i > 0) _sb.Append(", ");
                EmitExpression(expr.Arguments[i]);
            }
            _sb.Append(')');
        }

        bool TryEmitSpanCollectionExpression(ILAstExpression expr, string? expectedType)
        {
            string constructedType = ExtractTypeName(expr.Operand);
            if (!IsSpanOrReadOnlySpanType(constructedType)
                && !IsSpanOrReadOnlySpanType(expectedType))
            {
                return false;
            }

            if (expr.Arguments.Count != 1 || expr.Arguments[0].OpCode != ILOpCode.Newarr)
                return false;

            return TryEmitCollectionExpressionFromNewArray(expr.Arguments[0]);
        }

        bool TryEmitCollectionExpressionFromNewArray(ILAstExpression newArray)
        {
            if (newArray.Arguments.Count == 0 || !TryGetConstantIndex(newArray.Arguments[0], out int size))
                return false;
            if (!_arrayInitValues.TryGetValue(newArray.Offset, out var initElements))
                return size == 0 && EmitEmptyCollectionExpression();

            _sb.Append('[');
            for (int i = 0; i < size; i++)
            {
                if (i > 0) _sb.Append(", ");
                if (initElements.TryGetValue(i, out var element))
                    EmitExpression(element);
                else
                    _sb.Append("default");
            }
            _sb.Append(']');
            return true;
        }

        bool EmitEmptyCollectionExpression()
        {
            _sb.Append("[]");
            return true;
        }

        static bool IsSpanOrReadOnlySpanType(string? typeName)
        {
            if (typeName is null)
                return false;
            return typeName.StartsWith("System.Span<", StringComparison.Ordinal)
                || typeName.StartsWith("Span<", StringComparison.Ordinal)
                || typeName.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal)
                || typeName.StartsWith("ReadOnlySpan<", StringComparison.Ordinal);
        }

        bool TryEmitPointerDereference(ILAstExpression expr)
        {
            if (expr.Arguments.Count != 1)
                return false;

            var address = expr.Arguments[0];
            if (address.OpCode != ILOpCode.Conv_u || address.Arguments.Count != 1)
                return false;
            if (!IsAddressExpression(address.Arguments[0]))
                return false;

            _sb.Append("*(");
            EmitAddressExpression(address.Arguments[0]);
            _sb.Append(')');
            return true;
        }

        static bool IsAddressExpression(ILAstExpression expr) =>
            expr.OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca
                or ILOpCode.Ldarga_s or ILOpCode.Ldarga
                or ILOpCode.Ldflda or ILOpCode.Ldsflda
                or ILOpCode.Ldelema;

        static bool IsPointerType(string? typeName) =>
            typeName is not null && typeName.EndsWith('*');

        void EmitAddressExpression(ILAstExpression expr)
        {
            _sb.Append('&');
            switch (expr.OpCode)
            {
                case ILOpCode.Ldloca_s or ILOpCode.Ldloca:
                    _sb.Append(expr.Operand ?? "loc");
                    break;
                case ILOpCode.Ldarga_s or ILOpCode.Ldarga:
                    _sb.Append(RemapArg(expr.Operand, expr.OpCode));
                    break;
                case ILOpCode.Ldflda:
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('.');
                    }
                    _sb.Append(ExtractMemberName(expr.Operand));
                    break;
                case ILOpCode.Ldsflda:
                    _sb.Append(expr.Operand ?? "/* field */");
                    break;
                case ILOpCode.Ldelema:
                    if (expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append(']');
                    }
                    else
                    {
                        _sb.Append("/* array element */");
                    }
                    break;
                default:
                    EmitExpression(expr);
                    break;
            }
        }

        void EmitCallArgument(ILAstExpression arg)
        {
            var modifier = arg.ExpectedArgumentModifier;
            if (modifier == CallArgumentModifier.None && IsAddressExpression(arg))
                modifier = CallArgumentModifier.Ref;

            if (modifier == CallArgumentModifier.None)
            {
                EmitExpression(arg);
                return;
            }

            _sb.Append(modifier switch
            {
                CallArgumentModifier.In => "in ",
                CallArgumentModifier.Out => "out ",
                _ => "ref "
            });

            if (IsAddressExpression(arg))
                EmitByRefArgumentTarget(arg);
            else
                EmitExpression(arg);
        }

        void EmitByRefArgumentTarget(ILAstExpression expr)
        {
            switch (expr.OpCode)
            {
                case ILOpCode.Ldloca_s or ILOpCode.Ldloca:
                    _sb.Append(expr.Operand ?? "loc");
                    break;
                case ILOpCode.Ldarga_s or ILOpCode.Ldarga:
                    _sb.Append(RemapArg(expr.Operand, expr.OpCode));
                    break;
                case ILOpCode.Ldflda:
                    if (expr.Arguments.Count > 0)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('.');
                    }
                    _sb.Append(ExtractMemberName(expr.Operand));
                    break;
                case ILOpCode.Ldsflda:
                    _sb.Append(expr.Operand ?? "/* field */");
                    break;
                case ILOpCode.Ldelema:
                    if (expr.Arguments.Count >= 2)
                    {
                        EmitExpression(expr.Arguments[0]);
                        _sb.Append('[');
                        EmitExpression(expr.Arguments[1]);
                        _sb.Append(']');
                    }
                    else
                    {
                        _sb.Append("/* array element */");
                    }
                    break;
                default:
                    EmitExpression(expr);
                    break;
            }
        }

        void EmitReceiver(ILAstExpression receiver, bool isBaseCall)
        {
            if (isBaseCall)
                _sb.Append("base");
            else if (IsRuntimeAwaitExpression(receiver))
            {
                // `(await GetThingAsync()).Member` — the await result is the receiver, so it
                // must be parenthesized; `await GetThingAsync().Member` would bind differently.
                _sb.Append('(');
                EmitExpression(receiver);
                _sb.Append(')');
            }
            else
                EmitExpression(receiver);
        }

        /// <summary>
        /// True for a runtime-async await helper call: a static call to
        /// <c>System.Runtime.CompilerServices.AsyncHelpers.Await</c> with a single awaitable
        /// argument. Covers every overload (Task/ValueTask, configured, and the generic
        /// value-returning forms) because the operand carries no parameter or generic-arg text.
        /// </summary>
        static bool IsRuntimeAwaitCall(ILAstExpression expr) =>
            expr.IsStaticCall
            && expr.Arguments.Count == 1
            && expr.Operand is "System.Runtime.CompilerServices.AsyncHelpers::Await";

        bool IsRuntimeCustomAwaitGetResultCall(ILAstExpression expr)
            => expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && !expr.IsStaticCall
                && expr.Operand is string operand
                && ExtractMemberName(operand) == "GetResult"
                && expr.Arguments.Count > 0
                && GetLocalReferenceName(expr.Arguments[0]) is { } awaiterVar
                && _runtimeCustomAwaitSources.ContainsKey(awaiterVar);

        bool IsRuntimeAwaitExpression(ILAstExpression expr)
            => IsRuntimeAwaitCall(expr) || IsRuntimeCustomAwaitGetResultCall(expr);

        void EmitCallExpression(ILAstExpression expr)
        {
            string? methodName = expr.Operand;
            if (methodName is null)
            {
                _sb.Append("/* call */");
                return;
            }

            // Runtime async (.NET 11+ "async v2"): the compiler lowers `await x` to a call
            // to System.Runtime.CompilerServices.AsyncHelpers.Await(x) instead of emitting a
            // state machine. Render it back as `await x` for a faithful, readable view.
            if (IsRuntimeAwaitCall(expr))
            {
                _sb.Append("await ");
                var awaited = expr.Arguments[0];
                // The await operand binds as a unary prefix; parenthesize lower-precedence
                // operands (binary expressions) so `await a + b` doesn't read as `(await a) + b`.
                bool wrap = IsBinaryOp(awaited.OpCode);
                if (wrap) _sb.Append('(');
                EmitExpression(awaited);
                if (wrap) _sb.Append(')');
                return;
            }

            if (IsRuntimeCustomAwaitGetResultCall(expr)
                && GetLocalReferenceName(expr.Arguments[0]) is { } awaiterVar
                && _runtimeCustomAwaitSources.TryGetValue(awaiterVar, out var awaitedExpression))
            {
                EmitAwaitExpression(awaitedExpression);
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

            if (TryEmitInlineArrayAsSpanExpression(expr, memberPart))
                return;

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

            // Base/chaining constructor call: call .ctor on this → base(args)
            if (memberPart == ".ctor" && !expr.IsStaticCall && expr.Arguments.Count > 0
                && expr.Arguments[0].OpCode is ILOpCode.Ldarg_0)
            {
                _sb.Append("base(");
                for (int i = 1; i < expr.Arguments.Count; i++)
                {
                    if (i > 1) _sb.Append(", ");
                    EmitCallArgument(expr.Arguments[i]);
                }
                _sb.Append(')');
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
                    var simplifiedType = SimplifyTypeName(typePart);

                    // Extension method syntax: arg0.Method(arg1, arg2, ...)
                    if (expr.Arguments.Count >= 1 && IsLikelyExtensionMethodType(simplifiedType))
                    {
                        EmitCallArgument(expr.Arguments[0]);
                        _sb.Append($".{memberPart}(");
                        for (int i = 1; i < expr.Arguments.Count; i++)
                        {
                            if (i > 1) _sb.Append(", ");
                            EmitCallArgument(expr.Arguments[i]);
                        }
                        _sb.Append(')');
                    }
                    else
                    {
                        // Static call: TypeName.Method(args)
                        _sb.Append($"{simplifiedType}.{memberPart}(");
                        for (int i = 0; i < expr.Arguments.Count; i++)
                        {
                            if (i > 0) _sb.Append(", ");
                            EmitCallArgument(expr.Arguments[i]);
                        }
                        _sb.Append(')');
                    }
                }
            }
            else if (!isStatic && expr.Arguments.Count > 0)
            {
                // Instance call: receiver.Method(args) or receiver?.Method(args)
                bool isNullConditionalCall = _nullConditionalReceiver is not null
                    && expr.Arguments[0] is { OpCode: ILOpCode.Nop, Operand: { } op }
                    && op.StartsWith("S_in_", StringComparison.Ordinal);

                // Base call: non-virtual call on 'this' → base.Method()
                bool isBaseCall = expr.OpCode is ILOpCode.Call
                    && _hasThis
                    && expr.Arguments[0].OpCode is ILOpCode.Ldarg_0
                    && memberPart != ".ctor";
                string dot = isNullConditionalCall ? "?." : ".";

                // Indexer getter: get_Item(key)/get_Chars(index) → [key]
                if (memberPart is "get_Item" or "get_Chars" && expr.Arguments.Count == 2)
                {
                    EmitReceiver(expr.Arguments[0], isBaseCall);
                    _sb.Append('[');
                    EmitCallArgument(expr.Arguments[1]);
                    _sb.Append(']');
                }
                // Indexer setter: set_Item(key, value) → [key] = value
                else if (memberPart == "set_Item" && expr.Arguments.Count == 3)
                {
                    EmitReceiver(expr.Arguments[0], isBaseCall);
                    _sb.Append('[');
                    EmitCallArgument(expr.Arguments[1]);
                    _sb.Append("] = ");
                    EmitCallArgument(expr.Arguments[2]);
                }
                // Property getter sugar: get_XXX() → .XXX
                else if (memberPart.StartsWith("get_", StringComparison.Ordinal) && expr.Arguments.Count == 1)
                {
                    EmitReceiver(expr.Arguments[0], isBaseCall);
                    _sb.Append($"{dot}{memberPart[4..]}");
                }
                // Property setter sugar: set_XXX(value) → .XXX = value
                else if (memberPart.StartsWith("set_", StringComparison.Ordinal) && expr.Arguments.Count == 2)
                {
                    EmitReceiver(expr.Arguments[0], isBaseCall);
                    _sb.Append($"{dot}{memberPart[4..]} = ");
                    EmitCallArgument(expr.Arguments[1]);
                }
                else
                {
                    EmitReceiver(expr.Arguments[0], isBaseCall);
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

        void EmitAwaitExpression(ILAstExpression awaited)
        {
            _sb.Append("await ");
            bool wrap = IsBinaryOp(awaited.OpCode);
            if (wrap) _sb.Append('(');
            EmitExpression(awaited);
            if (wrap) _sb.Append(')');
        }

        bool TryEmitInlineArrayAsSpanExpression(ILAstExpression expr, string memberPart)
        {
            if (memberPart is not ("InlineArrayAsReadOnlySpan" or "InlineArrayAsSpan"))
                return false;
            if (expr.Arguments.Count < 2)
                return false;
            if (GetLocalReferenceName(expr.Arguments[0]) is not { } inlineArrayLocal)
                return false;
            if (!TryGetConstantIndex(expr.Arguments[1], out int length))
                return false;
            if (!_inlineArrayInitValues.TryGetValue(inlineArrayLocal, out var elements))
                return false;

            _sb.Append('[');
            for (int i = 0; i < length; i++)
            {
                if (i > 0) _sb.Append(", ");
                if (elements.TryGetValue(i, out var element))
                    EmitExpression(element);
                else
                    _sb.Append("default");
            }
            _sb.Append(']');
            return true;
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
                // bne.un is the standard inequality branch: plain != for
                // integers/references, and C#'s != already has unordered
                // semantics for floats.
                case ILOpCode.Bne_un or ILOpCode.Bne_un_s:
                    EmitBinaryCondition(expr, "!="); break;
                case ILOpCode.Bge or ILOpCode.Bge_s:
                    EmitBinaryCondition(expr, ">="); break;
                case ILOpCode.Bge_un or ILOpCode.Bge_un_s:
                    EmitUnsignedComparison(expr, ">=", "<"); break;
                case ILOpCode.Bgt or ILOpCode.Bgt_s:
                    EmitBinaryCondition(expr, ">"); break;
                case ILOpCode.Bgt_un or ILOpCode.Bgt_un_s:
                    EmitUnsignedComparison(expr, ">", "<="); break;
                case ILOpCode.Ble or ILOpCode.Ble_s:
                    EmitBinaryCondition(expr, "<="); break;
                case ILOpCode.Ble_un or ILOpCode.Ble_un_s:
                    EmitUnsignedComparison(expr, "<=", ">"); break;
                case ILOpCode.Blt or ILOpCode.Blt_s:
                    EmitBinaryCondition(expr, "<"); break;
                case ILOpCode.Blt_un or ILOpCode.Blt_un_s:
                    EmitUnsignedComparison(expr, "<", ">="); break;
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
        /// Handle ceq(comparison, 0) → negated comparison.
        /// E.g., ceq(clt(value, 0), 0) → value >= 0 instead of (value &lt; 0) == 0.
        /// </summary>
        bool TryEmitNegatedComparisonZero(ILAstExpression ceqExpr)
        {
            if (ceqExpr.Arguments.Count < 2) return false;

            var lhs = ceqExpr.Arguments[0];
            var rhs = ceqExpr.Arguments[1];

            ILAstExpression? comparison = null;
            if (IsZeroLiteral(rhs) && IsComparisonOp(lhs))
                comparison = lhs;
            else if (IsZeroLiteral(lhs) && IsComparisonOp(rhs))
                comparison = rhs;

            if (comparison is null || comparison.Arguments.Count < 2) return false;

            // Negating an unordered compare yields the ordered complement
            // (with NaN semantics and unsigned casts handled there).
            if (comparison.OpCode == ILOpCode.Clt_un)
            {
                EmitNegatedUnsignedComparison(comparison, ">=");
                return true;
            }
            if (comparison.OpCode == ILOpCode.Cgt_un)
            {
                EmitNegatedUnsignedComparison(comparison, "<=");
                return true;
            }

            string negatedOp = comparison.OpCode switch
            {
                ILOpCode.Clt => ">=",
                ILOpCode.Cgt => "<=",
                ILOpCode.Ceq => "!=",
                _ => ""
            };
            if (negatedOp == "") return false;

            EmitExpression(comparison.Arguments[0]);
            _sb.Append($" {negatedOp} ");
            EmitExpression(comparison.Arguments[1]);
            return true;
        }

        static bool IsZeroLiteral(ILAstExpression expr) =>
            expr.OpCode is ILOpCode.Ldc_i4_0;

        static bool IsComparisonOp(ILAstExpression expr) =>
            expr.OpCode is ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Clt
                or ILOpCode.Cgt_un or ILOpCode.Clt_un;

        /// <summary>
        static bool IsBoolContext(ILAstExpression expr, bool parentBoolContext)
        {
            if (parentBoolContext)
                return true;
            var tn = expr.ResultType.TypeName;
            if (tn is "bool" or "System.Boolean" or "Boolean")
                return true;
            return false;
        }

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

        /// <summary>
        /// Renders the unsigned/unordered comparison opcodes (cgt.un, clt.un, and
        /// the b*.un branches). Their semantics differ from the signed forms:
        /// for floats they mean "compare or unordered" — the negation of the
        /// complementary ordered compare (cgt.un(a,b) == !(a &lt;= b), which differs
        /// from a &gt; b when either operand is NaN); for integers they compare the
        /// bits as unsigned, which C# expresses by casting both operands (the
        /// classic bounds-check (uint)i &lt; (uint)length). cgt.un against null is
        /// the compiler's reference-inequality idiom.
        /// </summary>
        void EmitUnsignedComparison(ILAstExpression expr, string op, string orderedComplement)
        {
            if (expr.Arguments.Count < 2)
            {
                EmitBinary(expr, op);
                return;
            }

            var left = expr.Arguments[0];
            var right = expr.Arguments[1];

            if (op == ">" && right.OpCode == ILOpCode.Ldnull)
            {
                EmitExpression(left);
                _sb.Append(" != null");
                return;
            }

            switch (ComparisonKind(left, right))
            {
                case StackValueKind.Float:
                    _sb.Append("!(");
                    EmitExpression(left);
                    _sb.Append($" {orderedComplement} ");
                    EmitExpression(right);
                    _sb.Append(')');
                    break;
                case StackValueKind.Int64:
                    EmitUnsignedBinary(expr, op, "(ulong)", ILOpCode.Conv_u8);
                    break;
                case StackValueKind.NativeInt:
                    EmitUnsignedBinary(expr, op, "(nuint)", ILOpCode.Conv_u);
                    break;
                case StackValueKind.Int32:
                    EmitUnsignedBinary(expr, op, "(uint)", ILOpCode.Conv_u4);
                    break;
                default:
                    // ObjRef/ByRef/unknown — pointer-style comparison; the raw
                    // operator is the closest C# rendering.
                    EmitBinary(expr, op);
                    break;
            }
        }

        /// <summary>
        /// Negated form (via ceq(cmp, 0)) of an unsigned/unordered comparison.
        /// Note the asymmetry with <see cref="EmitUnsignedComparison"/>: negating
        /// an unordered compare yields the ORDERED complement — !cgt.un(a,b) is
        /// exactly a &lt;= b, including NaN behavior.
        /// </summary>
        void EmitNegatedUnsignedComparison(ILAstExpression comparison, string orderedOp)
        {
            var left = comparison.Arguments[0];
            var right = comparison.Arguments[1];

            // !(x != null) → x == null
            if (orderedOp == "<=" && right.OpCode == ILOpCode.Ldnull)
            {
                EmitExpression(left);
                _sb.Append(" == null");
                return;
            }

            switch (ComparisonKind(left, right))
            {
                case StackValueKind.Float:
                    EmitExpression(left);
                    _sb.Append($" {orderedOp} ");
                    EmitExpression(right);
                    break;
                case StackValueKind.Int64:
                    EmitUnsignedBinary(comparison, orderedOp, "(ulong)", ILOpCode.Conv_u8);
                    break;
                case StackValueKind.NativeInt:
                    EmitUnsignedBinary(comparison, orderedOp, "(nuint)", ILOpCode.Conv_u);
                    break;
                case StackValueKind.Int32:
                    EmitUnsignedBinary(comparison, orderedOp, "(uint)", ILOpCode.Conv_u4);
                    break;
                default:
                    EmitBinary(comparison, orderedOp);
                    break;
            }
        }

        /// <summary>div.un/rem.un: unsigned arithmetic, rendered with operand casts.</summary>
        void EmitUnsignedArithmetic(ILAstExpression expr, string op)
        {
            if (expr.Arguments.Count < 2)
            {
                EmitBinary(expr, op);
                return;
            }

            switch (ComparisonKind(expr.Arguments[0], expr.Arguments[1]))
            {
                case StackValueKind.Int64:
                    EmitUnsignedBinary(expr, op, "(ulong)", ILOpCode.Conv_u8);
                    break;
                case StackValueKind.NativeInt:
                    EmitUnsignedBinary(expr, op, "(nuint)", ILOpCode.Conv_u);
                    break;
                default:
                    EmitUnsignedBinary(expr, op, "(uint)", ILOpCode.Conv_u4);
                    break;
            }
        }

        void EmitUnsignedBinary(ILAstExpression expr, string op, string cast, ILOpCode redundantConv)
        {
            EmitUnsignedOperand(expr.Arguments[0], cast, redundantConv);
            _sb.Append($" {op} ");
            EmitUnsignedOperand(expr.Arguments[1], cast, redundantConv);
        }

        void EmitUnsignedOperand(ILAstExpression arg, string cast, ILOpCode redundantConv)
        {
            // Non-negative constants are unchanged by unsigned reinterpretation,
            // and an operand that is itself the matching conversion already
            // renders the cast.
            if ((IsLdcI4(arg.OpCode) && GetI4Value(arg) >= 0) || arg.OpCode == redundantConv)
            {
                EmitExpression(arg);
                return;
            }

            _sb.Append(cast);
            bool wrap = IsBinaryOp(arg.OpCode);
            if (wrap) _sb.Append('(');
            EmitExpression(arg);
            if (wrap) _sb.Append(')');
        }

        /// <summary>Joint stack-value kind of a comparison's operands.</summary>
        static StackValueKind ComparisonKind(ILAstExpression left, ILAstExpression right)
        {
            var a = left.ResultType.Kind;
            var b = right.ResultType.Kind;
            if (a == StackValueKind.Float || b == StackValueKind.Float) return StackValueKind.Float;
            if (a == StackValueKind.ObjRef || b == StackValueKind.ObjRef) return StackValueKind.ObjRef;
            if (a == StackValueKind.ByRef || b == StackValueKind.ByRef) return StackValueKind.ByRef;
            if (a == StackValueKind.Int64 || b == StackValueKind.Int64) return StackValueKind.Int64;
            if (a == StackValueKind.NativeInt || b == StackValueKind.NativeInt) return StackValueKind.NativeInt;
            if (a == StackValueKind.Int32 || b == StackValueKind.Int32) return StackValueKind.Int32;
            return StackValueKind.Unknown;
        }

        // --- Helpers ---

        void EmitBinary(ILAstExpression expr, string op)
        {
            if (expr.Arguments.Count >= 2)
            {
                if (TryFoldBinary(expr, op, out var foldedExpr) && foldedExpr is { })
                {
                    // Emit folded expression through parenthesization logic
                    // to preserve required parentheses for parent context
                    EmitParenthesized(expr, -1, foldedExpr);
                    return;
                }

                EmitParenthesized(expr, 0);
                _sb.Append($" {op} ");
                EmitParenthesized(expr, 1);
            }
            else
            {
                _sb.Append($"/* {op} */");
            }
        }

        bool TryFoldBinary(ILAstExpression expr, string op, out ILAstExpression? foldedExpr)
        {
            foldedExpr = null;
            if (expr.Arguments.Count < 2) return false;

            var lhs = expr.Arguments[0];
            var rhs = expr.Arguments[1];

            if (op == "+" && IsZero(rhs) && !IsSideEffecting(lhs))
            {
                foldedExpr = lhs;
                return true;
            }
            if (op == "+" && IsZero(lhs) && !IsSideEffecting(rhs))
            {
                foldedExpr = rhs;
                return true;
            }
            if (op == "-" && IsZero(rhs) && !IsSideEffecting(lhs))
            {
                foldedExpr = lhs;
                return true;
            }
            if (op == "*" && IsOne(rhs) && !IsSideEffecting(lhs))
            {
                foldedExpr = lhs;
                return true;
            }
            if (op == "*" && IsOne(lhs) && !IsSideEffecting(rhs))
            {
                foldedExpr = rhs;
                return true;
            }

            return false;
        }

        static bool IsZero(ILAstExpression expr) => expr.OpCode switch
        {
            ILOpCode.Ldc_i4_0 => true,
            ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => expr.Operand is "0",
            ILOpCode.Ldc_i8 => expr.Operand is "0",
            _ => false
        };

        static bool IsOne(ILAstExpression expr) => expr.OpCode switch
        {
            ILOpCode.Ldc_i4_1 => true,
            ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => expr.Operand is "1",
            _ => false
        };

        static bool IsSideEffecting(ILAstExpression expr)
        {
            if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli)
                return true;
            foreach (var arg in expr.Arguments)
                if (IsSideEffecting(arg))
                    return true;
            return false;
        }

        void EmitParenthesized(ILAstExpression parent, int argIndex)
        {
            if (argIndex >= parent.Arguments.Count) return;
            var arg = parent.Arguments[argIndex];
            bool needsParens = NeedsParenthesesInContext(arg, parent);
            if (needsParens) _sb.Append('(');
            EmitExpression(arg);
            if (needsParens) _sb.Append(')');
        }

        void EmitParenthesized(ILAstExpression parent, int argIndex, ILAstExpression foldedExpr)
        {
            // Used when folding to emit the folded expression with proper parent context
            bool needsParens = argIndex < 0 || NeedsParenthesesInContext(foldedExpr, parent);
            if (needsParens) _sb.Append('(');
            EmitExpression(foldedExpr);
            if (needsParens) _sb.Append(')');
        }

        static bool NeedsParenthesesInContext(ILAstExpression arg, ILAstExpression parent)
        {
            if (!IsBinaryOp(arg.OpCode))
                return false;

            int parentPrec = GetPrecedence(parent.OpCode);
            int argPrec = GetPrecedence(arg.OpCode);

            if (argPrec < parentPrec)
                return true;

            if (argPrec == parentPrec)
            {
                if (parent.OpCode == ILOpCode.Sub || parent.OpCode == ILOpCode.Div
                    || parent.OpCode == ILOpCode.Rem)
                {
                    if (arg.OpCode == parent.OpCode)
                        return true;
                }
                if (parent.OpCode == ILOpCode.Shl || parent.OpCode == ILOpCode.Shr)
                    return true;
            }

            if (arg.OpCode == ILOpCode.Sub)
                return true;

            return false;
        }

        void EmitCast(ILAstExpression expr, string typeName)
        {
            _sb.Append($"({typeName})");
            EmitParenthesized(expr, 0);
        }

        void EmitCheckedBinary(ILAstExpression expr, string op)
        {
            _sb.Append("checked(");
            EmitBinary(expr, op);
            _sb.Append(')');
        }

        void EmitCheckedCast(ILAstExpression expr, string typeName)
        {
            _sb.Append("checked(");
            EmitCast(expr, typeName);
            _sb.Append(')');
        }

        string PreferredUInt16Type(string? resolvedType)
            => resolvedType is "char" or "System.Char" ? "char" : "ushort";

        void WriteIndent(int indent)
        {
            for (int i = 0; i < indent; i++)
                _sb.Append("    ");
        }

        static bool IsLdcI4(ILOpCode op) => op is
            ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or
            ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or
            ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or
            ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4;

        static int GetI4Value(ILAstExpression expr) => expr.OpCode switch
        {
            ILOpCode.Ldc_i4_m1 => -1,
            ILOpCode.Ldc_i4_0 => 0,
            ILOpCode.Ldc_i4_1 => 1,
            ILOpCode.Ldc_i4_2 => 2,
            ILOpCode.Ldc_i4_3 => 3,
            ILOpCode.Ldc_i4_4 => 4,
            ILOpCode.Ldc_i4_5 => 5,
            ILOpCode.Ldc_i4_6 => 6,
            ILOpCode.Ldc_i4_7 => 7,
            ILOpCode.Ldc_i4_8 => 8,
            _ => int.TryParse(expr.Operand, out int v) ? v : 0
        };

        readonly Dictionary<string, Dictionary<int, string>?> _enumCache = [];

        bool TryResolveEnumName(string typeName, int value, out string? result)
        {
            result = null;
            if (_reader is null) return false;

            if (!_enumCache.TryGetValue(typeName, out var valueMap))
            {
                valueMap = BuildEnumValueMap(typeName);
                _enumCache[typeName] = valueMap;
            }

            if (valueMap is null) return false;
            return valueMap.TryGetValue(value, out result);
        }

        string? TryResolveFieldType(string? qualifiedName)
        {
            if (_reader is null || string.IsNullOrEmpty(qualifiedName))
                return null;

            int sep = qualifiedName.LastIndexOf("::", StringComparison.Ordinal);
            if (sep <= 0 || sep >= qualifiedName.Length - 2)
                return null;

            string typeName = qualifiedName[..sep];
            string fieldName = qualifiedName[(sep + 2)..];

            foreach (var typeDefHandle in _reader.TypeDefinitions)
            {
                var typeDef = _reader.GetTypeDefinition(typeDefHandle);
                if (_reader.GetFullTypeName(typeDef) != typeName)
                    continue;

                foreach (var fieldHandle in typeDef.GetFields())
                {
                    var field = _reader.GetFieldDefinition(fieldHandle);
                    if (_reader.GetString(field.Name) != fieldName)
                        continue;

                    try
                    {
                        return field.DecodeSignature(SignatureDecoder.Instance, null);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        bool TryFormatTokenExpression(ILAstExpression expr, string tokenOp, out string? tokenExpr)
        {
            tokenExpr = null;
            if (!tokenOp.Contains("::", StringComparison.Ordinal))
                return false;

            int sep = tokenOp.LastIndexOf("::", StringComparison.Ordinal);
            if (sep <= 0 || sep >= tokenOp.Length - 2)
                return false;

            string memberRef = $"{SimplifyTypeName(tokenOp[..sep])}.{tokenOp[(sep + 2)..]}";
            string? handleType = expr.ResultType.TypeName;

            if (handleType is "System.RuntimeMethodHandle")
            {
                tokenExpr = $"__methodref({memberRef})";
                return true;
            }

            if (handleType is "System.RuntimeFieldHandle")
            {
                tokenExpr = $"__fieldref({memberRef})";
                return true;
            }

            return false;
        }

        Dictionary<int, string>? BuildEnumValueMap(string typeName)
        {
            if (_reader is null) return null;

            foreach (var typeDefHandle in _reader.TypeDefinitions)
            {
                var typeDef = _reader.GetTypeDefinition(typeDefHandle);
                if (_reader.GetFullTypeName(typeDef) != typeName)
                    continue;

                var map = new Dictionary<int, string>();
                string shortType = SimplifyTypeName(typeName);

                foreach (var fieldHandle in typeDef.GetFields())
                {
                    var field = _reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & (System.Reflection.FieldAttributes.Literal | System.Reflection.FieldAttributes.Static))
                        != (System.Reflection.FieldAttributes.Literal | System.Reflection.FieldAttributes.Static))
                        continue;

                    try
                    {
                        var constant = _reader.GetConstant(field.GetDefaultValue());
                        var blob = _reader.GetBlobReader(constant.Value);
                        int fieldValue = constant.TypeCode switch
                        {
                            ConstantTypeCode.Int32 => blob.ReadInt32(),
                            ConstantTypeCode.UInt32 => unchecked((int)blob.ReadUInt32()),
                            ConstantTypeCode.Int16 => blob.ReadInt16(),
                            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                            ConstantTypeCode.Byte => blob.ReadByte(),
                            ConstantTypeCode.SByte => blob.ReadSByte(),
                            _ => int.MinValue
                        };
                        if (fieldValue != int.MinValue)
                            map.TryAdd(fieldValue, $"{shortType}.{_reader.GetString(field.Name)}");
                    }
                    catch { }
                }

                return map.Count > 0 ? map : null;
            }

            return null;
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
            var tempCtx = new EmitterContext(_ast, _structure, sb, _reader, _hasThis, _returnsBool ? "bool" : null, _paramNames);
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
            var tempCtx = new EmitterContext(_ast, _structure, sb, _reader, _hasThis, _returnsBool ? "bool" : null, _paramNames);
            tempCtx.EmitBranchCondition(branchExpr);
            return sb.ToString();
        }

        static string NegateConditionString(string condition)
        {
            // A fully-wrapped negation strips exactly. This must run before the
            // operator flips: float unordered forms like !(a <= b) negate to the
            // ordered a <= b, not to !(a > b) (which differs under NaN).
            if (condition.StartsWith("!(", StringComparison.Ordinal)
                && condition.EndsWith(')')
                && ParenWrapsWholeString(condition))
            {
                return condition[2..^1];
            }

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
            if (condition.StartsWith('!'))
                return condition[1..];
            return $"!{condition}";
        }

        /// <summary>
        /// True when the parenthesis opened at index 1 closes at the final
        /// character — i.e. <c>!(...)</c> wraps the whole condition rather than
        /// just its first term (<c>!(a) &amp;&amp; b</c>).
        /// </summary>
        static bool ParenWrapsWholeString(string condition)
        {
            int depth = 0;
            for (int i = 1; i < condition.Length; i++)
            {
                if (condition[i] == '(')
                {
                    depth++;
                }
                else if (condition[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                        return i == condition.Length - 1;
                }
            }
            return false;
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
                // Binary comparison branch — reconstruct as comparison expression.
                // bge/ble/bne have no c* counterpart, so they ride on Ceq with the
                // operator (or ">=u"/"<=u" unsigned marker) in Operand, which the
                // Ceq emit case honors.
                return new ILAstExpression
                {
                    OpCode = branchExpr.OpCode switch
                    {
                        ILOpCode.Bgt or ILOpCode.Bgt_s => ILOpCode.Cgt,
                        ILOpCode.Bgt_un or ILOpCode.Bgt_un_s => ILOpCode.Cgt_un,
                        ILOpCode.Blt or ILOpCode.Blt_s => ILOpCode.Clt,
                        ILOpCode.Blt_un or ILOpCode.Blt_un_s => ILOpCode.Clt_un,
                        _ => ILOpCode.Ceq
                    },
                    Operand = branchExpr.OpCode switch
                    {
                        ILOpCode.Bne_un or ILOpCode.Bne_un_s => "!=",
                        ILOpCode.Bge or ILOpCode.Bge_s => ">=",
                        ILOpCode.Bge_un or ILOpCode.Bge_un_s => ">=u",
                        ILOpCode.Ble or ILOpCode.Ble_s => "<=",
                        ILOpCode.Ble_un or ILOpCode.Ble_un_s => "<=u",
                        _ => null
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
                        or ILOpCode.Ret or ILOpCode.Br or ILOpCode.Br_s;
                }
            }

            // Check children recursively (e.g., sequence ending with throw)
            if (block.Children.Count > 0)
                return BlockEndsWithNoFallthrough(block.Children[^1]);

            return false;
        }

        static bool IsBinaryOp(ILOpCode op) => op switch
        {
            ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or
            ILOpCode.Rem or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or
            ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Ceq or ILOpCode.Cgt or
            ILOpCode.Clt or ILOpCode.Cgt_un or ILOpCode.Clt_un => true,
            _ => false
        };

        static int GetPrecedence(ILOpCode op) => op switch
        {
            ILOpCode.Or => 1,
            ILOpCode.Xor => 2,
            ILOpCode.And => 3,
            ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Clt or
            ILOpCode.Cgt_un or ILOpCode.Clt_un => 4,
            ILOpCode.Shl or ILOpCode.Shr => 5,
            ILOpCode.Add or ILOpCode.Sub => 6,
            ILOpCode.Mul or ILOpCode.Div or ILOpCode.Rem => 7,
            _ => 8
        };

        /// <summary>
        /// Detect the pattern <c>varName = varName op expr</c> and emit as compound assignment.
        /// Returns true if compound assignment was emitted. Handles:
        /// <c>x = x + 1</c> → <c>x++</c>, <c>x = x - 1</c> → <c>x--</c>,
        /// <c>x = x + e</c> → <c>x += e</c>, etc.
        /// </summary>
        bool TryEmitCompoundAssignment(string varName, ILAstExpression valueExpr, int indent)
        {
            if (valueExpr.Arguments.Count < 2)
                return false;

            string? opSymbol = valueExpr.OpCode switch
            {
                ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un => "+",
                ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un => "-",
                ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un => "*",
                ILOpCode.Div => "/",
                ILOpCode.Rem => "%",
                ILOpCode.And => "&",
                ILOpCode.Or => "|",
                ILOpCode.Xor => "^",
                ILOpCode.Shl => "<<",
                ILOpCode.Shr => ">>",
                ILOpCode.Shr_un => ">>>",
                _ => null
            };
            if (opSymbol is null) return false;

            var lhs = valueExpr.Arguments[0];
            if (!IsLoadOf(lhs, varName)) return false;

            var rhs = valueExpr.Arguments[1];

            // x = x + 1 → x++ / x = x - 1 → x--
            if (opSymbol is "+" or "-" && IsConstantOne(rhs))
            {
                WriteIndent(indent);
                _sb.Append(varName);
                _sb.AppendLine(opSymbol == "+" ? "++;" : "--;");
                return true;
            }

            // x = x op expr → x op= expr
            WriteIndent(indent);
            _sb.Append($"{varName} {opSymbol}= ");
            EmitExpression(rhs);
            _sb.AppendLine(";");
            return true;
        }

        /// <summary>
        /// Format a compound assignment as a string (for for-loop increment clauses).
        /// Returns null if not a compound assignment pattern.
        /// </summary>
        string? FormatCompoundAssignment(string varName, ILAstExpression valueExpr)
        {
            if (valueExpr.Arguments.Count < 2)
                return null;

            string? opSymbol = valueExpr.OpCode switch
            {
                ILOpCode.Add or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un => "+",
                ILOpCode.Sub or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un => "-",
                ILOpCode.Mul or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un => "*",
                ILOpCode.Div => "/",
                ILOpCode.Rem => "%",
                ILOpCode.And => "&",
                ILOpCode.Or => "|",
                ILOpCode.Xor => "^",
                ILOpCode.Shl => "<<",
                ILOpCode.Shr => ">>",
                ILOpCode.Shr_un => ">>>",
                _ => null
            };
            if (opSymbol is null) return null;

            var lhs = valueExpr.Arguments[0];
            if (!IsLoadOf(lhs, varName)) return null;

            var rhs = valueExpr.Arguments[1];

            if (opSymbol is "+" or "-" && IsConstantOne(rhs))
                return $"{varName}{(opSymbol == "+" ? "++" : "--")}";

            return $"{varName} {opSymbol}= {ExpressionToString(rhs)}";
        }

        static bool IsLoadOf(ILAstExpression expr, string varName)
        {
            if (expr.Operand == varName) return true;
            string? name = expr.OpCode switch
            {
                ILOpCode.Ldloc_0 => "V_0",
                ILOpCode.Ldloc_1 => "V_1",
                ILOpCode.Ldloc_2 => "V_2",
                ILOpCode.Ldloc_3 => "V_3",
                ILOpCode.Ldloca_s or ILOpCode.Ldloca => expr.Operand,
                _ => null
            };
            return name == varName;
        }

        static bool IsConstantOne(ILAstExpression expr)
        {
            if (expr.OpCode == ILOpCode.Ldc_i4_1) return true;
            if (expr.OpCode is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 && expr.Operand is "1")
                return true;
            return false;
        }

        string RemapArg(string? operand, ILOpCode opcode)
        {
            if (operand is not null && operand.StartsWith("P_")
                && int.TryParse(operand.AsSpan(2), out int idx))
            {
                if (_hasThis)
                {
                    if (idx == 0) return "this";
                    idx--;
                }
                if (_paramNames is not null && idx >= 0 && idx < _paramNames.Count)
                    return _paramNames[idx];
                return $"P_{idx}";
            }
            return operand ?? GetArgName(opcode, _hasThis, _paramNames);
        }

        static string GetArgName(ILOpCode opcode, bool hasThis = false, IReadOnlyList<string>? paramNames = null)
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
            if (paramNames is not null && idx >= 0 && idx < paramNames.Count)
                return paramNames[idx];
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

        static bool IsLikelyExtensionMethodType(string typeName)
        {
            return typeName.EndsWith("Extensions", StringComparison.Ordinal)
                || s_knownExtensionMethodTypes.Contains(typeName);
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
                _ => StripNamespaces(typeName)
            };
        }

        static string StripNamespaces(string typeName)
        {
            // Handle generic types recursively: e.g. System.Collections.Generic.List<System.String>
            int genericStart = typeName.IndexOf('<');
            if (genericStart >= 0 && typeName.EndsWith('>'))
            {
                string outerRaw = typeName[..genericStart];
                string innerArgs = typeName[(genericStart + 1)..^1];

                // Nullable<T> → T?
                if (outerRaw is "System.Nullable" or "Nullable"
                    && !innerArgs.Contains(','))
                    return $"{SimplifyTypeName(innerArgs.Trim())}?";

                string outerType = StripNamespacePrefix(outerRaw);

                // Split generic arguments respecting nested angle brackets
                var args = SplitGenericArguments(innerArgs);
                var simplified = new StringBuilder();
                simplified.Append(outerType);
                simplified.Append('<');
                for (int i = 0; i < args.Count; i++)
                {
                    if (i > 0) simplified.Append(", ");
                    simplified.Append(SimplifyTypeName(args[i].Trim()));
                }
                simplified.Append('>');
                return simplified.ToString();
            }

            return StripNamespacePrefix(typeName);
        }

        static string StripNamespacePrefix(string typeName)
        {
            // Strip common "global using" namespace prefixes (longest first)
            ReadOnlySpan<string> prefixes =
            [
                "System.Collections.Generic.",
                "System.Threading.Tasks.",
                "System.Linq.",
                "System.Text.",
                "System.IO.",
                "System.",
            ];

            foreach (var prefix in prefixes)
            {
                if (typeName.StartsWith(prefix, StringComparison.Ordinal))
                    return typeName[prefix.Length..];
            }

            return typeName;
        }

        static List<string> SplitGenericArguments(string args)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case '<': depth++; break;
                    case '>': depth--; break;
                    case ',' when depth == 0:
                        result.Add(args[start..i]);
                        start = i + 1;
                        break;
                }
            }
            result.Add(args[start..]);
            return result;
        }
    }

    record InterpolationPart(bool IsLiteral, string? LiteralText, ILAstExpression? FormatExpression);
}
