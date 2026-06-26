using System;

namespace LadderRung3;

// Rung 3 of the decompiler product quality ladder (#1599): C# 6 sugar.
// LadderRung3GateTests pins null propagation and simple interpolation as
// recoverable method-body sugar. Expression-bodied members are IL-identical to
// block-bodied members, so their arrow spelling is pinned only as a type-source
// rendering preference for applicable single-expression bodies.
public class Program
{
    public string Prefix => "Hello";

    public static void Main(string[] args)
    {
        var node = args.Length == 0 ? null : new Node(args[0]);
        Console.WriteLine(Describe(node));
        Console.WriteLine(Format(args.Length, Describe(node)));
    }

    public static string Describe(Node node) => node?.Label ?? "none";

    public static string Format(int count, string label) => $"Count={count}; Label={label}";

    public string Greet(Node node) => $"{Prefix}, {node?.Label ?? "guest"}";
}

public class Node
{
    public Node(string label)
    {
        Label = label;
    }

    public string Label { get; }
}
