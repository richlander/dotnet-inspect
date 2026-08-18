namespace ILInspector.Analysis.ClassicAsyncFixtures;

public static class ClassicAsyncSiblingFixture
{
    public static int ReadValue(int value)
        => value;

    public static Task<int> ReadValueAsync(
        int value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    public static async Task<int> CallsSyncSiblingFromAsync(
        int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }

    public static void ReadByRef(ref int value)
        => value++;

    public static Task ReadByRefAsync(out int value)
    {
        value = 42;
        return Task.CompletedTask;
    }

    public static async Task CallsRefWithOutSiblingAsync()
    {
        await Task.Yield();
        int value = 0;
        ReadByRef(ref value);
    }

    public static void ReadCompatibleByRef(ref int value)
        => value++;

    public static Task ReadCompatibleByRefAsync(ref int value)
    {
        value++;
        return Task.CompletedTask;
    }

    public static async Task CallsCompatibleRefSiblingAsync()
    {
        await Task.Yield();
        int value = 0;
        ReadCompatibleByRef(ref value);
    }
}

public sealed class ClassicGenericSelfSiblingFixture<T>
{
    public int Read(T value) => 0;

    public async Task<int> ReadAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public static class ClassicGenericMethodSelfSiblingFixture
{
    public static T Read<T>(T value) => value;

    public static async Task<T> ReadAsync<T>(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public interface IClassicGenericInterfaceSelfSiblingFixture
{
    T Load<T>(T key);
    Task<T> LoadAsync<T>(T key);
}

public sealed class ClassicGenericInterfaceSelfSiblingFixture
    : IClassicGenericInterfaceSelfSiblingFixture
{
    public T Load<T>(T key) => key;

    public async Task<T> LoadAsync<T>(T key)
    {
        await Task.Yield();
        return ((IClassicGenericInterfaceSelfSiblingFixture)this)
            .Load(key);
    }
}

public interface IClassicGenericExplicitSelfSiblingFixture
{
    T Fetch<T>(T key);
    Task<T> FetchAsync<T>(T key);
}

public sealed class ClassicGenericExplicitSelfSiblingFixture
    : IClassicGenericExplicitSelfSiblingFixture
{
    T IClassicGenericExplicitSelfSiblingFixture.Fetch<T>(
        T key)
        => key;

    async Task<T>
        IClassicGenericExplicitSelfSiblingFixture.FetchAsync<T>(
            T key)
    {
        await Task.Yield();
        return ((IClassicGenericExplicitSelfSiblingFixture)this)
            .Fetch(key);
    }
}

public class ClassicGenericVirtualSelfSiblingFixture
{
    public virtual T Lookup<T>(T key) => key;

    public virtual async Task<T> LookupAsync<T>(T key)
    {
        await Task.Yield();
        return Lookup(key);
    }
}

public sealed class ClassicGenericVirtualDerivedSelfSiblingFixture
    : ClassicGenericVirtualSelfSiblingFixture
{
    public override async Task<T> LookupAsync<T>(T key)
    {
        await Task.Yield();
        return base.Lookup(key);
    }
}

public sealed class ClassicStateMachineCollision
{
    public int Read(int value) => value;

    public Task<int> ReadAsync(string value)
        => Task.FromResult(value.Length);

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public sealed class ClassicStateMachineCollision<T>
{
    public int Read(T value) => 0;

    public Task<int> ReadAsync(T value)
        => Task.FromResult(Read(value));

    public async Task<int> AnalyzeAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public interface IClassicInterfaceCacheFixture
{
    int Read();
    Task<int> ReadAsync();
}

public sealed class ClassicInterfaceCacheFixture
    : IClassicInterfaceCacheFixture
{
    public int Read() => 0;

    public async Task<int> AaaOtherAsync()
    {
        await Task.Yield();
        return ((IClassicInterfaceCacheFixture)this).Read();
    }

    public async Task<int> ReadAsync()
    {
        await Task.Yield();
        return ((IClassicInterfaceCacheFixture)this).Read();
    }
}

public sealed class ClassicSelfCacheFixture
{
    public int Aaa() => 0;

    public async Task<int> AaaAsync()
    {
        await Task.Yield();
        return Aaa();
    }

    public async Task<int> ZzzAnalyzeAsync()
    {
        await Task.Yield();
        return Aaa();
    }
}

public class ClassicProtectedSiblingBaseFixture
{
    protected int Read(int value) => value;

    protected Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class ClassicProtectedSiblingDerivedFixture
    : ClassicProtectedSiblingBaseFixture
{
    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public class ClassicPrivateProtectedSiblingBaseFixture
{
    private protected int Read(int value) => value;

    private protected Task<int> ReadAsync(int value)
        => Task.FromResult(value);
}

public sealed class ClassicPrivateProtectedSiblingDerivedFixture
    : ClassicPrivateProtectedSiblingBaseFixture
{
    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public interface IClassicCovariantInterfaceSelfSiblingFixture<out T>
{
    object Read();
    Task<object> ReadAsync();
}

public sealed class ClassicCovariantInterfaceSelfSiblingFixture
    : IClassicCovariantInterfaceSelfSiblingFixture<string>
{
    public object Read() => "";

    public async Task<object> ReadAsync()
    {
        await Task.Yield();
        return ((IClassicCovariantInterfaceSelfSiblingFixture<object>)this)
            .Read();
    }
}

public class ClassicProtectedReceiverBaseFixture
{
    public int Read() => 0;

    protected Task<int> ReadAsync()
        => Task.FromResult(0);
}

public sealed class ClassicProtectedReceiverDerivedFixture
    : ClassicProtectedReceiverBaseFixture
{
    public async Task<int> AnalyzeAsync(
        ClassicProtectedReceiverBaseFixture other)
    {
        await Task.Yield();
        return other.Read();
    }
}

public class ClassicProtectedStaticSiblingBaseFixture
{
    public static int Read() => 0;

    protected static Task<int> ReadAsync()
        => Task.FromResult(0);
}

public sealed class ClassicProtectedStaticSiblingDerivedFixture
    : ClassicProtectedStaticSiblingBaseFixture
{
    public async Task<int> AnalyzeAsync()
    {
        await Task.Yield();
        return Read();
    }
}

public interface IClassicContravariantDefaultSiblingFixture<in T>
{
    void Consume(T value);

    async Task ConsumeAsync(T value)
    {
        await Task.Yield();
        ((IClassicContravariantDefaultSiblingFixture<string>)(object)this)
            .Consume("");
    }
}

public sealed class ClassicContravariantDefaultSiblingFixture
    : IClassicContravariantDefaultSiblingFixture<object>
{
    public void Consume(object value)
    {
    }
}

public interface IClassicDefaultSiblingFirstFixture
{
    int Read();

    async Task<int> ReadAsync()
    {
        await Task.Yield();
        return Read();
    }
}

public interface IClassicDefaultSiblingSecondFixture
{
    Task<int> ReadAsync();
}

public sealed class ClassicUnrelatedExplicitDefaultSiblingFixture
    : IClassicDefaultSiblingFirstFixture,
        IClassicDefaultSiblingSecondFixture
{
    public int Read() => 0;

    async Task<int>
        IClassicDefaultSiblingSecondFixture.ReadAsync()
    {
        await Task.Yield();
        return ((IClassicDefaultSiblingFirstFixture)this)
            .Read();
    }
}

public sealed class ClassicNestedPrivateSiblingFixture
{
    private int Read() => 0;

    private Task<int> ReadAsync()
        => Task.FromResult(0);

    public sealed class Consumer
    {
        public async Task<int> AnalyzeAsync(
            ClassicNestedPrivateSiblingFixture other)
        {
            await Task.Yield();
            return other.Read();
        }
    }
}

public sealed class ClassicOuterToNestedPrivateSiblingFixture
{
    public sealed class Provider
    {
        public int Read() => 0;

        private Task<int> ReadAsync()
            => Task.FromResult(0);
    }

    public async Task<int> AnalyzeAsync(Provider provider)
    {
        await Task.Yield();
        return provider.Read();
    }
}

public sealed class ClassicSiblingNestedPrivateSiblingFixture
{
    public sealed class Provider
    {
        public int Read() => 0;

        private Task<int> ReadAsync()
            => Task.FromResult(0);
    }

    public sealed class Consumer
    {
        public async Task<int> AnalyzeAsync(Provider provider)
        {
            await Task.Yield();
            return provider.Read();
        }
    }
}

public interface IClassicHiddenBaseSiblingFixture
{
    int Read();

    async Task<int> ReadAsync()
    {
        await Task.Yield();
        return 1;
    }
}

public interface IClassicHiddenDerivedSiblingFixture
    : IClassicHiddenBaseSiblingFixture
{
    new async Task<int> ReadAsync()
    {
        await Task.Yield();
        return ((IClassicHiddenBaseSiblingFixture)this)
            .Read();
    }
}
