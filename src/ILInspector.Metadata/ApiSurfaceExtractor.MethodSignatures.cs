using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

public static partial class ApiSurfaceExtractor
{

    internal static (string Text, ApiSignature Model, bool IsDegraded)
        GetMethodSignatureForIdentity(
            MetadataReader reader,
            GenericContext typeContext,
            MethodDefinitionHandle methodHandle,
            MethodDefinition method,
            byte typeNullableContext,
            Action<string>? beforeRetainText = null,
            Action<int>? beforeDecodeWork = null)
        => GetMethodSignature(
            reader,
            typeContext,
            methodHandle,
            method,
            typeNullableContext,
            beforeRetainText: beforeRetainText,
            beforeDecodeWork: beforeDecodeWork,
            beforeAttributeMaterialize: beforeDecodeWork);

    private static (string Text, ApiSignature Model, bool IsDegraded) GetMethodSignature(
        MetadataReader reader,
        GenericContext typeContext,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        byte typeNullableContext,
        bool captureExtensionReceiver = false,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null,
        TypeParameterConstraintResolution? constraintResolution = null,
        Action<int>? beforeAttributeMaterialize = null)
    {
        Action<int>? attributeMaterialize =
            beforeAttributeMaterialize ?? beforeDecodeWork;
        string name = DecodeString(
            reader,
            method.Name,
            beforeDecodeWork);
        var context = GenericContext.ForMethod(
            reader,
            typeContext,
            method,
            beforeDecodeWork);
        var typeNodeProvider = beforeRetainText is null
            ? TypeNodeProvider.Instance
            : new TypeNodeProvider(beforeRetainText, beforeDecodeWork);
        var treeSignature = GuardedProviderDecode.Method(
            reader,
            method,
            typeNodeProvider,
            context,
            (TypeNode)new DegradedTypeNode());

        // Determine the effective nullable default: method overrides type
        byte nullableDefault =
            NullabilityReader.GetNullableContext(
                reader,
                method.GetCustomAttributes(),
                beforeDecodeWork)
            ?? typeNullableContext;

        // Apply nullability to return type
        var paramHandles = method.GetParameters();
        var returnBytes = NullabilityReader.GetParameterNullableBytes(
            reader,
            paramHandles,
            0,
            beforeDecodeWork);
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(returnBytes, ref pos, nullableDefault);
        var returnDynamicFlags = DynamicReader.GetParameterDynamicFlags(
            reader,
            paramHandles,
            0,
            beforeDecodeWork);
        pos = 0;
        treeSignature.ReturnType.ApplyDynamic(returnDynamicFlags, ref pos);
        treeSignature.ReturnType.ApplyTupleNames(
            TupleElementNamesReader.GetParameterTupleElementNames(
                reader,
                paramHandles,
                0,
                beforeDecodeWork));

        // Build parameter list with nullability
        var paramTypes = treeSignature.ParameterTypes;
        string? extensionReceiverType = null;
        if (captureExtensionReceiver && paramTypes.Length > 0)
        {
            beforeDecodeWork?.Invoke(
                (int)Math.Min(
                    paramTypes[0].EstimatedRenderedLength,
                    int.MaxValue));
            extensionReceiverType = paramTypes[0].RenderCanonical();
            beforeRetainText?.Invoke(extensionReceiverType);
        }

        List<string> parameters = [];
        List<ApiParameter> parameterModels = [];
        var parameterInfos = Enumerable.Range(1, paramTypes.Length)
            .Select(sequenceNumber => GetParameterInfo(
                reader,
                paramHandles,
                sequenceNumber,
                beforeRetainText,
                attributeMaterialize))
            .ToArray();
        string[] parameterNames = CSharpParameterNames.Allocate(
            parameterInfos.Select(info => info.name).ToArray(),
            context.MethodParameters);
        for (int i = 0; i < paramTypes.Length; i++)
        {
            // Apply nullability to this parameter's type tree
            var paramBytes = NullabilityReader.GetParameterNullableBytes(
                reader,
                paramHandles,
                i + 1,
                beforeDecodeWork);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, nullableDefault);
            var paramDynamicFlags = DynamicReader.GetParameterDynamicFlags(
                reader,
                paramHandles,
                i + 1,
                beforeDecodeWork);
            pos = 0;
            paramTypes[i].ApplyDynamic(paramDynamicFlags, ref pos);
            paramTypes[i].ApplyTupleNames(
                TupleElementNamesReader.GetParameterTupleElementNames(
                    reader,
                    paramHandles,
                    i + 1,
                    beforeDecodeWork));
            string type = paramTypes[i].Render();
            string canonicalType = paramTypes[i].RenderCanonical();

            // Parameter handles may include return parameter at SequenceNumber 0
            // Actual parameters have SequenceNumber 1, 2, 3...
            var (_, isParams, refKind, hasDefault, defaultValue, attributes) =
                parameterInfos[i];
            var isByRef = type.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                type = type["ref ".Length..];
                canonicalType = canonicalType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            bool acceptsNullDefault = AcceptsNullDefault(paramTypes[i]);
            string? defaultValueText = DefaultValueText(
                reader,
                defaultValue,
                type,
                hasDefault,
                acceptsNullDefault,
                beforeDecodeWork);
            var paramStr = FormatParameter(
                type,
                parameterNames[i],
                modifier,
                hasDefault,
                defaultValue,
                defaultValueText);

            beforeRetainText?.Invoke(paramStr);
            var parameterModel = new ApiParameter
            {
                Attributes = attributes,
                Name = parameterNames[i],
                Type = type,
                CanonicalType = canonicalType,
                StructuralType = paramTypes[i].HasStructuralPayload
                    ? paramTypes[i].StructuralIdentity()
                    : null,
                TypeReferences =
                    [.. paramTypes[i].ReferencedTypes().Distinct()],
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = defaultValueText
            };
            ObserveText(parameterModel, beforeRetainText);
            parameters.Add(paramStr);
            parameterModels.Add(parameterModel);
        }

