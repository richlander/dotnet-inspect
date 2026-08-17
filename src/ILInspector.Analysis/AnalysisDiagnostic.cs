namespace ILInspector.Analysis;

public sealed record AnalysisDiagnostic(
    int MethodToken,
    string Method,
    string Message,
    int? SourceMethodToken = null,
    TypeRef? DeclaringType = null,
    TypeRef? SourceDeclaringType = null,
    int? DeclaringTypeToken = null);
