namespace DotnetInspector.Sections.Tests.SymbolFixtures;

// Compiled declarations whose metadata encoding differs from their C#
// spelling, inspected by LibrarySymbolDocumentFixtureTests.

public abstract class CovariantBase
{
    public abstract object Make();
}

public class CovariantDerived : CovariantBase
{
    public override string Make() => "";
}

public abstract class CovariantAbstractDerived : CovariantBase
{
    public override string Make() => "";

    public abstract void Other();
}

public class RefReturns
{
    private int _value;

    public ref int Value() => ref _value;

    public ref int Property => ref _value;
}

public class Indexers
{
    public int this[List<int> key] => key.Count;

    public int this[int[,] key] => key.Length;
}
