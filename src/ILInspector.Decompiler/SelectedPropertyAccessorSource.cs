using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
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
    readonly bool _automaticGetter;

    public IReadOnlyList<string> Attributes { get; private init; } = [];

    SelectedPropertyAccessorSource(
        ApiMember property, string accessorKind, IReadOnlyList<string> valueAttributes,
        bool automaticGetter = false)
    {
        _property = property;
        _accessorKind = accessorKind;
        _valueAttributes = valueAttributes;
        _automaticGetter = automaticGetter;
    }

    /// <summary>
    /// Returns null for methods without an owning property, indexers, and
    /// properties whose backing storage is not a proven getter-only
    /// auto-property, or whose selected override has narrowed accessibility.
    /// The handle must already be resolved in this reader.
    /// </summary>
    public static SelectedPropertyAccessorSource? Create(
        MetadataSource source, int methodToken, ApiMember method,
        bool includeAttributes = false)
    {
        var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken);
        var selected = Create(source, handle, method);
        return selected is null ? null : new(
            selected._property, selected._accessorKind, selected._valueAttributes,
            selected._automaticGetter)
        {
            Attributes = includeAttributes
                ? AttributeReader.RenderMethodAttributes(source.Reader, handle)
                : [],
        };
    }

    internal static SelectedPropertyAccessorSource? Create(
        MetadataSource source,
        MethodDefinitionHandle methodHandle,
        ApiMember method)
    {
        var reader = source.Reader;
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
            bool hasBackingStorage = false;
            foreach (var fieldHandle in type.GetFields())
            {
                string fieldName = reader.GetString(reader.GetFieldDefinition(fieldHandle).Name);
                if (CSharpNaming.BackingFieldProperty(fieldName) == name)
                    hasBackingStorage = true;
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
            if (hasBackingStorage
                && !IsAutomaticGetter(source, handle, methodHandle, selectedProperty))
                return null;
            return new(selectedProperty, keyword, valueAttributes, hasBackingStorage);
        }
        return null;
    }

    static bool IsAutomaticGetter(
        MetadataSource source, PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle methodHandle, ApiMember property)
    {
        var reader = source.Reader;
        var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
        if (accessors.Getter != methodHandle || !accessors.Setter.IsNil
            || property.IsUnsafe)
            return false;
        var method = reader.GetMethodDefinition(methodHandle);
        if (method.GetGenericParameters().Count != 0)
            return false;
        var typeHandle = method.GetDeclaringType();
        if (!MemberBodyProducer.IsCompilerGeneratedAutoProperty(
                source, reader, typeHandle, property, methodHandle, null, out var backingFieldHandle))
            return false;

        var body = source.Pe.GetMethodBody(method.RelativeVirtualAddress);
        if (!body.ExceptionRegions.IsEmpty || !body.LocalSignature.IsNil)
            return false;
        var decoded = MethodInstructions.Decode(body);
        if (!decoded.IsComplete)
            return false;
        var instructions = decoded.Instructions
            .Where(instruction => instruction.OpCode != ILOpCode.Nop).ToArray();
        var fieldInstruction = property.IsStatic
            ? instructions is [{ OpCode: ILOpCode.Ldsfld }, { OpCode: ILOpCode.Ret }]
                ? instructions[0] : null
            : instructions is [{ OpCode: ILOpCode.Ldarg_0 }, { OpCode: ILOpCode.Ldfld }, { OpCode: ILOpCode.Ret }]
                ? instructions[1] : null;
        if (fieldInstruction is null)
            return false;

        var type = reader.GetTypeDefinition(typeHandle);
        var genericNames = type.GetGenericParameters()
            .Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))
            .ToImmutableArray();
        var field = IrImporter.ResolveField(
            reader, MetadataTokens.EntityHandle((int)fieldInstruction.OperandValue),
            new GenericScope(genericNames, []));
        if (field.Type.Kind == TypeRefKind.ByRef || UnsafeAwaitOperand.ContainsPointer(field.Type))
            return false;
        if (field.DeclaringType.Kind == TypeRefKind.GenericInstance)
        {
            var arguments = field.DeclaringType.TypeArguments;
            if (arguments.Length != genericNames.Length
                || arguments.Where((argument, index) =>
                    argument.Kind != TypeRefKind.GenericParameter
                    || argument.GenericParameterIndex != index).Any())
                return false;
        }
        else if (genericNames.Length != 0)
            return false;

        var definition = reader.GetFieldDefinition(backingFieldHandle);
        var expected = FieldAttributes.Private | FieldAttributes.InitOnly
            | (property.IsStatic ? FieldAttributes.Static : 0);
        if (definition.Attributes != expected || definition.GetOffset() >= 0)
            return false;
        foreach (var attributeHandle in definition.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            string? name = AttributeReader.GetAttributeTypeName(reader, attribute.Constructor);
            if (name == "System.Diagnostics.DebuggerBrowsableAttribute")
            {
                var value = reader.GetBlobReader(attribute.Value);
                if (value.RemainingBytes != 8 || value.ReadUInt16() != 1
                    || value.ReadInt32() != (int)System.Diagnostics.DebuggerBrowsableState.Never
                    || value.ReadUInt16() != 0)
                    return false;
            }
            else if (name is not (KnownAttributeNames.CompilerGeneratedAttribute
                or KnownAttributeNames.NullableAttribute))
                return false;
        }
        return true;
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
        if (_automaticGetter)
            return FormatAutomaticGetter(head, accessor, content, attributes,
                leadingBodyComments, declarationTrailingComment, indent);
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

    static string FormatAutomaticGetter(
        string head, string accessor, string content, IReadOnlyList<string>? attributes,
        IReadOnlyList<string>? leadingBodyComments, string? declarationTrailingComment, int indent)
    {
        // The complete IL shell established the replacement. Retain presentation
        // comments from its literal-free field-load body, not its recursive spelling.
        var comments = new List<string>(leadingBodyComments ?? []);
        foreach (string line in content.ReplaceLineEndings("\n").Split('\n'))
            if (line.IndexOf("//", StringComparison.Ordinal) is >= 0 and var start)
                comments.Add(line[start..]);
        string pad = new(' ', indent);
        if (attributes is not { Count: > 0 } && comments.Count == 0 && accessor == "get")
            return $"{pad}{head} {{ get; }}"
                + (declarationTrailingComment is { Length: > 0 }
                    ? $"  // {declarationTrailingComment}" : "");
        var sb = new StringBuilder();
        sb.Append(pad).Append(head);
        if (declarationTrailingComment is { Length: > 0 })
            sb.Append("  // ").Append(declarationTrailingComment);
        sb.Append('\n').Append(pad).Append("{\n");
        foreach (string attribute in attributes ?? [])
            sb.Append(pad).Append("    [").Append(attribute).Append("]\n");
        sb.Append(pad).Append("    ").Append(accessor).Append(";\n");
        foreach (string comment in comments)
            sb.Append(pad).Append("    ").Append(comment).Append('\n');
        sb.Append(pad).Append('}');
        return sb.ToString();
    }
}
