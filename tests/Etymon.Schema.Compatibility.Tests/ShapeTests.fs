/// Deriving a shape from the Schema that does the work.
///
/// A snapshot matches its entries by name. A shape recorded under a name its
/// Schema does not carry diffs cleanly against itself forever and is never
/// compared with the thing it describes -- the failure is silence, which is the
/// failure this package exists to remove. These pin that the name cannot be
/// supplied wrongly, because it is not supplied at all.
module Etymon.Schema.Compatibility.Tests.ShapeTests

open System
open Expecto
open Etymon

type private SignIn = { Email: string; Password: string }

let private signInSchema =
    Schema.object "SignInRequest" {
        let! email = Schema.required "email" Schema.string (fun r -> r.Email)
        and! password = Schema.required "password" Schema.string (fun r -> r.Password)
        return { Email = email; Password = password }
    }

type private Suspended =
    {
        Reason: string
        Reasons: string list
        History: string list option
    }

let private reasonRule = (Check.oneOf [ "fired"; "left" ]).Constraint

let private suspendedSchema =
    let reason = Schema.string |> Schema.constrain (Check.oneOf [ "fired"; "left" ])

    Schema.object "Suspended" {
        let! r = Schema.required "reason" reason (fun x -> x.Reason)
        and! rs = Schema.required "reasons" (Schema.list reason) (fun x -> x.Reasons)

        and! history =
            Schema.required "history" (Schema.nullable (Schema.list reason)) (fun x -> x.History)

        return
            {
                Reason = r
                Reasons = rs
                History = history
            }
    }

type private Line = { Sku: string; Qty: int }

type private Entry =
    {
        Lines: Line list
        FirstLine: Line option
        Note: string
    }

let private lineSchema =
    Schema.object "Line" {
        let! sku = Schema.required "sku" Schema.string (fun l -> l.Sku)
        and! qty = Schema.required "qty" Schema.int (fun l -> l.Qty)
        return { Sku = sku; Qty = qty }
    }

let private entrySchema =
    Schema.object "Entry" {
        let! lines = Schema.required "lines" (Schema.list lineSchema) (fun e -> e.Lines)
        and! first = Schema.optional "firstLine" lineSchema (fun e -> e.FirstLine)
        and! note = Schema.defaulted "note" Schema.string "none" (fun e -> e.Note)

        return
            {
                Lines = lines
                FirstLine = first
                Note = note
            }
    }

type private Outcome =
    | Succeeded of Line
    | Failed of string
    | Skipped

let private outcomeSchema =
    Schema.union
        "Outcome"
        "kind"
        [
            Schema.case
                "succeeded"
                lineSchema
                Succeeded
                (function
                | Succeeded l -> ValueSome l
                | _ -> ValueNone
                )
            Schema.case
                "failed"
                Schema.string
                Failed
                (function
                | Failed why -> ValueSome why
                | _ -> ValueNone
                )
            Schema.caseUnit
                "skipped"
                Skipped
                (function
                | Skipped -> true
                | _ -> false
                )
        ]

type private Run = { Entry: Entry; Outcome: Outcome }

/// A recursive union reached through a field: a case whose payload is the
/// union itself, and a case whose payload is an object holding the union.
type private Expr =
    | Literal of int
    | Not of Expr
    | And of Expr * Expr

let private exprSchema =
    Schema.recursive
        "Expr"
        (fun self ->
            Schema.union
                "Expr"
                "kind"
                [
                    Schema.case
                        "literal"
                        Schema.int
                        Literal
                        (function
                        | Literal n -> ValueSome n
                        | _ -> ValueNone
                        )
                    Schema.case
                        "not"
                        self
                        Not
                        (function
                        | Not e -> ValueSome e
                        | _ -> ValueNone
                        )
                    Schema.case
                        "and"
                        (Schema.object "Expr.pair" {
                            let! left = Schema.required "left" self fst
                            and! right = Schema.required "right" self snd
                            return left, right
                        })
                        And
                        (function
                        | And(l, r) -> ValueSome(l, r)
                        | _ -> ValueNone
                        )
                ]
        )

type private Filter = { Where: Expr }

let private filterSchema =
    Schema.object "Filter" {
        let! where = Schema.required "where" exprSchema (fun f -> f.Where)
        return { Where = where }
    }

type private Wrapped = { Outcome: Outcome }

let private wrappedSchema =
    Schema.union
        "Wrapped"
        "kind"
        [
            Schema.case "outcome" outcomeSchema (fun o -> { Outcome = o }) (fun w -> ValueSome w.Outcome)
        ]

type private Tree = { Children: Tree list }

let private runSchema =
    Schema.object "Run" {
        let! entry = Schema.required "entry" entrySchema (fun r -> r.Entry)
        and! outcome = Schema.required "outcome" outcomeSchema (fun r -> r.Outcome)
        return { Entry = entry; Outcome = outcome }
    }

