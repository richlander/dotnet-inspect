import { readdirSync } from "node:fs";
import {
  join,
  relative,
  resolve,
  sep,
} from "node:path";

export const typeScriptSourceExtensions = [".ts", ".mts", ".cts", ".tsx"] as const;
export const javaScriptSourceExtensions = [".js", ".mjs", ".cjs", ".jsx"] as const;

export function projectRelative(root: string, file: string): string {
  return relative(root, file).split(sep).join("/");
}

export function isGeneratedDirectory(
  directory: string,
  name: string,
  root: string,
  unprunedRoots: readonly string[],
): boolean {
  const [outermost] = projectRelative(root, join(directory, name)).split("/");
  if (outermost !== undefined && unprunedRoots.includes(outermost)) {
    return false;
  }
  if (name === "node_modules") {
    return true;
  }
  if (name === "dist") {
    return resolve(directory) === resolve(root);
  }
  if (name === "bin" || name === "obj") {
    return readdirSync(directory).some(sibling => sibling.endsWith(".csproj"));
  }
  return false;
}

export function projectSourceFiles(
  root: string,
  extensions: readonly string[],
  unprunedRoots: readonly string[],
): string[] {
  const files: string[] = [];
  const walk = (directory: string): void => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const full = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (!isGeneratedDirectory(directory, entry.name, root, unprunedRoots)) {
          walk(full);
        }
      } else if (
        (entry.isFile() || entry.isSymbolicLink())
        && extensions.some(extension =>
          entry.name.toLowerCase().endsWith(extension))
      ) {
        files.push(full);
      }
    }
  };
  walk(root);
  return files;
}
