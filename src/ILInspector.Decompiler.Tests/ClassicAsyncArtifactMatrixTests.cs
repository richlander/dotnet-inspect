using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
[Trait("Speed", "Slow")]
public sealed class ClassicAsyncArtifactMatrixTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts."
        + "ClassicAsyncArtifactFixtures";
    const string DefaultInterfaceType =
        "ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts."
        + "IClassicDefaultArtifactFixture";
    const string RecoverableMethod = "RecoverableAsync";
    const string RemovedMethod = "RemovedAsync";
    const string DefaultInterfaceMethod = "DefaultAsync";

    static readonly Lazy<Task<ArtifactMatrix>> s_matrix =
        new(BuildMatrixAsync);

    [Fact]
    public async Task
        TrimmedArtifactWithoutRolePreservation_RetainsKickoffButCannotFormRequest()
    {
        ArtifactMatrix matrix = await s_matrix.Value;

        using ArtifactEvidence artifact = ArtifactEvidence.Open(matrix.Trimmed);
        StateMachineRelationshipResult result =
            artifact.Relationship(FixtureType, RecoverableMethod);

        var rejected =
            Assert.IsType<StateMachineRelationshipResult.Rejected>(result);
        Assert.Equal(
            StateMachineRelationshipFailureKind.Unresolved,
            rejected.Failure.Kind);
        Assert.Contains(
            "required state-machine interface role",
            rejected.Failure.Detail,
            StringComparison.Ordinal);

        var stateMachine = Assert.Single(
            rejected.Failure.StateMachineCandidates);
        Assert.True(
            stateMachine.TryResolve(
                artifact.Reader,
                out TypeDefinitionHandle stateMachineHandle));
        TypeDefinition definition = artifact.Reader.GetTypeDefinition(
            stateMachineHandle);
        string[] methods = definition.GetMethods()
            .Select(handle => artifact.Reader.GetString(
                artifact.Reader.GetMethodDefinition(handle).Name))
            .ToArray();
        Assert.Contains("MoveNext", methods);
        Assert.DoesNotContain("SetStateMachine", methods);
    }

    [Fact]
    public async Task TrimmedArtifact_RemovesUnusedAsyncMethod()
    {
        ArtifactMatrix matrix = await s_matrix.Value;

        using ArtifactEvidence artifact = ArtifactEvidence.Open(matrix.Trimmed);

        Assert.True(artifact.FindMethod(FixtureType, RemovedMethod).IsNil);
    }

    [Fact]
    public async Task
        ImplementationAndRolePreservedTrim_AuthenticateRecoverableClassicRecipe()
    {
        ArtifactMatrix matrix = await s_matrix.Value;

        using (ArtifactEvidence untrimmedArtifact =
            ArtifactEvidence.Open(matrix.Untrimmed))
        {
            AssertClassicRelationship(
                untrimmedArtifact.Relationship(
                    FixtureType,
                    RecoverableMethod));
        }

        AssertReconstructsAcceptedRecipe(matrix.Untrimmed);

        using ArtifactEvidence rolePreservedArtifact =
            ArtifactEvidence.Open(matrix.RolePreservedTrimmed);
        AssertClassicRelationship(
            rolePreservedArtifact.Relationship(
                FixtureType,
                RecoverableMethod));

        AssertReconstructsAcceptedRecipe(matrix.RolePreservedTrimmed);
    }

    static void AssertReconstructsAcceptedRecipe(string assemblyPath)
    {
        DecompilerResult result = Render(assemblyPath, RecoverableMethod);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("await first", result.Output, StringComparison.Ordinal);
        Assert.Contains("await second", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        ReferenceArtifact_AuthenticatesRelationshipsOverBodyReplacingIl()
    {
        ArtifactMatrix matrix = await s_matrix.Value;

        using ArtifactEvidence artifact =
            ArtifactEvidence.Open(matrix.Reference);
        var resolved = AssertClassicRelationship(
            artifact.Relationship(FixtureType, RecoverableMethod));

        AssertBodyReplacing(artifact, resolved.Relationship.Kickoff.Handle);
        Assert.All(
            resolved.Relationship.Methods,
            method => AssertBodyReplacing(artifact, method.Method.Handle));
    }

    [Fact]
    public async Task
        DefaultInterfaceArtifact_AuthenticatesFromManagedMethodEvidence()
    {
        ArtifactMatrix matrix = await s_matrix.Value;

        using ArtifactEvidence artifact =
            ArtifactEvidence.Open(matrix.Untrimmed);
        AssertClassicRelationship(
            artifact.Relationship(
                DefaultInterfaceType,
                DefaultInterfaceMethod));
    }

    static StateMachineRelationshipResult.Resolved AssertClassicRelationship(
        StateMachineRelationshipResult result)
    {
        var resolved =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(result);
        Assert.Equal(
            StateMachineClaimKind.ClassicAsync,
            resolved.Relationship.Kind);
        Assert.Collection(
            resolved.Relationship.Methods.OrderBy(method => method.Role),
            method => Assert.Equal(
                StateMachineMethodRole.MoveNext,
                method.Role),
            method => Assert.Equal(
                StateMachineMethodRole.SetStateMachine,
                method.Role));
        return resolved;
    }

    static void AssertBodyReplacing(
        ArtifactEvidence artifact,
        MethodDefinitionHandle methodHandle)
    {
        MethodDefinition method = artifact.Reader.GetMethodDefinition(
            methodHandle);
        byte[] il = artifact.PeReader
            .GetMethodBody(method.RelativeVirtualAddress)
            .GetILBytes()
            ?? throw new BadImageFormatException(
                "The reference MethodDef has no IL body.");
        Assert.Equal([0x14, 0x7A], il);
    }

    static DecompilerResult Render(string assemblyPath, string methodName)
    {
        using MetadataSource source = MetadataSource.Open(assemblyPath);
        IrFunction? function = IrImporter.Import(
            source,
            FixtureType,
            methodName);
        Assert.NotNull(function);

        return CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method));
    }

    static async Task<ArtifactMatrix> BuildMatrixAsync()
    {
        FixtureDefinition fixture =
            FixtureCatalog.DecompilerClassicAsyncArtifacts;
        string repositoryRoot =
            AuthoredCorpusRatchetTests.FindRepositoryRoot();
        string artifactRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "classic-async-artifact-matrix",
            RuntimeInformation.RuntimeIdentifier);
        await BuildArtifactMatrixAsync(repositoryRoot);

        string untrimmed = fixture.AssemblyPath();
        string reference = Path.Combine(
            repositoryRoot,
            "artifacts",
            "obj",
            fixture.ProjectName,
            "release",
            "ref",
            fixture.AssemblyFileName);

        Assert.True(
            File.Exists(reference),
            $"Expected the SDK reference artifact at {reference}.");

        string trimmed = PublishedAssembly(
            fixture,
            artifactRoot,
            "trimmed");
        string rolePreserved = PublishedAssembly(
            fixture,
            artifactRoot,
            "role-preserved-trimmed");

        return new ArtifactMatrix(
            untrimmed,
            reference,
            trimmed,
            rolePreserved);
    }

    static string PublishedAssembly(
        FixtureDefinition fixture,
        string artifactRoot,
        string variant)
    {
        string assemblyPath = Path.Combine(
            artifactRoot,
            variant,
            fixture.AssemblyFileName);
        Assert.True(
            File.Exists(assemblyPath),
            $"Artifact matrix build did not produce {assemblyPath}.");
        return assemblyPath;
    }

    static async Task BuildArtifactMatrixAsync(string repositoryRoot)
    {
        string dotnet = DotnetHost();
        var startInfo = new ProcessStartInfo(dotnet)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "eng",
            "classic-async-artifact-matrix.proj"));
        startInfo.ArgumentList.Add("-t:Build");
        startInfo.ArgumentList.Add("-p:Configuration=Release");
        startInfo.ArgumentList.Add(
            $"-p:RuntimeIdentifier={RuntimeInformation.RuntimeIdentifier}");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:minimal");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start '{dotnet}'.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            if (TestContext.Current.CancellationToken.IsCancellationRequested)
                throw;

            Assert.Fail(
                "Building the classic async artifact matrix timed out.");
        }

        string output = string.Concat(await stdout, await stderr);
        Assert.True(
            process.ExitCode == 0,
            "Building the classic async artifact matrix exited "
                + $"{process.ExitCode}.{Environment.NewLine}{output}");
    }

    static string DotnetHost()
    {
        string? host =
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(host))
            return host;

        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            string candidate = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        return "dotnet";
    }

    sealed record ArtifactMatrix(
        string Untrimmed,
        string Reference,
        string Trimmed,
        string RolePreservedTrimmed);

    sealed class ArtifactEvidence : IDisposable
    {
        readonly FileStream _stream;
        readonly StateMachineRelationshipIndex _relationships;

        ArtifactEvidence(
            FileStream stream,
            PEReader peReader,
            MetadataReader reader)
        {
            _stream = stream;
            PeReader = peReader;
            Reader = reader;
            _relationships = StateMachineRelationshipIndex.Create(reader);
        }

        public PEReader PeReader { get; }
        public MetadataReader Reader { get; }

        public static ArtifactEvidence Open(string path)
        {
            FileStream stream = File.OpenRead(path);
            var peReader = new PEReader(stream);
            return new ArtifactEvidence(
                stream,
                peReader,
                peReader.GetMetadataReader());
        }

        public StateMachineRelationshipResult Relationship(
            string typeName,
            string methodName)
        {
            MethodDefinitionHandle method = FindMethod(typeName, methodName);
            Assert.False(
                method.IsNil,
                $"Expected {typeName}.{methodName} in the artifact.");
            return _relationships.GetByKickoff(method);
        }

        public MethodDefinitionHandle FindMethod(
            string typeName,
            string methodName)
        {
            foreach (TypeDefinitionHandle typeHandle in Reader.TypeDefinitions)
            {
                TypeDefinition type = Reader.GetTypeDefinition(typeHandle);
                string candidate = Reader.GetString(type.Namespace).Length == 0
                    ? Reader.GetString(type.Name)
                    : $"{Reader.GetString(type.Namespace)}."
                        + Reader.GetString(type.Name);
                if (!string.Equals(
                        candidate,
                        typeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return type.GetMethods().FirstOrDefault(
                    handle => Reader.StringComparer.Equals(
                        Reader.GetMethodDefinition(handle).Name,
                        methodName));
            }

            return default;
        }

        public void Dispose()
        {
            PeReader.Dispose();
            _stream.Dispose();
        }
    }
}
