# Releasing

Publishing is never automatic and never happens from a developer machine. A
release is a tag; the workflow does the rest, behind a GitHub environment that
can require an approval.

## Once, before the first release

Publishing uses **Trusted Publishing**, so there is no long-lived API key: the
workflow asks GitHub for a short-lived OIDC token, nuget.org validates it
against a policy naming this repository and this workflow, and hands back a key
that expires in an hour. Nothing to store, nothing to rotate, nothing to leak.

1. **Register the policy.** On nuget.org: your username → *Trusted Publishing* →
   add a policy.
   - Repository Owner: `CaelusMinds`
   - Repository: `Etymon`
   - Workflow File: `release.yml` — the file name only, no path
   - Environment: `nuget` — the workflow declares it, so the policy should
     require it
   - Scope the glob to `Etymon*`, allowing new packages and new versions.
2. **Add the username.** Add the secret `NUGET_USER` to the repository, or to
   the `nuget` environment. Its value is the nuget.org profile name of the
   account that **created** the policy — not the account that owns the
   packages. Those differ here: the policy was created by `ssallaj-cm` and the
   packages are owned by the `CaelusMinds` organization, so the value is
   `ssallaj-cm`. Getting it the other way round fails the token exchange with
   HTTP 401 and "No matching trust policy owned by user".

   This is the only stored value, and it is not a credential. Add required
   reviewers on the `nuget` environment if a push should need sign-off.
3. **Reserve the prefix.** After the first push, email `account@nuget.org` with
   the owner display name and the prefixes `Etymon` and `Etymon.*`. Until it is
   reserved, anyone can publish `Etymon.Something` and it will look official.

A policy on a private repository starts *temporarily active* for seven days
until a first publish locks it to the repository and owner IDs. This repository
is public, so that does not apply — but if the policy ever shows as pending,
that is why.

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
*dry-run* left checked. It builds, tests, packs, uploads the `.nupkg` files as
an artifact, and exchanges an OIDC token for a real short-lived NuGet key —
everything except the push. The key exchange is included on purpose: it is the
part most likely to be misconfigured, and a rehearsal that skips it proves only
that the code compiles. The key expires unused within the hour.

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
