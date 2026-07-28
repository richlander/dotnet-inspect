using System;

namespace DotnetInspector.HostileNameFixtures;

public sealed class MarkAttribute : Attribute
{
    public MarkAttribute(string value) => Value = value;

    public string Value { get; }
}

public static class HostileLiterals
{
    // U+202E is a bidi override and U+000B a vertical tab. Both are safe as C#
    // source but rewrite the terminal once rendered (issue #3319).
    public static void WithDefault(string arg = "Def\u202EINJECTEDDEFAULT\u000B")
    {
    }

    [Obsolete("Obs\u202EINJECTEDOBSOLETE\u000B")]
    public static void Deprecated()
    {
    }

    [Mark("Attr\u202EINJECTEDATTRIBUTE\u000B")]
    public static void Marked()
    {
    }
}
