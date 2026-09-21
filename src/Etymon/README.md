# Etymon

> *etymon* (n.) — the root word from which later forms derive.

**Define the type once. Derive everything else.**

This package has no code. It references the parts of the [Etymon](https://github.com/CaelusMinds/Etymon)
suite that cost nothing but `FSharp.Core`:

`Etymon.Core` · `Etymon.Base` · `Etymon.Schema` · `Etymon.Schema.OpenApi` ·
`Etymon.Schema.TypeScript` · `Etymon.Invariants` · `Etymon.Config` ·
`Etymon.Schema.Sql` · `Etymon.Api` · `Etymon.Api.Client`

## What is deliberately not here

Three packages are left out, each because it carries a dependency that would
otherwise become everyone's:

| Package | Carries | Install it when |
| --- | --- | --- |
| `Etymon.Api.Giraffe` | Giraffe | You serve endpoints with Giraffe |
| `Etymon.Api.AspNetCore` | ASP.NET Core | You serve endpoints on minimal APIs |
| `Etymon.Invariants.FsCheck` | FsCheck | You write property tests |

A meta-package is a convenience, and a convenience that quietly adds a web
framework to a console application is not one. The two adapters are also
mutually exclusive in practice, so there is no version of this package that
could include the right one.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
