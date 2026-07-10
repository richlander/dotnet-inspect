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
            var memberArray = (request.Members ?? request.Type.Members
                ?? throw new ArgumentException(
                    $"Type '{request.Type.FullName}' has a null member collection.",
                    nameof(requests)))
                .ToArray();
            if (memberArray.Any(member => member is null))
            {
                throw new ArgumentException(
                    $"Type '{request.Type.FullName}' has a null member entry.",
                    nameof(requests));
            }

            var type = SnapshotTypeForRendering(request.Type, memberArray);
            ValidateRequiredShape(type);
            ValidateTopLevelSkeletonType(type);
            ValidateResolvedBodyPolicies(request, memberArray, nameof(requests));

            var containingNamespace = NormalizeNamespace(type.Namespace);
            var canonicalIdentity = new TypeOutputIdentity(
                containingNamespace,
                string.IsNullOrWhiteSpace(type.MetadataName)
                    ? type.Name
                    : type.MetadataName);
            var outputIdentity = new TypeOutputIdentity(
                containingNamespace,
                type.Name);
            if (!canonicalIdentities.Add(canonicalIdentity)
                || !outputIdentities.Add(outputIdentity))
            {
                throw new ArgumentException(
                    $"Type print requests contain duplicate C# type '{type.FullName}'.",
                    nameof(requests));
            }

            var formatter = new CSharpFormatter(new CSharpFormatOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                ContainingNamespace = containingNamespace.Length == 0 ? null : containingNamespace,
                NamespacePolicy = CSharpNamespacePolicy.Omit,
                TerminateMemberDeclaration = true,
                IncludeCustomAttributes = options.IncludeCustomAttributes
            });

            var rendered = formatter.FormatTypeUnit(
                type,
                type.Members);

            if (rendered.Usings.Count > 0)
            {
                throw new InvalidOperationException(
                    "Namespace-batched type source cannot contain declaration-local using directives.");
            }

            var typeName = type.FullName;
            preparedTypes.Add(new PreparedTypeSource(containingNamespace, rendered.Text));
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

    internal static ApiType SnapshotTypeForRendering(
        ApiType type,
        IReadOnlyList<ApiMember> members)
    {
        var attributes = type.Attributes;
        var interfaces = type.Interfaces;
        var typeParameters = type.TypeParameters;
        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            MetadataName = type.MetadataName,
            Kind = type.Kind,
            Attributes = attributes?.ToList()!,
            EnumUnderlyingType = type.EnumUnderlyingType,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            IsByRefLike = type.IsByRefLike,
            IsReadOnly = type.IsReadOnly,
            BaseType = type.BaseType,
            Interfaces = interfaces?.ToList()!,
            TypeParameters = typeParameters?.Select(SnapshotTypeParameter).ToList()!,
            Members = members.Select(SnapshotMember).ToList()
        };
    }

    static ApiMember SnapshotMember(ApiMember member)
    {
        var attributes = member.Attributes;
        var signatureModel = member.SignatureModel;
        return new ApiMember
        {
            Name = member.Name,
            Kind = member.Kind,
            Attributes = attributes?.ToList()!,
            ReturnType = member.ReturnType,
            Signature = member.Signature,
            SignatureModel = signatureModel is null ? null : SnapshotSignature(signatureModel),
            IsStatic = member.IsStatic,
            IsVirtual = member.IsVirtual,
            IsAbstract = member.IsAbstract,
            IsOverride = member.IsOverride,
            IsSealed = member.IsSealed,
            IsReadOnly = member.IsReadOnly,
            IsConst = member.IsConst,
            IsUnsafe = member.IsUnsafe,
            Accessibility = member.Accessibility,
            IsExtension = member.IsExtension,
            IsObsolete = member.IsObsolete,
            ObsoleteMessage = member.ObsoleteMessage
        };
    }

    static ApiSignature SnapshotSignature(ApiSignature signature)
    {
        var returnAttributes = signature.ReturnAttributes;
        var typeParameters = signature.TypeParameters;
        var parameters = signature.Parameters;
        var accessors = signature.Accessors;
        return new ApiSignature
        {
            ReturnType = signature.ReturnType,
            ReturnAttributes = returnAttributes?.ToList()!,
            MemberName = signature.MemberName,
            IsRequired = signature.IsRequired,
            TypeParameters = typeParameters?.Select(SnapshotTypeParameter).ToList()!,
            Parameters = parameters?.Select(SnapshotParameter).ToList()!,
            Accessors = accessors?.Select(SnapshotAccessor).ToList()!
        };
    }

    static TypeParameter SnapshotTypeParameter(TypeParameter parameter)
    {
        var constraints = parameter.Constraints;
        return new TypeParameter
        {
            Name = parameter.Name,
            Variance = parameter.Variance,
            Constraints = constraints?.ToList()!
        };
    }

    static ApiParameter SnapshotParameter(ApiParameter parameter)
    {
        var attributes = parameter.Attributes;
        return new ApiParameter
        {
            Attributes = attributes?.ToList()!,
            Name = parameter.Name,
            Type = parameter.Type,
            Modifier = parameter.Modifier,
            HasDefault = parameter.HasDefault,
            DefaultValueText = parameter.DefaultValueText
        };
    }

    static ApiAccessor SnapshotAccessor(ApiAccessor accessor)
    {
        var returnAttributes = accessor.ReturnAttributes;
        return new ApiAccessor
        {
            Kind = accessor.Kind,
            Accessibility = accessor.Accessibility,
            ReturnAttributes = returnAttributes?.ToList()!
        };
    }

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

    static void ValidateResolvedBodyPolicies(
        CSharpTypePrintRequest request,
        IReadOnlyList<ApiMember> members,
        string parameterName)
    {
        var selectedMembers = new HashSet<ApiMember>(members, ReferenceEqualityComparer.Instance);
        var overrides = new Dictionary<ApiMember, CSharpBodyPolicy>(ReferenceEqualityComparer.Instance);
        foreach (var policy in request.MemberPolicyOverrides)
        {
            if (!selectedMembers.Contains(policy.Member))
            {
                throw new ArgumentException(
                    $"Member policy override '{policy.Member.Name}' is not in the selected member set.",
                    parameterName);
            }
            if (!overrides.TryAdd(policy.Member, policy.BodyPolicy))
            {
                throw new ArgumentException(
                    $"Member '{policy.Member.Name}' has multiple policy overrides.",
                    parameterName);
            }
        }

        foreach (var member in members)
        {
            var bodyPolicy = overrides.TryGetValue(member, out var memberPolicy)
                ? memberPolicy
                : request.BodyPolicy;
            if (bodyPolicy != CSharpBodyPolicy.Skeleton)
            {
                throw new NotSupportedException(
                    $"C# member body policy '{bodyPolicy}' for '{member.Name}' requires a body provider; "
                    + "this printer currently supports skeleton requests.");
            }
        }
    }

    readonly record struct PreparedTypeSource(string Namespace, string Source);

    readonly record struct TypeOutputIdentity(string Namespace, string Name);
}
