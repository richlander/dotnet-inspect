using System.Reflection.Metadata;

namespace DotnetInspector.Decompiler.Transforms;

/// <summary>
/// Eliminates single-use temporary variables by inlining their assigned
/// expression directly at the use site. Operates on the ILAst before emission.
/// </summary>
sealed class ExpressionInliner : IILAstTransform
{
    public void Run(ILAstMethod method, TransformContext context)
        => context.InlinedLocals.UnionWith(Inline(method));

    /// <summary>
    /// Run inlining passes on <paramref name="method"/>. Returns the set of
    /// local variable names that were successfully inlined (and whose declarations
    /// should be suppressed).
    /// </summary>
    public HashSet<string> Inline(ILAstMethod method)
    {
        HashSet<string> inlinedLocals = [];

        // Pass 1: within-block inlining (single block, store → load adjacency)
        foreach (var block in method.Blocks)
            InlineBlock(block, inlinedLocals);

        // Pass 2: cross-block inlining (exactly 1 def + 1 use across entire method)
        InlineCrossBlock(method, inlinedLocals);

        return inlinedLocals;
    }

    /// <summary>
    /// Within a single block, find stloc patterns where the stored value is used
    /// exactly once in a subsequent node, with no intervening redefinition.
    /// Replace the use with the stored expression and nop-out the store.
    /// </summary>
    static void InlineBlock(ILAstBlock block, HashSet<string> inlinedLocals)
    {
        for (int i = 0; i < block.Nodes.Count; i++)
        {
            // Match: stloc V_x = expr (as ILAstStatement wrapping a stloc expression)
            if (block.Nodes[i] is not ILAstStatement { Expression: var storeExpr })
                continue;

            if (storeExpr.OpCode is not (ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or
                ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc))
                continue;

            string? varName = storeExpr.Operand ?? GetLocalNameFromOp(storeExpr.OpCode);
            if (varName is null || storeExpr.Arguments.Count == 0)
                continue;

            var valueExpr = storeExpr.Arguments[0];

            // Don't inline side-effecting expressions (calls, newobj) — preserves
            // using/foreach pattern detection which requires named variable stores
            if (IsSideEffecting(valueExpr))
                continue;

            // Don't inline self-referential stores like V_0 = V_0 + 1 (mutations)
            if (ExprReferencesVar(valueExpr, varName))
                continue;

            // Moving the evaluation to the use site is only sound if nothing the
            // value reads can change in between: track the locals it loads, and
            // whether it reads mutable state (fields, array elements, indirection)
            // that any call or store could invalidate.
            var localsRead = new HashSet<string>();
            CollectLocalReads(valueExpr, localsRead);
            bool readsMutableState = ReadsMutableState(valueExpr);

            // Scan forward for a single use of this variable before any redefinition
            for (int j = i + 1; j < block.Nodes.Count; j++)
            {
                var candidate = block.Nodes[j];

                // Check if this node redefines the variable — stop scanning
                if (IsStoreToVar(candidate, varName))
                    break;

                // Count references to this variable in the candidate node
                int refCount = CountReferences(candidate, varName);
                if (refCount == 0)
                {
                    // No use here — but does this node invalidate the captured value?
                    if (StoresToAny(candidate, localsRead))
                        break;
                    if (readsMutableState && NodeMutatesOrCalls(candidate))
                        break;
                    continue;
                }
                if (refCount > 1)
                    break; // multiple uses — can't inline

                // The use site itself: anything evaluated BEFORE the reference
                // within this node (earlier call arguments, receivers) must not
                // mutate state the value reads.
                if (readsMutableState && MutatesBeforeFirstRef(candidate, varName))
                    break;

                // Exactly one reference — inline it
                if (ReplaceReference(candidate, varName, valueExpr))
                {
                    // Nop-out the original store by replacing with a nop statement
                    storeExpr = new ILAstExpression { OpCode = ILOpCode.Nop, Offset = storeExpr.Offset };
                    block.Nodes[i] = new ILAstStatement { Expression = storeExpr };
                    inlinedLocals.Add(varName);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Cross-block inlining: for variables with exactly 1 definition and 1 use
    /// across the entire method, inline the definition at the use site.
    /// </summary>
    static void InlineCrossBlock(ILAstMethod method, HashSet<string> inlinedLocals)
    {
        // Count definitions and uses per variable across all blocks
        Dictionary<string, int> defCount = [];
        Dictionary<string, int> useCount = [];
        Dictionary<string, (ILAstBlock block, int nodeIdx, ILAstExpression storeExpr)> defSites = [];

        for (int blockIdx = 0; blockIdx < method.Blocks.Count; blockIdx++)
        {
            var block = method.Blocks[blockIdx];
            for (int nodeIdx = 0; nodeIdx < block.Nodes.Count; nodeIdx++)
            {
                var node = block.Nodes[nodeIdx];

                // Count definitions (stloc statements)
                if (node is ILAstStatement { Expression: var expr }
                    && expr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or
                       ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc)
                {
                    string? vn = expr.Operand ?? GetLocalNameFromOp(expr.OpCode);
                    if (vn is not null)
                    {
                        defCount[vn] = defCount.GetValueOrDefault(vn) + 1;
                        defSites[vn] = (block, nodeIdx, expr);
                    }
                }

                // Count uses (references in expressions)
                CountReferencesInNode(node, useCount);
            }
        }

        // Inline variables with exactly 1 def and 1 use
        foreach (var (varName, defs) in defCount)
        {
            if (defs != 1 || useCount.GetValueOrDefault(varName) != 1)
                continue;
            if (inlinedLocals.Contains(varName))
                continue;
            if (!defSites.TryGetValue(varName, out var site))
                continue;

            var storeExpr = site.storeExpr;
            if (storeExpr.Arguments.Count == 0)
                continue;

            var valueExpr = storeExpr.Arguments[0];

            // Across blocks there is no ordering guarantee between definition and
            // use (the def may sit in a loop, the use after it; nothing here checks
            // dominance), so only position-independent constants are sound to move.
            if (!IsPositionIndependent(valueExpr))
                continue;

            // Don't inline self-referential stores
            if (ExprReferencesVar(valueExpr, varName))
                continue;

            // Find and replace the single use
            bool replaced = false;
            foreach (var block in method.Blocks)
            {
                foreach (var node in block.Nodes)
                {
                    if (ReplaceReference(node, varName, valueExpr))
                    {
                        replaced = true;
                        break;
                    }
                }
                if (replaced) break;
            }

            if (replaced)
            {
                // Nop-out the definition
                var nopExpr = new ILAstExpression { OpCode = ILOpCode.Nop, Offset = storeExpr.Offset };
                site.block.Nodes[site.nodeIdx] = new ILAstStatement { Expression = nopExpr };
                inlinedLocals.Add(varName);
            }
        }
    }

    /// <summary>
    /// Count all references to a variable in a node's expression tree.
    /// Does NOT count the variable in its own definition (stloc operand).
    /// </summary>
    static void CountReferencesInNode(ILAstNode node, Dictionary<string, int> counts)
    {
        switch (node)
        {
            case ILAstStatement { Expression: var expr }:
                // For stloc, count references in the value expression only (not the operand)
                if (expr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                    or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc)
                {
                    foreach (var arg in expr.Arguments)
                        CountExprRefs(arg, counts);
                }
                else
                {
                    CountExprRefs(expr, counts);
                }
                break;
            case ILAstAssignment { Value: var val }:
                CountExprRefs(val, counts);
                break;
        }
    }

    static void CountExprRefs(ILAstExpression expr, Dictionary<string, int> counts)
    {
        if (expr.OpCode is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
            or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc)
        {
            string? vn = expr.Operand ?? GetLocalNameFromOp(expr.OpCode);
            if (vn is not null)
                counts[vn] = counts.GetValueOrDefault(vn) + 1;
        }
        foreach (var arg in expr.Arguments)
            CountExprRefs(arg, counts);
    }

    static int CountReferences(ILAstNode node, string varName)
    {
        return node switch
        {
            ILAstStatement { Expression: var expr } => CountExprRefs(expr, varName),
            ILAstAssignment { Value: var val } => CountExprRefs(val, varName),
            _ => 0
        };
    }

    static int CountExprRefs(ILAstExpression expr, string varName)
    {
        int count = 0;
        if (IsLoadOf(expr, varName))
            count++;
        foreach (var arg in expr.Arguments)
            count += CountExprRefs(arg, varName);
        return count;
    }

    /// <summary>
    /// Replace the first reference to <paramref name="varName"/> in a node
    /// with <paramref name="valueExpr"/>. Returns true if replaced.
    /// </summary>
    static bool ReplaceReference(ILAstNode node, string varName, ILAstExpression valueExpr)
    {
        return node switch
        {
            ILAstStatement { Expression: var expr } => ReplaceInExpr(expr, varName, valueExpr),
            ILAstAssignment assign => ReplaceInExpr(assign.Value, varName, valueExpr),
            _ => false
        };
    }

    static bool ReplaceInExpr(ILAstExpression expr, string varName, ILAstExpression valueExpr)
    {
        for (int i = 0; i < expr.Arguments.Count; i++)
        {
            if (IsLoadOf(expr.Arguments[i], varName))
            {
                expr.Arguments[i] = valueExpr;
                return true;
            }
            if (ReplaceInExpr(expr.Arguments[i], varName, valueExpr))
                return true;
        }
        return false;
    }

    static bool IsLoadOf(ILAstExpression expr, string varName)
    {
        if (expr.OpCode is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
            or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc)
        {
            string? name = expr.Operand ?? GetLocalNameFromOp(expr.OpCode);
            return name == varName;
        }
        return false;
    }

    static bool IsStoreToVar(ILAstNode node, string varName)
    {
        if (node is ILAstStatement { Expression: var expr }
            && expr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
               or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc)
        {
            string? name = expr.Operand ?? GetLocalNameFromOp(expr.OpCode);
            return name == varName;
        }
        return false;
    }

    static bool ExprReferencesVar(ILAstExpression expr, string varName)
    {
        if (IsLoadOf(expr, varName))
            return true;
        foreach (var arg in expr.Arguments)
            if (ExprReferencesVar(arg, varName))
                return true;
        return false;
    }

    static bool IsSideEffecting(ILAstExpression expr)
    {
        if (expr.OpCode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli)
            return true;
        foreach (var arg in expr.Arguments)
            if (IsSideEffecting(arg))
                return true;
        return false;
    }

    /// <summary>Locals loaded anywhere in the expression tree.</summary>
    static void CollectLocalReads(ILAstExpression expr, HashSet<string> locals)
    {
        if (expr.OpCode is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2
            or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc
            or ILOpCode.Ldloca or ILOpCode.Ldloca_s)
        {
            string? name = expr.Operand ?? GetLocalNameFromOp(expr.OpCode);
            if (name is not null)
                locals.Add(name);
        }
        foreach (var arg in expr.Arguments)
            CollectLocalReads(arg, locals);
    }

    /// <summary>
    /// True when the expression reads state a call or store could change:
    /// fields, array elements, or indirection. (Array length is immutable
    /// per instance, and local reads are tracked separately.)
    /// </summary>
    static bool ReadsMutableState(ILAstExpression expr)
    {
        if (expr.OpCode is ILOpCode.Ldfld or ILOpCode.Ldsfld or ILOpCode.Ldflda or ILOpCode.Ldsflda
            or ILOpCode.Ldelem or ILOpCode.Ldelema
            or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4
            or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref
            or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4
            or ILOpCode.Ldind_i or ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_i4
            or ILOpCode.Ldind_i8 or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref
            or ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4 or ILOpCode.Ldobj)
        {
            return true;
        }
        foreach (var arg in expr.Arguments)
            if (ReadsMutableState(arg))
                return true;
        return false;
    }

    static bool IsMutationOrCall(ILOpCode op) => op is
        ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Calli or ILOpCode.Newobj
        or ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Stobj or ILOpCode.Initobj
        or ILOpCode.Cpobj or ILOpCode.Cpblk or ILOpCode.Initblk
        or ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4
        or ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_ref
        or ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
        or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
        or ILOpCode.Stelem_ref;

    static bool NodeMutatesOrCalls(ILAstNode node) => node switch
    {
        ILAstStatement { Expression: var expr } => TreeMutatesOrCalls(expr),
        ILAstAssignment { Value: var value } => TreeMutatesOrCalls(value),
        _ => false
    };

    static bool TreeMutatesOrCalls(ILAstExpression expr)
    {
        if (IsMutationOrCall(expr.OpCode))
            return true;
        foreach (var arg in expr.Arguments)
            if (TreeMutatesOrCalls(arg))
                return true;
        return false;
    }

    /// <summary>Does the node store to any local in <paramref name="locals"/>?</summary>
    static bool StoresToAny(ILAstNode node, HashSet<string> locals)
    {
        foreach (string local in locals)
            if (IsStoreToVar(node, local))
                return true;
        return false;
    }

    /// <summary>
    /// Walks the node's evaluation order (depth-first, argument order) and
    /// reports whether any mutation/call executes before the first load of
    /// <paramref name="varName"/> — the position the inlined expression
    /// would evaluate at.
    /// </summary>
    static bool MutatesBeforeFirstRef(ILAstNode node, string varName)
    {
        var expr = node switch
        {
            ILAstStatement s => s.Expression,
            ILAstAssignment a => a.Value,
            _ => null
        };
        if (expr is null)
            return true; // unknown shape — don't inline

        bool foundRef = false;
        return Walk(expr, varName, ref foundRef);

        static bool Walk(ILAstExpression expr, string varName, ref bool foundRef)
        {
            // Arguments evaluate before the operation itself.
            foreach (var arg in expr.Arguments)
            {
                if (Walk(arg, varName, ref foundRef))
                    return true;
                if (foundRef)
                    return false;
            }

            if (IsLoadOf(expr, varName))
            {
                foundRef = true;
                return false;
            }

            return IsMutationOrCall(expr.OpCode);
        }
    }

    /// <summary>
    /// True for expressions whose value cannot depend on where they are
    /// evaluated: constants and string/null/token loads.
    /// </summary>
    static bool IsPositionIndependent(ILAstExpression expr) => expr.OpCode is
        ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4_m1
        or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3
        or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7
        or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i8 or ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8
        or ILOpCode.Ldstr or ILOpCode.Ldnull;

    static string? GetLocalNameFromOp(ILOpCode op) => op switch
    {
        ILOpCode.Stloc_0 or ILOpCode.Ldloc_0 => "V_0",
        ILOpCode.Stloc_1 or ILOpCode.Ldloc_1 => "V_1",
        ILOpCode.Stloc_2 or ILOpCode.Ldloc_2 => "V_2",
        ILOpCode.Stloc_3 or ILOpCode.Ldloc_3 => "V_3",
        _ => null
    };
}
