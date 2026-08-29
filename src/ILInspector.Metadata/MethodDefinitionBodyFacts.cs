using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Metadata-only facts about a MethodDef's executable body.</summary>
public static class MethodDefinitionBodyFacts
{
    /// <summary>
    /// Returns whether the MethodDef carries analyzable managed IL.
    /// <c>MetadataDeclarationQueryTests.TypeSurfaces_ClassifyOnlyAnalyzableManagedIlAsMethodBodies</c>
    /// gates the distinction between managed IL and a nonzero native/unmanaged RVA.
    /// </summary>
    public static bool HasAnalyzableIlBody(MethodDefinition method)
        => method.RelativeVirtualAddress != 0
           && (method.Attributes
               & (MethodAttributes.Abstract | MethodAttributes.PinvokeImpl)) == 0
           && (method.ImplAttributes
               & (MethodImplAttributes.CodeTypeMask
                  | MethodImplAttributes.ManagedMask
                  | MethodImplAttributes.InternalCall
                  | MethodImplAttributes.ForwardRef))
           == MethodImplAttributes.IL;
}
