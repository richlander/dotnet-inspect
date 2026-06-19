using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Builds one <see cref="MetadataContext"/> shared across a whole corpus sweep so
/// a dependency such as CoreLib is opened and indexed once for the entire run
/// rather than once per assembly. The locator searches every directory the
/// corpus assemblies live in — a superset of the per-assembly sibling policy, so
/// resolution is at least as complete and identical for the common single
/// -directory corpus.
/// </summary>
static class CorpusMetadata
{
    public static MetadataContext Create(IEnumerable<string> assemblies)
    {
        var directories = assemblies
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AssemblyLocator locator = (name, trust) =>
        {
            foreach (var directory in directories)
            {
                string candidate = Path.Combine(directory!, name + ".dll");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        };

        return new MetadataContext(locator);
    }
}
