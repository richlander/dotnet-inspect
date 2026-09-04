using System;
using System.IO;

namespace LadderRung1;

// Rung 1 foundation control flow for the decompiler product quality ladder
// (#1599 / #1691). The original LadderRung1.Program fixture only guards
// methods/locals/calls/fields/properties and simple if/return branches, yet the
// rung is labeled the "Straightforward syntax foundation". This type adds the
// core C# 1 control-flow surface that was working but unguarded: loops, switch
// statements, arrays, try/catch/finally, exception filters, and using
// statements. LadderRung1FoundationGateTests decompiles this type and pins the
// same scoped bar as the Program gate (every member imports + renders Full,
// fully raised with zero gaps, zero malformed Full / semantic-binding defects),
// so a regression below the foundation bar fails fast in PR CI.
public class Foundation
{
    // for loop.
    public int ForLoop(int n)
    {
        int sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += i;
        }
        return sum;
    }

    // while loop.
    public int WhileLoop(int n)
    {
        int sum = 0;
        while (n > 0)
        {
            sum += n;
            n--;
        }
        return sum;
    }

    // do/while loop.
    public int DoWhileLoop(int n)
    {
        int sum = 0;
        do
        {
            sum += n;
            n--;
        }
        while (n > 0);
        return sum;
    }

    // foreach over an array.
    public int ForEachLoop(int[] values)
    {
        int sum = 0;
        foreach (int value in values)
        {
            sum += value;
        }
        return sum;
    }

    // Dense int switch (a jump-table switch the IL actually encodes, so the
    // switch shape is recoverable — unlike a 2-3 case switch, which the compiler
    // lowers to compare branches that are IL-identical to if/else).
    public string Switch(int code)
    {
        switch (code)
        {
            case 0: return "zero";
            case 1: return "one";
            case 2: return "two";
            case 3: return "three";
            case 4: return "four";
            case 5: return "five";
            default: return "many";
        }
    }

    // Array allocation, indexing, and assignment.
    public int[] ArrayCreateAndIndex(int n)
    {
        int[] result = new int[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = i * i;
        }
        return result;
    }

    // Array literal.
    public int[] ArrayLiteral()
    {
        return new int[] { 1, 2, 3, 4 };
    }

    // try/catch/finally.
    public int TryCatchFinally(int x)
    {
        try
        {
            return 100 / x;
        }
        catch (DivideByZeroException)
        {
            return -1;
        }
        finally
        {
            Console.WriteLine("done");
        }
    }

    // Exception filter (catch when).
    public int TryCatchFilter(int x)
    {
        try
        {
            return 100 / x;
        }
        catch (Exception error) when (error.Message.Length > 3)
        {
            return -2;
        }
    }

    // using statement.
    public long UsingStatement()
    {
        using (MemoryStream stream = new MemoryStream())
        {
            return stream.Length;
        }
    }
}
