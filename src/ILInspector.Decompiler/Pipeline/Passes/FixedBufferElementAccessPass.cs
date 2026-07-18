namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's fixed-buffer element lowering from the generated
/// <c>FixedElementField</c> address plus byte offset back to the source
/// <c>buffer[index]</c> place.
/// </summary>
public sealed class FixedBufferElementAccessPass : IIrPass
{
    public string Name => "fixed-buffer-element-access";

    enum AccessKind
    {
        Address,
        Read,
        Write,
        PinnedSource,
    }

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var node in function.Descendants.ToList())
        {
            switch (node)
            {
                case LoadIndirect load
                    when load.Type is { } loadType
                        && TryCreate(load.Address, loadType, AccessKind.Read, out var loadAddress):
                    context.Stepper.StepOver("raise fixed-buffer read address", load.Address);
                    load.Address.ReplaceWith(loadAddress);
                    break;

                case StoreIndirect store
                    when store.Type is { } storeType
                        && TryCreate(store.Address, storeType, AccessKind.Write, out var storeAddress):
                    context.Stepper.StepOver("raise fixed-buffer write address", store.Address);
                    store.Address.ReplaceWith(storeAddress);
                    break;

                case StoreLocal store
                    when store.Type is { Kind: TypeRefKind.Pinned, ElementType: { Kind: TypeRefKind.ByRef, ElementType: { } element } }
                        && TryCreate(store.Value, element, AccessKind.PinnedSource, out var pinSource):
                    context.Stepper.StepOver("raise fixed-buffer pinned source", store.Value);
                    store.Value.ReplaceWith(pinSource);
                    break;

                case StoreLocal store
                    when store.Type is { Kind: TypeRefKind.ByRef, ElementType: { } element }
                        && TryCreate(store.Value, element, AccessKind.Address, out var localAddress):
                    context.Stepper.StepOver("raise fixed-buffer ref local source", store.Value);
                    store.Value.ReplaceWith(localAddress);
                    break;

                case StoreLocal store
                    when store.Type is { Kind: TypeRefKind.Pointer }
                        && TryCreatePointerAddress(store.Value, store.Type, out var pointerLocalAddress):
                    context.Stepper.StepOver("raise fixed-buffer pointer local source", store.Value);
                    ((Convert)store.Value).Operand.ReplaceWith(pointerLocalAddress);
                    break;

                case Return ret
                    when function.Signature.ReturnType is { Kind: TypeRefKind.ByRef, ElementType: { } element }
                        && ret.Value is { } value
                        && TryCreate(value, element, AccessKind.Address, out var returnAddress):
                    context.Stepper.StepOver("raise fixed-buffer ref return source", value);
                    value.ReplaceWith(returnAddress);
                    break;

                case Return ret
                    when function.Signature.ReturnType is { Kind: TypeRefKind.Pointer } pointerType
                        && ret.Value is { } value
                        && TryCreatePointerAddress(value, pointerType, out var pointerReturnAddress):
                    context.Stepper.StepOver("raise fixed-buffer pointer return source", value);
                    ((Convert)value).Operand.ReplaceWith(pointerReturnAddress);
                    break;

                case Call call:
                    RaiseCallArguments(call, context);
                    break;
            }
        }
    }

    static void RaiseCallArguments(Call call, PassContext context)
    {
        var arguments = call.Arguments;
        if (call.Callee.HasThis)
        {
            if (arguments.Count == 0)
                return;
            if (TryCreate(arguments[0], call.Callee.DeclaringType, AccessKind.Address, out var receiverAddress))
            {
                context.Stepper.StepOver("raise fixed-buffer instance receiver source", arguments[0]);
                arguments[0].ReplaceWith(receiverAddress);
            }
        }

        int firstParameterArgument = call.Callee.HasThis ? 1 : 0;
        for (int argumentIndex = firstParameterArgument; argumentIndex < arguments.Count; argumentIndex++)
        {
            int parameterIndex = argumentIndex - firstParameterArgument;
            if (parameterIndex >= call.Callee.ParameterTypes.Length
                || call.Callee.ParameterTypes[parameterIndex] is not { Kind: TypeRefKind.Pointer } pointerType
                || !TryCreatePointerAddress(arguments[argumentIndex], pointerType, out var argumentAddress))
            {
                continue;
            }

            context.Stepper.StepOver("raise fixed-buffer pointer argument source", arguments[argumentIndex]);
            ((Convert)arguments[argumentIndex]).Operand.ReplaceWith(argumentAddress);
        }

        if (call.Callee.ParameterRefKindsFacts != ParameterRefKindFacts.Known
            || call.Callee.ParameterRefKinds.Length < call.Callee.ParameterTypes.Length)
        {
            return;
        }

        for (int argumentIndex = firstParameterArgument; argumentIndex < arguments.Count; argumentIndex++)
        {
            int parameterIndex = argumentIndex - firstParameterArgument;
            if (parameterIndex >= call.Callee.ParameterTypes.Length
                || call.Callee.ParameterTypes[parameterIndex] is not { Kind: TypeRefKind.ByRef, ElementType: { } element }
                || call.Callee.ParameterRefKinds[parameterIndex] == ArgumentRefKind.Value
                || !TryCreate(arguments[argumentIndex], element, AccessKind.Address, out var argumentAddress))
            {
                continue;
            }

            context.Stepper.StepOver("raise fixed-buffer ref argument source", arguments[argumentIndex]);
            arguments[argumentIndex].ReplaceWith(argumentAddress);
        }
    }

    static bool TryCreatePointerAddress(IrExpression value, TypeRef expectedPointer, out FixedBufferElementAddress access)
    {
        access = null!;
        if (expectedPointer is not { Kind: TypeRefKind.Pointer, ElementType: { } expectedElement }
            || value is not Convert
            {
                Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" },
                Operand: { } address,
            })
        {
            return false;
        }

        return TryCreate(address, expectedElement, AccessKind.Address, out access);
    }

    static bool TryCreate(IrExpression address, TypeRef expectedElement, AccessKind accessKind, out FixedBufferElementAddress access)
    {
        access = null!;
        if (address is Binary { Kind: BinaryKind.Add } add
            && TrySplitFixedElementAddress(add, out var elementFieldAddress, out var offset)
            && TryCreateFromElementAddress(
                elementFieldAddress,
                expectedElement,
                accessKind,
                out var fixedBuffer,
                out var bufferFieldAddress)
            && TryScaledPointerIndex(offset, fixedBuffer.ElementType, out var index))
        {
            access = CreateAccess(bufferFieldAddress, fixedBuffer, index, add);
            return true;
        }

        if (address is LoadFieldAddress bareElementFieldAddress
            && IsFixedElementField(bareElementFieldAddress)
            && TryCreateFromElementAddress(
                bareElementFieldAddress,
                expectedElement,
                accessKind,
                out var bareFixedBuffer,
                out var bareBufferFieldAddress))
        {
            access = CreateAccess(
                bareBufferFieldAddress,
                bareFixedBuffer,
                new Constant(0, TypeRef.CoreLib("System", "Int32")),
                bareElementFieldAddress);
            return true;
        }

        return false;
    }

    static bool TryCreateFromElementAddress(
        LoadFieldAddress elementFieldAddress,
        TypeRef expectedElement,
        AccessKind accessKind,
        out FixedBufferFieldInfo fixedBuffer,
        out LoadFieldAddress bufferFieldAddress)
    {
        fixedBuffer = null!;
        bufferFieldAddress = null!;
        if (elementFieldAddress.Instance is not LoadFieldAddress candidateBufferFieldAddress
            || candidateBufferFieldAddress.Instance is null
            || candidateBufferFieldAddress.Field.FixedBuffer is not { } candidateFixedBuffer
            || !ElementAccessTypeMatches(expectedElement, candidateFixedBuffer.ElementType, accessKind)
            || !elementFieldAddress.Field.Type.Equals(candidateFixedBuffer.ElementType)
            || !elementFieldAddress.Field.DeclaringType.Equals(candidateBufferFieldAddress.Field.Type))
        {
            return false;
        }

        fixedBuffer = candidateFixedBuffer;
        bufferFieldAddress = candidateBufferFieldAddress;
        return true;
    }

    static FixedBufferElementAddress CreateAccess(
        LoadFieldAddress bufferFieldAddress,
        FixedBufferFieldInfo fixedBuffer,
        IrExpression index,
        IrNode source)
    {
        var access = new FixedBufferElementAddress(
            bufferFieldAddress.Field,
            fixedBuffer.ElementType,
            (IrExpression)bufferFieldAddress.Instance!.Clone(),
            (IrExpression)index.Clone());
        access.InheritSourceOffset(source);
        return access;
    }

    static bool TrySplitFixedElementAddress(Binary add, out LoadFieldAddress fieldAddress, out IrExpression offset)
    {
        if (add.Left is LoadFieldAddress left && IsFixedElementField(left))
        {
            fieldAddress = left;
            offset = add.Right;
            return true;
        }

        if (add.Right is LoadFieldAddress right && IsFixedElementField(right))
        {
            fieldAddress = right;
            offset = add.Left;
            return true;
        }

        fieldAddress = null!;
        offset = add.Right;
        return false;
    }

    static bool IsFixedElementField(LoadFieldAddress address)
        => address.Field.Name == "FixedElementField";

    static bool ElementAccessTypeMatches(TypeRef observed, TypeRef element, AccessKind accessKind)
    {
        if (observed.Equals(element))
            return true;

        if (accessKind is AccessKind.Address or AccessKind.PinnedSource)
            return false;

        return accessKind == AccessKind.Read
            ? ReadStorageTypeMatches(observed, element)
            : WriteStorageTypeMatches(observed, element);
    }

    static bool ReadStorageTypeMatches(TypeRef observed, TypeRef element)
        => element is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && observed is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && (element.Name, observed.Name) is
                ("Boolean", "Byte") or
                ("Char", "UInt16") or
                ("UInt64", "Int64");

    static bool WriteStorageTypeMatches(TypeRef observed, TypeRef element)
        => element is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && observed is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && (element.Name, observed.Name) is
                ("Boolean", "SByte") or
                ("Byte", "SByte") or
                ("Char", "Int16") or
                ("UInt16", "Int16") or
                ("UInt32", "Int32") or
                ("UInt64", "Int64");

    static bool TryScaledPointerIndex(IrExpression offset, TypeRef elementType, out IrExpression index)
    {
        if (ByteSize(elementType) is not { } elementSize)
        {
            index = offset;
            return false;
        }

        offset = NativeIntegerOperand(offset);

        if (TryConstantMultiple(offset, elementSize, out var multiple))
        {
            index = multiple >= int.MinValue && multiple <= int.MaxValue
                ? new Constant((int)multiple, TypeRef.CoreLib("System", "Int32"))
                : new Constant(multiple, TypeRef.CoreLib("System", "Int64"));
            return true;
        }

        if (offset is Binary { Kind: BinaryKind.Multiply } multiply)
        {
            if (IsConstant(multiply.Left, elementSize))
            {
                index = NativeIntegerOperand(multiply.Right);
                return true;
            }

            if (IsConstant(multiply.Right, elementSize))
            {
                index = NativeIntegerOperand(multiply.Left);
                return true;
            }
        }

        if (elementSize == 1)
        {
            index = NativeIntegerOperand(offset);
            return true;
        }

        index = offset;
        return false;
    }

    static IrExpression NativeIntegerOperand(IrExpression expression)
        => expression is Convert { Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" }, Operand: { } operand }
                && !IsIntegerConstantExpression(operand)
            ? operand
            : expression;

    static bool IsIntegerConstantExpression(IrExpression expression)
        => expression is Constant
            || expression is Convert { Operand: { } operand } && IsIntegerConstantExpression(operand);

    static bool IsConstant(IrExpression expression, int value)
        => TryConstantInteger(expression, out var actual) && actual == value;

    static bool TryConstantMultiple(IrExpression expression, int divisor, out long multiple)
    {
        if (!TryConstantInteger(expression, out var value) || divisor == 0 || value % divisor != 0)
        {
            multiple = 0;
            return false;
        }

        multiple = value / divisor;
        return true;
    }

    static bool TryConstantInteger(IrExpression expression, out long value)
    {
        switch (expression)
        {
            case Constant { Value: int i }:
                value = i;
                return true;
            case Constant { Value: long l }:
                value = l;
                return true;
            case Convert { Operand: { } operand } conversion
                when TryConstantInteger(operand, out var operandValue)
                    && TryValuePreservingIntegerConversion(conversion, operandValue):
                value = operandValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    static bool TryValuePreservingIntegerConversion(Convert conversion, long value)
        => conversion.Target is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && conversion.Target.Name switch
            {
                "SByte" => value is >= sbyte.MinValue and <= sbyte.MaxValue,
                "Byte" => value is >= byte.MinValue and <= byte.MaxValue,
                "Int16" => value is >= short.MinValue and <= short.MaxValue,
                "UInt16" => value is >= ushort.MinValue and <= ushort.MaxValue,
                "Int32" => value is >= int.MinValue and <= int.MaxValue,
                "UInt32" => value is >= uint.MinValue and <= uint.MaxValue,
                "Int64" => true,
                "IntPtr" => value is >= int.MinValue and <= int.MaxValue,
                "UInt64" => value >= 0,
                "UIntPtr" => value is >= uint.MinValue and <= uint.MaxValue,
                _ => false,
            };

    static int? ByteSize(TypeRef type)
        => type is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "Boolean" or "Byte" or "SByte" => 1,
                "Char" or "Int16" or "UInt16" => 2,
                "Int32" or "UInt32" or "Single" => 4,
                "Int64" or "UInt64" or "Double" => 8,
                _ => null,
            }
            : null;
}
