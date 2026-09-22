
namespace FSharp



namespace Etymon
    
    /// The kind of a value carried in a path or a query string, named so that a
    /// generator can render it without knowing the F# type it came from.
    [<RequireQualifiedAccess>]
    type ParamKind =
        
        /// Any text.
        | String
        
        /// A 32-bit integer.
        | Int
        
        /// A 64-bit integer.
        | Int64
        
        /// A UUID.
        | Guid
        
        /// A boolean.
        | Bool
    
    /// <summary>
    /// One named value read out of a URL.
    /// </summary>
    /// <remarks>
    /// Carries its kind as data so that OpenAPI, TypeScript and the route template a
    /// server framework needs can all be generated from it, and carries the parse
    /// and render functions so that the same declaration binds the value on the
    /// server and builds the URL on the client.
    /// </remarks>
    [<NoEquality; NoComparison>]
    type RouteParam<'T> =
        {
          
          /// The name, as it appears in the path template.
          Name: string
          
          /// What kind of value it is.
          Kind: ParamKind
          
          /// Reads it from the URL.
          Parse: (string -> 'T option)
          
          /// Writes it into a URL.
          Render: ('T -> string)
        }
    
    /// One piece of a path.
    [<RequireQualifiedAccess>]
    type PathSegmentSpec =
        
        /// A fixed piece: the <c>users</c> in <c>/users/{id}</c>.
        | Literal of string
        
        /// A placeholder, with its name and kind.
        | Capture of name: string * kind: ParamKind
    
    /// <summary>
    /// A path, and how to read values out of it and put them back in.
    /// </summary>
    /// <remarks>
    /// The path is <em>data</em>: a list of segments, not a format string. That is
    /// what lets Etymon render the same route as an OpenAPI path template, an
    /// ASP.NET route pattern, a Giraffe matcher and a client-side URL builder,
    /// without any of them re-deriving it.
    /// </remarks>
    [<NoEquality; NoComparison>]
    type Route<'T> =
        {
          
          /// The segments, in order.
          Segments: PathSegmentSpec list
          
          /// Reads the captured values, in the order they appear.
          Extract: (string list -> 'T option)
          
          /// Produces the captured values, in the order they appear.
          Fill: ('T -> string list)
        }
    
    /// Building <see cref="T:Etymon.RouteParam`1"/> values.
    [<RequireQualifiedAccess>]
    module Param =
        
        /// <summary>A text parameter.</summary>
        /// <example><code lang="fsharp">
        /// Param.string "slug"
        /// </code></example>
        val string: name: string -> RouteParam<string>
        
        /// <summary>A 32-bit integer parameter.</summary>
        /// <example><code lang="fsharp">
        /// Param.int "page"
        /// </code></example>
        val int: name: string -> RouteParam<int>
        
        /// <summary>A 64-bit integer parameter.</summary>
        /// <example><code lang="fsharp">
        /// Param.int64 "offset"
        /// </code></example>
        val int64: name: string -> RouteParam<int64>
        
        /// <summary>A UUID parameter.</summary>
        /// <example><code lang="fsharp">
        /// Param.guid "id"
        /// </code></example>
        val guid: name: string -> RouteParam<System.Guid>
        
        /// <summary>A boolean parameter, accepting the forms that occur in URLs.</summary>
        /// <example><code lang="fsharp">
        /// Param.bool "includeArchived"
        /// </code></example>
        val bool: name: string -> RouteParam<bool>
    
    /// <summary>
    /// Building <see cref="T:Etymon.Route`1"/> values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero, one and two path parameters have their own constructors rather than
    /// there being a general combinator. That is a deliberate trade: it costs a
    /// little elegance and buys full type inference with no operator overloading,
    /// and a route with three or more parameters is already telling you something
    /// about the design. <see cref="M:Etymon.Route.many"/> is there for the rest.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Route =
        
        val private literals: (string list -> PathSegmentSpec list)
        
        /// <summary>A path with no parameters.</summary>
        /// <example><code lang="fsharp">
        /// Route.literal [ "users" ] // /users
        /// </code></example>
        val literal: segments: string list -> Route<unit>
        
        /// <summary>A path with one parameter.</summary>
        /// <example><code lang="fsharp">
        /// Route.one [ "users" ] (Param.guid "id") [] // /users/{id}
        /// Route.one [ "users" ] (Param.guid "id") [ "orders" ] // /users/{id}/orders
        /// </code></example>
        val one:
          before: string list ->
            param: RouteParam<'A> -> after: string list -> Route<'A>
        
        /// <summary>A path with two parameters.</summary>
        /// <example><code lang="fsharp">
        /// Route.two [ "users" ] (Param.guid "id") [ "orders" ] (Param.int "orderId") []
        /// // /users/{id}/orders/{orderId}
        /// </code></example>
        val two:
          before: string list ->
            first: RouteParam<'A> ->
            between: string list ->
            second: RouteParam<'B> -> after: string list -> Route<'A * 'B>
        
        /// <summary>
        /// A path with any number of parameters, all of the same kind, handed over as
        /// a list. The escape hatch for a route shape the typed constructors do not
        /// cover.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Route.many [ PathSegmentSpec.Literal "files"; PathSegmentSpec.Capture("path", ParamKind.String) ]
        /// </code></example>
        val many: segments: PathSegmentSpec list -> Route<string list>
        
        /// <summary>The parameters a route captures, in order.</summary>
        /// <example><code lang="fsharp">
        /// Route.parameters route // [ "id", ParamKind.Guid ]
        /// </code></example>
        val parameters: route: Route<'T> -> (string * ParamKind) list
        
        /// <summary>
        /// The path as a template with braces, which is what OpenAPI and ASP.NET
        /// Core both use.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Route.template route // "/users/{id}/orders"
        /// </code></example>
        val template: route: Route<'T> -> string
        
        /// <summary>
        /// A concrete path for a value, ready to request. This is what makes a
        /// generated client possible without it re-deriving the URL shape.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Route.toPath userRoute someGuid // "/users/9d1c.../orders"
        /// </code></example>
        val toPath: route: Route<'T> -> value: 'T -> string
        
        /// <summary>
        /// Matches a concrete path, returning the captured value when it fits.
        /// </summary>
        /// <remarks>
        /// Every adapter uses this rather than delegating to its framework's router,
        /// so that one route description behaves identically on Giraffe, on minimal
        /// APIs and in the client.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Route.matchPath userRoute "/users/9d1c4b2a-.../orders" // Some guid
        /// </code></example>
        val matchPath: route: Route<'T> -> path: string -> 'T option

namespace Etymon
    
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
        val name: verb: HttpVerb -> string
    
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
    type Endpoint<'Route,'Request,'Response> =
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
        val private nothing: Schema<unit>
        
        val private create:
          verb: HttpVerb ->
            operationId: string ->
            route: Route<'R> ->
            request: Schema<'Req> option ->
            response: Schema<'Res> -> status: int -> Endpoint<'R,'Req,'Res>
        
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
        val get:
          operationId: string ->
            route: Route<'R> -> response: Schema<'Res> -> Endpoint<'R,unit,'Res>
        
        /// <summary>A DELETE endpoint, which likewise has no request body.</summary>
        /// <example><code lang="fsharp">
        /// Api.delete "deleteUser" (Route.one [ "users" ] (Param.guid "id") []) Api.noBody
        /// </code></example>
        val delete:
          operationId: string ->
            route: Route<'R> -> response: Schema<'Res> -> Endpoint<'R,unit,'Res>
        
        /// <summary>A POST endpoint, with a request body.</summary>
        /// <example><code lang="fsharp">
        /// Api.post "createUser" (Route.literal [ "users" ]) newUserSchema userSchema
        /// </code></example>
        val post:
          operationId: string ->
            route: Route<'R> ->
            request: Schema<'Req> ->
            response: Schema<'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>A PUT endpoint, with a request body.</summary>
        /// <example><code lang="fsharp">
        /// Api.put "replaceUser" (Route.one [ "users" ] (Param.guid "id") []) userSchema userSchema
        /// </code></example>
        val put:
          operationId: string ->
            route: Route<'R> ->
            request: Schema<'Req> ->
            response: Schema<'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>A PATCH endpoint, with a request body.</summary>
        /// <example><code lang="fsharp">
        /// Api.patch "updateUser" (Route.one [ "users" ] (Param.guid "id") []) patchSchema userSchema
        /// </code></example>
        val patch:
          operationId: string ->
            route: Route<'R> ->
            request: Schema<'Req> ->
            response: Schema<'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>
        /// A schema for an endpoint with no body, encoded as JSON <c>null</c>.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Api.delete "deleteUser" route Api.noBody
        /// </code></example>
        val noBody: Schema<unit>
        
        /// <summary>Says what the endpoint is for.</summary>
        /// <example><code lang="fsharp">
        /// endpoint |> Api.summary "Fetches one user by id"
        /// </code></example>
        val summary:
          text: string ->
            endpoint: Endpoint<'R,'Req,'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>Groups the endpoint, which becomes its OpenAPI tag.</summary>
        /// <example><code lang="fsharp">
        /// endpoint |> Api.tag "Users"
        /// </code></example>
        val tag:
          name: string ->
            endpoint: Endpoint<'R,'Req,'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>Declares a required query parameter.</summary>
        /// <example><code lang="fsharp">
        /// endpoint |> Api.query (Param.int "page")
        /// </code></example>
        val query:
          param: RouteParam<'T> ->
            endpoint: Endpoint<'R,'Req,'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>Declares an optional query parameter.</summary>
        /// <example><code lang="fsharp">
        /// endpoint |> Api.optionalQuery (Param.bool "includeArchived")
        /// </code></example>
        val optionalQuery:
          param: RouteParam<'T> ->
            endpoint: Endpoint<'R,'Req,'Res> -> Endpoint<'R,'Req,'Res>
        
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
        val failsWith:
          status: int ->
            body: Schema<'E> ->
            description: string ->
            endpoint: Endpoint<'R,'Req,'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>Declares a failure with no body.</summary>
        /// <example><code lang="fsharp">
        /// endpoint |> Api.failsWithNoBody 401 "Not authenticated"
        /// </code></example>
        val failsWithNoBody:
          status: int ->
            description: string ->
            endpoint: Endpoint<'R,'Req,'Res> -> Endpoint<'R,'Req,'Res>
        
        /// <summary>The path template, as OpenAPI and ASP.NET Core both spell it.</summary>
        /// <example><code lang="fsharp">
        /// Api.template endpoint // "/users/{id}"
        /// </code></example>
        val template: endpoint: Endpoint<'R,'Req,'Res> -> string
        
        /// <summary>
        /// A concrete URL for a call, including the query string. What a generated
        /// client requests.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Api.toUrl endpoint someGuid [ "page", "2" ] // "/users/9d1c.../?page=2"
        /// </code></example>
        val toUrl:
          endpoint: Endpoint<'R,'Req,'Res> ->
            route: 'R -> query: (string * string) list -> string

namespace Etymon
    
    /// <summary>
    /// OpenAPI 3.1 paths, derived from endpoint declarations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pure function of the endpoints, as everything else in the suite is a pure
    /// function of a schema. The document cannot describe an endpoint differently
    /// from the way the server routes it, because both read the same value.
    /// </para>
    /// <para>
    /// This emits the document; serving it is the application's business.
    /// </para>
    /// <para>
    /// An application that already runs ASP.NET Core's own OpenAPI generation does
    /// not need this: <c>Etymon.Api.AspNetCore</c> registers each endpoint with the
    /// metadata ASP.NET understands, so the endpoints appear in that document
    /// already. What it cannot carry there is the constraints — ASP.NET reflects
    /// over the CLR type, and the type does not know them. Use this where the
    /// document must say <c>minLength</c> and <c>enum</c> rather than only
    /// <c>string</c>.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module ApiOpenApi =
        
        [<AbstractClass; Sealed; Class>]
        type private Node =
            
            static member Of: value: bool -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: int -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: string -> System.Text.Json.Nodes.JsonNode
        
        val private kindSchema:
          kind: ParamKind -> System.Text.Json.Nodes.JsonObject
        
        /// A named schema becomes a reference so it is written once. Anything else is
        /// inlined, structure and all — a list has to carry its element type, or the
        /// document says "an array of something" and a generated client has nothing
        /// to work from.
        val private reference:
          info: SchemaInfo -> System.Text.Json.Nodes.JsonObject
        
        val private jsonContent:
          info: SchemaInfo -> System.Text.Json.Nodes.JsonObject
        
        val private parametersFor:
          endpoint: Endpoint<'R,'Req,'Res> -> System.Text.Json.Nodes.JsonArray
        
        val private operationFor:
          endpoint: Endpoint<'R,'Req,'Res> -> System.Text.Json.Nodes.JsonObject
        
        /// <summary>
        /// The <c>paths</c> object for a set of endpoints.
        /// </summary>
        /// <remarks>
        /// Endpoints sharing a path are merged into one entry with several methods,
        /// which is how OpenAPI expects them.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// ApiOpenApi.paths [ Endpoint.erase getUser; Endpoint.erase createUser ]
        /// </code></example>
        val pathsOf:
          entries: (string * string * System.Text.Json.Nodes.JsonObject) list ->
            System.Text.Json.Nodes.JsonObject
        
        /// <summary>
        /// One endpoint rendered as a path template, a method and an operation,
        /// ready to be combined with others.
        /// </summary>
        /// <example><code lang="fsharp">
        /// let entries = [ ApiOpenApi.entryFor getUser; ApiOpenApi.entryFor createUser ]
        /// ApiOpenApi.pathsOf entries
        /// </code></example>
        val entryFor:
          endpoint: Endpoint<'R,'Req,'Res> ->
            string * string * System.Text.Json.Nodes.JsonObject
        
        /// <summary>
        /// Every named schema an endpoint refers to, for the <c>components/schemas</c>
        /// section.
        /// </summary>
        /// <example><code lang="fsharp">
        /// ApiOpenApi.definitionsFor getUser |> Map.keys // seq [ "User" ]
        /// </code></example>
        val definitionsFor:
          endpoint: Endpoint<'R,'Req,'Res> -> Map<string,SchemaInfo>

