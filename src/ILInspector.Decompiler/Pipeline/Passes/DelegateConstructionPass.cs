namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a method group to delegate creation — the inverse of the compiler's
/// <c>ldftn/ldvirtftn; newobj DelegateType::.ctor(object, native int)</c>
/// lowering. The <c>(object, native int)</c> constructor shape fed a
/// <see cref="LoadFunctionPointer"/> is the same evidence ILSpy and the runtime
/// use; the result is a typed <see cref="DelegateCreation"/> node, not name
/// surgery at print time.
/// </summary>
public sealed class DelegateConstructionPass : IIrPass
{
    public string Name => "delegate-construction";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            if (newObject.Parent is null)
                continue;  // detached by an earlier rewrite in this walk
            if (!IsDelegateConstructor(newObject.Constructor))
                continue;
            if (newObject.Children.Count != 2 || newObject.Children[1] is not LoadFunctionPointer)
                continue;

            var children = newObject.DetachChildren().Cast<IrExpression>().ToList();
            var target = children[0];
            var pointer = (LoadFunctionPointer)children[1];
            context.Stepper.StepOver($"raise method group to {newObject.Constructor.DeclaringType.Name} delegate creation", newObject);
            newObject.ReplaceWith(new DelegateCreation(
                newObject.Constructor.DeclaringType, pointer.Method, pointer.IsVirtual, target));
        }
    }

    // The delegate constructor's universal shape: an instance .ctor taking
    // (object target, native int method). Only delegate types carry it, and the
    // C# compiler only ever feeds an ldftn result as its second argument.
    static bool IsDelegateConstructor(MethodRef constructor)
        => constructor.Name == ".ctor"
            && constructor.HasThis
            && constructor.ParameterTypes.Length == 2
            && constructor.ParameterTypes[0] is { Namespace: "System", Name: "Object" }
            && constructor.ParameterTypes[1] is { Namespace: "System", Name: "IntPtr" };
}
