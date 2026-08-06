using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal static class TypeResolutionRequestFactory
{
    internal static TypeResolutionRequest Create(
        ResolvedAssemblyReference source,
        ResolvableTypeReference reference)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(reference);

        return reference.Origin switch
        {
            TypeReferenceOrigin.AssemblyReference assembly =>
                TypeResolutionRequest.FromReference(
                    assembly.Assembly,
                    AssemblyBindingOrigin.FromAssembly(source),
                    Scope(assembly.Assembly),
                    reference.Type),
            TypeReferenceOrigin.CurrentAssembly =>
                TypeResolutionRequest.FromAssembly(
                    source,
                    AssemblyResolutionScope.Any,
                    reference.Type),
            TypeReferenceOrigin.IntrinsicCoreLibrary =>
                TypeResolutionRequest.FromCoreLibrary(
                    source,
                    AssemblyResolutionScope.Platform,
                    reference.Type),
            TypeReferenceOrigin.ModuleReference module =>
                TypeResolutionRequest.FromModule(
                    source,
                    module.ModuleName,
                    reference.Type),
            _ => throw new InvalidOperationException(
                "Unknown type-reference origin."),
        };
    }

    internal static AssemblyResolutionScope Scope(
        AssemblyReferenceIdentity reference) =>
        PlatformKeys.IsPlatform(reference.PublicKeyToken)
            ? AssemblyResolutionScope.Platform
            : AssemblyResolutionScope.Any;
}
