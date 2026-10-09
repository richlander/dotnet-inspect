// Fast Diff fixture, version 2. Each Type isolates one kind of change; the
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

public class Added
{
}
