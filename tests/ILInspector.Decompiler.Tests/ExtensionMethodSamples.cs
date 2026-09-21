namespace ILInspector.Decompiler.Tests;

// Same-assembly samples for extension-method rendering: one genuine [Extension]
// method and one plain static of the same call shape, to exercise the IsExtension
// gate that decides instance-vs-static spelling.
public static class ExtensionMethodSamples
{
    public static int Doubled(this int value) => value * 2;

    public static int Combine(int left, int right) => left + right;
}

public static class OutputInferenceMethodGroupSamples
{
    public static IEnumerable<int> Call(IEnumerable<string> values)
        => values.Select<string, int>(int.Parse);
}

public interface IGenericReceiver<T>;

public sealed class AmbiguousGenericReceiver :
    IGenericReceiver<int>,
    IGenericReceiver<string>;

public sealed class SingleGenericReceiver : IGenericReceiver<int>;

public sealed class GenericConversionBox<T>
{
    public static implicit operator GenericConversionBox<T>(string value)
        => new();
}

public static class GenericReceiverExtensions
{
    public static T Value<T>(this IGenericReceiver<T> receiver) => default!;

    public static T CovariantValue<T>(this IEnumerable<T> receiver) => default!;

    public static T Overloaded<T>(this IGenericReceiver<T> receiver, string value)
        => default!;

    public static T Overloaded<T, TValue>(this IGenericReceiver<T> receiver, TValue value)
        => default!;

    public static int SiblingInference<T>(this IEnumerable<T> receiver, object value)
        => 1;

    public static int SiblingInference<T>(this IEnumerable<T> receiver, Func<T> value)
        => 2;

    public static Type BareReceiverType<T>(this T value) => typeof(T);

    public static Type TakeFactory<T>(
        this IEnumerable<T> receiver,
        Func<T> factory) => typeof(T);

    public static int ParamsCollection<T>(
        this IEnumerable<T> receiver,
        string first,
        object second) => 1;

    public static int ParamsCollection<T, TValue>(
        this IEnumerable<T> receiver,
        params ReadOnlySpan<TValue> values) => 2;

    public static T[] CopyArray<T>(this IEnumerable<T> receiver)
        => [.. receiver];

    public static unsafe Type FunctionPointerArgument<T>(
        this IEnumerable<T> receiver,
        delegate*<T, void> callback) => typeof(T);

    public static int NullSiblingInference<T>(
        this IEnumerable<T> receiver,
        object first,
        object second) => 1;

    public static int NullSiblingInference<T>(
        this IEnumerable<T> receiver,
        List<T> first,
        Func<T> second) => 2;

    public static int ConversionSiblingInference<T>(
        this IEnumerable<T> receiver,
        object first,
        object second) => 1;

    public static int ConversionSiblingInference<T>(
        this IEnumerable<T> receiver,
        GenericConversionBox<T> first,
        Func<T> second) => 2;

    public static int SpanSiblingInference<T>(
        this IEnumerable<T> receiver,
        Span<object> first,
        object second) => 1;

    public static int SpanSiblingInference<T>(
        this IEnumerable<T> receiver,
        ReadOnlySpan<T> first,
        string second) => 2;

    public static int CollectionSiblingInference<T>(
        this IEnumerable<T> receiver,
        IEnumerable<object> values) => 1;

    public static int CollectionSiblingInference<T>(
        this IEnumerable<T> receiver,
        List<T> values) => 2;

    public static Type CollectionArgument<T>(
        this IEnumerable<T> receiver,
        IEnumerable<T> values) => typeof(T);

    public static Type TupleArgument<T>(
        this IEnumerable<T> receiver,
        (T, T) values) => typeof(T);

    public static TResult OutputCandidate<TSource, TResult>(
        this IEnumerable<TSource> receiver,
        Func<TSource, TResult> selector) => selector(receiver.First());

    public static TResult OutputCandidate<TSource, TResult>(
        this IEnumerable<TSource> receiver,
        Func<object, TResult> selector) => selector(receiver.First()!);
}

public class ExtensionMethodReceiverBase
{
    public object[] Values(Type type, bool inherit) => [];
}

public class ExtensionMethodReceiver : ExtensionMethodReceiverBase;

public static class ExtensionMethodCollisionSamples
{
    public static IEnumerable<Attribute> Values(
        this ExtensionMethodReceiver receiver,
        Type type,
        bool inherit) => [];

    public static Attribute? CallsShadowedExtension(
        ExtensionMethodReceiver receiver)
        => Values(receiver, typeof(Attribute), true)
            .FirstOrDefault<Attribute>();

    public static Attribute? CallsPlatformShadowedExtension(
        System.Reflection.TypeInfo typeInfo)
        => System.Reflection.CustomAttributeExtensions.GetCustomAttributes(
                typeInfo,
                typeof(Attribute),
                true)
            .FirstOrDefault<Attribute>();

    public static bool CallsShadowedGenericExtension(
        List<int> values,
        int value)
        => Enumerable.Contains(values, value);
}

public class ExtensionPropertyCollisionReceiver
{
    public object[] Values => [];
}

public static class ExtensionPropertyCollisionSamples
{
    public static IEnumerable<Attribute> Values(
        this ExtensionPropertyCollisionReceiver receiver,
        Type type,
        bool inherit) => [];

    public static Attribute? CallsPropertyShadowedExtension(
        ExtensionPropertyCollisionReceiver receiver)
        => Values(receiver, typeof(Attribute), true)
            .FirstOrDefault<Attribute>();
}

public struct RefExtensionCollisionReceiver
{
    public int Value() => 0;
}

public static class RefExtensionCollisionSamples
{
    public static string Value(
        this ref RefExtensionCollisionReceiver receiver) => "";

    public static string CallsShadowedRefExtension(
        ref RefExtensionCollisionReceiver receiver)
        => Value(ref receiver);

    public static unsafe string CallsPointerShadowedRefExtension(
        RefExtensionCollisionReceiver* receiver)
        => Value(ref *receiver);
}

public static class ArrayExtensionCollisionSamples
{
    public static string Clone(this int[] values) => "";

    public static string CallsShadowedArrayExtension(int[] values)
        => Clone(values);
}

public interface IInterfaceExtensionCollisionReceiver;

public static class InterfaceExtensionCollisionSamples
{
    public static bool Equals(
        this IInterfaceExtensionCollisionReceiver receiver,
        object other) => false;

    public static bool CallsObjectShadowedExtension(
        IInterfaceExtensionCollisionReceiver receiver,
        object other) => Equals(receiver, other);
}

public static class GenericParameterExtensionCollisionSamples
{
    public static bool Equals<T>(this T value, object other)
        => false;

    public static bool CallsConstraintUnknownExtension<T>(
        T value,
        object other) => Equals(value, other);
}
