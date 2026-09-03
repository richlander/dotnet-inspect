using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// Bounded refusal categories. A category names why no valid plan exists; it
/// never carries repository path bytes.
/// </summary>
internal enum PlanRefusalCategory
{
    Usage,
    Repository,
    EvidenceDirectory,
    ObjectIdFormat,
    EndpointUnresolved,
    CandidateMismatch,
    EvidenceUnavailable,
    EvidenceFraming,
    EvidenceStatus,
    EvidencePath,
    EvidenceDuplicate,
    ScopeOverflow,
    PlanOverflow,
    PlanSerialization,
    PlanPublication,
}

/// <summary>
/// The only exception type the planner CLI boundary converts into a refusal.
/// </summary>
internal sealed class PlanRefusalException : Exception
{
    internal PlanRefusalException(PlanRefusalCategory category, string detail)
        : base($"{CategoryName(category)}: {Sanitize(detail)}") =>
        Category = category;

    internal PlanRefusalCategory Category { get; }

    /// <summary>
    /// Gets the lower camel case wire name of a refusal category.
    /// </summary>
    /// <param name="category">The refusal category.</param>
    /// <returns>The ASCII category name.</returns>
    internal static string CategoryName(PlanRefusalCategory category) =>
        category switch
        {
            PlanRefusalCategory.Usage => "usage",
            PlanRefusalCategory.Repository => "repository",
            PlanRefusalCategory.EvidenceDirectory => "evidenceDirectory",
            PlanRefusalCategory.ObjectIdFormat => "objectIdFormat",
            PlanRefusalCategory.EndpointUnresolved => "endpointUnresolved",
            PlanRefusalCategory.CandidateMismatch => "candidateMismatch",
            PlanRefusalCategory.EvidenceUnavailable => "evidenceUnavailable",
            PlanRefusalCategory.EvidenceFraming => "evidenceFraming",
            PlanRefusalCategory.EvidenceStatus => "evidenceStatus",
            PlanRefusalCategory.EvidencePath => "evidencePath",
            PlanRefusalCategory.EvidenceDuplicate => "evidenceDuplicate",
            PlanRefusalCategory.ScopeOverflow => "scopeOverflow",
            PlanRefusalCategory.PlanOverflow => "planOverflow",
            PlanRefusalCategory.PlanSerialization => "planSerialization",
            PlanRefusalCategory.PlanPublication => "planPublication",
            _ => "unknown",
        };

    /// <summary>
    /// Reduces a detail string to bounded printable ASCII so a diagnostic
    /// cannot leak arbitrary path bytes or unbounded content.
    /// </summary>
    /// <param name="detail">The proposed detail text.</param>
    /// <returns>Bounded printable ASCII.</returns>
    private static string Sanitize(string detail)
    {
        const int Maximum = 256;
        StringBuilder builder = new(Math.Min(detail.Length, Maximum));
        foreach (char character in detail)
        {
            if (builder.Length == Maximum)
            {
                break;
            }

            builder.Append(character is >= ' ' and <= '~' ? character : '?');
        }

        return builder.Length == 0 ? "refused" : builder.ToString();
    }
}
