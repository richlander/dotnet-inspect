using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

/// <summary>
/// Composes one selected accessor body with its metadata-owned property
/// declaration. The selected MethodDef remains the body/evidence identity.
/// </summary>
public sealed class SelectedPropertyAccessorSource
{
    readonly ApiMember _property;
    readonly string _accessorKind;
    readonly IReadOnlyList<string> _valueAttributes;

    public IReadOnlyList<string> Attributes { get; private init; } = [];

    SelectedPropertyAccessorSource(
        ApiMember property, string accessorKind, IReadOnlyList<string> valueAttributes)
    {
        _property = property;
        _accessorKind = accessorKind;
        _valueAttributes = valueAttributes;
    }

    /// <summary>
    /// Returns null for methods without an owning property, indexers, and
    /// properties whose backing storage can be projected as a recursive
    /// property access, or whose selected override has narrowed accessibility.
    /// The handle must already be resolved in this reader.
    /// </summary>
    public static SelectedPropertyAccessorSource? Create(
        MetadataSource source, int methodToken, ApiMember method,
        bool includeAttributes = false)
    {
        var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken);
        var selected = Create(source.Reader, handle, method);
        return selected is null ? null : new(selected._property, selected._accessorKind, selected._valueAttributes)
        {
            Attributes = includeAttributes
                ? AttributeReader.RenderMethodAttributes(source.Reader, handle)
                : [],
        };
    }

    internal static SelectedPropertyAccessorSource? Create(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        ApiMember method)
    {
        var definition = reader.GetMethodDefinition(methodHandle);
        var type = reader.GetTypeDefinition(definition.GetDeclaringType());
        foreach (var handle in type.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            var accessors = property.GetAccessors();
            bool getter = accessors.Getter == methodHandle;
            if (!getter && accessors.Setter != methodHandle)
                continue;

            var signature = GuardedSignatureText.PropertyText(
                reader, property, GenericContext.ForType(reader, type));
            if (!signature.ParameterTypes.IsEmpty)
                return null;

            if (method.IsOverride)
            {
                var declaration = MetadataDeclarationQuery.GetProperty(reader, type, property);
                string role = getter ? "get" : "set";
                // A narrowed accessor cannot supply the inherited property's accessibility.
                if (declaration.Signature.Accessors.Any(accessor =>
                    accessor.Kind == role && accessor.Accessibility is not null))
                    return null;
            }

            string name = reader.GetString(property.Name);
            foreach (var fieldHandle in type.GetFields())
            {
                string fieldName = reader.GetString(reader.GetFieldDefinition(fieldHandle).Name);
                if (CSharpNaming.BackingFieldProperty(fieldName) == name)
                    return null;
            }

            string? propertyType = getter
                ? method.SignatureModel?.ReturnType ?? method.ReturnType
                : method.SignatureModel?.Parameters.LastOrDefault()?.Type;
            if (string.IsNullOrEmpty(propertyType))
                throw new InvalidOperationException("The selected property accessor does not provide its property type.");

            string keyword = getter ? "get"
                : MetadataDeclarationQuery.IsInitOnlySetter(reader, type, definition) ? "init" : "set";
            int token = MetadataTokens.GetToken(methodHandle);
            List<string> returnAttributes = [];
            List<string> valueAttributes = [];
            bool hasImplicitValueName = getter;
            foreach (var parameterHandle in definition.GetParameters())
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber == 0)
                    returnAttributes = AttributeReader.RenderParameterAttributes(reader, parameterHandle);
                else if (!getter && parameter.SequenceNumber == 1)
                {
                    hasImplicitValueName = reader.GetString(parameter.Name) == "value";
                    valueAttributes = AttributeReader.RenderParameterAttributes(reader, parameterHandle);
                }
            }
            if (!hasImplicitValueName)
                return null;
            var selectedProperty = new ApiMember
            {
                Name = name,
                Kind = method.Kind == "explicit-interface-implementation"
                    ? method.Kind : "property",
                ReturnType = propertyType,
                DeclarationMetadataToken = MetadataTokens.GetToken(handle),
                GetterToken = getter ? token : null,
                SetterToken = getter ? null : token,
                IndexParameterCount = 0,
                Accessibility = method.Accessibility,
                IsStatic = method.IsStatic,
                IsVirtual = method.IsVirtual,
                IsOverride = method.IsOverride,
                IsSealed = method.IsSealed,
                IsAbstract = method.IsAbstract,
                IsReadOnly = method.IsReadOnly,
                IsUnsafe = method.IsUnsafe,
                SignatureModel = new ApiSignature
                {
                    MemberName = name,
                    ReturnType = propertyType,
                    Accessors =
                    [
                        new ApiAccessor
                        {
                            Kind = keyword,
                            Name = method.Name,
                            ReturnAttributes = returnAttributes,
                        },
                    ],
                },
            };
            return new(selectedProperty, keyword, valueAttributes);
        }
        return null;
    }

    /// <summary>
    /// Renders only the selected accessor. Attributes are method-targeted
    /// contents without brackets, never property attributes.
    /// </summary>
    public string Format(
        ApiType type,
        CSharpBlockBody body,
        bool bodyIsSingleExpressionBody = false,
        bool preferExpressionBodied = true,
        IReadOnlyList<string>? attributes = null,
        IReadOnlyList<string>? leadingBodyComments = null,
        string? declarationTrailingComment = null,
        bool includeSignatureAttributes = true,
        bool wrapExpressionBodyArrow = false,
        int indent = 0)
    {
        if (body.RequiresAsyncModifier)
            throw new InvalidOperationException("C# properties cannot carry an async accessor modifier.");
        if (body.ParameterNames is { Count: > 0 } names
            && (_accessorKind == "get" || names.Count != 1 || names[0] != "value"))
        {
            throw new InvalidOperationException(
                "The selected property accessor body does not match its implicit parameter bindings.");
        }

        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            OmitPropertyAccessors = true,
            OmitInterfaceMemberModifiers = true,
            IncludeCustomAttributes = false,
            IncludeObsoleteAttribute = false,
            IncludeSignatureAttributes = includeSignatureAttributes,
        });
        string head = formatter.FormatMemberWithBody(type, _property, body);
        string accessor = formatter.FormatAccessorHead(type, _property, _accessorKind);
        if (includeSignatureAttributes && _valueAttributes.Count > 0)
            accessor = $"[param: {string.Join(", ", _valueAttributes)}] {accessor}";
        string content = body.Source.TrimEnd();
        bool hasAttributes = attributes is { Count: > 0 } || accessor != _accessorKind;
        bool hasComments = leadingBodyComments is { Count: > 0 };
        bool expressionBody = preferExpressionBodied && !hasComments
            && (bodyIsSingleExpressionBody || CSharpExpressionBody.FromSingleStatement(content) is not null);
        var sb = new StringBuilder();

        if (_accessorKind == "get" && !hasAttributes && expressionBody)
        {
            CSharpMemberLayout.Append(
                sb, head, content, indent, wrapExpressionBodyArrow,
                bodyIsSingleExpressionBody);
            string expression = AnnotationCaret.Flatten(sb.ToString().TrimEnd());
            return declarationTrailingComment is { Length: > 0 } comment
                ? $"{expression}  // {comment}" : expression;
        }

        string pad = new(' ', indent);
        sb.Append(pad).Append(head);
        if (declarationTrailingComment is { Length: > 0 })
            sb.Append("  // ").Append(declarationTrailingComment);
        sb.Append('\n').Append(pad).Append("{\n");
        foreach (string attribute in attributes ?? [])
            sb.Append(pad).Append("    [").Append(attribute).Append("]\n");
        if (expressionBody)
        {
            CSharpMemberLayout.Append(
                sb, accessor, content, indent + 4, wrapExpressionBodyArrow,
                bodyIsSingleExpressionBody);
        }
        else
        {
            sb.Append(pad).Append("    ").Append(accessor).Append('\n');
            sb.Append(pad).Append("    {\n");
            IEnumerable<string> lines = content.ReplaceLineEndings("\n").Split('\n');
            if (hasComments)
                lines = leadingBodyComments!.Concat(lines);
            foreach (string line in lines)
            {
                if (line.Length != 0)
                {
                    sb.Append(pad);
                    if (AnnotationCaret.TryHoist(line, out string hoisted))
                        sb.Append("    ").Append(hoisted);
                    else
                        sb.Append("        ").Append(line);
                }
                sb.Append('\n');
            }
            sb.Append(pad).Append("    }\n");
        }
        sb.Append(pad).Append('}');
        return sb.ToString();
    }
}
