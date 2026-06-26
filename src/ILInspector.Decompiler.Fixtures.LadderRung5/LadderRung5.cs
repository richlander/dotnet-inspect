using System;
using System.IO;

namespace LadderRung5;

// Rung 5 of the decompiler product quality ladder (#1599): C# 8-9 syntax surface.
// Every member here exercises one rung 5 construct — index/range operators,
// using declarations, switch expressions, expanded patterns
// (relational/logical/property/tuple/type), and records with init-only members.
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
