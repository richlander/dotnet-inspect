namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Identity facts the classic general inverse and its request authentication
/// read from state-machine storage.
/// </summary>
internal static class ClassicInverseNodeFacts
{
    internal static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element
            ? element
            : type;

    internal static bool IsMachineField(FieldRef field, TypeRef machine)
    {
        TypeRef declaring = Definition(field.DeclaringType);
        machine = Definition(machine);
        return !declaring.DefinitionHandle.IsNil
            && declaring.DefinitionHandle == machine.DefinitionHandle
            && declaring.DefinitionModuleVersionId is { } declaringMvid
            && machine.DefinitionModuleVersionId is { } machineMvid
            && declaringMvid == machineMvid;
    }
}
