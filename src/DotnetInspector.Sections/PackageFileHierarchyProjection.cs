using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>A directory context or a file row with its exact package-relative path.</summary>
public sealed record PackageFileHierarchyNode(
    InertString Path,
    InertString Name,
    PackageFileInventoryEntry? File);

/// <summary>Pushes selected file rows and their directory context without retaining a second tree.</summary>
public static class PackageFileHierarchyProjection
{
    public static void Write(
        IReadOnlyList<PackageFileInventoryEntry> files,
        IInspectionHierarchySink<PackageFileHierarchyNode> sink,
        bool collapseDirectoryChains = true)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(sink);
        var ordered = files.OrderBy(file => file.Path.ToString(), StringComparer.Ordinal).ToArray();
        WriteRange(ordered, 0, ordered.Length, "", sink, collapseDirectoryChains);
    }

    private static void WriteRange(
        PackageFileInventoryEntry[] files, int start, int end, string parent,
        IInspectionHierarchySink<PackageFileHierarchyNode> sink, bool collapse)
    {
        int position = start;
        while (position < end)
        {
            string path = files[position].Path.ToString();
            int slash = path.IndexOf('/', parent.Length);
            if (slash < 0)
            {
                sink.WriteNode(new(files[position].Path,
                    new InertString(TextPolicy.Field, path[parent.Length..]),
                    files[position]), position + 1 == end);
                position++;
                continue;
            }

            string prefix = path[..(slash + 1)];
            int next = position + 1;
            while (next < end && files[next].Path.ToString().StartsWith(prefix, StringComparison.Ordinal))
                next++;

            if (collapse)
            {
                while (true)
                {
                    int childSlash = path.IndexOf('/', prefix.Length);
                    if (childSlash < 0)
                        break;
                    string candidate = path[..(childSlash + 1)];
                    if (!files[next - 1].Path.ToString().StartsWith(candidate, StringComparison.Ordinal))
                        break;
                    prefix = candidate;
                }
            }

            int childStart = position;
            string directory = prefix[..^1];
            sink.WriteNode(new(
                    new InertString(TextPolicy.Field, directory),
                    new InertString(TextPolicy.Field, directory[parent.Length..]),
                    null),
                next == end,
                children => WriteRange(files, childStart, next, prefix, children, collapse));
            position = next;
        }
    }
}
