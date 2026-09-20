# Releasing

Publishing is never automatic and never happens from a developer machine. A
release is a tag; the workflow does the rest, behind a GitHub environment that
can require an approval.

## Once, before the first release

1. **Create the API key.** On nuget.org, *API Keys* → *Create*, scoped to
   **Push new packages and package versions**, with the glob pattern `Etymon*`.
   A key scoped to one glob cannot be used to push anything else if it leaks.
2. **Store it.** Repository *Settings* → *Environments* → `nuget` → add the
   secret `NUGET_API_KEY`. Add required reviewers on that environment if a
   second pair of eyes should sign off on a push.
3. **Reserve the prefix.** After the first push, ask nuget.org to reserve the
   `Etymon.` ID prefix for the CaelusMinds account. Without it, anyone can
   publish `Etymon.Something` and it will look official.

## Each release

The tag is the whole version, suffix included. The workflow reads the suffix
from the tag and passes it to `build` and `pack`, so the tag and the package
version cannot disagree.

```bash
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

`VersionPrefix` in `Directory.Build.props` must match the tag's prefix, or the
workflow stops before building.

A stable release is the same thing with no suffix:

```bash
git tag v0.1.0
git push origin v0.1.0
```

To rehearse without publishing, run the **Release** workflow by hand with
*dry-run* left checked. It builds, tests and packs, and uploads the `.nupkg`
files as an artifact without pushing.

## What a version number costs

A version on nuget.org can never be deleted, only unlisted, and the number is
burned either way. Previews are cheap and disposable — `preview.1`, `preview.2`,
as many as it takes. `0.1.0` itself is a one-shot and should not be spent until
the API has survived a real consumer.

The whole suite ships one version number in lockstep, so "which versions go
together" is never a question.

## Consuming a preview

Previews are not restored by default. A consumer either asks for the version
explicitly:

```xml
<PackageReference Include="Etymon" Version="0.1.0-preview.1" />
```

or, with central package management, pins it in `Directory.Packages.props`.

Take `Etymon` for everything that costs nothing but `FSharp.Core`, and add an
adapter only if you are serving HTTP:

| You are writing | Install |
| --- | --- |
| A library or domain model | `Etymon` |
| A Giraffe server | `Etymon` + `Etymon.Api.Giraffe` |
| A minimal-API server | `Etymon` + `Etymon.Api.AspNetCore` |
| Property tests | `Etymon.Invariants.FsCheck` (test project only) |
