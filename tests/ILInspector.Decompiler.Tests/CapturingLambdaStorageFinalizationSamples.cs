using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Decompiler.Tests;

public static class CapturingLambdaStorageFinalizationSamples
{
    public static Action<object, int> Callback(MethodInfo method)
        => (target, value) => method.Invoke(target, new object[] { value });

    public static Func<GenericParameterHandle, string> CapturedReader(MetadataReader reader)
        => handle => reader.GetString(reader.GetGenericParameter(handle).Name);

    public static Action<object, int> EffectfulEarlierArgument(MethodInfo method, Func<object> target)
        => (_, value) =>
        {
            var arguments = new object[] { value };
            method.Invoke(target(), arguments);
        };

    public static Action<object, int> MultipleUses(MethodInfo method)
        => (target, value) =>
        {
            var arguments = new object[] { value };
            method.Invoke(target, arguments);
            method.Invoke(target, arguments);
        };

    public static Action<object, int> CapturedLocal(MethodInfo method)
    {
        var local = Select(method);
        return (target, value) => local.Invoke(target, new object[] { value });
    }

    public static Action<object, int> ExistingUserLocal(MethodInfo method)
        => (target, value) =>
        {
            int count = value;
            method.Invoke(target, new object[] { count++ });
            Observe(count);
        };

    public static Func<Action<object, int>> FurtherNestedBody(MethodInfo method)
        => () => (target, value) => method.Invoke(target, new object[] { value });

    public static Action<object, int> NestedNonCapturingBody(MethodInfo method)
        => (target, value) => method.Invoke(target, new object[] { new Func<int>(() => 42), value });

    public static Action<object, int> CovariantArrayStorage(MethodInfo method)
        => (target, _) =>
        {
            object[] arguments = new string[] { "value" };
            method.Invoke(target, arguments);
        };

    public static object?[] NonCapturing(object? value) => new[] { value };

    public static MethodInfo Select(MethodInfo method) => method;

    public static void Observe(int value) { }
}
