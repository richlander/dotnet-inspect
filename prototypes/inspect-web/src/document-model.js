// The portable AnnotatedSourceDocument model — validation, coordinates, line derivation,
// segmentation, and fact/target/node resolution — is owned by the annotated-source-viewer
// prototype. The engine project links that exact file over this path in wwwroot, so the deployed
// site loads the owner's module verbatim; this re-export is what the repository tree and the
// Node tests resolve. Do not add logic here.
export * from "../../annotated-source-viewer/src/document-model.js";
