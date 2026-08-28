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
