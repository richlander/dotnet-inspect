namespace ILInspector.Decompiler.Tests;

using System;

/// <summary>
/// Fixture for issue #3028 (PR A): a <c>switch</c> expression over a plain
/// receiver whose arms all bind a local used only inside their own arm, so csc
/// lowers it to the <em>flat inline</em> cascade —
/// <c>if (P is Tk xk) { return vk; } ... return d;</c> — that
/// <see cref="ILInspector.Decompiler.Pipeline.IsPatternPass"/> folds each arm's
/// <c>as</c>/null test into a positive <c>is</c> test. This is the second shape
/// <see cref="ILInspector.Decompiler.Pipeline.PatternSwitchExpressionPass"/>
/// raises (distinct from the nested <c>as</c>/null-test intro chain the #3022
/// <see cref="PatternSwitchSample"/> exercises). Isolated from
/// <see cref="CfgSampleClass"/> so it does not perturb the fidelity gate's pinned
/// population.
/// </summary>
public static class InlinePatternSwitchSample
{
    public abstract class Address
    {
    }

    public sealed class LocalRef : Address
    {
        public LocalRef(int index) => Index = index;

        public int Index { get; }
    }

    public sealed class ArgRef : Address
    {
        public ArgRef(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }

        public string Name { get; }
    }

    public sealed class FieldRef : Address
    {
        public FieldRef(string field) => Field = field;

        public string Field { get; }
    }

    public sealed class ElementRef : Address
    {
    }

    public static Address? Simplify(Address address) => address switch
    {
        LocalRef local => new LocalRef(local.Index),
        ArgRef arg => new ArgRef(arg.Index, arg.Name),
        FieldRef field => new FieldRef(field.Field),
        ElementRef element => element,
        _ => null,
    };

    // A hand-written ladder whose first arm carries a guard and whose type
    // (IComparable) overlaps a later arm (ICloneable) — `string` satisfies both.
    // The inline fold must DECLINE here: a `switch` routes a failed `when` to the
    // next arm, but this ladder's guard-fail returns the shared default and exits,
    // so for a short-circuiting value the two forms disagree on which arm wins.
    // csc emits the same flat inline `is` shape as the foldable cases, so this is
    // the compiled canary that the guard restriction holds.
    public static int GuardedOverlap(object value, bool flag)
    {
        if (value is IComparable comparable)
        {
            if (flag)
            {
                return comparable.GetHashCode();
            }
            return 0;
        }
        if (value is ICloneable cloneable)
        {
            return cloneable.GetHashCode();
        }
        return 0;
    }
}
