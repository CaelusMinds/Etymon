namespace Etymon

open System

/// An HTTP method.
[<RequireQualifiedAccess>]
type HttpVerb =
    /// GET.
    | Get
    /// POST.
    | Post
    /// PUT.
    | Put
    /// PATCH.
    | Patch
    /// DELETE.
    | Delete

/// Working with <see cref="T:Etymon.HttpVerb"/>.
[<RequireQualifiedAccess>]
module HttpVerb =

    /// <summary>The method as it appears on the wire.</summary>
    /// <example><code lang="fsharp">
    /// HttpVerb.name HttpVerb.Get // "GET"
    /// </code></example>
    let name (verb: HttpVerb) =
        match verb with
        | HttpVerb.Get -> "GET"
        | HttpVerb.Post -> "POST"
        | HttpVerb.Put -> "PUT"
        | HttpVerb.Patch -> "PATCH"
        | HttpVerb.Delete -> "DELETE"

/// A named value read from the query string.
[<NoEquality; NoComparison>]
type QuerySpec =
    {
        /// The name in the query string.
        Name: string
        /// What kind of value it is.
        Kind: ParamKind
        /// Whether a request without it is rejected.
        Required: bool
        /// What it means.
        Description: string option
    }

/// One response an endpoint can produce.
[<NoEquality; NoComparison>]
type ResponseSpec =
    {
        /// The status code.
        Status: int
        /// The shape of the body, or none when there is no body.
        Body: SchemaInfo option
        /// What this response means.
        Description: string
    }

/// <summary>
/// An HTTP endpoint, described once.
/// </summary>
/// <remarks>
/// <para>
/// This type performs no HTTP. It is a description that several interpreters
/// read: a Giraffe handler, an ASP.NET Core route registration, a typed client,
/// OpenAPI paths and TypeScript declarations. Because they all read the same
/// value, none of them can describe the endpoint differently from the others.
/// </para>
/// <para>
/// <c>'Route</c> is what the path captures, <c>'Request</c> what the body
/// carries, and <c>'Response</c> what a successful call returns.
/// </para>
/// </remarks>
[<NoEquality; NoComparison>]
type Endpoint<'Route, 'Request, 'Response> =
    {
        /// A stable identifier, used as the OpenAPI operation id and as the
        /// generated client function's name.
        OperationId: string
        /// The method.
        Verb: HttpVerb
        /// The path, and how to read and write its parameters.
        Route: Route<'Route>
        /// Query-string parameters.
        Query: QuerySpec list
        /// The request body, when there is one.
        Request: Schema<'Request> option
        /// The successful response, and its status code.
        Response: Schema<'Response>
        /// The status of a successful response.
        SuccessStatus: int
        /// Every response this endpoint can produce, including failures. Data, so
        /// that a client can be generated that handles all of them.
        Responses: ResponseSpec list
        /// What the endpoint is for.
        Summary: string option
        /// Grouping, used as the OpenAPI tag.
        Tags: string list
    }

