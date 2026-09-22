/// The compatibility table, one test per row.
///
/// These are the verdicts the package exists to give, so they are pinned
/// individually rather than through a few composite cases. A wrong verdict here
/// is worse than no verdict: somebody ships a change because a tool said it was
/// safe, and the log stops being readable.
module Etymon.Schema.Compatibility.Tests.CompatibilityTests

open Expecto
open Etymon

// ---------------------------------------------------------------------------
// Shapes built by hand, so a test says exactly what changed.
// ---------------------------------------------------------------------------

let private field name t required constraints : ShapeField =
    {
        Name = name
        Type = t
        Required = required
        Constraints = constraints
        ElementConstraints = Some []
    }

let private shape version fields : Shape =
    {
        Name = "InvoiceRaised"
        Version = version
        Fields = fields
    }

let private text = FieldType.Scalar "string"
let private number = FieldType.Scalar "int32"

/// v1: a required id and a required total.
let private v1 = shape 1 [ field "id" text true []; field "total" number true [] ]

let private changesFor before after =
    Compatibility.betweenShapes [] before after

/// The directions a set of changes breaks, as a set.
let private directions (changes: ShapeChange list) =
    changes |> List.collect (fun c -> c.Breaks |> List.map fst) |> Set.ofList

let private breaksBackward changes =
    directions changes |> Set.contains Direction.Backward

let private breaksForward changes =
    directions changes |> Set.contains Direction.Forward

