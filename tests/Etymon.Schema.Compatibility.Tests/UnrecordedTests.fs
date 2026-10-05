/// The snapshot must not lag.
///
/// A harness compares the committed file with the shapes the codecs declare
/// today. A change the matrix calls safe is exactly the change nobody is made
/// to record, and a file that stops describing the bytes is how the next change
/// is measured against the wrong thing. These pin that the lag is reported,
/// that regeneration keeps history, and that stranded versions are found in the
/// file as well as in the code.
module Etymon.Schema.Compatibility.Tests.UnrecordedTests

open Expecto
open Etymon

let private field name t required : ShapeField =
    {
        Name = name
        Type = t
        Required = required
        Constraints = []
        ElementConstraints = Some []
    }

let private shape name version fields : Shape =
    {
        Name = name
        Version = version
        Fields = fields
    }

let private text = FieldType.Scalar "string"

let private raisedV1 =
    shape "InvoiceRaised" 1 [ field "id" text true; field "total" text true ]

let private raisedV2 =
    shape "InvoiceRaised" 2 [ field "id" text true; field "total" text true; field "dueOn" text true ]

let private settledV1 = shape "InvoiceSettled" 1 [ field "id" text true ]

let private snap shapes = ShapeSnapshot.of' shapes

let private messages (problems: Unresolved list) =
    problems |> List.map (fun p -> p.Message)

