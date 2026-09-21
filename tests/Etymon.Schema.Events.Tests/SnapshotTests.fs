/// The snapshot file: it round-trips, it is stable, and it reports its own
/// problems with a path.
///
/// Described by an Etymon Schema rather than hand-written, so these are checks
/// on that decision paying off rather than on a bespoke parser.
module Etymon.Schema.Events.Tests.SnapshotTests

open Expecto
open Etymon

let private field name t required constraints : EventField =
    {
        Name = name
        Type = t
        Required = required
        Constraints = constraints
    }

/// One of every FieldType, so nothing in the union goes untested.
let private everyShape: EventShape =
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

let private snapshot = EventSnapshot.of' [ everyShape ]

let tests =
    testList
        "EventSnapshots"
        [
            test "a snapshot round-trips through its own format" {
                match EventSnapshots.fromJson (EventSnapshots.toJson snapshot) with
                | Ok read -> Expect.equal read snapshot "every field type survives the trip"
                | Error errors -> failtestf "the snapshot did not read back: %s" (ValidationErrors.format errors)
            }

            test "the same shapes always produce the same file" {
                // A snapshot whose bytes depend on the order somebody listed
                // things in produces spurious diffs, and a spurious diff is how
                // a real one stops being read.
                let second: EventShape =
                    { everyShape with
                        Name = "InvoiceSettled"
                        Version = 1
                    }

                Expect.equal
                    (EventSnapshots.toJson (EventSnapshot.of' [ everyShape; second ]))
                    (EventSnapshots.toJson (EventSnapshot.of' [ second; everyShape ]))
                    "ordered by name and version, whatever order they arrived in"
            }

            test "a malformed file reports the path, not a stack trace" {
                // The reason the format is described by a Schema at all.
                let broken =
                    """{"formatVersion":1,"shapes":[{"name":"A","version":1,"fields":[
                         {"type":{"kind":"scalar","value":"string"},"required":true,"constraints":[]}]}]}"""

                match EventSnapshots.fromJson broken with
                | Ok _ -> failtest "a field with no name should not read"
                | Error errors ->
                    let message = ValidationErrors.format errors
                    Expect.stringContains message "shapes[0].fields[0].name" "it says exactly where"
                    Expect.stringContains message "is required" "and what is wrong"
            }

            test "a snapshot reports what it holds" {
                Expect.equal (EventSnapshot.names snapshot) [ "InvoiceRaised" ] "the event types"
                Expect.equal (EventSnapshot.versionsOf "InvoiceRaised" snapshot) [ 3 ] "and their versions"
                Expect.isSome (EventSnapshot.tryLatest "InvoiceRaised" snapshot) "and the current shape"
                Expect.isNone (EventSnapshot.tryLatest "Missing" snapshot) "and nothing for what it does not hold"
            }
        ]
