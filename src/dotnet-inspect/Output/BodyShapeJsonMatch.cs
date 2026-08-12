using ILInspector.Decompiler;

namespace DotnetInspector.Output;

internal sealed record BodyShapeJsonMatch(
    string AssemblyName,
    string Member,
    string TypeName,
    string MethodName,
    string MethodToken,
    string Kind,
    PrintedExtent Extent,
    string Text);
