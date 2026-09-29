using DotnetInspect.Cli.Models;

namespace DotnetInspect.Cli.Output;

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
