using System.Collections.Generic;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Compiled specimens for the opt-in apparent-type <c>var</c> bucket
/// (<see cref="ILInspector.Decompiler.Pipeline.PrinterOptions.PreferVarWhenTypeApparent"/>,
/// <c>csharp_style_var_when_type_is_apparent</c>). Each method declares a real local
/// (used more than once so it survives as a declaring store) whose type is — or is
/// deliberately not — apparent from its initializer.
/// </summary>
public sealed class VarWhenApparentSpecimen
{
    // Apparent via object creation of the exact (non-built-in) type. The default view
    // shortens the RHS to `new()`; the var bucket must keep the explicit `new List<int>()`
    // (a bare `var x = new()` is CS8754), so the two spellings are mutually exclusive.
    public static int ObjectCreation()
    {
        List<int> items = new List<int>();
        items.Add(1);
        return items.Count;
    }

    // Apparent via single-dimension array creation. The declared type `int[]` is a shape,
    // not a built-in keyword type, so the built-in exclusion does not apply here.
    public static int ArrayCreation()
    {
        int[] buffer = new int[4];
        buffer[0] = 7;
        return buffer[0];
    }

    // Apparent via an explicit reference cast to a non-built-in type.
    public static int ReferenceCast(object o)
    {
        Node node = (Node)o;
        return node.Value + node.Value;
    }

    // Negative: apparent, but the declared type is a C# built-in keyword type
    // (`string`) — governed by the separate built-in-types bucket, so the apparent
    // bucket must decline and leave the explicit type.
    public static int BuiltInObjectCreation()
    {
        string text = new string('x', 3);
        return text.Length + text.Length;
    }

    // Negative: the type is not apparent from the initializer (a plain call), so the
    // apparent bucket must decline regardless of the option.
    public static int NotApparent()
    {
        List<int> items = Make();
        items.Add(1);
        return items.Count;
    }

    static List<int> Make() => new();

    public sealed class Node
    {
        public int Value;
    }
}
