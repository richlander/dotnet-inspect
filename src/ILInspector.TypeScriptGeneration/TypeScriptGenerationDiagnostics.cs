using CSharpText;

namespace ILInspector.TypeScriptGeneration;

internal sealed class TypeScriptGenerationDiagnostics
{
    private readonly List<TypeScriptGenerationDiagnostic> _unmappedTypes = [];

    public IReadOnlyList<TypeScriptGenerationDiagnostic> UnmappedTypes => _unmappedTypes;

    public bool HasUnmappedTypes => _unmappedTypes.Count > 0;

    public void ReportUnmappedType(string location, string csharpType)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentException.ThrowIfNullOrEmpty(csharpType);
        _unmappedTypes.Add(
            new TypeScriptGenerationDiagnostic(
                CSharpIdentifier.ContainRenderedText(location),
                CSharpIdentifier.ContainRenderedText(csharpType)));
    }
}

internal readonly record struct TypeScriptGenerationDiagnostic(
    string Location,
    string CSharpType);
