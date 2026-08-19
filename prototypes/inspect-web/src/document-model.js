// The portable AnnotatedSourceDocument model — validation, coordinates, line derivation,
// segmentation, and fact/target/node resolution — is owned by the annotated-source-viewer
// prototype. This re-export is the entry point Vite and the Node tests resolve, so the deployed
// bundle and tests use the owner's module without copying its logic. Do not add logic here.
export * from "../../annotated-source-viewer/src/document-model.js";
