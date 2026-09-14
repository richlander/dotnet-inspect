namespace ILInspector.Decompiler.Tests;

public static class FinallyReturnTimingSample
{
    static int s_count;

    public static int Run(bool loop, bool setValue, bool exit)
    {
        int result = 0;
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            result += 100;
        }

    Done:
        return result;
    }

    public static int RunNested(bool loop, bool setValue, bool exit)
    {
        int result = 0;
        try
        {
            try
            {
                while (loop)
                {
                    if (setValue)
                    {
                        result = 10;
                        goto Done;
                    }

                    if (exit)
                        goto Done;

                    loop = false;
                }
            }
            finally
            {
                result += 100;
            }
        }
        finally
        {
            s_count++;
        }

    Done:
        return result;
    }

    public static int RunArgument(
        int result,
        bool loop,
        bool setValue,
        bool exit)
    {
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            result += 100;
        }

    Done:
        return result;
    }

    public static int RunAliasedLocal(bool loop, bool setValue, bool exit)
    {
        int result = 0;
        ref int alias = ref result;
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            alias += 100;
        }

    Done:
        return result;
    }

    public static int RunConditionalAlias(
        bool useResult,
        bool loop,
        bool setValue,
        bool exit)
    {
        int result = 0;
        int other = 0;
        ref int alias = ref (useResult ? ref result : ref other);
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            alias += 100;
        }

    Done:
        return result;
    }

    public static int RunFieldAlias(bool loop, bool setValue, bool exit)
    {
        int result = 0;
        scoped RefHolder holder = default;
        holder.Value = ref result;
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            holder.Value += 100;
        }

    Done:
        return result;
    }

    public static int RunCallAlias(
        bool useResult,
        bool loop,
        bool setValue,
        bool exit)
    {
        int result = 0;
        int other = 0;
        ref int alias = ref Select(useResult, ref result, ref other);
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            alias += 100;
        }

    Done:
        return result;
    }

    public static int RunArgumentAlias(
        scoped ref int alias,
        bool loop,
        bool setValue,
        bool exit)
    {
        int result = 0;
        alias = ref result;
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            alias += 100;
        }

    Done:
        return result;
    }

    public static int RunConstructorAlias(
        bool loop,
        bool setValue,
        bool exit)
    {
        int result = 0;
        scoped RefHolder holder = new(ref result);
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            holder.Value += 100;
        }

    Done:
        return result;
    }

    public static int RunHelperAlias(
        bool loop,
        bool setValue,
        bool exit)
    {
        int result = 0;
        scoped RefHolder holder = default;
        Bind(ref holder, ref result);
        try
        {
            while (loop)
            {
                if (setValue)
                {
                    result = 10;
                    goto Done;
                }

                if (exit)
                    goto Done;

                loop = false;
            }
        }
        finally
        {
            holder.Value += 100;
        }

    Done:
        return result;
    }

    static ref int Select(bool first, ref int left, ref int right)
        => ref first ? ref left : ref right;

    static void Bind(
        scoped ref RefHolder holder,
        [System.Diagnostics.CodeAnalysis.UnscopedRef] ref int value)
        => holder.Value = ref value;

    ref struct RefHolder
    {
        public ref int Value;

        public RefHolder(ref int value)
        {
            Value = ref value;
        }
    }
}
