namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises an anonymous-type construction back into a C# anonymous-object literal:
/// the compiler lowers <c>new { a = x, b = y }</c> to a constructor call on a
/// generated <c>&lt;&gt;f__AnonymousType0&lt;...&gt;(x, y)</c>, whose type name is
/// unspeakable in C#, so left flat the printer emits invalid source. This folds it
/// back to <c>new { a = x, b = y }</c>.
///
/// The match keys off the metadata the importer captured on the
/// <see cref="NewObject"/>: <see cref="NewObject.AnonymousPropertyNames"/> is
/// non-empty only for anonymous-type constructors, and aligns 1:1 with the
/// arguments. No metadata is read here — the pass operates purely on the captured
/// names, like every other raising pass.
/// </summary>
public sealed class AnonymousObjectPass : IIrPass
{
    public string Name => "anonymous-object";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            var names = newObject.AnonymousPropertyNames;
            if (names.IsDefaultOrEmpty || names.Length != newObject.Arguments.Count)
                continue;
            if (HasDuplicateNames(names))
                continue;

            var values = newObject.DetachChildren().Cast<IrExpression>().ToList();
            var anonymous = new AnonymousObject(newObject.Constructor.DeclaringType, names, values);
            context.Stepper.StepOver("raise anonymous-type constructor to anonymous object", newObject);
            newObject.ReplaceWith(anonymous);
        }
    }

    static bool HasDuplicateNames(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
            if (!seen.Add(name))
                return true;
        return false;
    }
}
