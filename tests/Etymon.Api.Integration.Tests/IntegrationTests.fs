/// End-to-end tests: the generated client, over real HTTP, against a real
/// server — once through Giraffe and once through minimal APIs.
///
/// Everything else in this suite is a pure function and can be tested as one.
/// These adapters are not: they touch HttpContext, streams and status codes, and
/// a bug in one of them compiles perfectly well. The first draft of the Giraffe
/// adapter wrote its response and then called `next` anyway, which the compiler
/// accepted and only a real request would have caught.
module Etymon.Api.Integration.Tests.IntegrationTests

open System
open System.Net.Http
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Giraffe
open Etymon

// ---- the API both servers implement -----------------------------------------

type User =
    { Id: Guid; Name: string; Role: string }

type NewUser = { Name: string; Role: string }
type Problem = { Detail: string }

module Contracts =
    let userSchema =
        Schema.object "User" {
            let! id = Schema.required "id" Schema.guid (fun (u: User) -> u.Id)

            and! name =
                Schema.required
                    "name"
                    (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 40)))
                    (fun (u: User) -> u.Name)

            and! role =
                Schema.required
                    "role"
                    (Schema.string |> Schema.constrain (Check.oneOf [ "admin"; "member" ]))
                    (fun (u: User) -> u.Role)

            return { Id = id; Name = name; Role = role }
        }

    let newUserSchema =
        Schema.object "NewUser" {
            let! name =
                Schema.required
                    "name"
                    (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 40)))
                    (fun (n: NewUser) -> n.Name)

            and! role =
                Schema.required
                    "role"
                    (Schema.string |> Schema.constrain (Check.oneOf [ "admin"; "member" ]))
                    (fun (n: NewUser) -> n.Role)

            return { Name = name; Role = role }
        }

    let problemSchema =
        Schema.object "Problem" {
            let! detail = Schema.required "detail" Schema.string (fun (p: Problem) -> p.Detail)
            return { Detail = detail }
        }

module Endpoints =
    let getUser =
        Api.get "getUser" (Route.one [ "users" ] (Param.guid "id") []) Contracts.userSchema
        |> Api.failsWith 404 Contracts.problemSchema "No user with that id"
        |> Api.summary "Fetches one user by id"
        |> Api.tag "Users"

    let createUser =
        Api.post "createUser" (Route.literal [ "users" ]) Contracts.newUserSchema Contracts.userSchema

    /// Declares only 200, so returning anything else is a bug the adapters catch.
    let strictUser =
        Api.get "strictUser" (Route.one [ "strict" ] (Param.guid "id") []) Contracts.userSchema

let private knownId = Guid "11111111-1111-1111-1111-111111111111"
let private missingId = Guid "22222222-2222-2222-2222-222222222222"

let private knownUser =
    {
        Id = knownId
        Name = "Ada"
        Role = "admin"
    }

// ---- the two servers ---------------------------------------------------------

let private giraffeApp: HttpHandler =
    choose
        [ // An ordinary Giraffe route, to prove Etymon endpoints sit beside
            // rather than instead of what is already there.
            route "/health" >=> text "ok"

            GiraffeAdapter.handle
                Endpoints.getUser
                (fun userId _ctx ->
                    task {
                        if userId = knownId then
                            return ApiResult.Ok knownUser
                        else
                            return
                                ApiResult.Failed(
                                    404,
                                    GiraffeAdapter.body Contracts.problemSchema { Detail = "No user with that id" }
                                )
                    }
                )

            GiraffeAdapter.handleWithBody
                Endpoints.createUser
                (fun () newUser _ctx ->
                    task {
                        return
                            ApiResult.Ok
                                {
                                    Id = knownId
                                    Name = newUser.Name
                                    Role = newUser.Role
                                }
                    }
                )

            GiraffeAdapter.handle Endpoints.strictUser (fun _ _ctx -> task { return ApiResult.Failed(418, None) })

            setStatusCode 404 >=> text "not found"
        ]

