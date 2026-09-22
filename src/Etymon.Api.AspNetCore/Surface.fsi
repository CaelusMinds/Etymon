
namespace FSharp



namespace Etymon
    
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
        
        val private jsonResponse:
          ctx: Microsoft.AspNetCore.Http.HttpContext ->
            status: int -> body: string option -> System.Threading.Tasks.Task
        
        /// One shape for every rejection, so a caller can parse a failure without
        /// knowing which endpoint produced it.
        val private writeValidationFailure:
          ctx: Microsoft.AspNetCore.Http.HttpContext ->
            errors: ValidationErrors -> System.Threading.Tasks.Task
        
        val private checkDeclared:
          endpoint: Endpoint<'R,'Req,'Res> -> status: int -> unit
        
        val private respond:
          endpoint: Endpoint<'R,'Req,'Res> ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            result: Handled<'Res> -> System.Threading.Tasks.Task
        
        /// ASP.NET's router hands back the captured values by name; Etymon's Route
        /// puts them back in declaration order so the endpoint's own Extract can
        /// read them.
        val private capturedValues:
          endpoint: Endpoint<'R,'Req,'Res> ->
            ctx: Microsoft.AspNetCore.Http.HttpContext -> string list
        
        val private bindRoute:
          endpoint: Endpoint<'R,'Req,'Res> ->
            ctx: Microsoft.AspNetCore.Http.HttpContext -> 'R option
        
        val private readBody:
          schema: Schema<'Req> ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            System.Threading.Tasks.Task<Validation<'Req>>
        
        /// <summary>
        /// Tells ASP.NET what this endpoint is, in the vocabulary ASP.NET already
        /// has.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Without this, an Etymon endpoint is a <c>RequestDelegate</c> on a route
        /// and nothing else: it appears in ASP.NET's OpenAPI document — and in
        /// anything else reading endpoint metadata — with no request type, no
        /// response type and no statuses. An application that already publishes a
        /// document would have to describe the endpoint a second time by hand, and
        /// the second description is the one that goes stale.
        /// </para>
        /// <para>
        /// Only what the framework understands natively is attached, so this needs
        /// no OpenAPI package. The constraints a schema carries — lengths, ranges,
        /// permitted values — do not reach the document this way, because ASP.NET
        /// derives its schemas by reflecting over the CLR type and the type does not
        /// know them. <c>ApiOpenApi</c> writes a document that does carry them. The
        /// two are not yet joined, and joining them needs a document transformer,
        /// which needs a dependency this package does not have.
        /// </para>
        /// </remarks>
        val private describe:
          endpoint: Endpoint<'R,'Req,'Res> ->
            builder: Microsoft.AspNetCore.Builder.RouteHandlerBuilder ->
            Microsoft.AspNetCore.Builder.RouteHandlerBuilder
        
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
        val map:
          endpoint: Endpoint<'R,unit,'Res> ->
            run: ('R ->
                    Microsoft.AspNetCore.Http.HttpContext ->
                    System.Threading.Tasks.Task<Handled<'Res>>) ->
            routes: Microsoft.AspNetCore.Routing.IEndpointRouteBuilder ->
            Microsoft.AspNetCore.Builder.RouteHandlerBuilder
        
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
        val mapWithBody:
          endpoint: Endpoint<'R,'Req,'Res> ->
            run: ('R ->
                    'Req ->
                    Microsoft.AspNetCore.Http.HttpContext ->
                    System.Threading.Tasks.Task<Handled<'Res>>) ->
            routes: Microsoft.AspNetCore.Routing.IEndpointRouteBuilder ->
            Microsoft.AspNetCore.Builder.RouteHandlerBuilder

