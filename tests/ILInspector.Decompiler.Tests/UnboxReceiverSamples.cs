namespace ILInspector.Decompiler.Tests;

// The source-level cast reads a field from the boxed argument and compiles to
// `unbox` (a managed pointer into the box) plus `ldfld`. The decompiler must
// preserve that receiver as `Unsafe.Unbox<CfgBoxed>(other).Value`, not as a
// value-copy cast or invalid bare `ref` expression.
// UnboxReceiverRenderingTests.UnboxFieldReceiver_SpellsUnsafeUnbox gates this shape.
public struct CfgBoxed
{
    public int Value;
    public bool FieldEquals(object other) => Value == ((CfgBoxed)other).Value;
}
