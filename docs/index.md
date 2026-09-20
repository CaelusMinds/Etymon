# Etymon

**Define the type once. Derive everything else.**

Etymon is a suite of F# libraries built on one idea: a single schema definition
should be the source of truth for JSON encoding, decoding, validation,
documentation, test data and database structure — rather than five hand-written
things you keep in sync by remembering to.

## Where to start

- **[Etymon.Core](reference/etymon-core.html)** — the foundation. Constrained
  types, an error model that accumulates and carries field paths, and the
  constraint vocabulary every other package reads.

The remaining packages are being built in phases; this page will grow as they
land. See [the repository README](https://github.com/CaelusMinds/Etymon) for the
full plan.

## The commitments

Everything in Etymon follows from these.

**Expected failures are values.** Anything that fails for an ordinary reason
returns `Validation<'T>`. Exceptions mean a bug, not a rejected input.

**Errors accumulate.** Four bad fields produce four errors, each with a path like
`address.zip` or `items[3].name`. The only combinator that stops early is `bind`,
because a dependent step has no value to work with.

**Explicit beats derived.** Combinators are the primary path: no reflection,
friendly to trimming and native AOT. Reflection-based derivation exists as a
labelled convenience, never as the default.

**Constraints are data.** One vocabulary, defined in Core, read by every package
that derives anything from it. A rule Etymon cannot inspect is `Opaque` —
enforced and documented, but never half-derived into something that looks right
and is not.

**Ambiguity is an error, not a guess.** Where a derivation would have to choose
between reasonable options, Etymon returns an error naming the choice rather than
picking a default you would discover in production.
