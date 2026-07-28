using System;
using System.Reflection;

// Assembly-level attribute text reaches the library "Library Info" table, which
// read these strings raw before #3319. It is a channel distinct from the member
// literals below: it never passes through a C# literal escaper.
[assembly: AssemblyCompany("Comp\u202EINJECTEDCOMPANY")]
[assembly: AssemblyProduct("Prod\u000BINJECTEDPRODUCT")]
[assembly: AssemblyCopyright("Copy\u202EINJECTEDCOPYRIGHT")]

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

/// <summary>
/// Docs‮INJECTEDTYPEDOC summary.
/// </summary>
public static class HostileDocs
{
    /// <summary>
    /// Member‮INJECTEDMEMBERDOC summary.
    /// </summary>
    /// <returns>Ret‮INJECTEDRETURNSDOC value.</returns>
    public static string Documented() => "ok";
}

public static class HostileBodyLiterals
{
    // A bidi override inside a method-body string literal survives into the
    // decompiled source that renders inside a Markdown code fence.
    public static string Literal() => "Body\u202EINJECTEDBODYLITERAL";

    public static char Character() => '\u202E';
}

// A hostile *identifier* cannot come from this fixture. C# admits Unicode
// category Cf in identifiers, so `Evt\u202EName` compiles -- but ECMA-334
// requires identifiers to be normalized with formatting characters removed, so
// the emitted metadata name is plain `EvtName` and a gate built on it would be
// vacuous. Hostile identifiers must be synthesized directly into metadata; see
// UntrustedLibraryViewContainmentTests.WriteHostileLibrary.
