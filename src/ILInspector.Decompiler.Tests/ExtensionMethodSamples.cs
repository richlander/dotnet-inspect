namespace ILInspector.Decompiler.Tests;

// Same-assembly samples for extension-method rendering: one genuine [Extension]
// method and one plain static of the same call shape, to exercise the IsExtension
// gate that decides instance-vs-static spelling.
public static class ExtensionMethodSamples
{
    public static int Doubled(this int value) => value * 2;

    public static int Combine(int left, int right) => left + right;
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
