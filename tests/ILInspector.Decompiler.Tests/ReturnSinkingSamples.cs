namespace ILInspector.Decompiler.Tests;

// ReturnSinkingPass (#3552): a `return` inside a `using` body is lowered
// through a return accumulator — csc spills the value to a synthetic local,
// `leave`s the region, and emits `ldloc; ret` after the generated Dispose. The
// pass undoes that, and a `using` reaches it as its underlying try/finally
// because UsingStatementPass deliberately runs later. But when the body is a
// try/catch whose catch arm rethrows, the pass declined: it demanded a
// fall-through tail store from every arm, and a rethrowing arm has none. So the
// temp survived as `V = expr;` inside the block plus a trailing `return V;` —
// the Azure `TableClient.Create` shape. An arm that cannot fall through never
// reaches the trailing return, so it contributes no tail. The related source
// shapes stay together here as one compiler-fixture group.
public sealed class ReturnSinkScope : System.IDisposable
{
    public int Seen;
    public void Start() => Seen++;
    public int Compute(int seed) => seed * 2;
    public void Dispose() => Seen = -1;
}

public static class ReturnSinkSamples
{
    // The plain case: the accumulator's only store is the fall-through tail of
    // the using body, and the trailing `return V;` follows the using statement.
    public static int ReturnFromUsingBody(int seed)
    {
        using (var scope = new ReturnSinkScope())
        {
            scope.Start();
            return scope.Compute(seed);
        }
    }

    // The TableClient.Create shape: a try/catch nested in the using whose catch
    // arm rethrows. The rethrowing arm cannot fall through, so it reaches no
    // trailing return and contributes no tail.
    public static int ReturnFromTryInsideUsing(int seed)
    {
        using (var scope = new ReturnSinkScope())
        {
            scope.Start();
            try
            {
                return scope.Compute(seed);
            }
            catch (System.Exception)
            {
                throw;
            }
        }
    }
}

// #3552 review finding (credit: adversarial reviewer). A catch arm ending in
// `break` must NOT be folded. CollectBlockTail accepts a `StoreLocal; Break`
// pair as a foldable tail and Apply detaches the Break, so treating such an arm
// as falling through rewrites a loop `break` into a method `return` — the arm
// transfers to the enclosing loop and skips the trailing return entirely. This
// shape reproduces on main as well; the FallsThrough terminator set is what
// keeps it correct now that catch arms are classified at all.
public static class ReturnSinkBreakSamples
{
    public static int CatchArmBreaksOutOfLoop(int x)
    {
        int accumulator;
        while (x > 0)
        {
            try
            {
                accumulator = 1;
            }
            catch
            {
                accumulator = 2;
                break;
            }
            return accumulator;
        }
        System.Console.WriteLine("ended");
        return -1;
    }

    public static int TryBodyBreaksOutOfLoop(int x)
    {
        int accumulator;
        while (x > 0)
        {
            try
            {
                if (x == 1)
                {
                    accumulator = 1;
                }
                else
                {
                    accumulator = 2;
                    break;
                }
            }
            catch
            {
                accumulator = 3;
            }
            return accumulator;
        }
        System.Console.WriteLine("ended");
        return -1;
    }

    public static int FinallyProtectedBodyBreaksOutOfLoop(int x)
    {
        int accumulator;
        while (x > 0)
        {
            try
            {
                if (x == 1)
                {
                    accumulator = 1;
                }
                else
                {
                    accumulator = 2;
                    break;
                }
            }
            finally
            {
                System.Console.WriteLine("cleanup");
            }
            return accumulator;
        }
        System.Console.WriteLine("ended");
        return -1;
    }

    public static int IfElseArmBreaksOutOfLoop(int x)
    {
        int accumulator;
        while (x > 0)
        {
            if (x == 1)
            {
                accumulator = 1;
            }
            else
            {
                accumulator = 2;
                break;
            }
            return accumulator;
        }
        System.Console.WriteLine("ended");
        return -1;
    }
}
