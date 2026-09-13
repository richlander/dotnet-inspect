using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static ILInspector.Decompiler.Fixtures.ClassicAsync.InventorySupport;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public static class AsyncInventoryFixtures
{
    public static async Task<int> Plain(Task<int> task) => await task;
    public static async Task<int> ArrayNeighbor(int value) => await Count(new int[value]);
    public static async Task<int> InterpolationNeighbor(int value) => await Text($"value={value}");
    public static async Task<int> StructNeighbor(InventoryAwaitable value) => await value;
    public static async Task<int> InterfaceNeighbor(IInventoryAwaitable value) => await value;
    public static async Task<int> ConstantArrayOperand() => await Count(new[] { 1, 2, 3 });
    public static async Task<int> ConstantByteArrayOperand() => await Bytes(new byte[] { 1, 2, 3, 4, 5 });
    public static async Task<Range> RangeResult(Task<int> task) => (await task)..^1;
    public static async Task<int[]> SliceResult(Task<int[]> task) => (await task)[1..^1];
    public static async Task<int> SliceOperand(int[] values) => await Count(values[1..^1]);
    public static async Task<int> FromEndResult(Task<int[]> task) => (await task)[^1];
    public static async Task<int> FromEndOperand(int[] values) => await Task.FromResult(values[^1]);
    public static async Task<object> AnonymousResult(Task<int> task) => new { Value = await task };
    public static async Task<int> AnonymousOperand(int value) => await ObjectCount(new { Value = value });
    public static async Task<int> DelegateOperand(InventoryShape shape) => await Invoke(shape.Read);
    public static async Task<int> InterpolationAnonymous(int value) => await Text($"[{new { Value = value }}]");
    public static async Task<int> InterpolationNullable(int? value) => await Text($"[{value.GetValueOrDefault()}]");
    public static async Task<int> NestedInterpolation(int value) => await Text($"[{Format($"{value}")}]");
    public static async Task<int> PriorInterpolationArgument(int value) => await NumberAndText(Tick(), $"value={value}");
    public static async Task<int> FollowingInterpolationArgument(int value) => await TextAndNumber($"value={value}", Tick());
    public static async Task<int> ReadOnlySpanLiteral(int value) => await ReadOnlySpanCount([value, 2]);
    public static async Task<int> ByRefArgument(Task<int> task, InventoryShape shape) => Add(await task, ref shape.Value);
    public static async Task<int> OutArgument(Task<int> task, InventoryShape shape) => Write(await task, out shape.Value);
    public static async Task<int> RefReceiverResult(Task<InventoryShape> task) => ReadRef(ref (await task).Value);
    public static async Task<int> ConditionalAwaitable(bool value, Task<int> yes, Task<int> no) => await (value ? yes : no);
    public static async Task<int> CoalesceAwaitable(Task<int> task, Task<int> fallback) => await (task ?? fallback);
    public static async Task<int> SwitchResult(Task<int> task) => await task switch { 0 => 7, 1 => 9, _ => 11 };
    public static async Task<int> GenericAny<T>(T value) where T : IInventoryAwaitable => await value;
    public static async Task<int> GenericStruct<T>(T value) where T : struct, IInventoryAwaitable => await value;
    public static async Task<int> GenericClass<T>(T value) where T : class, IInventoryAwaitable => await value;
    public static async Task<int[]> SliceStart(Task<int[]> task) => (await task)[1..];
    public static async Task<int[]> SliceEnd(Task<int[]> task) => (await task)[..^1];
    public static async Task<int[]> SliceAll(Task<int[]> task) => (await task)[..];
    public static async Task<int> VirtualDelegateOperand(InventoryShape shape) => await Invoke(shape.VirtualRead);
    public static async Task<int> SwitchEffects(Task<int> task) => await task switch { 0 => Tick(), 3 => Tick() + 1, _ => 11 };
    public static async Task<int> SwitchLabels(Task<int> task) => await task switch { -3 => 7, 9 => 9, _ => 11 };
    public static async Task<int> SwitchTable(Task<int> task) => await task switch { 0 => 7, 1 => 9, 2 => 13, 3 => 15, 4 => 17, _ => 11 };
    public static async Task<int> ConditionalOperandEffects(bool flag) => await (flag ? NumberAndText(Tick(), "yes") : NumberAndText(Tick(), "no"));
    public static async Task<int> CoalesceOperandEffects(Task<int> task) => await (task ?? NumberAndText(Tick(), "fallback"));
    public static async Task<int> CollectionEffects() => await ReadOnlySpanCount([Tick(), Tick()]);
}

public static class InventorySupport
{
    public static Task<int> Count(int[] values) => Task.FromResult(values.Length);
    public static Task<int> Bytes(byte[] values) => Task.FromResult(values.Length);
    public static Task<int> ObjectCount(object value) => Task.FromResult(value.GetHashCode());
    public static Task<int> Invoke(Func<int> action) => Task.FromResult(action());
    public static Task<int> Text(string value) => Task.FromResult(value.Length);
    public static string Format(string value) => value;
    public static int Tick() => Environment.TickCount;
    public static Task<int> NumberAndText(int value, string text) => Task.FromResult(value + text.Length);
    public static Task<int> TextAndNumber(string text, int value) => Task.FromResult(value + text.Length);
    public static Task<int> ReadOnlySpanCount(ReadOnlySpan<int> values) => Task.FromResult(values.Length);
    public static int Add(int value, ref int other) => value + other;
    public static int Write(int value, out int other) => other = value;
    public static int ReadRef(ref int value) => value;
}

public interface IInventoryAwaitable
{
    TaskAwaiter<int> GetAwaiter();
}

public readonly struct InventoryAwaitable : IInventoryAwaitable
{
    readonly Task<int> _task;
    public InventoryAwaitable(Task<int> task) => _task = task;
    public TaskAwaiter<int> GetAwaiter() => _task.GetAwaiter();
}

public class InventoryShape
{
    public int Value;
    public int Read() => Value;
    public virtual int VirtualRead() => Value;
}