        string paramStr2 = string.Join(", ", parameters);
        var returnType = FormatMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var canonicalReturnType = FormatCanonicalMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var returnAttributes = ReturnParameterAttributes(
            reader,
            paramHandles,
            beforeRetainText,
            attributeMaterialize);
        var methodTypeParameters = GenericParameters(
            reader,
            method.GetGenericParameters(),
            context,
            nullableDefault,
            includeVariance: false,
            methodHandle,
            beforeRetainText,
            beforeDecodeWork,
            constraintResolution);
        var methodName = context.MethodParameters.Count > 0
            ? $"{name}<{string.Join(", ", methodTypeParameters.Select(parameter => parameter.Name))}>"
            : name;
        // MemberName carries source-level generic spelling used by identity
        // consumers, so it keeps the raw metadata spelling; only the rendered
        // signature is sanitized (issue #3319).
        var displayName = context.MethodParameters.Count > 0
            ? $"{SanitizeMemberDisplayName(name)}<{string.Join(", ", methodTypeParameters.Select(parameter => SanitizeIdentifier(parameter.Name)))}>"
            : SanitizeMemberDisplayName(name);
        IReadOnlyList<string>? xmlDocumentationParameterTypes =
            TryGetXmlDocumentationNames(
                treeSignature.ParameterTypes,
                beforeRetainText);
        string? xmlDocumentationReturnType = null;
        if (ApiMemberIdentity.IsConversionOperator(name)
            && treeSignature.ReturnType.TryGetXmlDocumentationName(
                out string? exactReturnType))
        {
            xmlDocumentationReturnType = exactReturnType;
        }
        if (xmlDocumentationReturnType is not null)
            beforeRetainText?.Invoke(xmlDocumentationReturnType);
        return ($"{returnType} {displayName}({paramStr2})", new ApiSignature
        {
            ExtensionReceiverType = extensionReceiverType,
            XmlDocumentationParameterTypes =
                xmlDocumentationParameterTypes,
            XmlDocumentationReturnType =
                xmlDocumentationReturnType,
            XmlDocumentationIsVararg =
                treeSignature.Header.CallingConvention
                    == SignatureCallingConvention.VarArgs,
            ReturnType = returnType,
            CanonicalReturnType = canonicalReturnType,
            StructuralReturnType = treeSignature.ReturnType.HasStructuralPayload
                ? treeSignature.ReturnType.StructuralIdentity()
                : null,
            ReturnTypeReferences =
                [.. treeSignature.ReturnType.ReferencedTypes().Distinct()],
            ReturnTypeDefinitionReference =
                treeSignature.ReturnType.DefinitionReference(),
            ReturnTypeShape =
                ApiTypeShapeFactory.FromTypeNode(
                    treeSignature.ReturnType),
            ReturnAttributes = returnAttributes,
            MemberName = methodName,
            TypeParameters = methodTypeParameters,
            Parameters = parameterModels
        }, treeSignature.ReturnType.IsDegraded
            || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    static IReadOnlyList<string>? TryGetXmlDocumentationNames(
        ImmutableArray<TypeNode> types,
        Action<string>? beforeRetainText)
    {
        var names = new string[types.Length];
        for (int index = 0; index < names.Length; index++)
        {
            if (!types[index].TryGetXmlDocumentationName(out string? name))
                return null;
            names[index] = name;
            beforeRetainText?.Invoke(names[index]);
        }
        return names;
    }

    private static List<string> ReturnParameterAttributes(
        MetadataReader reader,
        ParameterHandleCollection handles,
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in handles)
        {
            if (reader.GetParameter(handle).SequenceNumber == 0)
                return AttributeReader.RenderParameterAttributes(
                    reader,
                    handle,
                    beforeRetain: beforeRetain,
                    beforeMaterialize: beforeMaterialize);
        }

        return [];
    }

