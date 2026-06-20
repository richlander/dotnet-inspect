using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's anonymous-type lowering back into a C# anonymous object
/// creation: a <c>newobj</c> on a generated <c>&lt;&gt;f__AnonymousType*</c> type
/// becomes <c>new { Name = value, ... }</c>. The member names (in argument order)
/// are recovered at import from the anonymous type's property metadata and
/// carried on <see cref="MethodRef.AnonymousMemberNames"/>.
///
/// <para>The anonymous type name is compiler-generated and unspeakable, so the
/// match is unambiguous (no hand-written false positives), and the round-trip is
/// opcode-exact: csc re-lowers <c>new { a = a, b = b }</c> to the same anonymous
/// type construction. Without this raise the printer emits the raw
/// <c>&lt;&gt;f__AnonymousType*</c> name, which does not compile — so this fixes
/// a validity defect, not merely syntax altitude.</para>
/// </summary>
public sealed class AnonymousObjectPass : IIrPass
{
    public string Name => "anonymous-object";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            var memberNames = newObject.Constructor.AnonymousMemberNames;
            if (memberNames.IsEmpty)
                continue;
            // The ctor arity and the property count always agree for an anonymous
            // type; guard anyway so a malformed shape is left untouched.
            if (memberNames.Length != newObject.Arguments.Count)
                continue;

            var values = newObject.Arguments.ToList();
            foreach (var value in values)
                value.Detach();
            var anonymous = new AnonymousObjectExpression(memberNames, values, newObject.ResultType);
            context.Stepper.StepOver("raise anonymous-type newobj to new { ... }", newObject);
            newObject.ReplaceWith(anonymous);
        }
    }
}
