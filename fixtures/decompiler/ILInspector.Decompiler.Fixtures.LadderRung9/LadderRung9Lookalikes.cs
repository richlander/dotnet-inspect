namespace LadderRung9;

using System;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;

[CompilerGenerated]
public class DynamicLookalikes
{
    // A manual shell that mimics the dynamic getter scaffolding exactly,
    // but the cache field is not compiler-generated.
    static CallSite<Func<CallSite, object, object>> s_cache;

    public object ManualCache(object value)
    {
        if (s_cache == null)
        {
            s_cache = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(CSharpBinderFlags.None, "Length", typeof(DynamicLookalikes), new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }));
        }
        return s_cache.Target.Invoke(s_cache, value);
    }

    // A compiler-generated cache field, but an extra side-effect.
    [CompilerGenerated]
    static CallSite<Func<CallSite, object, object>> s_cache_extra_side_effect;
    public static int s_extra;

    public object ExtraSideEffect(object value)
    {
        if (s_cache_extra_side_effect == null)
        {
            s_extra = 1;
            s_cache_extra_side_effect = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(CSharpBinderFlags.None, "Length", typeof(DynamicLookalikes), new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }));
        }
        return s_cache_extra_side_effect.Target.Invoke(s_cache_extra_side_effect, value);
    }

    // A compiler-generated cache field, but wrong member name.
    [CompilerGenerated]
    static CallSite<Func<CallSite, object, object>> s_cache_wrong_name;

    public object WrongName(object value)
    {
        if (s_cache_wrong_name == null)
        {
            s_cache_wrong_name = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(CSharpBinderFlags.None, "123Unspellable", typeof(DynamicLookalikes), new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }));
        }
        return s_cache_wrong_name.Target.Invoke(s_cache_wrong_name, value);
    }

    // A compiler-generated cache field, but wrong context type.
    [CompilerGenerated]
    static CallSite<Func<CallSite, object, object>> s_cache_wrong_context;

    public object WrongContext(object value)
    {
        if (s_cache_wrong_context == null)
        {
            s_cache_wrong_context = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(CSharpBinderFlags.None, "Length", typeof(string), new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }));
        }
        return s_cache_wrong_context.Target.Invoke(s_cache_wrong_context, value);
    }

    // A compiler-generated cache field, but wrong flags.
    [CompilerGenerated]
    static CallSite<Func<CallSite, object, object>> s_cache_wrong_flags;

    public object WrongFlags(object value)
    {
        if (s_cache_wrong_flags == null)
        {
            s_cache_wrong_flags = CallSite<Func<CallSite, object, object>>.Create(
                Binder.GetMember(CSharpBinderFlags.InvokeSimpleName, "Length", typeof(DynamicLookalikes), new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }));
        }
        return s_cache_wrong_flags.Target.Invoke(s_cache_wrong_flags, value);
    }
}
