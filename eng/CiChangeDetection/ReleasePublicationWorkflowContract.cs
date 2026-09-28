using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class ReleasePublicationWorkflowContract
{
    private const string DownloadArtifactAction =
        "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c";
    private const string AzureAction =
        "Azure/static-web-apps-deploy@1a947af9992250f3bc2e68ad0754c0b0c11566c9";

    internal static void AssertMutations(string repository)
    {
        string workflow = File.ReadAllText(
            Path.Combine(repository, ".github", "workflows", "release.yml"));

        Validate(workflow);

        AssertMutationRejected(
            workflow,
            "  cancel-in-progress: false\n",
            "  cancel-in-progress: true\n",
            Validate,
            "Release workflow contract accepted cancellation during publication.");
        AssertMutationRejectedAll(
            workflow,
            "      - name: Revalidate selected candidate\n",
            "      - name: Download selected candidate\n",
            Validate,
            "Release workflow contract accepted download before candidate revalidation.");
        AssertMutationRejectedAll(
            workflow,
            "          artifact-ids: ${{ needs.resolve.outputs.artifact_id }}\n",
            "          name: dotnet-inspect-release-candidate\n",
            Validate,
            "Release workflow contract accepted artifact selection by name.");
        AssertMutationRejectedAll(
            workflow,
            "          digest-mismatch: error\n",
            "",
            Validate,
            "Release workflow contract accepted download without digest enforcement.");
        AssertMutationRejectedAll(
            workflow,
            "      - name: Verify retained candidate bytes\n",
            "      - name: NuGet login (OIDC to temporary API key)\n",
            Validate,
            "Release workflow contract accepted package publication without retained-byte verification.");
        AssertMutationRejected(
            workflow,
            "    needs:\n      - resolve\n      - publish\n",
            "    needs: resolve\n",
            Validate,
            "Release workflow contract accepted production deployment before package publication.");
        AssertMutationRejected(
            workflow,
            "            dotnet run eng/verify-nuget-retry-package.cs -- " +
                "\"${retry_args[@]}\"\n",
            "            true\n",
            Validate,
            "Release workflow contract accepted a retry without package identity verification.");
        AssertMutationRejected(
            workflow,
            "          skip_app_build: true\n",
            "",
            Validate,
            "Release workflow contract accepted an Azure production rebuild.");
    }

    private static void Validate(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
            throw new InvalidOperationException("Expected one release workflow document.");

        YamlMappingNode root =
            RequireMapping(yaml.Documents[0].RootNode, "release workflow root");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "concurrency", "env", "jobs"],
            "release workflow");
        RequireScalarValue(root, "name", "Publish", "release workflow");
        ValidateTrigger(GetRequiredMapping(root, "on", "release workflow"));
        RequireExactScalarValues(
            GetRequiredMapping(root, "concurrency", "release workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "publish",
                ["cancel-in-progress"] = "false",
            },
            "release concurrency");

        YamlMappingNode jobs =
            GetRequiredMapping(root, "jobs", "release workflow");
        RequireExactKeys(jobs, ["resolve", "publish", "deploy"], "release jobs");

        YamlMappingNode resolve =
            GetRequiredMapping(jobs, "resolve", "release jobs");
        RequireScalarValue(
            resolve,
            "name",
            "Validate selected candidate",
            "release resolve");
        RequireExactScalarValues(
            GetRequiredMapping(resolve, "outputs", "release resolve"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sha"] = "${{ steps.candidate.outputs.sha }}",
                ["run_attempt"] = "${{ steps.candidate.outputs.run_attempt }}",
                ["artifact_id"] = "${{ steps.candidate.outputs.artifact_id }}",
                ["artifact_digest"] = "${{ steps.candidate.outputs.artifact_digest }}",
                ["concerns_accepted"] =
                    "${{ steps.candidate.outputs.concerns_accepted }}",
            },
            "release resolve outputs");
        string initialValidation = GetRequiredScalar(
            FindStep(resolve, "Validate candidate identity and evidence"),
            "run",
            "initial candidate validation");
        RequireContains(initialValidation, "eng/validate-release-candidate.sh");
        RequireContains(initialValidation, "\"$GITHUB_OUTPUT\"");

        YamlMappingNode publish =
            GetRequiredMapping(jobs, "publish", "release jobs");
        RequireScalarValue(publish, "needs", "resolve", "release publish");
        RequireScalarValue(publish, "environment", "nuget", "release publish");
        ValidateConsumerSteps(
            GetRequiredSequence(publish, "steps", "release publish"),
            "release publish",
            publishing: true);

        YamlMappingNode deploy =
            GetRequiredMapping(jobs, "deploy", "release jobs");
        RequireSequenceValues(
            GetRequiredSequence(deploy, "needs", "release deploy"),
            ["resolve", "publish"],
            "release deploy needs");
        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "release deploy");
        RequireExactScalarValues(
            environment,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-production-promotion",
                ["url"] = "https://dotnet-inspect.net",
            },
            "release deploy environment");
        ValidateConsumerSteps(
            GetRequiredSequence(deploy, "steps", "release deploy"),
            "release deploy",
            publishing: false);
    }

    private static void ValidateTrigger(YamlMappingNode trigger)
    {
        RequireExactKeys(trigger, ["workflow_dispatch"], "release trigger");
        YamlMappingNode dispatch =
            GetRequiredMapping(trigger, "workflow_dispatch", "release trigger");
        RequireExactKeys(dispatch, ["inputs"], "release workflow_dispatch");
        YamlMappingNode inputs =
            GetRequiredMapping(dispatch, "inputs", "release workflow_dispatch");
        RequireExactKeys(
            inputs,
            [
                "candidate_run_id",
                "candidate_attempt",
                "accept_certification_concerns",
                "confirm",
            ],
            "release inputs");
        RequireScalarValue(
            GetRequiredMapping(inputs, "candidate_run_id", "release inputs"),
            "required",
            "true",
            "candidate run input");
        RequireScalarValue(
            GetRequiredMapping(inputs, "candidate_attempt", "release inputs"),
            "required",
            "true",
            "candidate attempt input");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "accept_certification_concerns",
                "release inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] =
                    "Accept every disclosed completed non-success certification outcome",
                ["required"] = "true",
                ["default"] = "false",
                ["type"] = "boolean",
            },
            "candidate concern input");
        RequireScalarValue(
            GetRequiredMapping(inputs, "confirm", "release inputs"),
            "required",
            "true",
            "release confirmation input");
    }

    private static void ValidateConsumerSteps(
        YamlSequenceNode steps,
        string context,
        bool publishing)
    {
        int revalidateIndex = FindStepIndex(steps, "Revalidate selected candidate");
        int downloadIndex = FindStepIndex(steps, "Download selected candidate");
        int verifyIndex = FindStepIndex(steps, "Verify retained candidate bytes");
        if (!(revalidateIndex < downloadIndex && downloadIndex < verifyIndex))
        {
            throw new InvalidOperationException(
                $"{context} must revalidate, download, then verify the candidate.");
        }

        YamlMappingNode revalidate =
            RequireMapping(steps.Children[revalidateIndex], $"{context} revalidation");
        string revalidationCommand =
            GetRequiredScalar(revalidate, "run", $"{context} revalidation");
        RequireContains(revalidationCommand, "eng/validate-release-candidate.sh");
        RequireContains(revalidationCommand, "\"$EXPECTED_ARTIFACT_ID\"");
        RequireContains(revalidationCommand, "\"$EXPECTED_DIGEST\"");
        RequireContains(revalidationCommand, "\"$EXPECTED_CONCERNS_ACCEPTED\"");

        YamlMappingNode download =
            RequireMapping(steps.Children[downloadIndex], $"{context} download");
        RequireScalarValue(
            download,
            "uses",
            DownloadArtifactAction,
            $"{context} download");
        RequireExactScalarValues(
            GetRequiredMapping(download, "with", $"{context} download"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["artifact-ids"] = "${{ needs.resolve.outputs.artifact_id }}",
                ["github-token"] = "${{ secrets.GITHUB_TOKEN }}",
                ["repository"] = "${{ github.repository }}",
                ["run-id"] = "${{ inputs.candidate_run_id }}",
                ["path"] = "artifacts/release-candidate",
                ["digest-mismatch"] = "error",
            },
            $"{context} download inputs");

        YamlMappingNode verify =
            RequireMapping(steps.Children[verifyIndex], $"{context} verification");
        string verificationCommand =
            GetRequiredScalar(verify, "run", $"{context} verification");
        RequireContains(
            verificationCommand,
            "eng/verify-release-candidate-artifact.sh");
        RequireContains(verificationCommand, "\"$EXPECTED_DIGEST\"");

        if (publishing)
        {
            int retryIndex = FindStepIndex(steps, "Verify NuGet retry identity");
            YamlMappingNode retry =
                RequireMapping(steps.Children[retryIndex], "NuGet retry verification");
            string retryCommand =
                GetRequiredScalar(retry, "run", "NuGet retry verification");
            RequireContains(
                retryCommand,
                "https://api.nuget.org/v3-flatcontainer/");
            RequireContains(
                retryCommand,
                "dotnet run eng/verify-nuget-retry-package.cs -- " +
                    "\"${retry_args[@]}\"");
            int publishIndex = FindStepIndex(steps, "Publish retained packages");
            int releaseIndex =
                FindStepIndex(steps, "Create GitHub release from retained packages");
            if (!(verifyIndex < retryIndex &&
                  retryIndex < publishIndex &&
                  publishIndex < releaseIndex))
            {
                throw new InvalidOperationException(
                    "Candidate and retry identity verification must precede package " +
                    "and GitHub publication.");
            }
            YamlMappingNode release =
                RequireMapping(steps.Children[releaseIndex], "GitHub release");
            string releaseCommand =
                GetRequiredScalar(release, "run", "GitHub release");
            RequireContains(releaseCommand, ".target_commitish");
            RequireContains(releaseCommand, "--target \"$EXPECTED_SHA\"");
            RequireContains(releaseCommand, "gh release upload");
            return;
        }

        int deployIndex = FindStepIndex(steps, "Deploy to production");
        if (verifyIndex >= deployIndex)
        {
            throw new InvalidOperationException(
                "Site verification must precede production deployment.");
        }
        YamlMappingNode deploy =
            RequireMapping(steps.Children[deployIndex], "production deployment");
        RequireScalarValue(
            deploy,
            "uses",
            AzureAction,
            "production deployment");
        RequireExactScalarValues(
            GetRequiredMapping(deploy, "with", "production deployment"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["azure_static_web_apps_api_token"] =
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_PRODUCTION }}",
                ["action"] = "upload",
                ["app_location"] =
                    "artifacts/release-candidate/site/wwwroot",
                ["api_location"] =
                    "artifacts/release-candidate/site/api",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
                ["skip_api_build"] = "true",
            },
            "production deployment inputs");
    }

    private static YamlMappingNode FindStep(YamlMappingNode job, string name)
    {
        YamlSequenceNode steps = GetRequiredSequence(job, "steps", $"job for {name}");
        return RequireMapping(steps.Children[FindStepIndex(steps, name)], $"step {name}");
    }

    private static int FindStepIndex(YamlSequenceNode steps, string name)
    {
        for (int index = 0; index < steps.Children.Count; index++)
        {
            YamlMappingNode step =
                RequireMapping(steps.Children[index], $"step {name}");
            if (GetOptionalScalar(step, "name") == name)
                return index;
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

    private static void AssertMutationRejectedAll(
        string original,
        string oldValue,
        string newValue,
        Action<string> validate,
        string message)
    {
        string mutated = original.Replace(oldValue, newValue, StringComparison.Ordinal);
        if (mutated == original)
            throw new InvalidOperationException($"Mutation did not apply: {message}");
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
