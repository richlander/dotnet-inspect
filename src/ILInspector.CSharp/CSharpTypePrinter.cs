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
        return PrintBatch([request], options);
    }

    public CSharpTypePrintResult PrintBatch(
        IEnumerable<CSharpTypePrintRequest> requests,
        CSharpTypePrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        options ??= new CSharpTypePrintOptions();

        var requestList = requests.ToArray();
        if (requestList.Any(request => request is null))
            throw new ArgumentException("Type print requests cannot contain null entries.", nameof(requests));

        var preparedTypes = new List<PreparedTypeSource>();
        var canonicalIdentities = new HashSet<TypeOutputIdentity>();
        var outputIdentities = new HashSet<TypeOutputIdentity>();
        var diagnostics = ImmutableArray.CreateBuilder<CSharpTypePrintDiagnostic>();
        foreach (var request in requestList)
        {
            if (request.BodyPolicy != CSharpTypeBodyPolicy.Skeleton)
            {
                throw new NotSupportedException(
                    $"C# type body policy '{request.BodyPolicy}' requires a body provider; "
                    + "this printer currently supports skeleton requests.");
            }

            ValidateRequiredShape(request.Type);
            ValidateTopLevelSkeletonType(request.Type);

            var containingNamespace = NormalizeNamespace(request.Type.Namespace);
            var canonicalIdentity = new TypeOutputIdentity(
                containingNamespace,
                string.IsNullOrWhiteSpace(request.Type.MetadataName)
                    ? request.Type.Name
                    : request.Type.MetadataName);
            var outputIdentity = new TypeOutputIdentity(
                containingNamespace,
                request.Type.Name);
            if (!canonicalIdentities.Add(canonicalIdentity)
                || !outputIdentities.Add(outputIdentity))
            {
                throw new ArgumentException(
                    $"Type print requests contain duplicate C# type '{request.Type.FullName}'.",
                    nameof(requests));
            }

            var declarationOptions = new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ContextualShort,
                ContainingNamespace = containingNamespace.Length == 0 ? null : containingNamespace,
                NamespaceMode = CSharpNamespaceMode.Omit,
                TerminateMemberDeclaration = true,
                IncludeCustomAttributes = options.IncludeCustomAttributes
            };

            var members = request.Members ?? request.Type.Members
                ?? throw new ArgumentException(
                    $"Type '{request.Type.FullName}' has a null member collection.",
                    nameof(requests));
            if (members.Any(member => member is null))
            {
                throw new ArgumentException(
                    $"Type '{request.Type.FullName}' has a null member entry.",
                    nameof(requests));
            }

            var rendered = CSharpDeclarationWriter.RenderTypeUnit(
                request.Type,
                members.ToArray(),
                declarationOptions);

            if (rendered.Usings.Count > 0)
            {
                throw new InvalidOperationException(
                    "Namespace-batched type source cannot contain declaration-local using directives.");
            }

            var typeName = request.Type.FullName;
            preparedTypes.Add(new PreparedTypeSource(containingNamespace, rendered.Source));
            diagnostics.AddRange(rendered.Diagnostics.Select(
                diagnostic => new CSharpTypePrintDiagnostic(typeName, diagnostic)));
        }

        var units = ImmutableArray.CreateBuilder<CSharpTypeSourceUnit>();
        foreach (var group in preparedTypes.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            var containingNamespace = group.Key.Length == 0 ? null : group.Key;
            var source = string.Join("\n\n", group.Select(type => type.Source));
            if (containingNamespace is not null)
                source = $"namespace {containingNamespace};\n\n{source}";

            units.Add(new CSharpTypeSourceUnit(containingNamespace, source));
        }

        return new CSharpTypePrintResult(units.ToImmutable(), diagnostics.ToImmutable());
    }

    static string NormalizeNamespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value;

    static void ValidateRequiredShape(ApiType type)
    {
        if (string.IsNullOrWhiteSpace(type.Name))
            throw new ArgumentException("Type print requests require a non-empty type name.");
        if (type.TypeParameters is null)
            throw new ArgumentException($"Type '{type.FullName}' has a null type-parameter collection.");
        if (type.Name.Contains('<', StringComparison.Ordinal)
            || type.Name.Contains('>', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' must use a metadata name rather than C# type-argument spelling.");
        }

        var tick = type.Name.LastIndexOf('`');
        if (tick < 0)
        {
            if (type.TypeParameters.Count > 0)
            {
                throw new ArgumentException(
                    $"Generic type '{type.FullName}' requires metadata arity in its name.");
            }

            return;
        }

        if (!int.TryParse(type.Name.AsSpan(tick + 1), out var arity)
            || arity <= 0
            || arity != type.TypeParameters.Count)
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' has inconsistent metadata arity and type parameters.");
        }
    }

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

    readonly record struct PreparedTypeSource(string Namespace, string Source);

    readonly record struct TypeOutputIdentity(string Namespace, string Name);
}
