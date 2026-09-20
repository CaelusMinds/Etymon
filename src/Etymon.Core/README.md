# Etymon.Core

The foundation of the [Etymon](https://github.com/CaelusMinds/Etymon) suite.

**Depends on nothing but `FSharp.Core`.** It trims to roughly 47 KB when only the
error model is used, so it can sit in a Blazor WebAssembly domain layer rather
than stopping at a server boundary.

Take this package alone if you want smart constructors and a proper validation
error model, and nothing else. In particular, the error vocabulary deliberately
does not depend on `Etymon.Schema`: if you hand-write your JSON codecs — because
the bytes already in your database are a contract you cannot let a rename change
— you can adopt `Path` and `ValidationErrors` and keep those codecs exactly as
they are.

## What it gives you

### Errors that accumulate, and know where they came from

```fsharp
open Etymon

validation {
    let! name = NonEmpty.create raw.Name |> Validation.underField "name"
    and! age = Age.create raw.Age |> Validation.underField "age"
    and! zip = Zip.create raw.Zip |> Validation.underField "address" 
    return { Name = name; Age = age; Zip = zip }
}
// Error, all at once:
//   name: must be at least 1 character
//   age: must be between 0 and 130
//   address: must be exactly 5 characters
```

`Validation<'T>` is an abbreviation for `Result<'T, ValidationErrors>`, not a new
type, so it pattern-matches as `Ok` / `Error` and interoperates with everything
that already understands `Result`. Use `and!` to collect every error; a plain
sequence of `let!` uses `bind` and stops at the first, because a dependent step
has no value to work with.

### Constrained types in a few lines

```fsharp
type Email = private Email of string

module Email =
    let refinement =
        Refine.ofString "Email"
        |> Refine.trimmed
        |> Refine.lowercased
        |> Refine.length (Some 3) (Some 254)
        |> Refine.format Format.Email
        |> Refine.wrap Email (fun (Email s) -> s)

    let create (s: string) = Refine.create refinement s
    let value (Email s) = s
```

Every rule runs, so a value that breaks three of them reports three errors.
Normalisation (`trimmed`, `lowercased`) always happens before any rule, so
`"   "` trims to `""` and is then correctly rejected as empty.

### Constraints as data

```fsharp
Email.refinement.Constraints
// [ Length(Some 3, Some 254); HasFormat Email ]
```

This list is the point of the whole suite. `Etymon.Schema.OpenApi` turns it into
JSON Schema keywords, `Etymon.Migrations` into SQL constraints,
`Etymon.Invariants.FsCheck` into generators — all reading the same rules rather
than restating them. A rule Etymon cannot inspect is `Opaque`: enforced and
documented, but never half-derived into something that looks right and is not.

### Secrets that stay secret

```fsharp
let settings = { Host = "db.internal"; Password = Secret.create "hunter2" }
printfn "%A" settings
// { Host = "db.internal"; Password = <redacted> }
```

Redaction lives on the type, so `ToString`, `%O`, `%A`, `string` and most
structured loggers are all covered — including when the secret is nested inside
the record somebody actually logs. Getting the value back is `Secret.reveal`,
which is one greppable token, so auditing where secrets escape is a search
rather than an audit.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
