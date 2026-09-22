# Etymon.Api.Client

**Calls an Etymon endpoint over HTTP. No server, no web framework.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Api` and nothing else — a console application calling an Etymon-described
API takes this package alone.

```fsharp
match! Client.call http getUser userId with
| Ok user -> printfn "%s" user.Name
| Error e -> eprintfn "%s" (ApiError.describe e)
```

The URL is built by **the same `Route` value the server matches with**, so the
client cannot ask for a path the server does not serve. The body is encoded and
decoded with the endpoint's own schemas.

## Three kinds of failure, kept apart

```fsharp
| Error (ApiError.Failed failure) ->
    // The server answered, but not with success.
    // failure.Declared says whether the endpoint admits to this status,
    // and failure.Description says what it means — without asking anyone.

| Error (ApiError.Malformed (status, errors)) ->
    // The server said this succeeded and then sent something the schema
    // rejects. That is the two sides disagreeing about the contract, which
    // deserves a louder reaction than an ordinary failure.

| Error (ApiError.Transport exn) ->
    // The request never completed.
```

`Declared` is the field worth branching on. A status the endpoint declared is one
the server meant to send and the OpenAPI document lists. A status it did not is a
surprise, and the two deserve different handling — retrying a declared 409 is
sensible; retrying an undeclared 500 usually is not.

## Tested against real servers

The client is exercised end to end against both a Giraffe server and a
minimal-API server built from the same endpoint declarations, over real HTTP
through `Microsoft.AspNetCore.TestHost`.

## Public surface

The complete public surface of this package, every value with its full signature
and its documentation, is in `Surface.fsi` beside this file. It is written by the
build from the implementation, so it is where to learn what a function hands
back without compiling anything.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
