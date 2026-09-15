namespace ILInspector.Decompiler.Tests;

public sealed class ExactArrayReader<T>
{
    public T[] Value { get; set; } = new T[1];
    public T[] Read() => Value;
    public void Observe() => Value = new T[1];

    public void MutateAndReplace(T replacement)
    {
        Value[0] = replacement;
        Observe();
    }
}

public static class ExactSzArraySlotMaterializationSamples
{
    public static object[] ReadObjects(ExactArrayReader<object> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static int[] ReadIntegers(ExactArrayReader<int> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static Uri[] ReadClasses(ExactArrayReader<Uri> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static IDisposable[] ReadInterfaces(ExactArrayReader<IDisposable> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static DayOfWeek[] ReadEnums(ExactArrayReader<DayOfWeek> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static KeyValuePair<int, string>[] ReadGenericStructs(ExactArrayReader<KeyValuePair<int, string>> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static T[] ReadGeneric<T>(ExactArrayReader<T> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static int[][] ReadJagged(ExactArrayReader<int[]> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static int[][,] ReadRectangularElements(ExactArrayReader<int[,]> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static int[] AllocateAndObserve(int length, ExactArrayReader<int> reader)
    {
        var value = new int[length];
        reader.Observe();
        return value;
    }

    public static int[] InitializeAndObserve(int first, int second, ExactArrayReader<int> reader)
    {
        var value = new int[] { first, second };
        reader.Observe();
        return value;
    }

    public static object[] MutateAndObserve(ExactArrayReader<object> reader, object replacement)
    {
        var value = reader.Read();
        reader.MutateAndReplace(replacement);
        return value;
    }

    public static object[] CovariantReturn(ExactArrayReader<Uri> reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static bool SwapArrays(object[] first, object[] second)
    {
        (first, second) = (second, first);
        return ReferenceEquals(first, second);
    }

    public static unsafe int*[] AllocatePointers(int length, ExactArrayReader<int> reader)
    {
        var value = new int*[length];
        reader.Observe();
        return value;
    }

    public static unsafe delegate*<int>[] AllocateFunctionPointers(int length, ExactArrayReader<int> reader)
    {
        var value = new delegate*<int>[length];
        reader.Observe();
        return value;
    }
}
