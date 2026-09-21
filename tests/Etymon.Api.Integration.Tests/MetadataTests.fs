/// What an Etymon endpoint tells ASP.NET about itself.
///
/// An application that already publishes an OpenAPI document reads endpoint
/// metadata to build it. If an Etymon endpoint carries none, it appears in that
/// document as a bare path with no request type, no response type and no
/// statuses -- so the application has to describe it a second time by hand, and
/// the hand-written second description is the one that goes stale.
///
/// These read the metadata back out of a real host rather than testing the
/// adapter's own source, because the question is what ASP.NET ended up holding.
module Etymon.Api.Integration.Tests.MetadataTests

open System.Linq
open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Etymon.Api.Integration.Tests.IntegrationTests

/// Every endpoint ASP.NET knows about, by name.
let private endpointsOf (host: IHost) =
    host.Services.GetRequiredService<EndpointDataSource>().Endpoints
    |> Seq.choose (fun e ->
        match e with
        | :? RouteEndpoint as route ->
            match route.Metadata.GetMetadata<IEndpointNameMetadata>() with
            | null -> None
            | named -> Some(named.EndpointName, route)
        | _ -> None
    )
    |> Map.ofSeq

let private withEndpoints (body: Map<string, RouteEndpoint> -> unit) =
    let host = startMinimalApi ()

    try
        body (endpointsOf host)
    finally
        host.Dispose()

let private statusesOf (endpoint: RouteEndpoint) =
    endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
    |> Seq.map (fun m -> m.StatusCode)
    |> Set.ofSeq

let tests =
    testList
        "what ASP.NET is told"
        [
            test "an endpoint is registered under its operation id" {
                withEndpoints (fun endpoints ->
                    Expect.isTrue (endpoints.ContainsKey "getUser") "named by the operation id, not by the route"
                    Expect.isTrue (endpoints.ContainsKey "createUser") "and so is the other one"
                )
            }

            test "the route template is the endpoint's own" {
                withEndpoints (fun endpoints ->
                    Expect.equal
                        (endpoints["getUser"].RoutePattern.RawText)
                        "/users/{id}"
                        "one declaration, and ASP.NET routes on it"
                )
            }

            test "every declared status reaches the metadata" {
                withEndpoints (fun endpoints ->
                    let statuses = statusesOf endpoints["getUser"]

                    // 404 matters as much as 200: a caller reading the document
                    // needs to know it can happen.
                    Expect.isTrue (statuses.Contains 200) "the success"
                    Expect.isTrue (statuses.Contains 404) "and the declared failure"
                )
            }

            test "an endpoint that declares only success says only that" {
                withEndpoints (fun endpoints ->
                    let statuses = statusesOf endpoints["strictUser"]
                    Expect.equal statuses (Set.ofList [ 200 ]) "nothing invented"
                )
            }

            test "a request body is declared where there is one" {
                withEndpoints (fun endpoints ->
                    let accepts = endpoints["createUser"].Metadata.GetMetadata<IAcceptsMetadata>()
                    Expect.isNotNull accepts "the POST accepts a body"

                    Expect.contains accepts.ContentTypes "application/json" "and says which content type it takes"
                )
            }

            test "a GET is not said to accept a body" {
                withEndpoints (fun endpoints ->
                    // Declaring one would tell the document about a body the
                    // endpoint will never read.
                    Expect.isNull (endpoints["getUser"].Metadata.GetMetadata<IAcceptsMetadata>()) "nothing to accept"
                )
            }

            test "the rejection the adapter performs itself is declared" {
                withEndpoints (fun endpoints ->
                    // The adapter answers 422 for a body the schema refuses,
                    // before the handler runs. The endpoint never declared it,
                    // so nothing else would put it in the document -- and a
                    // caller would meet it for the first time in production.
                    Expect.isTrue
                        (statusesOf endpoints["createUser"] |> Set.contains 422)
                        "422 is part of the contract"
                )
            }

            test "summary and tag are carried across" {
                withEndpoints (fun endpoints ->
                    let endpoint = endpoints["getUser"]
                    let summary = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()
                    Expect.isNotNull summary "there is a summary"
                    Expect.equal summary.Summary "Fetches one user by id" "and it is the endpoint's own"

                    let tags = endpoint.Metadata.GetMetadata<ITagsMetadata>()
                    Expect.isNotNull tags "there are tags"
                    Expect.contains tags.Tags "Users" "and the endpoint's tag is among them"
                )
            }
        ]
