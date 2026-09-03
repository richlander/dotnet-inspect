namespace ILInspector.Metadata.Tests;

/// <summary>
/// How a custom-attribute parameter type is laid out in metadata. The guard
/// classifies these down separate branches, so a corpus of names alone pins
/// only the shape it happens to build.
/// </summary>
/// <remarks>
/// <para>
/// <c>CustomAttributeValueGuard.IsSrmSystemType</c> renders a
/// <c>TypeDefinition</c> through <c>GetTypeNameFromDefinition</c> and every
/// other handle through <c>GetTypeName</c>. Both branches end at the same
/// comparison, and pinning the comparison over one of them leaves the other
/// free to diverge.
/// </para>
/// <para>
/// The namespace layout matters independently of the row kind. A row may
/// carry <c>System</c> in its namespace column and <c>Type</c> in its name
/// column, or the whole of <c>System.Type</c> in its name column with the
/// namespace column nil. Both render identically, and nothing obliges an
/// author to choose the first: the dotted form is what an attacker writes.
/// Round 7 found that gap -- restricting the <c>TypeDefinition</c> branch to
/// rows with a populated namespace column left all 2,459 metadata tests green
/// while making the guard read four enum bytes where SRM read a
/// <c>SerString</c>, which is precisely the cursor drift this gate exists to
/// prevent.
/// </para>
/// </remarks>
public enum ClassificationRowShape
{
    /// <summary>
    /// A <c>TypeRef</c> row split across its namespace and name columns.
    /// </summary>
    ReferenceCanonical,

    /// <summary>
    /// A <c>TypeRef</c> row carrying the whole dotted name in its name column
    /// and a nil namespace column.
    /// </summary>
    ReferenceDotted,

    /// <summary>
    /// A <c>TypeDef</c> row split across its namespace and name columns.
    /// </summary>
    DefinitionCanonical,

    /// <summary>
    /// A <c>TypeDef</c> row carrying the whole dotted name in its name column
    /// and a nil namespace column.
    /// </summary>
    DefinitionDotted,
}
