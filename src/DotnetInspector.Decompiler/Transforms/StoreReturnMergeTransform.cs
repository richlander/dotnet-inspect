using System.Reflection.Metadata;

namespace DotnetInspector.Decompiler.Transforms;

/// <summary>
/// Merges the <c>stloc V = expr; ret(ldloc V)</c> pair into <c>ret(expr)</c>
/// when the return is the variable's only load, removing the temp entirely
/// when nothing else references it. Replaces an emit-time peephole — as an
/// AST rewrite, downstream consumers (merged-declaration detection, bool
/// context, declaration emission) see the simplified shape for free.
/// </summary>
public sealed class StoreReturnMergeTransform : IILAstTransform
{
    public void Run(ILAstMethod method, TransformContext context)
    {
        foreach (var block in method.Blocks)
        {
            for (int i = 0; i + 1 < block.Nodes.Count; i++)
            {
                if (block.Nodes[i] is not ILAstStatement { Expression: var storeExpr })
                    continue;
                if (storeExpr.OpCode is not (ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                    or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc))
                    continue;
                if (storeExpr.Arguments.Count == 0)
                    continue;

                if (block.Nodes[i + 1] is not ILAstStatement { Expression: var retExpr })
                    continue;
                if (retExpr.OpCode != ILOpCode.Ret || retExpr.Arguments.Count == 0)
                    continue;

                string? storeVar = storeExpr.Operand ?? LocalNameFromOpcode(storeExpr.OpCode);
                string? retVar = LoadedLocalName(retExpr.Arguments[0]);
                if (storeVar is null || retVar is null || storeVar != retVar)
                    continue;

                // Only when this return is the sole load: the stored value may
                // feed other blocks (shared return locals, gotos).
                if (CountLoads(method, storeVar) != 1)
                    continue;

                retExpr.Arguments[0] = storeExpr.Arguments[0];
                block.Nodes.RemoveAt(i);

                if (CountLoads(method, storeVar) == 0 && CountStores(method, storeVar) == 0)
                    method.Locals.RemoveAll(l => l.Name == storeVar);
            }
        }
    }

    static string? LoadedLocalName(ILAstExpression expr) => expr.OpCode switch
    {
        ILOpCode.Ldloc_0 => "V_0",
        ILOpCode.Ldloc_1 => "V_1",
        ILOpCode.Ldloc_2 => "V_2",
        ILOpCode.Ldloc_3 => "V_3",
        ILOpCode.Ldloc_s or ILOpCode.Ldloc => expr.Operand,
        _ => null
    };

    static string? LocalNameFromOpcode(ILOpCode op) => op switch
    {
        ILOpCode.Stloc_0 => "V_0",
        ILOpCode.Stloc_1 => "V_1",
        ILOpCode.Stloc_2 => "V_2",
        ILOpCode.Stloc_3 => "V_3",
        _ => null
    };

    static int CountLoads(ILAstMethod method, string name)
    {
        int count = 0;
        foreach (var block in method.Blocks)
        {
            foreach (var node in block.Nodes)
            {
                var expr = node switch
                {
                    ILAstStatement s => s.Expression,
                    ILAstAssignment a => a.Value,
                    _ => null
                };
                if (expr is not null)
                    CountLoadsIn(expr, name, ref count);
            }
        }
        return count;
    }

    static void CountLoadsIn(ILAstExpression expr, string name, ref int count)
    {
        if (LoadedLocalName(expr) == name
            || (expr.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s && expr.Operand == name))
        {
            count++;
        }
        foreach (var arg in expr.Arguments)
            CountLoadsIn(arg, name, ref count);
    }

    static int CountStores(ILAstMethod method, string name)
    {
        int count = 0;
        foreach (var block in method.Blocks)
        {
            foreach (var node in block.Nodes)
            {
                if (node is ILAstAssignment assign && assign.Variable.Name == name)
                {
                    count++;
                }
                else if (node is ILAstStatement { Expression: var expr }
                    && expr.OpCode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2
                        or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc
                    && (expr.Operand ?? LocalNameFromOpcode(expr.OpCode)) == name)
                {
                    count++;
                }
            }
        }
        return count;
    }
}
