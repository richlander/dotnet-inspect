using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a pattern-guarded single-store diamond into the short-circuit <c>&amp;&amp;</c>
/// the compiler lowered it from. csc lowers <c>target = cond &amp;&amp; rhs</c> to
/// <c>if (cond) target = rhs; else target = false;</c>. When the condition is an
/// <c>is</c>-pattern (for example <c>Subject is T subject</c>), the pre-pattern
/// diamond folders cannot run: the pattern only enters the condition after
/// <see cref="IsPatternPass"/>, and — for a struct type parameter — the true arm
/// still opens with the compiler's spilled <c>default(T)</c> temp
/// (<c>initobj</c> on a local address, read once by the comparison). This pass
/// runs after <see cref="IsPatternPass"/>, inlines that arm-local default temp as
/// an inline <see cref="DefaultValue"/> so the arm collapses to a single store,
/// and rebuilds the <c>&amp;&amp;</c> so the bound pattern local reads inline into
/// the right operand — recovering
/// <c>target = Subject is T subject &amp;&amp; subject.CompareTo(default(T)) &gt; 0</c>
/// (the FluentAssertions <c>BePositive</c> shape).
///
/// The rewrite preserves evaluation order: in the original the guarded arm runs
/// only when the condition held, which is exactly when a short-circuit right
/// operand evaluates. The default temp is inlined only when it is local to the
/// arm (single-assignment, read once by value, never addressed elsewhere), so
/// nothing outside the arm observes it.
///
/// Only the <c>&amp;&amp;</c> shape is handled. A non-pattern <c>&amp;&amp;</c>/<c>||</c>
/// diamond is already collapsed to a ternary by the pre-pattern
/// <see cref="BooleanFoldingPass"/>; the <c>||</c> shape never survives to here
/// with a pattern, and its right operand would not definitely assign a pattern
/// local anyway.
/// </summary>
public sealed class PatternGuardedShortCircuitPass : IIrPass
{
    public string Name => "pattern-guarded-short-circuit";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    readonly record struct StoreTarget(bool IsSlot, int Id);

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        foreach (var ifs in function.Descendants.OfType<IfStatement>())
        {
            if (!ifs.HasElse || ifs.Else is not { } elseArm)
                continue;

            // The else arm marks `&&`: a single store of false to the target.
            if (AsSingleStore(elseArm) is not { } elseStore || BoolConstant(elseStore.Value) != false)
                continue;

            // The true arm ends in a (non-constant) store to the same target,
            // optionally preceded by compiler-spilled default(T) temp inits.
            if (ArmStore(ifs.Then) is not { } thenStore)
                continue;
            if (thenStore.Target != elseStore.Target || BoolConstant(thenStore.Value) is not null)
                continue;

            var rhs = thenStore.Value;

            // The `&&` right operand must be boolean. Without this a non-bool
            // single-store diamond whose else arm happens to store 0/false (for
            // example `if (cond) t = someInt; else t = 0;`) would fold to the
            // invalid `t = cond && someInt`.
            if (!TypeFamilies.IsBoolean(rhs.ResultType))
                continue;

            // Recover each arm-local default temp before folding; if any preamble
            // statement is not a recoverable default init, the arm has other
            // effects and must not collapse to a single `&&` operand.
            if (!TryRecoverArmDefaults(function, ifs.Then, thenStore.Store, rhs))
                continue;

            var patternLocals = ifs.Condition.Descendants.Prepend(ifs.Condition)
                .OfType<IsPattern>()
                .Select(pattern => pattern.LocalIndex)
                .ToList();

            // Every pattern local must be confined to this `if`; after the fold
            // it is definitely assigned only inside the `&&` right operand, so a
            // read anywhere else would be use-before-assignment (CS0165).
            IrNode[] allowed = [ifs];
            if (patternLocals.Any(local => !ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, local, allowed)))
                continue;

            var condition = ifs.Condition;
            condition.Detach();
            rhs.Detach();

            var logical = new LogicalBinary(LogicalKind.And, condition, rhs);
            IrNode replacement = thenStore.Target.IsSlot
                ? new StoreStackSlot(thenStore.Target.Id, logical)
                : new StoreLocal(thenStore.Target.Id, thenStore.LocalType!, logical);
            replacement.InheritSourceOffset(ifs);

            stepper.StepOver("fold pattern-guarded diamond into &&", ifs);
            ifs.ReplaceWith(replacement);
            return true;
        }

        return false;
    }

    // Inlines every compiler-spilled default(T) temp that opens the true arm.
    // Each preceding statement must be an `initobj` on a local address whose
    // local is arm-local: single-assignment (this init only, no StoreLocal),
    // read exactly once by value inside the arm's store value, and never
    // addressed elsewhere. Returns false — leaving the arm untouched — if any
    // preamble statement is something else.
    static bool TryRecoverArmDefaults(IrFunction function, Block thenArm, IrNode armStore, IrExpression rhs)
    {
        var preamble = thenArm.Children.Where(child => !ReferenceEquals(child, armStore)).ToList();

        var recoveries = new List<(InitObject Init, LoadLocal Load)>();
        foreach (var statement in preamble)
        {
            if (statement is not InitObject init || init.Address is not LoadLocalAddress { Index: var index })
                return false;

            // The local must belong to this arm only.
            if (!ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, index, [thenArm]))
                return false;
            if (function.Descendants.OfType<StoreLocal>().Any(store => store.Index == index))
                return false;
            if (function.Descendants.OfType<LoadLocalAddress>()
                    .Any(address => address.Index == index && !ReferenceEquals(address, init.Address)))
            {
                return false;
            }

            var loads = rhs.Descendants.Prepend(rhs).OfType<LoadLocal>().Where(load => load.Index == index).ToList();
            if (loads.Count != 1)
                return false;

            recoveries.Add((init, loads[0]));
        }

        foreach (var (init, load) in recoveries)
        {
            load.ReplaceWith(new DefaultValue(init.Type));
            init.Detach();
        }

        return true;
    }

    readonly record struct ArmStoreInfo(StoreTarget Target, IrExpression Value, TypeRef? LocalType, IrNode Store);

    // The arm's last statement is its target store; earlier statements are the
    // default-temp preamble handled by TryRecoverArmDefaults.
    static ArmStoreInfo? ArmStore(Block block)
    {
        if (block.Children.Count == 0)
            return null;

        return block.Children[^1] switch
        {
            StoreStackSlot slot => new ArmStoreInfo(new StoreTarget(IsSlot: true, slot.Slot), slot.Value, null, slot),
            StoreLocal local => new ArmStoreInfo(new StoreTarget(IsSlot: false, local.Index), local.Value, local.Type, local),
            _ => null,
        };
    }

    readonly record struct SingleStore(StoreTarget Target, IrExpression Value);

    static SingleStore? AsSingleStore(Block block)
    {
        if (block.Children.Count != 1)
            return null;

        return block.Children[0] switch
        {
            StoreStackSlot slot => new SingleStore(new StoreTarget(IsSlot: true, slot.Slot), slot.Value),
            StoreLocal local => new SingleStore(new StoreTarget(IsSlot: false, local.Index), local.Value),
            _ => null,
        };
    }

    static bool? BoolConstant(IrExpression expression) => expression switch
    {
        Constant { Value: bool value } => value,
        Constant { Value: int value } when value is 0 or 1 => value == 1,
        _ => null,
    };
}
