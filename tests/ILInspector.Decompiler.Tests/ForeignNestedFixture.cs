namespace Repro;

// Fixture for #1581: a non-generic nested type referenced from inside its
// enclosing scope (Outer.InScope) and from a foreign scope (Other.ForeignReturn).
// The in-scope body may render the bare innermost name `Inner`; the foreign body
// must qualify through the declaring chain (`Outer.Inner`), or `Inner` is not in
// scope inside `Repro.Other` and the Full-looking body is invalid C#.
public sealed class Outer
{
    public sealed class Inner
    {
        public int Value;
    }

    public static Inner InScope()
        => new Inner { Value = 1 };
}

public sealed class Other
{
    public static Outer.Inner ForeignReturn()
        => new Outer.Inner { Value = 2 };
}
