using System.Linq.Expressions;

namespace CSharpText.Tests;

public static class UnicodeIdentifierFixtures
{
    public static int CombiningMarkLocal(int value)
    {
        int A\u0301 = value;
        Increment(ref A\u0301);
        return A\u0301;
    }

    static void Increment(ref int value) => value++;

    public static Expression<Func<int, int>> CombiningMarkExpressionTree()
        => A\u0301 => A\u0301 + 1;

    public static object CombiningMarkDynamicMember(dynamic value)
        => value.A\u0301;

    public static object CombiningMarkAnonymousProperty(int A\u0301)
        => new { A\u0301 };
}
