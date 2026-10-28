using System.Reflection;
using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal enum ExtensionDeclarationShape
{
    None,
    Skeleton,
    Unconfirmed,
}

internal enum MethodBodyAvailability
{
    Present,
    NoApplicableInput,
}

/// <summary>
/// Declaration-only classifications of a method definition that every
/// method-body consumer shares: whether it has a body to inspect, and whether
/// it is an extension-block declaration copy.
/// </summary>
internal static class MethodDefinitionShapes
{
    // Matches Member Body comparison: a declaration that is abstract, a
    // P/Invoke, runtime-provided or internal-call, or an IL declaration without
    // a body (such as an extern UnsafeAccessor) has no body to inspect.
    internal static MethodBodyAvailability ClassifyBodyAvailability(
        MethodDefinition method)
        => (method.Attributes
                & (MethodAttributes.Abstract
                    | MethodAttributes.PinvokeImpl))
                != 0
            || !HasManagedIlBody(method.ImplAttributes)
            || (method.ImplAttributes
                & MethodImplAttributes.InternalCall)
                != 0
            || method.RelativeVirtualAddress == 0
                ? MethodBodyAvailability.NoApplicableInput
                : MethodBodyAvailability.Present;

    // Roslyn emits each extension-block member twice: an implementation on the
    // [Extension] static class, and a declaration copy with a throwing body in
    // a nested SpecialName [Extension] grouping type. Every method of a
    // confirmed grouping type is such a copy. An [ExtensionMarker] method
    // outside that shape is unconfirmed and must not stand as its own member.
    internal static ExtensionDeclarationShape ClassifyExtensionDeclaration(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method)
    {
        try
        {
            TypeDefinitionHandle enclosing = type.GetDeclaringType();
            bool groupingType =
                !enclosing.IsNil
                && (type.Attributes & TypeAttributes.SpecialName) != 0
                && AttributeReader.HasAttribute(
                    reader,
                    type.GetCustomAttributes(),
                    ExtensionAttributeName)
                && AttributeReader.HasAttribute(
                    reader,
                    reader.GetTypeDefinition(enclosing).GetCustomAttributes(),
                    ExtensionAttributeName);
            if (groupingType)
                return ExtensionDeclarationShape.Skeleton;
            return AttributeReader.TryGetExtensionMarkerName(
                    reader,
                    method.GetCustomAttributes(),
                    out _)
                ? ExtensionDeclarationShape.Unconfirmed
                : ExtensionDeclarationShape.None;
        }
        catch (BadImageFormatException)
        {
            return ExtensionDeclarationShape.Unconfirmed;
        }
    }

    const string ExtensionAttributeName =
        "System.Runtime.CompilerServices.ExtensionAttribute";

    internal static bool HasManagedIlBody(
        MethodImplAttributes attributes)
        => (attributes
                & MethodImplAttributes.CodeTypeMask)
            == MethodImplAttributes.IL
            && (attributes
                & MethodImplAttributes.ManagedMask)
            == MethodImplAttributes.Managed;

    // A compiler-generated type nested inside another type (closure, state
    // machine, or lifted helper container) holds bodies that belong to a
    // source owner; a top-level generated type has none.
    internal static bool IsDeclaredInNestedCompilerGeneratedType(
        MetadataReader reader,
        TypeDefinition type)
    {
        TypeDefinition current = type;
        for (int depth = 0;
            depth < MetadataSafetyPolicy.MaxRelationshipNodes;
            depth++)
        {
            TypeDefinitionHandle enclosing = current.GetDeclaringType();
            if (enclosing.IsNil)
                return false;
            if (LibraryBodyPrimaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
                        reader,
                        current.GetCustomAttributes()))
            {
                return true;
            }
            current = reader.GetTypeDefinition(enclosing);
        }
        return true;
    }
}
