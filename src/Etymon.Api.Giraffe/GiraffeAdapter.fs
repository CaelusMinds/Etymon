namespace Etymon

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Giraffe

/// <summary>
/// What a handler gives back: a value, or a failure with a status.
/// </summary>
/// <remarks>
/// A status the endpoint did not declare is a status the generated client does
/// not know about and the OpenAPI document does not list, so
/// <c>Etymon.Api.Giraffe</c> rejects it rather than letting it reach a caller
/// undocumented. See <see cref="M:Etymon.GiraffeAdapter.handle"/>.
/// </remarks>
[<RequireQualifiedAccess>]
type ApiResult<'T> =
    /// The endpoint succeeded, with its declared success status.
    | Ok of 'T
    /// The endpoint failed, with a status it declared and a body already encoded.
    | Failed of status: int * body: string option

/// <summary>
/// Serves an <see cref="T:Etymon.Endpoint`3"/> as a Giraffe <c>HttpHandler</c>.
/// </summary>
/// <remarks>
/// <para>
/// This does not replace Giraffe. The result is an ordinary <c>HttpHandler</c>
/// that composes into the <c>choose</c> you already have, beside the routes you
/// already wrote. Adoption is one endpoint at a time; nothing else in your
/// pipeline changes.
/// </para>
/// <para>
/// Etymon matches the path itself rather than delegating to <c>routef</c>,
/// because the route has to be data for the OpenAPI document and the typed
/// client to be derivable from the same declaration. The consequence is that an
/// Etymon endpoint sits alongside Giraffe's routing combinators rather than
/// inside them.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module GiraffeAdapter =

    let private matchesVerb (verb: HttpVerb) (ctx: HttpContext) =
        System.String.Equals(ctx.Request.Method, HttpVerb.name verb, System.StringComparison.OrdinalIgnoreCase)

    /// Writes a validation failure as a problem document. One shape for every
    /// rejection, so a caller can parse failures without knowing which endpoint
    /// produced them.
    let private writeValidationFailure (errors: ValidationErrors) : HttpHandler =
        fun next ctx ->
            let problems =
                errors
                |> ValidationErrors.toList
                |> List.map (fun e ->
                    {|
                        path = Path.toStringOr "(body)" e.Path
                        message = e.Message
                    |}
                )

            ctx.SetStatusCode 422

            json
                {|
                    title = "The request could not be accepted"
                    status = 422
                    errors = problems
                |}
                next
                ctx

    let private readBody (schema: Schema<'Req>) (ctx: HttpContext) : Task<Validation<'Req>> =
        task {
            use reader = new System.IO.StreamReader(ctx.Request.Body)
            let! text = reader.ReadToEndAsync()
            return Schema.fromJson schema text
        }

    /// Writes the response and stops. It does not call `next`: this handler has
    /// produced the response, so passing the request further down the pipeline
    /// would let something else write over it.
    let private respond (endpoint: Endpoint<'R, 'Req, 'Res>) (result: ApiResult<'Res>) : HttpHandler =
        fun _next ctx ->
            match result with
            | ApiResult.Ok value ->
                ctx.SetStatusCode endpoint.SuccessStatus
                ctx.SetContentType "application/json"
                ctx.WriteStringAsync(Schema.toJson endpoint.Response value)

            | ApiResult.Failed(status, body) ->
                // A status the endpoint never declared would be invisible to the
                // generated client and absent from the OpenAPI document, so it is
                // a mistake in the handler rather than something to pass along.
                if not (endpoint.Responses |> List.exists (fun r -> r.Status = status)) then
                    failwithf
                        "The endpoint '%s' returned status %d, which it does not declare. Declare it with Api.failsWith, or return one of: %s."
                        endpoint.OperationId
                        status
                        (endpoint.Responses |> List.map (fun r -> string r.Status) |> String.concat ", ")

                ctx.SetStatusCode status

                match body with
                | Some text ->
                    ctx.SetContentType "application/json"
                    ctx.WriteStringAsync text
                | None -> Task.FromResult(Some ctx)

    /// <summary>
    /// Serves an endpoint that has no request body.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let webApp =
    ///     choose [
    ///         route "/health" >=> text "ok"          // your existing Giraffe
    ///
    ///         GiraffeAdapter.handle getUser (fun userId _ctx -> task {
    ///             match! findUser userId with
    ///             | Some user -> return ApiResult.Ok user
    ///             | None -> return ApiResult.Failed(404, None)
    ///         })
    ///     ]
    /// </code></example>
    let handle (endpoint: Endpoint<'R, unit, 'Res>) (run: 'R -> HttpContext -> Task<ApiResult<'Res>>) : HttpHandler =
        fun next ctx ->
            if not (matchesVerb endpoint.Verb ctx) then
                skipPipeline
            else
                match Route.matchPath endpoint.Route ctx.Request.Path.Value with
                | None -> skipPipeline
                | Some routeValue ->
                    task {
                        let! result = run routeValue ctx
                        return! respond endpoint result next ctx
                    }

    /// <summary>
    /// Serves an endpoint with a request body, which is decoded and validated
    /// before the handler runs.
    /// </summary>
    /// <remarks>
    /// A body that does not satisfy the schema is rejected with 422 and a list of
    /// every problem, each with its path — the handler is never called with a
    /// value the schema would not accept.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// GiraffeAdapter.handleWithBody createUser (fun () newUser _ctx -> task {
    ///     let! created = insert newUser
    ///     return ApiResult.Ok created
    /// })
    /// </code></example>
    let handleWithBody
        (endpoint: Endpoint<'R, 'Req, 'Res>)
        (run: 'R -> 'Req -> HttpContext -> Task<ApiResult<'Res>>)
        : HttpHandler
        =
        fun next ctx ->
            if not (matchesVerb endpoint.Verb ctx) then
                skipPipeline
            else
                match Route.matchPath endpoint.Route ctx.Request.Path.Value with
                | None -> skipPipeline
                | Some routeValue ->
                    match endpoint.Request with
                    | None ->
                        failwithf
                            "The endpoint '%s' has no request body; use GiraffeAdapter.handle instead."
                            endpoint.OperationId
                    | Some requestSchema ->
                        task {
                            let! body = readBody requestSchema ctx

                            match body with
                            | Error errors -> return! writeValidationFailure errors next ctx
                            | Ok value ->
                                let! result = run routeValue value ctx
                                return! respond endpoint result next ctx
                        }

    /// <summary>
    /// Encodes a declared failure body, so a handler does not hand-write JSON.
    /// </summary>
    /// <example><code lang="fsharp">
    /// return ApiResult.Failed(404, GiraffeAdapter.body problemSchema { Detail = "No such user" })
    /// </code></example>
    let body (schema: Schema<'E>) (value: 'E) = Some(Schema.toJson schema value)