let tests =
    testList
        "Compatibility"
        [
            testList
                "the table"
                [
                    test "an added optional field breaks neither direction" {
                        let after = shape 2 (v1.Fields @ [ field "note" text false [] ])
                        let changes = changesFor v1 after

                        Expect.isFalse (breaksBackward changes) "old events simply lack it"
                        Expect.isFalse (breaksForward changes) "old code simply ignores it"
                    }

                    test "an added required field breaks backward" {
                        let after = shape 2 (v1.Fields @ [ field "dueOn" text true [] ])
                        let changes = changesFor v1 after

                        Expect.isTrue (breaksBackward changes) "events already written have no dueOn"
                        Expect.isFalse (breaksForward changes) "old code does not look for it"

                        // The matrix states the fact. What to do about it is the
                        // policy's job -- an upcaster for an event or a request
                        // body, nothing at all for a response -- so the advice
                        // is not in here.
                        let reasons = changes |> List.collect (fun c -> c.Breaks) |> List.map snd

                        Expect.isTrue
                            (reasons |> List.exists (fun r -> r.Contains "already written"))
                            "and it says what is already out there does not carry it"

                        Expect.isFalse
                            (reasons |> List.exists (fun r -> r.Contains "upcaster"))
                            "without prescribing a fix that only one policy has"
                    }

                    test "a removed optional field breaks neither direction" {
                        let withNote = shape 1 (v1.Fields @ [ field "note" text false [] ])
                        let changes = changesFor withNote (shape 2 v1.Fields)

                        Expect.isFalse (breaksBackward changes) "nothing reads it now"
                        Expect.isFalse (breaksForward changes) "and nothing required it"
                    }

                    test "a removed required field breaks forward" {
                        let changes = changesFor v1 (shape 2 [ field "id" text true [] ])

                        Expect.isFalse (breaksBackward changes) "old events still carry it, harmlessly"
                        Expect.isTrue (breaksForward changes) "old code requires a field new events will not carry"
                    }

                    test "a changed type breaks both directions" {
                        let after = shape 2 [ field "id" text true []; field "total" text true [] ]
                        let changes = changesFor v1 after

                        Expect.isTrue (breaksBackward changes) "old events hold the old type"
                        Expect.isTrue (breaksForward changes) "old code expects the old type"
                    }

                    test "a changed rule is reported in both directions, because which way needs a person" {
                        let narrowed =
                            shape
                                2
                                [
                                    field "id" text true []
                                    field "total" number true [ Constraint.Range(Some(Num.Int 1L), None, false, false) ]
                                ]

                        let changes = changesFor v1 narrowed

                        Expect.isTrue (breaksBackward changes) "narrowing would strand events already written"
                        Expect.isTrue (breaksForward changes) "widening would have old code reject new events"
                    }

                    test "an added union case breaks forward" {
                        let before =
                            shape 1 [ field "state" (FieldType.Choice("State", [ "draft"; "sent" ])) true [] ]

                        let after =
                            shape
                                2
                                [
                                    field "state" (FieldType.Choice("State", [ "draft"; "sent"; "void" ])) true []
                                ]

                        let changes = changesFor before after

                        Expect.isTrue (breaksForward changes) "old code has no branch for 'void'"
                        Expect.isFalse (breaksBackward changes) "no event carries it yet"
                    }

                    test "a removed union case breaks backward" {
                        let before =
                            shape
                                1
                                [
                                    field "state" (FieldType.Choice("State", [ "draft"; "sent"; "void" ])) true []
                                ]

                        let after =
                            shape 2 [ field "state" (FieldType.Choice("State", [ "draft"; "sent" ])) true [] ]

                        let changes = changesFor before after

                        Expect.isTrue (breaksBackward changes) "events already written carry 'void'"
                        Expect.isFalse (breaksForward changes) "old code still handles everything new code writes"
                    }

                    test "no change is no verdict" {
                        Expect.isEmpty (changesFor v1 (shape 2 v1.Fields)) "nothing to say"
                    }
                ]

            testList
                "a rename is declared, never detected"
                [
                    test "an undeclared removal and addition is refused, not guessed" {
                        let before = ShapeSnapshot.of' [ v1 ]

                        let after =
                            ShapeSnapshot.of' [ shape 2 [ field "id" text true []; field "amount" number true [] ] ]

                        match Events.undeclaredRenames [] before after with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "'total'" "it names what went"
                            Expect.stringContains problem.Message "'amount'" "and what arrived"

                            Expect.stringContains
                                problem.Message
                                "either a rename or separate changes"
                                "and the ambiguity"

                            Expect.stringContains problem.Message "Rename" "and how to settle it"
                        | other -> failtestf "expected one refusal, got %A" other
                    }

                    test "a declared rename settles it" {
                        let before = ShapeSnapshot.of' [ v1 ]

                        let after =
                            ShapeSnapshot.of' [ shape 2 [ field "id" text true []; field "amount" number true [] ] ]

                        let renames =
                            [
                                {
                                    Shape = "InvoiceRaised"
                                    From = "total"
                                    To = "amount"
                                }
                            ]

                        Expect.isEmpty (Events.undeclaredRenames renames before after) "nothing left ambiguous"
                    }

                    test "a declared rename is not reported as a removal and an addition" {
                        let renames =
                            [
                                {
                                    Shape = "InvoiceRaised"
                                    From = "total"
                                    To = "amount"
                                }
                            ]

                        let after = shape 2 [ field "id" text true []; field "amount" number true [] ]
                        let changes = Compatibility.betweenShapes renames v1 after

                        Expect.isEmpty changes "the same field under a new name changed nothing else"
                    }

                    test "a removal on its own is not ambiguous" {
                        let before = ShapeSnapshot.of' [ v1 ]
                        let after = ShapeSnapshot.of' [ shape 2 [ field "id" text true [] ] ]

                        Expect.isEmpty (Events.undeclaredRenames [] before after) "a removal is a removal"
                    }
                ]

            testList
                "every stored version must be readable"
                [
                    test "a gap in the chain is named by version" {
                        let snapshot =
                            ShapeSnapshot.of' [ shape 1 v1.Fields; shape 2 v1.Fields; shape 3 v1.Fields ]

                        let upcasters =
                            [
                                {
                                    Shape = "InvoiceRaised"
                                    Reads = 2
                                    Produces = 3
                                }
                            ]

                        match Events.unreachable upcasters snapshot with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "v1 and v2 and v3" "it lists what is stored"
                            Expect.stringContains problem.Message "current shape is v3" "and what is current"
                            Expect.stringContains problem.Message "v2 -> v3" "and what exists"
                            Expect.stringContains problem.Message "Nothing reads v1" "and names the gap"
                        | other -> failtestf "expected one gap, got %A" other
                    }

                    test "a complete chain is reachable through several steps" {
                        let snapshot =
                            ShapeSnapshot.of' [ shape 1 v1.Fields; shape 2 v1.Fields; shape 3 v1.Fields ]

                        let upcasters =
                            [
                                {
                                    Shape = "InvoiceRaised"
                                    Reads = 1
                                    Produces = 2
                                }
                                {
                                    Shape = "InvoiceRaised"
                                    Reads = 2
                                    Produces = 3
                                }
                            ]

                        Expect.isEmpty (Events.unreachable upcasters snapshot) "v1 reaches v3 by way of v2"
                    }

                    test "a chain that skips a version is still a chain" {
                        let snapshot = ShapeSnapshot.of' [ shape 1 v1.Fields; shape 3 v1.Fields ]

                        let upcasters =
                            [
                                {
                                    Shape = "InvoiceRaised"
                                    Reads = 1
                                    Produces = 3
                                }
                            ]

                        Expect.isEmpty (Events.unreachable upcasters snapshot) "one hop is enough"
                    }

                    test "one version needs no upcasters at all" {
                        Expect.isEmpty
                            (Events.unreachable [] (ShapeSnapshot.of' [ shape 1 v1.Fields ]))
                            "nothing older exists"
                    }

                    test "no upcasters at all is reported, not assumed fine" {
                        let snapshot = ShapeSnapshot.of' [ shape 1 v1.Fields; shape 2 v1.Fields ]

                        match Events.unreachable [] snapshot with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "No upcasters are declared" "said plainly"
                        | other -> failtestf "expected one gap, got %A" other
                    }
                ]

            testList
                "the verdict is prose"
                [
                    test "a report names the event, the change and the consequence" {
                        let after = shape 2 (v1.Fields @ [ field "dueOn" text true [] ])
                        let report = Compatibility.report (changesFor v1 after)

                        Expect.stringContains report "InvoiceRaised" "the event"
                        Expect.stringContains report "Backward" "the direction"
                        Expect.stringContains report "already written" "and what it means"
                    }

                    test "nothing to report says so in a sentence" {
                        Expect.stringContains
                            (Compatibility.report [])
                            "No shape changed"
                            "rather than printing nothing at all"
                    }
                ]

            testList
                "element rules"
                [
                    test "narrowing the rules on a sequence's elements is reported" {
                        let rule codes = (Check.oneOf codes).Constraint

                        let roles codes =
                            { field "roles" (FieldType.Sequence(FieldType.Scalar "string")) true [] with
                                ElementConstraints = Some [ rule codes ]
                            }

                        let before = shape 1 [ roles [ "read"; "write"; "admin" ] ]
                        let after = shape 2 [ roles [ "read"; "write" ] ]

                        match changesFor before after with
                        | [ change ] ->
                            Expect.stringContains
                                change.Description
                                "each element of 'roles'"
                                "named as the elements' rules"
                        | other -> failtestf "expected the element rules to be reported, got %A" other
                    }

                    test "a report ends its sentences once" {
                        // Every reason already ends in a full stop; the report
                        // used to add another to each.
                        let a = shape 1 [ field "a" text true [] ]
                        let b = shape 2 [ field "a" text false [] ]
                        let rendered = Compatibility.report (changesFor a b)
                        Expect.isFalse (rendered.Contains "..") "no doubled full stops"
                        Expect.stringContains rendered "." "but sentences still end"
                    }

                    test "a snapshot that never recorded element rules is not compared on them" {
                        // The regression this replaces: an old snapshot read as
                        // "no rules", and the first diff after the upgrade
                        // reported a narrowing on every list field with item
                        // rules, with nothing to tell it from a real one.
                        let rule = (Check.oneOf [ "read"; "write" ]).Constraint

                        let roles recorded =
                            { field "roles" (FieldType.Sequence(FieldType.Scalar "string")) true [] with
                                ElementConstraints = recorded
                            }

                        let unrecorded = shape 1 [ roles None ]
                        let recorded = shape 1 [ roles (Some [ rule ]) ]

                        Expect.isEmpty
                            (changesFor unrecorded recorded)
                            "nothing to compare against, so nothing reported"

                        Expect.isEmpty (changesFor recorded unrecorded) "in either direction"

                        Expect.isNonEmpty
                            (changesFor (shape 1 [ roles (Some []) ]) recorded)
                            "but recorded-and-empty against recorded rules is a real change"
                    }
                ]

            testList
                "widening by null"
                [
                    test "adding null breaks only forward" {
                        match
                            changesFor
                                (shape 1 [ field "a" text true [] ])
                                (shape 2 [ field "a" (FieldType.Nullable text) true [] ])
                        with
                        | [ change ] ->
                            Expect.stringContains
                                change.Description
                                "now admits null"
                                "named as a widening, not a type change"

                            Expect.equal
                                (change.Breaks |> List.map fst)
                                [ Direction.Forward ]
                                "old code may meet a null; old data still reads"
                        | other -> failtestf "expected one change, got %A" other
                    }

                    test "removing null breaks only backward" {
                        match
                            changesFor
                                (shape 1 [ field "a" (FieldType.Nullable text) true [] ])
                                (shape 2 [ field "a" text true [] ])
                        with
                        | [ change ] ->
                            Expect.equal
                                (change.Breaks |> List.map fst)
                                [ Direction.Backward ]
                                "old data may hold a null nothing reads"
                        | other -> failtestf "expected one change, got %A" other
                    }

                    test "a different type still breaks both ways" {
                        match
                            changesFor (shape 1 [ field "a" text true [] ]) (shape 2 [ field "a" number true [] ])
                        with
                        | [ change ] ->
                            Expect.equal
                                (change.Breaks |> List.map fst |> List.sort)
                                [ Direction.Backward; Direction.Forward ]
                                "as before"
                        | other -> failtestf "expected one change, got %A" other
                    }
                ]
        ]
