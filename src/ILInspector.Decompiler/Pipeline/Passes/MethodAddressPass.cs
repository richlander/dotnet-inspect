namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a surviving static <see cref="LoadFunctionPointer"/> (a
/// non-virtual <c>ldftn</c>) into <see cref="AddressOfMethod"/> — C#'s
/// <c>&amp;Method</c>. These are the function-pointer loads the
/// <see cref="DelegateConstructionPass"/> did not consume: they feed a
/// <c>calli</c>, a native-callback argument, or a <c>delegate*</c>-typed field,
/// all of which take a function pointer directly. Runs just before
/// <see cref="FunctionPointerDiagnosticsPass"/> so only the unspellable residue
/// (a virtual <c>ldvirtftn</c>, which needs an instance) reaches the diagnostic.
/// </summary>
public sealed class MethodAddressPass : IIrPass
{
    public string Name => "method-address";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var pointer in function.Descendants.OfType<LoadFunctionPointer>().ToList())
        {
            if (pointer.Parent is null || pointer.IsVirtual)
                continue;
            pointer.ReplaceWith(new AddressOfMethod(pointer.Method));
        }
    }
}
