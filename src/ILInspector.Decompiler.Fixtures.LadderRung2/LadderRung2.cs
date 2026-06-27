using System;
using System.Collections.Generic;

namespace LadderRung2;

// Rung 2 of the decompiler product quality ladder (#1599): C# 2-3 structural
// basics. The initializer shapes here are intentionally ordinary C# 3 source:
// an object initializer whose constructed value is used after another initializer
// chain, a collection initializer assigned to a local, a collection initializer
// returned directly, and the already-supported direct-return object initializer.
public static class Program
{
    public static void Main()
    {
        Box<int> item = new Box<int> { Value = 3, Label = "three" };
        List<int> values = new List<int> { 1, 2, 3 };
        Console.WriteLine(item.FormatWith(values.FirstOrZero()));
    }

    public static List<int> MakeCollectionInitializer()
    {
        return new List<int> { 1, 2, 3 };
    }

    public static Box<string> MakeDirectReturn(string text)
        => new Box<string> { Value = text, Label = text.OrFallback("empty") };
}

public sealed class Box<T>
{
    public T Value { get; set; }
    public string Label { get; set; }

    public string FormatWith(int value) => string.Concat(Label, ":", Value, ":", value);
}

public static class LadderRung2Extensions
{
    public static int FirstOrZero(this List<int> values) => values.Count == 0 ? 0 : values[0];

    public static string OrFallback(this string value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value;
}
