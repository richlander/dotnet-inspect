namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Raises forward branch regions into nested <see cref="IfStatement"/>s —
/// the first structuring slice. Guard shapes (<c>if (c) goto M; …body…; M:</c>)
/// and diamonds (<c>if (c) goto T; …false…; goto M; T: …true…; M:</c>) nest
/// recursively. The pass is two-phase: the whole function is validated
/// against the slice (forward branches only — loops, switch, and EH stay
/// flat) before any mutation, so a function either structures completely or
/// keeps the always-correct flat form. Conditions render the fallthrough
/// arm first, matching the current emitter's guard style.
/// </summary>
public sealed class StructuringPass : IIrPass
{
    public string Name => "structuring";

    public void Run(IrFunction function)
    {
        if (!function.Regions.IsEmpty)
            return;  // flat EH model structures in a later slice
        var blocks = function.Body.Blocks;
        if (blocks.Count <= 1)
            return;

        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (!Validate(blocks, offsetToIndex, 0, blocks.Count, joinIndex: blocks.Count))
            return;

        var structured = BuildRegion(blocks, offsetToIndex, 0, blocks.Count, joinIndex: blocks.Count);
        var container = new BlockContainer();
        container.Add(structured);
        function.Body.ReplaceWith(container);
    }

    /// <summary>Phase 1: pure shape check over block indices — no mutation until the whole function fits the slice.</summary>
    static bool Validate(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int start, int stop, int joinIndex)
    {
        int i = start;
        while (i < stop)
        {
            var block = blocks[i];
            if (block.Children.Count == 0)
            {
                // Nop-only label landing pads (Debug builds): pure fallthrough.
                i++;
                continue;
            }
            for (int s = 0; s < block.Children.Count - 1; s++)
            {
                if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                    return false;  // mid-block control flow is outside the slice
            }
            switch (block.Children[^1])
            {
                case Return or Throw:
                    i++;
                    break;
                case Branch branch:
                    // Only the region-exit goto is in the slice; it must be
                    // the region's last block.
                    if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int branchTarget)
                        || branchTarget != joinIndex || i + 1 != stop)
                    {
                        return false;
                    }
                    i = stop;
                    break;
                case ConditionalBranch conditional:
                {
                    if (!offsetToIndex.TryGetValue(conditional.TargetOffset, out int target) || target <= i)
                        return false;  // backward = loop, later slice
                    if (target > stop)
                        return false;
                    // Guard: if (c) goto M with M ending this region's view.
                    // Diamond: the fallthrough arm ends with goto M past the
                    // true arm.
                    int falseStart = i + 1;
                    if (FindDiamondJoin(blocks, offsetToIndex, falseStart, target, stop) is { } join)
                    {
                        // False arm exits by goto join; true arm falls (or
                        // returns) into join.
                        if (!Validate(blocks, offsetToIndex, falseStart, target, joinIndex: join)
                            || !Validate(blocks, offsetToIndex, target, join, joinIndex: join))
                        {
                            return false;
                        }
                        i = join;
                        break;
                    }
                    // Guard form: arm is (i+1, target), continues at target.
                    if (!Validate(blocks, offsetToIndex, falseStart, target, joinIndex: target))
                        return false;
                    i = target;
                    break;
                }
                default:
                    // A block that falls through to its successor.
                    if (i + 1 >= stop && stop != joinIndex)
                        return false;
                    i++;
                    break;
            }
        }
        return true;
    }

    /// <summary>The diamond join: the false arm's last block ends with a goto past the true arm; null means guard shape.</summary>
    static int? FindDiamondJoin(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int falseStart, int trueStart, int stop)
    {
        if (falseStart >= trueStart)
            return null;
        if (blocks[trueStart - 1].Children.Count == 0)
            return null;
        if (blocks[trueStart - 1].Children[^1] is Branch branch
            && offsetToIndex.TryGetValue(branch.TargetOffset, out int join)
            && join > trueStart && join <= stop)
        {
            return join;
        }
        return null;
    }

    /// <summary>Phase 2: same walk, moving statements into the structured tree. Mirrors Validate exactly; shapes were already proven.</summary>
    static Block BuildRegion(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int start, int stop, int joinIndex)
    {
        var result = new Block(blocks[start].StartOffset);
        int i = start;
        while (i < stop)
        {
            var block = blocks[i];
            if (block.Children.Count == 0)
            {
                i++;
                continue;
            }
            var statements = block.DetachChildren();
            var last = statements[^1];
            for (int s = 0; s < statements.Count - 1; s++)
                result.Add(statements[s]);
            switch (last)
            {
                case Return or Throw:
                    result.Add(last);
                    i++;
                    break;
                case Branch:
                    i = stop;  // the region-exit goto disappears into structure
                    break;
                case ConditionalBranch conditional:
                {
                    int target = offsetToIndex[conditional.TargetOffset];
                    var condition = (IrExpression)conditional.DetachChildren()[0];
                    int falseStart = i + 1;
                    if (FindDiamondJoin(blocks, offsetToIndex, falseStart, target, stop) is { } join)
                    {
                        // Fallthrough arm first, current-emitter guard style:
                        // the negated condition selects it.
                        var thenArm = BuildRegion(blocks, offsetToIndex, falseStart, target, joinIndex: join);
                        var elseArm = BuildRegion(blocks, offsetToIndex, target, join, joinIndex: join);
                        result.Add(new IfStatement(Negate(condition), thenArm, elseArm));
                        i = join;
                        break;
                    }
                    var guardArm = BuildRegion(blocks, offsetToIndex, falseStart, target, joinIndex: target);
                    result.Add(new IfStatement(Negate(condition), guardArm, null));
                    i = target;
                    break;
                }
                default:
                    result.Add(last);
                    i++;
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Negates a detached condition with the type-aware IL duals (the old
    /// pipeline's NaN lesson): integer comparisons invert their kind; float
    /// comparisons invert kind AND flip the unordered flag (NOT(a &lt; b) is
    /// a &gt;= b unordered, never plain a &gt;= b); unknown operand types
    /// wrap in LogicalNot — honest, never a guess. Double negation unwraps.
    /// </summary>
    static IrExpression Negate(IrExpression condition) => condition switch
    {
        LogicalNot not => (IrExpression)not.DetachChildren()[0],
        Comparison comparison => InvertComparison(comparison),
        _ => new LogicalNot(condition),
    };

    static IrExpression InvertComparison(Comparison comparison)
    {
        bool? isFloat = OperandFloatness(comparison);
        if (isFloat is null && comparison.Kind is not (ComparisonKind.Equal or ComparisonKind.NotEqual))
            return new LogicalNot(comparison);  // ordering duals need known operand types

        var operands = comparison.DetachChildren();
        var kind = comparison.Kind switch
        {
            ComparisonKind.Equal => ComparisonKind.NotEqual,
            ComparisonKind.NotEqual => ComparisonKind.Equal,
            ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
            _ => ComparisonKind.LessThan,
        };
        bool isUnsigned = isFloat == true ? !comparison.IsUnsigned : comparison.IsUnsigned;
        return new Comparison(kind, isUnsigned, (IrExpression)operands[0], (IrExpression)operands[1]);
    }

    /// <summary>True = float operands, false = integer, null = unknown.</summary>
    static bool? OperandFloatness(Comparison comparison)
    {
        bool? Of(TypeRef? type) => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "Single" or "Double" => true,
                "Boolean" or "Char" or "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32"
                    or "Int64" or "UInt64" or "IntPtr" or "UIntPtr" => false,
                _ => null,
            }
            : null;
        return Of(comparison.Left.ResultType) ?? Of(comparison.Right.ResultType);
    }
}
