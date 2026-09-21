using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler;

internal sealed class SelectedGetterStorage(
    int methodToken, string fieldName, TypeRef fieldType)
{
    public static SelectedGetterStorage? TryCreate(
        MetadataSource source, PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle methodHandle, ApiMember property)
    {
        var reader = source.Reader;
        var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
        var method = reader.GetMethodDefinition(methodHandle);
        if (accessors.Getter != methodHandle || !accessors.Setter.IsNil
            || property.IsUnsafe || method.RelativeVirtualAddress == 0
            || method.GetGenericParameters().Count != 0)
            return null;
        var typeHandle = method.GetDeclaringType();
        if (!MemberBodyProducer.TryGetCompilerGeneratedBackingField(
                reader, typeHandle, property, methodHandle, null, out var fieldHandle))
            return null;

        var type = reader.GetTypeDefinition(typeHandle);
        var genericNames = type.GetGenericParameters()
            .Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))
            .ToImmutableArray();
        var scope = new GenericScope(genericNames, []);
        var expectedFlags = FieldAttributes.Private
            | (property.IsStatic ? FieldAttributes.Static : 0);
        // A field-bodied getter does not imply readonly storage, unlike get;.
        if (!property.IsStatic && !type.BaseType.IsNil
            && IrImporter.ResolveTypeToken(reader, type.BaseType, scope)
                .Equals(TypeRef.CoreLib("System", "ValueType"))
            && AttributeReader.HasAttribute(
                reader, type.GetCustomAttributes(), KnownAttributeNames.IsReadOnlyAttribute))
            expectedFlags |= FieldAttributes.InitOnly;
        if (!SelectedPropertyAccessorSource.HasSupportedBackingField(
                reader, fieldHandle, scope, expectedFlags))
            return null;

        var body = source.Pe.GetMethodBody(method.RelativeVirtualAddress);
        if (!body.ExceptionRegions.IsEmpty)
            return null;
        var decoded = MethodInstructions.Decode(body);
        if (!decoded.IsComplete)
            return null;
        if (decoded.Instructions.Count(instruction => instruction.OpCode != ILOpCode.Nop)
            <= (property.IsStatic ? 2 : 3))
            return null;
        bool hasRead = false;
        foreach (var instruction in decoded.Instructions)
        {
            if (instruction.OpCode is ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Calli
                || instruction.Operand == OperandKind.InlineTok)
                return null;
            if (instruction.Operand != OperandKind.InlineField)
                continue;
            if (instruction.OpCode != (property.IsStatic ? ILOpCode.Ldsfld : ILOpCode.Ldfld)
                || !MemberBodyProducer.FieldOperandMatchesBackingField(
                    reader, typeHandle,
                    MetadataTokens.EntityHandle((int)instruction.OperandValue), fieldHandle))
                return null;
            var field = IrImporter.ResolveField(
                reader, MetadataTokens.EntityHandle((int)instruction.OperandValue), scope);
            if (!SelectedPropertyAccessorSource.HasOwnTypeArguments(field.DeclaringType, genericNames.Length))
                return null;
            hasRead = true;
        }
        if (!hasRead)
            return null;

        var definition = reader.GetFieldDefinition(fieldHandle);
        var binding = new SelectedGetterStorage(
            MetadataTokens.GetToken(methodHandle), reader.GetString(definition.Name),
            GuardedDecode.FieldType(reader, definition, scope));
        var imported = IrImporter.Import(source, methodHandle);
        return imported is not null && binding.CanBindBody(imported) ? binding : null;
    }

    bool CanBindBody(IrFunction function)
    {
        if (function.MetadataToken != methodToken
            || function.LocalNames.Contains("field"))
            return false;
        bool hasRead = false;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadField load:
                    if (load.IsVolatile || load.Field.Name != fieldName
                        || !load.Field.Type.Equals(fieldType)
                        || (function.Signature.HasThis
                            ? load.Instance is not LoadArgument { Index: 0 }
                            : load.Instance is not null))
                        return false;
                    hasRead = true;
                    break;
                case LoadArgument argument:
                    if (argument.Index != 0 || argument.Parent is not LoadField)
                        return false;
                    break;
                case Call call when call.Callee.Name == "field"
                    || GeneratedCodeIdentity.IsLocalFunctionMethod(call.Callee):
                case LoadFieldAddress or StoreField or LoadFunctionPointer:
                    return false;
            }
        }
        return hasRead;
    }

    public void BindBody(IrFunction function)
    {
        if (!CanBindBody(function))
            throw new InvalidOperationException("The selected getter body no longer matches its proven storage binding.");
        function.HasAccessorStorageBinding = true;
        foreach (var load in function.Descendants.OfType<LoadField>())
            load.UsesAccessorStorage = true;
    }
}
