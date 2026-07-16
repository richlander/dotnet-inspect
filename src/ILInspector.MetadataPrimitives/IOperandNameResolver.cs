namespace ILInspector.Metadata;

/// <summary>
/// Resolves metadata-backed IL operand tokens for instruction rendering.
/// </summary>
public interface IOperandNameResolver
{
    ILSyntax Syntax { get; }
    string ResolveType(int token);
    string ResolveMethod(int token);
    string ResolveField(int token);
    string ResolveString(int token);
    string ResolveToken(int token);
}

/// <summary>
/// Selects how IL instructions and their metadata operands are rendered.
/// </summary>
public enum ILSyntax
{
    /// <summary>Human-readable C#-style operand names.</summary>
    Display,

    /// <summary>
    /// Canonical ilasm syntax — assembly-qualified type names, IL primitive names,
    /// return types and calling conventions on member refs — suitable for feeding
    /// back through an IL assembler.
    /// </summary>
    Canonical,
}
