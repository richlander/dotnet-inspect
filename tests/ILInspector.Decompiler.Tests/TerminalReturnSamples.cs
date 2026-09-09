namespace ILInspector.Decompiler.Tests;

public static class TerminalReturnSamples
{
    public static void BeforeLocalFunction()
    {
        F();
        static void F() { }
    }

    public static void EarlyReturnBeforeLocalFunction(bool skip)
    {
        if (skip)
            return;

        F();
        static void F() { }
    }
}
