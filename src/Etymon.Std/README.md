# Etymon.Std

An F#-idiomatic layer over the base-library APIs you reach for constantly, part of
the [Etymon](https://github.com/CaelusMinds/Etymon) suite.

**Depends on nothing but `FSharp.Core`**, including no other Etymon package. Take
it on its own if that is all you want.

The rule throughout: something that is absent returns `option`, something that can
fail for a reason worth branching on returns `Result`, and neither returns `null`
or throws.

## Parsing, with the culture spelled out

```fsharp
Parse.int "42"              // Some 42
Parse.decimal "1.50"        // Some 1.50m  -- scale preserved
Parse.bool "YES"            // Some true
Parse.guid "not-a-guid"     // None
Parse.dateOnly "2026-09-20" // Some ...
```

The plain names use the **invariant culture**, because a value that came off a
wire, out of a config file or from a command line has no culture and must not
acquire the machine's by accident. The `In` variants take one, for text a person
typed:

```fsharp
Parse.decimal "1,50"                          // None
Parse.decimalIn (CultureInfo.GetCultureInfo "de-DE") "1,50"  // Some 1.50m
```

That first line matters more than it looks. `Decimal.TryParse` with
`NumberStyles.Number` — the obvious thing to write — parses `"1,5"` as **15**
under the invariant culture, because the comma is a group separator there. A
price becomes ten times a price and nothing complains. The invariant parsers here
disallow grouping for exactly that reason.

Two other sharp edges are handled: `Parse.float` rejects `NaN` and the
infinities, and `Parse.enum` rejects a number with no matching member, which is
how `(DayOfWeek)99` normally gets into a domain.

## Strings

```fsharp
Str.toOption "  "                       // None -- blank is absent
Str.split ',' "a, b ,, c"               // [ "a"; "b"; "c" ]
Str.splitFirst '=' "KEY=a=b"            // Some ("KEY", "a=b")
Str.equalsIgnoreCase "Content-Type" "content-type"  // true
Str.truncate 8 "a long sentence"        // "a long …"
```

Every comparison is **ordinal** unless its name says otherwise, because
culture-aware comparison is for sorting prose and is almost never right for a
header name, a config key or an identifier. Nothing here throws on `null`.

Named `Str`, not `String`, so it never shadows FSharp.Core's `String` module —
`String.concat` keeps working.

## Dictionaries

```fsharp
Dict.tryFind "accept" headers                    // Some "application/json"
Dict.tryPick Parse.int "retries" settings        // Some 3
Dict.ofListIgnoreCase [ "Content-Type", "json" ] // the right comparer for headers
```

Written over `IDictionary` and `IReadOnlyDictionary`, so they work on
`Dictionary`, `ConcurrentDictionary` and anything else implementing them.

## Environment

```fsharp
Env.tryGet "PATH"            // Some "..."
Env.tryGetInt "PORT"         // Some 8080
Env.isEnabled "ETYMON_DEBUG" // false when unset -- the shape of a feature switch
```

Unset and set-but-blank are both absent by default, because an empty connection
string is as useless as a missing one. `Env.tryGetAllowEmpty` is there for the
rare case that genuinely cares.

## File IO with a typed error

```fsharp
match File.tryReadAllText "appsettings.json" with
| Ok contents -> ...
| Error (FileError.NotFound path) -> ...      // create a default
| Error (FileError.AccessDenied path) -> ...  // tell the operator
| Error e -> eprintfn "%s" (FileError.describe e)
```

`FileError` names the cases a caller can actually do something about — `NotFound`,
`DirectoryNotFound`, `AccessDenied`, `InUse`, `InvalidPath`, `NotValidText` — and
puts everything else in `IoFailure`. Nobody should have to match on a message.

Two deliberate behaviours: writing creates the containing directory, since not
doing so only ever produces a failure the caller would fix by creating it; and
deleting something already absent succeeds, because the caller's intent has been
met either way. Text is UTF-8 **without a byte order mark**.

`Dir` is the same for directories. Listings are sorted, so anything generated
from one is reproducible.

## Non-goals

This package is deliberately bounded. It will not grow:

- **Collection combinators.** `List`, `Seq`, `Array` and `Map` are already good.
- **`Option` and `Result` combinators.** Those belong in `Etymon.Core`, or in
  FsToolkit if you want the full set.
- **Custom operators.** No `>>=`, no `<!>`, nothing you have to learn.
- **HTTP, JSON, serialisation, logging, DI, process control.** Not a framework.
- **Async or task helpers.** That is a design space of its own, and a large one.
- **Wrappers for APIs that are already fine from F#.** If calling it directly
  reads well, it does not belong here.

The list exists so that "no" is a cheap answer to a feature request, which is the
only thing that keeps a utility package from becoming everything.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
