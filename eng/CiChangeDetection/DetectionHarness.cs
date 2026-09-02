using System.Diagnostics;

namespace CiChangeDetection;

internal sealed class DetectionHarness(
    string repository,
    string body,
    IReadOnlyCollection<string> expectedOutputs)
{
    private const string Before =
        "1111111111111111111111111111111111111111";
    private const string Sha =
        "2222222222222222222222222222222222222222";

    internal Dictionary<string, string> Run(DetectionScenario scenario)
    {
        string temporary = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-ci-change-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string output = Path.Combine(temporary, "github-output");
            string standardOutputPath = Path.Combine(temporary, "stdout");
            string standardErrorPath = Path.Combine(temporary, "stderr");
            string binaries = Path.Combine(temporary, "bin");
            Directory.CreateDirectory(binaries);

            WriteExecutable(Path.Combine(binaries, "gh"), FakeGh);
            WriteExecutable(Path.Combine(binaries, "git"), FakeGit);
            if (scenario.FailDecodeAt > 0)
            {
                WriteExecutable(
                    Path.Combine(binaries, "base64"),
                    FakeBase64);
            }

            ProcessStartInfo startInfo = new("bash")
            {
                UseShellExecute = false,
                WorkingDirectory = repository,
            };
            startInfo.ArgumentList.Add("--noprofile");
            startInfo.ArgumentList.Add("--norc");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                "exec bash --noprofile --norc -e -o pipefail -c " +
                "\"$1\" >\"$2\" 2>\"$3\"");
            startInfo.ArgumentList.Add("change-detection-wrapper");
            startInfo.ArgumentList.Add(body);
            startInfo.ArgumentList.Add(standardOutputPath);
            startInfo.ArgumentList.Add(standardErrorPath);
            startInfo.Environment["BASH_ENV"] = "";
            startInfo.Environment["CHANGED_FILES"] = scenario.Files;
            startInfo.Environment["CI_BEFORE_SHA"] = Before;
            startInfo.Environment["CI_PR_NUMBER"] =
                scenario.EventName == "pull_request" ? "3704" : "";
            startInfo.Environment["CHANGED_FILE_COUNT_IS_STRING"] =
                scenario.ChangedFileCountIsString
                    .ToString()
                    .ToLowerInvariant();
            startInfo.Environment["EXPECTED_BEFORE"] = Before;
            startInfo.Environment["EXPECTED_SHA"] = Sha;
            startInfo.Environment["EMPTY_PUSH_RECORD"] =
                scenario.EmptyPushRecord.ToString().ToLowerInvariant();
            startInfo.Environment["FILE_STATUS"] = scenario.FileStatus;
            startInfo.Environment["GITHUB_EVENT_NAME"] =
                scenario.EventName;
            startInfo.Environment["GITHUB_OUTPUT"] = output;
            startInfo.Environment["GITHUB_REPOSITORY"] =
                "richlander/dotnet-inspect";
            startInfo.Environment["GITHUB_SHA"] = Sha;
            startInfo.Environment["BASE64_DECODE_COUNT"] =
                Path.Combine(temporary, "base64-decode-count");
            startInfo.Environment["FAIL_DECODE_AT"] =
                scenario.FailDecodeAt.ToString();
            startInfo.Environment["MALFORMED_FILE_RECORD_JSON"] =
                scenario.MalformedFileRecordJson;
            startInfo.Environment["OBJECT_SHAPED_FILE_PAGE"] =
                scenario.ObjectShapedFilePage
                    .ToString()
                    .ToLowerInvariant();
            startInfo.Environment["NUL_FILE_RECORD"] =
                scenario.NulFileRecord.ToString().ToLowerInvariant();
            startInfo.Environment["NUL_PREVIOUS_FILE_RECORD"] =
                scenario.NulPreviousFileRecord
                    .ToString()
                    .ToLowerInvariant();
            startInfo.Environment["PATH"] =
                $"{binaries}{Path.PathSeparator}" +
                $"{startInfo.Environment["PATH"]}";
            startInfo.Environment["PREVIOUS_FILES"] =
                scenario.PreviousFiles;
            startInfo.Environment["REPORTED_CHANGED_FILE_COUNT"] =
                scenario.ReportedChangedFileCount ?? "";
            startInfo.Environment["RESOLUTION_SUCCEEDS"] =
                scenario.ResolutionSucceeds
                    .ToString()
                    .ToLowerInvariant();
            startInfo.Environment["TRUNCATE_RECORD_STREAM"] =
                scenario.TruncateRecordStream
                    .ToString()
                    .ToLowerInvariant();
            startInfo.Environment["TRUNCATE_PUSH_STREAM"] =
                scenario.TruncatePushStream
                    .ToString()
                    .ToLowerInvariant();
            startInfo.Environment["TLA_CANDIDATE_FILES"] =
                scenario.TlaCandidateFiles
                    ?? JoinPathLists(
                        scenario.PreviousFiles,
                        scenario.Files);
            startInfo.Environment["TLA_CANDIDATE_RESOLUTION_SUCCEEDS"] =
                scenario.TlaCandidateResolutionSucceeds
                    .ToString()
                    .ToLowerInvariant();

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start Bash.");
            bool timedOut = !process.WaitForExit(milliseconds: 30_000);
            if (timedOut)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            string standardOutput = ReadIfExists(standardOutputPath);
            string standardError = ReadIfExists(standardErrorPath);
            if (timedOut)
            {
                throw new InvalidOperationException(
                    "Change detection did not exit within 30 seconds.\n" +
                    $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Change detection exited {process.ExitCode}\n" +
                    $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
            }

            Dictionary<string, string> values =
                new(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(output))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0 ||
                    !values.TryAdd(
                        line[..separator],
                        line[(separator + 1)..]))
                {
                    throw new InvalidOperationException(
                        $"Invalid or duplicate workflow output: {line}");
                }
            }

            if (!values.Keys
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedOutputs))
            {
                throw new InvalidOperationException(
                    $"Expected outputs " +
                    $"[{string.Join(", ", expectedOutputs)}], got " +
                    $"{GateAssertions.FormatValues(values)}.");
            }

            return values;
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string JoinPathLists(string first, string second)
    {
        if (first.Length == 0)
        {
            return second;
        }

        return second.Length == 0
            ? first
            : $"{first}\n{second}";
    }

    private static string ReadIfExists(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : "";

    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    private const string FakeGh = """
        #!/bin/sh
        COUNT_JQ='if ((.changed_files | type) == "number") and (.changed_files >= 0) and (.changed_files == (.changed_files | floor)) then (.changed_files | tostring) else error("invalid changed_files") end'
        if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
          exit 1
        fi
        if [ "$#" -eq 4 ] && [ "$1" = "api" ] \
           && [ "$2" = "repos/richlander/dotnet-inspect/pulls/3704" ] \
           && [ "$3" = "--jq" ] && [ "$4" = "$COUNT_JQ" ]; then
          if [ -n "$REPORTED_CHANGED_FILE_COUNT" ]; then
          count=$REPORTED_CHANGED_FILE_COUNT
          elif [ -n "$MALFORMED_FILE_RECORD_JSON" ]; then
          count=1
          elif [ "$NUL_FILE_RECORD" = "true" ]; then
          count=1
          elif [ "$NUL_PREVIOUS_FILE_RECORD" = "true" ]; then
          count=1
          elif [ -n "$PREVIOUS_FILES" ]; then
          count=1
          elif [ -z "$CHANGED_FILES" ]; then
          count=0
          else
          count=0
          while IFS= read -r file; do
            count=$((count + 1))
          done <<EOF
        $CHANGED_FILES
        EOF
          fi
          if [ "$CHANGED_FILE_COUNT_IS_STRING" = "true" ]; then
            jq -cn --arg count "$count" '{changed_files: $count}' |
              jq -r "$4"
          else
            printf '{"changed_files":%s}\n' "$count" |
              jq -r "$4"
          fi
          exit $?
        fi
        if [ "$#" -eq 5 ] && [ "$1" = "api" ] && [ "$2" = "--paginate" ] \
           && [ "$3" = "repos/richlander/dotnet-inspect/pulls/3704/files" ] \
           && [ "$4" = "--jq" ]; then
          records=$(
            if [ "$OBJECT_SHAPED_FILE_PAGE" = "true" ]; then
              printf '%s\n' \
                '{"a":{"status":"modified","filename":"README.md"}}'
            elif [ -n "$MALFORMED_FILE_RECORD_JSON" ]; then
              printf '%s\n' "$MALFORMED_FILE_RECORD_JSON"
            elif [ "$NUL_FILE_RECORD" = "true" ]; then
              printf '%s\n' \
                '[{"status":"modified","filename":"s\u0000rc/Program.cs"}]'
            elif [ "$NUL_PREVIOUS_FILE_RECORD" = "true" ]; then
              printf '%s\n' \
                '[{"status":"renamed","previous_filename":"sr\u0000c/Program.cs","filename":"notes/payload.bin"}]'
            elif [ -n "$PREVIOUS_FILES" ]; then
              jq -cn \
                --arg previous "$PREVIOUS_FILES" \
                --arg filename "$CHANGED_FILES" \
                '[{
                  status: "renamed",
                  previous_filename: $previous,
                  filename: $filename
                }]'
            elif [ -n "$CHANGED_FILES" ]; then
              printf '%s\n' "$CHANGED_FILES" |
                jq -Rsc --arg status "$FILE_STATUS" '
                  split("\n")
                  | map(
                      select(length > 0)
                      | {status: $status, filename: .}
                    )
                '
            else
              printf '[]\n'
            fi
          ) || exit 1
          rendered=$(printf '%s\n' "$records" | jq -r "$5") || exit 1
          if [ "$TRUNCATE_RECORD_STREAM" = "true" ]; then
            printf '%s' "${rendered%????}"
          else
            printf '%s\n' "$rendered"
          fi
          exit 0
        fi
        echo "unexpected gh invocation: $*" >&2
        exit 64
        """;

    private const string FakeGit = """
        #!/bin/sh
        if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
          exit 1
        fi
        if [ "$#" -eq 3 ] && [ "$1" = "cat-file" ] && [ "$2" = "-e" ] \
           && [ "$3" = "${EXPECTED_BEFORE}^{commit}" ]; then
          if [ "$GITHUB_EVENT_NAME" = "pull_request" ] \
             && [ "$TLA_CANDIDATE_RESOLUTION_SUCCEEDS" != "true" ]; then
            exit 1
          fi
          exit 0
        fi
        if [ "$#" -eq 7 ] && [ "$1" = "diff" ] && [ "$2" = "--no-renames" ] \
           && [ "$3" = "--name-only" ] && [ "$4" = "-z" ] \
           && [ "$5" = "$EXPECTED_BEFORE" ] && [ "$6" = "HEAD" ] \
           && [ "$7" = "--" ]; then
          if [ "$TLA_CANDIDATE_RESOLUTION_SUCCEEDS" != "true" ]; then
            exit 1
          fi
          if [ -n "$TLA_CANDIDATE_FILES" ]; then
            while IFS= read -r file; do
              printf '%s\0' "$file"
            done <<EOF
        $TLA_CANDIDATE_FILES
        EOF
          fi
          exit 0
        fi
        if [ "$#" -eq 6 ] && [ "$1" = "diff" ] && [ "$2" = "--no-renames" ] \
           && [ "$3" = "--name-only" ] && [ "$4" = "-z" ] \
           && [ "$5" = "$EXPECTED_BEFORE" ] && [ "$6" = "$EXPECTED_SHA" ]; then
          if [ "$EMPTY_PUSH_RECORD" = "true" ]; then
            printf '\0'
          elif [ -n "$CHANGED_FILES" ]; then
            if [ "$TRUNCATE_PUSH_STREAM" = "true" ]; then
              printf '%s' "$CHANGED_FILES"
            else
              while IFS= read -r file; do
                printf '%s\0' "$file"
              done <<EOF
        $CHANGED_FILES
        EOF
            fi
          fi
          exit 0
        fi
        echo "unexpected git invocation: $*" >&2
        exit 64
        """;

    private const string FakeBase64 = """
        #!/bin/sh
        if [ "$1" = "--decode" ]; then
          count=0
          if [ -f "$BASE64_DECODE_COUNT" ]; then
            IFS= read -r count < "$BASE64_DECODE_COUNT"
          fi
          count=$((count + 1))
          printf '%s\n' "$count" > "$BASE64_DECODE_COUNT"
          if [ "$FAIL_DECODE_AT" = "$count" ]; then
            exit 1
          fi
        fi
        exec /usr/bin/base64 "$@"
        """;
}