let tests =
    testList
        "Unrecorded"
        [
            testList
                "regeneration keeps history"
                [
                    test "a version only the committed file carries stays" {
                        let merged = ShapeSnapshot.extend (snap [ raisedV1 ]) (snap [ raisedV2 ])

                        Expect.equal
                            (ShapeSnapshot.versionsOf "InvoiceRaised" merged)
                            [ 1; 2 ]
                            "stored events at v1 exist whether or not the code still declares v1"
                    }

                    test "the current shape replaces the committed shape of the same version" {
                        let withNote =
                            shape "InvoiceRaised" 1 (raisedV1.Fields @ [ field "note" text false ])

                        let merged = ShapeSnapshot.extend (snap [ raisedV1 ]) (snap [ withNote ])

                        match ShapeSnapshot.tryShape "InvoiceRaised" 1 merged with
                        | Some recorded -> Expect.isSome (Shape.tryField "note" recorded) "recorded as the shape is now"
                        | None -> failtest "v1 is gone"

                        Expect.equal (List.length merged.Shapes) 1 "and not duplicated"
                    }

                    test "a new shape is added, and the result is ordered" {
                        let merged = ShapeSnapshot.extend (snap [ settledV1 ]) (snap [ raisedV1 ])

                        Expect.equal
                            (merged.Shapes |> List.map (fun s -> s.Name))
                            [ "InvoiceRaised"; "InvoiceSettled" ]
                            "name order, so the file does not churn"
                    }
                ]

            testList
                "the lag is reported"
                [
                    test "an identical pair has nothing unrecorded" {
                        let snapshot = snap [ raisedV1; settledV1 ]
                        Expect.isEmpty (Events.unrecorded snapshot snapshot) "nothing to regenerate"
                    }

                    test "an added optional field is unrecorded, although the matrix calls it safe" {
                        // The defect this exists for. The matrix is right that
                        // the field breaks nothing; the file still has to say
                        // the field exists, or a later removal of the same field
                        // is measured against a file that never carried it.
                        let withNote =
                            shape "InvoiceRaised" 1 (raisedV1.Fields @ [ field "note" text false ])

                        match Events.unrecorded (snap [ raisedV1 ]) (snap [ withNote ]) with
                        | [ problem ] ->
                            Expect.equal problem.Shape "InvoiceRaised" "the shape"
                            Expect.stringContains problem.Message "lacks 'note'" "the field the file does not carry"
                            Expect.stringContains problem.Message "ShapeSnapshot.extend" "and how to regenerate"

                            Expect.stringContains
                                problem.Message
                                "what was already written"
                                "in the event policy's words"
                        | other -> failtestf "expected the lag to be reported once, got %A" other
                    }

                    test "a removed optional field is unrecorded" {
                        let withNote =
                            shape "InvoiceRaised" 1 (raisedV1.Fields @ [ field "note" text false ])

                        match Events.unrecorded (snap [ withNote ]) (snap [ raisedV1 ]) with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "still carries 'note'" "the file is behind"
                        | other -> failtestf "expected one report, got %A" other
                    }

                    test "a new shape is unrecorded" {
                        match Events.unrecorded (snap [ raisedV1 ]) (snap [ raisedV1; settledV1 ]) with
                        | [ problem ] ->
                            Expect.equal problem.Shape "InvoiceSettled" "the new one"

                            Expect.stringContains problem.Message "v1 is not in the committed snapshot" "said plainly"
                        | other -> failtestf "expected one report, got %A" other
                    }

                    test "a new version is unrecorded" {
                        match Events.unrecorded (snap [ raisedV1 ]) (snap [ raisedV2 ]) with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "v2 is not in the committed snapshot" "by version"
                        | other -> failtestf "expected one report, got %A" other
                    }

                    test "a field recorded differently is unrecorded" {
                        // A version-1 file never recorded element rules; the
                        // matrix compares nothing against None, and the file
                        // still wants regenerating so the next comparison has
                        // both sides.
                        let unrecordedRules =
                            { field "tags" (FieldType.Sequence text) true with
                                ElementConstraints = None
                            }

                        let recordedRules = field "tags" (FieldType.Sequence text) true

                        match
                            Events.unrecorded
                                (snap [ shape "R" 1 [ unrecordedRules ] ])
                                (snap [ shape "R" 1 [ recordedRules ] ])
                        with
                        | [ problem ] -> Expect.stringContains problem.Message "records 'tags' differently" "named"
                        | other -> failtestf "expected one report, got %A" other
                    }

                    test "a version the code no longer declares is not a lag" {
                        // The committed file carries v1 because stored events
                        // do; the code declares v2. Nothing about v1 wants
                        // regenerating.
                        Expect.isEmpty
                            (Events.unrecorded (snap [ raisedV1; raisedV2 ]) (snap [ raisedV2 ]))
                            "v2 is recorded, and v1 is history"
                    }

                    test "the wire policy says sent, never written" {
                        let withNote = shape "InvoiceDto" 1 (raisedV1.Fields @ [ field "note" text false ])

                        match Wire.unrecorded (snap [ { raisedV1 with Name = "InvoiceDto" } ]) (snap [ withNote ]) with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "what was already sent" "the wire noun"
                            Expect.isFalse (problem.Message.Contains "written") "and not the event one"
                        | other -> failtestf "expected one report, got %A" other
                    }
                ]

            testList
                "check carries the lag and finds stranded versions in the file"
                [
                    test
                        "a version bump without an upcaster is stranded, although the code declares only the new version" {
                        // The harness derives the current snapshot from the
                        // codecs, so the current snapshot has v2 alone. The
                        // committed file has v1. Looking at the current snapshot
                        // alone found nothing stranded.
                        let problems = Events.check [] [] (snap [ raisedV1 ]) (snap [ raisedV2 ])

                        Expect.isTrue
                            (messages problems |> List.exists (fun m -> m.Contains "Nothing reads v1"))
                            "v1 is in the file, so events at v1 exist, so something must read v1"
                    }

                    test "a declared upcaster settles the stranded version" {
                        let upcaster =
                            {
                                Shape = "InvoiceRaised"
                                Reads = 1
                                Produces = 2
                            }

                        let problems = Events.check [ upcaster ] [] (snap [ raisedV1 ]) (snap [ raisedV2 ])

                        Expect.isFalse
                            (messages problems |> List.exists (fun m -> m.Contains "Nothing reads"))
                            "v1 reaches v2"

                        Expect.isTrue
                            (messages problems
                             |> List.exists (fun m -> m.Contains "v2 is not in the committed snapshot"))
                            "and the file still wants regenerating"
                    }

                    test "an identical pair is settled" {
                        let snapshot = snap [ raisedV1; settledV1 ]
                        Expect.isEmpty (Events.check [] [] snapshot snapshot) "nothing unresolved"
                    }

                    test "after regenerating with extend, only the upcaster is owed" {
                        let committed = ShapeSnapshot.extend (snap [ raisedV1 ]) (snap [ raisedV2 ])

                        match Events.check [] [] committed (snap [ raisedV2 ]) with
                        | [ problem ] -> Expect.stringContains problem.Message "Nothing reads v1" "the one real debt"
                        | other -> failtestf "expected the upcaster alone to be owed, got %A" other
                    }

                    test "a file that carries a higher version than the code strands the file's version, named as such" {
                        // A reverted bump, or a file regenerated on another
                        // branch. The code reads v1; the file says v2 exists.
                        let problems = Events.check [] [] (snap [ raisedV1; raisedV2 ]) (snap [ raisedV1 ])

                        Expect.isTrue
                            (messages problems
                             |> List.exists (fun m -> m.Contains "current shape is v1" && m.Contains "Nothing reads v2"))
                            "toward the version the code declares"
                    }

                    test "a request upcaster settles the version found only in the file" {
                        let upcaster =
                            {
                                Shape = "InvoiceRaised"
                                Reads = 1
                                Produces = 2
                            }

                        let problems =
                            Wire.unresolvedRequests [ upcaster ] [] (snap [ raisedV1 ]) (snap [ raisedV2 ])

                        Expect.isFalse
                            (messages problems |> List.exists (fun m -> m.Contains "Nothing reads"))
                            "v1 reaches v2"

                        Expect.isTrue
                            (messages problems
                             |> List.exists (fun m -> m.Contains "v2 is not in the committed snapshot"))
                            "and the file still wants regenerating"
                    }

                    test "requests find stranded versions in the file too" {
                        let problems = Wire.unresolvedRequests [] [] (snap [ raisedV1 ]) (snap [ raisedV2 ])

                        Expect.isTrue
                            (messages problems
                             |> List.exists (fun m -> m.Contains "bodies already sent" && m.Contains "Nothing reads v1"))
                            "in the wire policy's words"
                    }
                ]

            testList
                "responses have their own unresolved report"
                [
                    test "a removal and an addition on a response is refused, not guessed" {
                        let before = snap [ { raisedV1 with Name = "InvoiceDto" } ]

                        let after =
                            snap [ shape "InvoiceDto" 1 [ field "id" text true; field "amount" text true ] ]

                        let problems = Wire.unresolvedResponses [] before after

                        Expect.isTrue
                            (messages problems
                             |> List.exists (fun m ->
                                 m.Contains "whether clients already written can read what you send"
                             ))
                            "what the ambiguity decides, for a response"
                    }

                    test "a lagging response snapshot is reported" {
                        let before = snap [ shape "InvoiceDto" 1 [ field "id" text true ] ]

                        let after =
                            snap [ shape "InvoiceDto" 1 [ field "id" text true; field "note" text false ] ]

                        match Wire.unresolvedResponses [] before after with
                        | [ problem ] -> Expect.stringContains problem.Message "lacks 'note'" "the lag"
                        | other -> failtestf "expected one report, got %A" other
                    }

                    test "an identical pair is settled" {
                        let snapshot = snap [ shape "InvoiceDto" 1 [ field "id" text true ] ]
                        Expect.isEmpty (Wire.unresolvedResponses [] snapshot snapshot) "nothing unresolved"
                    }
                ]
        ]
