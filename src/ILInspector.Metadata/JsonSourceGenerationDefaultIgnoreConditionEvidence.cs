namespace ILInspector.Metadata;

/// <summary>
/// Authentic metadata evidence for
/// <c>JsonSourceGenerationOptionsAttribute.DefaultIgnoreCondition</c>.
/// </summary>
/// <remarks>
/// <see cref="AttributeCount"/> distinguishes an absent options attribute from
/// one whose omitted or explicit option has the framework default
/// <see cref="JsonWireIgnoreCondition.Never"/>. <see cref="Value"/> is retained
/// only for one fully supported authentic row. A duplicate row remains visible
/// through the count, while malformed, unknown, or unsupported row content is
/// retained by <see cref="HasUnsupportedRow"/>.
/// </remarks>
public readonly record struct
    JsonSourceGenerationDefaultIgnoreConditionEvidence(
        int AttributeCount,
        JsonWireIgnoreCondition? Value,
        bool HasUnsupportedRow);
