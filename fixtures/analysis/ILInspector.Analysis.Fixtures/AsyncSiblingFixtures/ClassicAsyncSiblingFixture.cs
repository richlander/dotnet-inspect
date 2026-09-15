using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

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

    [CompilerGenerated]
    public static class CompilerGeneratedAsyncOwnerContainer
    {
        public static int Read(int value) => value;

        public static Task<int> ReadAsync(int value) =>
            Task.FromResult(value);

        public static async Task<int> AnalyzeAsync(int value)
        {
            await Task.Yield();
            return Read(value);
        }
    }

    public static async Task<int> CallsSyncSiblingFromAsync(
        int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }

    public static async Task<string>
        ReturnsCallStoredBeforeAwait()
    {
        string payload = ProducePayload();
        await Task.Yield();
        return payload;
    }

    public static async Task<string>
        ReturnsCallStoredBeforeMultipleAwaits()
    {
        string payload = ProducePayload();
        await Task.Yield();
        await Task.Yield();
        return payload;
    }

    public static async Task<string>
        ReturnsCallStoredAcrossFinally()
    {
        string payload = ProducePayload();
        try
        {
            await Task.Yield();
        }
        finally
        {
            GC.KeepAlive(payload);
        }
        return payload;
    }

    public static async Task<string>
        DoesNotBorrowUnrelatedFieldStore(string payload)
    {
        string unrelated = ProducePayload();
        await Task.Yield();
        GC.KeepAlive(unrelated);
        return payload;
    }

    public static async Task<string>
        HasMultipleStoresBeforeAwait(bool first)
    {
        string payload;
        if (first)
            payload = ProducePayload();
        else
            payload = ProduceOtherPayload();
        await Task.Yield();
        return payload;
    }

    public static async Task<string>
        ConditionallyOverwritesParameterBeforeAwait(string payload)
    {
        if (payload.Length > 3)
            payload = ProducePayload();
        await Task.Yield();
        return payload;
    }

    public static async Task<string>
        ConditionallySuspendsAfterParameterOverwrite(
            string payload,
            bool serialize)
    {
        if (serialize)
        {
            payload = ProducePayload();
            await Task.Yield();
        }
        return payload;
    }

    public static async Task<string?>
        ConditionallyInitializesLocalBeforeSuspension(
            bool serialize)
    {
        string? payload = null;
        if (serialize)
        {
            payload = ProducePayload();
            await Task.Yield();
        }
        return payload;
    }

    public static async Task<string>
        MutatesFieldByReferenceAfterAwait()
    {
        string payload = ProducePayload();
        await Task.Yield();
        ReplacePayload(ref payload);
        return payload;
    }

    public static async Task<string>
        UsesSecondaryBuilderAfterAwait()
    {
        string payload = ProducePayload();
        await Task.Yield();
        AsyncTaskMethodBuilder<string> other =
            AsyncTaskMethodBuilder<string>.Create();
        other.SetResult(payload);
        return payload;
    }

    public static async Task<string>
        StoresInLoopBeforeAwait(int count)
    {
        string payload;
        do
        {
            payload = ProducePayload();
            count--;
        }
        while (count > 0);
        await Task.Yield();
        return payload;
    }

    public static async AnalysisCustomTask<string>
        UsesCustomAsyncBuilder()
    {
        string payload = ProducePayload();
        await Task.Yield();
        return payload;
    }

    [AsyncStateMachine(
        typeof(CustomBuilderSecondaryStateMachine))]
    public static AnalysisCustomTask<string>
        CustomBuilderSecondarySource() =>
        default;

    [CompilerGenerated]
    public struct CustomBuilderSecondaryStateMachine :
        IAsyncStateMachine
    {
        public AsyncTaskMethodBuilder<string> SecondaryBuilder;
        public string Payload;
        public YieldAwaitable.YieldAwaiter Awaiter;

        public void MoveNext()
        {
            Payload = ProducePayload();
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            Awaiter = awaiter;
            SecondaryBuilder.AwaitUnsafeOnCompleted(
                ref awaiter,
                ref this);
            SecondaryBuilder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            SecondaryBuilder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(ExternalAddressStateMachine))]
    public static Task<string> ExternalAddressSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct ExternalAddressStateMachine : IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload;
        public YieldAwaitable.YieldAwaiter Awaiter;

        public void MoveNext()
        {
            if (State == 0)
                goto Resume;

            Payload = ProducePayload();
            State = 0;
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            Awaiter = awaiter;
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            return;

        Resume:
            Corrupt();
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);

        void Corrupt() => ReplacePayload(ref Payload);
    }

    [AsyncStateMachine(
        typeof(MismatchedBuilderStateMachine))]
    public static Task<string> MismatchedBuilderSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct MismatchedBuilderStateMachine : IAsyncStateMachine
    {
        public AsyncValueTaskMethodBuilder<string> Builder;
        public string Payload;
        public YieldAwaitable.YieldAwaiter Awaiter;

        public void MoveNext()
        {
            Payload = ProducePayload();
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            Awaiter = awaiter;
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(ReenteringCleanupStateMachine))]
    public static Task<string> ReenteringCleanupSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct ReenteringCleanupStateMachine : IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload;
        public bool ClearPayload;
        public YieldAwaitable.YieldAwaiter Awaiter;

        public void MoveNext()
        {
            if (State == 0)
                goto Complete;

            Payload = ProducePayload();
            State = 0;
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            Awaiter = awaiter;
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            return;

        Complete:
            Builder.SetResult(Payload);
            if (ClearPayload)
            {
                Payload = null!;
                ClearPayload = false;
                goto Complete;
            }
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(MixedSuspensionBuilderStateMachine))]
    public static Task<string> MixedSuspensionBuilderSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct MixedSuspensionBuilderStateMachine :
        IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public AsyncValueTaskMethodBuilder<string> OtherBuilder;
        public string Payload;

        public void MoveNext()
        {
            if (State == 0)
                goto Serialize;
            if (State == 1)
                goto Complete;

            Payload = ProducePayload();
            YieldAwaitable.YieldAwaiter otherAwaiter =
                Task.Yield().GetAwaiter();
            State = 0;
            OtherBuilder.AwaitUnsafeOnCompleted(
                ref otherAwaiter,
                ref this);
            return;

        Serialize:
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            State = 1;
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            return;

        Complete:
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(ImmediateCompletionStateMachine))]
    public static Task<string> ImmediateCompletionSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct ImmediateCompletionStateMachine :
        IAsyncStateMachine
    {
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload;

        public void MoveNext()
        {
            Payload = ProducePayload();
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(WrongStateMachineArgumentStateMachine))]
    public static Task<string>
        WrongStateMachineArgumentSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct WrongStateMachineArgumentStateMachine :
        IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload;

        public void MoveNext()
        {
            if (State == 0)
                goto Complete;

            Payload = ProducePayload();
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            State = 0;
            WrongStateMachineArgumentStateMachine other =
                default;
            Builder.AwaitUnsafeOnCompleted(
                ref awaiter,
                ref other);
            return;

        Complete:
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(AddressMutatedReferenceStateMachine))]
    public static Task<string>
        AddressMutatedReferenceStateMachineSource()
    {
        var machine = new AddressMutatedReferenceStateMachine
        {
            Builder = AsyncTaskMethodBuilder<string>.Create(),
            State = -1,
        };
        machine.Builder.Start(ref machine);
        return machine.Builder.Task;
    }

    [CompilerGenerated]
    public sealed class AddressMutatedReferenceStateMachine :
        IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload = "";

        public void MoveNext()
        {
            if (State == 0)
                goto Complete;

            Payload = ProducePayload();
            State = 0;
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            AddressMutatedReferenceStateMachine machine = this;
            ReplaceStateMachine(ref machine, Builder);
            Builder.AwaitUnsafeOnCompleted(
                ref awaiter,
                ref machine);
            return;

        Complete:
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    static void ReplaceStateMachine(
        ref AddressMutatedReferenceStateMachine machine,
        AsyncTaskMethodBuilder<string> builder) =>
        machine = new AddressMutatedReferenceStateMachine
        {
            Builder = builder,
            State = 0,
        };

    [AsyncStateMachine(
        typeof(WholeInstanceWriteStateMachine))]
    public static Task<string>
        WholeInstanceWriteStateMachineSource()
    {
        WholeInstanceWriteStateMachine machine = default;
        machine.Builder =
            AsyncTaskMethodBuilder<string>.Create();
        machine.State = -1;
        machine.Builder.Start(ref machine);
        return machine.Builder.Task;
    }

    [CompilerGenerated]
    public struct WholeInstanceWriteStateMachine :
        IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload;

        public void MoveNext()
        {
            if (State == 0)
                goto Complete;

            Payload = ProducePayload();
            State = 0;
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            Builder.AwaitUnsafeOnCompleted(
                ref awaiter,
                ref this);
            return;

        Complete:
            Clear();
            Builder.SetResult(Payload!);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);

        void Clear()
        {
            AsyncTaskMethodBuilder<string> builder = Builder;
            this = default;
            Builder = builder;
        }
    }

    [AsyncStateMachine(
        typeof(NonGenericSuspensionBuilderStateMachine))]
    public static Task<string>
        NonGenericSuspensionBuilderSource()
    {
        NonGenericSuspensionBuilderStateMachine machine = default;
        machine.Builder =
            AsyncTaskMethodBuilder<string>.Create();
        machine.State = -1;
        machine.Builder.Start(ref machine);
        return machine.Builder.Task;
    }

    [CompilerGenerated]
    public struct NonGenericSuspensionBuilderStateMachine :
        IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public AsyncTaskMethodBuilder OtherBuilder;
        public string Payload;

        public void MoveNext()
        {
            if (State == 0)
                goto Second;
            if (State == 1)
                goto Complete;

            Payload = ProducePayload();
            State = 0;
            YieldAwaitable.YieldAwaiter first =
                Task.Yield().GetAwaiter();
            Builder.AwaitUnsafeOnCompleted(
                ref first,
                ref this);
            return;

        Second:
            State = 1;
            YieldAwaitable.YieldAwaiter second =
                Task.Yield().GetAwaiter();
            OtherBuilder.AwaitUnsafeOnCompleted(
                ref second,
                ref this);
            return;

        Complete:
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);
    }

    [AsyncStateMachine(
        typeof(FailedExternalStoreStateMachine))]
    public static Task<string> FailedExternalStoreSource() =>
        Task.FromResult("raw");

    [CompilerGenerated]
    public struct FailedExternalStoreStateMachine :
        IAsyncStateMachine
    {
        public int State;
        public AsyncTaskMethodBuilder<string> Builder;
        public string Payload;

        public void MoveNext()
        {
            if (State == 0)
                goto Complete;

            Payload = ProducePayload();
            YieldAwaitable.YieldAwaiter awaiter =
                Task.Yield().GetAwaiter();
            State = 0;
            Builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
            return;

        Complete:
            Builder.SetResult(Payload);
        }

        public void SetStateMachine(
            IAsyncStateMachine stateMachine) =>
            Builder.SetStateMachine(stateMachine);

        void Corrupt(
            int first,
            long second,
            string third,
            object fourth,
            Guid fifth)
        {
            Probe();
            Payload = ProduceOtherPayload();
            GC.KeepAlive(
                (first, second, third, fourth, fifth));
        }

        static void Probe()
        {
        }
    }

    public static async Task<string>
        StoresAfterDifferentSuspension()
    {
        await Task.Yield();
        string payload = ProducePayload();
        await Task.Yield();
        return payload;
    }

    static string ProducePayload() => "payload";

    static string ProduceOtherPayload() => "other";

    static void ReplacePayload(ref string payload) =>
        payload = "replacement";

    public static async Task<bool> AsyncGenBoxed<T>(
        T left,
        T right)
    {
        await Task.Yield();
        return left!.Equals(right);
    }

    [CompilerGenerated]
    public static async Task<int> CompilerGeneratedAsyncOwner(
        int value)
    {
        await Task.Yield();
        Func<int> capture = () => value;
        return ReadValue(capture());
    }

    [GeneratedCode("ILInspector.Analysis.Fixtures", "1.0")]
    public static Func<int, Task<int>>
        GeneratedUltimateAsyncOwner()
    {
        return Child;

        static async Task<int> Child(int value)
        {
            await Task.Yield();
            return GeneratedRead(value);
        }
    }

    public static int GeneratedRead(int value) =>
        value;

    public static Task<int> GeneratedReadAsync(int value) =>
        Task.FromResult(value);

    public static Action<Task> AwaitTaskInAsyncLambda() =>
        async task => await task;

    internal static Action<Task> ScopedAsyncLambdaOwner(
        string marker) =>
        async task => await task;

    internal static Action<Task> ScopedCapturingAsyncLambdaOwner(
        string marker) =>
        async task =>
        {
            _ = marker;
            await task;
        };

    internal static Func<Task<int>>
        ScopedAsyncLambdaRecommendationOwner() =>
        async () =>
        {
            await Task.Yield();
            return ReadValue(42);
        };

    internal static Func<int, object>
        ScopedAllocationHotspotLambdaOwner() =>
        count =>
        {
            var items = new List<object>();
            for (int i = 0; i < count; i++)
            {
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
            }
            return items;
        };

    internal static Func<int, Task<object>>
        ScopedAsyncAllocationHotspotLambdaOwner() =>
        async count =>
        {
            var items = new List<object>();
            await Task.Yield();
            for (int i = 0; i < count; i++)
            {
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                items.Add(new object());
                Action capture = () => GC.KeepAlive(i);
                capture();
            }
            return items;
        };

    internal static Func<Task<object>>
        ScopedAsyncLocalAllocationOwner()
    {
        async Task<object> BuildAsync()
        {
            await Task.Yield();
            return new object();
        }

        return BuildAsync;
    }

    internal static IEnumerable<Task<object>>
        ScopedIteratorAsyncLocalAllocationOwner()
    {
        async Task<object> BuildAsync()
        {
            await Task.Yield();
            return new object();
        }

        yield return BuildAsync();
    }

    internal static Func<Task<object>>
        ScopedIndirectAsyncLocalAllocationOwner()
    {
        async Task<object> BuildAsync()
        {
            await Task.Yield();
            return new object();
        }

        return async () =>
        {
            await Task.Yield();
            return await BuildAsync();
        };
    }

    internal static Func<Task<object>>
        ScopedNestedAsyncLocalAllocationOwner()
    {
        Func<Task<object>> BuildFactory()
        {
            async Task<object> BuildAsync()
            {
                await Task.Yield();
                return new object();
            }

            return BuildAsync;
        }

        return BuildFactory();
    }

    internal static IEnumerable<Task<object>>
        ScopedIteratorFinallyAsyncLocalAllocationOwner()
    {
        async Task<object> BuildAsync()
        {
            await Task.Yield();
            return new object();
        }

        try
        {
            yield return Task.FromResult<object>(new object());
        }
        finally
        {
            GC.KeepAlive(BuildAsync());
        }
    }

    internal static IEnumerable<Task<object>>
        ScopedGenericIteratorFinallyAsyncLocalAllocationOwner<T>()
    {
        async Task<object> BuildAsync()
        {
            await Task.Yield();
            return new object[1];
        }

        try
        {
            yield return Task.FromResult<object>(
                typeof(T));
        }
        finally
        {
            GC.KeepAlive(BuildAsync());
        }
    }

    internal static Func<Task<object>>
        ScopedCapturedAsyncLocalAllocationOwner(int marker)
    {
        async Task<object> BuildAsync()
        {
            await Task.Yield();
            GC.KeepAlive(marker);
            return new object[1];
        }

        return BuildAsync;
    }

    internal static Func<int>
        SharedLambdaOrdinalOwner() =>
        static () => 42;

    public static Task ScopedAsyncLambdaOwner(int marker) =>
        Task.CompletedTask;

    public static int CallsThroughLocalFunction(int value)
    {
        int Core(int v) => ReadValue(v);
        return Core(value);
    }

    internal static class GenericIteratorOwner<T>
    {
        internal static IEnumerable<Task<object>>
            ScopedIteratorFinallyAsyncLocalAllocationOwner()
        {
            async Task<object> BuildAsync()
            {
                await Task.Yield();
                return new object();
            }

            try
            {
                yield return Task.FromResult<object>(
                    typeof(T));
            }
            finally
            {
                GC.KeepAlive(BuildAsync());
            }
        }
    }

    [GeneratedCode("ILInspector.Analysis.Fixtures", "1.0")]
    internal static class GeneratedAsyncIteratorOwner
    {
        internal static async IAsyncEnumerable<object>
            StreamAsync()
        {
            async Task<object> BuildAsync()
            {
                await Task.Yield();
                return new object();
            }

            await Task.Yield();
            yield return await BuildAsync();
        }
    }

    public static int CallsThroughSiblingLocalFunctions(int value)
    {
        return First(value);

        static int First(int v) => Second(v);
        static int Second(int v) =>
            v > 0 ? First(v - 1) : ReadValue(v);
    }

    public static async Task<int> AsyncOwnerCallsThroughLocalFunction(
        int value)
    {
        await Task.Yield();
        int offset = value;
        return Core();

        int Core() => ReadValue(offset);
    }

    public static async Task<int> AsyncOwnerCallsThroughAsyncLambda(
        int value)
    {
        await Task.Yield();
        Func<Task<int>> core = async () =>
        {
            await Task.Yield();
            return ReadValue(value);
        };
        return await core();
    }

    public static async Task<int> AsyncLiftedFunctionCallsSibling(
        int value)
    {
        return await Outer(value);

        static async Task<int> Outer(int v)
        {
            await Task.Yield();
            return Inner(v);
        }

        static int Inner(int v) => ReadValue(v);
    }

    [AsyncStateMachine(typeof(ExplicitMoveNextStateMachine))]
    public static void ExplicitMoveNextSource()
    {
    }

    internal static async Task<int> ScopedAsyncLocalOwner(string marker)
    {
        await Task.Yield();
        return Core(marker.Length);

        static int Core(int value) => ReadValue(value);
    }

    public static async Task<int> ScopedAsyncLocalOwner(int marker)
    {
        await Task.Yield();
        return Core(marker);

        static int Core(int value) => ReadValue(value);
    }

    private struct ExplicitMoveNextStateMachine : IAsyncStateMachine
    {
        void IAsyncStateMachine.MoveNext() => ReadValue(1);

        public void MoveNext(int value) => ReadValue(value);

        void IAsyncStateMachine.SetStateMachine(
            IAsyncStateMachine stateMachine)
        {
        }
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

[AsyncMethodBuilder(typeof(AnalysisCustomTaskMethodBuilder<>))]
public readonly struct AnalysisCustomTask<T>
{
    readonly Task<T> _task;

    internal AnalysisCustomTask(Task<T> task) =>
        _task = task;

    public TaskAwaiter<T> GetAwaiter() =>
        _task.GetAwaiter();
}

public struct AnalysisCustomTaskMethodBuilder<T>
{
    AsyncTaskMethodBuilder<T> _builder;

    public static AnalysisCustomTaskMethodBuilder<T> Create() =>
        new()
        {
            _builder = AsyncTaskMethodBuilder<T>.Create(),
        };

    public AnalysisCustomTask<T> Task =>
        new(_builder.Task);

    public void SetResult(T result) =>
        _builder.SetResult(result);

    public void SetException(Exception exception) =>
        _builder.SetException(exception);

    public void SetStateMachine(IAsyncStateMachine stateMachine) =>
        _builder.SetStateMachine(stateMachine);

    public void Start<TStateMachine>(
        ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine =>
        _builder.Start(ref stateMachine);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _builder.AwaitOnCompleted(
            ref awaiter,
            ref stateMachine);

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _builder.AwaitUnsafeOnCompleted(
            ref awaiter,
            ref stateMachine);
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
