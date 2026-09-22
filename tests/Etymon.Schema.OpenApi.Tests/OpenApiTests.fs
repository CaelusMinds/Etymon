module Etymon.Schema.OpenApi.Tests.OpenApiTests

open System.Text.Json.Nodes
open Expecto
open Etymon
open Etymon.Tests
open Etymon.Tests.Domain

/// The keyword at a path in a rendered schema, as text. Keeps the assertions
/// about individual keywords readable next to the whole-document snapshots.
let private at (path: string list) (root: JsonObject) =
    let rec walk (node: JsonNode) (remaining: string list) =
        match remaining with
        | [] -> Some(node.ToJsonString())
        | (key: string) :: rest ->
            match node with
            | :? JsonObject as o ->
                match o[key] with
                | null -> None
                | child -> walk child rest
            | _ -> None

    walk root path

let tests =
    testList
        "OpenApi"
        [
            testList
                "a null default"
                [
                    test "renders as null rather than crashing" {
                        // Schema.defaulted stores a JSON null default as Some null,
                        // because System.Text.Json.Nodes has no other value for
                        // it. The renderer dereferenced it. This is the ordinary
                        // shape of a request field a caller may leave out and a
                        // form may send as null.
                        let schema =
                            Schema.object "R" {
                                let! note =
                                    Schema.defaulted
                                        "note"
                                        (Schema.nullable Schema.string)
                                        None
                                        (fun (r: string option) -> r)

                                return note
                            }

                        let rendered = (OpenApi.toComponents schema).ToJsonString()
                        Expect.stringContains rendered "\"default\":null" "stated as null"
                        Expect.isNonEmpty (OpenApi.toComponentsWithRoot schema).Components "and the document assembles"
                    }
                ]

            testList
                "assembling a document from many schemas"
                [
                    test "the root of an object is a reference into components" {
                        let assembled = OpenApi.toComponentsWithRoot Person.schema

                        Expect.equal
                            (at [ "$ref" ] assembled.Root)
                            (Some "\"#/components/schemas/Person\"")
                            "addressed where an OpenAPI document keeps them"

                        Expect.isTrue
                            (assembled.Components.ContainsKey "Person")
                            "and the component is there to point at"
                    }

                    test "the root of a list carries its reference under items" {
                        // The trap. A hand-rolled lift that reads $ref at the
                        // root finds nothing here, lifts nothing, and describes
                        // the response as an empty object. The document stays
                        // valid OpenAPI and every "this path is described" test
                        // stays green, so nothing says otherwise.
                        let assembled = OpenApi.toComponentsWithRoot (Schema.list Person.schema)

                        Expect.equal (at [ "type" ] assembled.Root) (Some "\"array\"") "an array"

                        Expect.equal
                            (at [ "items"; "$ref" ] assembled.Root)
                            (Some "\"#/components/schemas/Person\"")
                            "whose items are the reference, one level down"

                        Expect.isTrue (assembled.Components.ContainsKey "Person") "components are lifted either way"
                    }

                    test "the root is never an empty object while there is something to describe" {
                        // Stated as its own assertion because "describes nothing"
                        // is precisely the shape of the silent failure.
                        for root in
                            [
                                (OpenApi.toComponentsWithRoot Person.schema).Root
                                (OpenApi.toComponentsWithRoot (Schema.list Person.schema)).Root
                                (OpenApi.toComponentsWithRoot (Schema.list (Schema.list Person.schema))).Root
                            ] do
                            Expect.isGreaterThan root.Count 0 "a root that says nothing describes nothing"
                    }

                    test "components carry every named type reachable, not just the root" {
                        let assembled = OpenApi.toComponentsWithRoot (Schema.list Person.schema)

                        Expect.equal
                            (assembled.Components |> Seq.map (fun p -> p.Key) |> Seq.sort |> List.ofSeq)
                            (OpenApi.toComponents Person.schema
                             |> Seq.map (fun p -> p.Key)
                             |> Seq.sort
                             |> List.ofSeq)
                            "the same set toComponents would give"
                    }

                    test "a schema with nothing named is inlined rather than left dangling" {
                        let assembled = OpenApi.toComponentsWithRoot Schema.string

                        Expect.equal
                            (at [ "type" ] assembled.Root)
                            (Some "\"string\"")
                            "inline, because there is no component"

                        Expect.equal assembled.Components.Count 0 "and nothing to lift"
                    }
                ]

            testList
                "primitives map to the expected type and format"
                [
                    let expect name schema expected =
                        test name {
                            let rendered = OpenApi.toJsonSchema schema
                            Expect.equal (at [ "type" ] rendered) (Some(fst expected)) "type"

                            match snd expected with
                            | Some format -> Expect.equal (at [ "format" ] rendered) (Some format) "format"
                            | None -> Expect.equal (at [ "format" ] rendered) None "no format"
                        }

                    expect "string" Schema.string ("\"string\"", None)
                    expect "int" Schema.int ("\"integer\"", Some "\"int32\"")
                    expect "int64" Schema.int64 ("\"integer\"", Some "\"int64\"")
                    expect "float" Schema.float ("\"number\"", Some "\"double\"")
                    expect "decimal" Schema.decimal ("\"number\"", None)
                    expect "bool" Schema.bool ("\"boolean\"", None)
                    expect "guid" Schema.guid ("\"string\"", Some "\"uuid\"")
                    expect "dateTimeOffset" Schema.dateTimeOffset ("\"string\"", Some "\"date-time\"")
                    expect "dateOnly" Schema.dateOnly ("\"string\"", Some "\"date\"")
                    expect "timeOnly" Schema.timeOnly ("\"string\"", Some "\"time\"")
                    expect "timeSpan" Schema.timeSpan ("\"string\"", Some "\"duration\"")

                    test "bytes carries its encoding" {
                        let rendered = OpenApi.toJsonSchema Schema.bytes
                        Expect.equal (at [ "type" ] rendered) (Some "\"string\"") "type"
                        Expect.equal (at [ "contentEncoding" ] rendered) (Some "\"base64\"") "encoding"
                    }

                    test "raw constrains nothing" {
                        let rendered = OpenApi.toJsonSchema Schema.raw
                        Expect.equal (at [ "type" ] rendered) None "any JSON at all is the absence of keywords"
                    }
                ]

            testList
                "constraints become keywords"
                [
                    test "length" {
                        let schema = Schema.string |> Schema.constrain (Check.length (Some 3) (Some 254))
                        let rendered = OpenApi.toJsonSchema schema
                        Expect.equal (at [ "minLength" ] rendered) (Some "3") "minLength"
                        Expect.equal (at [ "maxLength" ] rendered) (Some "254") "maxLength"
                    }

                    test "an inclusive range" {
                        let schema = Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))
                        let rendered = OpenApi.toJsonSchema schema
                        Expect.equal (at [ "minimum" ] rendered) (Some "0") "minimum"
                        Expect.equal (at [ "maximum" ] rendered) (Some "130") "maximum"
                        Expect.equal (at [ "exclusiveMinimum" ] rendered) None "not exclusive"
                    }

                    test "an exclusive bound uses the exclusive keyword" {
                        let schema = Schema.decimal |> Schema.constrain (Check.greaterThan 0m)
                        let rendered = OpenApi.toJsonSchema schema
                        Expect.equal (at [ "exclusiveMinimum" ] rendered) (Some "0") "exclusiveMinimum"
                        Expect.equal (at [ "minimum" ] rendered) None "and not the inclusive one"
                    }

                    test "pattern is carried verbatim" {
                        let schema = Schema.string |> Schema.constrain (Check.pattern @"^\d{5}$")
                        Expect.equal (at [ "pattern" ] (OpenApi.toJsonSchema schema)) (Some "\"^\\\\d{5}$\"") "exactly"
                    }

                    test "format" {
                        let schema = Schema.string |> Schema.constrain (Check.format Format.Email)
                        Expect.equal (at [ "format" ] (OpenApi.toJsonSchema schema)) (Some "\"email\"") "email"
                    }

                    test "oneOf becomes an enum" {
                        let schema = Schema.string |> Schema.constrain (Check.oneOf [ "red"; "green" ])
                        Expect.equal (at [ "enum" ] (OpenApi.toJsonSchema schema)) (Some """["red","green"]""") "enum"
                    }

                    test "item bounds" {
                        let schema =
                            Schema.list Schema.string
                            |> Schema.constrain (Check.itemCount (Some 1) (Some 10))

                        let rendered = OpenApi.toJsonSchema schema
                        Expect.equal (at [ "minItems" ] rendered) (Some "1") "minItems"
                        Expect.equal (at [ "maxItems" ] rendered) (Some "10") "maxItems"
                    }
                ]

            testList
                "the honesty rules"
                [
                    test "an opaque constraint is described, never pretended to be a keyword" {
                        // The promise: a rule Etymon cannot express as a keyword is told
                        // to the reader rather than silently dropped.
                        let schema =
                            Schema.int
                            |> Schema.constrain (Check.opaque "even" "must be even" (fun n -> n % 2 = 0))

                        let rendered = OpenApi.toJsonSchema schema

                        Expect.equal (at [ "description" ] rendered) (Some "\"must be even\"") "it appears in prose"

                        Expect.equal (List.length (List.ofSeq (rendered :> seq<_>))) 4 "and adds no keyword of its own"
                    }

                    test "an opaque constraint is appended to an existing description" {
                        let schema =
                            Schema.int
                            |> Schema.describe "A count"
                            |> Schema.constrain (Check.opaque "even" "must be even" (fun n -> n % 2 = 0))

                        Expect.equal
                            (at [ "description" ] (OpenApi.toJsonSchema schema))
                            (Some "\"A count. must be even\"")
                            "both survive"
                    }

                    test "additionalProperties is never claimed to be false" {
                        // Etymon's decoder ignores keys it does not know, so saying
                        // additionalProperties:false would describe a stricter contract
                        // than the code enforces.
                        let text = OpenApi.toJsonSchemaText Person.schema
                        Expect.isFalse (text.Contains "additionalProperties") "no such claim anywhere"
                    }
                ]

            testList
                "structure"
                [
                    test "optional fields are absent from required" {
                        let rendered = OpenApi.toJsonSchema Person.schema
                        let personDef = (rendered["$defs"] :?> JsonObject)["Person"] :?> JsonObject

                        Expect.equal
                            (at [ "required" ] personDef)
                            (Some """["name","age","address","tags"]""")
                            "email is optional, so it is not required"
                    }

                    test "a nullable value is a union of types" {
                        let rendered = OpenApi.toJsonSchema (Schema.nullable Schema.string)
                        Expect.equal (at [ "type" ] rendered) (Some """["string","null"]""") "string or null"
                    }

                    test "a nullable reference uses anyOf, because a $ref cannot carry a type" {
                        let rendered = OpenApi.toJsonSchema (Schema.nullable Address.schema)
                        Expect.isSome (at [ "anyOf" ] rendered) "anyOf rather than a type array"
                    }

                    test "a named schema is referenced, not inlined" {
                        let rendered = OpenApi.toJsonSchema Person.schema
                        let personDef = (rendered["$defs"] :?> JsonObject)["Person"] :?> JsonObject

                        Expect.equal
                            (at [ "properties"; "address"; "$ref" ] personDef)
                            (Some "\"#/$defs/Address\"")
                            "Address is written once and pointed at"
                    }

                    test "a recursive schema terminates and refers to itself" {
                        let rendered = OpenApi.toJsonSchema Tree.schema
                        let treeDef = (rendered["$defs"] :?> JsonObject)["Tree"] :?> JsonObject

                        Expect.equal
                            (at [ "properties"; "children"; "items"; "$ref" ] treeDef)
                            (Some "\"#/$defs/Tree\"")
                            "the reference closes the loop"
                    }

                    test "a field description reaches the property" {
                        let rendered = OpenApi.toJsonSchema Address.schema
                        let addressDef = (rendered["$defs"] :?> JsonObject)["Address"] :?> JsonObject

                        Expect.equal
                            (at [ "properties"; "street"; "description" ] addressDef)
                            (Some "\"Street address, including the number\"")
                            "prose survives"
                    }

                    test "a sensitive field is marked writeOnly" {
                        let schema =
                            Schema.object "Credentials" {
                                let! secret = Schema.required "password" (Schema.string |> Schema.sensitive) id
                                return secret
                            }

                        let rendered = OpenApi.toJsonSchema schema
                        let def = (rendered["$defs"] :?> JsonObject)["Credentials"] :?> JsonObject

                        Expect.equal
                            (at [ "properties"; "password"; "writeOnly" ] def)
                            (Some "true")
                            "so a generated document does not echo it back"
                    }

                    test "a default reaches the property" {
                        let schema =
                            Schema.object "Settings" {
                                let! retries = Schema.defaulted "retries" Schema.int 3 id
                                return retries
                            }

                        let rendered = OpenApi.toJsonSchema schema
                        let def = (rendered["$defs"] :?> JsonObject)["Settings"] :?> JsonObject
                        Expect.equal (at [ "properties"; "retries"; "default" ] def) (Some "3") "the default is stated"
                    }

                    test "a union becomes oneOf with a const tag per case" {
                        let rendered = OpenApi.toJsonSchema Shape.schema
                        let def = (rendered["$defs"] :?> JsonObject)["Shape"] :?> JsonObject
                        let choices = def["oneOf"] :?> JsonArray
                        Expect.equal choices.Count 3 "three cases"

                        let first = choices[0] :?> JsonObject
                        Expect.equal (at [ "properties"; "kind"; "const" ] first) (Some "\"circle\"") "tagged by const"
                    }

                    test "a payload-free case does not require a value" {
                        let rendered = OpenApi.toJsonSchema Shape.schema
                        let def = (rendered["$defs"] :?> JsonObject)["Shape"] :?> JsonObject
                        let point = (def["oneOf"] :?> JsonArray)[2] :?> JsonObject

                        Expect.equal (at [ "required" ] point) (Some """["kind"]""") "the tag alone"
                        Expect.equal (at [ "properties"; "value" ] point) None "and no value property"
                    }
                ]

            testList
                "dialects"
                [
                    test "JSON Schema declares its dialect and uses $defs" {
                        let text = OpenApi.toJsonSchemaText Person.schema
                        Expect.stringContains text "https://json-schema.org/draft/2020-12/schema" "declares the dialect"
                        Expect.stringContains text "#/$defs/Address" "and references into $defs"
                    }

                    test "components reference into components/schemas" {
                        let text = OpenApi.toComponentsText Person.schema
                        Expect.stringContains text "#/components/schemas/Address" "the OpenAPI location"
                        Expect.isFalse (text.Contains "$schema") "a component is not a standalone document"
                    }

                    test "components are keyed by name" {
                        let components = OpenApi.toComponents Person.schema
                        let names = components |> Seq.map (fun pair -> pair.Key) |> List.ofSeq |> List.sort
                        Expect.equal names [ "Address"; "Person" ] "every named schema, once each"
                    }
                ]

            testList
                "determinism"
                [
                    test "the same schema renders byte-identically every time" {
                        // Without this, a snapshot test is worse than no test.
                        let once = OpenApi.toJsonSchemaText Person.schema
                        let twice = OpenApi.toJsonSchemaText Person.schema
                        Expect.equal once twice "identical output"
                    }
                ]

            // ---- snapshots ----------------------------------------------------
            //
            // These pin the exact generated document. A change to any of them shows
            // up as a diff in the pull request, which is the point: output that
            // consumers depend on should not be able to change quietly.
            testList
                "snapshots"
                [
                    test "a record with constrained fields, nesting and optionality" {
                        Snapshot.verify "person-json-schema" (OpenApi.toJsonSchemaText Person.schema)
                    }

                    test "the same record as OpenAPI components" {
                        Snapshot.verify "person-openapi-components" (OpenApi.toComponentsText Person.schema)
                    }

                    test "a tagged union" {
                        Snapshot.verify "shape-json-schema" (OpenApi.toJsonSchemaText Shape.schema)
                    }

                    test "a recursive type" {
                        Snapshot.verify "tree-json-schema" (OpenApi.toJsonSchemaText Tree.schema)
                    }
                ]
        ]
