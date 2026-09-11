namespace CSharpText.Tests;

public static class PdbScopeFixtures
{
    public static int DisjointScopeLocals(bool condition, int value)
    {
        if (condition)
        {
            int same = value;
            Increment(ref same);
            return same;
        }
        else
        {
            string same = value.ToString();
            KeepAlive(ref same);
            return same.Length;
        }
    }

    static void Increment(ref int value) => value++;

    static void KeepAlive(ref string value) => System.GC.KeepAlive(value);
}
