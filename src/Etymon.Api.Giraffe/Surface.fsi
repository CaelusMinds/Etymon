
namespace FSharp



namespace Etymon
    
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
        
        val private matchesVerb:
          verb: HttpVerb -> ctx: Microsoft.AspNetCore.Http.HttpContext -> bool
        
        /// Writes a validation failure as a problem document. One shape for every
        /// rejection, so a caller can parse failures without knowing which endpoint
        /// produced them.
        val private writeValidationFailure:
          errors: ValidationErrors ->
            next: Giraffe.Core.HttpFunc ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            Giraffe.Core.HttpFuncResult
        
        val private readBody:
          schema: Schema<'Req> ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            System.Threading.Tasks.Task<Validation<'Req>>
        
        /// Writes the response and stops. It does not call `next`: this handler has
        /// produced the response, so passing the request further down the pipeline
        /// would let something else write over it.
        val private respond:
          endpoint: Endpoint<'R,'Req,'Res> ->
            result: ApiResult<'Res> ->
            _next: Giraffe.Core.HttpFunc ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            Giraffe.Core.HttpFuncResult
        
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
        val handle:
          endpoint: Endpoint<'R,unit,'Res> ->
            run: ('R ->
                    Microsoft.AspNetCore.Http.HttpContext ->
                    System.Threading.Tasks.Task<ApiResult<'Res>>) ->
            next: Giraffe.Core.HttpFunc ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            Giraffe.Core.HttpFuncResult
        
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
        val handleWithBody:
          endpoint: Endpoint<'R,'Req,'Res> ->
            run: ('R ->
                    'Req ->
                    Microsoft.AspNetCore.Http.HttpContext ->
                    System.Threading.Tasks.Task<ApiResult<'Res>>) ->
            next: Giraffe.Core.HttpFunc ->
            ctx: Microsoft.AspNetCore.Http.HttpContext ->
            Giraffe.Core.HttpFuncResult
        
        /// <summary>
        /// Encodes a declared failure body, so a handler does not hand-write JSON.
        /// </summary>
        /// <example><code lang="fsharp">
        /// return ApiResult.Failed(404, GiraffeAdapter.body problemSchema { Detail = "No such user" })
        /// </code></example>
        val body: schema: Schema<'E> -> value: 'E -> string option

