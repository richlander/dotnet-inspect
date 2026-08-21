import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const supportedPlatforms = new Set(["darwin", "linux", "win32"]);
const supportedArchitectures = new Set(["arm64", "x64"]);

export function verifyAnalysisHost(platform, architecture) {
  if (
    !supportedPlatforms.has(platform)
    || !supportedArchitectures.has(architecture)
  ) {
    throw new Error(
      "inspect-web analysis requires an x64 or arm64 host running macOS, Linux, "
      + `or Windows; the current host is ${platform}-${architecture}.`,
    );
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  verifyAnalysisHost(process.platform, process.arch);
}
