using System;
using System.IO;

namespace LadderRung5;

// Rung 5 of the decompiler product quality ladder (#1599): the straightforward
// modern-syntax row (row 2). Most members exercise one C# 8-9 construct —
// index/range operators, using declarations, switch expressions, expanded
// patterns (relational/logical/property/tuple/type), and records with init-only
// members — plus C# 11 checked user-defined operators (#1706), whose USE must
// render with a checked(...) context rather than an explicit op_Checked* call.
// LadderRung5GateTests decompiles this assembly and measures the rung 5 bar:
// fixture members render valid C# with no invalid Full, and source concepts the
// IL erases degrade with owned residuals rather than wrong source truth.
public class Program
{
    // Index-from-end operator (^1).
    public int LastElement(int[] values)
    {
        return values[^1];
    }

    // Index-from-end with a computed offset.
    public int FromEnd(int[] values, int offset)
    {
        return values[^offset];
    }

    // Range operator producing a slice (1..^1).
    public int[] MiddleSlice(int[] values)
    {
        return values[1..^1];
    }

    // Range with an open end (..2).
    public int[] Prefix(int[] values)
    {
        return values[..2];
    }

    // Switch expression with relational and logical (and) patterns.
    public string Size(int value)
    {
        return value switch
        {
            < 0 => "negative",
            0 => "zero",
            > 0 and < 10 => "small",
            _ => "big",
        };
    }

    // Switch expression over a tuple with nested relational patterns.
    public string Quadrant(int x, int y)
    {
        return (x, y) switch
        {
            ( > 0, > 0) => "I",
            ( < 0, > 0) => "II",
            ( < 0, < 0) => "III",
            ( > 0, < 0) => "IV",
            _ => "axis",
        };
    }

    // Property pattern.
    public bool IsOrigin(Point point)
    {
        return point is { X: 0, Y: 0 };
    }

    // Type pattern with a logical-not pattern.
    public bool IsRealString(object value)
    {
        return value is string and not "";
    }

    // Switch expression mixing type, constant, and discard patterns.
    public string Describe(object value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            int n and > 0 => "positive int",
            _ => "other",
        };
    }

    // using declaration over a disposable local.
    public long UsingDeclaration(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        stream.ReadByte();
        return stream.Length;
    }

    // Object initializer assigning an init-only property on a record.
    public Point MakeScaled(int x, int y)
    {
        return new Point(x, y) { Magnitude = x + y };
    }

    // Nondestructive mutation (with-expression) on a record.
    public Point Shift(Point point, int dx)
    {
        return point with { X = point.X + dx };
    }
}

// Positional record with an extra init-only property: rung 5 "records/init where
// visible". The compiler-synthesized members (Equals, ==, GetHashCode, ToString,
// Deconstruct, copy-constructor, Clone, EqualityContract) are part of what the
// decompiler must render validly or degrade honestly.
public record Point(int X, int Y)
{
    public int Magnitude { get; init; }
}

// C# 11 checked user-defined operators (#1706): a modern-syntax construct whose
// USE must render with a checked(...) context, not an explicit op_Checked* method
// call (which is CS0571 "cannot explicitly call operator" — invalid Full). The
// checked and unchecked operators are both exercised so the gate pins the
// distinction: AddChecked renders `checked(a + b)`, AddUnchecked renders `a + b`.
public readonly struct CheckedMeters
{
    public CheckedMeters(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static CheckedMeters operator +(CheckedMeters a, CheckedMeters b)
        => new CheckedMeters(a.Value + b.Value);

    public static CheckedMeters operator checked +(CheckedMeters a, CheckedMeters b)
        => checked(new CheckedMeters(a.Value + b.Value));

    // User-defined ++/-- (#1712): the IL lowers a ++/-- use to an op_Increment /
    // op_CheckedIncrement call + store. The decompiler must fold that back to the
    // ++/-- operator (with checked(...) for the checked overload); the bare
    // op_Increment(a) method spelling is CS0571 invalid Full.
    public static CheckedMeters operator ++(CheckedMeters a)
        => new CheckedMeters(a.Value + 1);

    public static CheckedMeters operator checked ++(CheckedMeters a)
        => checked(new CheckedMeters(a.Value + 1));

    public static CheckedMeters operator --(CheckedMeters a)
        => new CheckedMeters(a.Value - 1);

    // The decompiler renders these uses (not the operator declarations above):
    // the checked use must force the checked overload with checked(...).
    public static CheckedMeters AddChecked(CheckedMeters a, CheckedMeters b) => checked(a + b);

    public static CheckedMeters AddUnchecked(CheckedMeters a, CheckedMeters b) => a + b;

    public static CheckedMeters PreIncrement(CheckedMeters a) => ++a;

    public static CheckedMeters PostIncrement(CheckedMeters a) => a++;

    public static CheckedMeters PreIncrementChecked(CheckedMeters a) => checked(++a);

    public static CheckedMeters PreDecrement(CheckedMeters a) => --a;

    // Statement form (the increment value is discarded): folds to a++; not the
    // CS0571 op_Increment(a) call. The checked statement needs a checked { ... }
    // block, since checked(a++) is CS0201 in statement position.
    public static CheckedMeters StatementIncrement(CheckedMeters a)
    {
        a++;
        return a;
    }

    public static CheckedMeters StatementIncrementChecked(CheckedMeters a)
    {
        checked
        {
            a++;
        }
        return a;
    }
}

// A class user type with a checked ++ operator: a checked for-loop over a class
// loop variable raises to a `for` whose header increment is checked, which has no
// in-header checked spelling. The decompiler wraps the whole loop in checked { }
// so the bare i++ selects the checked overload (#1712).
public class CheckedStep
{
    public int Value;

    public static CheckedStep operator ++(CheckedStep c) => new CheckedStep { Value = c.Value + 1 };

    public static CheckedStep operator checked ++(CheckedStep c) => checked(new CheckedStep { Value = c.Value + 1 });

    public static int CheckedForLoop(CheckedStep start, int n)
    {
        checked
        {
            for (CheckedStep i = start; i.Value < n; i++)
            {
            }
        }
        return n;
    }
}

// C# 12 primary constructor on a class. The captured parameters lift into
// unspeakable <param>P fields; the decompiler must render them as the parameter
// name (a real lowered field + this.-qualified constructor stores), never the raw
// <seed>P, which is invalid Full (#1719). Rung 5 modern-syntax surface (#1599 row 2).
public class PrimaryCounter(int seed, int step)
{
    public int Next() => seed + step;

    public int SeedPlus(int extra) => seed + extra;
}

// Other straightforward C# 11-12 modern-syntax shapes the rung 5 bar covers: each
// must render valid C# or honestly degrade (HasNoInvalidFull guards the whole
// LadderRung5 surface). A collection-expression spread recovers to [..head, tail];
// a list pattern and a required-member object initializer render recognizably.
public class ModernSyntax
{
    public int[] Spread(int[] head, int tail) => [.. head, tail];

    public bool IsOneTwoThree(int[] xs) => xs is [1, 2, 3];

    public ModernHolder MakeHolder() => new ModernHolder { Name = "n", Count = 3 };
}

public class ModernHolder
{
    public required string Name { get; set; }

    public required int Count { get; init; }
}
