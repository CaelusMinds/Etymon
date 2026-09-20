# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Every package in the suite ships the same version number while the suite is 0.x,
so "which versions go together" is never a question. Independent versioning will
be reconsidered at 1.0.

## [Unreleased]

### Added

- **Etymon.Base** — an F#-idiomatic layer over the base library, depending on
  nothing but `FSharp.Core` and on no other Etymon package.
  - `Parse`: culture-explicit parsing returning `option`. The plain names use the
    invariant culture and disallow digit grouping, because `NumberStyles.Number`
    parses `"1,5"` as `15` there. `Parse.float` rejects NaN and the infinities;
    `Parse.enum` rejects a number with no matching member.
  - `Str`: string operations that never throw on `null` and are ordinal unless
    their name says otherwise. Named `Str` so as not to shadow FSharp.Core`s
    `String` module.
  - `Dict`: `option`-returning lookups over `IDictionary` and
    `IReadOnlyDictionary`, plus a case-insensitive constructor.
  - `Env`: environment variables as `option`, treating blank as absent.
  - `File`, `Dir` and `FileError`: file IO returning `Result` with a typed error
    union naming the cases a caller can act on.
- Preview versioning (`VersionSuffix`), and `eng/pack-local.ps1` to pack the
  suite into a local NuGet feed that other solutions can restore from.

- **Etymon.Core** — the foundation of the suite.
  - `Path` and `PathSegment`: a location inside a value, rendering as
    `address.zip` or `items[3].name`, with quoting for keys that are not bare
    identifiers.
  - `Constraint`: one constraint vocabulary for the whole suite, expressed as
    data — lengths, numeric ranges, patterns, named formats, enumerations,
    collection bounds, and `Opaque` for rules that cannot be derived.
  - `ValidationError`, `ValidationErrors` and `ErrorReason`: an error model that
    separates the machine-readable reason from the human-readable message.
  - `Validation<'T>`, an abbreviation for `Result<'T, ValidationErrors>`, with
    accumulating combinators and a `validation { let! … and! … }` computation
    expression.
  - `ConstraintCheck<'T>` and the `Check` module: constraints paired with the
    tests that decide them, including format checks for email, UUID, RFC 3339
    timestamps, dates, times, ISO 8601 durations, URIs, hostnames and IP
    addresses.
  - `Secret<'T>`: a value that redacts itself through `ToString`, `%O`, `%A` and
    `string`, including when nested inside a record.
  - `Refinement<'Raw, 'T>` and the `Refine` module: constrained types in a few
    lines, reporting every rule a value breaks rather than the first.
- Repository scaffolding: central package management, deterministic builds,
  embedded symbols, SourceLink, Fantomas formatting, and dependency rules
  enforced as build errors (`ETY0001`–`ETY0003`).

[Unreleased]: https://github.com/CaelusMinds/Etymon/commits/main
