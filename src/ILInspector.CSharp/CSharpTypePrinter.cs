using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public sealed class CSharpTypePrinter
{
    public CSharpTypePrintResult Print(
        CSharpTypePrintRequest request,
        CSharpTypePrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Print([request], options);
    }

    public CSharpTypePrintResult Print(
        IEnumerable<CSharpTypePrintRequest> requests,
        CSharpTypePrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        options ??= new CSharpTypePrintOptions();

        var requestList = requests.ToArray();
        if (requestList.Any(request => request is null))
            throw new ArgumentException("Type print requests cannot contain null entries.", nameof(requests));

        var outputIdentities = new HashSet<TypeOutputIdentity>();
        foreach (var request in requestList)
        {
            if (request.BodyPolicy != CSharpTypeBodyPolicy.Skeleton)
            {
                throw new NotSupportedException(
                    $"C# type body policy '{request.BodyPolicy}' requires a body provider; "
                    + "this printer currently supports skeleton requests.");
            }

            ValidateTopLevelSkeletonType(request.Type);

            var outputIdentity = new TypeOutputIdentity(
                NormalizeNamespace(request.Type.Namespace),
                request.Type.Name);
            if (!outputIdentities.Add(outputIdentity))
            {
                throw new ArgumentException(
                    $"Type print requests contain duplicate C# type '{request.Type.FullName}'.",
                    nameof(requests));
            }
        }

        var units = ImmutableArray.CreateBuilder<CSharpTypeSourceUnit>();
        var diagnostics = ImmutableArray.CreateBuilder<CSharpTypePrintDiagnostic>();

        foreach (var group in requestList.GroupBy(
                     request => NormalizeNamespace(request.Type.Namespace),
                     StringComparer.Ordinal))
        {
            var containingNamespace = group.Key.Length == 0 ? null : group.Key;
            var declarationOptions = new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ContextualShort,
                ContainingNamespace = containingNamespace,
                NamespaceMode = CSharpNamespaceMode.Omit,
                TerminateMemberDeclaration = true,
                IncludeCustomAttributes = options.IncludeCustomAttributes
            };

            var typeSources = new List<string>();
            foreach (var request in group)
            {
                var rendered = CSharpDeclarationWriter.RenderTypeUnit(
                    request.Type,
                    request.Members ?? request.Type.Members,
                    declarationOptions);

                if (rendered.Usings.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Namespace-batched type source cannot contain declaration-local using directives.");
                }

                typeSources.Add(rendered.Source);
                diagnostics.AddRange(rendered.Diagnostics.Select(
                    diagnostic => new CSharpTypePrintDiagnostic(request.Type.FullName, diagnostic)));
            }

            var source = string.Join("\n\n", typeSources);
            if (containingNamespace is not null)
                source = $"namespace {containingNamespace};\n\n{source}";

            units.Add(new CSharpTypeSourceUnit(containingNamespace, source));
        }

        return new CSharpTypePrintResult(units.ToImmutable(), diagnostics.ToImmutable());
    }

    static string NormalizeNamespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value;

    static void ValidateTopLevelSkeletonType(ApiType type)
    {
        if (type.MetadataName?.Contains('+', StringComparison.Ordinal) == true
            || type.Name.Contains('.', StringComparison.Ordinal)
            || type.Name.Contains('+', StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"C# skeleton printing for nested type '{type.FullName}' requires its declaring type.");
        }

        if (type.Kind is not ("class" or "struct" or "interface" or "record"))
        {
            throw new NotSupportedException(
                $"C# skeleton printing does not yet support type kind '{type.Kind}' for '{type.FullName}'.");
        }
    }

    readonly record struct TypeOutputIdentity(string Namespace, string Name);
}
