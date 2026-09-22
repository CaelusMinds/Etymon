# Etymon.Config

**Configuration loaded through a schema, with every problem reported at once.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core`, `Etymon.Base` and `Etymon.Schema` — no NuGet package beyond
`FSharp.Core`, and deliberately not `Microsoft.Extensions.Configuration`.

```fsharp
Config.load settingsSchema [
    Source.jsonFile "appsettings.json"
    Source.optionalJsonFile $"appsettings.{environmentName}.json"
    Source.environment "APP"
]
```

Sources are given in **increasing order of precedence**: the last one holding a
setting wins.

## Every problem, once

Starting up, failing on the first missing setting, being restarted, and failing
on the second is a slow way to learn something that could have been said in one
breath.

```
3 configuration problems:
  database.port: is required, and must be an integer (searched: appsettings.json, environment)
  database.password: is required, and must be a string (searched: appsettings.json, environment). The value is not shown because this setting is sensitive.
  serviceName: is required, and must be a string (searched: appsettings.json, environment)
```

Each line says the path, what was expected, and **where it was looked for**. When
a value was found but was wrong, it says which source supplied it instead:

```
  database.port: expected an integer (from environment)
```

That last detail is the one that saves an afternoon. The configuration bug that
actually costs time is rarely a wrong value — it is the wrong source winning, and
that is invisible until something names it.

A source that cannot be read at all is reported the same way, alongside the
settings it could not supply, rather than throwing before the others have been
looked at.

## Everything is a string, and that is fine

Environment variables, query strings and `.env` files have no types.
`Etymon.Config` decodes in `DecodeMode.Coercing`, so `"8080"` is the port and
`"yes"` is the boolean — the forms that actually occur, not only the two JSON
recognises.

Coercion never makes nonsense acceptable. `"eighty"` is still not a port.

## Secrets

Mark a field `Schema.sensitive` and it is redacted wherever Etymon prints it —
in errors, and in the startup summary. Decode it into `Secret<'T>` from
`Etymon.Core` and it redacts itself everywhere else too, including when somebody
prints the whole settings record:

```fsharp
Schema.required "password"
    (Schema.string |> Schema.sensitive |> Schema.convert Secret.create Secret.reveal)
    (fun d -> d.Password)
```
```fsharp
printfn "%A" settings
// { Database = { Host = "db.internal"; Port = 5432; Password = <redacted> } ... }
```

## Say what you loaded

```fsharp
printfn "%s" (Config.explain settingsSchema sources)
```
```
database.host      db.internal  appsettings.json
database.port      5432         appsettings.json
database.password  <redacted>   environment
serviceName        billing      appsettings.json
retries            (not set)    (not set)
```

Worth printing at startup for the same reason the errors name their source.
`Config.settings` gives the same information as data if you would rather log it
structurally.

## Sources

`Source.environment` (with a required prefix — without one, `PATH` starts meaning
something), `Source.jsonFile`, `Source.optionalJsonFile`, `Source.jsonText`,
`Source.inMemory`, and `Source.custom` for user secrets, a key vault, a command
line or anything else.

A source is one function returning flat key-value entries, so adding one is small.

Two deliberate behaviours around optional files: an **absent** optional file is
not a problem, but one that **exists and does not parse** still is. Silently
ignoring a malformed file is how a deployment runs for a week on defaults nobody
meant.

## How lookup works

Rather than merging every source into one document and hoping, the loader walks
**the schema** to learn which settings exist, then looks each one up in each
source. That is why it can say "`database.port` was not found" instead of waiting
for a decoder to notice, and why it knows what type each setting wanted.

Keys are matched case-insensitively and `__` separates path segments — the same
convention Docker, Kubernetes and `Microsoft.Extensions.Configuration` already
use, so an existing deployment needs no changes.

## Public surface

The complete public surface of this package, every value with its full signature
and its documentation, is in `Surface.fsi` beside this file. It is written by the
build from the implementation, so it is where to learn what a function hands
back without compiling anything.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
