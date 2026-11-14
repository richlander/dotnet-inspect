namespace DotnetInspector.Sections.Tests.ApiReferenceFixtures;

// Compiled declarations whose metadata encoding differs from their C#
// spelling, inspected by LibraryApiReferenceFixtureTests.

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

public static class ClassicExtensions
{
    public static string Classic(this int receiver, out string result)
    {
        result = "";
        return receiver.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public interface IReabstractBase
{
    void M();
}

public interface IReabstractDefault : IReabstractBase
{
    void IReabstractBase.M() { }
}

public interface IReabstracted : IReabstractDefault
{
    abstract void IReabstractBase.M();
}

public interface IReabstractedAncestorFirst : IReabstractDefault, IReabstracted { }

public interface IReabstractedDescendantFirst : IReabstracted, IReabstractDefault { }

public interface IDischargedAncestorFirst : IReabstractBase, IReabstractDefault { }

public interface IDischargedDescendantFirst : IReabstractDefault, IReabstractBase { }

public interface IGenericSlot<T>
{
    void M(T value);
}

public interface IOneConstructedSlot : IGenericSlot<int>, IGenericSlot<string>
{
    void IGenericSlot<int>.M(int value) { }
}

public interface IBothConstructedSlots : IGenericSlot<int>, IGenericSlot<string>
{
    void IGenericSlot<int>.M(int value) { }

    void IGenericSlot<string>.M(string value) { }
}

public interface IStaticSlot
{
    static abstract int M();
}

public interface IStaticReabstraction : IStaticSlot
{
    static abstract int IStaticSlot.M();
}

public interface IProducer
{
    RefReturns Produced { get; }
}

public class ExplicitProducer : IProducer
{
    RefReturns IProducer.Produced => new();
}

public abstract unsafe class FunctionIdentityCollision
{
    public abstract void M(delegate*<int, int> callback);

    public abstract void M(delegate*<long, long> callback);
}
