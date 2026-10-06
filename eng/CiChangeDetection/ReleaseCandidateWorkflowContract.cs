using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class ReleaseCandidateWorkflowContract
{
    internal static void AssertMutations(string repository)
    {
        string candidate = File.ReadAllText(
            Path.Combine(
                repository,
                ".github",
                "workflows",
                "release-candidate.yml"));
        string deepInspect = File.ReadAllText(
            Path.Combine(
                repository,
                ".github",
                "workflows",
                "deep-inspect.yml"));

        ValidateCandidate(candidate);
        ValidateDeepInspectAdoption(deepInspect);

        AssertMutationRejected(
            candidate,
            "  cancel-in-progress: false\n",
            "  cancel-in-progress: true\n",
            ValidateCandidate,
            "Release candidate contract accepted active-run cancellation.");
        AssertMutationRejected(
            candidate,
            "    needs: assemble\n",
            "    needs: source\n",
            ValidateCandidate,
            "Release candidate contract accepted certification before asset assembly.");
        AssertMutationRejected(
            candidate,
            "          retention-days: 30\n",
            "          retention-days: 1\n",
            ValidateCandidate,
            "Release candidate contract accepted short-lived release assets.");
        AssertMutationRejected(
            candidate,
            "          retention-days: 30\n" +
            "          include-hidden-files: true\n" +
            "          overwrite: true\n",
            "          retention-days: 30\n" +
            "          include-hidden-files: true\n",
            ValidateCandidate,
            "Release candidate contract accepted a non-rerun-safe artifact.");
        AssertMutationRejected(
            candidate,
            "      - name: Verify production site\n" +
            "        run: eng/verify-inspect-web-site-artifact.sh artifacts/inspect-web-publish\n",
            "",
            ValidateCandidate,
            "Release candidate contract accepted an unverified production site.");
        AssertMutationRejected(
            candidate,
            "      lane: release-candidate\n",
            "      lane: test\n",
            ValidateCandidate,
            "Release candidate contract accepted incomplete Deep Inspect evidence.");
        AssertMutationRejected(
            candidate,
            "    permissions:\n" +
            "      contents: read\n" +
            "      packages: read\n" +
            "      issues: write\n" +
            "    uses: ./.github/workflows/deep-inspect.yml\n",
            "    permissions:\n" +
            "      contents: read\n" +
            "      issues: write\n" +
            "    uses: ./.github/workflows/deep-inspect.yml\n",
            ValidateCandidate,
            "Release candidate contract accepted a caller without Deep Inspect package access.");
        AssertMutationRejected(
            deepInspect,
            "      inputs.lane == 'census' ||\n" +
            "      inputs.lane == 'release-candidate' ||\n",
            "      inputs.lane == 'census' ||\n",
            ValidateDeepInspectAdoption,
            "Deep Inspect contract accepted a missing release-candidate route.");
        AssertMutationRejected(
            deepInspect,
            "      - name: Run Resource ownership contract tests\n" +
            "        if: ${{ !cancelled() && steps.build.outcome == 'success' }}\n" +
            "        run: dotnet run --project tests/Inspector.Resources.Tests -c Release\n",
            "",
            ValidateDeepInspectAdoption,
            "Deep Inspect contract accepted a removed daily suite.");
        AssertMutationRejected(
            deepInspect,
            "        run: eng/test-ts-jsexport-context-aot.sh \"linux-x64\"\n",
            "        run: echo skipped\n",
            ValidateDeepInspectAdoption,
            "Deep Inspect contract accepted a missing NativeAOT probe.");
        AssertMutationRejected(
            deepInspect,
            "        if: ${{ !cancelled() && steps.package_fixture.outcome == 'failure' }}\n",
            "        if: false\n",
            ValidateDeepInspectAdoption,
            "Deep Inspect contract accepted a green failed package fixture.");
    }

    private static void ValidateCandidate(string workflow)
    {
        YamlMappingNode root = LoadRoot(workflow, "release candidate workflow");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "concurrency", "env", "jobs"],
            "release candidate workflow");
        RequireScalarValue(
            root,
            "name",
            "Nightly release candidate",
            "release candidate workflow");

        YamlMappingNode trigger =
            GetRequiredMapping(root, "on", "release candidate workflow");
        RequireExactKeys(
            trigger,
            ["schedule", "workflow_dispatch"],
            "release candidate trigger");
        YamlSequenceNode schedules =
            GetRequiredSequence(trigger, "schedule", "release candidate trigger");
        if (schedules.Children.Count != 1)
            throw new InvalidOperationException("Release candidate must have one schedule.");
        YamlMappingNode schedule =
            RequireMapping(schedules.Children[0], "release candidate schedule");
        RequireExactScalarValues(
            schedule,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cron"] = "17 0 * * *",
            },
            "release candidate schedule");
        if (!TryGetNode(trigger, "workflow_dispatch", out YamlNode dispatch) ||
            dispatch is not YamlScalarNode { Value: null or "" })
        {
            throw new InvalidOperationException(
                "Release candidate workflow_dispatch must not declare inputs.");
        }

        RequireExactScalarValues(
            GetRequiredMapping(root, "concurrency", "release candidate workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "nightly-release-candidate",
                ["cancel-in-progress"] = "false",
            },
            "release candidate concurrency");

        YamlMappingNode jobs =
            GetRequiredMapping(root, "jobs", "release candidate workflow");
        RequireExactKeys(
            jobs,
            [
                "source",
                "build-native",
                "build-portable",
                "build-site",
                "assemble",
                "certify",
                "ready",
                "report-failure",
            ],
            "release candidate jobs");

        YamlMappingNode source =
            GetRequiredMapping(jobs, "source", "release candidate jobs");
        string sourceRun = GetRequiredScalar(
            FindStep(source, "Require green main and an unreleased version"),
            "run",
            "release candidate source validation");
        RequireContains(
            sourceRun,
            ".check_runs[] | select(.name == \"ci / ci-required\")");
        RequireContains(sourceRun, "test \"$skill_version\" = \"$version\"");
        RequireContains(
            sourceRun,
            "Version $version does not advance released version $release_version.");

        YamlMappingNode buildSite =
            GetRequiredMapping(jobs, "build-site", "release candidate jobs");
        string publishSite = GetRequiredScalar(
            FindStep(buildSite, "Publish browser app"),
            "run",
            "release candidate site publish");
        RequireContains(
            publishSite,
            "src/DotnetInspect.Web/DotnetInspect.Web.csproj");
        RequireContains(
            publishSite,
            "-p:InspectWebIncludeFrontend=true");
        YamlMappingNode verifySite =
            FindStep(buildSite, "Verify production site");
        RequireScalarValue(
            verifySite,
            "run",
            "eng/verify-inspect-web-site-artifact.sh artifacts/inspect-web-publish",
            "release candidate site verification");

        YamlMappingNode assemble =
            GetRequiredMapping(jobs, "assemble", "release candidate jobs");
        RequireSequenceValues(
            GetRequiredSequence(assemble, "needs", "release candidate assemble"),
            ["source", "build-native", "build-portable", "build-site"],
            "release candidate assemble needs");
        YamlMappingNode upload =
            FindStep(assemble, "Upload immutable candidate");
        YamlMappingNode uploadWith =
            GetRequiredMapping(upload, "with", "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "name",
            "dotnet-inspect-release-candidate",
            "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "retention-days",
            "30",
            "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "include-hidden-files",
            "true",
            "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "overwrite",
            "true",
            "release candidate upload");

        YamlMappingNode certify =
            GetRequiredMapping(jobs, "certify", "release candidate jobs");
        RequireScalarValue(certify, "needs", "assemble", "release candidate certify");
        YamlMappingNode certifyPermissions = GetRequiredMapping(
            certify, "permissions", "release candidate certify");
        RequireScalarValue(certifyPermissions, "contents", "read", "release candidate certify");
        RequireScalarValue(certifyPermissions, "packages", "read", "release candidate certify");
        RequireScalarValue(
            certify,
            "uses",
            "./.github/workflows/deep-inspect.yml",
            "release candidate certify");
        RequireScalarValue(
            GetRequiredMapping(certify, "with", "release candidate certify"),
            "lane",
            "release-candidate",
            "release candidate certify");

        YamlMappingNode ready =
            GetRequiredMapping(jobs, "ready", "release candidate jobs");
        RequireScalarValue(ready, "if", "always()", "release candidate ready");
        RequireSequenceValues(
            GetRequiredSequence(ready, "needs", "release candidate ready"),
            ["assemble", "certify"],
            "release candidate ready needs");

        YamlMappingNode report =
            GetRequiredMapping(jobs, "report-failure", "release candidate jobs");
        RequireScalarValue(
            report,
            "if",
            "${{ github.event_name == 'schedule' && failure() }}",
            "release candidate failure report");
        YamlMappingNode reportStep =
            FindStep(report, "Open or update the tracking issue");
        RequireContains(
            GetRequiredScalar(
                reportStep,
                "run",
                "release candidate failure report"),
            "title=\"Nightly release candidate failed\"");
    }

    private static void ValidateDeepInspectAdoption(string workflow)
    {
        YamlMappingNode root = LoadRoot(workflow, "Deep Inspect workflow");
        YamlMappingNode trigger = GetRequiredMapping(root, "on", "Deep Inspect workflow");
        YamlMappingNode workflowCall =
            GetRequiredMapping(trigger, "workflow_call", "Deep Inspect trigger");
        YamlMappingNode lane =
            GetRequiredMapping(
                GetRequiredMapping(workflowCall, "inputs", "Deep Inspect workflow_call"),
                "lane",
                "Deep Inspect workflow_call inputs");
        RequireScalarValue(lane, "required", "true", "Deep Inspect lane input");
        RequireScalarValue(lane, "type", "string", "Deep Inspect lane input");

        YamlSequenceNode schedules =
            GetRequiredSequence(trigger, "schedule", "Deep Inspect trigger");
        string[] crons = schedules.Children
            .Select(node => GetRequiredScalar(
                RequireMapping(node, "Deep Inspect schedule"),
                "cron",
                "Deep Inspect schedule"))
            .ToArray();
        if (crons.Contains("0 6 * * *", StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Deep Inspect must not independently schedule release certification.");
        if (!crons.Contains("30 11 * * *", StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Deep Inspect must schedule the full Linux test lane daily.");
        if (!crons.Contains("0 13 * * *", StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Deep Inspect must schedule full inspect-web coverage daily.");

        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "Deep Inspect workflow");
        YamlMappingNode testJob =
            GetRequiredMapping(jobs, "test", "Deep Inspect jobs");
        YamlMappingNode testPermissions =
            GetRequiredMapping(testJob, "permissions", "Deep Inspect test lane");
        RequireScalarValue(testPermissions, "contents", "read", "Deep Inspect test permissions");
        RequireScalarValue(testPermissions, "packages", "read", "Deep Inspect test permissions");
        string testCondition = GetRequiredScalar(
            testJob,
            "if",
            "Deep Inspect test lane");
        RequireContains(
            testCondition,
            "github.event_name == 'schedule' && github.event.schedule == '30 11 * * *'");
        YamlSequenceNode testSteps = GetRequiredSequence(
            testJob,
            "steps",
            "Deep Inspect test lane");
        YamlMappingNode[] scheduledSteps = testSteps.Children
            .Select(node => RequireMapping(node, "Deep Inspect test step"))
            .ToArray();
        YamlMappingNode[] querySteps = scheduledSteps
            .Where(step => GetOptionalScalar(step, "name") == "Run query tests")
            .ToArray();
        if (querySteps.Length != 1)
            throw new InvalidOperationException(
                "Daily Deep Inspect must run the full inspection-query suite.");
        RequireScalarValue(
            querySteps[0],
            "run",
            "dotnet run --project tests/DotnetInspector.Queries.Tests -c Release",
            "Deep Inspect query step");
        foreach (string project in new[]
        {
            "eng/DependencyPolicy.Tests",
            "tests/ILInspector.CSharp.Tests",
            "tests/Inspector.Graph.Tests",
            "tests/DotnetInspector.Libraries.Tests",
            "tests/Inspector.Resources.Tests",
            "tests/DotnetInspector.Platforms.Tests",
            "tests/DotnetInspector.DependencyManifests.Tests",
            "tests/DotnetInspector.PlatformHouse.Tests",
            "tests/DotnetInspector.PlatformHouse.Local.Tests",
            "tests/DotnetInspector.PortableQueries.Tests",
            "tests/DotnetInspector.RowSelection.Tests",
            "tests/DotnetInspector.PerformanceOracles.Tests",
            "tests/Inspector.Text.Tests",
            "tests/DotnetInspector.SourceSelection.Tests",
            "tests/DotnetInspector.SourceDelegation.Tests",
            "tests/DotnetInspector.SourceHouse.Tests",
            "tests/DotnetInspector.Sections.Tests",
            "tests/ILInspector.Instructions.Tests",
            "tests/ILInspector.ILDiff.Tests",
            "tests/DotnetInspector.FixtureInfrastructure.Tests",
            "tests/NetworkAccess.Tests",
            "tests/BinaryFetch.Tests",
            "tests/ZipFetch.Tests",
            "tests/UntrustedDocuments.Tests",
            "tests/DotnetInspector.Networking.Tests",
            "tests/DotnetInspector.Cache.Tests",
            "tests/DotnetInspector.Packages.Tests",
            "tests/ILInspector.JsExportSurface.Tests",
            "tests/runfaster.Tests",
        })
        {
            string command = $"dotnet run --project {project} -c Release";
            YamlMappingNode[] matches = scheduledSteps
                .Where(step => GetOptionalScalar(step, "run") == command)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Daily Deep Inspect must run the complete {project} suite once.");
            RequireScalarValue(
                matches[0],
                "if",
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
                $"Deep Inspect {project} step");
        }
        foreach ((string name, string command) in new[]
        {
            ("Self-test Library Type-leverage census", "eng/census-library-type-leverage.cs"),
            ("Test CLI runtime flavor across publish modes", "eng/test-runtime-flavor.sh linux-x64"),
            ("Run MethodSemantics NativeAOT probe", "eng/run-method-semantics-platform-probe.sh nativeaot linux-x64"),
            ("Run local-path admission NativeAOT probe", "eng/run-local-path-admission-platform-probe.sh nativeaot linux-x64"),
            ("Prepare PR decompiler corpus", "eng/prepare-decompiler-pr-corpus.sh"),
            ("Run PR decompiler corpus sensor", "--diff-corpus-baseline tools/DecompilerHarness/corpus/pr-quick-baseline.json"),
            ("Run GitHub Packages fixture test", "Package_Manifest_RendersToolManifestRows"),
            ("Check Subject Relations constructed-generic oracle", "tools/SubjectRelationsScorecard"),
            ("Exercise Catalog research probe (offline)", "tools/CatalogChangeBenchmark.cs"),
            ("Exercise package assembly-query benchmark (offline)", "tools/PackageAssemblyQueryBenchmark.cs"),
            ("Run JSExport runtime-async wire gates", "RuntimeAsync"),
            ("Run ts-jsexport generator acceptance gates", "TsJsExportContractsTests"),
            ("Run ts-jsexport context NativeAOT gate", "eng/test-ts-jsexport-context-aot.sh"),
            ("Run Debug dependency sidecar contracts (Release)", "-p:DefineConstants=DEBUG"),
        })
        {
            YamlMappingNode[] matches = scheduledSteps
                .Where(step => GetOptionalScalar(step, "name") == name)
                .ToArray();
            if (matches.Length != 1 || !GetRequiredScalar(
                    matches[0],
                    "run",
                    $"Deep Inspect {name}").Contains(command, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Daily Deep Inspect must run {name} once.");
            RequireScalarValue(
                matches[0],
                "if",
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
                $"Deep Inspect {name} condition");
        }
        YamlMappingNode corpusRun = scheduledSteps.Single(step =>
            GetOptionalScalar(step, "name") == "Run PR decompiler corpus sensor");
        RequireScalarValue(
            corpusRun, "continue-on-error", "true", "Deep Inspect PR corpus sensor");
        YamlMappingNode corpusUpload = scheduledSteps.Single(step =>
            GetOptionalScalar(step, "name") == "Upload PR decompiler corpus artifact");
        RequireScalarValue(
            corpusUpload, "if", "always()", "Deep Inspect PR corpus upload");
        RequireScalarValue(
            corpusUpload, "uses", "actions/upload-artifact@v7", "Deep Inspect PR corpus upload");
        YamlMappingNode corpusCheck = scheduledSteps.Single(step =>
            GetOptionalScalar(step, "name") == "Check PR decompiler corpus result");
        RequireScalarValue(
            corpusCheck,
            "if",
            "${{ !cancelled() && steps.decompiler_pr_corpus.outcome == 'failure' }}",
            "Deep Inspect PR corpus failure check");
        YamlMappingNode packageFixture = scheduledSteps.Single(step =>
            GetOptionalScalar(step, "name") == "Run GitHub Packages fixture test");
        RequireScalarValue(
            packageFixture, "continue-on-error", "true", "Deep Inspect package fixture");
        YamlMappingNode packageFixtureEnv = GetRequiredMapping(
            packageFixture, "env", "Deep Inspect package fixture");
        RequireScalarValue(
            packageFixtureEnv,
            "DOTNET_INSPECT_PACKAGE_FIXTURE_USER",
            "${{ github.actor }}",
            "Deep Inspect package fixture");
        RequireScalarValue(
            packageFixtureEnv,
            "DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN",
            "${{ github.token }}",
            "Deep Inspect package fixture");
        YamlMappingNode packageFixtureCheck = scheduledSteps.Single(step =>
            GetOptionalScalar(step, "name") == "Check GitHub Packages fixture result");
        RequireScalarValue(
            packageFixtureCheck,
            "if",
            "${{ !cancelled() && steps.package_fixture.outcome == 'failure' }}",
            "Deep Inspect package fixture failure check");
        foreach ((string name, string command) in new[]
        {
            ("Discover expected decompiler gate tests", "--gate-discovery-receipt"),
            ("Run bounded decompiler receipt", "--gate pre-merge"),
            ("Compare bounded decompiler receipt against known-red", "eng/check-decompiler-gate.cs"),
        })
        {
            YamlMappingNode[] matches = testSteps.Children
                .Select(node => RequireMapping(node, "Deep Inspect test step"))
                .Where(step => GetOptionalScalar(step, "name") == name)
                .ToArray();
            if (matches.Length != 1 || !GetRequiredScalar(
                    matches[0],
                    "run",
                    $"Deep Inspect {name}").Contains(command, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Daily Deep Inspect must run {name} once.");
        }
        foreach ((string name, string command) in new[]
        {
            ("Pack pointer package", "dotnet pack src/DotnetInspect.Cli -c Release -p:SelfContained=true -p:OfficialBuild=true -p:CreateRidSpecificToolPackages=false -p:DotnetInspectWebsiteUrl=https://dotnet-inspect.net"),
            ("Pack any (non-AOT) package", "dotnet pack src/DotnetInspect.Cli -c Release -r any -p:PublishAot=false -p:OfficialBuild=true -p:DotnetInspectWebsiteUrl=https://dotnet-inspect.net"),
            ("Pack linux-x64 AOT package", "dotnet pack src/DotnetInspect.Cli -c Release -r linux-x64 -p:OfficialAotBuild=true -p:DotnetInspectWebsiteUrl=https://dotnet-inspect.net"),
            ("Install tool from local packages", "dotnet tool install --global dotnet-inspect --source ./artifacts/package/release --no-cache"),
            ("Smoke test - version", "dotnet-inspect --version"),
            ("Smoke test - platform library", "dotnet-inspect library System.Private.CoreLib"),
        })
        {
            YamlMappingNode[] matches = testSteps.Children
                .Select(node => RequireMapping(node, "Deep Inspect test step"))
                .Where(step => GetOptionalScalar(step, "name") == name)
                .ToArray();
            if (matches.Length != 1 || !GetRequiredScalar(
                    matches[0],
                    "run",
                    $"Deep Inspect {name}").Contains(command, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Daily Deep Inspect must run {name} once.");
        }
        YamlMappingNode inspectWebJob =
            GetRequiredMapping(jobs, "inspect-web", "Deep Inspect jobs");
        RequireContains(
            GetRequiredScalar(inspectWebJob, "if", "Deep Inspect inspect-web lane"),
            "github.event_name == 'schedule' && github.event.schedule == '0 13 * * *'");
        YamlSequenceNode inspectWebSteps = GetRequiredSequence(
            inspectWebJob,
            "steps",
            "Deep Inspect inspect-web lane");
        foreach ((string name, string command) in new[]
        {
            ("Build browser frontend", "npm run build:generated"),
            ("Test browser frontend", "node --test"),
            ("Test annotated-source viewer", "npm test"),
            ("Test browser engine", "dotnet run --project tests/DotnetInspect.Web.Tests -c Release"),
            ("Check complete generated facade contract", "eng/generate-inspect-web-engine-facade.sh --check"),
            ("Test complete shared-runtime multi-facade canary", "eng/test-inspect-web-multi-facade-canary.sh"),
            ("Test complete managed-operation bridge canary", "eng/test-inspect-web-managed-operation-bridge-canary.sh"),
            ("Typecheck generated ts-jsexport facade", "eng/test-ts-jsexport-typescript.sh"),
            ("Test browser UI in Firefox", "npm run test:browser:generated"),
            ("Publish browser application", "src/DotnetInspect.Web/DotnetInspect.Web.csproj"),
            ("Publish Inspect Web managed API", "src/MsdlProxy/MsdlProxy.csproj"),
            ("Verify published site artifact", "eng/verify-inspect-web-site-artifact.sh"),
            ("Test published browser application", "eng/test-inspect-web-published-application.sh"),
        })
        {
            YamlMappingNode[] matches = inspectWebSteps.Children
                .Select(node => RequireMapping(node, "Deep Inspect inspect-web step"))
                .Where(step => GetOptionalScalar(step, "name") == name)
                .ToArray();
            if (matches.Length != 1 || !GetRequiredScalar(
                    matches[0],
                    "run",
                    $"Deep Inspect {name}").Contains(
                        command,
                        StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Deep Inspect inspect-web must run {name} once.");
            }
        }
        YamlMappingNode platformTest =
            GetRequiredMapping(jobs, "platform-test", "Deep Inspect jobs");
        RequireScalarValue(
            platformTest,
            "timeout-minutes",
            "120",
            "Deep Inspect platform-test");
        string[] requiredJobs =
        [
            "test",
            "platform-test",
            "decompiler-corpus",
            "release-certification",
            "inspect-web",
            "census",
        ];
        foreach (string jobName in requiredJobs)
        {
            string condition = GetRequiredScalar(
                GetRequiredMapping(jobs, jobName, "Deep Inspect jobs"),
                "if",
                $"Deep Inspect job {jobName}");
            RequireContains(condition, "inputs.lane == 'release-candidate'");
        }
    }

    private static YamlMappingNode LoadRoot(string workflow, string context)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
            throw new InvalidOperationException($"Expected one {context} document.");
        return RequireMapping(yaml.Documents[0].RootNode, $"{context} root");
    }

    private static YamlMappingNode FindStep(YamlMappingNode job, string name)
    {
        YamlSequenceNode steps = GetRequiredSequence(job, "steps", $"job for {name}");
        foreach (YamlNode node in steps.Children)
        {
            YamlMappingNode step = RequireMapping(node, $"step {name}");
            if (GetOptionalScalar(step, "name") == name)
                return step;
        }

        throw new InvalidOperationException($"Missing step '{name}'.");
    }

    private static void RequireContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected content '{expected}'.");
    }

    private static void RequireSequenceValues(
        YamlSequenceNode sequence,
        IReadOnlyList<string> expected,
        string context)
    {
        string[] actual = sequence.Children
            .Select(node => RequireScalar(node, context))
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{context} expected [{string.Join(", ", expected)}], " +
                $"found [{string.Join(", ", actual)}].");
        }
    }

    private static void AssertMutationRejected(
        string original,
        string oldValue,
        string newValue,
        Action<string> validate,
        string message)
    {
        string mutated = ReplaceExactlyOnce(original, oldValue, newValue, message);
        try
        {
            validate(mutated);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
