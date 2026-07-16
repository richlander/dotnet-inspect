using System.Reflection.Metadata;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed class MetadataOperandNameResolver(
    MetadataReader reader,
    ILSyntax syntax = ILSyntax.Display) : IOperandNameResolver
{
    public ILSyntax Syntax { get; } = syntax;

    public string ResolveType(int token)
        => Syntax == ILSyntax.Canonical
            ? CanonicalIL.ResolveType(reader, token)
            : ILTokenResolver.ResolveType(reader, token);

    public string ResolveMethod(int token)
        => Syntax == ILSyntax.Canonical
            ? CanonicalIL.ResolveMethod(reader, token)
            : ILTokenResolver.ResolveMethod(reader, token);

    public string ResolveField(int token)
        => Syntax == ILSyntax.Canonical
            ? CanonicalIL.ResolveField(reader, token)
            : ILTokenResolver.ResolveField(reader, token);

    public string ResolveString(int token)
        => Syntax == ILSyntax.Canonical
            ? CanonicalIL.ResolveString(reader, token)
            : ILTokenResolver.ResolveString(reader, token);

    public string ResolveToken(int token)
        => Syntax == ILSyntax.Canonical
            ? CanonicalIL.ResolveToken(reader, token)
            : ILTokenResolver.ResolveToken(reader, token);
}
