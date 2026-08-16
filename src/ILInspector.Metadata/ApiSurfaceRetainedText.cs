namespace ILInspector.Metadata;

/// <summary>Counts text retained by API-surface transfer models without projecting new text.</summary>
/// <remarks>
/// <c>ApiSurfaceExtractorBoundsTests.RetainedTextBudget_IsExact</c> gates exact accounting on a
/// real compiled surface, while
/// <c>RepeatedLongMethodName_IsStoppedByRetainedTextBeforeRowBounds</c> gates the hostile shared
/// string shape that motivated this budget.
/// </remarks>
internal static class ApiSurfaceRetainedText
{
    public static long Surface(ApiSurface surface) =>
        Length(surface.Name)
        + Length(surface.Version)
        + Length(surface.Source)
        + Length(surface.Library)
        + Length(surface.Tfm)
        + Length(surface.RepositoryUrl)
        + surface.Types.Sum(
            type => TypeHeader(type) + type.Members.Sum(Member))
        + surface.InspectionFailures.Sum(InspectionFailure)
        + surface.TypeForwarders.Sum(TypeForwarder);

    public static long TypeHeader(ApiType type) =>
        TypeHeaderWithoutInterfaces(type) + Strings(type.Interfaces);

    public static long TypeHeaderWithoutInterfaces(ApiType type) =>
        Length(type.Namespace)
        + Length(type.Name)
        + Length(type.MetadataName)
        + DefinitionName(type.DefinitionName)
        + Length(type.Accessibility)
        + Length(type.Kind)
        + Strings(type.Attributes)
        + Length(type.EnumUnderlyingType)
        + Length(type.BaseType)
        + Strings(type.DerivedTypes)
        + type.TypeParameters.Sum(TypeParameter)
        + Length(type.SourceFilePath)
        + Length(type.SourceUrl)
        + Length(type.GitHubBrowseUrl)
        + Length(type.SourceChecksumAlgorithm)
        + Length(type.SourceResolution)
        + type.AdditionalSourceFiles.Sum(PartialSourceFile)
        + Length(type.SourceAssemblyPath)
        + Documentation(type.Documentation);

    public static long Member(ApiMember member) =>
        Length(member.Name)
        + Length(member.Kind)
        + Strings(member.Attributes)
        + Length(member.ReturnType)
        + Length(member.Signature)
        + Length(member.Digest)
        + Length(member.CanonicalSignature)
        + Signature(member.SignatureModel)
        + Length(member.Accessibility)
        + Length(member.ObsoleteMessage)
        + Length(member.ExtendedType)
        + Length(member.DeclaringType)
        + Length(member.EnumValueLiteral)
        + Length(member.SourceFilePath)
        + Length(member.SourceUrl)
        + Length(member.SourceChecksumAlgorithm)
        + Documentation(member.Documentation);

    public static long InspectionFailure(ApiSurfaceInspectionFailure failure) =>
        Length(failure.Operation)
        + Length(failure.Kind)
        + Length(failure.Detail);

    public static long TypeForwarder(TypeForwarder forwarder) =>
        DefinitionName(forwarder.DefinitionName)
        + Length(forwarder.TypeName)
        + Length(forwarder.TargetAssembly);

    static long Signature(ApiSignature? signature) =>
        signature is null
            ? 0
            : Length(signature.ReturnType)
                + Length(signature.CanonicalReturnType)
                + Strings(signature.ReturnAttributes)
                + Length(signature.MemberName)
                + signature.TypeParameters.Sum(TypeParameter)
                + signature.Parameters.Sum(Parameter)
                + signature.Accessors.Sum(Accessor);

    public static long TypeParameter(TypeParameter parameter) =>
        Length(parameter.Name)
        + Length(parameter.Variance)
        + Strings(parameter.Constraints)
        + (parameter.StructuredConstraints?.Sum(
            constraint => Length(constraint.Value)) ?? 0);

    public static long Parameter(ApiParameter parameter) =>
        Strings(parameter.Attributes)
        + Length(parameter.Name)
        + Length(parameter.Type)
        + Length(parameter.CanonicalType)
        + Length(parameter.Modifier)
        + Length(parameter.DefaultValueText);

    public static long Accessor(ApiAccessor accessor) =>
        Length(accessor.Kind)
        + Length(accessor.Accessibility)
        + Strings(accessor.ReturnAttributes);

    static long DefinitionName(MetadataTypeDefinitionName? name) =>
        name is null
            ? 0
            : Length(name.Namespace) + Strings(name.Segments);

    static long Documentation(DocComment documentation) =>
        Length(documentation.Summary)
        + Length(documentation.Remarks)
        + (documentation.Parameters?.Sum(
            parameter => (long)Length(parameter.Key) + Length(parameter.Value)) ?? 0)
        + Length(documentation.Returns)
        + documentation.Samples.Sum(Sample);

    static long Sample(SampleReference sample) =>
        Length(sample.RelativePath)
        + Length(sample.Description)
        + Length(sample.Region)
        + Length(sample.ResolvedUrl)
        + Length(sample.Content);

    static long PartialSourceFile(PartialSourceFileInfo file) =>
        Length(file.FilePath)
        + Length(file.SourceUrl)
        + Length(file.GitHubBrowseUrl)
        + Length(file.SourceChecksumAlgorithm);

    static long Strings(IEnumerable<string> values)
    {
        long characters = 0;
        foreach (string value in values)
            characters += value.Length;
        return characters;
    }

    static int Length(string? value) => value?.Length ?? 0;
}
