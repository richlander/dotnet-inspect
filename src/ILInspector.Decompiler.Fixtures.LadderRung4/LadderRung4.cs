namespace LadderRung4;

// Rung 4 of the decompiler product quality ladder (#1599): C# 7 local syntax.
// This fixture is deliberately scoped to ordinary source shapes that should
// either decompile to valid C# or degrade honestly rather than claiming invalid
// Full output: tuples, deconstruction, local functions, simple patterns, and ref
// locals/returns.
public static class CSharp7LocalSyntax
{
    public static (int Sum, int Product) TupleReturn(int left, int right)
    {
        return (left + right, left * right);
    }

    public static int TupleUse((int Left, int Right) pair)
    {
        return pair.Left + pair.Right;
    }

    public static int DeconstructPoint(Point point)
    {
        var (x, y) = point;
        return x + y;
    }

    public static int LocalFunctionNonCapturing(int value)
    {
        int Double(int item)
        {
            return item * 2;
        }

        return Double(value);
    }

    public static int LocalFunctionCapturing(int value)
    {
        int AddSeed(int item) => item + value;

        return AddSeed(3);
    }

    public static int LocalFunctionCapturingWithLocal(int value)
    {
        int AddSquare(int item)
        {
            int y = item + value;
            return y * y;
        }

        return AddSquare(3);
    }

    public static int SimplePattern(object value)
    {
        if (value is int number)
        {
            return number;
        }

        if (value is string text)
        {
            return text.Length;
        }

        return 0;
    }

    public static int RefLocalUpdate(bool useLeft)
    {
        int left = 1;
        int right = 2;
        ref int target = ref SelectRef(useLeft, ref left, ref right);
        target += 10;
        return left + right;
    }

    public static ref int SelectRef(bool useLeft, ref int left, ref int right)
    {
        if (useLeft)
        {
            return ref left;
        }

        return ref right;
    }

    public static ref readonly int SelectReadonlyRef(bool useLeft, in int left, in int right)
    {
        if (useLeft)
        {
            return ref left;
        }

        return ref right;
    }
}

public readonly struct Point
{
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }

    public int Y { get; }

    public void Deconstruct(out int x, out int y)
    {
        x = X;
        y = Y;
    }
}
