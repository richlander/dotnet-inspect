const assetUrls = {
  aspire: "/assets/ecosystem-find/aspire.json",
  microsoftExtensions:
    "/assets/ecosystem-find/microsoft-extensions.json",
} as const;

type AssetName = keyof typeof assetUrls;

const assets = new Map<AssetName, Promise<string>>();

async function loadAsset(name: AssetName): Promise<string> {
  let pending = assets.get(name);
  if (pending === undefined) {
    pending = fetch(assetUrls[name]).then(async response => {
      if (!response.ok) {
        throw new Error(
          `Indexed Find asset '${name}' returned HTTP ${String(response.status)}.`,
        );
      }

      const bytes = new Uint8Array(await response.arrayBuffer());
      let binary = "";
      const chunkSize = 0x8000;
      for (let offset = 0; offset < bytes.length; offset += chunkSize) {
        binary += String.fromCharCode(
          ...bytes.subarray(offset, offset + chunkSize),
        );
      }
      return btoa(binary);
    });
    assets.set(name, pending);
  }
  return await pending;
}

export async function loadAspire(): Promise<string> {
  return await loadAsset("aspire");
}

export async function loadMicrosoftExtensions(): Promise<string> {
  return await loadAsset("microsoftExtensions");
}
