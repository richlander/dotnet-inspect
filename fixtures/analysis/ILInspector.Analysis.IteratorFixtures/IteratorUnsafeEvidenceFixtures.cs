namespace ILInspector.Analysis.IteratorFixtures;

// Synchronous iterators kept apart from ILInspector.Analysis.Fixtures, whose
// legacy unsafe census still reports iterator bodies as unattributed.
// Roslyn emits an iterator's finally block as the state machine's
// <>m__Finally1 helper, so this Type's only unsafe evidence is outside
// MoveNext.
public static class GeneratedIteratorFinallyUnsafeSample
{
    public static int Observed;

    public static IEnumerable<int> FinallyOnlyUnsafe(int value)
    {
        try
        {
            yield return value;
        }
        finally
        {
            Observed = System.Runtime.CompilerServices.Unsafe.As<int, int>(
                ref value);
        }
    }
}

public static class GeneratedIteratorLiftedUnsafeSample
{
    public static IEnumerable<int> LiftedInIterator(int value)
    {
        Func<int, int> readStatic = static current =>
            System.Runtime.CompilerServices.Unsafe.As<int, int>(
                ref current);
        int offset = 1;
        Func<int, int> readCaptured = current =>
            System.Runtime.CompilerServices.Unsafe.As<int, int>(
                ref current) + offset;
        yield return readStatic(value) + readCaptured(value) + Local(value);

        static int Local(int current) =>
            System.Runtime.CompilerServices.Unsafe.As<int, int>(
                ref current);
    }
}
