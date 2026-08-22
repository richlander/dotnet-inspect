import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export const supportedAnalysisHosts = Object.freeze([
  "darwin-arm64",
  "darwin-x64",
  "linux-arm64",
  "linux-x64",
  "win32-arm64",
  "win32-x64",
]);

const supportedAnalysisHostSet = new Set(supportedAnalysisHosts);

export function verifyAnalysisHost(platform, architecture) {
  if (!supportedAnalysisHostSet.has(`${platform}-${architecture}`)) {
    throw new Error(
      "inspect-web analysis requires an x64 or arm64 host running macOS, Linux, "
      + `or Windows; the current host is ${platform}-${architecture}.`,
    );
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  verifyAnalysisHost(process.platform, process.arch);
}
