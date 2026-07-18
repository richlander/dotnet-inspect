using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds an exhaustive nested if/return comparison tree over two or more
/// independent integer-like places into one tuple relational-pattern switch
/// expression. csc's lowering for a tuple relational-pattern switch such as
/// <c>(x, y) switch { (&gt; 0, &gt; 0) =&gt; "I", (&lt; 0, &gt; 0) =&gt; "II", (&lt; 0, &lt; 0)
/// =&gt; "III", (&gt; 0, &lt; 0) =&gt; "IV", _ =&gt; "axis" }</c> is a decision tree testing
/// each place against a shared anchor constant, which <see cref="ReturnDispatchPass"/>
/// already folds into real nested <see cref="IfStatement"/>/<see cref="Return"/>
/// IR (its comparison-tree fold). This pass runs immediately after that fold and
/// walks the resulting tree, intersecting per-component relational constraints
/// along each root-to-leaf path so it can prove the arms are mutually exclusive
/// and, together with one merged default arm, exhaustive.
///
/// <para>The recognition is deliberately narrow and declines rather than guesses:</para>
/// <list type="bullet">
/// <item>the container must already be the single-block, single-statement shape
/// <see cref="ReturnDispatchPass"/> produces — no prefix statement anywhere in the
/// tree (a side-effecting setup step declines the whole rewrite);</item>
/// <item>every condition is a comparison between one place — newly admitted only
/// if it is a bare <see cref="LoadArgument"/>/<see cref="LoadLocal"/> of an
/// integer-like type (floats are excluded: ordered/unordered float compares
/// disagree on NaN, the same reason <c>IsPatternPass</c> declines float positional
/// sub-patterns) — and a constant that anchors that place the same way (same
/// value and type) everywhere in the tree;</item>
/// <item>a leaf whose accumulated per-component constraints cover every
/// discovered component becomes an explicit arm; a leaf that leaves at least one
/// component unconstrained is a default candidate, and all default candidates
/// must share one structurally-equal <see cref="Constant"/> value to merge into
/// the single trailing default arm — an ambiguous or non-constant candidate
/// declines the whole rewrite; a fully-determined leaf whose value already
/// equals the merged default value is dropped as redundant rather than kept as
/// its own arm, so the recovered switch matches the idiomatic hand-written
/// shape instead of a noisier behavior-preserving superset;</item>
/// <item>at least <see cref="MinArms"/> distinct explicit arms and one default
/// candidate are required, mirroring <see cref="ComparisonTrees"/>' own
/// multi-way gate.</item>
/// </list>
///
/// <para>Each component and each leaf value is moved (not cloned) from the
/// discarded tree into the new node, so no place read or leaf value is ever
/// duplicated; the switch's governing tuple is printed once, so every component
/// is evaluated exactly once.</para>
/// </summary>
public sealed class TupleSwitchExpressionPass : IIrPass
{
    public string Name => "tuple-switch-expression";
    const int MinArms = 4;

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
            if (TryFold(container, context.Stepper))
                return;
    }

    sealed record Leaf(Dictionary<int, ComparisonKind> Constraints, IrExpression Value);

    static bool TryFold(BlockContainer container, Stepper stepper)
    {
        if (container.Blocks is not [{ Children: [IfStatement rootIf] }])
            return false;

        var components = new List<IrExpression>();
        var anchors = new List<(object? Value, TypeRef Type)>();
        var leaves = new List<Leaf>();
        if (!Walk(rootIf, components, anchors, ImmutableDictionary<int, ComparisonKind>.Empty, leaves))
            return false;

        int componentCount = components.Count;
        if (componentCount < 2)
            return false;

        var explicitLeaves = leaves.Where(leaf => leaf.Constraints.Count == componentCount).ToList();
        var partialLeaves = leaves.Where(leaf => leaf.Constraints.Count < componentCount).ToList();
        if (partialLeaves.Count == 0)
            return false;

        if (partialLeaves[0].Value is not Constant defaultAnchor)
            return false;
        for (int i = 1; i < partialLeaves.Count; i++)
        {
            if (partialLeaves[i].Value is not Constant other
                || !Equals(other.Value, defaultAnchor.Value)
                || !other.Type.Equals(defaultAnchor.Type))
            {
                return false;
            }
        }

        // A fully-determined leaf whose value already equals the merged default's
        // value is redundant as a named arm — the trailing `_` covers it exactly,
        // so dropping it here is what turns the recognized tree into the same
        // shape a hand-written tuple switch would spell (issue #2867's "fully
        // raised" bar), not just a behavior-preserving-but-noisy superset.
        bool IsDefaultValue(IrExpression value)
            => value is Constant constant && Equals(constant.Value, defaultAnchor.Value) && constant.Type.Equals(defaultAnchor.Type);
        explicitLeaves = explicitLeaves.Where(leaf => !IsDefaultValue(leaf.Value)).ToList();
        if (explicitLeaves.Count < MinArms)
            return false;

        // Validated: every read below moves (never clones) a node out of the
        // tree we are about to discard, so nothing is duplicated.
        foreach (var component in components)
            component.Detach();

        var arms = new List<TupleSwitchExpressionArm>();
        foreach (var leaf in explicitLeaves)
        {
            var subpatterns = ImmutableArray.CreateBuilder<PositionalPatternSubpattern>(componentCount);
            var constants = new List<Constant>(componentCount);
            for (int i = 0; i < componentCount; i++)
            {
                subpatterns.Add(new PositionalPatternSubpattern(leaf.Constraints[i]));
                constants.Add(new Constant(anchors[i].Value, anchors[i].Type));
            }
            leaf.Value.Detach();
            arms.Add(new TupleSwitchExpressionArm(subpatterns.MoveToImmutable(), constants, leaf.Value));
        }

        var defaultValue = partialLeaves[0].Value;
        defaultValue.Detach();
        arms.Add(new TupleSwitchExpressionArm([], [], defaultValue));

        var tupleSwitch = new TupleSwitchExpression(components, arms);
        stepper.StepOver("fold exhaustive tuple comparison-tree dispatch to a tuple relational-pattern switch expression", rootIf);
        rootIf.ReplaceWith(new Return(tupleSwitch));
        return true;
    }

    /// <summary>
    /// Walks one root-to-leaf path, accumulating <paramref name="pathConstraints"/>
    /// (immutable so branching costs nothing but a reference copy) and recording
    /// each terminal <see cref="Return"/> as a <see cref="Leaf"/>. Declines
    /// (returns false) on anything outside the narrow recognized shape: a missing
    /// <c>else</c> (non-exhaustive), a prefix statement (effectful), an
    /// unrecognized condition or place, or a contradictory constraint.
    /// </summary>
    static bool Walk(
        IrNode node,
        List<IrExpression> components,
        List<(object? Value, TypeRef Type)> anchors,
        ImmutableDictionary<int, ComparisonKind> pathConstraints,
        List<Leaf> leaves)
    {
        switch (node)
        {
            case Return { Value: { } value }:
                leaves.Add(new Leaf(pathConstraints.ToDictionary(kv => kv.Key, kv => kv.Value), value));
                return true;

            case IfStatement { Else: { } elseArm } ifStatement:
                if (ifStatement.Then.Children is not [{ } thenStatement]
                    || elseArm.Children is not [{ } elseStatement]
                    || !TryDecompose(ifStatement.Condition, components, anchors, out int index, out var trueKind))
                {
                    return false;
                }

                return TryMerge(pathConstraints, index, trueKind, out var thenConstraints)
                    && TryMerge(pathConstraints, index, Conditions.Inverse(trueKind), out var elseConstraints)
                    && Walk(thenStatement, components, anchors, thenConstraints, leaves)
                    && Walk(elseStatement, components, anchors, elseConstraints, leaves);

            default:
                return false;
        }
    }

    /// <summary>
    /// Recognizes <c>place OP constant</c> (either operand order), resolving
    /// <paramref name="index"/> to an existing component via
    /// <see cref="PlaceIdentity.SameVariable"/> or admitting a new one. A new
    /// component must be integer-like; an existing one must share the exact same
    /// anchor constant (value and type) as its first occurrence, or the whole
    /// rewrite declines — a different anchor for the same place is out of scope.
    /// </summary>
    static bool TryDecompose(
        IrExpression condition,
        List<IrExpression> components,
        List<(object? Value, TypeRef Type)> anchors,
        out int index,
        out ComparisonKind kind)
    {
        index = -1;
        kind = default;
        if (condition is not Comparison comparison)
            return false;

        IrExpression place;
        Constant constant;
        if (comparison.Right is Constant rightConstant && IsPlaceCandidate(comparison.Left))
        {
            place = comparison.Left;
            constant = rightConstant;
            kind = comparison.Kind;
        }
        else if (comparison.Left is Constant leftConstant && IsPlaceCandidate(comparison.Right))
        {
            place = comparison.Right;
            constant = leftConstant;
            kind = Conditions.Mirror(comparison.Kind);
        }
        else
        {
            return false;
        }

        index = components.FindIndex(existing => PlaceIdentity.SameVariable(existing, place));
        if (index < 0)
        {
            if (place.ResultType is not { } placeType || !TypeFamilies.IsIntegerLike(placeType))
                return false;

            index = components.Count;
            components.Add(place);
            anchors.Add((constant.Value, constant.Type));
            return true;
        }

        return Equals(constant.Value, anchors[index].Value) && constant.Type.Equals(anchors[index].Type);
    }

    static bool IsPlaceCandidate(IrExpression expression) => expression is LoadArgument or LoadLocal;

    static bool TryMerge(
        ImmutableDictionary<int, ComparisonKind> constraints,
        int index,
        ComparisonKind incoming,
        out ImmutableDictionary<int, ComparisonKind> merged)
    {
        if (constraints.TryGetValue(index, out var existing))
        {
            if (Intersect(existing, incoming) is not { } combined)
            {
                merged = constraints;
                return false;
            }
            merged = constraints.SetItem(index, combined);
            return true;
        }
        merged = constraints.SetItem(index, incoming);
        return true;
    }

    /// <summary>
    /// Intersects two relational constraints on the same integer-like place,
    /// e.g. <c>&lt;= k</c> and <c>&gt;= k</c> tighten to <c>== k</c>; <c>null</c>
    /// means the combination is unsatisfiable (a contradiction along this path,
    /// which declines the whole rewrite rather than silently dropping a branch).
    /// </summary>
    static ComparisonKind? Intersect(ComparisonKind a, ComparisonKind b) => (a, b) switch
    {
        _ when a == b => a,
        (ComparisonKind.Equal, ComparisonKind.LessThanOrEqual) or (ComparisonKind.LessThanOrEqual, ComparisonKind.Equal) => ComparisonKind.Equal,
        (ComparisonKind.Equal, ComparisonKind.GreaterThanOrEqual) or (ComparisonKind.GreaterThanOrEqual, ComparisonKind.Equal) => ComparisonKind.Equal,
        (ComparisonKind.NotEqual, ComparisonKind.LessThan) or (ComparisonKind.LessThan, ComparisonKind.NotEqual) => ComparisonKind.LessThan,
        (ComparisonKind.NotEqual, ComparisonKind.GreaterThan) or (ComparisonKind.GreaterThan, ComparisonKind.NotEqual) => ComparisonKind.GreaterThan,
        (ComparisonKind.NotEqual, ComparisonKind.LessThanOrEqual) or (ComparisonKind.LessThanOrEqual, ComparisonKind.NotEqual) => ComparisonKind.LessThan,
        (ComparisonKind.NotEqual, ComparisonKind.GreaterThanOrEqual) or (ComparisonKind.GreaterThanOrEqual, ComparisonKind.NotEqual) => ComparisonKind.GreaterThan,
        (ComparisonKind.LessThan, ComparisonKind.LessThanOrEqual) or (ComparisonKind.LessThanOrEqual, ComparisonKind.LessThan) => ComparisonKind.LessThan,
        (ComparisonKind.GreaterThan, ComparisonKind.GreaterThanOrEqual) or (ComparisonKind.GreaterThanOrEqual, ComparisonKind.GreaterThan) => ComparisonKind.GreaterThan,
        (ComparisonKind.LessThanOrEqual, ComparisonKind.GreaterThanOrEqual) or (ComparisonKind.GreaterThanOrEqual, ComparisonKind.LessThanOrEqual) => ComparisonKind.Equal,
        _ => null,
    };
}
