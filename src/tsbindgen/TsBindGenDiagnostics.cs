namespace tsbindgen;

internal sealed class TsBindGenDiagnostics
{
    private readonly List<TsBindGenDiagnostic> _unmappedTypes = [];

    public IReadOnlyList<TsBindGenDiagnostic> UnmappedTypes => _unmappedTypes;

    public bool HasUnmappedTypes => _unmappedTypes.Count > 0;

    public void ReportUnmappedType(string location, string csharpType)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentException.ThrowIfNullOrEmpty(csharpType);
        _unmappedTypes.Add(new TsBindGenDiagnostic(location, csharpType));
    }
}

internal readonly record struct TsBindGenDiagnostic(string Location, string CSharpType);
