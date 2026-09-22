
namespace FSharp



namespace Etymon
    
    /// <summary>
    /// A response that was not the success the endpoint promises.
    /// </summary>
    /// <remarks>
    /// <c>Declared</c> is the field worth branching on. A status the endpoint
    /// declared is one the server meant to send and the OpenAPI document lists; a
    /// status it did not is a surprise, and the two deserve different handling.
    /// </remarks>
    type ApiFailure =
        {
          
          /// The status that came back.
          Status: int
          
          /// Whether the endpoint declared this status.
          Declared: bool
          
          /// What the endpoint says this status means, when it declared it.
          Description: string option
          
          /// The body, as text, when there was one.
          Body: string option
        }
    
    /// Why a call did not produce a value.
    [<RequireQualifiedAccess; NoComparison>]
    type ApiError =
        
        /// The server answered, but not with success.
        | Failed of ApiFailure
        
        /// The server answered with the success status, but the body did not match
        /// the schema. The server and the client disagree about the contract.
        | Malformed of status: int * errors: ValidationErrors
        
        /// The request never completed.
        | Transport of exn
    
    /// Working with <see cref="T:Etymon.ApiError"/>.
    [<RequireQualifiedAccess>]
    module ApiError =
        
        /// <summary>What went wrong, in a sentence.</summary>
        /// <example><code lang="fsharp">
        /// ApiError.describe error
        /// // "the server returned 404 (No user with that id)"
        /// </code></example>
        val describe: error: ApiError -> string
    
    /// <summary>
    /// Calls an <see cref="T:Etymon.Endpoint`3"/> over HTTP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needs no web framework and no server: the endpoint declaration is enough to
    /// know the method, build the URL, encode the body and decode the response. A
    /// console application calling somebody else's Etymon-described API takes this
    /// package and nothing else.
    /// </para>
    /// <para>
    /// The URL is built by the same <c>Route</c> value the server matches with, so
    /// the client cannot ask for a path the server does not serve.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Client =
        
        val private toMethod: verb: HttpVerb -> System.Net.Http.HttpMethod
        
        val private interpret:
          endpoint: Endpoint<'R,'Req,'Res> ->
            status: int -> body: string -> Result<'Res,ApiError>
        
        val private send:
          http: System.Net.Http.HttpClient ->
            endpoint: Endpoint<'R,'Req,'Res> ->
            route: 'R ->
            query: (string * string) list ->
            content: System.Net.Http.HttpContent option ->
            System.Threading.Tasks.Task<Result<'Res,ApiError>>
        
        /// <summary>
        /// Calls an endpoint that has no request body.
        /// </summary>
        /// <example><code lang="fsharp">
        /// match! Client.call http getUser userId with
        /// | Ok user -> printfn "%s" user.Name
        /// | Error e -> eprintfn "%s" (ApiError.describe e)
        /// </code></example>
        val call:
          http: System.Net.Http.HttpClient ->
            endpoint: Endpoint<'R,unit,'Res> ->
            route: 'R -> System.Threading.Tasks.Task<Result<'Res,ApiError>>
        
        /// <summary>Calls an endpoint with query-string values.</summary>
        /// <example><code lang="fsharp">
        /// Client.callWithQuery http listUsers () [ "page", "2" ]
        /// </code></example>
        val callWithQuery:
          http: System.Net.Http.HttpClient ->
            endpoint: Endpoint<'R,unit,'Res> ->
            route: 'R ->
            query: (string * string) list ->
            System.Threading.Tasks.Task<Result<'Res,ApiError>>
        
        /// <summary>
        /// Calls an endpoint with a request body, encoded with the endpoint's own
        /// request schema.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Client.callWithBody http createUser () { Name = "Ada"; Role = "admin" }
        /// </code></example>
        val callWithBody:
          http: System.Net.Http.HttpClient ->
            endpoint: Endpoint<'R,'Req,'Res> ->
            route: 'R ->
            body: 'Req -> System.Threading.Tasks.Task<Result<'Res,ApiError>>

