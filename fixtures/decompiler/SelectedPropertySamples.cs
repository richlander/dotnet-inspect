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

public class SelectedAutoPropertySamples
{
    public int Count { get; } = 7;
    public static int SharedCount { get; } = 11;
    public virtual int Limit { get; } = 13;
    public int @event { get; } = 17;
    public string? Label
    {
        [System.Diagnostics.DebuggerStepThrough]
        [return: MaybeNull]
        get;
    }

    public int MutableCount { get; set; }
    public int InitialCount { get; init; }

    public int ComputedCount
    {
        [CompilerGenerated]
        get => field + 1;
    }

    [field: System.ComponentModel.Description("retained field contract")]
    public int DescribedCount { get; }

    [field: System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.RootHidden)]
    public int DebugCount { get; }
}

public class DerivedAutoPropertySamples : SelectedAutoPropertySamples
{
    public override int Limit { get; } = 19;
}

public class GenericAutoPropertySamples<T>
{
    public T? Item { get; }
    public static int SharedCount { get; } = 23;
}

public struct StructAutoPropertySamples
{
    public int Count { get; }
}

public interface IAutoPropertySample
{
    int Count { get; }
}

public class ExplicitAutoPropertySamples : IAutoPropertySample
{
    int IAutoPropertySample.Count { get; } = 29;
}

public class SelectedFieldPropertySamples
{
    public int Count => field + 1;
    public static int SharedCount => field + 2;
    public int RepeatedCount => field + field;
    public int CheckedCount => checked(field + 1);
    public string? Label => field ?? "this.Label / field";
    public int @event => field + 3;
    public int BranchedCount => field > 0 ? field : -1;
    public int ChangingCount => ++field;
    public string? LazyLabel => field ??= "initialized";
    public int MutableCount { get => field + 1; set; }
    public int InitialCount { get => field + 1; init; }

    [field: System.ComponentModel.Description("preserve storage contract")]
    public int DescribedCount => field + 1;

    public int AttributedCount
    {
        [System.Diagnostics.DebuggerStepThrough]
        get => field + 1;
    }

    public int NestedCount
    {
        get
        {
            int Read() => field + 1;
            return Read();
        }
    }

    public int ProtectedCount
    {
        get
        {
            try { return field + 1; }
            finally { System.GC.KeepAlive(0); }
        }
    }

#pragma warning disable CS9258 // Intentionally retain a PDB local whose name conflicts with accessor storage.
    public int ShadowedCount
    {
        get
        {
            int @field = System.Math.Abs(field);
            return field + @field;
        }
    }
#pragma warning restore CS9258
}

public class GenericFieldPropertySamples<T> where T : class
{
    public T? Value => field is not null ? field : default;
    public static int SharedCount => field + 2;
}

public readonly struct ReadonlyFieldPropertySamples
{
    public int Count => field + 1;
}

public class DerivedFieldPropertySamples : SelectedAutoPropertySamples
{
    public override int Limit => field + 1;
}

public class ExplicitFieldPropertySamples : IAutoPropertySample
{
    int IAutoPropertySample.Count => field + 1;
}
