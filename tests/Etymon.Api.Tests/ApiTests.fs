module Etymon.Api.Tests.ApiTests

open System
open Expecto
open Etymon
open Etymon.Tests

// ---- the API under test -----------------------------------------------------

type User =
    {
        Id: Guid
        Name: string
        Email: string option
        Role: string
    }

type NewUser = { Name: string; Role: string }
type Problem = { Detail: string }

module Contracts =
    let userSchema =
        Schema.object "User" {
            let! id = Schema.required "id" Schema.guid (fun (u: User) -> u.Id)

            and! name =
                Schema.required
                    "name"
                    (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 100)))
                    (fun (u: User) -> u.Name)

            and! email =
                Schema.optional
                    "email"
                    (Schema.string |> Schema.constrain (Check.format Format.Email))
                    (fun (u: User) -> u.Email)

            and! role =
                Schema.required
                    "role"
                    (Schema.string |> Schema.constrain (Check.oneOf [ "admin"; "member" ]))
                    (fun (u: User) -> u.Role)
                |> Schema.doc "What the user may do"

            return
                {
                    Id = id
                    Name = name
                    Email = email
                    Role = role
                }
        }

    let newUserSchema =
        Schema.object "NewUser" {
            let! name = Schema.required "name" Schema.string (fun (n: NewUser) -> n.Name)
            and! role = Schema.required "role" Schema.string (fun n -> n.Role)
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
        |> Api.summary "Fetches one user by id"
        |> Api.tag "Users"
        |> Api.failsWith 404 Contracts.problemSchema "No user with that id"

    let listUsers =
        Api.get "listUsers" (Route.literal [ "users" ]) (Schema.list Contracts.userSchema)
        |> Api.query (Param.int "page")
        |> Api.optionalQuery (Param.bool "includeArchived")
        |> Api.tag "Users"

    let createUser =
        Api.post "createUser" (Route.literal [ "users" ]) Contracts.newUserSchema Contracts.userSchema
        |> Api.tag "Users"
        |> Api.failsWith 422 Contracts.problemSchema "The user could not be accepted"

    let getOrder =
        Api.get
            "getOrder"
            (Route.two [ "users" ] (Param.guid "id") [ "orders" ] (Param.int "orderId") [])
            Contracts.userSchema

    let deleteUser =
        Api.delete "deleteUser" (Route.one [ "users" ] (Param.guid "id") []) Api.noBody
        |> Api.failsWithNoBody 404 "No user with that id"

let private sampleId = Guid "9d1c4b2a-0000-0000-0000-000000000001"

let tests =
    testList
        "Api"
        [
            testList
                "routes"
                [
                    test "a path with no parameters" {
                        let route = Route.literal [ "users" ]
                        Expect.equal (Route.template route) "/users" "template"
                        Expect.equal (Route.toPath route ()) "/users" "concrete path"
                        Expect.equal (Route.matchPath route "/users") (Some()) "matches"
                        Expect.equal (Route.matchPath route "/orders") None "and only itself"
                    }

                    test "a path with one parameter round-trips" {
                        let route = Route.one [ "users" ] (Param.guid "id") []
                        Expect.equal (Route.template route) "/users/{id}" "template"

                        let path = Route.toPath route sampleId
                        Expect.equal (Route.matchPath route path) (Some sampleId) "what goes in comes out"
                    }

                    test "a parameter in the middle of a path" {
                        let route = Route.one [ "users" ] (Param.guid "id") [ "orders" ]
                        Expect.equal (Route.template route) "/users/{id}/orders" "template"
                        Expect.equal (Route.matchPath route (Route.toPath route sampleId)) (Some sampleId) "round-trips"
                    }

                    test "two parameters" {
                        let route =
                            Route.two [ "users" ] (Param.guid "id") [ "orders" ] (Param.int "orderId") []

                        Expect.equal (Route.template route) "/users/{id}/orders/{orderId}" "template"

                        let value = (sampleId, 42)
                        Expect.equal (Route.matchPath route (Route.toPath route value)) (Some value) "both come back"
                    }

                    test "a path of the wrong length does not match" {
                        let route = Route.one [ "users" ] (Param.guid "id") []
                        Expect.equal (Route.matchPath route "/users") None "too short"
                        Expect.equal (Route.matchPath route $"/users/{sampleId}/orders") None "too long"
                    }

                    test "a parameter that does not parse does not match" {
                        // A guid route must not match /users/hello, or the handler
                        // would be called with something it cannot use.
                        let route = Route.one [ "users" ] (Param.guid "id") []
                        Expect.equal (Route.matchPath route "/users/hello") None "not a uuid"
                    }

                    test "a literal segment is compared exactly" {
                        let route = Route.one [ "users" ] (Param.guid "id") []
                        Expect.equal (Route.matchPath route $"/Users/{sampleId}") None "case matters in a path"
                    }

                    test "parameters are reported in order, with their kinds" {
                        let route =
                            Route.two [ "users" ] (Param.guid "id") [ "orders" ] (Param.int "orderId") []

                        Expect.equal
                            (Route.parameters route)
                            [ "id", ParamKind.Guid; "orderId", ParamKind.Int ]
                            "which is what a generator reads"
                    }

                    test "path segments are escaped" {
                        let route = Route.one [ "files" ] (Param.string "name") []
                        let path = Route.toPath route "a b/c"
                        Expect.isFalse (path.EndsWith "a b/c") "the value is escaped, not spliced in"
                        Expect.equal (Route.matchPath route path) (Some "a b/c") "and unescapes back"
                    }

                    testProperty "any guid round-trips through a route"
                    <| fun () ->
                        let route = Route.one [ "users" ] (Param.guid "id") []
                        let id = Guid.NewGuid()
                        Route.matchPath route (Route.toPath route id) = Some id

                    testProperty "any int round-trips through a route"
                    <| fun (value: int) ->
                        let route = Route.one [ "page" ] (Param.int "n") []
                        Route.matchPath route (Route.toPath route value) = Some value
                ]

            testList
                "endpoints"
                [
                    test "a GET carries its method, route and success status" {
                        Expect.equal Endpoints.getUser.Verb HttpVerb.Get "method"
                        Expect.equal (Api.template Endpoints.getUser) "/users/{id}" "path"
                        Expect.equal Endpoints.getUser.SuccessStatus 200 "status"
                        Expect.isNone Endpoints.getUser.Request "and no request body"
                    }

                    test "a POST defaults to 201 and carries a request body" {
                        Expect.equal Endpoints.createUser.Verb HttpVerb.Post "method"
                        Expect.equal Endpoints.createUser.SuccessStatus 201 "created"
                        Expect.isSome Endpoints.createUser.Request "with a body"
                    }

                    test "declared failures are data" {
                        // Declared rather than discovered, so a generated client can
                        // handle every case the endpoint admits to.
                        let statuses = Endpoints.getUser.Responses |> List.map (fun r -> r.Status)
                        Expect.equal statuses [ 200; 404 ] "success and the one declared failure"
                    }

                    test "a failure with no body is still declared" {
                        let notFound = Endpoints.deleteUser.Responses |> List.find (fun r -> r.Status = 404)
                        Expect.equal notFound.Body None "no body"
                        Expect.equal notFound.Description "No user with that id" "but it is documented"
                    }

                    test "query parameters record whether they are required" {
                        let required = Endpoints.listUsers.Query |> List.filter (fun q -> q.Required)
                        let optional = Endpoints.listUsers.Query |> List.filter (fun q -> not q.Required)
                        Expect.equal (required |> List.map (fun q -> q.Name)) [ "page" ] "required"
                        Expect.equal (optional |> List.map (fun q -> q.Name)) [ "includeArchived" ] "optional"
                    }

                    test "a URL is built from the same declaration that matches it" {
                        let url = Api.toUrl Endpoints.listUsers () [ "page", "2" ]
                        Expect.equal url "/users?page=2" "path and query"
                    }

                    test "query values are escaped" {
                        let url = Api.toUrl Endpoints.listUsers () [ "q", "a&b=c" ]
                        Expect.isFalse (url.Contains "a&b=c") "not spliced in raw"
                        Expect.stringContains url "a%26b%3Dc" "escaped"
                    }
                ]

            testList
                "OpenAPI paths"
                [
                    test "an operation carries its id, method and parameters" {
                        let template, method, operation = ApiOpenApi.entryFor Endpoints.getUser
                        Expect.equal template "/users/{id}" "the OpenAPI path template"
                        Expect.equal method "GET" "the method"
                        Expect.stringContains (operation.ToJsonString()) "\"operationId\":\"getUser\"" "the id"
                    }

                    test "a path parameter is always required" {
                        let _, _, operation = ApiOpenApi.entryFor Endpoints.getUser
                        let json = operation.ToJsonString()
                        Expect.stringContains json "\"in\":\"path\"" "declared as a path parameter"
                        Expect.stringContains json "\"format\":\"uuid\"" "with its kind"
                    }

                    test "response bodies reference the component schema" {
                        let _, _, operation = ApiOpenApi.entryFor Endpoints.getUser

                        Expect.stringContains
                            (operation.ToJsonString())
                            "#/components/schemas/User"
                            "written once and referenced"
                    }

                    test "every declared response appears" {
                        let _, _, operation = ApiOpenApi.entryFor Endpoints.getUser
                        let json = operation.ToJsonString()
                        Expect.stringContains json "\"200\"" "success"
                        Expect.stringContains json "\"404\"" "and the declared failure"
                    }

                    test "endpoints sharing a path are merged into one entry" {
                        let paths =
                            ApiOpenApi.pathsOf
                                [
                                    ApiOpenApi.entryFor Endpoints.listUsers
                                    ApiOpenApi.entryFor Endpoints.createUser
                                ]

                        let users = paths["/users"].ToJsonString()
                        Expect.stringContains users "\"get\"" "both methods"
                        Expect.stringContains users "\"post\"" "on one path"
                    }

                    test "definitions collect every schema an endpoint touches" {
                        let names =
                            ApiOpenApi.definitionsFor Endpoints.createUser
                            |> Map.keys
                            |> List.ofSeq
                            |> List.sort

                        Expect.equal names [ "NewUser"; "Problem"; "User" ] "request, response and failure"
                    }

                    test "output is deterministic" {
                        let render () =
                            ApiOpenApi.pathsOf
                                [
                                    ApiOpenApi.entryFor Endpoints.listUsers
                                    ApiOpenApi.entryFor Endpoints.createUser
                                    ApiOpenApi.entryFor Endpoints.getUser
                                ]
                            |> fun p -> p.ToJsonString()

                        Expect.equal (render ()) (render ()) "the same every time"
                    }
                ]

            testList
                "TypeScript"
                [
                    test "a record becomes an interface with readonly fields" {
                        let ts = TypeScript.emitOne Contracts.userSchema
                        Expect.stringContains ts "export interface User {" "an interface"
                        Expect.stringContains ts "readonly id: string;" "a uuid is a string"
                        Expect.stringContains ts "readonly name: string;" "required"
                    }

                    test "optional and nullable are different things" {
                        // The reason the schema type keeps them apart at all.
                        let ts = TypeScript.emitOne Contracts.userSchema
                        Expect.stringContains ts "readonly email?: string;" "absent is ?:"

                        let nullableSchema =
                            Schema.object "Thing" {
                                let! value = Schema.required "value" (Schema.nullable Schema.string) id
                                return value
                            }

                        Expect.stringContains
                            (TypeScript.emitOne nullableSchema)
                            "readonly value: string | null;"
                            "present-but-empty is | null"
                    }

                    test "an enumeration becomes a union of literals" {
                        // So a typo is a compile error in the frontend too.
                        let ts = TypeScript.emitOne Contracts.userSchema
                        Expect.stringContains ts "readonly role: \"admin\" | \"member\";" "not a bare string"
                    }

                    test "a field description becomes a doc comment" {
                        let ts = TypeScript.emitOne Contracts.userSchema
                        Expect.stringContains ts "/** What the user may do */" "prose survives"
                    }

                    test "a union becomes a discriminated union" {
                        let shapeSchema =
                            Schema.union
                                "Shape"
                                "kind"
                                [
                                    Schema.case
                                        "circle"
                                        Schema.float
                                        Choice1Of2
                                        (function
                                        | Choice1Of2 v -> ValueSome v
                                        | _ -> ValueNone
                                        )
                                    Schema.caseUnit
                                        "point"
                                        (Choice2Of2())
                                        (function
                                        | Choice2Of2 _ -> true
                                        | _ -> false
                                        )
                                ]

                        let ts = TypeScript.emitOne shapeSchema

                        Expect.stringContains
                            ts
                            "readonly kind: \"circle\"; readonly value: number"
                            "a case with a payload"

                        Expect.stringContains ts "{ readonly kind: \"point\" }" "and one without"
                    }

                    test "nested types are emitted once and referenced by name" {
                        let addressSchema =
                            Schema.object "Address" {
                                let! street = Schema.required "street" Schema.string id
                                return street
                            }

                        let personSchema =
                            Schema.object "Person" {
                                let! address = Schema.required "address" addressSchema id
                                return address
                            }

                        let ts = TypeScript.emitOne personSchema
                        Expect.stringContains ts "export interface Address {" "emitted"
                        Expect.stringContains ts "readonly address: Address;" "and referenced"
                    }

                    test "output is deterministic" {
                        Expect.equal
                            (TypeScript.emitOne Contracts.userSchema)
                            (TypeScript.emitOne Contracts.userSchema)
                            "the same every time"
                    }
                ]

            testList
                "snapshots"
                [
                    test "the OpenAPI paths for a whole API" {
                        let paths =
                            ApiOpenApi.pathsOf
                                [
                                    ApiOpenApi.entryFor Endpoints.listUsers
                                    ApiOpenApi.entryFor Endpoints.createUser
                                    ApiOpenApi.entryFor Endpoints.getUser
                                    ApiOpenApi.entryFor Endpoints.deleteUser
                                ]

                        let options = Text.Json.JsonSerializerOptions(WriteIndented = true)
                        Snapshot.verify "openapi-paths" (paths.ToJsonString options)
                    }

                    test "the TypeScript declarations for the same API" {
                        Snapshot.verify
                            "typescript-declarations"
                            (TypeScript.emit
                                [
                                    Contracts.userSchema.Info
                                    Contracts.newUserSchema.Info
                                    Contracts.problemSchema.Info
                                ])
                    }
                ]
        ]
