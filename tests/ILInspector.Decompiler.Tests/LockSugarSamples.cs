namespace ILInspector.Decompiler.Tests;

/// <summary>Lock shapes the lock-sugar pass must raise, including static-field, instance-field, and parameter receivers.</summary>
public sealed class LockFixtureSamples
{
    static readonly object s_staticRoot = new();
    static int s_staticValue;
    readonly object _root = new();
    int _value;

    public static void IncrementStaticUnderLock()
    {
        lock (s_staticRoot) { s_staticValue++; }
    }

    public void IncrementUnderLock()
    {
        lock (_root) { _value++; }
    }

    public int ReadUnderLock()
    {
        lock (_root) { return _value; }
    }

    public void LockOnParameter(object gate)
    {
        lock (gate) { _value = 1; }
    }
}
