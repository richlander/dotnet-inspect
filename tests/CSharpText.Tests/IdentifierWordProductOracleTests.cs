namespace CSharpText.Tests;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

public sealed class IdentifierWordProductOracleTests
{
    [Fact]
    public void PinnedReferencePackAndReviewSet_ReproduceCheckedInSnapshot()
    {
        string repository = RepositoryRoot();
        IdentifierWordOracleEntry[] reviewed = File
            .ReadLines(Path.Combine(
                repository,
                "eng",
                "csharp-identifier-word-oracle-reviewed.txt"))
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(ParseEntry)
            .ToArray();

        Assert.Equal(IdentifierWordProductOracle.Entries, reviewed);
        Assert.Equal(
            IdentifierWordProductOracle.Digest,
            IdentifierWordOracle.ComputeDigest(reviewed));

        var witnessed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in EnumeratePublicNames(ReferencePackDirectory()))
        {
            var result = Assert.IsType<IdentifierWordBreakOutcome.Succeeded>(
                IdentifierWordBreaker.Break(
                    name,
                    IdentifierWordProductOracle.Instance)).Result;
            foreach (IdentifierWordSpan span in result.Spans)
            {
                if (span.Evidence.OracleEntry is { } entry)
                    witnessed.Add(entry);
            }
        }

        Assert.DoesNotContain(
            reviewed,
            entry => !witnessed.Contains(entry.Text));

        string expectedSnapshot = Snapshot(reviewed);
        string checkedInSnapshot = File.ReadAllText(Path.Combine(
            repository,
            "eng",
            "csharp-identifier-word-oracle.snapshot"));
        Assert.Equal(expectedSnapshot, checkedInSnapshot);
    }

    static string Snapshot(IEnumerable<IdentifierWordOracleEntry> entries)
    {
        var snapshot = new StringBuilder();
        snapshot.Append("grammar\t").Append(IdentifierWordBreaker.GrammarVersion).Append('\n');
        snapshot.Append("vocabulary\t").Append(IdentifierWordProductOracle.VocabularyVersion).Append('\n');
        snapshot.Append("source\t").Append(IdentifierWordProductOracle.SourceCoordinate).Append('\n');
        snapshot.Append("review\t").Append(IdentifierWordProductOracle.ReviewSetVersion).Append('\n');
        snapshot.Append("digest\t").Append(IdentifierWordProductOracle.Digest).Append('\n');
        foreach (IdentifierWordOracleEntry entry in entries)
        {
            snapshot
                .Append(entry.Kind == IdentifierWordOracleEntryKind.Atom ? "atom" : "compound")
                .Append('\t')
                .Append(entry.Text)
                .Append('\n');
        }
        return snapshot.ToString();
    }

    static IdentifierWordOracleEntry ParseEntry(string line)
    {
        string[] parts = line.Split('\t');
        Assert.Equal(2, parts.Length);
        return new(
            parts[1],
            parts[0] switch
            {
                "atom" => IdentifierWordOracleEntryKind.Atom,
                "compound" => IdentifierWordOracleEntryKind.Compound,
                _ => throw new InvalidDataException(
                    $"Invalid oracle entry kind: {parts[0]}"),
            });
    }

    static string ReferencePackDirectory()
    {
        string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        string path = Path.Combine(
            packages,
            "microsoft.netcore.app.ref",
            "10.0.10",
            "ref",
            "net10.0");
        Assert.True(
            Directory.Exists(path),
            $"The PackageDownload reference pack is absent: {path}");
        return path;
    }

    static IEnumerable<string> EnumeratePublicNames(string packDirectory)
    {
        foreach (string path in Directory.EnumerateFiles(packDirectory, "*.dll"))
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                continue;

            MetadataReader reader = pe.GetMetadataReader();
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                if (!IsPublicType(reader, typeHandle))
                    continue;

                yield return reader.GetString(type.Name);
                foreach (string segment in reader
                    .GetString(type.Namespace)
                    .Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return segment;
                }
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    if ((method.Attributes & MethodAttributes.MemberAccessMask)
                        == MethodAttributes.Public)
                    {
                        yield return reader.GetString(method.Name);
                    }
                }

                static bool IsPublicType(MetadataReader reader, TypeDefinitionHandle handle)
                {
                    TypeDefinition type = reader.GetTypeDefinition(handle);
                    TypeAttributes visibility =
                        type.Attributes & TypeAttributes.VisibilityMask;
                    TypeDefinitionHandle declaringType = type.GetDeclaringType();
                    return declaringType.IsNil
                        ? visibility == TypeAttributes.Public
                        : visibility == TypeAttributes.NestedPublic
                            && IsPublicType(reader, declaringType);
                }
                foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                {
                    FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.FieldAccessMask)
                        == FieldAttributes.Public)
                    {
                        yield return reader.GetString(field.Name);
                    }
                }
            }
        }
    }

    static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null
            && !File.Exists(Path.Combine(root.FullName, "dotnet-inspect.slnx")))
        {
            root = root.Parent;
        }
        return root?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