let private startGiraffe () : IHost =
    let host =
        Host
            .CreateDefaultBuilder()
            .ConfigureWebHostDefaults(fun webHost ->
                webHost
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddGiraffe() |> ignore)
                    .Configure(fun app -> app.UseGiraffe giraffeApp)
                |> ignore
            )
            .Build()

    host.Start()
    host

let startMinimalApi () : IHost =
    let host =
        Host
            .CreateDefaultBuilder()
            .ConfigureWebHostDefaults(fun webHost ->
                webHost
                    .UseTestServer()
                    .Configure(fun app ->
                        app.UseRouting() |> ignore

                        app.UseEndpoints(fun routes ->
                            routes
                            |> MinimalApi.map
                                Endpoints.getUser
                                (fun userId _ctx ->
                                    task {
                                        if userId = knownId then
                                            return Handled.Ok knownUser
                                        else
                                            return
                                                Handled.Failed(
                                                    404,
                                                    Some(
                                                        Schema.toJson
                                                            Contracts.problemSchema
                                                            { Detail = "No user with that id" }
                                                    )
                                                )
                                    }
                                )
                            |> ignore

                            routes
                            |> MinimalApi.mapWithBody
                                Endpoints.createUser
                                (fun () newUser _ctx ->
                                    task {
                                        return
                                            Handled.Ok
                                                {
                                                    Id = knownId
                                                    Name = newUser.Name
                                                    Role = newUser.Role
                                                }
                                    }
                                )
                            |> ignore

                            routes
                            |> MinimalApi.map
                                Endpoints.strictUser
                                (fun _ _ctx -> task { return Handled.Failed(418, None) })
                            |> ignore
                        )
                        |> ignore
                    )
                |> ignore
            )
            .Build()

    host.Start()
    host

/// Runs the same body against both adapters, so neither can quietly diverge
/// from the other.
let private againstBothServers name (body: HttpClient -> Task<unit>) =
    [ "Giraffe", startGiraffe; "minimal APIs", startMinimalApi ]
    |> List.map (fun (label, start) ->
        testTask $"%s{name} (%s{label})" {
            // Explicit disposal rather than `use`: inside a task expression F#
            // resolves `use` to IAsyncDisposable, which neither IHost nor
            // HttpClient implements.
            let host = start ()
            let client: HttpClient = host.GetTestClient()

            try
                do! body client
            finally
                client.Dispose()
                host.Dispose()
        }
    )

