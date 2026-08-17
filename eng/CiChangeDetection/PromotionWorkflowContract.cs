using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class PromotionWorkflowContract
{
    private const string AzureAction =
        "Azure/static-web-apps-deploy@4d27395796ac319302594769cfe812bd207490b1";
    private const string IndexCheck =
        "test -f artifacts/inspect-web-publish/wwwroot/index.html";

    internal static void AssertMutations(string repository)
    {
        string promotionPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "promote-inspect-web.yml");
        string stagingPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "deploy-inspect-web.yml");
        string promotionWorkflow = File.ReadAllText(promotionPath);
        string stagingWorkflow = File.ReadAllText(stagingPath);
        ValidatePromotion(promotionWorkflow);
        ValidateStaging(stagingWorkflow);

        const string trustedCheckout =
            """
                steps:
                  - uses: actions/checkout@v6

                  - name: Setup .NET
            """;
        const string candidateCheckout =
            """
                steps:
                  - uses: actions/checkout@v6
                    with:
                      ref: ${{ needs.resolve.outputs.sha }}

                  - name: Setup .NET
            """;
        AssertMutationRejected(
            promotionWorkflow,
            trustedCheckout,
            candidateCheckout,
            ValidatePromotion,
            "Promotion workflow contract accepted candidate-controlled production checkout.");

        const string stagingDownload =
            """
                steps:
                  - name: Download staged site artifact
            """;
        const string stagingCheckout =
            """
                steps:
                  - uses: actions/checkout@v6

                  - name: Download staged site artifact
            """;
        AssertMutationRejected(
            stagingWorkflow,
            stagingDownload,
            stagingCheckout,
            ValidateStaging,
            "Staging workflow contract accepted candidate code in the deployment job.");

        AssertMutationRejected(
            promotionWorkflow,
            "      - name: Setup .NET\n        uses: actions/setup-dotnet@v5",
            "      - name: Setup .NET\n        uses: actions/download-artifact@v8",
            ValidatePromotion,
            "Promotion workflow contract accepted an alternate setup action.");
        AssertMutationRejected(
            promotionWorkflow,
            "            \"$EXPECTED_DIGEST\"\n",
            "            \"$EXPECTED_DIGEST\" || true\n",
            ValidatePromotion,
            "Promotion workflow contract accepted disabled revalidation.");
        AssertMutationRejected(
            promotionWorkflow,
            "      - name: Revalidate staged site\n",
            "      - name: Download staged site artifact\n",
            ValidatePromotion,
            "Promotion workflow contract accepted download before revalidation.");
        AssertMutationRejected(
            stagingWorkflow,
            $"        run: {IndexCheck}\n",
            $"        run: {IndexCheck} || true\n",
            ValidateStaging,
            "Staging workflow contract accepted disabled artifact verification.");
        AssertMutationRejected(
            stagingWorkflow,
            "          skip_app_build: true\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted Azure app build.");
    }

    private static void ValidatePromotion(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one promotion workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "promotion workflow root");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "promotion workflow");
        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "promotion jobs");
        RequireScalarValue(deploy, "needs", "resolve", "jobs.deploy");
        RequireScalarValue(deploy, "runs-on", "ubuntu-26.04", "jobs.deploy");

        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireExactScalarValues(
            environment,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-production",
                ["url"] = "https://dotnet-inspect.net",
            },
            "jobs.deploy.environment");

        YamlSequenceNode steps = GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (steps.Children.Count != 6)
        {
            throw new InvalidOperationException(
                "Production deployment must contain checkout, setup, revalidation, " +
                "artifact download, artifact verification, and deploy steps.");
        }

        YamlMappingNode checkout = RequireStep(steps, 0, null);
        RequireExactKeys(checkout, ["uses"], "jobs.deploy checkout");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v6",
            "jobs.deploy checkout");

        YamlMappingNode setup = RequireStep(steps, 1, "Setup .NET");
        RequireExactKeys(setup, ["name", "uses", "with"], "production setup step");
        RequireScalarValue(
            setup,
            "uses",
            "actions/setup-dotnet@v5",
            "production setup step");
        RequireExactScalarValues(
            GetRequiredMapping(setup, "with", "production setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "11.0.x",
                ["dotnet-quality"] = "preview",
            },
            "production setup step.with");

        YamlMappingNode revalidate =
            RequireStep(steps, 2, "Revalidate staged site");
        RequireExactKeys(
            revalidate,
            ["name", "shell", "env", "run"],
            "revalidation step");
        RequireScalarValue(revalidate, "shell", "bash", "revalidation step");
        RequireExactScalarValues(
            GetRequiredMapping(revalidate, "env", "revalidation step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GH_TOKEN"] = "${{ secrets.GITHUB_TOKEN }}",
                ["STAGING_RUN_ID"] = "${{ inputs.staging_run_id }}",
                ["EXPECTED_SHA"] = "${{ needs.resolve.outputs.sha }}",
                ["EXPECTED_ATTEMPT"] = "${{ needs.resolve.outputs.run_attempt }}",
                ["EXPECTED_ARTIFACT_ID"] =
                    "${{ needs.resolve.outputs.artifact_id }}",
                ["EXPECTED_DIGEST"] =
                    "${{ needs.resolve.outputs.artifact_digest }}",
            },
            "revalidation step.env");
        string revalidationCommand = GetRequiredScalar(
            revalidate,
            "run",
            "revalidation step");
        const string ExpectedRevalidation =
            """
            bash eng/validate-inspect-web-promotion.sh \
              "$STAGING_RUN_ID" \
              720 \
              "$RUNNER_TEMP/revalidated-inspect-web" \
              "$EXPECTED_SHA" \
              "$EXPECTED_ATTEMPT" \
              "$EXPECTED_ARTIFACT_ID" \
              "$EXPECTED_DIGEST"
            """;
        if (revalidationCommand.TrimEnd() != ExpectedRevalidation)
        {
            throw new InvalidOperationException(
                "Production revalidation command does not match the trusted contract.");
        }

        YamlMappingNode download =
            RequireStep(steps, 3, "Download staged site artifact");
        RequireExactKeys(
            download,
            ["name", "uses", "with"],
            "artifact download step");
        RequireScalarValue(
            download,
            "uses",
            "actions/download-artifact@v8",
            "artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "artifact download step");
        RequireExactScalarValues(
            downloadWith,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["artifact-ids"] = "${{ needs.resolve.outputs.artifact_id }}",
                ["github-token"] = "${{ secrets.GITHUB_TOKEN }}",
                ["repository"] = "${{ github.repository }}",
                ["run-id"] = "${{ inputs.staging_run_id }}",
                ["path"] = "artifacts/inspect-web-publish/wwwroot",
                ["digest-mismatch"] = "error",
            },
            "artifact download step.with");

        YamlMappingNode verify =
            RequireStep(steps, 4, "Verify staged site artifact");
        RequireExactKeys(
            verify,
            ["name", "shell", "run"],
            "artifact verification step");
        RequireScalarValue(
            verify,
            "shell",
            "bash",
            "artifact verification step");
        RequireScalarValue(
            verify,
            "run",
            IndexCheck,
            "artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(steps, 5, "Deploy to production");
        RequireExactKeys(
            deployStep,
            ["name", "uses", "with"],
            "production deploy step");
        RequireScalarValue(
            deployStep,
            "uses",
            AzureAction,
            "production deploy step");
        RequireExactScalarValues(
            GetRequiredMapping(deployStep, "with", "production deploy step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["azure_static_web_apps_api_token"] =
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB }}",
                ["action"] = "upload",
                ["app_location"] = "artifacts/inspect-web-publish/wwwroot",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
            },
            "production deploy step.with");
    }

    private static void ValidateStaging(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one staging workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "staging workflow root");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "staging workflow");
        YamlMappingNode build = GetRequiredMapping(jobs, "build", "staging jobs");
        RequireAbsent(build, "environment", "jobs.build");
        RequireAbsent(build, "outputs", "jobs.build");
        YamlSequenceNode buildSteps = GetRequiredSequence(build, "steps", "jobs.build");
        YamlMappingNode upload = RequireNamedStep(
            buildSteps,
            "Upload staged site artifact",
            "jobs.build");
        RequireExactKeys(
            upload,
            ["name", "uses", "with"],
            "staging artifact upload step");
        RequireScalarValue(
            upload,
            "uses",
            "actions/upload-artifact@v7",
            "staging artifact upload step");
        RequireExactScalarValues(
            GetRequiredMapping(upload, "with", "staging artifact upload step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-publish/wwwroot",
                ["if-no-files-found"] = "error",
                ["retention-days"] = "30",
            },
            "staging artifact upload step.with");

        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "staging jobs");
        RequireScalarValue(deploy, "needs", "build", "jobs.deploy");
        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireExactScalarValues(
            environment,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-staging",
                ["url"] = "https://dotnet-inspect.ca",
            },
            "jobs.deploy.environment");
        YamlSequenceNode deploySteps =
            GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (deploySteps.Children.Count != 3)
        {
            throw new InvalidOperationException(
                "Staging deployment must contain only artifact download, " +
                "artifact verification, and deploy steps.");
        }

        YamlMappingNode download =
            RequireStep(deploySteps, 0, "Download staged site artifact");
        RequireExactKeys(
            download,
            ["name", "uses", "with"],
            "staging artifact download step");
        RequireScalarValue(
            download,
            "uses",
            "actions/download-artifact@v8",
            "staging artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "staging artifact download step");
        RequireExactScalarValues(
            downloadWith,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-publish/wwwroot",
                ["digest-mismatch"] = "error",
            },
            "staging artifact download step.with");

        YamlMappingNode verify =
            RequireStep(deploySteps, 1, "Verify staged site artifact");
        RequireExactKeys(
            verify,
            ["name", "shell", "run"],
            "staging artifact verification step");
        RequireScalarValue(
            verify,
            "shell",
            "bash",
            "staging artifact verification step");
        RequireScalarValue(
            verify,
            "run",
            IndexCheck,
            "staging artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(deploySteps, 2, "Deploy to staging");
        RequireExactKeys(
            deployStep,
            ["name", "uses", "with"],
            "staging deploy step");
        RequireScalarValue(
            deployStep,
            "uses",
            AzureAction,
            "staging deploy step");
        RequireExactScalarValues(
            GetRequiredMapping(deployStep, "with", "staging deploy step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["azure_static_web_apps_api_token"] =
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_STAGING }}",
                ["action"] = "upload",
                ["app_location"] = "artifacts/inspect-web-publish/wwwroot",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
            },
            "staging deploy step.with");
    }

    private static YamlMappingNode RequireNamedStep(
        YamlSequenceNode steps,
        string name,
        string context)
    {
        YamlMappingNode[] matches = steps.Children
            .Select((node, index) => RequireMapping(node, $"{context} step {index}"))
            .Where(step => GetOptionalScalar(step, "name") == name)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"{context} contains {matches.Length} '{name}' steps; expected one.");
        }
        return matches[0];
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void AssertMutationRejected(
        string workflow,
        string oldValue,
        string newValue,
        Action<string> validate,
        string message)
    {
        string mutated = workflow.Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);
        if (mutated == workflow)
            throw new InvalidOperationException($"Mutation did not apply: {message}");
        ExpectFailure(() => validate(mutated), message);
    }

    private static YamlMappingNode RequireStep(
        YamlSequenceNode steps,
        int index,
        string? name)
    {
        YamlMappingNode step = RequireMapping(
            steps.Children[index],
            $"jobs.deploy step {index}");
        if (name is not null)
            RequireScalarValue(step, "name", name, $"jobs.deploy step {index}");
        return step;
    }
}
