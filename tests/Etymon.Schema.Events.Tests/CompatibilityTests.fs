/// The compatibility table, one test per row.
///
/// These are the verdicts the package exists to give, so they are pinned
/// individually rather than through a few composite cases. A wrong verdict here
/// is worse than no verdict: somebody ships a change because a tool said it was
/// safe, and the log stops being readable.
module Etymon.Schema.Events.Tests.CompatibilityTests

open Expecto
open Etymon

// ---------------------------------------------------------------------------
// Shapes built by hand, so a test says exactly what changed.
// ---------------------------------------------------------------------------

let private field name t required constraints : EventField =
    {
        Name = name
        Type = t
        Required = required
        Constraints = constraints
    }

let private shape version fields : EventShape =
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

                        let reason =
                            changes
                            |> List.collect (fun c -> c.Breaks)
                            |> List.map snd
                            |> List.filter (fun r -> r.Contains "upcaster")

                        Expect.isNonEmpty reason "and it says an upcaster must supply one"
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
                        let before = EventSnapshot.of' [ v1 ]

                        let after =
                            EventSnapshot.of' [ shape 2 [ field "id" text true []; field "amount" number true [] ] ]

                        match Upcasters.undeclaredRenames [] before after with
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
                        let before = EventSnapshot.of' [ v1 ]

                        let after =
                            EventSnapshot.of' [ shape 2 [ field "id" text true []; field "amount" number true [] ] ]

                        let renames =
                            [
                                {
                                    Event = "InvoiceRaised"
                                    From = "total"
                                    To = "amount"
                                }
                            ]

                        Expect.isEmpty (Upcasters.undeclaredRenames renames before after) "nothing left ambiguous"
                    }

                    test "a declared rename is not reported as a removal and an addition" {
                        let renames =
                            [
                                {
                                    Event = "InvoiceRaised"
                                    From = "total"
                                    To = "amount"
                                }
                            ]

                        let after = shape 2 [ field "id" text true []; field "amount" number true [] ]
                        let changes = Compatibility.betweenShapes renames v1 after

                        Expect.isEmpty changes "the same field under a new name changed nothing else"
                    }

                    test "a removal on its own is not ambiguous" {
                        let before = EventSnapshot.of' [ v1 ]
                        let after = EventSnapshot.of' [ shape 2 [ field "id" text true [] ] ]

                        Expect.isEmpty (Upcasters.undeclaredRenames [] before after) "a removal is a removal"
                    }
                ]

            testList
                "every stored version must be readable"
                [
                    test "a gap in the chain is named by version" {
                        let snapshot =
                            EventSnapshot.of' [ shape 1 v1.Fields; shape 2 v1.Fields; shape 3 v1.Fields ]

                        let upcasters =
                            [
                                {
                                    Event = "InvoiceRaised"
                                    Reads = 2
                                    Produces = 3
                                }
                            ]

                        match Upcasters.unreachable upcasters snapshot with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "v1 and v2 and v3" "it lists what is stored"
                            Expect.stringContains problem.Message "current shape is v3" "and what is current"
                            Expect.stringContains problem.Message "v2 -> v3" "and what exists"
                            Expect.stringContains problem.Message "Nothing reads v1" "and names the gap"
                        | other -> failtestf "expected one gap, got %A" other
                    }

                    test "a complete chain is reachable through several steps" {
                        let snapshot =
                            EventSnapshot.of' [ shape 1 v1.Fields; shape 2 v1.Fields; shape 3 v1.Fields ]

                        let upcasters =
                            [
                                {
                                    Event = "InvoiceRaised"
                                    Reads = 1
                                    Produces = 2
                                }
                                {
                                    Event = "InvoiceRaised"
                                    Reads = 2
                                    Produces = 3
                                }
                            ]

                        Expect.isEmpty (Upcasters.unreachable upcasters snapshot) "v1 reaches v3 by way of v2"
                    }

                    test "a chain that skips a version is still a chain" {
                        let snapshot = EventSnapshot.of' [ shape 1 v1.Fields; shape 3 v1.Fields ]

                        let upcasters =
                            [
                                {
                                    Event = "InvoiceRaised"
                                    Reads = 1
                                    Produces = 3
                                }
                            ]

                        Expect.isEmpty (Upcasters.unreachable upcasters snapshot) "one hop is enough"
                    }

                    test "one version needs no upcasters at all" {
                        Expect.isEmpty
                            (Upcasters.unreachable [] (EventSnapshot.of' [ shape 1 v1.Fields ]))
                            "nothing older exists"
                    }

                    test "no upcasters at all is reported, not assumed fine" {
                        let snapshot = EventSnapshot.of' [ shape 1 v1.Fields; shape 2 v1.Fields ]

                        match Upcasters.unreachable [] snapshot with
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
                        Expect.stringContains report "events already written" "and what it means"
                    }

                    test "nothing to report says so in a sentence" {
                        Expect.stringContains
                            (Compatibility.report [])
                            "No event shape changed"
                            "rather than printing nothing at all"
                    }
                ]
        ]