let tests =
    testList
        "end to end"
        [
            yield!
                againstBothServers
                    "a known id comes back through the generated client"
                    (fun client ->
                        task {
                            match! Client.call client Endpoints.getUser knownId with
                            | Ok user -> Expect.equal user knownUser "the client decoded what the server encoded"
                            | Error e -> failtest (ApiError.describe e)
                        }
                    )

            yield!
                againstBothServers
                    "a declared failure arrives as a declared failure"
                    (fun client ->
                        task {
                            match! Client.call client Endpoints.getUser missingId with
                            | Ok _ -> failtest "should not have found a user"
                            | Error(ApiError.Failed failure) ->
                                Expect.equal failure.Status 404 "the status"
                                Expect.isTrue failure.Declared "which the endpoint declared"

                                Expect.equal
                                    failure.Description
                                    (Some "No user with that id")
                                    "so the client knows what it means without asking"
                            | Error other -> failtest (ApiError.describe other)
                        }
                    )

            yield!
                againstBothServers
                    "a request body round-trips"
                    (fun client ->
                        task {
                            let newUser = { Name = "Grace"; Role = "member" }

                            match! Client.callWithBody client Endpoints.createUser () newUser with
                            | Ok created ->
                                Expect.equal created.Name "Grace" "the server saw what the client sent"
                                Expect.equal created.Role "member" "all of it"
                            | Error e -> failtest (ApiError.describe e)
                        }
                    )

            yield!
                againstBothServers
                    "an invalid body is rejected before the handler runs"
                    (fun client ->
                        task {
                            // "emperor" is not one of the roles the schema admits, and
                            // the name is empty. Both rules are in the schema, and
                            // neither handler contains a line of validation.
                            let body =
                                new StringContent(
                                    """{"name":"","role":"emperor"}""",
                                    Text.Encoding.UTF8,
                                    "application/json"
                                )

                            let! (response: HttpResponseMessage) = client.PostAsync("/users", body)
                            let! text = response.Content.ReadAsStringAsync()

                            Expect.equal (int response.StatusCode) 422 "rejected"
                            Expect.stringContains text "name" "naming the first bad field"
                            Expect.stringContains text "role" "and the second"
                            Expect.stringContains text "must be one of: admin, member" "with what was wrong"
                        }
                    )

            yield!
                againstBothServers
                    "a path value that does not parse is not a match"
                    (fun client ->
                        task {
                            // /users/hello against /users/{id:guid}. The handler must
                            // never be called with something it cannot use.
                            let! (response: HttpResponseMessage) = client.GetAsync("/users/hello")

                            Expect.isTrue (int response.StatusCode >= 400) "refused rather than handed to the handler"
                        }
                    )

            yield!
                againstBothServers
                    "an undeclared status is refused"
                    (fun client ->
                        task {
                            // The handler returns 418, which strictUser never declares.
                            // A status the OpenAPI document omits and the generated
                            // client cannot handle is a bug in the handler, so the
                            // adapter raises rather than forwarding it — the request
                            // fails loudly instead of quietly teaching a caller about a
                            // status nothing documents.
                            let mutable refusal = None

                            try
                                let! (response: HttpResponseMessage) = client.GetAsync($"/strict/{knownId}")

                                Expect.notEqual
                                    (int response.StatusCode)
                                    418
                                    "an undeclared status must not reach the caller"
                            with exn ->
                                refusal <- Some(exn.ToString())

                            match refusal with
                            | Some message ->
                                Expect.stringContains message "418" "the refusal names the status"

                                Expect.stringContains
                                    message
                                    "does not declare"
                                    "and says what is wrong rather than failing opaquely"
                            | None -> ()
                        }
                    )

            testTask "Giraffe routes that were already there still work" {
                let host = startGiraffe ()
                let client: HttpClient = host.GetTestClient()

                try
                    let! (response: HttpResponseMessage) = client.GetAsync "/health"
                    let! text = response.Content.ReadAsStringAsync()
                    Expect.equal (int response.StatusCode) 200 "still served"
                    Expect.equal text "ok" "by Giraffe, untouched"
                finally
                    client.Dispose()
                    host.Dispose()
            }

            testTask "the two adapters agree byte for byte" {
                // The point of one declaration and several interpreters.
                let giraffe = startGiraffe ()
                let minimal = startMinimalApi ()
                let giraffeClient: HttpClient = giraffe.GetTestClient()
                let minimalClient: HttpClient = minimal.GetTestClient()

                try
                    let! (fromGiraffe: HttpResponseMessage) = giraffeClient.GetAsync($"/users/{knownId}")
                    let! giraffeBody = fromGiraffe.Content.ReadAsStringAsync()
                    let! (fromMinimal: HttpResponseMessage) = minimalClient.GetAsync($"/users/{knownId}")
                    let! minimalBody = fromMinimal.Content.ReadAsStringAsync()

                    Expect.equal giraffeBody minimalBody "the same bytes from both"

                    Expect.equal (int fromGiraffe.StatusCode) (int fromMinimal.StatusCode) "and the same status"
                finally
                    giraffeClient.Dispose()
                    minimalClient.Dispose()
                    giraffe.Dispose()
                    minimal.Dispose()
            }
        ]
