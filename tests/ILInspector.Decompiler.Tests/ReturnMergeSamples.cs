namespace ILInspector.Decompiler.Tests;

public static class ReturnMergeSamples
{
    // csc places the labeled break block immediately before the shared return
    // tail. DoWhileLoopPass raises the exit branch to Break before
    // ReturnMergePass classifies the tail's lexical predecessor.
    public static int LabeledBreakBeforeReturnTail(int x)
    {
        int result;
        do
        {
            if (x == 0)
                goto BreakLoop;
            if (x == 1)
            {
                result = 10;
                goto ReturnResult;
            }
            if (x == 2)
            {
                result = 20;
                goto ReturnResult;
            }
            x--;
            continue;

        BreakLoop:
            result = 2;
            break;
        ReturnResult:
            return result;
        }
        while (x > 0);

        return 0;
    }
}
