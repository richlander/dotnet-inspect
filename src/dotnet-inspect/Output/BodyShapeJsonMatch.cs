using ILInspector.Decompiler;

namespace DotnetInspector.Output;

public sealed record BodyShapeJsonMatch(
    string AssemblyName,
    string Member,
    string TypeName,
    string MethodName,
    string MethodToken,
    string Kind,
    PrintedExtent Extent,
    string Text)
{
    internal static BodyShapeJsonMatch FromMatch(BodyShapeMatch match)
        => new(
            match.AssemblyName,
            match.Member,
            match.TypeName,
            match.MethodName,
            $"0x{match.MethodToken:X8}",
            match.Kind,
            match.Extent,
            match.Text);
}
