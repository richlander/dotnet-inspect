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
}