/// <summary>
/// Describing HTTP endpoints.
/// </summary>
/// <remarks>
/// An endpoint built here does nothing on its own. Pass it to
/// <c>Etymon.Api.Giraffe</c>, <c>Etymon.Api.AspNetCore</c> or
/// <c>Etymon.Api.Client</c> to make it do something, and to
/// <c>Api.toOpenApiPaths</c> to document it — always from this one declaration.
/// </remarks>
[<RequireQualifiedAccess>]
module Api =

    /// The body-less schema used by endpoints that neither take nor return one.
    let private nothing: Schema<unit> =
        Schema.convert (fun (_: bool) -> ()) (fun () -> true) Schema.bool

    let private create
        verb
        operationId
        route
        (request: Schema<'Req> option)
        response
        status
        : Endpoint<'R, 'Req, 'Res>
        =
        {
            OperationId = operationId
            Verb = verb
            Route = route
            Query = []
            Request = request
            Response = response
            SuccessStatus = status
            Responses =
                [
                    {
                        Status = status
                        Body = Some response.Info
                        Description = "Success"
                    }
                ]
            Summary = None
            Tags = []
        }

    /// <summary>
    /// A GET endpoint.
    /// </summary>
    /// <remarks>
    /// The request type is <c>unit</c> rather than left open, because a GET has
    /// no body. Leaving it generic would make every endpoint declaration hit
    /// F#'s value restriction, and would let a handler be written for a body
    /// that can never arrive.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Api.get "getUser" (Route.one [ "users" ] (Param.guid "id") []) userSchema
    /// </code></example>
    let get (operationId: string) (route: Route<'R>) (response: Schema<'Res>) : Endpoint<'R, unit, 'Res> =
        create HttpVerb.Get operationId route None response 200

    /// <summary>A DELETE endpoint, which likewise has no request body.</summary>
    /// <example><code lang="fsharp">
    /// Api.delete "deleteUser" (Route.one [ "users" ] (Param.guid "id") []) Api.noBody
    /// </code></example>
    let delete (operationId: string) (route: Route<'R>) (response: Schema<'Res>) : Endpoint<'R, unit, 'Res> =
        create HttpVerb.Delete operationId route None response 200

    /// <summary>A POST endpoint, with a request body.</summary>
    /// <example><code lang="fsharp">
    /// Api.post "createUser" (Route.literal [ "users" ]) newUserSchema userSchema
    /// </code></example>
    let post
        (operationId: string)
        (route: Route<'R>)
        (request: Schema<'Req>)
        (response: Schema<'Res>)
        : Endpoint<'R, 'Req, 'Res>
        =
        create HttpVerb.Post operationId route (Some request) response 201

    /// <summary>A PUT endpoint, with a request body.</summary>
    /// <example><code lang="fsharp">
    /// Api.put "replaceUser" (Route.one [ "users" ] (Param.guid "id") []) userSchema userSchema
    /// </code></example>
    let put
        (operationId: string)
        (route: Route<'R>)
        (request: Schema<'Req>)
        (response: Schema<'Res>)
        : Endpoint<'R, 'Req, 'Res>
        =
        create HttpVerb.Put operationId route (Some request) response 200

    /// <summary>A PATCH endpoint, with a request body.</summary>
    /// <example><code lang="fsharp">
    /// Api.patch "updateUser" (Route.one [ "users" ] (Param.guid "id") []) patchSchema userSchema
    /// </code></example>
    let patch
        (operationId: string)
        (route: Route<'R>)
        (request: Schema<'Req>)
        (response: Schema<'Res>)
        : Endpoint<'R, 'Req, 'Res>
        =
        create HttpVerb.Patch operationId route (Some request) response 200

    /// <summary>
    /// A schema for an endpoint with no body, encoded as JSON <c>null</c>.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Api.delete "deleteUser" route Api.noBody
    /// </code></example>
    let noBody: Schema<unit> = nothing

    /// <summary>Says what the endpoint is for.</summary>
    /// <example><code lang="fsharp">
    /// endpoint |> Api.summary "Fetches one user by id"
    /// </code></example>
    let summary (text: string) (endpoint: Endpoint<'R, 'Req, 'Res>) = { endpoint with Summary = Some text }

    /// <summary>Groups the endpoint, which becomes its OpenAPI tag.</summary>
    /// <example><code lang="fsharp">
    /// endpoint |> Api.tag "Users"
    /// </code></example>
    let tag (name: string) (endpoint: Endpoint<'R, 'Req, 'Res>) =
        { endpoint with
            Tags = endpoint.Tags @ [ name ]
        }

    /// <summary>Declares a required query parameter.</summary>
    /// <example><code lang="fsharp">
    /// endpoint |> Api.query (Param.int "page")
    /// </code></example>
    let query (param: RouteParam<'T>) (endpoint: Endpoint<'R, 'Req, 'Res>) =
        { endpoint with
            Query =
                endpoint.Query
                @ [
                    {
                        Name = param.Name
                        Kind = param.Kind
                        Required = true
                        Description = None
                    }
                ]
        }

    /// <summary>Declares an optional query parameter.</summary>
    /// <example><code lang="fsharp">
    /// endpoint |> Api.optionalQuery (Param.bool "includeArchived")
    /// </code></example>
    let optionalQuery (param: RouteParam<'T>) (endpoint: Endpoint<'R, 'Req, 'Res>) =
        { endpoint with
            Query =
                endpoint.Query
                @ [
                    {
                        Name = param.Name
                        Kind = param.Kind
                        Required = false
                        Description = None
                    }
                ]
        }

    /// <summary>
    /// Declares a failure this endpoint can produce.
    /// </summary>
    /// <remarks>
    /// Declared rather than discovered, so that the generated client can handle
    /// every case the endpoint admits to, and the OpenAPI document lists them.
    /// An undeclared status is one a caller will meet in production without
    /// warning.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// endpoint |> Api.failsWith 404 problemSchema "No user with that id"
    /// </code></example>
    let failsWith (status: int) (body: Schema<'E>) (description: string) (endpoint: Endpoint<'R, 'Req, 'Res>) =
        { endpoint with
            Responses =
                endpoint.Responses
                @ [
                    {
                        Status = status
                        Body = Some body.Info
                        Description = description
                    }
                ]
        }

    /// <summary>Declares a failure with no body.</summary>
    /// <example><code lang="fsharp">
    /// endpoint |> Api.failsWithNoBody 401 "Not authenticated"
    /// </code></example>
    let failsWithNoBody (status: int) (description: string) (endpoint: Endpoint<'R, 'Req, 'Res>) =
        { endpoint with
            Responses =
                endpoint.Responses
                @ [
                    {
                        Status = status
                        Body = None
                        Description = description
                    }
                ]
        }

    /// <summary>The path template, as OpenAPI and ASP.NET Core both spell it.</summary>
    /// <example><code lang="fsharp">
    /// Api.template endpoint // "/users/{id}"
    /// </code></example>
    let template (endpoint: Endpoint<'R, 'Req, 'Res>) = Route.template endpoint.Route

    /// <summary>
    /// A concrete URL for a call, including the query string. What a generated
    /// client requests.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Api.toUrl endpoint someGuid [ "page", "2" ] // "/users/9d1c.../?page=2"
    /// </code></example>
    let toUrl (endpoint: Endpoint<'R, 'Req, 'Res>) (route: 'R) (query: (string * string) list) =
        let path = Route.toPath endpoint.Route route

        match query with
        | [] -> path
        | pairs ->
            let rendered =
                pairs
                |> List.map (fun (name, value) -> Uri.EscapeDataString name + "=" + Uri.EscapeDataString value)

            path + "?" + String.Join("&", rendered)
