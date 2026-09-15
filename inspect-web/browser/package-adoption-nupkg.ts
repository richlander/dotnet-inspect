import { Buffer } from "node:buffer";
import { crc32 } from "node:zlib";

// A dependency-free ZIP writer that emits STORED (uncompressed) entries. A
// nupkg is an ordinary ZIP archive; the engine's package reader opens STORED
// archives (the native BrowserEngineBoundaryTests package helpers use
// CompressionLevel.NoCompression), so this reuses that proven shape rather than
// adding a compression dependency. Entries are deterministic: no timestamps and
// no extra fields, so identical inputs yield byte-identical fixtures. The CRC-32
// each entry needs comes from Node's built-in node:zlib, part of the runtime the
// gate already requires, so no hand-rolled table or dependency is involved.

interface ZipEntry {
  readonly name: string;
  readonly bytes: Uint8Array;
}

/**
 * Builds a STORED ZIP archive (a nupkg) from the supplied entries. The output
 * is deterministic for a given set of entries.
 */
export function storedZip(entries: readonly ZipEntry[]): Buffer {
  const locals: Buffer[] = [];
  const centrals: Buffer[] = [];
  let offset = 0;

  for (const entry of entries) {
    const nameBytes = Buffer.from(entry.name, "utf8");
    const data = Buffer.from(entry.bytes);
    const crc = crc32(data) >>> 0;

    const localHeader = Buffer.alloc(30);
    localHeader.writeUInt32LE(0x04_03_4b_50, 0);
    localHeader.writeUInt16LE(20, 4);
    localHeader.writeUInt16LE(0, 6);
    localHeader.writeUInt16LE(0, 8);
    localHeader.writeUInt16LE(0, 10);
    localHeader.writeUInt16LE(0, 12);
    localHeader.writeUInt32LE(crc, 14);
    localHeader.writeUInt32LE(data.length, 18);
    localHeader.writeUInt32LE(data.length, 22);
    localHeader.writeUInt16LE(nameBytes.length, 26);
    localHeader.writeUInt16LE(0, 28);

    const centralHeader = Buffer.alloc(46);
    centralHeader.writeUInt32LE(0x02_01_4b_50, 0);
    centralHeader.writeUInt16LE(20, 4);
    centralHeader.writeUInt16LE(20, 6);
    centralHeader.writeUInt16LE(0, 8);
    centralHeader.writeUInt16LE(0, 10);
    centralHeader.writeUInt16LE(0, 12);
    centralHeader.writeUInt16LE(0, 14);
    centralHeader.writeUInt32LE(crc, 16);
    centralHeader.writeUInt32LE(data.length, 20);
    centralHeader.writeUInt32LE(data.length, 24);
    centralHeader.writeUInt16LE(nameBytes.length, 28);
    centralHeader.writeUInt16LE(0, 30);
    centralHeader.writeUInt16LE(0, 32);
    centralHeader.writeUInt16LE(0, 34);
    centralHeader.writeUInt16LE(0, 36);
    centralHeader.writeUInt32LE(0, 38);
    centralHeader.writeUInt32LE(offset, 42);

    locals.push(localHeader, nameBytes, data);
    centrals.push(centralHeader, nameBytes);
    offset += localHeader.length + nameBytes.length + data.length;
  }

  const centralDirectory = Buffer.concat(centrals);
  const centralOffset = offset;
  const endRecord = Buffer.alloc(22);
  endRecord.writeUInt32LE(0x06_05_4b_50, 0);
  endRecord.writeUInt16LE(0, 4);
  endRecord.writeUInt16LE(0, 6);
  endRecord.writeUInt16LE(entries.length, 8);
  endRecord.writeUInt16LE(entries.length, 10);
  endRecord.writeUInt32LE(centralDirectory.length, 12);
  endRecord.writeUInt32LE(centralOffset, 16);
  endRecord.writeUInt16LE(0, 20);

  return Buffer.concat([...locals, centralDirectory, endRecord]);
}

const fixtureFramework = "net11.0";

/**
 * A single-assembly healthy package: the same valid managed assembly appears in
 * both the reference (compile) and implementation groups. queryPackage yields
 * public types and members with no inspection errors.
 */
export function healthyNupkg(
  assemblyBytes: Uint8Array,
  assemblyFileName: string,
): Buffer {
  return storedZip([
    { name: `ref/${fixtureFramework}/${assemblyFileName}`, bytes: assemblyBytes },
    { name: `lib/${fixtureFramework}/${assemblyFileName}`, bytes: assemblyBytes },
  ]);
}

