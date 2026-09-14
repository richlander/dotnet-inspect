namespace ILInspector.Decompiler.Tests;

public static class FinallyReturnTimingSample
{
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
}
