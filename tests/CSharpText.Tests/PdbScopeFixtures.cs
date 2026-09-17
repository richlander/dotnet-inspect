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

    public static void SequentialScopeLocals(int value)
    {
        {
            int same = value;
            Increment(ref same);
        }
        {
            string same = value.ToString();
            KeepAlive(ref same);
        }
    }

    public static int SequentialStackCarry(int value)
    {
        int total = 0;
        {
            int same = value;
            Increment(ref same);
            total += same;
        }
        {
            string same = value.ToString();
            KeepAlive(ref same);
            total += same.Length;
        }
        return total;
    }

    public static void SequentialValueTypeScopeLocals()
    {
        {
            System.Guid same = default;
            KeepGuidAlive(ref same);
        }
        {
            System.Guid same = default;
            KeepGuidAlive(ref same);
        }
    }

    public static System.Action<int> LambdaScopes() => static value =>
    {
        {
            int same = value;
            Increment(ref same);
        }
        {
            string same = value.ToString();
            KeepAlive(ref same);
        }
    };

    public static int LocalFunctionScopes(bool condition, int value)
    {
        return Run(condition, value);

        static int Run(bool condition, int value)
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
    }

    public static System.Collections.Generic.IEnumerable<int> IteratorScopes(bool condition, int value)
    {
        if (condition)
        {
            int same = value;
            Increment(ref same);
            value = same;
        }
        else
        {
            string same = value.ToString();
            KeepAlive(ref same);
            value = same.Length;
        }
        yield return value;
    }

    static void Increment(ref int value) => value++;

    static void KeepAlive(ref string value) => System.GC.KeepAlive(value);

    static void KeepGuidAlive(ref System.Guid value) => System.GC.KeepAlive(value);
}
