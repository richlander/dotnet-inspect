// Fast Diff fixture, version 1. Each Type isolates one kind of change; the
// expected API and Body states are asserted by FastDiffTests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FastDiffFixture;

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