let private names (shapes: Shape list) = shapes |> List.map (fun s -> s.Name)

let private fieldNamed name (shape: Shape) =
    shape.Fields |> List.find (fun f -> f.Name = name)

let tests =
    testList
        "Shape.ofSchema"
        [
            testList
                "deep"
                [
                    test "the root comes first, then every nested object once, in name order" {
                        let shapes = Shape.ofSchemaDeep 1 runSchema

                        Expect.equal
                            (names shapes)
                            [ "Run"; "Entry"; "Line"; "Outcome"; "Outcome.failed" ]
                            "Line is reached through a list, an option and a union case, and recorded once; the union under the union's own name"

                        Expect.isTrue (shapes |> List.forall (fun s -> s.Version = 1)) "all at the root's version"
                    }

                    test "a union case with an object payload is recorded under the object's name" {
                        let shapes = Shape.ofSchemaDeep 1 outcomeSchema
                        Expect.contains (names shapes) "Line" "the succeeded case's payload"
                    }

                    test "a union case with a scalar payload is recorded under Union.tag with a root field" {
                        let shapes = Shape.ofSchemaDeep 1 outcomeSchema
                        let failed = shapes |> List.find (fun s -> s.Name = "Outcome.failed")

                        match failed.Fields with
                        | [ root ] ->
                            Expect.equal root.Name "$" "the document root"
                            Expect.equal root.Type (FieldType.Scalar "string") "holding the payload's type"
                        | other -> failtestf "expected one root field, got %A" other
                    }

                    test "a case with no payload records nothing" {
                        let shapes = Shape.ofSchemaDeep 1 outcomeSchema
                        Expect.isFalse (names shapes |> List.contains "Outcome.skipped") "nothing to compare"
                    }

                    test "a union root records one field named after the tag key" {
                        let root = Shape.ofSchemaDeep 1 outcomeSchema |> List.head

                        match root.Fields with
                        | [ tag ] ->
                            Expect.equal tag.Name "kind" "the tag key"

                            Expect.equal
                                tag.Type
                                (FieldType.Choice("Outcome", [ "succeeded"; "failed"; "skipped" ]))
                                "holding the cases"
                        | other -> failtestf "expected one tag field, got %A" other
                    }

                    test "the shallow derivation of a union root agrees" {
                        Expect.equal
                            (Shape.ofSchema 1 outcomeSchema)
                            (Shape.ofSchemaDeep 1 outcomeSchema |> List.head)
                            "one field, not none"
                    }

                    test "a list root records one field named $" {
                        let shape = Shape.ofSchemaNamed "Lines" 1 (Schema.list lineSchema)

                        match shape.Fields with
                        | [ root ] ->
                            Expect.equal root.Name "$" "the document root"
                            Expect.equal root.Type (FieldType.Sequence(FieldType.Nested "Line")) "holding the type"
                        | other -> failtestf "expected one root field, got %A" other
                    }

                    test "the walk stops at a reference" {
                        let tree =
                            Schema.recursive
                                "Tree"
                                (fun self ->
                                    Schema.object "Tree" {
                                        let! children =
                                            Schema.required "children" (Schema.list self) (fun (t: Tree) -> t.Children)

                                        return { Children = children }
                                    }
                                )

                        let shapes = Shape.ofSchemaDeep 1 tree
                        Expect.equal (names shapes) [ "Tree" ] "once, and no overflow"
                    }

                    test "a recursive union reached through a field is recorded, and a reference resolves to the cases" {
                        let shapes = Shape.ofSchemaDeep 1 filterSchema

                        Expect.equal
                            (names shapes)
                            [ "Filter"; "Expr"; "Expr.literal"; "Expr.not"; "Expr.pair" ]
                            "the union under the union's own name, the scalar and self cases under Union.tag, the pair under the object's name"

                        let pair = shapes |> List.find (fun s -> s.Name = "Expr.pair")
                        let cases = FieldType.Choice("Expr", [ "literal"; "not"; "and" ])
                        Expect.equal (fieldNamed "left" pair).Type cases "a reference to the union is the union's cases"

                        let where = fieldNamed "where" (List.head shapes)
                        Expect.equal where.Type cases "as a field holding the resolved union is"

                        let snapshot = ShapeSnapshot.of' shapes
                        Expect.isEmpty (Events.check [] [] snapshot snapshot) "and nothing is unresolved"
                    }

                    test "a union case whose payload is a union is recorded under the inner union's name" {
                        let shapes = Shape.ofSchemaDeep 1 wrappedSchema

                        Expect.equal
                            (names shapes)
                            [ "Wrapped"; "Line"; "Outcome"; "Outcome.failed" ]
                            "the inner union under the inner union's own name, and the inner union's payloads"
                    }

                    test "a list root named with Schema.named is walked" {
                        let shapes = Shape.ofSchemaDeep 1 (Schema.list lineSchema |> Schema.named "Lines")
                        Expect.equal (names shapes) [ "Lines"; "Line" ] "the root, then the element"

                        let snapshot = ShapeSnapshot.of' shapes
                        Expect.isEmpty (Wire.unresolvedResponses [] snapshot snapshot) "and nothing is unresolved"
                    }

                    test "an unnamed root says how to name a list" {
                        let message =
                            try
                                Shape.ofSchemaDeep 1 (Schema.list lineSchema) |> ignore
                                None
                            with :? ArgumentException as e ->
                                Some e.Message

                        match message with
                        | Some text -> Expect.stringContains text "Schema.named" "the one way a list root takes a name"
                        | None -> failtest "an unnamed root was accepted"
                    }

                    test "two different definitions under one name are refused" {
                        let otherLine =
                            Schema.object "Line" {
                                let! sku = Schema.required "sku" Schema.string id
                                return sku
                            }

                        let clash =
                            Schema.object "Clash" {
                                let! a = Schema.required "a" lineSchema fst
                                and! b = Schema.required "b" otherLine snd
                                return a, b
                            }

                        let message =
                            try
                                Shape.ofSchemaDeep 1 clash |> ignore
                                None
                            with :? ArgumentException as e ->
                                Some e.Message

                        match message with
                        | Some text -> Expect.stringContains text "'Line'" "named"
                        | None -> failtest "two shapes under one name were accepted"
                    }
                ]

            testList
                "defaults"
                [
                    test "a default is recorded as the encoded JSON text" {
                        let shape = Shape.ofSchema 1 entrySchema
                        Expect.equal (fieldNamed "note" shape).Default (Some(Some "\"none\"")) "the text"
                    }

                    test "a field without a default is recorded as having none" {
                        let shape = Shape.ofSchema 1 entrySchema
                        Expect.equal (fieldNamed "lines" shape).Default (Some None) "recorded, and none"
                    }
                ]

            test "the name comes from the Schema, so it cannot disagree with it" {
                let shape = Shape.ofSchema 1 signInSchema
                Expect.equal shape.Name "SignInRequest" "the Schema already said so"
                Expect.equal shape.Version 1 "and the version is still the caller's to give"
            }

            test "the fields are the Schema's fields" {
                let shape = Shape.ofSchema 1 signInSchema

                Expect.equal (shape.Fields |> List.map (fun f -> f.Name)) [ "email"; "password" ] "in declaration order"

                Expect.isTrue (shape.Fields |> List.forall (fun f -> f.Required)) "both required"
            }

            test "the derived shape is the named one, given the same name" {
                // ofSchemaNamed is the escape hatch, not a different derivation.
                Expect.equal
                    (Shape.ofSchema 3 signInSchema)
                    (Shape.ofSchemaNamed "SignInRequest" 3 signInSchema)
                    "same shape either way"
            }

            test "a deliberate rename keeps the old entry findable" {
                // The reason the escape hatch exists: a snapshot entry outlives
                // the name the code uses for it.
                let shape = Shape.ofSchemaNamed "LegacySignIn" 3 signInSchema
                Expect.equal shape.Name "LegacySignIn" "recorded under the name the snapshot knows"
            }

            test "an unnamed Schema is refused, rather than recorded namelessly" {
                // A shape with an empty name is the silent failure. Saying so is
                // the whole point of taking the name away from the caller.
                let unnamed = Schema.list Schema.string

                let message =
                    try
                        Shape.ofSchema 1 unnamed |> ignore
                        None
                    with :? ArgumentException as e ->
                        Some e.Message

                match message with
                | Some text -> Expect.stringContains text "ofSchemaNamed" "and says what to do instead"
                | None -> failtest "an unnamed schema was accepted, and would snapshot under no name"
            }

            test "the rules on a sequence's elements are carried, not dropped" {
                // The fields most likely to hold a code set are the list-typed
                // ones -- roles, permissions, grants -- which were exactly the
                // ones left unchecked.
                let shape = Shape.ofSchema 1 suspendedSchema

                let field name =
                    shape.Fields |> List.find (fun f -> f.Name = name)

                Expect.equal (field "reason").Constraints [ reasonRule ] "a scalar's rules, as before"
                Expect.equal (field "reason").ElementConstraints (Some []) "and none recorded on a scalar"
                Expect.equal (field "reasons").Constraints [] "a sequence has no rules of its own"
                Expect.equal (field "reasons").ElementConstraints (Some [ reasonRule ]) "its elements do"
            }

            test "element rules are found through a nullable list" {
                let shape = Shape.ofSchema 1 suspendedSchema
                let field = shape.Fields |> List.find (fun f -> f.Name = "history")

                Expect.equal
                    field.ElementConstraints
                    (Some [ reasonRule ])
                    "a list that may itself be null still reports them"
            }
        ]
