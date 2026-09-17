using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

public static partial class ApiSurfaceExtractor
{

    static bool DefinesPrimitiveTypes(MetadataReader reader)
        => PrimitiveDefinitionClassifications.GetValue(
            reader,
            static value => new(
                ComputeDefinesPrimitiveTypes(value)))
            .Value;

    static bool ComputeDefinesPrimitiveTypes(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return false;

        try
        {
            AssemblyDefinition definition =
                reader.GetAssemblyDefinition();
            string name = reader.GetString(definition.Name);
            if (name is not ("System.Private.CoreLib" or "mscorlib"))
                return false;
            if (reader.GetBlobReader(definition.PublicKey).Length
                > MetadataSafetyPolicy.MaxStructuralSignatureChars / 2)
            {
                return false;
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            return identity.PublicKeyToken is { } token
                && PlatformKeys.IsPlatform(token);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    sealed record PrimitiveDefinitionClassification(bool Value);

    internal static MetadataTypeDefinitionName? GetLocalPrimitiveDefinition(
        PrimitiveTypeCode typeCode)
    {
        if (typeCode is not (
            PrimitiveTypeCode.Void
            or PrimitiveTypeCode.Boolean
            or PrimitiveTypeCode.Char
            or PrimitiveTypeCode.SByte
            or PrimitiveTypeCode.Byte
            or PrimitiveTypeCode.Int16
            or PrimitiveTypeCode.UInt16
            or PrimitiveTypeCode.Int32
            or PrimitiveTypeCode.UInt32
            or PrimitiveTypeCode.Int64
            or PrimitiveTypeCode.UInt64
            or PrimitiveTypeCode.Single
            or PrimitiveTypeCode.Double
            or PrimitiveTypeCode.String
            or PrimitiveTypeCode.Object
            or PrimitiveTypeCode.IntPtr
            or PrimitiveTypeCode.UIntPtr
            or PrimitiveTypeCode.TypedReference))
        {
            return null;
        }

        return ((MetadataTypeDefinitionNameResult.Valid)
            MetadataTypeDefinitionName.Create(
                "System",
                [typeCode.ToString()])).Name;
    }

    static string DecodeString(
        MetadataReader reader,
        StringHandle handle,
        Action<int>? beforeDecodeWork)
    {
        beforeDecodeWork?.Invoke(reader.GetBlobReader(handle).Length);
        return reader.GetString(handle);
    }

    static ApiAssemblyIdentity? ResolveTypeAssemblyIdentity(
        MetadataReader reader,
        EntityHandle type,
        ApiAssemblyIdentity? currentAssembly,
        Action<int>? beforeDecodeWork)
    {
        if (type.Kind == HandleKind.TypeDefinition)
            return currentAssembly;
        if (type.Kind != HandleKind.TypeReference)
            return null;

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    (TypeReferenceHandle)type,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _))
        {
            return null;
        }

        return terminal.Kind switch
        {
            HandleKind.AssemblyReference =>
                ApiAssemblyIdentity.FromReference(
                    reader,
                    (AssemblyReferenceHandle)terminal,
                    beforeDecodeWork),
            HandleKind.ModuleDefinition or HandleKind.ModuleReference =>
                currentAssembly,
            _ when terminal.IsNil => currentAssembly,
            _ => null,
        };
    }

    sealed class TypeParameterConstraintResolution
    {
        readonly MetadataReader _reader;
        readonly List<Group> _groups = [];

        internal TypeParameterConstraintResolution(
            MetadataReader reader,
            ResolvedAssemblyReference source,
            int maxTypeResolutionRequests)
        {
            _reader = reader;
            Plan = new TypeParameterKindClassifier.ResolutionPlan(
                reader,
                source,
                maxTypeResolutionRequests);
        }

        internal TypeParameterKindClassifier.ResolutionPlan Plan { get; }

        internal IReadOnlyCollection<TypeResolutionRequest> Requests =>
            Plan.Requests;

        internal Checkpoint CreateCheckpoint() =>
            new(_groups.Count, Plan.Checkpoint());

        internal void Rollback(Checkpoint checkpoint)
        {
            if (checkpoint.GroupCount < 0
                || checkpoint.GroupCount > _groups.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint));
            }

            _groups.RemoveRange(
                checkpoint.GroupCount,
                _groups.Count - checkpoint.GroupCount);
            Plan.Rollback(checkpoint.RequestCheckpoint);
        }

        internal void Track(
            EntityHandle subject,
            List<(GenericParameterHandle Handle, TypeParameter Parameter)>
                group)
        {
            if (group.Count != 0)
                _groups.Add(new Group(subject, group));
        }

        internal void Apply(TypeResolutionContext context)
        {
            Plan.Bind(context);
            foreach (var group in _groups)
            {
                var chain =
                    new TypeParameterKindClassifier.ChainState(
                        Plan,
                        group.Subject);
                foreach (var (handle, parameter) in group.Parameters)
                {
                    GenericParameter definition =
                        _reader.GetGenericParameter(handle);
                    GenericParameterAttributes attributes =
                        definition.Attributes;
                    parameter.TypeKind =
                        TypeParameterKindClassifier.Classify(
                            _reader,
                            handle,
                            hasValueTypeConstraint:
                                (attributes
                                    & GenericParameterAttributes
                                        .NotNullableValueTypeConstraint) != 0,
                            hasReferenceTypeConstraint:
                                (attributes
                                    & GenericParameterAttributes
                                        .ReferenceTypeConstraint) != 0,
                            chain);
                }
            }
        }

        internal readonly record struct Checkpoint(
            int GroupCount,
            TypeParameterKindClassifier.ResolutionPlan.RequestCheckpoint
                RequestCheckpoint);

        readonly record struct Group(
            EntityHandle Subject,
            List<(GenericParameterHandle Handle, TypeParameter Parameter)>
                Parameters);
    }
}
