namespace Etymon

open System
open System.Net.Http
open System.Text
open System.Threading.Tasks

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
    let describe (error: ApiError) =
        match error with
        | ApiError.Failed failure ->
            let meaning =
                match failure.Description with
                | Some text -> $" (%s{text})"
                | None when not failure.Declared -> " (which this endpoint does not declare)"
                | None -> ""

            $"the server returned %d{failure.Status}%s{meaning}"
        | ApiError.Malformed(status, errors) ->
            let problems = errors |> ValidationErrors.toList |> List.map (fun e -> e.ToString())

            $"the server returned %d{status}, but the body did not match the schema: "
            + String.Join("; ", problems)
        | ApiError.Transport exn -> $"the request did not complete: %s{exn.Message}"

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

    let private toMethod (verb: HttpVerb) =
        match verb with
        | HttpVerb.Get -> HttpMethod.Get
        | HttpVerb.Post -> HttpMethod.Post
        | HttpVerb.Put -> HttpMethod.Put
        | HttpVerb.Patch -> HttpMethod.Patch
        | HttpVerb.Delete -> HttpMethod.Delete

    let private interpret (endpoint: Endpoint<'R, 'Req, 'Res>) (status: int) (body: string) : Result<'Res, ApiError> =
        if status = endpoint.SuccessStatus then
            match Schema.fromJson endpoint.Response body with
            | Ok value -> Ok value
            // The server said this succeeded and then sent something the schema
            // rejects. That is the two sides disagreeing about the contract, not
            // an ordinary failure, so it is reported as its own case.
            | Error errors -> Error(ApiError.Malformed(status, errors))
        else
            let declared = endpoint.Responses |> List.tryFind (fun r -> r.Status = status)

            Error(
                ApiError.Failed
                    {
                        Status = status
                        Declared = declared.IsSome
                        Description = declared |> Option.map (fun r -> r.Description)
                        Body = if String.IsNullOrWhiteSpace body then None else Some body
                    }
            )

    let private send
        (http: HttpClient)
        (endpoint: Endpoint<'R, 'Req, 'Res>)
        (route: 'R)
        (query: (string * string) list)
        (content: HttpContent option)
        : Task<Result<'Res, ApiError>>
        =
        task {
            try
                let url = Api.toUrl endpoint route query
                use request = new HttpRequestMessage(toMethod endpoint.Verb, url)

                match content with
                | Some body -> request.Content <- body
                | None -> ()

                use! response = http.SendAsync request
                let! body = response.Content.ReadAsStringAsync()
                return interpret endpoint (int response.StatusCode) body
            with exn ->
                return Error(ApiError.Transport exn)
        }

    /// <summary>
    /// Calls an endpoint that has no request body.
    /// </summary>
    /// <example><code lang="fsharp">
    /// match! Client.call http getUser userId with
    /// | Ok user -> printfn "%s" user.Name
    /// | Error e -> eprintfn "%s" (ApiError.describe e)
    /// </code></example>
    let call (http: HttpClient) (endpoint: Endpoint<'R, unit, 'Res>) (route: 'R) = send http endpoint route [] None

    /// <summary>Calls an endpoint with query-string values.</summary>
    /// <example><code lang="fsharp">
    /// Client.callWithQuery http listUsers () [ "page", "2" ]
    /// </code></example>
    let callWithQuery
        (http: HttpClient)
        (endpoint: Endpoint<'R, unit, 'Res>)
        (route: 'R)
        (query: (string * string) list)
        =
        send http endpoint route query None

    /// <summary>
    /// Calls an endpoint with a request body, encoded with the endpoint's own
    /// request schema.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Client.callWithBody http createUser () { Name = "Ada"; Role = "admin" }
    /// </code></example>
    let callWithBody (http: HttpClient) (endpoint: Endpoint<'R, 'Req, 'Res>) (route: 'R) (body: 'Req) =
        match endpoint.Request with
        | None -> failwithf "The endpoint '%s' has no request body; use Client.call instead." endpoint.OperationId
        | Some requestSchema ->
            let content =
                new StringContent(Schema.toJson requestSchema body, Encoding.UTF8, "application/json")

            send http endpoint route [] (Some(content :> HttpContent))
