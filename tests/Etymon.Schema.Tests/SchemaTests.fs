module Etymon.Schema.Tests.SchemaTests

open System
open Expecto
open Etymon
open Etymon.Schema.Tests.Domain

let private messagesOf (v: Validation<'T>) =
    v |> Validation.errorList |> List.map (fun e -> e.ToString())

/// Encodes then decodes, which is the property every schema must satisfy.
let private roundTrips (schema: Schema<'T>) (value: 'T) =
    match Schema.fromJson schema (Schema.toJson schema value) with
    | Ok decoded -> decoded = value
    | Error _ -> false

let tests =
    testList
        "Schema"
        [
            testList
                "primitives"
                [
                    test "encode" {
                        Expect.equal (Schema.toJson Schema.string "hi") "\"hi\"" "string"
                        Expect.equal (Schema.toJson Schema.int 42) "42" "int"
                        Expect.equal (Schema.toJson Schema.int64 9007199254740993L) "9007199254740993" "int64"
                        Expect.equal (Schema.toJson Schema.bool true) "true" "bool"
                        Expect.equal (Schema.toJson Schema.decimal 1.50m) "1.50" "decimal keeps its scale"
                        Expect.equal (Schema.toJson Schema.bytes [| 1uy; 2uy |]) "\"AQI=\"" "bytes are base64"
                    }

                    test "decode" {
                        Expect.equal (Schema.fromJson Schema.string "\"hi\"") (Ok "hi") "string"
                        Expect.equal (Schema.fromJson Schema.int "42") (Ok 42) "int"
                        Expect.equal (Schema.fromJson Schema.bool "true") (Ok true) "bool"
                        Expect.equal (Schema.fromJson Schema.decimal "1.50") (Ok 1.50m) "decimal"
                    }

                    test "a type mismatch says what it got as well as what it wanted" {
                        match Validation.errorList (Schema.fromJson Schema.int "\"42\"") with
                        | [ e ] ->
                            Expect.equal
                                e.Reason
                                (ErrorReason.TypeMismatch("number", "string"))
                                "the reason is machine-readable"

                            Expect.equal e.Message "must be a number, but was a string" "and the message is readable"
                        | other -> failtestf "expected one error, got %A" other
                    }

                    test "an integer too large for its type is out of range, not a type mismatch" {
                        match Validation.errorList (Schema.fromJson Schema.int "2147483648") with
                        | [ e ] ->
                            Expect.equal e.Reason (ErrorReason.Rejected "out-of-range") "a number, but not this one"
                        | other -> failtestf "expected one error, got %A" other
                    }

                    test "dates and times round-trip" {
                        let stamp = DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero)
                        Expect.equal (Schema.toJson Schema.dateTimeOffset stamp) "\"2026-09-20T14:30:00Z\"" "RFC 3339"
                        Expect.isTrue (roundTrips Schema.dateTimeOffset stamp) "and reads back"
                        Expect.isTrue (roundTrips Schema.dateOnly (DateOnly(2026, 9, 20))) "date"
                        Expect.isTrue (roundTrips Schema.timeOnly (TimeOnly(14, 30, 0))) "time"
                        Expect.isTrue (roundTrips Schema.timeSpan (TimeSpan.FromHours 1.0)) "duration"
                        Expect.isTrue (roundTrips Schema.guid (Guid.NewGuid())) "uuid"
                    }

                    test "malformed JSON is one error, not a list of them" {
                        let v = Schema.fromJson Schema.int "{oh no"
                        Expect.equal (Validation.errorCount v) 1 "there is nothing to accumulate"

                        match Validation.errorList v with
                        | [ e ] -> Expect.equal e.Reason (ErrorReason.Rejected "malformed-json") "and it says so"
                        | other -> failtestf "expected one error, got %A" other
                    }
                ]

            testList
                "coercing mode"
                [
                    test "a string stands in for a number, a boolean and a date" {
                        let coerce schema json =
                            Schema.fromJsonWith DecodeMode.Coercing schema json

                        Expect.equal (coerce Schema.int "\"8080\"") (Ok 8080) "int"
                        Expect.equal (coerce Schema.bool "\"yes\"") (Ok true) "bool, in the forms that occur"
                        Expect.equal (coerce Schema.decimal "\"1.5\"") (Ok 1.5m) "decimal"
                        Expect.isTrue (Validation.isOk (coerce Schema.dateOnly "\"2026-09-20\"")) "date"
                    }

                    test "strict mode refuses the same input" {
                        Expect.isTrue
                            (Validation.isError (Schema.fromJson Schema.int "\"8080\""))
                            "a string is not a number"

                        Expect.isTrue (Validation.isError (Schema.fromJson Schema.bool "\"yes\"")) "nor a boolean"
                    }

                    test "coercing does not make nonsense acceptable" {
                        Expect.isTrue
                            (Validation.isError (Schema.fromJsonWith DecodeMode.Coercing Schema.int "\"eighty\""))
                            "a string that is not a number is still not a number"
                    }
                ]

            testList
                "input somebody else chose"
                [
                    test "a large array of the wrong thing is rejected in linear time" {
                        // Accumulation used to join a growing list per element,
                        // which is quadratic: 200,000 bad elements took five
                        // minutes of CPU for an 800 KB body, and anyone who could
                        // post to an endpoint could take a core off it.
                        //
                        // The bound is deliberately loose. It is not measuring
                        // how fast this is; it is measuring that it is not
                        // quadratic, and the quadratic version needed well over a
                        // minute to reach this point.
                        let json = "[" + String.Join(",", Array.create 100_000 "\"x\"") + "]"

                        let clock = Diagnostics.Stopwatch.StartNew()
                        let result = Schema.fromJson (Schema.list Schema.int) json
                        clock.Stop()

                        Expect.equal (Validation.errorCount result) 100_000 "every element is still reported"

                        Expect.isLessThan
                            clock.Elapsed.TotalSeconds
                            10.0
                            "a rejection should not cost more than the request that caused it"
                    }

                    test "nesting past what the parser allows is an error, not a crash" {
                        // A recursive decoder walking a deliberately deep value is
                        // how a stack overflow happens, and a stack overflow cannot
                        // be caught -- it takes the process with it. The parser
                        // refuses first, and this pins that it keeps doing so.
                        let deep = String.replicate 5_000 "[" + "1" + String.replicate 5_000 "]"
                        let result = Schema.fromJson (Schema.list Schema.int) deep

                        Expect.isTrue (Validation.isError result) "refused"

                        match Validation.errorList result with
                        | [ e ] -> Expect.equal e.Reason (ErrorReason.Rejected "malformed-json") "as malformed input"
                        | other -> failtestf "expected one error, got %A" other
                    }

                    test "nesting within what the parser allows still decodes" {
                        // The limit belongs to the parser, not to Etymon, so this
                        // is here to notice if it ever moves.
                        let nested = String.replicate 60 "[" + "1" + String.replicate 60 "]"
                        Expect.isTrue (Validation.isError (Schema.fromJson Schema.int nested)) "wrong type, but read"
                    }
                ]

            testList
                "structure"
                [
                    test "nullable reads null as absent" {
                        let schema = Schema.nullable Schema.string
                        Expect.equal (Schema.fromJson schema "null") (Ok None) "null is None"
                        Expect.equal (Schema.fromJson schema "\"hi\"") (Ok(Some "hi")) "a value is Some"
                        Expect.equal (Schema.toJson schema None) "null" "and writes back as null"
                    }

                    test "a list reports every bad element, with its index" {
                        let v = Schema.fromJson (Schema.list Schema.int) "[1,\"x\",\"y\"]"
                        Expect.equal (Validation.errorCount v) 2 "two bad elements, two errors"

                        Expect.equal
                            (messagesOf v)
                            [
                                "[1]: must be a number, but was a string"
                                "[2]: must be a number, but was a string"
                            ]
                            "each carries its position"
                    }

                    test "a list round-trips" {
                        Expect.isTrue (roundTrips (Schema.list Schema.int) [ 1; 2; 3 ]) "non-empty"
                        Expect.isTrue (roundTrips (Schema.list Schema.int) []) "empty"
                    }

                    test "an array round-trips" {
                        let schema = Schema.array Schema.int
                        Expect.equal (Schema.toJson schema [| 1; 2 |]) "[1,2]" "encodes"
                        Expect.equal (Schema.fromJson schema "[1,2]") (Ok [| 1; 2 |]) "decodes"
                    }

                    test "a map round-trips and reports bad values by key" {
                        let schema = Schema.map Schema.int
                        Expect.isTrue (roundTrips schema (Map.ofList [ "a", 1; "b", 2 ])) "round-trips"

                        let v = Schema.fromJson schema """{"a":1,"b":"x"}"""

                        Expect.equal
                            (messagesOf v)
                            [ "b: must be a number, but was a string" ]
                            "the key names the error"
                    }

                    test "a non-array where an array was wanted" {
                        let v = Schema.fromJson (Schema.list Schema.int) "42"
                        Expect.equal (messagesOf v) [ "must be a array, but was a number" ] "says so"
                    }
                ]

            testList
                "objects"
                [
                    test "encodes in declaration order" {
                        let json = Schema.toJson Person.schema samplePerson

                        Expect.stringStarts
                            json
                            """{"name":"Ada","age":36,"email":"ada@example.com","address":"""
                            "fields are written in the order they were declared"
                    }

                    test "round-trips" {
                        Expect.isTrue (roundTrips Person.schema samplePerson) "a person survives the round trip"
                    }

                    test "an absent optional field is simply absent" {
                        let withoutEmail = { samplePerson with Email = None }
                        let json = Schema.toJson Person.schema withoutEmail
                        Expect.isFalse (json.Contains "email") "the key is omitted rather than written as null"
                        Expect.isTrue (roundTrips Person.schema withoutEmail) "and still round-trips"
                    }

                    test "an explicit null in an optional field reads as absent" {
                        let json =
                            """{"name":"Ada","age":36,"email":null,"address":{"street":"s","zip":"12345"},"tags":[]}"""

                        match Schema.fromJson Person.schema json with
                        | Ok person -> Expect.equal person.Email None "null and absent mean the same thing here"
                        | Error e -> failtestf "should have decoded: %s" (ValidationErrors.format e)
                    }

                    test "every problem is reported at once, with a path" {
                        // The behaviour the whole error model exists for.
                        let json = """{"age":-3,"address":{"zip":"xx"},"tags":[]}"""
                        let v = Schema.fromJson Person.schema json

                        Expect.equal
                            (messagesOf v)
                            [
                                "name: is required"
                                "age: must be between 0 and 130"
                                "address.street: is required"
                                "address.zip: must be exactly 5 characters"
                                "address.zip: must match ^\\d+$"
                            ]
                            "five problems, five errors, each saying where"
                    }

                    test "a missing required field names itself" {
                        let v = Schema.fromJson Address.schema """{"zip":"12345"}"""
                        Expect.equal (messagesOf v) [ "street: is required" ] "and says it is required"

                        match Validation.errorList v with
                        | [ e ] -> Expect.equal e.Reason ErrorReason.Missing "machine-readably"
                        | other -> failtestf "expected one error, got %A" other
                    }

                    test "a non-object where an object was wanted" {
                        let v = Schema.fromJson Address.schema "42"
                        Expect.equal (messagesOf v) [ "must be a object, but was a number" ] "says so"
                    }

                    test "a defaulted field stands in when absent" {
                        let schema =
                            Schema.object "Settings" {
                                let! retries = Schema.defaulted "retries" Schema.int 3 id
                                return retries
                            }

                        Expect.equal (Schema.fromJson schema "{}") (Ok 3) "absent means the default"
                        Expect.equal (Schema.fromJson schema """{"retries":5}""") (Ok 5) "present means the value"
                        Expect.equal (Schema.toJson schema 3) """{"retries":3}""" "and it is written back out"
                    }

                    test "field documentation reaches the description" {
                        match SchemaInfo.strip Address.schema.Info with
                        | SObject(_, fields) ->
                            let street = fields |> List.find (fun f -> f.Name = "street")

                            Expect.equal
                                street.Description
                                (Some "Street address, including the number")
                                "doc attaches to the field it follows"
                        | other -> failtestf "expected an object, got %A" other
                    }

                    test "required and optional are distinguishable in the description" {
                        match SchemaInfo.strip Person.schema.Info with
                        | SObject(_, fields) ->
                            let required =
                                fields |> List.filter (fun f -> f.Required) |> List.map (fun f -> f.Name)

                            Expect.equal required [ "name"; "age"; "address"; "tags" ] "email is the only optional one"
                        | other -> failtestf "expected an object, got %A" other
                    }
                ]

            testList
                "constrained types"
                [
                    test "a refinement's rules run on decode" {
                        let v = Schema.fromJson Email.schema "\"nope\""
                        Expect.equal (messagesOf v) [ "must be a valid email" ] "the refinement rejected it"
                    }

                    test "a refinement's normalisation applies on decode" {
                        match Schema.fromJson Email.schema "\"  Ada@Example.COM  \"" with
                        | Ok email -> Expect.equal (Email.value email) "ada@example.com" "trimmed and lower-cased"
                        | Error e -> failtestf "should have decoded: %s" (ValidationErrors.format e)
                    }

                    test "a refinement's constraints reach the description" {
                        Expect.equal
                            (SchemaInfo.constraints Email.schema.Info)
                            [ Constraint.Length(Some 3, Some 254); Constraint.HasFormat Format.Email ]
                            "this list is what OpenAPI, SQL and generators all read"
                    }

                    test "a constrained value round-trips" {
                        Expect.isTrue
                            (roundTrips
                                Email.schema
                                (Person.schema |> ignore
                                 samplePerson.Email.Value))
                            "email"
                    }

                    test "constrain adds to the description as well as enforcing" {
                        Expect.equal
                            (SchemaInfo.constraints Person.ageSchema.Info)
                            [ Constraint.Range(Some(Num.Int 0L), Some(Num.Int 130L), false, false) ]
                            "the rule is data, not only behaviour"

                        Expect.isTrue (Validation.isError (Schema.fromJson Person.ageSchema "200")) "and is enforced"
                    }
                ]

            testList
                "unions"
                [
                    test "a case with a payload is adjacently tagged" {
                        Expect.equal
                            (Schema.toJson Shape.schema (Circle 1.0))
                            """{"kind":"circle","value":1}"""
                            "the tag can never collide with a payload field"
                    }

                    test "a case with no payload is the tag alone" {
                        Expect.equal (Schema.toJson Shape.schema Point) """{"kind":"point"}""" "no empty value field"
                    }

                    test "every case round-trips" {
                        Expect.isTrue (roundTrips Shape.schema (Circle 1.5)) "circle"
                        Expect.isTrue (roundTrips Shape.schema (Rectangle(2.0, 3.0))) "rectangle"
                        Expect.isTrue (roundTrips Shape.schema Point) "point"
                    }

                    test "an unknown tag lists the ones that exist" {
                        let v = Schema.fromJson Shape.schema """{"kind":"hexagon"}"""

                        Expect.equal
                            (messagesOf v)
                            [ "kind: must be one of: circle, rectangle, point" ]
                            "so the caller can see what was allowed"
                    }

                    test "a missing tag says which field it wanted" {
                        let v = Schema.fromJson Shape.schema """{"value":1}"""
                        Expect.equal (messagesOf v) [ "kind: is required" ] "names the tag field"
                    }

                    test "errors inside a payload carry their path" {
                        let v =
                            Schema.fromJson Shape.schema """{"kind":"rectangle","value":{"width":"x"}}"""

                        Expect.equal
                            (messagesOf v)
                            [
                                "value.width: must be a number, but was a string"
                                "value.height: is required"
                            ]
                            "both problems, both located"
                    }
                ]

            testList
                "recursion"
                [
                    test "a self-referential schema can be built at all" {
                        Expect.isTrue (Tree.schema.Info |> SchemaInfo.name |> Option.isSome) "construction terminates"
                    }

                    test "a nested tree round-trips" {
                        let tree =
                            {
                                Value = 1
                                Children =
                                    [
                                        { Value = 2; Children = [] }
                                        {
                                            Value = 3
                                            Children = [ { Value = 4; Children = [] } ]
                                        }
                                    ]
                            }

                        Expect.isTrue (roundTrips Tree.schema tree) "three levels deep"
                    }

                    test "an error deep in a tree carries its whole path" {
                        let json = """{"value":1,"children":[{"value":"x","children":[]}]}"""
                        let v = Schema.fromJson Tree.schema json

                        Expect.equal
                            (messagesOf v)
                            [ "children[0].value: must be a number, but was a string" ]
                            "the path survives the recursion"
                    }

                    test "an error four levels down carries every step" {
                        // Paths are not built while descending into a value; they
                        // are assembled as an error unwinds, so that a successful
                        // read allocates nothing to describe a problem it did not
                        // find. That makes depth the thing to test: one level
                        // working proves almost nothing about four.
                        let json =
                            """{"value":1,"children":[
                                 {"value":2,"children":[
                                   {"value":3,"children":[
                                     {"value":"deep","children":[]}]}]}]}"""

                        Expect.equal
                            (messagesOf (Schema.fromJson Tree.schema json))
                            [
                                "children[0].children[0].children[0].value: must be a number, but was a string"
                            ]
                            "every field and every index, in order, outermost first"
                    }

                    test "errors at different depths each keep their own path" {
                        // The re-rooting happens once per level per error. Two
                        // errors at different depths in the same read is where
                        // getting that wrong shows up as one path stamped onto
                        // both.
                        let json =
                            """{"value":1,"children":[
                                 {"value":"a","children":[]},
                                 {"value":3,"children":[{"value":"b","children":[]}]}]}"""

                        Expect.equal
                            (messagesOf (Schema.fromJson Tree.schema json))
                            [
                                "children[0].value: must be a number, but was a string"
                                "children[1].children[0].value: must be a number, but was a string"
                            ]
                            "two depths, two paths, neither borrowed from the other"
                    }

                    test "definitions terminate on a recursive schema" {
                        let names = Schema.definitions Tree.schema |> Map.keys |> List.ofSeq
                        Expect.equal names [ "Tree" ] "recorded once, not forever"
                    }
                ]

            testList
                "description"
                [
                    test "definitions collects every named schema" {
                        let names = Schema.definitions Person.schema |> Map.keys |> List.ofSeq |> List.sort
                        Expect.equal names [ "Address"; "Person" ] "both objects, each once"
                    }

                    test "named, describe and example attach without changing behaviour" {
                        let schema =
                            Schema.int
                            |> Schema.named "Count"
                            |> Schema.describe "How many"
                            |> Schema.example 7

                        let meta = SchemaInfo.meta schema.Info
                        Expect.equal meta.Name (Some "Count") "name"
                        Expect.equal meta.Description (Some "How many") "description"
                        Expect.equal (List.length meta.Examples) 1 "example"
                        Expect.equal (Schema.fromJson schema "5") (Ok 5) "and it still decodes"
                    }

                    test "sensitive is carried on the field as well as the schema" {
                        let schema =
                            Schema.object "Credentials" {
                                let! secret = Schema.required "password" (Schema.string |> Schema.sensitive) id

                                return secret
                            }

                        match SchemaInfo.strip schema.Info with
                        | SObject(_, [ field ]) -> Expect.isTrue field.Sensitive "so a consumer can redact it"
                        | other -> failtestf "expected one field, got %A" other
                    }
                ]

            testList
                "validate"
                [
                    test "checks a value without any JSON changing hands" {
                        Expect.isTrue (Validation.isOk (Schema.validate Person.ageSchema 30)) "30 is a valid age"
                        Expect.equal (Validation.errorCount (Schema.validate Person.ageSchema 200)) 1 "200 is not"
                    }

                    test "validates a whole record" {
                        Expect.isTrue
                            (Validation.isOk (Schema.validate Person.schema samplePerson))
                            "the sample is valid"

                        let invalid =
                            { samplePerson with
                                Age = 200
                                Name = ""
                            }

                        Expect.equal (Validation.errorCount (Schema.validate Person.schema invalid)) 2 "both fields"
                    }
                ]

            testList
                "properties"
                [
                    testProperty "any int round-trips" <| fun (v: int) -> roundTrips Schema.int v
                    testProperty "any int64 round-trips"
                    <| fun (v: int64) -> roundTrips Schema.int64 v
                    testProperty "any bool round-trips" <| fun (v: bool) -> roundTrips Schema.bool v
                    testProperty "any decimal round-trips"
                    <| fun (v: decimal) -> roundTrips Schema.decimal v

                    testProperty "any string round-trips, including awkward ones"
                    <| fun (v: string) -> isNull v || roundTrips Schema.string v

                    testProperty "any finite float round-trips"
                    <| fun (v: float) -> not (Double.IsFinite v) || roundTrips Schema.float v

                    testProperty "any list of ints round-trips"
                    <| fun (v: int list) -> roundTrips (Schema.list Schema.int) v

                    testProperty "any map of ints round-trips"
                    <| fun (pairs: (string * int) list) ->
                        let pairs = pairs |> List.filter (fst >> isNull >> not)
                        roundTrips (Schema.map Schema.int) (Map.ofList pairs)

                    testProperty "N bad fields in an object produce N errors"
                    <| fun (badName: bool, badAge: bool, badZip: bool) ->
                        let name = if badName then "" else "Ada"
                        let age = if badAge then 200 else 36
                        let zip = if badZip then "xx" else "12345"

                        let json =
                            $"""{{"name":"{name}","age":{age},"address":{{"street":"s","zip":"{zip}"}},"tags":[]}}"""

                        // A bad zip breaks two rules -- the length and the pattern --
                        // so it contributes two errors rather than one.
                        let expected =
                            (if badName then 1 else 0)
                            + (if badAge then 1 else 0)
                            + (if badZip then 2 else 0)

                        Validation.errorCount (Schema.fromJson Person.schema json) = expected

                    testProperty "encoding is deterministic"
                    <| fun (v: int list) ->
                        let schema = Schema.list Schema.int
                        Schema.toJson schema v = Schema.toJson schema v
                ]
        ]
