# Etymon.Api.Giraffe

**Serves an Etymon endpoint as a Giraffe `HttpHandler`. It does not replace
Giraffe.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Api` and Giraffe.

The result is an ordinary `HttpHandler` that composes into the `choose` you
already have, beside the routes you already wrote:

```fsharp
let webApp =
    choose [
        route "/health" >=> text "ok"            // your existing Giraffe, untouched
        GET >=> routef "/legacy/%s" oldHandler   // also untouched

        GiraffeAdapter.handle getUser (fun userId _ctx -> task {
            match! findUser userId with
            | Some user -> return ApiResult.Ok user
            | None -> return ApiResult.Failed(404, None)
        })
    ]
```

Your pipeline, middleware and error handling stay exactly as they are. Adoption
is one endpoint at a time; you never have to convert anything you do not want to.

## What you get for free

**The request body is decoded and validated before your handler runs.** A body
that does not satisfy the schema is rejected with 422 and a list of every
problem, each with its path — your handler is never called with a value the
schema would not accept.

```json
{
  "title": "The request could not be accepted",
  "status": 422,
  "errors": [
    { "path": "name", "message": "must be at least 1 character" },
    { "path": "role", "message": "must be one of: admin, member" }
  ]
}
```

**An undeclared status is refused.** Returning `ApiResult.Failed(418, …)` from an
endpoint that never declared 418 raises, naming the statuses it did declare. A
status the OpenAPI document does not list and the generated client cannot handle
is a bug in the handler, not something to forward to a caller.

## One thing to know

Etymon matches the path itself rather than delegating to `routef`, because the
route has to be data for the OpenAPI document and the typed client to come out
of the same declaration. Etymon endpoints therefore sit **alongside** Giraffe's
routing combinators rather than inside them.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