    private static List<string> RenderMemberAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
        => AttributeReader.RenderAttributes(
            reader,
            attributes,
            skipAttribute: static name => name == "System.ObsoleteAttribute",
            qualifyNames: true,
            beforeRetain: beforeRetain,
            beforeMaterialize: beforeMaterialize);

    private static string FormatMethodReturnType(
        MetadataReader reader,
        TypeNode returnType,
        ParameterHandleCollection paramHandles,
        Action<int>? beforeMaterialize = null)
    {
        var rendered = returnType.Render();
        if (!rendered.StartsWith("ref ", StringComparison.Ordinal)
            || !IsReadOnlyByRefReturn(
                reader,
                returnType,
                paramHandles,
                beforeMaterialize))
        {
            return rendered;
        }

        return $"ref readonly {rendered["ref ".Length..]}";
    }

    /// <summary>
    /// Canonical (tuple-erased) counterpart to <see cref="FormatMethodReturnType"/>. Mirrors
    /// its <c>ref readonly</c> synthesis so the canonical return spelling preserves by-ref
    /// return modifiers used by member identity, differing from the display spelling only in
    /// tuple rendering.
    /// </summary>
    private static string FormatCanonicalMethodReturnType(
        MetadataReader reader,
        TypeNode returnType,
        ParameterHandleCollection paramHandles,
        Action<int>? beforeMaterialize = null)
    {
        var rendered = returnType.RenderCanonical();
        if (!rendered.StartsWith("ref ", StringComparison.Ordinal)
            || !IsReadOnlyByRefReturn(
                reader,
                returnType,
                paramHandles,
                beforeMaterialize))
        {
            return rendered;
        }

        return $"ref readonly {rendered["ref ".Length..]}";
    }

