namespace Etymon

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

/// <summary>
/// What a minimal-API handler gives back: a value, or a failure with a status.
/// </summary>
/// <remarks>
/// A status the endpoint did not declare is absent from the OpenAPI document and
/// unknown to the generated client, so the adapter refuses it rather than
/// letting it reach a caller undocumented.
/// </remarks>
[<RequireQualifiedAccess>]
type Handled<'T> =
    /// The endpoint succeeded, with its declared success status.
    | Ok of 'T
    /// The endpoint failed, with a status it declared and a body already encoded.
    | Failed of status: int * body: string option

/// <summary>
/// Registers an <see cref="T:Etymon.Endpoint`3"/> on ASP.NET Core's minimal-API
/// routing.
/// </summary>
/// <remarks>
/// <para>
/// Needs no third-party web framework. ASP.NET Core is the platform rather than
/// a library, so this is the adapter for an application that does not want one.
/// </para>
/// <para>
/// The route template comes from the endpoint's own <c>Route</c> value, so
/// ASP.NET's router does the matching and Etymon does the binding. That is the
/// opposite of the Giraffe adapter, which matches itself because Giraffe has no
/// template-based routing to hand the work to.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module MinimalApi =

    let private jsonResponse (ctx: HttpContext) (status: int) (body: string option) =
        ctx.Response.StatusCode <- status

        match body with
        | Some text ->
            ctx.Response.ContentType <- "application/json"
            ctx.Response.WriteAsync(text, Encoding.UTF8)
        | None -> Task.CompletedTask

    /// One shape for every rejection, so a caller can parse a failure without
    /// knowing which endpoint produced it.
    let private writeValidationFailure (ctx: HttpContext) (errors: ValidationErrors) =
        let problems =
            errors
            |> ValidationErrors.toList
            |> List.map (fun e ->
                let path = Path.toStringOr "(body)" e.Path
                let message = e.Message.Replace("\"", "\\\"")
                $"{{\"path\":\"%s{path}\",\"message\":\"%s{message}\"}}"
            )

        let body =
            "{\"title\":\"The request could not be accepted\",\"status\":422,\"errors\":["
            + String.Join(",", problems)
            + "]}"

        jsonResponse ctx 422 (Some body)

    let private checkDeclared (endpoint: Endpoint<'R, 'Req, 'Res>) (status: int) =
        if not (endpoint.Responses |> List.exists (fun r -> r.Status = status)) then
            failwithf
                "The endpoint '%s' returned status %d, which it does not declare. Declare it with Api.failsWith, or return one of: %s."
                endpoint.OperationId
                status
                (endpoint.Responses |> List.map (fun r -> string r.Status) |> String.concat ", ")

    let private respond (endpoint: Endpoint<'R, 'Req, 'Res>) (ctx: HttpContext) (result: Handled<'Res>) =
        match result with
        | Handled.Ok value -> jsonResponse ctx endpoint.SuccessStatus (Some(Schema.toJson endpoint.Response value))
        | Handled.Failed(status, body) ->
            checkDeclared endpoint status
            jsonResponse ctx status body

    /// ASP.NET's router hands back the captured values by name; Etymon's Route
    /// puts them back in declaration order so the endpoint's own Extract can
    /// read them.
    let private capturedValues (endpoint: Endpoint<'R, 'Req, 'Res>) (ctx: HttpContext) =
        Route.parameters endpoint.Route
        |> List.map (fun (name, _) ->
            match ctx.Request.RouteValues.TryGetValue name with
            | true, value when not (isNull value) -> string value
            | _ -> ""
        )

    let private bindRoute (endpoint: Endpoint<'R, 'Req, 'Res>) (ctx: HttpContext) =
        endpoint.Route.Extract(capturedValues endpoint ctx)

    let private readBody (schema: Schema<'Req>) (ctx: HttpContext) =
        task {
            use reader = new StreamReader(ctx.Request.Body)
            let! text = reader.ReadToEndAsync()
            return Schema.fromJson schema text
        }

    /// <summary>
    /// Registers an endpoint that has no request body.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let app = builder.Build()
    ///
    /// app |> MinimalApi.map getUser (fun userId _ctx -> task {
    ///     match! findUser userId with
    ///     | Some user -> return Handled.Ok user
    ///     | None -> return Handled.Failed(404, None)
    /// })
    /// </code></example>
    let map
        (endpoint: Endpoint<'R, unit, 'Res>)
        (run: 'R -> HttpContext -> Task<Handled<'Res>>)
        (routes: IEndpointRouteBuilder)
        =
        let handler =
            RequestDelegate(fun ctx ->
                task {
                    match bindRoute endpoint ctx with
                    // ASP.NET matched the template but a value did not parse --
                    // /users/hello against /users/{id:guid}. That is a bad
                    // request, not a missing route.
                    | None ->
                        do! jsonResponse ctx 400 (Some "{\"title\":\"A path value could not be read\",\"status\":400}")
                    | Some routeValue ->
                        let! result = run routeValue ctx
                        do! respond endpoint ctx result
                }
                :> Task
            )

        routes
            .MapMethods(Route.template endpoint.Route, [| HttpVerb.name endpoint.Verb |], handler)
            .WithName(endpoint.OperationId)

    /// <summary>
    /// Registers an endpoint with a request body, which is decoded and validated
    /// before the handler runs.
    /// </summary>
    /// <example><code lang="fsharp">
    /// app |> MinimalApi.mapWithBody createUser (fun () newUser _ctx -> task {
    ///     let! created = insert newUser
    ///     return Handled.Ok created
    /// })
    /// </code></example>
    let mapWithBody
        (endpoint: Endpoint<'R, 'Req, 'Res>)
        (run: 'R -> 'Req -> HttpContext -> Task<Handled<'Res>>)
        (routes: IEndpointRouteBuilder)
        =
        let requestSchema =
            match endpoint.Request with
            | Some schema -> schema
            | None ->
                failwithf "The endpoint '%s' has no request body; use MinimalApi.map instead." endpoint.OperationId

        let handler =
            RequestDelegate(fun ctx ->
                task {
                    match bindRoute endpoint ctx with
                    | None ->
                        do! jsonResponse ctx 400 (Some "{\"title\":\"A path value could not be read\",\"status\":400}")
                    | Some routeValue ->
                        let! body = readBody requestSchema ctx

                        match body with
                        | Error errors -> do! writeValidationFailure ctx errors
                        | Ok value ->
                            let! result = run routeValue value ctx
                            do! respond endpoint ctx result
                }
                :> Task
            )

        routes
            .MapMethods(Route.template endpoint.Route, [| HttpVerb.name endpoint.Verb |], handler)
            .WithName(endpoint.OperationId)
