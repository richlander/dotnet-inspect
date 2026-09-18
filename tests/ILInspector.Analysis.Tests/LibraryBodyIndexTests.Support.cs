using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    static byte[] SerializeDirectionProbe(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static void AddAsyncStateMachineAttribute(
        MetadataBuilder metadata,
        MethodDefinitionHandle method,
        MemberReferenceHandle constructor,
        string stateMachineType)
    {
        var value = new BlobBuilder();
        value.WriteUInt16(0x0001);
        value.WriteSerializedString(stateMachineType);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            method,
            constructor,
            metadata.GetOrAddBlob(value));
    }

    /// <summary>Emits a minimal unsigned assembly whose only content is what <paramref name="addContent"/> adds.</summary>
    static byte[] EmitAssembly(
        string name,
        Action<MetadataBuilder> addContent,
        Guid? moduleVersionId = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(moduleVersionId ?? Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: System.Reflection.AssemblyHashAlgorithm.Sha1);

        // Row 1 is always <Module>, exactly as a compiler emits.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        addContent(metadata);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static void ReplaceAscii(
        byte[] image,
        string oldValue,
        string newValue,
        int expectedReplacements)
    {
        Assert.Equal(oldValue.Length, newValue.Length);
        byte[] oldBytes = System.Text.Encoding.ASCII.GetBytes(oldValue);
        byte[] newBytes = System.Text.Encoding.ASCII.GetBytes(newValue);
        int replacements = 0;
        for (int i = 0; i <= image.Length - oldBytes.Length; i++)
        {
            if (!image.AsSpan(i, oldBytes.Length).SequenceEqual(oldBytes))
                continue;

            newBytes.CopyTo(image.AsSpan(i, newBytes.Length));
            replacements++;
        }

        Assert.Equal(expectedReplacements, replacements);
    }

}
