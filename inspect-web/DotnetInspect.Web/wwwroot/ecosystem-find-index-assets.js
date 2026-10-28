const assetUrls = Object.freeze({
  aspire: "/assets/ecosystem-find/aspire.json",
  microsoftExtensions:
    "/assets/ecosystem-find/microsoft-extensions.json",
});

const assets = new Map();

async function loadAsset(name) {
  let pending = assets.get(name);
  if (!pending) {
    pending = fetch(assetUrls[name]).then(async response => {
      if (!response.ok) {
        throw new Error(
          `Indexed Find asset '${name}' returned HTTP ${response.status}.`,
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

export async function loadAspire() {
  return await loadAsset("aspire");
}

export async function loadMicrosoftExtensions() {
  return await loadAsset("microsoftExtensions");
}
