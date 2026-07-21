using DotnetInspector.Models;

namespace DotnetInspector.Output;

internal sealed record ExtensionMethodJsonResult(
    string Method,
    string Class,
    string ExtendedType,
    string Library,
    string? Signature,
    List<string>? Signatures,
    int? Overloads,
    string Kind,
    string? Source,
    string? SourceVersion,
    string? ReachablePath,
    string? ReachableFromType)
{
    internal static ExtensionMethodJsonResult From(ExtensionMethodResult result) => new(
        result.MethodName,
        result.ExtensionClass,
        result.ExtendedType,
        result.Assembly,
        result.Signature,
        result.Signatures,
        result.Overloads,
        result.Kind,
        result.Source,
        result.SourceVersion,
        result.ReachablePath,
        result.ReachableFromType);
}

internal sealed record ImplementerJsonResult(
    string Type,
    string? Namespace,
    string Kind,
    string Relationship,
    string? Library,
    string? Source,
    string? SourceVersion)
{
    internal static ImplementerJsonResult From(ImplementerResult result) => new(
        result.TypeName,
        result.Namespace,
        result.Kind,
        result.Relationship,
        result.Assembly,
        result.Source,
        result.SourceVersion);
}
