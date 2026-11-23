namespace DotnetInspect.Web.Interop.Metadata.Wire;

/// <summary>The Metadata facade's detached Type Signature presentation.</summary>
public sealed record TypeSignaturePresentation(
    string Type,
    string Kind,
    string[] Modifiers,
    string Accessibility,
    string Namespace,
    string? BaseType,
    string? TypeParameters,
    int InterfaceCount,
    int NestedTypeCount);
