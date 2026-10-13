const markerHeader = "x-dotnet-inspect-package-entry-cache";
const keyPattern = /^[0-9a-f]{64}\/(?:archive|directory|entries\/[0-9a-f]{64})$/;
let configuredName: string | undefined;
let cache: Cache | undefined;

function requireKey(key: string): Request {
  if (!keyPattern.test(key)) {
    throw new Error("The package-entry cache key is invalid.");
  }
  return new Request(
    new URL(`/.dotnet-inspect/package-entry-cache/${key}`, globalThis.location.origin),
  );
}

function requireCache(): Cache {
  if (cache === undefined) {
    throw new Error("The package-entry cache is not configured.");
  }
  return cache;
}

function encode(bytes: Uint8Array): string {
  const chunkSize = 0x8000;
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(
      ...bytes.subarray(offset, offset + chunkSize),
    );
  }
  return btoa(binary);
}

function decode(content: string): Uint8Array<ArrayBuffer> {
  const binary = atob(content);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

export async function configure(cacheName: string): Promise<void> {
  if (typeof cacheName !== "string" || cacheName.length === 0) {
    throw new Error("The package-entry cache name is invalid.");
  }
  if (configuredName !== undefined && configuredName !== cacheName) {
    throw new Error("The package-entry cache is already configured.");
  }
  configuredName = cacheName;
  cache ??= await globalThis.caches.open(cacheName);
}

export async function isPersistent(): Promise<boolean> {
  return typeof globalThis.navigator?.storage?.persisted === "function"
    && await globalThis.navigator.storage.persisted();
}

export async function read(key: string): Promise<string | null> {
  const response = await requireCache().match(requireKey(key));
  if (response === undefined) return null;
  if (!response.ok || response.headers.get(markerHeader) !== "v1") {
    throw new Error("The package-entry cache item is invalid.");
  }
  return encode(new Uint8Array(await response.arrayBuffer()));
}

export async function publish(
  key: string,
  content: string,
): Promise<void> {
  const request = requireKey(key);
  const target = requireCache();
  if (await target.match(request) !== undefined) return;
  const bytes = decode(content);
  await target.put(
    request,
    new Response(bytes, {
      headers: {
        "content-type": "application/octet-stream",
        "content-length": String(bytes.byteLength),
        [markerHeader]: "v1",
      },
    }),
  );
}

export async function archiveLength(key: string): Promise<number> {
  const response = await requireCache().match(requireKey(key));
  if (response === undefined) return -1;
  const length = Number(response.headers.get("content-length"));
  if (!response.ok || response.headers.get(markerHeader) !== "v1"
    || !Number.isSafeInteger(length) || length <= 0 || length > 2147483647) {
    throw new Error("The cached archive length is invalid.");
  }
  return length;
}

export async function readArchive(key: string, expectedLength: number): Promise<string | null> {
  if (!Number.isSafeInteger(expectedLength) || expectedLength <= 0) {
    throw new Error("The cached archive read limit is invalid.");
  }
  const response = await requireCache().match(requireKey(key));
  if (response === undefined) return null;
  if (!response.ok || response.headers.get(markerHeader) !== "v1"
    || Number(response.headers.get("content-length")) !== expectedLength
    || response.body === null) {
    throw new Error("The cached archive is invalid.");
  }
  const bytes = new Uint8Array(expectedLength);
  const reader = response.body.getReader();
  let offset = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      if (offset + value.byteLength > expectedLength) {
        throw new Error("The cached archive exceeds its declared length.");
      }
      bytes.set(value, offset);
      offset += value.byteLength;
    }
    if (offset !== expectedLength) throw new Error("The cached archive is truncated.");
    return encode(bytes);
  } finally {
    void reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}
