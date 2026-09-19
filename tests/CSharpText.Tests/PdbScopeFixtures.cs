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

    public static int SequentialScopeLocalsWithGoto(bool skip, int value)
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

        if (skip)
            goto Decrement;
    Increment:
        total++;
        if (total < value)
            goto Decrement;
        goto Done;
    Decrement:
        total--;
        if (total > -value)
            goto Increment;
    Done:
        return total;
    }

    public static int SequentialScopeLocalsWithInternalLabels(
        bool firstPath,
        bool secondPath,
        int value)
    {
        int total = 0;
        {
            int same = value;
            if (firstPath)
                goto FirstIncrement;
        FirstRecord:
            total += same;
            if (same < value + 2)
                goto FirstIncrement;
            goto FirstDone;
        FirstIncrement:
            Increment(ref same);
            if (same <= value + 2)
                goto FirstRecord;
        FirstDone:
            total += same;
        }
        {
            string same = value.ToString();
            if (secondPath)
                goto SecondKeep;
        SecondRecord:
            total += same.Length;
            if (same.Length < value)
                goto SecondKeep;
            goto SecondDone;
        SecondKeep:
            KeepAlive(ref same);
            if (same.Length <= value)
                goto SecondRecord;
        SecondDone:
            total += same.Length;
        }
        return total;
    }

    public static int SequentialScopeLocalsWithEntryLabels(bool secondPath, int value)
    {
        int total = 0;
        if (secondPath)
            goto Second;
    First:
        {
            int same = value;
            Increment(ref same);
            total += same;
        }
        if (total < value)
            goto Second;
        goto Done;
    Second:
        {
            string same = value.ToString();
            KeepAlive(ref same);
            total += same.Length;
        }
        if (total < value)
            goto First;
    Done:
        return total;
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

    public static int SequentialPatterns(object first, object second)
    {
        {
            if (first is string value)
                return PatternValue(value);
        }

        {
            if (second is string value)
                return PatternValue(value);
        }

        return 0;
    }

    public static int SequentialOutVariables(string first, string second)
    {
        int total = 0;
        {
            if (TryRead(first, out int value))
                total += value;
        }
        {
            if (TryRead(second, out int value))
                total += value;
        }
        return total;
    }

    public static bool SwitchExpressionOutVariables(
        int selector,
        string first,
        string second)
        => selector switch
        {
            0 => TryRead(first, out int value) && value >= 0,
            _ => TryRead(second, out int value) && value >= 0,
        };

    public static int SwitchSectionOutVariables(int selector, string value)
    {
        switch (selector)
        {
            case 0:
            {
                return TryRead(value, out int same) ? same : -1;
            }
            case 1:
            {
                return TryRead(value, out int same) ? same + 1 : -1;
            }
            case 2:
            {
                return TryRead(value, out int same) ? same + 2 : -1;
            }
            case 3:
            {
                return TryRead(value, out int same) ? same + 3 : -1;
            }
            default:
            {
                return TryRead(value, out int same) ? same + 4 : -1;
            }
        }
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

    static int PatternValue(string value) => value.Length;

    static bool TryRead(string text, out int value) => int.TryParse(text, out value);
}