/** One declared NuGet dependency in a fixture package's root manifest. */
export interface ManifestDependency {
  readonly id: string;
  readonly versionRange: string;
}

// The root manifest of a fixture package, declaring one dependency group for the
// fixture framework. Manifest dependency evidence needs a real manifest: the
// package facade reads this nuspec through PackageDependencyGroupsQuery, so a
// fixture without one carries no dependency groups at all.
function nuspecBytes(
  packageId: string,
  version: string,
  dependency: ManifestDependency,
): Uint8Array {
  return Buffer.from(
    `<?xml version="1.0" encoding="utf-8"?>
<package>
  <metadata>
    <id>${packageId}</id>
    <version>${version}</version>
    <dependencies>
      <group targetFramework="${fixtureFramework}">
        <dependency id="${dependency.id}" version="${dependency.versionRange}" />
      </group>
    </dependencies>
  </metadata>
</package>
`,
    "utf8");
}

/**
 * A healthy package that also carries a root manifest: the same valid managed
 * assembly appears in the reference (compile) and implementation groups, and the
 * manifest declares one dependency group for the fixture framework. Both the
 * manifest dependency groups and the selected compile assembly's direct
 * AssemblyRef rows are therefore real evidence.
 */
export function manifestBackedNupkg(
  assemblyBytes: Uint8Array,
  assemblyFileName: string,
  packageId: string,
  version: string,
  dependency: ManifestDependency,
): Buffer {
  return storedZip([
    { name: `${packageId}.nuspec`, bytes: nuspecBytes(packageId, version, dependency) },
    { name: `ref/${fixtureFramework}/${assemblyFileName}`, bytes: assemblyBytes },
    { name: `lib/${fixtureFramework}/${assemblyFileName}`, bytes: assemblyBytes },
  ]);
}

/**
 * A manifest-only package: an ordinary valid manifest declaring one dependency
 * group, with no reference or implementation assets. This is a real package
 * shape (a dependency-aggregating or documentation package), so its manifest
 * dependency evidence stays healthy while no compile library can be selected
 * and no assembly-reference list can exist.
 */
export function manifestOnlyNupkg(
  packageId: string,
  version: string,
  dependency: ManifestDependency,
): Buffer {
  return storedZip([
    { name: `${packageId}.nuspec`, bytes: nuspecBytes(packageId, version, dependency) },
    {
      name: "README.md",
      bytes: Buffer.from(
        `# ${packageId}\n\nDeclares dependencies and ships no compile assets.\n`,
        "utf8"),
    },
  ]);
}

/**
 * A valid-reference / malformed-implementation package. Two assemblies with
 * distinct identities are both selected participants: the healthy carrier has
 * valid reference and implementation assets, while the broken carrier has a
 * genuinely valid reference assembly beside a malformed (ordinary invalid)
 * implementation. The reference/compile group therefore builds a healthy API
 * surface for both names, so queryPackage returns healthy evidence; only the
 * implementation group carries the malformed bytes, so the analysis facade's
 * package integrations expose the broken carrier's selected rejection.
 */
export function malformedAlongsideHealthyNupkg(
  healthyBytes: Uint8Array,
  healthyFileName: string,
  brokenReferenceBytes: Uint8Array,
  brokenFileName: string,
  malformedImplementationBytes: Uint8Array,
): Buffer {
  return storedZip([
    { name: `ref/${fixtureFramework}/${healthyFileName}`, bytes: healthyBytes },
    { name: `ref/${fixtureFramework}/${brokenFileName}`, bytes: brokenReferenceBytes },
    { name: `lib/${fixtureFramework}/${healthyFileName}`, bytes: healthyBytes },
    { name: `lib/${fixtureFramework}/${brokenFileName}`, bytes: malformedImplementationBytes },
  ]);
}

/**
 * Deterministic invalid DLL bytes. This is not a security probe: it exercises
 * graceful rejection of an assembly the metadata reader cannot open. The "MZ"
 * prefix makes it look like a PE while the remaining bytes are not valid
 * metadata.
 */
export function malformedAssemblyBytes(): Uint8Array {
  const bytes = new Uint8Array(512);
  bytes[0] = 0x4d;
  bytes[1] = 0x5a;
  for (let index = 2; index < bytes.length; index++) {
    bytes[index] = (index * 37 + 11) & 0xff;
  }
  return bytes;
}

/** The lower-cased CDN download path the Gallery source client requests. */
export function galleryDownloadPath(packageId: string, version: string): string {
  return `/packages/${packageId.toLowerCase()}.${version}.nupkg`;
}

export { fixtureFramework };
