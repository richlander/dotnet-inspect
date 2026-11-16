using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The single walk that pairs a Type selection with its extensions returns the
/// union of the canonical per-receiver extension relation populations.
/// </summary>
public sealed class ExtensionOccurrencesForReceiversTests
{
    // CoreLib, whose extensions include primitive receivers, is sampled:
    // every tenth Type and the primitive and span receivers. The other
    // images check every Type.
    static readonly string[] SampledCoreLibReceivers =
        ["System.String", "System.Int32", "System.Char", "System.Span`1", "System.ReadOnlySpan`1"];

    [Theory]
    [InlineData("System.Private.CoreLib")]
    [InlineData("System.Linq")]
    [InlineData("System.Collections.Immutable")]
    public void SingleWalk_EqualsUnionOfPerReceiverPopulations(string assembly)
    {
        string path = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            assembly + ".dll");
        bool sampled = assembly == "System.Private.CoreLib";
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();
        AssemblyReferenceIdentity identity = AssemblyReferenceIdentity.FromAssemblyDefinition(reader);

        var everyReceiver = new HashSet<MetadataTypeDefinitionName>();
        var union = new List<string>();
        int receiversWithExtensions = 0;
        int ordinal = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionName name = Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
                MetadataTypeDefinitionName.Read(reader, handle)).Name;
            if (sampled
                && ordinal++ % 10 != 0
                && !SampledCoreLibReceivers.Contains(name.ToMetadataFullName()))
            {
                continue;
            }
            everyReceiver.Add(name);
            MetadataExtensionRelationPopulationOutcome outcome =
                MetadataRelationInspection.ExecuteExtensionPopulation(
                    image,
                    reader,
                    new MetadataExtensionRelationPopulationRequest(
                        new MetadataExtensionReceiverSelection(
                            identity,
                            name,
                            MetadataTypeDefinitionAddress.FromHandle(reader, handle)),
                        new MetadataOperationPolicy(int.MaxValue),
                        rows: new MetadataExtensionRelationPopulationRowsRequest(0, int.MaxValue),
                        includeNonPublic: true),
                    TestContext.Current.CancellationToken);
            MetadataExtensionRelationPopulationResult result =
                Assert.IsType<MetadataExtensionRelationPopulationOutcome.Available>(outcome).Result;
            Assert.Equal(MetadataRelationFamilyDisposition.Complete, result.Disposition);
            MetadataExtensionRelationPopulationRowsOutcome.Read rows =
                Assert.IsType<MetadataExtensionRelationPopulationRowsOutcome.Read>(result.Rows);
            Assert.Null(rows.NextOrdinal);

            string[] own = [.. rows.Items.SelectMany(row => row.Occurrences).Select(Key)];
            if (own.Length > 0)
                receiversWithExtensions++;
            union.AddRange(own);

            // Each receiver alone.
            Assert.Equal(
                own.Order(StringComparer.Ordinal),
                Walk(reader, [name]).Order(StringComparer.Ordinal));
        }

        Assert.True(receiversWithExtensions > 0);
        Assert.Equal(
            union.Order(StringComparer.Ordinal),
            Walk(reader, everyReceiver).Order(StringComparer.Ordinal));
    }

    static IEnumerable<string> Walk(MetadataReader reader, HashSet<MetadataTypeDefinitionName> receivers)
        => Assert.IsAssignableFrom<IReadOnlyList<MetadataExtensionRelationEvidence>>(
                MetadataRelationInspection.ExtensionOccurrencesForReceivers(
                    reader,
                    receivers,
                    TestContext.Current.CancellationToken))
            .Select(Key);

    static string Key(MetadataExtensionRelationEvidence evidence)
        => $"{evidence.DeclarationMetadataToken:X8} {evidence.DeclaringTypeName} {evidence.Member} {evidence.Receiver}";
}
