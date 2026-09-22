/// The snapshot file: it round-trips, it is stable, and it reports its own
/// problems with a path.
///
/// Described by an Etymon Schema rather than hand-written, so these are checks
/// on that decision paying off rather than on a bespoke parser.
module Etymon.Schema.Compatibility.Tests.SnapshotTests

open Expecto
open Etymon

let private field name t required constraints : ShapeField =
    {
        Name = name
        Type = t
        Required = required
        Constraints = constraints
        ElementConstraints = Some []
    }

/// One of every FieldType, so nothing in the union goes untested.
let private everyShape: Shape =
    {
        Name = "InvoiceRaised"
        Version = 3
        Fields =
            [
                field "id" (FieldType.Scalar "uuid") true []
                field "lines" (FieldType.Sequence(FieldType.Nested "Line")) true []
                field "tags" (FieldType.Mapping(FieldType.Scalar "string")) false []
                field "note" (FieldType.Nullable(FieldType.Scalar "string")) false []
                field "state" (FieldType.Choice("State", [ "draft"; "sent" ])) true []
                field "extra" FieldType.Unknown false []
                field "total" (FieldType.Scalar "int32") true [ Constraint.Length(Some 1, Some 40) ]
            ]
    }

let private snapshot = ShapeSnapshot.of' [ everyShape ]

let tests =
    testList
        "ShapeSnapshots"
        [
            test "a snapshot round-trips through its own format" {
                match ShapeSnapshots.fromJson (ShapeSnapshots.toJson snapshot) with
                | Ok read -> Expect.equal read snapshot "every field type survives the trip"
                | Error errors -> failtestf "the snapshot did not read back: %s" (ValidationErrors.format errors)
            }

            test "the same shapes always produce the same file" {
                // A snapshot whose bytes depend on the order somebody listed
                // things in produces spurious diffs, and a spurious diff is how
                // a real one stops being read.
                let second: Shape =
                    { everyShape with
                        Name = "InvoiceSettled"
                        Version = 1
                    }

                Expect.equal
                    (ShapeSnapshots.toJson (ShapeSnapshot.of' [ everyShape; second ]))
                    (ShapeSnapshots.toJson (ShapeSnapshot.of' [ second; everyShape ]))
                    "ordered by name and version, whatever order they arrived in"
            }

            test "a malformed file reports the path, not a stack trace" {
                // The reason the format is described by a Schema at all.
                let broken =
                    """{"formatVersion":1,"shapes":[{"name":"A","version":1,"fields":[
                         {"type":{"kind":"scalar","value":"string"},"required":true,"constraints":[]}]}]}"""

                match ShapeSnapshots.fromJson broken with
                | Ok _ -> failtest "a field with no name should not read"
                | Error errors ->
                    let message = ValidationErrors.format errors
                    Expect.stringContains message "shapes[0].fields[0].name" "it says exactly where"
                    Expect.stringContains message "is required" "and what is wrong"
            }

            test "a snapshot reports what it holds" {
                Expect.equal (ShapeSnapshot.names snapshot) [ "InvoiceRaised" ] "the event types"
                Expect.equal (ShapeSnapshot.versionsOf "InvoiceRaised" snapshot) [ 3 ] "and their versions"
                Expect.isSome (ShapeSnapshot.tryLatest "InvoiceRaised" snapshot) "and the current shape"
                Expect.isNone (ShapeSnapshot.tryLatest "Missing" snapshot) "and nothing for what it does not hold"
            }

            test "a snapshot written before element rules existed still reads" {
                // The key is optional, not defaulted: an old file never recorded
                // element rules, and that must read as "not recorded", never as
                // "recorded none" -- the two diff differently.
                let old =
                    """{"formatVersion":1,"shapes":[{"name":"A","version":1,"fields":[
                        {"name":"tags","type":{"kind":"sequence","value":{"kind":"scalar","value":"string"}},"required":true,"constraints":[]}]}]}"""

                match ShapeSnapshots.fromJson old with
                | Ok snapshot ->
                    match ShapeSnapshot.tryLatest "A" snapshot with
                    | Some shape ->
                        Expect.equal
                            (List.head shape.Fields).ElementConstraints
                            None
                            "read as not recorded, not refused"
                    | None -> failtest "the shape should be there"
                | Error errors -> failtestf "an old snapshot should still read: %s" (ValidationErrors.format errors)
            }

            test "recorded-and-empty survives a round trip as recorded, not as unrecorded" {
                // The distinction the whole fix rests on. Some [] is a snapshot
                // that looked and found no rules; None is one that never looked.
                let tags =
                    { field "tags" (FieldType.Sequence(FieldType.Scalar "string")) true [] with
                        ElementConstraints = Some []
                    }

                let shape: Shape =
                    {
                        Name = "A"
                        Version = 1
                        Fields = [ tags ]
                    }

                match ShapeSnapshots.fromJson (ShapeSnapshots.toJson (ShapeSnapshot.of' [ shape ])) with
                | Ok snapshot ->
                    match ShapeSnapshot.tryLatest "A" snapshot with
                    | Some read -> Expect.equal (List.head read.Fields).ElementConstraints (Some []) "still recorded"
                    | None -> failtest "the shape should be there"
                | Error errors -> failtestf "should round-trip: %s" (ValidationErrors.format errors)
            }
        ]