    private static bool IsReadOnlyByRefReturn(
        MetadataReader reader,
        TypeNode returnType,
        ParameterHandleCollection paramHandles,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in paramHandles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == 0
                && HasReadOnlyByRefAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    beforeMaterialize))
                return true;
        }

        return returnType.HasRequiredModifier("System.Runtime.CompilerServices", "IsReadOnlyAttribute")
            || returnType.HasRequiredModifier("System.Runtime.CompilerServices", "RequiresLocationAttribute")
            || returnType.HasRequiredModifier("System.Runtime.InteropServices", "InAttribute");
    }

    private static bool HasReadOnlyByRefAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.IsReadOnlyAttribute,
                beforeMaterialize)
            || AttributeReader.HasAttribute(
                reader,
                attributes,
                "System.Runtime.CompilerServices.RequiresLocationAttribute",
                beforeMaterialize);

    private static (string? name, bool isParams, string? refKind, bool hasDefault, object? defaultValue, List<string> attributes) GetParameterInfo(
        MetadataReader reader,
        ParameterHandleCollection handles,
        int sequenceNumber,
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var handle in handles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
            {
                string name = DecodeString(
                    reader,
                    param.Name,
                    beforeMaterialize);
                var attributes = param.GetCustomAttributes();
                bool isParams = AttributeReader.HasAttribute(
                        reader,
                        attributes,
                        "System.ParamArrayAttribute",
                        beforeMaterialize)
                    || AttributeReader.HasAttribute(
                        reader,
                        attributes,
                        KnownAttributeNames.ParamCollectionAttribute,
                        beforeMaterialize);
                var renderedAttributes = AttributeReader.RenderParameterAttributes(
                    reader,
                    handle,
                    beforeRetain: beforeRetain,
                    beforeMaterialize: beforeMaterialize);
                // An interop-marshalled `ref` parameter sets both In and Out, so
                // neither flag alone identifies a C# `out`/`in`. Spelling such a
                // parameter `out` breaks definite assignment in the body.
                bool isOut = (param.Attributes & System.Reflection.ParameterAttributes.Out) != 0;
                bool isIn = (param.Attributes & System.Reflection.ParameterAttributes.In) != 0;
                string? refKind = isOut && !isIn
                    ? "out"
                    : isIn && !isOut
                        ? "in"
                        : null;

                bool hasDefault = (param.Attributes & System.Reflection.ParameterAttributes.HasDefault) != 0;
                object? defaultValue = null;

                if (TryReadAttributedParameterDefault(
                    reader,
                    attributes,
                    out var attributedDefault,
                    beforeMaterialize))
                {
                    hasDefault = true;
                    defaultValue = attributedDefault;
                }
                else if (hasDefault)
                {
                    var constantHandle = param.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        beforeMaterialize?.Invoke(
                            reader.GetBlobReader(constant.Value).Length);
                        defaultValue = ReadConstantValue(reader, constant);
                    }
                }

                return (name, isParams, refKind, hasDefault, defaultValue, renderedAttributes);
            }
        }

        return (null, false, null, false, null, []);
    }

    private sealed record DateTimeConstantDefault(long Ticks);

    private static bool TryReadAttributedParameterDefault(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out object? defaultValue,
        Action<int>? beforeMaterialize = null)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = AttributeReader.GetAttributeTypeName(
                reader,
                attribute.Constructor,
                beforeMaterialize);
            if (attributeTypeName == KnownAttributeNames.DecimalConstantAttribute)
            {
                ObserveAttributeValue(reader, attribute, beforeMaterialize);
                if (TryReadDecimalConstantAttribute(
                    reader,
                    attribute,
                    out var decimalValue,
                    beforeMaterialize))
                {
                    defaultValue = decimalValue;
                    return true;
                }
            }

            if (attributeTypeName == KnownAttributeNames.DateTimeConstantAttribute)
            {
                ObserveAttributeValue(reader, attribute, beforeMaterialize);
                if (TryReadDateTimeConstantAttribute(
                    reader,
                    attribute,
                    out var ticks,
                    beforeMaterialize))
                {
                    defaultValue = new DateTimeConstantDefault(ticks);
                    return true;
                }
            }
        }

        defaultValue = null;
        return false;
    }

    static void ObserveAttributeValue(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize)
        => beforeMaterialize?.Invoke(
            reader.GetBlobReader(attribute.Value).Length);

    private static bool TryReadDecimalConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out decimal value,
        Action<int>? beforeMaterialize = null)
    {
        if (AttributeDecoder.TryDecode(reader, attribute, beforeMaterialize) is not { } decoded
            || decoded.FixedArguments.Length != 5
            || decoded.FixedArguments[0].Value is not byte scale
            || decoded.FixedArguments[1].Value is not byte sign
            || !TryGetUInt32(decoded.FixedArguments[2].Value, out var hi)
            || !TryGetUInt32(decoded.FixedArguments[3].Value, out var mid)
            || !TryGetUInt32(decoded.FixedArguments[4].Value, out var low)
            || scale > 28
            || sign > 1)
        {
            value = default;
            return false;
        }

        value = new decimal(
            unchecked((int)low),
            unchecked((int)mid),
            unchecked((int)hi),
            sign != 0,
            scale);
        return true;
    }

    private static bool TryGetUInt32(object? value, out uint result)
    {
        switch (value)
        {
            case uint unsigned:
                result = unsigned;
                return true;
            case int signed:
                result = unchecked((uint)signed);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryReadDateTimeConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out long ticks,
        Action<int>? beforeMaterialize = null)
    {
        if (AttributeDecoder.TryDecode(reader, attribute, beforeMaterialize) is { FixedArguments.Length: 1 } decoded
            && decoded.FixedArguments[0].Value is long value)
        {
            ticks = value;
            return true;
        }

        ticks = 0;
        return false;
    }

    private static object? ReadConstantValue(MetadataReader reader, Constant constant)
    {
        var blob = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => null
        };
    }

    private static string? FormatFieldConstantLiteral(
        MetadataReader reader,
        Constant constant)
    {
        object? value = ReadConstantValue(reader, constant);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean when value is bool boolean =>
                boolean ? "true" : "false",
            ConstantTypeCode.Char when value is char character =>
                $"'{EscapeCharLiteral(character)}'",
            ConstantTypeCode.SByte when value is sbyte number =>
                number.ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Byte when value is byte number =>
                number.ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int16 when value is short number =>
                number.ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.UInt16 when value is ushort number =>
                number.ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int32 when value is int number =>
                number.ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.UInt32 when value is uint number =>
                number.ToString(CultureInfo.InvariantCulture) + "U",
            ConstantTypeCode.Int64 when value is long number =>
                FormatInt64Literal(number),
            ConstantTypeCode.UInt64 when value is ulong number =>
                number.ToString(CultureInfo.InvariantCulture) + "UL",
            ConstantTypeCode.Single when value is float number =>
                FormatSingleConstantLiteral(number),
            ConstantTypeCode.Double when value is double number =>
                FormatDoubleConstantLiteral(number),
            ConstantTypeCode.String when value is string text =>
                StringLiteral(text),
            ConstantTypeCode.NullReference => "null",
            _ => null,
        };
    }

    private static string FormatSingleConstantLiteral(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";
        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture) + "F";
    }

    private static string FormatDoubleConstantLiteral(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    // `null` is a legal default only for a reference type or a Nullable<T> (a
    // value type that nonetheless accepts the `null` literal). A non-nullable
    // value type must spell its null constant `default`.
    private static bool AcceptsNullDefault(TypeNode node)
        => node.IsReferenceType
            || node.Render().StartsWith("System.Nullable<", StringComparison.Ordinal);

    private static string FormatDefaultValue(
        MetadataReader reader,
        object? value,
        string typeName,
        bool acceptsNullDefault,
        Action<int>? beforeDecodeWork = null)
    {
        // A null constant is `default(T)` for a non-nullable value-type parameter
        // (the only legal spelling — `T x = null` is CS1750), and a genuine `null`
        // for reference types and Nullable<T> (both accept `null` as a literal
        // default). value-vs-reference comes from the signature's element type
        // (ELEMENT_TYPE_VALUETYPE), already on the decoded type node.
        if (value == null)
            return acceptsNullDefault ? "null" : "default";

        if (TryFormatEnumDefaultValue(
                reader,
                value,
                typeName,
                beforeDecodeWork) is { } enumValue)
            return enumValue;

        if (!acceptsNullDefault
            && IsLikelyEnumDefaultType(typeName)
            && TryConvertEnumConstant(value, out var defaultValue))
        {
            return $"({typeName}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            decimal d => FormatDecimalLiteral(d),
            string s => StringLiteral(s),
            char c => $"'{EscapeCharLiteral(c)}'",
            float f => f.ToString("G") + "f",
            double d => d.ToString("G"),
            _ => value.ToString() ?? "default"
        };
    }

    private static string? DefaultValueText(
        MetadataReader reader,
        object? value,
        string typeName,
        bool hasDefault,
        bool acceptsNullDefault,
        Action<int>? beforeDecodeWork = null)
    {
        if (!hasDefault || value is DateTimeConstantDefault)
            return null;
        return FormatDefaultValue(
            reader,
            value,
            typeName,
            acceptsNullDefault,
            beforeDecodeWork);
    }

    private static string EscapeCharLiteral(char c) => c switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        // Bidi overrides are category Cf, so char.IsControl is false for them and
        // they would reach the terminal raw (issue #3319). No end-to-end gate
        // covers this particular escaper — every probe reached the sibling
        // escaper below instead — so treat it as unverified hardening that keeps
        // the two spellings consistent, not as a proven-reachable fix.
        _ when CSharpIdentifierCore.RequiresLiteralEscape(c) => $"\\u{(int)c:x4}",
        _ => c.ToString()
    };

    private static string FormatParameter(
        string type,
        string name,
        string? modifier,
        bool hasDefault,
        object? defaultValue,
        string? defaultValueText)
    {
        var escapedName = SanitizeIdentifier(name);
        var parameter = modifier is null ? $"{type} {escapedName}" : $"{modifier} {type} {escapedName}";
        if (!hasDefault)
            return parameter;

        if (defaultValue is DateTimeConstantDefault dateTime)
        {
            var ticks = FormatInt64Literal(dateTime.Ticks);
            return $"[{OptionalAttributeName}, {DateTimeConstantAttributeName}({ticks})] {parameter}";
        }

        return $"{parameter} = {defaultValueText}";
    }

    /// <summary>
    /// The spelling for a metadata name entering emitted C# declaration text.
    /// Keyword escaping alone leaves an unspellable name (one carrying a line
    /// terminator, say) intact, which lets it break out of the surrounding code
    /// fence or tree layout; sanitizing folds it to identifier characters
    /// instead (issue #3319). Byte-neutral for names that are already legal
    /// identifiers, which covers every well-formed assembly.
    /// </summary>
    /// <summary>
    /// The display spelling of a member name. A member name is not always a simple
    /// identifier — <c>.ctor</c>, and an explicit interface implementation spells
    /// <c>System.IConvertible.ToBoolean</c> — so this contains it rather than
    /// sanitizing it into one, which would mangle both.
    /// </summary>
    private static string SanitizeMemberDisplayName(string name)
        => CSharpIdentifierCore.ContainComposedName(name);

    private static string SanitizeIdentifier(string name)
        => CSharpIdentifierCore.ContainIdentifier(name, CSharpKeywords.RequiresDeclarationEscape);

    private static string FormatDecimalLiteral(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture) + "m";

    private static string FormatInt64Literal(long value)
    {
        long minValue = long.MaxValue;
        minValue = -minValue - 1;
        return value == minValue
            ? "long.MinValue"
            : value.ToString(CultureInfo.InvariantCulture) + "L";
    }

    private static string StringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when CSharpIdentifierCore.RequiresLiteralEscape(c) => $"\\u{(int)c:X4}",
                _ => c.ToString()
            });
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string? TryFormatEnumDefaultValue(
        MetadataReader reader,
        object value,
        string typeName,
        Action<int>? beforeDecodeWork = null)
    {
        if (!TryConvertEnumConstant(value, out var defaultValue))
            return null;

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            try
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                if (!IsEnum(reader, typeDef, beforeDecodeWork))
                    continue;

                if (MetadataTypeDefinitionNameReader.Read(
                        reader,
                        typeHandle,
                        beforeDecodeWork)
                    is not MetadataTypeDefinitionNameReadResult.Read resolvedEnumType)
                {
                    continue;
                }

                string enumTypeName = resolvedEnumType.Name.ToMetadataFullName();
                if (!string.Equals(typeName, enumTypeName, StringComparison.Ordinal))
                    continue;

                foreach (var fieldHandle in typeDef.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.Literal) == 0)
                        continue;
                    var constantHandle = field.GetDefaultValue();
                    if (constantHandle.IsNil)
                        continue;
                    var constant = reader.GetConstant(constantHandle);
                    if (TryReadEnumConstant(reader, constant, out var memberValue)
                        && memberValue == defaultValue)
                    {
                        return $"{typeName}.{DecodeString(reader, field.Name, beforeDecodeWork)}";
                    }
                }

                return $"({typeName}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                continue;
            }
        }

        return null;
    }

    private static bool IsLikelyEnumDefaultType(string typeName)
        => typeName is not ("bool" or "char" or "sbyte" or "byte" or "short" or "ushort"
            or "int" or "uint" or "long" or "ulong" or "float" or "double" or "decimal"
            or "System.Boolean" or "System.Char" or "System.SByte" or "System.Byte"
            or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
            or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double"
            or "System.Decimal" or "System.DateTime");

    // Base types, interfaces, and events resolve to a display string via the
    // string-based TypeResolver, which has no DynamicAttribute context. Only a
    // generic instantiation (a TypeSpecification) can carry `dynamic`, so when
    // one does, re-decode it through the TypeNode tree and apply the flags. Every
    // other case (non-TypeSpec, or no DynamicAttribute) returns the string result
    // unchanged, so this never alters non-dynamic output.
    private static string ApplyDynamicView(
        MetadataReader reader,
        EntityHandle typeHandle,
        CustomAttributeHandleCollection attributes,
        GenericContext context,
        string fallback,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        if (typeHandle.Kind != HandleKind.TypeSpecification)
            return fallback;
        if (DynamicReader.GetDynamicFlags(
                reader,
                attributes,
                beforeDecodeWork) is not { } flags)
            return fallback;
        var node = GuardedProviderDecode.TypeSpec(
            reader,
            (TypeSpecificationHandle)typeHandle,
            beforeRetainText is null
                ? TypeNodeProvider.Instance
                : new TypeNodeProvider(beforeRetainText, beforeDecodeWork),
            context,
            (TypeNode)new DegradedTypeNode());
        // A rejected/degraded TypeSpec renders as a bare "object"/"dynamic", which would
        // obliterate the fully resolved string fallback. Keep failure visible: trust the
        // string resolver rather than silently collapsing the type.
        if (node.IsDegraded)
            return fallback;
        int position = 0;
        node.ApplyDynamic(flags, ref position);
        return node.Render();
    }

    private static string ResolveRequiredTypeName(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context = null,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null,
        Action<TypeNode>? captureTypeNode = null)
    {
        if (beforeDecodeWork is not null || captureTypeNode is not null)
        {
            var provider =
                new TypeNodeProvider(beforeMaterialize: beforeDecodeWork);
            TypeNode? typeNode = handle.Kind switch
            {
                HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)handle,
                    rawTypeKind: 0),
                HandleKind.TypeReference => provider.GetTypeFromReference(
                    reader,
                    (TypeReferenceHandle)handle,
                    rawTypeKind: 0),
                HandleKind.TypeSpecification => GuardedProviderDecode.TypeSpec(
                    reader,
                    (TypeSpecificationHandle)handle,
                    provider,
                    context,
                    (TypeNode)new DegradedTypeNode()),
                _ => null,
            };
            if (typeNode is not null)
                captureTypeNode?.Invoke(typeNode);
        }

        string resolved = TypeResolver.ResolveTypeName(reader, handle, context) switch
        {
            MetadataTypeNameResult.Resolved success => success.Value,
            MetadataTypeNameResult.Rejected rejected =>
                throw new MetadataRowRejectedException(
                    "type name",
                    rejected.Failure),
            MetadataTypeNameResult.Absent =>
                throw new MetadataRowRejectedException(
                    "type name",
                    MetadataTypeNameFailure.ForMechanism(
                        MetadataTypeNameFailureMechanism.Metadata,
                        handle,
                        "The metadata type name is absent.")),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };
        beforeRetainText?.Invoke(resolved);
        return resolved;
    }

    static ApiTypeReferenceIdentity? DecodeTypeDefinitionReference(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext context,
        Action<string>? beforeRetainText,
        Action<int>? beforeDecodeWork)
    {
        var provider = new TypeNodeProvider(
            beforeRetainText,
            beforeDecodeWork);
        TypeNode node = handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => provider.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => GuardedProviderDecode.TypeSpec(
                reader,
                (TypeSpecificationHandle)handle,
                provider,
                context,
                (TypeNode)new DegradedTypeNode()),
            _ => new DegradedTypeNode(),
        };
        return node.IsDegraded
            ? null
            : node.DefinitionReference();
    }

    private static void AddInspectionFailure(
        ApiSurface surface,
        ExtractionBudget? budget,
        string operation,
        EntityHandle subject,
        MetadataTypeNameFailure failure,
        AssemblyReferenceIdentity? subjectAssembly = null,
        TypeDefinitionHandle owningType = default,
        TypeDefinitionHandle owningTypeParent = default,
        MetadataTypeDefinitionName? owningTypeDefinition = null,
        TypeAttributes? owningTypeAttributes = null)
    {
        var retained = new ApiSurfaceInspectionFailure(
            operation,
            failure.SubjectToken ?? MetadataTokens.GetToken(subject),
            failure.Mechanism,
            failure.Kind,
            failure.Detail,
            subjectAssembly)
        {
            OwningTypeToken = owningType.IsNil
                ? null
                : MetadataTokens.GetToken(owningType),
            OwningTypeParentToken = owningTypeParent.IsNil
                ? null
                : MetadataTokens.GetToken(owningTypeParent),
            OwningTypeDefinition = owningTypeDefinition,
            OwningTypeAttributes = owningTypeAttributes,
        };
        budget?.RetainInspectionFailure(retained);
        surface.InspectionFailures.Add(retained);
    }

    private static bool IsEnum(
        MetadataReader reader,
        TypeDefinition typeDef,
        Action<int>? beforeDecodeWork = null)
        => !typeDef.BaseType.IsNil
            && TypeResolver.GetTypeName(
                reader,
                typeDef.BaseType,
                context: null,
                beforeMaterialize: beforeDecodeWork) == "System.Enum";

    private sealed class MetadataRowRejectedException
        : InvalidOperationException
    {
        public MetadataRowRejectedException(
            string operation,
            MetadataTypeNameFailure failure)
            : base(
                $"Metadata row rejected during {operation} "
                + $"({failure.Mechanism}/{failure.Kind}): {failure.Detail}")
        {
            Operation = operation;
            Failure = failure;
        }

        public string Operation { get; }
        public MetadataTypeNameFailure Failure { get; }
    }

    private static bool TryReadEnumConstant(MetadataReader reader, Constant constant, out decimal value)
    {
        var blob = reader.GetBlobReader(constant.Value);
        switch (constant.TypeCode)
        {
            case ConstantTypeCode.SByte:
                return TryConvertEnumConstant(blob.ReadSByte(), out value);
            case ConstantTypeCode.Byte:
                return TryConvertEnumConstant(blob.ReadByte(), out value);
            case ConstantTypeCode.Int16:
                return TryConvertEnumConstant(blob.ReadInt16(), out value);
            case ConstantTypeCode.UInt16:
                return TryConvertEnumConstant(blob.ReadUInt16(), out value);
            case ConstantTypeCode.Int32:
                return TryConvertEnumConstant(blob.ReadInt32(), out value);
            case ConstantTypeCode.UInt32:
                return TryConvertEnumConstant(blob.ReadUInt32(), out value);
            case ConstantTypeCode.Int64:
                return TryConvertEnumConstant(blob.ReadInt64(), out value);
            case ConstantTypeCode.UInt64:
                return TryConvertEnumConstant(blob.ReadUInt64(), out value);
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryConvertEnumConstant(object value, out decimal converted)
    {
        switch (value)
        {
            case sbyte v:
                converted = v;
                return true;
            case byte v:
                converted = v;
                return true;
            case short v:
                converted = v;
                return true;
            case ushort v:
                converted = v;
                return true;
            case int v:
                converted = v;
                return true;
            case uint v:
                converted = v;
                return true;
            case long v:
                converted = v;
                return true;
            case ulong v:
                converted = v;
                return true;
            default:
                converted = 0;
                return false;
        }
    }

    /// <summary>
    /// Maps MethodAttributes access level to a C# keyword.
    /// Returns null for public or unrepresentable access.
    /// </summary>
    private static string? GetAccessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };

    private static bool IsRepresentableMethodAccessibility(
        MethodAttributes access) =>
        access is
            MethodAttributes.Private
            or MethodAttributes.FamANDAssem
            or MethodAttributes.Assembly
            or MethodAttributes.Family
            or MethodAttributes.FamORAssem
            or MethodAttributes.Public;

    /// <summary>
    /// Maps FieldAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetFieldAccessibility(FieldAttributes access) => access switch
    {
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };
}
