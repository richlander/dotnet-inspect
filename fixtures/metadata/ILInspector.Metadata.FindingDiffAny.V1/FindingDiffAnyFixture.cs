// Finding Diff Any fixture, version 1. Each Type isolates one kind of change; the
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
    public int Value(int x) => x + 1;
}

public class LambdaOnly
{
    public IEnumerable<int> Value(IEnumerable<int> items) => items.Select(x => x + 1);
}

public class AsyncOnly
{
    public async Task<int> Value(int x)
    {
        await Task.Yield();
        return x + 1;
    }
}

public class LocalFunctionOnly
{
    public int Value(int x)
    {
        return Add(x);
        static int Add(int y) => y + 1;
    }
}

public class StringLiteralOnly
{
    public string Value() => "before";
}

// A catch Type is not an IL operation: canonical IL comparison does not
// show its change (#9942), so the Body axis compares it Unchanged.
public class CatchTypeOnly
{
    public int Value(Func<int> action)
    {
        try { return action(); }
        catch (InvalidOperationException) { return 0; }
    }
}

public class PrivateMemberAdded
{
    public int Value(int x) => x + 1;
}

// A private field added: a Body change, not an API change.
public class PrivateFieldAdded
{
    public int Value(int x) => x + 1;
}

// A public method whose only change is in the private method it calls.
public class PrivateCalleeChanged
{
    public int Value(int x) => Helper(x);

    private int Helper(int x) => x + 1;
}

public class PublicMemberAdded
{
    public int Value(int x) => x + 1;
}

public class MethodConstraint
{
    public void Value<T>() where T : class { }
}

#nullable disable
// Without nullable annotations the constraint row carries no attribute, so
// only the constraint fact itself differs.
public class ConstraintType
{
    public void Value<T>() where T : IDisposable { }
}
#nullable restore

public class ConstantValue
{
    public const int Value = 1;
}

public class DefaultParameter
{
    public int Value(int x = 1) => x;
}

public class ParameterName
{
    public int Value(int before) => before;
}

public class PublicAttribute
{
    [Obsolete("before")]
    public int Value() => 1;
}

public class InterfaceAdded
{
}

internal class BecomesPublic
{
}

public class Outer
{
    public class Inner
    {
        public int Value(int x) => x + 1;
    }
}

public class Generic<T>
{
    public T Value(T x) => x;
}

internal class InternalBodyOnly
{
    public int Value(int x) => x + 1;
}

// Roslyn emits NullableContext on NullableOuter only; Inner inherits it.
public class NullableOuter
{
    public string A(string x) => x!;
    public string B(string x) => x!;
    public string C(string x) => x!;

    public class Inner
    {
        public string X(string x) => x!;
        public string Y(string x) => x!;
        public string Z(string x) => x!;
    }
}

public class Removed
{
}

// A hidden public Member is on the API axis, marked hidden.
public class HiddenMemberChanged
{
    public int Value(int x) => x;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Hidden(int before) => before;
}

// A hidden public Type is on the API axis, marked hidden.
[EditorBrowsable(EditorBrowsableState.Never)]
public class HiddenTypeChanged
{
    public int Value(int before) => before;
}

// A protected Member is on the API axis.
public class ProtectedMemberChanged
{
    protected int Value(int before) => before;
}

// A private protected Member is not on the API axis.
public class PrivateProtectedMemberAdded
{
    public int Value(int x) => x;
}

// Accessibility changes within the API axis.
public class PublicToProtected
{
    public int Value(int x) => x;
}

public class PublicToProtectedInternal
{
    public int Value(int x) => x;
}

public class ProtectedInternalToProtected
{
    protected internal int Value(int x) => x;
}

public class ProtectedToProtectedInternal
{
    protected int Value(int x) => x;
}

public class ProtectedInternalToPublic
{
    protected internal int Value(int x) => x;
}

public class SetterToProtected
{
    public int Value { get; set; }
}

public class NestedPublicToProtected
{
    public class Nested
    {
    }
}

public class NestedProtectedToPublic
{
    protected class Nested
    {
    }
}

// Accessibility moves that leave or stay outside the API axis.
public class PublicToInternal
{
    public int Value(int x) => x;
}

public class PublicToPrivate
{
    public int Value(int x) => x;
}

public class ProtectedToInternal
{
    protected int Value(int x) => x;
}

public class InternalToPrivate
{
    internal int Value(int x) => x;
}

public class ProtectedToPrivateProtected
{
    protected int Value(int x) => x;
}

public class PublicTypeToInternal
{
}

public class PropertyPublicToProtected
{
    public int Value { get; set; }
}

// A whole property with a narrower accessor moves; only its accessibility changes.
public class PropertyWithPrivateSetterPublicToProtected
{
    public int Value { get; private set; }
}

// Changes wholly outside the API axis.
public class PrivateMemberRemoved
{
    public int Value(int x) => x;

    private int Helper() => 0;
}

public class PrivateMemberChanged
{
    public int Value(int x) => Helper(x);

    private int Helper(int x) => x;
}

internal class InternalTypeRemoved
{
}

internal class InternalTypeMemberChanged
{
    public int Value(int x) => x;
}

internal class InternalOuterNestedMemberChanged
{
    public class Nested
    {
        public int Value(int x) => x;
    }
}

// API status transitions (docs/design/api-status-analysis.md): each changes one
// subject's hidden or obsolete status and nothing else.
public class MemberBecomesHidden
{
    public int Value() => 1;
}

public class MemberBecomesVisible
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Value() => 1;
}

public class TypeBecomesHidden
{
    public int Value() => 1;
}

public class MemberBecomesObsolete
{
    public int Value() => 1;
}

public class ObsoleteWarningToError
{
    [Obsolete("m")]
    public int Value() => 1;
}

public class TypeBecomesObsolete
{
    public int Value() => 1;
}

public class ObsoleteMessageChanged
{
    [Obsolete("before")]
    public int Value() => 1;
}

public class MemberBecomesObsoleteError
{
    public int Value() => 1;
}

// A body present on one side only: an abstract method made virtual.
public abstract class AbstractToVirtual
{
    public abstract int Value(int x);
}

// A body present on one side only: a default interface implementation added.
public interface IDefaultImplementationAdded
{
    int Value(int x);
}

// Ordinary presentation (docs/design/library-api-diff-presentation.md): a public
// nested Type of a hidden Type is outside the ordinary population, and an
// obsolete Member's other changes stay in it.
[EditorBrowsable(EditorBrowsableState.Never)]
public class HiddenOuter
{
    public class Inner
    {
        public int Value(int before) => before;
    }
}

public class ObsoleteMemberChanged
{
    [Obsolete("m")]
    public int Value(int before) => before;
}
