using System;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ILInspector.Analysis.RenderFixtures;

public static class RealRenderFixtures
{
    // Takes the REAL framework RenderTreeBuilder and allocates a capturing delegate inside a
    // loop. Because RenderTreeBuilder carries a trusted framework public-key-token, this is
    // recognized as intrinsic Blazor render plumbing and its allocations are suppressed.
    public static int RenderWithDelegateLoop(RenderTreeBuilder builder, int[] values, int seed)
    {
        var total = 0;
        foreach (var v in values)
        {
            Func<int> handler = () => v + seed;
            total += handler();
        }

        return total;
    }

    public static void RenderWithDeferredFragmentLoop(RenderTreeBuilder builder, int[] values)
    {
        foreach (int value in values)
        {
            builder.AddAttribute(
                0,
                "ChildContent",
                (RenderFragment)(nested => nested.AddContent(1, BoxDeferredValue(value))));
        }
    }

    public static void RenderWithUnknownConsumerLoop(int[] values)
    {
        foreach (int value in values)
            RegisterUnknown(nested => nested.AddContent(1, BoxDeferredValue(value)));
    }

    public static RenderFragment BuildLatestFragment(int[] values)
    {
        RenderFragment callback = null;
        foreach (int value in values)
            callback = nested => nested.AddContent(1, BoxDeferredValue(value));
        return callback;
    }

    public static void InvokeLatestFragmentOnce(RenderTreeBuilder builder, int[] values)
    {
        RenderFragment callback = null;
        foreach (int value in values)
            callback = nested => nested.AddContent(1, BoxDeferredValue(value));
        callback?.Invoke(builder);
    }

    public static void RenderWithOutsideCallback(RenderTreeBuilder builder, int[] values)
    {
        RenderFragment callback = static nested => nested.AddContent(1, BoxDeferredValue(0));
        foreach (int value in values)
            builder.AddAttribute(0, "ChildContent", callback);
    }

    public static void RenderWithCachedCallbackLoop(RenderTreeBuilder builder, int[] values)
    {
        foreach (int value in values)
        {
            builder.AddAttribute(
                0,
                "ChildContent",
                (RenderFragment)(static nested => nested.AddContent(1, "cached")));
        }
    }

    public static void InvokeConstructedCallbackInLoop(int[] values)
    {
        foreach (int value in values)
            ((Action)(() => GC.KeepAlive(BoxImmediateValue(value))))();
    }

    public static void InvokeDirectlyInLoop(int[] values)
    {
        foreach (int value in values)
            GC.KeepAlive(BoxDeferredValue(value));
    }

    public static unsafe nint LoadFunctionPointerInLoop(int count)
    {
        delegate*<int, object> callback = null;
        for (int i = 0; i < count; i++)
            callback = &BoxDeferredValue;
        return (nint)callback;
    }

    public static object BoxDeferredValue(int value) => value;

    static object BoxImmediateValue(int value) => value;

    static void RegisterUnknown(RenderFragment callback) => GC.KeepAlive(callback);
}
