using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The handle-direct <see cref="IrImporter.Import(MetadataSource, TypeDefinitionHandle, MethodDefinitionHandle)"/>
/// front door — the canonical addressing the member-body substrate builds on —
/// must resolve to the same body the by-name/overload-index front door does for
/// the same method. These tests prove that equivalence broadly over a real
/// assembly and on the interleaved-visibility overload shape the positional
/// <c>publicOnly</c> index is fragile against.
/// </summary>
public class IrImporterHandleAddressingTests
{
    static string? Render(IrFunction? function)
        => function is null ? null : CSharpPrinter.PrintRaised(function).Output;

    [Fact]
    public void HandleDirect_MatchesByName_AcrossRealAssembly()
    {
        var source = MetadataSource.Open(typeof(AllocSampleClass).Assembly.Location);
        var reader = source.Reader;

        int compared = 0;
        int bodies = 0;
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            string typeFullName = reader.GetFullTypeName(typeDef);

            // The by-name front door counts every same-name overload in metadata
            // order at publicOnly:false, so the ordinal for a handle is the number
            // of same-name methods that precede it.
            var ordinals = new Dictionary<string, int>(System.StringComparer.Ordinal);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(method.Name);
                int ordinal = ordinals.TryGetValue(name, out var seen) ? seen : 0;
                ordinals[name] = ordinal + 1;

                var byName = Render(IrImporter.Import(source, typeFullName, name, ordinal, publicOnly: false));
                var byHandle = Render(IrImporter.Import(source, typeDefHandle, methodHandle));

                Assert.Equal(byName, byHandle);
                compared++;
                if (byHandle is not null)
                    bodies++;
            }
        }

        Assert.True(compared > 100, $"expected a broad sweep, only compared {compared} methods");
        Assert.True(bodies > 0, "expected at least one method with a body");
    }

    [Fact]
    public void HandleDirect_SelectsCorrectOverload_WhenPublicAndPrivateInterleave()
    {
        var source = MetadataSource.Open(typeof(InterleavedVisibilityOverloads).Assembly.Location);
        var reader = source.Reader;

        var typeDefHandle = System.Linq.Enumerable.Single(
            reader.TypeDefinitions,
            h => reader.GetFullTypeName(reader.GetTypeDefinition(h))
                == typeof(InterleavedVisibilityOverloads).FullName);
        var typeDef = reader.GetTypeDefinition(typeDefHandle);

        // Each Marker() overload returns a distinct literal, so the rendered body
        // identifies which method was resolved. Import each by its own handle; the
        // three bodies must all be present and distinct — proving handle-direct
        // addressing reaches the private overload interleaved between the public
        // ones, which a publicOnly index would skip or misplace.
        var bodies = new List<string>();
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != nameof(InterleavedVisibilityOverloads.Marker))
                continue;

            var body = Render(IrImporter.Import(source, typeDefHandle, methodHandle));
            Assert.NotNull(body);
            bodies.Add(body!);
        }

        Assert.Equal(3, bodies.Count);
        Assert.Equal(3, new HashSet<string>(bodies).Count);
    }

    [Fact]
    public void MetadataToken_ResolvesExactHandle_ForSurfaceMethodMembers()
    {
        // The substrate's same-reader address is ApiMember.MetadataToken: because
        // the surface is extracted from the same reader the bodies import through,
        // the token is an exact MethodDefinitionHandle. This proves that primitive
        // end-to-end over a real surface — every method-bearing member's token
        // points at a method whose metadata name matches the member, and the
        // handle-direct import produces a body exactly when the method has IL.
        string path = typeof(AllocSampleClass).Assembly.Location;
        var surface = AssemblyReader.ExtractApiSurface(path, includeAll: true);
        Assert.NotNull(surface);

        var source = MetadataSource.Open(path);
        var reader = source.Reader;

        int resolved = 0;
        foreach (var type in surface!.Types)
        {
            foreach (var member in type.Members)
            {
                if (member.MetadataToken is not int token)
                    continue;

                var handle = MetadataTokens.EntityHandle(token);
                if (handle.Kind != HandleKind.MethodDefinition)
                    continue;

                var methodHandle = (MethodDefinitionHandle)handle;
                var method = reader.GetMethodDefinition(methodHandle);
                var typeHandle = method.GetDeclaringType();

                // The token points at the right method: its metadata name matches
                // the member (constructors carry the metadata name ".ctor").
                string metadataName = reader.GetString(method.Name);
                string expectedName = member.Kind == "constructor" ? ".ctor" : member.Name;
                Assert.Equal(expectedName, metadataName);

                // Handle-direct import yields a body exactly when the method has IL.
                bool hasBody = method.RelativeVirtualAddress != 0;
                var imported = IrImporter.Import(source, typeHandle, methodHandle);
                Assert.Equal(hasBody, imported is not null);

                resolved++;
            }
        }

        Assert.True(resolved > 20, $"expected a broad sweep, only resolved {resolved} members");
    }

    [Fact]
    public void SurfaceAndMetadataAnchors_UseDistinctSpellings()
    {
        // Characterization guard: the API-flavored surface anchor (GetMemberAnchor,
        // int/object?/generic-<>) and the metadata-flavored anchor (CreateMethodAnchor,
        // System.Int32/generic-`n) are DELIBERATELY distinct spelling spaces whose
        // fingerprints do not cross-match. The substrate must NOT resolve cross-reader
        // members by comparing these raw fingerprints; it uses MetadataToken
        // (same-reader) or the normalizing ResearchMemberIdentity bridge instead.
        // This test pins that drift so a future "unification" that silently makes the
        // two fingerprints equal (or diverge differently) is caught.
        string path = typeof(AllocSampleClass).Assembly.Location;
        var surface = AssemblyReader.ExtractApiSurface(path, includeAll: true);
        Assert.NotNull(surface);

        var source = MetadataSource.Open(path);
        var reader = source.Reader;

        int spellingDivergences = 0;
        foreach (var type in surface!.Types)
        {
            foreach (var member in type.Members)
            {
                if (member.MetadataToken is not int token)
                    continue;

                var handle = MetadataTokens.EntityHandle(token);
                if (handle.Kind != HandleKind.MethodDefinition)
                    continue;

                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var typeHandle = method.GetDeclaringType();

                MemberAnchor surfaceAnchor = ApiMemberIdentity.GetMemberAnchor(type, member);
                MemberAnchor metadataAnchor = ApiMemberIdentity.CreateMethodAnchor(
                    reader, typeHandle, method, member.IsExtension);

                if (surfaceAnchor.CanonicalSignature != metadataAnchor.CanonicalSignature)
                    spellingDivergences++;
            }
        }

        Assert.True(
            spellingDivergences > 0,
            "expected the API-flavored and metadata-flavored anchor spellings to diverge; " +
            "if they now agree, the substrate's cross-reader resolution assumptions must be revisited");
    }
}

public class InterleavedVisibilityOverloads
{
    public int Marker() => 10;
    private int Marker(int a) => 20 + a;
    public int Marker(int a, int b) => 30 + a + b;
}