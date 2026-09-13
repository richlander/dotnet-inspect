namespace ILInspector.Decompiler.Tests;

using System.Collections.Generic;
using System.Text;

// Fixtures for the target-typed `new()` rendering transform. Each method below is
// decompiled by TargetTypedNewPassTests; the transform shortens `new T(args)` to
// `new(args)` only when the contextual target type is exactly the constructed type.
public class TargetTypedNewFixtures
{
    StringBuilder _builder = new StringBuilder();

    // Positive: a local whose declared type is exactly the constructed type — the
    // canonical `StringBuilder sb = new(n)` case.
    public int LocalDeclaration(int n)
    {
        StringBuilder sb = new StringBuilder(n);
        sb.Append('x');
        return sb.Length;
    }

    // Positive: an instance field store whose field type equals the constructed
    // type (reached through the assignment path).
    public void FieldStore(int n)
    {
        _builder = new StringBuilder(n);
    }

    // Boundary: return positions are out of the v1 LHS-only scope, so these stay
    // explicit (the transform never fires here yet).
    public StringBuilder ReturnPosition(int n)
    {
        if (n > 0)
            return new StringBuilder(n);
        return new StringBuilder();
    }

    // Positive: a value-type construction (newobj on a struct) into a local of the
    // same struct type.
    public int StructLocal(int n)
    {
        Box box = new Box(n);
        box.Bump();
        return box.Value;
    }

    // Positive: an array element store whose element type equals the constructed
    // type. A value-type element array (`stelem` carries the element token) exposes
    // the real element target; a reference-type array's `stelem.ref` is untyped
    // (object) and would soundly decline instead.
    public void ElementStore(Box[] boxes, int n)
    {
        boxes[0] = new Box(n);
    }

    // Negative: the target is an interface the constructed type implements, not the
    // constructed type itself — target-typed `new()` would bind the wrong type.
    public int InterfaceTargetDeclines()
    {
        IList<int> list = new List<int>(4);
        list.Add(1);
        return list.Count;
    }

    // Negative: a rectangular array creation is modeled as a `newobj`, but its
    // array type is never the target-typed-new form.
    public int[,] MultiDimArrayDeclines(int n)
    {
        int[,] grid = new int[n, n];
        grid[0, 0] = 1;
        return grid;
    }

    // Negative: an argument position. Target-typed `new()` there would participate
    // in overload resolution (Accept(StringBuilder) vs Accept(object)), so the
    // explicit type must stay.
    public int ArgumentPositionDeclines(int n)
        => Accept(new StringBuilder(n));

    // Negative: a bare `object` target. `object o = new object()` is a valid
    // target-typed-new, but the raw `object` target is indistinguishable at this seam
    // from a `dynamic` place (which the IR erases to `object`), where `new()` is
    // CS8752 — so all bare `System.Object` constructions decline. `new object()` has
    // no type name to drop anyway.
    public bool ObjectTargetDeclines()
    {
        object o = new object();
        object p = new object();
        return ReferenceEquals(o, p);
    }

    // Negative: a `dynamic` parameter reassignment. The signature spells `dynamic
    // value`, but the IR store carries the erased `object` type; shortening to
    // `value = new()` would be CS8752. The bare-object decline keeps `new object()`.
    public void DynamicParamDeclines(dynamic value)
    {
        value = new object();
    }

    static int Accept(StringBuilder builder) => builder.Length;
    static int Accept(object value) => value.GetHashCode();

    public struct Box
    {
        int _value;
        public Box(int value) => _value = value;
        public void Bump() => _value++;
        public readonly int Value => _value;
    }
}
