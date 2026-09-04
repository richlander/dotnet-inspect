using System;

namespace LadderRung1;

// Rung 1 of the decompiler product quality ladder (#1599): Hello World / C# 1
// core. Every member here is deliberately ordinary C# 1: methods, locals, calls,
// fields, properties (explicit get/set and getter-only), and simple if/else
// branches. LadderRung1GateTests decompiles this assembly and pins the rung 1
// completion bar (all members import + render Full, fully raised with zero gaps,
// zero malformed Full / semantic-binding defects, accessors included).
public class Program
{
    int _count;

    // Explicit get/set property backed by a field, with a setter branch.
    public int Count
    {
        get
        {
            return _count;
        }
        set
        {
            if (value < 0)
            {
                _count = 0;
                return;
            }
            _count = value;
        }
    }

    // Getter-only property.
    public string Prefix
    {
        get
        {
            return "Hello";
        }
    }

    // Object construction, local assignment, field/property interaction, a call,
    // Console.WriteLine, and a simple if/else branch.
    public static void Main(string[] args)
    {
        Program program = new Program();
        program.Count = args.Length;
        string message = program.Greet("world");
        if (program.IsPositive(program.Count))
        {
            Console.WriteLine(message);
        }
        else
        {
            Console.WriteLine(program.Prefix);
        }
    }

    // String concatenation / call spelling, plus a field mutation.
    public string Greet(string name)
    {
        string result = string.Concat(Prefix, ", ", name);
        _count++;
        return result;
    }

    // Multi-return branch classification.
    public int Classify(int value)
    {
        if (value == 0)
        {
            return 0;
        }
        if (value < 3)
        {
            return 1;
        }
        return 2;
    }

    // Simple arithmetic.
    public int Add(int left, int right)
    {
        return left + right;
    }

    // Boolean branch / result.
    public bool IsPositive(int value)
    {
        return value > 0;
    }
}
