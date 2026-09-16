namespace ILInspector.Decompiler.Tests;

public static class GenericSlotMaterializationSamples
{
    public static T ReadReference<T>(ExactReferenceReader<T> reader, T replacement) where T : class
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static T ReadValue<T>(ExactReferenceReader<T> reader, T replacement) where T : struct
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static object? BoxMethod<T>(ExactReferenceReader<T> reader, T replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static T CopyThenReplace<T>(ref T source, T replacement)
    {
        var copy = source;
        source = replacement;
        return copy;
    }

    public static T CopyAllowsRefStruct<T>(ref T source, T replacement) where T : allows ref struct
    {
        var copy = source;
        source = replacement;
        return copy;
    }
}

public static class GenericTypeStorageSamples<T>
{
    public static T ReadType(ExactReferenceReader<T> reader, T replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }
}

public static class RefLikeGenericTypeStorageSamples<T> where T : allows ref struct
{
    public static T CopyThenReplace(ref T source, T replacement)
    {
        var copy = source;
        source = replacement;
        return copy;
    }
}
