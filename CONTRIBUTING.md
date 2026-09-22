# Contributing to Etymon

Thanks for looking. Etymon is early, which means design feedback is worth more
right now than code — if something in the API reads badly to you, an issue saying
so is a genuine contribution.

## Before you start

For anything beyond a typo or an obvious bug fix, **open an issue first**. Etymon
has strong opinions about its dependency graph and its API shape, and it is
kinder to disagree about a design in an issue than in a pull request you have
already written.

## Building

```bash
dotnet tool restore     # Fantomas and fsdocs
dotnet build
dotnet run --project tests/Etymon.Core.Tests
```

Tests use [Expecto](https://github.com/haf/expecto) and run through its own
runner rather than `dotnet test`. The bridge package that would give you
`dotnet test` and IDE Test Explorer integration pins Expecto to an older major
version, and two majors back on FsCheck; the reasoning is in
`tests/Directory.Build.props`. Expecto's runner has filtering and JUnit output,
so CI loses nothing:

```bash
# one test list
dotnet run --project tests/Etymon.Core.Tests -- --filter Etymon.Core.Refine

# JUnit output, as CI does
dotnet run --project tests/Etymon.Core.Tests -- --junit-summary results.xml
```

## The rules the build enforces

You will meet these as build errors rather than review comments.

| Code | Rule |
| --- | --- |
| `ETY0001` | A shipping package that references other projects must declare `<EtymonAllowedReferences>`. |
| `ETY0002` | A shipping package may only reference what it declared. |
| `ETY0003` | A shipping package may not reference a test-only package. |
| `ETY0004` | The framework `Surface.fsi` is written from must be one that is built; otherwise the committed surface would go stale silently. |

If a rule is in your way, the answer may well be that the rule should change —
say so in the pull request rather than setting `EtymonEnforceLayers=false` and
hoping nobody notices. (That switch exists for getting a build out of a broken
tree mid-refactor, not for merging.)

## What a change needs

- **Tests.** Every public function has unit tests covering its error paths, not
  just its happy path. Where a behaviour is a promise — errors accumulating,
  secrets never printing — there is a property test for it.
- **XML doc comments on everything public**, each with a short `<example>`. The
  examples are the documentation most people will actually read.
- **Fantomas formatting.** `dotnet fantomas src tests samples` before you commit;
  CI runs `--check`.
- **A CHANGELOG entry** under `## [Unreleased]`, in
  [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) style.
- **No new warnings.** The build treats warnings as errors, including incomplete
  matches (`FS0025`).

## Writing style

The comments and docs in this repository try to explain *why*, not *what*. A
comment that restates the code is noise; a comment recording the trade-off that
produced the code is why the next person does not undo it. Please match that.

## Design principles

If you are proposing an API, these are the commitments it has to fit:

1. **Expected failures return `Validation<'T>`.** Exceptions mean a bug.
2. **Errors accumulate** and carry a path. Anything that stops at the first
   error needs a reason.
3. **Explicit over derived.** Reflection is opt-in, marked
   `RequiresUnreferencedCode`, and never on the default path.
4. **Constraints are data** in the single `Constraint` vocabulary, so every
   derivation target reads the same rules.
5. **Ambiguity is an error.** Where a derivation would have to guess, it returns
   an error naming the choice instead.
6. **Naming is consistent across packages.** `encode` / `decode` / `toOpenApi`
   mean the same thing everywhere.

## Releasing

Maintainers only. Releases are tagged and published by the release workflow;
nothing is published from a laptop.

## Code of conduct

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
