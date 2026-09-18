using System.Reflection;

namespace ILInspector.Metadata;

internal readonly record struct ApiMethodModifiers(
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsSealed,
    bool FinalFlagIsRepresentable)
{
    internal static ApiMethodModifiers FromAttributes(
        MethodAttributes attributes,
        bool isExplicitInterfaceImplementation)
    {
        bool isVirtual = (attributes & MethodAttributes.Virtual) != 0;
        bool isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
        bool isFinal = (attributes & MethodAttributes.Final) != 0;
        bool isOverride = isVirtual
            && !isNewSlot
            && !isExplicitInterfaceImplementation;
        return new(
            (attributes & MethodAttributes.Static) != 0,
            isVirtual,
            (attributes & MethodAttributes.Abstract) != 0,
            isOverride,
            isOverride && isFinal,
            !isFinal
                || isOverride
                || isExplicitInterfaceImplementation
                    && isVirtual
                    && isNewSlot);
    }
}
