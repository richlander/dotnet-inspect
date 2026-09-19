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
        bool allowSpecialName = false,
        bool allowRuntimeSpecialName = false)
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
        MethodAttributes permittedOrdinaryFlags =
            MethodAttributes.Static
                | MethodAttributes.Final
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Abstract;
        if (allowSpecialName)
            permittedOrdinaryFlags |= MethodAttributes.SpecialName;
        if (allowRuntimeSpecialName)
            permittedOrdinaryFlags |= MethodAttributes.RTSpecialName;
        bool ordinaryFlagsAreRepresentable =
            (nonAccess & ~permittedOrdinaryFlags) == 0;
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
                : ordinaryFlagsAreRepresentable
                    && (!isFinal || isOverride)
                    && (!isNewSlot || isVirtual));
    }
}
