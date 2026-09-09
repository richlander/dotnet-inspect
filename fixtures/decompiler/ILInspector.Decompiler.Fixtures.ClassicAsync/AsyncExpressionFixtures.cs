using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public static class AsyncExpressionFixtures
{
    public static async Task<long> WidenBeforeAdd(Task<int> a, int b)
        => (long)await a + b;

    public static async Task<double> WidenBeforeDivide(Task<int> a, int b)
        => (double)await a / b;

    public static async Task<long> CheckedWidenBeforeAdd(Task<int> a, int b)
        => checked((long)await a + b);

    public static async Task<int> IntegerAdd(Task<int> a, int b)
        => await a + b;

    public static async Task<int> AwaitedLength(Task<string> a)
        => (await a).Length;

    public static async Task<char> AwaitedIndexer(Task<string> a, int index)
        => (await a)[index];

    public static async Task<int> StaticPropertyAfterAwait(Task<int> a)
        => await a + Environment.TickCount;

    public static async Task<int> VirtualPropertyAfterAwait(Task<Reading> a)
        => (await a).Value;

    public static async Task<int> NegateAwaitedValue(Task<int> a)
        => -(await a);

    public static async Task<int> ComplementAwaitedValue(Task<int> a)
        => ~(await a);

    public static async Task<int> AwaitedArrayLength(Task<int[]> a)
        => (await a).Length;

    public static async Task<int> FieldAfterAwait(Task<Reading> a)
        => (await a).FieldValue;

    public static async Task<int> StaticFieldAfterAwait(Task<int> a)
        => await a + Reading.StaticFieldValue;

    public static async Task<int> VolatileFieldAfterAwait(Task<Reading> a)
        => (await a).VolatileValue;

    public static async Task<int> ConfiguredTask(Task<int> a)
        => await a.ConfigureAwait(false);

    public static async Task<int> ConfiguredTaskCapturesContext(Task<int> a)
        => await a.ConfigureAwait(true);

    public static async Task ConfiguredVoidTask(Task a)
        => await a.ConfigureAwait(false);

    public static async Task<int> ConfiguredValueTask(ValueTask<int> a)
        => await a.ConfigureAwait(false);

    public static async Task ConfiguredVoidValueTask(ValueTask a)
        => await a.ConfigureAwait(false);

    public static async Task<int> ConfiguredTaskWithOption(
        Task<int> a, bool continueOnCapturedContext)
        => await a.ConfigureAwait(continueOnCapturedContext);

    public static async Task<string> ReferenceCast(Task<object> a)
        => (string)await a;

    public static async Task<int> ValueCast(Task<object> a)
        => (int)await a;

    public static async Task<string> AsReference(Task<object> a)
        => await a as string;

    public static async Task<bool> TypeTest(Task<object> a)
        => await a is string;

    public static async Task<bool> NegatedTypeTest(Task<object> a)
        => await a is not string;

    public static async Task<int[]> NewIntArray(Task<int> a)
        => new int[await a];

    public static async Task<string[]> NewReferenceArray(Task<int> a)
        => new string[await a + 1];

    public static async Task<int> ArrayElementAfterAwait(Task<int[]> a, int index)
        => (await a)[index];

    public static async Task<bool> Not(Task<bool> a)
        => !await a;

    public static async Task<bool> EqualFalse(Task<bool> a)
        => await a == false;

    public static async Task<bool> EqualTrue(Task<bool> a)
        => await a == true;

    public static async Task<bool> NotEqualFalse(Task<bool> a)
        => await a != false;

    public static async Task<bool> NotEqualTrue(Task<bool> a)
        => await a != true;

    public static async Task<bool> CompareBooleans(Task<bool> a, bool b)
        => await a == b;

    public static async Task<bool> NotComparison(Task<int> a, int b)
        => !(await a > b);

    public static async Task<bool> NotFloatComparison(Task<double> a, double b)
        => !(await a > b);

    public static async Task<bool> NotUnsignedComparison(Task<uint> a, uint b)
        => !(await a > b);

    public static async Task<bool> DoubleNot(Task<bool> a)
        => !!await a;

    public static async Task<string> Coalesce(Task<string> a)
        => await a ?? "fallback";

    public static async Task<string> CoalesceCall(Task<string> a)
        => await a ?? Fallback();

    public static async Task<string> CoalesceParameter(Task<string> a, string fallback)
        => await a ?? fallback;

    public static async Task<string> CoalesceThenCall(Task<string> a)
        => (await a ?? Fallback()).Trim();

    public static async Task<string> CoalesceOperandCall(Task<string> a)
        => (await a).Trim() ?? Fallback();

    public static async Task<string> CoalesceBooleanArgument(Task<string> a, bool useFirst)
        => await a ?? ChooseFallback(!useFirst);

    static string Fallback() => Environment.TickCount.ToString();

    static string ChooseFallback(bool useFirst) => useFirst ? "first" : "second";

    public static async Task<bool> TypeEquality(Task<Type> value)
        => await value == typeof(string);

    public static async Task<Type> CoalesceTypeOf(Task<Type> value)
        => await value ?? typeof(string);

    public static async Task<bool> TypeArrayEquality(Task<Type> value)
        => await value == typeof(string[]);

    public static async Task<bool> TypeGenericEquality(Task<Type> value)
        => await value == typeof(Dictionary<string, int>);

    public static async Task<Type> TypeArguments(Task<int> value)
        => TypeChoice(await value, typeof(string), typeof(int));

    public static async Task<int> BooleanChoice(Task<bool> value, int yes, int no)
        => await value ? yes : no;

    public static async Task<int> BooleanChoiceCalls(Task<bool> value)
        => await value ? PositiveChoice() : NegativeChoice();

    public static async Task<int> NegatedBooleanChoice(Task<bool> value, int yes, int no)
        => !await value ? yes : no;

    public static async Task<string> BooleanChoiceObjects(Task<bool> value, string yes, string no)
        => await value ? yes : no;

    public static async Task<Type> BooleanChoiceTypeOf(Task<bool> value)
        => await value ? typeof(string) : typeof(int);

    public static async Task<int> ComparisonChoice(Task<int> value, int yes, int no)
        => await value > 0 ? yes : no;

    public static async Task<int> BooleanChoiceThenCall(Task<bool> value)
        => Math.Abs(await value ? -7 : -9);

    public static async Task<InitializerHolder> NestedInitializer(Task<int> value)
        => CombineInitialized(await value, new InitializerHolder { Child = { Value = 7 } });

    public static async Task<InitializerHolder> NestedInitializerEntries(Task<int> value)
        => CombineInitialized(await value,
            new InitializerHolder { Child = { Value = 7, Other = PositiveChoice() } });

    public static async Task<InitializerHolder> NestedCollectionInitializer(Task<int> value)
        => CombineInitialized(await value, new InitializerHolder { Values = { 7, 8 } });

    public static async Task<InitializerHolder> NestedInitializerTypeOf(Task<int> value)
        => CombineInitialized(await value, new InitializerHolder { Child = { Kind = typeof(string) } });

    static Type TypeChoice(int which, Type left, Type right) => which == 0 ? left : right;
    static int PositiveChoice() => Environment.TickCount;
    static int NegativeChoice() => Environment.CurrentManagedThreadId;
    static InitializerHolder CombineInitialized(int value, InitializerHolder holder)
    {
        holder.Child.Value += value;
        return holder;
    }

    public static async Task<int> ByteArgument(Task<int> value)
        => WithByte(await value, 42);

    public static async Task<int> ByteMinimumArgument(Task<int> value)
        => WithByte(await value, 0);

    public static async Task<int> ByteMaximumArgument(Task<int> value)
        => WithByte(await value, 255);

    public static async Task<decimal> DecimalConstant(Task<int> value)
        => await value + 0.5m;

    public static async Task<int> CharacterArgument(Task<int> value)
        => WithCharacter(await value, 'x');

    public static async Task<int> CharacterMaximumArgument(Task<int> value)
        => WithCharacter(await value, '\uffff');

    public static async Task<int> EnumArgument(Task<int> value)
        => WithEnum(await value, DayOfWeek.Friday);

    public static async Task<int> LongEnumArgument(Task<int> value)
        => WithLongEnum(await value, LongChoice.Negative);

    public static async Task<bool> ShortCircuitCall(Task<bool> value)
        => await value && Predicate();

    public static async Task<bool> ShortCircuitOrCall(Task<bool> value)
        => await value || Predicate();

    public static async Task<int> NestedBooleanChoice(Task<bool> value, bool other)
        => (await value ? other : !other) ? 7 : 9;

    public static async Task<int> NestedChoiceCalls(Task<bool> value, bool other)
        => (await value ? other : !other) ? PositiveChoice() : NegativeChoice();

    public static async Task<string> ComposedSelectionTypes(Task<bool> value, bool other)
        => ((await value ? other : !other) ? 13 : 17) > 15 ? "yes" : "no";

    public static async Task<string> Concatenation(Task<int> value)
        => "value=" + await value;

    public static async Task<string> IntReceiver(Task<int> value)
        => (await value).ToString();

    public static async Task<string> LongReceiver(Task<long> value)
        => (await value).ToString();

    public static async Task<string> BoolReceiver(Task<bool> value)
        => (await value).ToString();

    public static async Task<bool> VariableAnd(Task<bool> value, bool other)
        => await value && other;

    public static async Task<string> DateTimeReceiver(Task<DateTime> value)
        => (await value).ToString();

    public static async Task<string> DecimalReceiver(Task<decimal> value)
        => (await value).ToString();

    public static async Task<string> NamedIntReceiver(Task<int> value)
    {
        int result = await value;
        return result.ToString();
    }

    public static async Task<SinkShape> CharPropertyInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { Character = 'x' });

    public static async Task<SinkShape> CharFieldInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { CharacterField = 'x' });

    public static async Task<SinkShape> BoolPropertyInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { Enabled = true });

    public static async Task<SinkShape> EnumPropertyInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { Day = DayOfWeek.Friday });

    public static async Task<SinkShape> NestedCharInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { Child = { Character = 'x' } });

    public static async Task<List<char>> CharCollectionInitializer(Task<int> a)
        => KeepChars(await a, new List<char> { 'x', 'y' });

    public static async Task<SinkShape> DefaultGuidInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { Id = default(Guid) });

    public static async Task<SinkShape> DefaultDateTimeInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { Date = default(DateTime) });

    public static async Task<T> GenericAwait<T>(Task<T> a) => await a;

    public static async Task<T[]> GenericArrayAwait<T>(Task<T[]> a) => await a;

    public static async Task<GenericBox<T>> GenericConstruct<T>(Task<T> a)
        => new GenericBox<T>(await a);

    public static async Task<int> NullableDefaultReceiver(Task<int?> a)
        => (await a).GetValueOrDefault();

    public static async Task<string> NullableIntReceiver(Task<int?> a)
        => (await a).ToString();

    public static async Task<string> NullableGuidReceiver(Task<Guid?> a)
        => (await a).ToString();

    public static async Task<string> NullableBoolReceiver(Task<bool?> a)
        => (await a).ToString();

    public static async Task<string> NamedNullableReceiver(Task<int?> a)
    {
        int? result = await a;
        return result.ToString();
    }

    public static async Task<(int, int)> AwaitTuple(Task<int> a) => (await a, 2);

    public static async Task<(int, (int, int))> AwaitNestedTuple(Task<int> a) => (await a, (2, 3));

    public static async Task<(int, int)> AwaitTupleEffect(Task<int> a) => (await a, PositiveChoice());

    public static async Task<int> MixedAndOr(Task<bool> v, bool a)
        => await v && a || !a ? 1 : 2;

    public static async Task<bool> ShortCircuitBoth(Task<bool> v)
        => await v && Predicate() || OtherPredicate();

    public static async Task<GenericBox<T>> GenericCall<T>(Task<T> a)
        => BoxValue(await a);

    public static async Task<SinkShape> DefaultGuidFieldInitializer(Task<int> a)
        => KeepShape(await a, new SinkShape { IdField = default(Guid) });

    public static async Task<List<Guid>> DefaultGuidCollectionInitializer(Task<int> a)
        => KeepGuids(await a, new List<Guid> { default(Guid) });

    public static async Task<Dictionary<int, char>> MultiArgumentCollectionInitializer(Task<int> a)
        => KeepCharacters(await a, new Dictionary<int, char> { { 2, 'A' } });

    public static async Task<(int, int, int, int, int, int, int, int)> AwaitLongTuple(Task<int> a)
        => (await a, 2, 3, 4, 5, 6, 7, 8);

    public static async Task<bool> AndThenOr(Task<bool> v, bool a, bool b)
        => await v && a || b;

    public static async Task<(int, char, bool, DayOfWeek)> AwaitTypedTuple(Task<int> a)
        => (await a, 'A', true, DayOfWeek.Monday);

    public static async Task<int> AwaitInitializedStackalloc(int length)
        => await TaskForSpan(stackalloc int[length]);

    public static async Task<int> AwaitInitializedByteStackalloc(int length)
        => await TaskForByteSpan(stackalloc byte[length]);

    [System.Runtime.CompilerServices.SkipLocalsInit]
    public static async Task<int> AwaitUninitializedStackalloc(int length)
        => await TaskForSpan(unsafe(stackalloc int[length]));

    public static async Task<int> InterpolatedOperand(int value)
        => await TextAsync($"value={value}");

    public static async Task<int> InterpolatedFormats(double value)
        => await TextAsync($"[{value,8:N2}]");

    public static async Task<int> InterpolatedZeroAlignment(int value)
        => await TextAsync($"{value,0}");

    public static async Task<int> InterpolatedNegativeAlignment(string value)
        => await TextAsync($"[{value,-4}]");

    public static async Task<int> InterpolatedEffects(int value)
        => await TextAsync($"[{PositiveChoice()},{WithCharacter(value, 'x'):D4}]");

    public static async Task<int> InterpolatedGeneric<T>(T value)
        => await TextAsync($"value={value}");

    public static async Task<int> InterpolatedEscapedFormat(TimeSpan value)
        => await TextAsync($"{value:hh\\:mm}");

    public static async Task YieldOnce() => await Task.Yield();

    public static async Task<int> StructOperand(StructAwaitable value) => await value;
    public static async Task<int> MutableStructOperand(MutableStructAwaitable value) => await value;
    public static async Task<int> StructFactoryOperand(Task<int> value) => await MakeAwaitable(value);
    public static async Task<int> ClassAwaitableOperand(ClassAwaitable value) => await value;

    static Task<int> TextAsync(string value) => Task.FromResult(value.Length);
    static StructAwaitable MakeAwaitable(Task<int> value) => new(value);

    static Task<int> TaskForSpan(Span<int> value) => Task.FromResult(value.Length);
    static Task<int> TaskForByteSpan(Span<byte> value) => Task.FromResult(value.Length);

    static SinkShape KeepShape(int value, SinkShape shape) => shape;
    static List<char> KeepChars(int value, List<char> values) => values;
    static List<Guid> KeepGuids(int value, List<Guid> values) => values;
    static Dictionary<int, char> KeepCharacters(int value, Dictionary<int, char> values) => values;
    static GenericBox<T> BoxValue<T>(T value) => new(value);
    static bool OtherPredicate() => Environment.TickCount < 0;

    public enum LongChoice : long { Negative = -1 }

    static int WithByte(int value, byte other) => value + other;
    static int WithCharacter(int value, char other) => value + other;
    static int WithEnum(int value, DayOfWeek other) => value + (int)other;
    static int WithLongEnum(int value, LongChoice other) => value + (int)other;
    static bool Predicate() => Environment.TickCount > 0;

    public sealed class InitializerHolder
    {
        public InitializerChild Child { get; } = new();
        public List<int> Values { get; } = new();
    }

    public sealed class InitializerChild
    {
        public int Value { get; set; }
        public int Other;
        public Type Kind { get; set; }
    }

    public class Reading
    {
        public virtual int Value => 42;
        public int FieldValue;
        public static int StaticFieldValue;
        public volatile int VolatileValue;
    }

    public sealed class SinkShape
    {
        public char Character { get; set; }
        public char CharacterField;
        public bool Enabled { get; set; }
        public DayOfWeek Day { get; set; }
        public Guid Id { get; set; }
        public Guid IdField;
        public DateTime Date { get; set; }
        public SinkChild Child { get; } = new();
    }

    public sealed class SinkChild
    {
        public char Character { get; set; }
    }

    public sealed class GenericBox<T>
    {
        public T Value { get; }
        public GenericBox(T value) => Value = value;
    }

    public readonly struct StructAwaitable
    {
        readonly Task<int> _task;
        public StructAwaitable(Task<int> task) => _task = task;
        public System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter() => _task.GetAwaiter();
    }

    public struct MutableStructAwaitable
    {
        int _uses;
        public System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter()
            => Task.FromResult(++_uses).GetAwaiter();
    }

    public sealed class ClassAwaitable
    {
        readonly Task<int> _task;
        public ClassAwaitable(Task<int> task) => _task = task;
        public System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter() => _task.GetAwaiter();
    }
}

public static class GenericExpressionFixtures<TOuter>
{
    public static async Task<T> GenericContextAwait<T>(Task<T> a) => await a;

    public static async Task<(T, TOuter)> GenericContextTuple<T>(Task<T> a, TOuter b)
        => (await a, b);
}
