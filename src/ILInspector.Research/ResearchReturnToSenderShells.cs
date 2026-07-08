using System.Reflection.Metadata;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research;

/// <summary>
/// Research-owned request boundary for ReturnToSender compile-back shell composition.
/// </summary>
public static class ResearchReturnToSenderShells
{
    public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodRef method)
        => CompileBackSourceComposer.TryCreateClosureMemberRequirement(reader, typeHandle, method);

    public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        FieldRef field)
        => CompileBackSourceComposer.TryCreateClosureMemberRequirement(reader, typeHandle, field);

    public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string memberName)
        => CompileBackSourceComposer.TryCreateClosureMemberRequirement(reader, typeHandle, memberName);

    public static CompileBackSourceResult ComposePropertyGetter(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        PropertyDefinitionHandle targetProperty,
        MethodDefinitionHandle targetGetter,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
        => CompileBackSourceComposer.ComposePropertyGetter(
            assemblyPath,
            reader,
            function,
            targetType,
            targetProperty,
            targetGetter,
            targetBody,
            fullType,
            methodName,
            overload,
            signatureText,
            closureRoots,
            closureFacts,
            closureMemberRequirements);

    public static CompileBackSourceResult ComposePropertySetter(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        PropertyDefinitionHandle targetProperty,
        MethodDefinitionHandle targetSetter,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
        => CompileBackSourceComposer.ComposePropertySetter(
            assemblyPath,
            reader,
            function,
            targetType,
            targetProperty,
            targetSetter,
            targetBody,
            fullType,
            methodName,
            overload,
            signatureText,
            closureRoots,
            closureFacts,
            closureMemberRequirements);

    public static CompileBackSourceResult ComposeMethod(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        MethodDefinitionHandle targetMethod,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
        => CompileBackSourceComposer.ComposeMethod(
            assemblyPath,
            reader,
            function,
            targetType,
            targetMethod,
            targetBody,
            fullType,
            methodName,
            overload,
            signatureText,
            closureRoots,
            closureFacts,
            closureMemberRequirements);
}
