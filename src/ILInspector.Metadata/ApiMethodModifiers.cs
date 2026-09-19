using System.Reflection;

namespace ILInspector.Metadata;

internal readonly record struct ApiMethodModifiers(
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsSealed,
    bool AreRepresentable)
{
    internal static ApiMethodModifiers FromAttributes(
        MethodAttributes attributes,
        bool isExplicitInterfaceImplementation,
        bool allowSpecialName = false)
    {
        bool isVirtual = (attributes & MethodAttributes.Virtual) != 0;
        bool isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
        bool isFinal = (attributes & MethodAttributes.Final) != 0;
        bool isStatic = (attributes & MethodAttributes.Static) != 0;
        bool isOverride = isVirtual
            && !isNewSlot
            && !isExplicitInterfaceImplementation;
        MethodAttributes nonAccess =
            attributes & ~MethodAttributes.MemberAccessMask;
        MethodAttributes explicitShape = isStatic
            ? MethodAttributes.Static | MethodAttributes.HideBySig
            : MethodAttributes.Final
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.HideBySig;
        bool explicitShapeIsRepresentable =
            nonAccess == explicitShape
            || allowSpecialName
                && nonAccess
                    == (explicitShape | MethodAttributes.SpecialName);
        return new(
            isStatic,
            isVirtual,
            (attributes & MethodAttributes.Abstract) != 0,
            isOverride,
            isOverride && isFinal,
            isExplicitInterfaceImplementation
                ? explicitShapeIsRepresentable
                : !isFinal || isOverride);
    }
}
