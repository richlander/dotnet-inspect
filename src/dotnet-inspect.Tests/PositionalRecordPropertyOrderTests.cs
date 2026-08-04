using System.Reflection;
using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins that a positional record's reflected property order matches its
/// constructor's parameter order.
/// </summary>
/// <remarks>
/// This is a bug class, not a bug. Containment work redeclares a positional
/// property in the record body so the incoming value can be contained. The
/// compiler emits explicitly declared properties after the ones it generates
/// for the remaining positional parameters, so redeclaring <em>some</em> of
/// them silently rewrites the order of every reflected consumer: JSON and JSONL
/// keys, TSV and Markdown columns, and the generated <c>ToString</c>.
///
/// It shipped twice. <c>ProjectSkillRow</c> and <c>ProjectPackageDocument</c>
/// moved <c>size</c> ahead of <c>package</c> in <c>--jsonl</c>, and
/// <c>ApiChange</c> moved <c>Category</c> and <c>Subject</c> ahead of
/// <c>Message</c>. Both were found by review reading the source, and neither
/// was caught by a byte-neutrality sweep, because the reordering is invisible
/// unless the sweep happens to run a structured output mode over that exact
/// type.
///
/// So this does not enumerate the records that matter, which is the check that
/// already failed twice. It walks every positional record in the product
/// assemblies and requires the order to be the constructor's, which is the
/// order the author declared and the order a reader expects.
/// </remarks>
public class PositionalRecordPropertyOrderTests
{
    public static TheoryData<string> ProductAssemblies() => new()
    {
        "dotnet-inspect",
        "ILInspector.Metadata",
        "ILInspector.CSharp",
        "ILInspector.Analysis",
        "DotnetInspector.Services",
        "DotnetInspector.Core",
    };

    /// <summary>
    /// Positional records whose property order already diverged from their
    /// constructor before this gate existed.
    /// </summary>
    /// <remarks>
    /// These redeclare a property to enforce value equality on an
    /// <c>ImmutableArray</c>, not to contain untrusted text, and they are
    /// internal analysis identities rather than serialized rows, so the order
    /// is not observable in output. They are pinned rather than fixed because
    /// correcting them is a behavior change unrelated to issue #3319.
    ///
    /// The assertion below is set equality, not containment, so this list
    /// cannot rot: fixing one of these without removing its entry fails just as
    /// loudly as introducing a new offender.
    /// </remarks>
    private static readonly string[] KnownDivergentRecords =
    [
        "ILInspector.Analysis.MemberRef",
        "ILInspector.Analysis.MethodIdentity",
        "ILInspector.Metadata.ClassifiedMethodObservation",
        "ILInspector.Metadata.ExtensionMemberObservation",
    ];

    [Theory]
    [MemberData(nameof(ProductAssemblies))]
    public void PositionalRecords_DeclarePropertiesInConstructorOrder(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var offenders = new List<string>();
        var offenderNames = new List<string>();
        int checkedRecords = 0;

        foreach (var type in assembly.GetTypes())
        {
            if (!TryGetPrimaryConstructor(type, out var primary))
                continue;

            checkedRecords++;

            var parameterNames = primary.GetParameters().Select(p => p.Name!).ToArray();
            var parameterSet = parameterNames.ToHashSet(StringComparer.Ordinal);

            // Declared order as emitted into metadata, restricted to the
            // properties that back constructor parameters.
            var declaredOrder = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => p.Name)
                .Where(parameterSet.Contains)
                .ToArray();

            if (!declaredOrder.SequenceEqual(parameterNames, StringComparer.Ordinal))
            {
                offenderNames.Add(type.FullName!);
                offenders.Add(
                    $"{type.FullName}\n" +
                    $"      constructor: {string.Join(", ", parameterNames)}\n" +
                    $"      reflected:   {string.Join(", ", declaredOrder)}");
            }
        }

        // Non-vacuity: an assembly that suddenly contains no positional records
        // means this walked the wrong thing, and every assertion above is empty.
        Assert.True(
            checkedRecords > 0,
            $"no positional records found in {assemblyName}, so this gate proves nothing about it");

        var expected = KnownDivergentRecords
            .Where(name => name.StartsWith(assemblyName + ".", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var actual = offenderNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "the set of positional records whose property order is not their constructor's order changed.\n"
            + "Redeclare every positional property in the body, in constructor order, or none of them.\n"
            + $"expected: {string.Join(", ", expected)}\n"
            + $"actual:\n  - {string.Join("\n  - ", offenders)}");
    }

    /// <summary>
    /// Returns the record's primary constructor, or false when the type is not
    /// a positional record.
    /// </summary>
    /// <remarks>
    /// A record is identified by its compiler-generated <c>&lt;Clone&gt;$</c>
    /// member. The primary constructor is the one whose every parameter is
    /// backed by a declared property of the same name and type, which excludes
    /// the generated copy constructor and any hand-written overload.
    /// </remarks>
    private static bool TryGetPrimaryConstructor(Type type, out ConstructorInfo primary)
    {
        primary = null!;

        if (type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is null)
            return false;

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(p => p.Name, p => p.PropertyType, StringComparer.Ordinal);

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 0)
                continue;

            // The generated copy constructor takes the record type itself.
            if (parameters.Length == 1 && parameters[0].ParameterType == type)
                continue;

            if (parameters.All(p =>
                    p.Name is not null
                    && properties.TryGetValue(p.Name, out var propertyType)
                    && propertyType == p.ParameterType))
            {
                primary = ctor;
                return true;
            }
        }

        return false;
    }
}
