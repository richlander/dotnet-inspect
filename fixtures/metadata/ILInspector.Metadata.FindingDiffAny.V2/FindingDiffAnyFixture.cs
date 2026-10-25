// Finding Diff Any fixture, version 2. Each Type isolates one kind of change; the
// expected API and Body states are asserted by FindingDiffAnyTests.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace FindingDiffAnyFixture;

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

// A catch Type is an exception clause fact: canonical IL comparison shows
// its change (#9942), so the Body axis compares it.
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

// A mixed-accessor property whose widest accessor narrows: its accessibility,
// the widest of its accessors', moves from public to protected.
public class MixedAccessorPropertyPublicToProtected
{
    protected int Value { get; set; }
}

// The reverse: its accessibility widens from protected to public.
public class MixedAccessorPropertyProtectedToPublic
{
    public int Value { get; protected set; }
}

// Only the narrower accessor moves; the widest stays public, so the property's
// accessibility does not move.
public class MixedAccessorPropertyNarrowerAccessorMoves
{
    public int Value { get; private set; }
}

// The setter moves between accessibilities with the same external reach: derived
// Types in other assemblies.
public class AccessorProtectedToProtectedInternal
{
    public int Value { get; protected internal set; }
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

// API status transitions (docs/design/api-status-analysis.md): each changes one
// subject's hidden or obsolete status and nothing else.
public class MemberBecomesHidden
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Value() => 1;
}

public class MemberBecomesVisible
{
    public int Value() => 1;
}

[EditorBrowsable(EditorBrowsableState.Never)]
public class TypeBecomesHidden
{
    public int Value() => 1;
}

public class MemberBecomesObsolete
{
    [Obsolete("m")]
    public int Value() => 1;
}

public class ObsoleteWarningToError
{
    [Obsolete("m", true)]
    public int Value() => 1;
}

[Obsolete("m")]
public class TypeBecomesObsolete
{
    public int Value() => 1;
}

public class ObsoleteMessageChanged
{
    [Obsolete("after")]
    public int Value() => 1;
}

public class MemberBecomesObsoleteError
{
    [Obsolete("m", true)]
    public int Value() => 1;
}

// A body present on one side only: an abstract method made virtual.
public abstract class AbstractToVirtual
{
    public virtual int Value(int x) => x;
}

// A body present on one side only: a default interface implementation added.
public interface IDefaultImplementationAdded
{
    int Value(int x) => x;
}

// Ordinary presentation (docs/design/library-api-diff-presentation.md): a public
// nested Type of a hidden Type is outside the ordinary population, and an
// obsolete Member's other changes stay in it.
[EditorBrowsable(EditorBrowsableState.Never)]
public class HiddenOuter
{
    public class Inner
    {
        public int Value(int after) => after;
    }
}

public class ObsoleteMemberChanged
{
    [Obsolete("m")]
    public int Value(int after) => after;
}
