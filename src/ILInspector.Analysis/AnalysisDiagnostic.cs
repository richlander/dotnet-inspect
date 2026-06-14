namespace ILInspector.Analysis;

public sealed record AnalysisDiagnostic(
    int MethodToken,
    string Method,
    string Message);
