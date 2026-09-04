using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;
using System.Reflection.PortableExecutable;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// A node in a type dependency tree (base classes and interfaces).
/// </summary>
public record TypeDependencyNode(string TypeName, List<TypeDependencyNode> Children);

/// <summary>
/// Identifies why a candidate assembly was rejected during a dependency scan.
/// </summary>
public enum TypeDependencyRejectionKind
{
    UnsupportedMetadataFormat,
    MalformedMetadataRoot,
    InvalidImage,
}

/// <summary>
/// Records a candidate assembly that a dependency scan rejected. A rejection
/// scopes to its own participant and never aborts the surrounding scan.
/// </summary>
public sealed record TypeDependencyRejection(
    string AssemblyPath,
    TypeDependencyRejectionKind Kind)
{
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

/// <summary>
/// Every candidate in a dependency scan was rejected, so the scan has no
/// surviving participant to scope its rejections against. Each rejection is
/// an independent outcome: throwing one would discard the rest, so they are
/// carried together. <see cref="Rejections"/> holds the typed record for each
/// rejected candidate, keeping the path-to-mechanism correspondence available
/// as data rather than only in the rendered message.
/// </summary>
public sealed class AllCandidatesRejectedException : AggregateException
{
    private readonly string renderedMessage;

    internal AllCandidatesRejectedException(
        string message,
        ImmutableArray<TypeDependencyRejection> rejections,
        IEnumerable<Exception> mechanisms)
        : base(message, mechanisms)
    {
        renderedMessage = message;
        Rejections = rejections;
    }

    /// <summary>
    /// The rendered path-to-mechanism pairing, without the inner-message list
    /// <see cref="AggregateException"/> appends to its own message. That
    /// default would print every mechanism a second time at a command
    /// boundary, unpaired with the path it belongs to.
    /// </summary>
    public override string Message => renderedMessage;

    /// <summary>
    /// The rejected candidates, in scan order. Each entry corresponds to the
    /// inner exception at the same index.
    /// </summary>
    public ImmutableArray<TypeDependencyRejection> Rejections { get; }
}

/// <summary>
/// Result of building a type dependency tree.
/// </summary>
public record TypeDependencyResult(string? MatchedType, List<TypeDependencyNode> Tree)
{
    public bool Found => MatchedType != null;

    /// <summary>
    /// Candidate assemblies the scan rejected on metadata-format grounds.
    /// </summary>
    public IReadOnlyList<TypeDependencyRejection> Rejections { get; init; } = [];
}

/// <summary>
/// Walks the inheritance and interface implementation graph upward from a type.
/// This is the inverse of <see cref="TypeHierarchyScanner.FindImplementers"/> —
/// it shows what a type depends on, not what depends on it.
/// </summary>
public static class TypeDependencyScanner
{
    /// <summary>
    /// Builds the dependency tree for a type found in the given assemblies.
    /// Returns the direct base types and interfaces as root-level nodes,
    /// each with their own recursive dependencies.
    /// </summary>
    public static TypeDependencyResult BuildDependencyTree(
        string targetType,
        IReadOnlyList<string> assemblyPaths)
    {
        var typeIndex = new Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)>(
            StringComparer.OrdinalIgnoreCase);
        var peReaders = new List<PEReader>();
        var rejections = new List<TypeDependencyRejection>();
        var admittedAny = false;
        ExceptionDispatchInfo? firstInvalidImage = null;

        // The decoder's exception carries detail that a reconstructed one
        // would lose, so each invalid image keeps its own captured cause.
        var invalidImageCauses =
            new Dictionary<string, BadImageFormatException>(
                StringComparer.Ordinal);

        try
        {
            foreach (var path in assemblyPaths)
            {
                try
                {
                    var stream = File.OpenRead(path);
                    PEReader peReader;
                    try
                    {
                        peReader = new PEReader(stream);
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }
                    peReaders.Add(peReader);

                    try
                    {
                        if (!MetadataFormatAdmission.AdmitImage(peReader))
                            continue;

                        MetadataReader mdReader =
                            MetadataFormatAdmission.GetMetadataReader(peReader);

                        // Stage this participant's rows separately. A rejection
                        // must exclude the whole participant, so rows decoded
                        // before a later failure cannot be allowed to reach the
                        // shared index — they would shadow a healthy same-name
                        // definition under TryAdd and make the emitted tree
                        // wrong rather than merely incomplete.
                        var staged =
                            new Dictionary<string, (PEReader, MetadataReader, TypeDefinition)>(
                                StringComparer.OrdinalIgnoreCase);
                        foreach (var typeDefHandle in mdReader.TypeDefinitions)
                        {
                            var typeDef = mdReader.GetTypeDefinition(typeDefHandle);
                            if (!typeDef.IsPublic)
                                continue;

                            var name = mdReader.GetString(typeDef.Name);
                            if (TypeFilters.IsCompilerGenerated(name))
                                continue;

                            var ns = mdReader.GetString(typeDef.Namespace);
                            var fullName = TypeResolver.GetFullName(ns, name);

                            // Index by ECMA name for lookup
                            staged.TryAdd(fullName, (peReader, mdReader, typeDef));
                        }

                        foreach (var entry in staged)
                            typeIndex.TryAdd(entry.Key, entry.Value);

                        // Only a participant that decoded all the way through
                        // counts as surviving. A partially indexed one cannot
                        // scope another participant's rejection.
                        admittedAny = true;
                    }
                    // Admission passed but the metadata itself did not decode.
                    // That is an ordinary invalid-image outcome rather than an
                    // admission failure, and it still has to stay visible
                    // instead of silently dropping the participant.
                    // MalformedMetadataRootException derives from
                    // BadImageFormatException, so it is excluded here to reach
                    // its own handler and keep its exact root reason.
                    catch (Exception invalidImage) when (
                        invalidImage is not MalformedMetadataRootException
                        && invalidImage is BadImageFormatException
                            or OverflowException)
                    {
                        BadImageFormatException cause =
                            invalidImage as BadImageFormatException
                            ?? new BadImageFormatException(
                                "The selected image metadata is invalid.",
                                invalidImage);
                        firstInvalidImage ??=
                            ExceptionDispatchInfo.Capture(cause);
                        invalidImageCauses[path] = cause;
                        rejections.Add(
                            new TypeDependencyRejection(
                                path,
                                TypeDependencyRejectionKind.InvalidImage));
                    }
                }
                // A rejected candidate scopes to itself: record it exactly and
                // keep scanning the remaining assemblies.
                catch (UnsupportedMetadataFormatException)
                {
                    rejections.Add(
                        new TypeDependencyRejection(
                            path,
                            TypeDependencyRejectionKind
                                .UnsupportedMetadataFormat));
                }
                catch (MalformedMetadataRootException ex)
                {
                    rejections.Add(
                        new TypeDependencyRejection(
                            path,
                            TypeDependencyRejectionKind.MalformedMetadataRoot)
                        {
                            MetadataRootReason = ex.Reason,
                        });
                }
                // Skip assemblies that can't be read
                catch (Exception ex) when (
                    ex is not UnsupportedMetadataFormatException
                        and not MalformedMetadataRootException)
                {
                }
            }

            // Scoping only applies when the scan had a surviving participant.
            // If every candidate was rejected there is nothing to scope the
            // rejection against, so it stays the caller's exact outcome.
            if (!admittedAny && rejections.Count > 0)
            {
                // One rejection is the caller's exact outcome, so it keeps its
                // typed mechanism. Several are independent outcomes with no
                // single exact answer: throwing one would silently discard the
                // rest, which is the evidence loss this contract exists to
                // prevent, so every mechanism travels in an aggregate.
                if (rejections.Count > 1)
                {
                    // The typed records keep the path-to-mechanism
                    // correspondence as data; the message repeats it only so a
                    // rendered error is readable. Callers read Rejections.
                    Exception[] mechanisms =
                    [
                        .. rejections.Select(rejection =>
                            ToRejectionException(
                                rejection,
                                invalidImageCauses)),
                    ];
                    string rendered = string.Join(
                        "; ",
                        rejections.Zip(
                            mechanisms,
                            (rejection, mechanism) =>
                                $"'{rejection.AssemblyPath}': "
                                + mechanism.Message));
                    throw new AllCandidatesRejectedException(
                        "Every candidate assembly was rejected before the "
                            + $"dependency scan could run ({rendered})",
                        [.. rejections],
                        mechanisms);
                }

                TypeDependencyRejection soleRejection = rejections[0];

                // The captured invalid-image exception carries the decoder's
                // exact detail, which a reconstructed one would lose.
                if (soleRejection.Kind
                    == TypeDependencyRejectionKind.InvalidImage)
                {
                    firstInvalidImage?.Throw();
                }

                throw ToRejectionException(
                    soleRejection,
                    invalidImageCauses);
            }

            // Find the target type
            var normalizedTarget = FqnParser.NormalizeTypeName(targetType);
            var matchKey = typeIndex.Keys.FirstOrDefault(k => TypeMatcher.Matches(k, normalizedTarget));
            if (matchKey == null)
                return new TypeDependencyResult(null, []) { Rejections = rejections };

            var match = typeIndex[matchKey];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tree = BuildNode(match.MdReader, match.TypeDef, typeIndex, seen);
            return new TypeDependencyResult(TypeResolver.FormatDisplayName(matchKey), tree)
            {
                Rejections = rejections,
            };
        }
        finally
        {
            foreach (var pr in peReaders)
                pr.Dispose();
        }
    }

    /// <summary>
    /// Builds the child nodes for a type. Computes the "minimal" direct
    /// dependencies by removing interfaces that are transitively inherited
    /// through other direct interfaces. De-duplicates across the tree.
    /// </summary>
    private static List<TypeDependencyNode> BuildNode(
        MetadataReader reader,
        TypeDefinition typeDef,
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        HashSet<string> seen)
    {
        var context = GenericContext.ForType(reader, typeDef);

        // Gather all declared dependencies (base type + interfaces)
        var allDeps = new List<string>();

        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType, context);
            if (baseTypeName != null && !IsSystemRoot(baseTypeName))
                allDeps.Add(baseTypeName);
        }

        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(reader, iface.Interface, context);
            if (ifaceName != null)
                allDeps.Add(ifaceName);
        }

        if (allDeps.Count == 0)
            return [];

        // Compute transitive closure for each dep to find which are redundant
        var transitivelyReachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in allDeps)
        {
            CollectTransitive(dep, typeIndex, transitivelyReachable, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        // A dep is "direct" if it's not transitively reachable through another dep
        var directDeps = allDeps
            .Where(d => !transitivelyReachable.Contains(FqnParser.NormalizeTypeName(d)))
            .ToList();

        // Build tree nodes for direct deps only
        var results = new List<TypeDependencyNode>();
        foreach (var dep in directDeps)
        {
            var normalized = FqnParser.NormalizeTypeName(dep);
            if (!seen.Add(normalized))
            {
                // Already shown at a shallower level — include as leaf
                results.Add(new TypeDependencyNode(dep, []));
                continue;
            }

            var children = ResolveChildren(dep, typeIndex, seen);
            results.Add(new TypeDependencyNode(dep, children));
        }

        return results;
    }

    /// <summary>
    /// Collects all interfaces transitively reachable from a type's dependencies
    /// (not including the type itself).
    /// </summary>
    private static void CollectTransitive(
        string typeName,
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        HashSet<string> result,
        HashSet<string> visited)
    {
        var normalized = FqnParser.NormalizeTypeName(typeName);
        if (!visited.Add(normalized))
            return;

        var matchKey = ResolveTransitiveKey(typeIndex, normalized);
        if (matchKey == null)
            return;

        var (_, mdReader, typeDef) = typeIndex[matchKey];
        var context = GenericContext.ForType(mdReader, typeDef);

        // Base type
        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(mdReader, typeDef.BaseType, context);
            if (baseTypeName != null && !IsSystemRoot(baseTypeName))
            {
                result.Add(FqnParser.NormalizeTypeName(baseTypeName));
                CollectTransitive(baseTypeName, typeIndex, result, visited);
            }
        }

        // Interfaces
        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = mdReader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(mdReader, iface.Interface, context);
            if (ifaceName != null)
            {
                result.Add(FqnParser.NormalizeTypeName(ifaceName));
                CollectTransitive(ifaceName, typeIndex, result, visited);
            }
        }
    }

    private static List<TypeDependencyNode> ResolveChildren(
        string typeName,
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        HashSet<string> seen)
    {
        var normalizedName = FqnParser.NormalizeTypeName(typeName);

        var matchKey = ResolveTransitiveKey(typeIndex, normalizedName);
        if (matchKey == null)
            return [];

        var match = typeIndex[matchKey];
        return BuildNode(match.MdReader, match.TypeDef, typeIndex, seen);
    }

    /// <summary>
    /// Resolves a base/interface name (already a full ECMA name from metadata) to an index key.
    /// Tries an exact dictionary hit first — the common case for transitive walks — and only falls
    /// back to the fuzzy namespace-suffix scan, so closure traversal is O(1) per node instead of
    /// O(index size). The user-supplied root pattern still uses the fuzzy scan directly.
    /// </summary>
    private static string? ResolveTransitiveKey(
        Dictionary<string, (PEReader PeReader, MetadataReader MdReader, TypeDefinition TypeDef)> typeIndex,
        string normalized)
        => typeIndex.ContainsKey(normalized)
            ? normalized
            : typeIndex.Keys.FirstOrDefault(k => TypeMatcher.Matches(k, normalized));

    private static Exception ToRejectionException(
        TypeDependencyRejection rejection,
        IReadOnlyDictionary<string, BadImageFormatException> invalidImageCauses)
        => rejection.Kind switch
        {
            TypeDependencyRejectionKind.UnsupportedMetadataFormat =>
                new UnsupportedMetadataFormatException(),
            TypeDependencyRejectionKind.MalformedMetadataRoot
                when rejection.MetadataRootReason is { } reason =>
                new MalformedMetadataRootException(reason),
            TypeDependencyRejectionKind.InvalidImage =>
                invalidImageCauses.TryGetValue(
                    rejection.AssemblyPath,
                    out BadImageFormatException? cause)
                    ? cause
                    : new BadImageFormatException(
                        $"'{rejection.AssemblyPath}' has invalid metadata."),
            _ => new InvalidOperationException(
                "Unknown metadata-format rejection."),
        };

    private static bool IsSystemRoot(string typeName)
    {
        return typeName is "System.Object" or "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate";
    }
}
