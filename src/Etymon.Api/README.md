# Etymon.Api

**HTTP endpoints described once. This package performs no HTTP.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core`, `Etymon.Base`, `Etymon.Schema` and `Etymon.Schema.OpenApi` — no
web framework, no `HttpContext`, no socket.

```fsharp
let getUser =
    Api.get "getUser" (Route.one [ "users" ] (Param.guid "id") []) userSchema
    |> Api.summary "Fetches one user by id"
    |> Api.tag "Users"
    |> Api.failsWith 404 problemSchema "No user with that id"
```

That is a **value**. Several interpreters read it, and because they all read the
same one, none of them can describe the endpoint differently from the others:

| Package | What it produces |
| --- | --- |
| `Etymon.Api.Giraffe` | a Giraffe `HttpHandler` |
| `Etymon.Api.AspNetCore` | a minimal-API registration |
| `Etymon.Api.Client` | a typed `HttpClient` function |
| this package | OpenAPI 3.1 paths |
| `Etymon.Schema.TypeScript` | `.d.ts` for the frontend |

Adding Falco, Oxpecker or Saturn later is writing one more interpreter. The
declarations do not change, and nothing already using them does either.

## Routes are data, not format strings

```fsharp
Route.two [ "users" ] (Param.guid "id") [ "orders" ] (Param.int "orderId") []
```

```fsharp
Route.template route   // "/users/{id}/orders/{orderId}" — OpenAPI and ASP.NET both
Route.parameters route // [ "id", Guid; "orderId", Int ] — what a generator reads
Route.toPath route (id, 42)  // "/users/9d1c.../orders/42" — what a client requests
Route.matchPath route path   // Some (id, 42) — what a server binds
```

One declaration; the template, the URL builder and the matcher all come out of
it. A `printf`-style route would read more prettily and be nearly impossible to
introspect, which would mean the OpenAPI path and the client both re-deriving
what the server already knows.

Zero, one and two path parameters have dedicated constructors rather than a
general combinator. That costs a little elegance and buys full inference with no
operator overloading — and a route with three or more parameters is already
telling you something. `Route.many` is the escape hatch.

## Failures are declared, not discovered

```fsharp
|> Api.failsWith 404 problemSchema "No user with that id"
|> Api.failsWithNoBody 401 "Not authenticated"
```

An undeclared status is one a caller meets in production without warning: absent
from the OpenAPI document, unknown to the generated client. So the adapters
**reject** a handler returning a status the endpoint never declared, rather than
passing it along undocumented.

## Why GET has no request type variable

`Api.get` returns `Endpoint<'Route, unit, 'Response>`. Leaving the request type
open would make every declaration hit F#'s value restriction, and would let a
handler be written for a body that can never arrive.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
