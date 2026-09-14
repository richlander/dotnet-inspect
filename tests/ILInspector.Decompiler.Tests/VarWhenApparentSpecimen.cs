using System.Collections.Generic;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Compiled specimens for the three opt-in <c>var</c> spelling buckets. Each method
/// declares a real local (used more than once so it survives as a declaring store)
/// whose initializer either proves exact inference or exercises a close conversion
/// boundary.
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

    // Negative for the built-in bucket: the call's natural type is int, while the
    // declaration relies on an implicit widening conversion to long.
    public static long BuiltInNumericWidening()
    {
        long value = IntValue();
        return value + value;
    }

    // Negative for the built-in bucket: the IR can recover the sink's byte identity,
    // but the emitted bare literal still has C# natural type int.
    public static int BuiltInConstantConversion()
    {
        byte value = 1;
        return value + value;
    }

    // Negative for the elsewhere bucket: the initializer call returns List<int>,
    // while the declaration relies on its implicit reference conversion to the
    // IReadOnlyCollection<int> interface.
    public static int ElsewhereReferenceWidening()
    {
        IReadOnlyCollection<int> items = Make();
        return items.Count + items.Count;
    }

    // Negative for the elsewhere bucket: removing the declaration context makes the
    // tuple elements infer int, not byte.
    public static int TupleElementConversion()
    {
        (byte, byte) pair = (1, 2);
        return pair.Item1 + pair.Item2;
    }

    // Negatives for the built-in bucket: dynamic erases to object in metadata, but
    // `var` would preserve dynamic and change later overload binding.
    public static int DynamicParameterToObject(dynamic input)
    {
        object value = input;
        return Pick(value) + Pick(value);
    }

    public static int DynamicReturnToObject()
    {
        object value = GetDynamic();
        return Pick(value) + Pick(value);
    }

    // Nested dynamic positions erase to object in local signatures too. The member
    // signatures retain DynamicAttribute and render the initializer as List<dynamic>
    // or dynamic[], so `var` would restore dynamic binding at the element access.
    public static int NestedDynamicParameterToObjects(List<dynamic> input)
    {
        List<object> values = input;
        return Pick(values[0]) + Pick(values[0]);
    }

    public static int NestedDynamicReturnToObjects()
    {
        List<object> values = GetDynamicValues();
        return Pick(values[0]) + Pick(values[0]);
    }

    public static int NestedDynamicArrayToObjects(dynamic[] input)
    {
        object[] values = input;
        return Pick(values[0]) + Pick(values[0]);
    }

    // Close positive: the explicit constructed type proves static object at the
    // nested position, so the apparent bucket may still use var.
    public static int NestedObjectCreation()
    {
        List<object> values = new List<object>();
        values.Add("text");
        return values.Count;
    }

    static dynamic GetDynamic() => "text";

    static List<dynamic> GetDynamicValues() => new() { "text" };

    static int Pick(object value) => 1;

    static int Pick(string value) => 2;

    static int IntValue() => 3;

    static List<int> Make() => new();

    public sealed class Node
    {
        public int Value;
    }
}
