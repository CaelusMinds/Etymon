# Etymon.Api.AspNetCore

**Registers an Etymon endpoint on ASP.NET Core minimal-API routing.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Api` and ASP.NET Core — **no third-party web framework**. ASP.NET Core is
the platform rather than a library, so this is the adapter for an application
that does not want one.

```fsharp
let app = builder.Build()

app |> MinimalApi.map getUser (fun userId _ctx -> task {
    match! findUser userId with
    | Some user -> return Handled.Ok user
    | None -> return Handled.Failed(404, None)
})

app |> MinimalApi.mapWithBody createUser (fun () newUser _ctx -> task {
    let! created = insert newUser
    return Handled.Ok created
})
```

The route template comes from the endpoint's own `Route` value, so **ASP.NET's
router does the matching and Etymon does the binding**. That is the opposite of
the Giraffe adapter, which matches itself because Giraffe has no template-based
routing to hand the work to — and it means Etymon endpoints appear in ASP.NET's
endpoint metadata like any others.

## What you get for free

**The request body is decoded and validated before your handler runs.** A body
the schema rejects comes back as 422 with every problem and its path; your
handler is never called with a value the schema would not accept, and contains
no validation code of its own.

**A path value that does not parse is a 400, not a handler call.**
`/users/hello` against `/users/{id}` where `id` is a `Guid` never reaches you.

**An undeclared status is refused.** Returning `Handled.Failed(418, …)` from an
endpoint that never declared 418 raises, naming the statuses it did. A status
the OpenAPI document omits and the generated client cannot handle is a bug in
the handler, not something to forward.

## It appears in your OpenAPI document

An endpoint registers itself with the metadata ASP.NET already understands:
operation id, summary, tags, the request type, and **every status it declares**,
not just the successful one. An application that already publishes a document
gets Etymon endpoints in it without describing them a second time — and a
hand-written second description is the one that goes stale.

The 422 the adapter produces for a body the schema refuses is declared too, even
though the endpoint never mentions it. It is part of the contract whether or not
anyone wrote it down, and a caller meeting it for the first time in production is
the alternative.

What does not reach that document is the constraints. ASP.NET builds its schemas
by reflecting over the CLR type, and the type does not know that `role` is one of
three strings. `ApiOpenApi` writes a document that does carry them; joining the
two needs a document transformer, and that needs an OpenAPI package this one
deliberately does not take.

## Tested against the real thing

The adapter is exercised end to end through `Microsoft.AspNetCore.TestHost`:
real requests, real routing, real status codes, driven by `Etymon.Api.Client`.
The same tests run against the Giraffe adapter, and one asserts the two produce
**byte-identical responses** — which is the point of one declaration with several
interpreters.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
