namespace Etymon

open System
open System.Text.Json.Nodes

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
/// Where an application already has ASP.NET Core's own OpenAPI generation, use
/// <c>Etymon.Api.AspNetCore</c> to feed this into that pipeline instead of
/// emitting a competing document. Replacing a pipeline somebody already has is a
/// much bigger ask than adding to it.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module ApiOpenApi =

    [<AbstractClass; Sealed>]
    type private Node =
        static member Of(value: string) : JsonNode = JsonValue.Create value
        static member Of(value: int) : JsonNode = JsonValue.Create value
        static member Of(value: bool) : JsonNode = JsonValue.Create value

    let private kindSchema (kind: ParamKind) =
        let schema = JsonObject()

        match kind with
        | ParamKind.String -> schema.Add("type", Node.Of "string")
        | ParamKind.Int ->
            schema.Add("type", Node.Of "integer")
            schema.Add("format", Node.Of "int32")
        | ParamKind.Int64 ->
            schema.Add("type", Node.Of "integer")
            schema.Add("format", Node.Of "int64")
        | ParamKind.Guid ->
            schema.Add("type", Node.Of "string")
            schema.Add("format", Node.Of "uuid")
        | ParamKind.Bool -> schema.Add("type", Node.Of "boolean")

        schema

    /// A named schema becomes a reference so it is written once. Anything else is
    /// inlined, structure and all — a list has to carry its element type, or the
    /// document says "an array of something" and a generated client has nothing
    /// to work from.
    let rec private reference (info: SchemaInfo) : JsonObject =
        let schema = JsonObject()

        match SchemaInfo.name info with
        | Some name -> schema.Add("$ref", Node.Of("#/components/schemas/" + name))
        | None ->
            match SchemaInfo.strip info with
            | SPrim PrimKind.String
            | SPrim PrimKind.Guid
            | SPrim PrimKind.DateTimeOffset
            | SPrim PrimKind.DateOnly
            | SPrim PrimKind.TimeOnly
            | SPrim PrimKind.TimeSpan
            | SPrim PrimKind.Bytes -> schema.Add("type", Node.Of "string")
            | SPrim PrimKind.Int
            | SPrim PrimKind.Int64 -> schema.Add("type", Node.Of "integer")
            | SPrim PrimKind.Float
            | SPrim PrimKind.Decimal -> schema.Add("type", Node.Of "number")
            | SPrim PrimKind.Bool -> schema.Add("type", Node.Of "boolean")
            | SPrim PrimKind.Raw -> ()
            | SNullable inner ->
                let rendered = reference inner

                if rendered.ContainsKey "$ref" then
                    let choices = JsonArray()
                    choices.Add rendered
                    let nullChoice = JsonObject()
                    nullChoice.Add("type", Node.Of "null")
                    choices.Add nullChoice
                    schema.Add("anyOf", choices)
                else
                    for KeyValue(key, value) in rendered do
                        schema.Add(key, value.DeepClone())

                    schema.Add("nullable", Node.Of true)
            | SList inner ->
                schema.Add("type", Node.Of "array")
                schema.Add("items", reference inner)
            | SMap inner ->
                schema.Add("type", Node.Of "object")
                schema.Add("additionalProperties", reference inner)
            | _ -> schema.Add("type", Node.Of "object")

        schema

    let private jsonContent (info: SchemaInfo) =
        let media = JsonObject()
        media.Add("schema", reference info)
        let content = JsonObject()
        content.Add("application/json", media)
        content

    let private parametersFor (endpoint: Endpoint<'R, 'Req, 'Res>) =
        let parameters = JsonArray()

        for name, kind in Route.parameters endpoint.Route do
            let parameter = JsonObject()
            parameter.Add("name", Node.Of name)
            parameter.Add("in", Node.Of "path")
            // A path parameter is always required; OpenAPI says so explicitly.
            parameter.Add("required", Node.Of true)
            parameter.Add("schema", kindSchema kind)
            parameters.Add parameter

        for spec in endpoint.Query do
            let parameter = JsonObject()
            parameter.Add("name", Node.Of spec.Name)
            parameter.Add("in", Node.Of "query")
            parameter.Add("required", Node.Of spec.Required)
            parameter.Add("schema", kindSchema spec.Kind)

            match spec.Description with
            | Some text -> parameter.Add("description", Node.Of text)
            | None -> ()

            parameters.Add parameter

        parameters

    let private operationFor (endpoint: Endpoint<'R, 'Req, 'Res>) =
        let operation = JsonObject()
        operation.Add("operationId", Node.Of endpoint.OperationId)

        match endpoint.Summary with
        | Some text -> operation.Add("summary", Node.Of text)
        | None -> ()

        match endpoint.Tags with
        | [] -> ()
        | tags ->
            let array = JsonArray()

            for t in tags do
                array.Add(Node.Of t)

            operation.Add("tags", array)

        let parameters = parametersFor endpoint

        if parameters.Count > 0 then
            operation.Add("parameters", parameters)

        match endpoint.Request with
        | Some request ->
            let body = JsonObject()
            body.Add("required", Node.Of true)
            body.Add("content", jsonContent request.Info)
            operation.Add("requestBody", body)
        | None -> ()

        let responses = JsonObject()

        // Sorted by status so that the document is byte-identical between runs,
        // which is what makes a snapshot of it worth keeping.
        for response in endpoint.Responses |> List.sortBy (fun r -> r.Status) do
            let entry = JsonObject()
            entry.Add("description", Node.Of response.Description)

            match response.Body with
            | Some info -> entry.Add("content", jsonContent info)
            | None -> ()

            responses.Add(string response.Status, entry)

        operation.Add("responses", responses)
        operation

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
    let pathsOf (entries: (string * string * JsonObject) list) =
        let paths = JsonObject()

        for template, method, operation in entries |> List.sortBy (fun (t, m, _) -> t, m) do
            let entry =
                match paths[template] with
                | :? JsonObject as existing -> existing
                | _ ->
                    let created = JsonObject()
                    paths[template] <- created
                    created

            entry.Add(method.ToLowerInvariant(), operation)

        paths

    /// <summary>
    /// One endpoint rendered as a path template, a method and an operation,
    /// ready to be combined with others.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let entries = [ ApiOpenApi.entryFor getUser; ApiOpenApi.entryFor createUser ]
    /// ApiOpenApi.pathsOf entries
    /// </code></example>
    let entryFor (endpoint: Endpoint<'R, 'Req, 'Res>) =
        Route.template endpoint.Route, HttpVerb.name endpoint.Verb, operationFor endpoint

    /// <summary>
    /// Every named schema an endpoint refers to, for the <c>components/schemas</c>
    /// section.
    /// </summary>
    /// <example><code lang="fsharp">
    /// ApiOpenApi.definitionsFor getUser |> Map.keys // seq [ "User" ]
    /// </code></example>
    let definitionsFor (endpoint: Endpoint<'R, 'Req, 'Res>) =
        let fromRequest =
            match endpoint.Request with
            | Some request -> SchemaInfo.definitions request.Info
            | None -> Map.empty

        let fromResponses =
            endpoint.Responses
            |> List.choose (fun r -> r.Body)
            |> List.map SchemaInfo.definitions
            |> List.fold (fun acc m -> Map.fold (fun inner k v -> Map.add k v inner) acc m) Map.empty

        Map.fold (fun acc k v -> Map.add k v acc) fromResponses fromRequest
