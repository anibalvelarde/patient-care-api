namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// Anchor for the access-control matrix this API is implemented against (WP-17).
///
/// The matrix lives in patient-care-super (<c>docs/access-control-matrix.md</c>, human-edited)
/// and is published as a generated manifest (<c>docs/access-control-matrix.json</c>) whose
/// semantic hash covers the normalized (claim → granted-roles) tuples — formatting and
/// timestamps excluded. A vendored copy of the manifest sits in this repo at
/// <c>docs/access-control-matrix.json</c>.
///
/// <see cref="SemanticHash"/> is the drift tripwire: conformance tests assert it equals the
/// vendored manifest's recorded hash AND a hash recomputed from the manifest's tuples; CI
/// (WP-17D) recomputes it from the canonical source. If the matrix changes, regenerate via
/// <c>patient-care-super/tools/generate-access-manifest.sh</c>, re-vendor the manifest, and
/// bump this constant — deliberately, after re-reviewing the permission change.
/// </summary>
public static class AccessControlManifest
{
    /// <summary>First 12 hex chars of SHA-256 over the manifest's canonical grant tuples.</summary>
    public const string SemanticHash = "7ae3aa5e3274";
}
