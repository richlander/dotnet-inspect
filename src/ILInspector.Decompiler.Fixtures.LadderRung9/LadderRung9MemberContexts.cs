using System;
using System.Collections.Generic;

namespace LadderRung9;

// Compiler-backed dynamic GetMember contexts beyond the direct-return form.
// These exercise the immediate-use shapes csc emits around the same
// `Binder.GetMember` CallSite scaffolding (assignment, argument, nested bodies),
// so the dynamic-callsite raise can be proven against real metadata rather than
// synthetic IR. This type is intentionally separate from
// DynamicAndExpressionTrees so the rung 9 exact-member-set guard is unaffected.
public class DynamicMemberContexts
{
    object _last;

    // Immediate use as a field-store value (non-return).
    public void AssignToField(dynamic value)
    {
        _last = value.Length;
    }

    // Immediate use as a call argument (non-return).
    public object UseAsArgument(dynamic value)
    {
        return Identity(value.Length);
    }

    // Immediate use as a local initializer observed twice (defeats inlining to
    // a bare return-of-invoke).
    public object AssignToLocal(dynamic value)
    {
        object length = value.Length;
        return Identity(length) is null ? length : length;
    }

    // Nested lambda body: csc lowers this to a display-class method whose
    // declaring type is the generated environment, while the GetMember context
    // remains the authored enclosing type.
    public Func<object> InLambda(dynamic value)
    {
        return () => value.Length;
    }

    // Nested local-function body.
    public object InLocalFunction(dynamic value)
    {
        object Get() => value.Length;
        return Get();
    }

    // Nested local-function body whose local function has its OWN top-level
    // `dynamic` parameter (distinct from InLocalFunction, which captures the
    // enclosing dynamic param). The raised body drops the redundant cast to
    // `v.Length`, so the printer-owned local-function declaration must spell the
    // parameter `dynamic v`, not its `object` TypeRef — otherwise
    // `object Get(object v) => v.Length;` is CS1061 (#2984, PR #3032 review).
    public object InLocalFunctionOwnParam(string input)
    {
        static object Get(dynamic v) => v.Length;
        return Get(input);
    }

    // Iterator state-machine body: csc lowers this to a MoveNext method whose
    // declaring type is the generated iterator state machine, while the
    // GetMember context remains the authored enclosing type (same bridge as
    // InLambda, but through the iterator predicate).
    public IEnumerable<object> InIterator(dynamic value)
    {
        yield return value.Length;
    }

    static object Identity(object value) => value;

    public object Last => _last;
}

// Same nested-body dynamic GetMember shapes as DynamicMemberContexts, but
// authored inside a GENERIC enclosing type. csc emits the GetMember binder
// context as typeof(GenericDynamicMemberContexts<T>) — a generic instantiation
// of the enclosing definition with its own type parameter in scope — while the
// lowered display-class / state-machine declaring type's metadata-decoded
// EnclosingType is the bare generic definition. The dynamic-callsite raise must
// recognize that self-instantiation and still raise rather than declining on the
// GenericInstance-vs-Definition kind mismatch (#2968).
public class GenericDynamicMemberContexts<T>
{
    // Nested lambda body inside a generic enclosing type.
    public Func<object> InGenericLambda(dynamic value)
    {
        return () => value.Length;
    }

    // Iterator state-machine body inside a generic enclosing type.
    public IEnumerable<object> InGenericIterator(dynamic value)
    {
        yield return value.Length;
    }
}

