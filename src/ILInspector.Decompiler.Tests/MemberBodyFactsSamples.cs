namespace ILInspector.Decompiler.Tests;

// Fixtures for MemberBodyFactsTests constructor cases. The neutral constructor
// body-fact extractor pattern-matches decompiled IR, so these compiled
// constructor shapes exercise the chain-call and primary-constructor-prologue
// detections directly.
public class ChainedCtorSample
{
    public int Value;
    public string Label;

    // Overload 0 (declared first): parameterless ctor chaining to the
    // (int, string) overload -> chain parameter types [int, string].
    public ChainedCtorSample() : this(0, "none")
    {
    }

    public ChainedCtorSample(int value, string label)
    {
        Value = value;
        Label = label;
    }
}

public class PrimaryCtorSample(int alpha, string beta)
{
    public int Alpha => alpha;
    public string Beta => beta;
}

public static class NamespaceRefSample
{
    // References System.Text (StringBuilder) and System.Collections.Generic (List<T>)
    // alongside System (Int32/String), so the body-namespace extractor reports all
    // three distinct namespaces.
    public static string ReferencesTextAndGenerics(int count)
    {
        var list = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++)
            list.Add(i);
        var builder = new System.Text.StringBuilder();
        foreach (var value in list)
            builder.Append(value);
        return builder.ToString();
    }

    // References only System (Int32).
    public static int ReferencesOnlySystem(int x) => x + 1;

    // References NamespaceOrderFixtures.HPack and NamespaceOrderFixtures.Headers,
    // whose ordinal order (HPack, then Headers) differs from their culture-aware
    // and case-insensitive order (Headers, then HPack). See NamespaceOrderFixtures.cs.
    public static void ReferencesCaseOrderFlippedNamespaces(
        NamespaceOrderFixtures.HPack.Marker hpack,
        NamespaceOrderFixtures.Headers.Marker headers)
    {
        _ = hpack.ToString();
        _ = headers.ToString();
    }

    // References FunctionPointerNamespaceFixtures only through a function pointer
    // parameter's return type and argument type (delegate*<...>'s element/type
    // arguments), never as an ordinary local, field, or parameter/return type. See
    // FunctionPointerNamespaceFixtures.cs.
    public static unsafe void ReferencesNamespacesOnlyThroughFunctionPointer(
        delegate*<FunctionPointerNamespaceFixtures.ParameterMarker, FunctionPointerNamespaceFixtures.ReturnMarker> callback)
    {
        _ = callback;
    }
}

// Fixtures for MemberBodyFactsTests backing-field cases. Auto-property accessors
// touch the compiler-generated backing field directly, so importing get_/set_
// exercises the LoadField/StoreField walk with a resolvable BackingPropertyName.
public class BackingFieldSample
{
    public int Number { get; set; }

    public static string Label { get; set; } = "";

    // Reads no field, so the walk reports nothing.
    public int NoFieldAccess(int x) => x + 1;
}
