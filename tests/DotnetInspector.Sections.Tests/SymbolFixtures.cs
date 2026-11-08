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

public unsafe class FunctionBase
{
    public virtual void M(delegate*<int, int> callback) { }
}

public abstract unsafe class FunctionMiddle : FunctionBase
{
    public abstract void M(delegate*<long, long> callback);
}

public abstract unsafe class FunctionDerived : FunctionMiddle
{
    public override void M(delegate*<int, int> callback) { }
}

public static unsafe class Pointers
{
    public static void P(int* pointer) { }

    public static void F(delegate*<int, long> callback) { }
}

public interface IStaticMembers
{
    static abstract void Abstract();

    static virtual int Virtual() => 0;
}

public unsafe class ConventionBase
{
    public virtual void M(delegate* unmanaged[Cdecl]<int, int> callback) { }
}

public abstract unsafe class ConventionMiddle : ConventionBase
{
    public abstract void M(delegate* unmanaged[Stdcall]<int, int> callback);
}

public abstract unsafe class ConventionDerived : ConventionMiddle
{
    public override void M(delegate* unmanaged[Cdecl]<int, int> callback) { }
}

public abstract class InternalGetter
{
    public abstract int P { internal get; set; }
}

public class CovariantSealed : CovariantBase
{
    public sealed override string Make() => "";
}

public class InvariantSealed : CovariantBase
{
    public sealed override object Make() => "";
}
