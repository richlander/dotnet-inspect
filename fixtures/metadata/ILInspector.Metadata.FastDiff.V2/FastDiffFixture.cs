// Fast Diff fixture, version 2. Each Type isolates one kind of change; the
// expected API and Body states are asserted by FastDiffTests.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace FastDiffFixture;

public class Unchanged
{
    public int Value(int x) => x + 1;
}

public class BodyOnly
{
    public int Value(int x) => x + 2;
}

public class LambdaOnly
{
    public IEnumerable<int> Value(IEnumerable<int> items) => items.Select(x => x + 2);
}

public class AsyncOnly
{
    public async Task<int> Value(int x)
    {
        await Task.Yield();
        return x + 2;
    }
}

public class LocalFunctionOnly
{
    public int Value(int x)
    {
        return Add(x);
        static int Add(int y) => y + 2;
    }
}

public class StringLiteralOnly
{
    public string Value() => "after";
}

public class CatchTypeOnly
{
    public int Value(Func<int> action)
    {
        try { return action(); }
        catch (ArgumentException) { return 0; }
    }
}

public class PrivateMemberAdded
{
    public int Value(int x) => x + 1;

    private int Helper() => 0;
}

// A private field added: a Body change, not an API change.
public class PrivateFieldAdded
{
    public int Value(int x) => x + 1;

#pragma warning disable CS0169 // The field is the change.
    private int _added;
#pragma warning restore CS0169
}

// A public method whose only change is in the private method it calls.
public class PrivateCalleeChanged
{
    public int Value(int x) => Helper(x);

    private int Helper(int x) => x + 2;
}

public class PublicMemberAdded
{
    public int Value(int x) => x + 1;

    public int Added() => 0;
}

public class MethodConstraint
{
    public void Value<T>() where T : struct { }
}

#nullable disable
// Without nullable annotations the constraint row carries no attribute, so
// only the constraint fact itself differs.
public class ConstraintType
{
    public void Value<T>() where T : IComparable { }
}
#nullable restore

public class ConstantValue
{
    public const int Value = 2;
}

public class DefaultParameter
{
    public int Value(int x = 2) => x;
}

public class ParameterName
{
    public int Value(int after) => after;
}

public class PublicAttribute
{
    [Obsolete("after")]
    public int Value() => 1;
}

public class InterfaceAdded : IDisposable
{
    public void Dispose() { }
}

public class BecomesPublic
{
}

public class Outer
{
    public class Inner
    {
        public int Value(int x) => x + 2;
    }
}

public class Generic<T>
{
    public T Value(T x) => x;

    public T Other(T x) => x;
}

internal class InternalBodyOnly
{
    public int Value(int x) => x + 2;
}

// Roslyn emits NullableContext on NullableOuter only; Inner inherits it.
public class NullableOuter
{
    public string? A(string? x) => x!;
    public string? B(string? x) => x!;
    public string? C(string? x) => x!;

    public class Inner
    {
        public string? X(string? x) => x!;
        public string? Y(string? x) => x!;
        public string? Z(string? x) => x!;
    }
}

public class Added
{
}

// A hidden public Member is on the API axis, marked hidden.
public class HiddenMemberChanged
{
    public int Value(int x) => x;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Hidden(int after) => after;
}

// A hidden public Type is on the API axis, marked hidden.
[EditorBrowsable(EditorBrowsableState.Never)]
public class HiddenTypeChanged
{
    public int Value(int after) => after;
}

// A protected Member is on the API axis.
public class ProtectedMemberChanged
{
    protected int Value(int after) => after;
}

// A private protected Member is not on the API axis.
public class PrivateProtectedMemberAdded
{
    public int Value(int x) => x;

    private protected int Added() => 0;
}

// Accessibility changes within the API axis.
public class PublicToProtected
{
    protected int Value(int x) => x;
}

public class PublicToProtectedInternal
{
    protected internal int Value(int x) => x;
}

public class ProtectedInternalToProtected
{
    protected int Value(int x) => x;
}

public class ProtectedToProtectedInternal
{
    protected internal int Value(int x) => x;
}

public class ProtectedInternalToPublic
{
    public int Value(int x) => x;
}

public class SetterToProtected
{
    public int Value { get; protected set; }
}

public class NestedPublicToProtected
{
    protected class Nested
    {
    }
}

public class NestedProtectedToPublic
{
    public class Nested
    {
    }
}

// Accessibility moves that leave or stay outside the API axis.
public class PublicToInternal
{
    internal int Value(int x) => x;
}

public class PublicToPrivate
{
    private int Value(int x) => x;
}

public class ProtectedToInternal
{
    internal int Value(int x) => x;
}

public class InternalToPrivate
{
    private int Value(int x) => x;
}

public class ProtectedToPrivateProtected
{
    private protected int Value(int x) => x;
}

internal class PublicTypeToInternal
{
}

public class PropertyPublicToProtected
{
    protected int Value { get; set; }
}

// A whole property with a narrower accessor moves; only its accessibility changes.
public class PropertyWithPrivateSetterPublicToProtected
{
    protected int Value { get; private set; }
}

// Changes wholly outside the API axis.
public class PrivateMemberRemoved
{
    public int Value(int x) => x;
}

public class PrivateMemberChanged
{
    public int Value(int x) => (int)Helper(x);

    private long Helper(int x) => x;
}

internal class InternalTypeMemberChanged
{
    public long Value(int x) => x;
}

internal class InternalOuterNestedMemberChanged
{
    public class Nested
    {
        public long Value(int x) => x;
    }
}
