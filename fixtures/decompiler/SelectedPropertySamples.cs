using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ILInspector.Decompiler.Fixtures;

public class SelectedPropertySamples
{
    int _count;
    static int _sharedCount = 7;
    string? _label;

    public virtual int Capacity
    {
        get => _count;
        set => _count = value;
    }

    public int Count
    {
        get => _count;
        private set => _count = value;
    }

    public int InitialCount
    {
        get => _count;
        init => _count = value;
    }

    public int Item => _count;
    public int @event => _count;
    public static int SharedCount => _sharedCount;
    public ref int Storage => ref _count;

    public string? Label
    {
        [System.Diagnostics.DebuggerStepThrough]
        [return: MaybeNull]
        get => _label;
        set => _label = value;
    }

    public int AutoCount { get; private set; }

    [IndexerName("ByIndex")]
    public int this[int index] => _count + index;

    public int get_CapacityLookalike() => _count;
    public void set_CapacityLookalike(int amount) => _count = amount;
}

public class DerivedPropertySamples : SelectedPropertySamples
{
    public override int Capacity => 42;
}

public class NarrowedPropertySamples
{
    int _count;
    int _offset;

    public virtual int Count
    {
        get => _count;
        protected set => _count = value;
    }

    public virtual int Offset
    {
        protected get => _offset;
        set => _offset = value;
    }
}

public class NarrowedOverridePropertySamples : NarrowedPropertySamples
{
    public override int Count
    {
        get => base.Count;
        protected set => base.Count = value;
    }

    public override int Offset
    {
        protected get => base.Offset;
        set => base.Offset = value;
    }
}

public struct ReadonlyPropertySamples
{
    int _count;

    public int Count
    {
        readonly get => _count;
        set => _count = value;
    }
}

public interface IStaticPropertySamples
{
    static int _capacity;

    static virtual int Count => 17;

    static virtual int Capacity
    {
        get => _capacity;
        set => _capacity = value;
    }

    static int FixedCount => 23;
    int InstanceCount => 29;
    sealed int SealedCount => 31;
    private int PrivateCount => 37;

    sealed int SealedCapacity
    {
        get => _capacity;
        set => _capacity = value;
    }
}
