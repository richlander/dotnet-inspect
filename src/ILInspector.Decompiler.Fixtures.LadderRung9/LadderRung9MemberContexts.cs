using System;

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

    static object Identity(object value) => value;

    public object Last => _last;
}
