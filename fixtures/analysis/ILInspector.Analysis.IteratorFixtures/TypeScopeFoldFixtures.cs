using System.Runtime.CompilerServices;

namespace ILInspector.Analysis.IteratorFixtures;

// The issue #9755 shapes, kept out of ILInspector.Analysis.Fixtures, whose
// budget-sensitive structural-clone tests compare its methods. Each Type has
// one declared member, and its unsafe use lives in a compiler-generated body
// that must fold into that member.
public static class TypeScopeCapturingLambda
{
    public static int Read(int value)
    {
        int offset = 1;
        Func<int, int> read = current =>
            Unsafe.As<int, int>(ref current) + offset;
        return read(value);
    }
}

public static class TypeScopeStaticLambda
{
    public static int Read(int value)
    {
        Func<int, int> read = static current =>
            Unsafe.As<int, int>(ref current);
        return read(value);
    }
}

public static class TypeScopeLocalFunction
{
    public static int Read(int value)
    {
        return Local(value);

        static int Local(int current) =>
            Unsafe.As<int, int>(ref current);
    }
}

public static class TypeScopeAsyncMethod
{
    public static async Task<int> ReadAsync(int value)
    {
        await Task.Yield();
        return Unsafe.As<int, int>(ref value);
    }
}

public static class TypeScopeDirect
{
    public static int Read(int value) =>
        Unsafe.As<int, int>(ref value);
}
