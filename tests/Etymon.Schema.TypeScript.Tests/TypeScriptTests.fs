module Etymon.Schema.TypeScript.Tests.TypeScriptTests

open Expecto
open Etymon
open Etymon.Tests
open Etymon.Tests.Domain

// ---- shapes that the shared domain does not cover ---------------------------

type Nullables =
    {
        Absent: string option
        Empty: string option
        Both: string option option
        Names: string option list
    }

module Nullables =
    let schema =
        Schema.object "Nullables" {
            // A key that may be missing entirely.
            let! absent = Schema.optional "absent" Schema.string (fun n -> n.Absent)
            // A key that is always there but may hold null.
            and! empty =
                Schema.required "empty" (Schema.nullable Schema.string) (fun n -> n.Empty)
            // A key that may be missing and may hold null.
            and! both = Schema.optional "both" (Schema.nullable Schema.string) (fun n -> n.Both)

            and! names =
                Schema.required "names" (Schema.list (Schema.nullable Schema.string)) (fun n -> n.Names)

            return
                {
                    Absent = absent
                    Empty = empty
                    Both = both
                    Names = names
                }
        }

[<NoComparison>]
type Widths =
    {
        Small: int
        Large: int64
        Money: decimal
        Ratio: float
        Flag: bool
        Blob: byte[]
        Anything: System.Text.Json.Nodes.JsonNode
        Counts: Map<string, int>
    }

module Widths =
    let schema =
        Schema.object "Widths" {
            let! small = Schema.required "small" Schema.int (fun w -> w.Small)
            and! large = Schema.required "large" Schema.int64 (fun w -> w.Large)
            and! money = Schema.required "money" Schema.decimal (fun w -> w.Money)
            and! ratio = Schema.required "ratio" Schema.float (fun w -> w.Ratio)
            and! flag = Schema.required "flag" Schema.bool (fun w -> w.Flag)
            and! blob = Schema.required "blob" Schema.bytes (fun w -> w.Blob)
            and! anything = Schema.required "anything" Schema.raw (fun w -> w.Anything)
            and! counts = Schema.required "counts" (Schema.map Schema.int) (fun w -> w.Counts)

            return
                {
                    Small = small
                    Large = large
                    Money = money
                    Ratio = ratio
                    Flag = flag
                    Blob = blob
                    Anything = anything
                    Counts = counts
                }
        }

type Membership = { Role: string; Tier: string option }

module Membership =
    let roleSchema =
        Schema.string |> Schema.constrain (Check.oneOf [ "admin"; "member"; "guest" ])

    let schema =
        Schema.object "Membership" {
            let! role =
                Schema.required "role" roleSchema (fun m -> m.Role)
                |> Schema.doc "What this member may do"

            and! tier =
                Schema.optional
                    "tier"
                    (Schema.string |> Schema.constrain (Check.oneOf [ "free"; "paid" ]))
                    (fun m -> m.Tier)

            return { Role = role; Tier = tier }
        }

/// The declaration block for one named type, so an assertion can talk about a
/// single type without the rest of the file getting in the way.
let private blockFor (name: string) (generated: string) =
    let interfaceMarker = "export interface " + name + " {"
    let typeMarker = "export type " + name + " ="

    match generated.IndexOf interfaceMarker, generated.IndexOf typeMarker with
    | -1, -1 -> failwithf "%s is not in the generated declarations:\n%s" name generated
    | -1, start ->
        let finish = generated.IndexOf("};", start)
        generated.Substring(start, finish - start + 2)
    | start, _ ->
        let finish = generated.IndexOf("\n}", start)
        generated.Substring(start, finish - start + 2)

let tests =
    testList
        "TypeScript"
        [
            testList
                "the whole file"
                [
                    test "a record, its nested types and its constrained fields" {
                        Snapshot.verify "person" (TypeScript.emitOne Person.schema)
                    }

                    test "a union becomes a discriminated union of object literals" {
                        Snapshot.verify "shape" (TypeScript.emitOne Shape.schema)
                    }

                    test "a recursive type refers to itself by name" {
                        Snapshot.verify "tree" (TypeScript.emitOne Tree.schema)
                    }

                    test "optional and nullable stay apart" {
                        Snapshot.verify "nullables" (TypeScript.emitOne Nullables.schema)
                    }

                    test "every primitive width" { Snapshot.verify "widths" (TypeScript.emitOne Widths.schema) }

                    test "constrained strings and field documentation" {
                        Snapshot.verify "membership" (TypeScript.emitOne Membership.schema)
                    }

                    test "several schemas share one file, each type written once" {
                        // Person already reaches Address, so asking for both must
                        // not emit Address twice.
                        let generated =
                            TypeScript.emit [ Person.schema.Info; Address.schema.Info; Membership.schema.Info ]

                        let occurrences =
                            generated.Split([| "export interface Address {" |], System.StringSplitOptions.None).Length
                            - 1

                        Expect.equal occurrences 1 "Address is declared exactly once"
                        Expect.stringContains generated "export interface Person {" "and Person is there"
                        Expect.stringContains generated "export interface Membership {" "and so is Membership"
                    }
                ]

            testList
                "the parts that are easy to get subtly wrong"
                [
                    test "an absent key is a question mark and a null value is a union" {
                        let block = blockFor "Nullables" (TypeScript.emitOne Nullables.schema)

                        // These three lines are the whole reason optional and
                        // nullable are separate concepts in the schema type.
                        Expect.stringContains block "readonly absent?: string;" "may be missing"
                        Expect.stringContains block "readonly empty: string | null;" "always present, may be null"
                        Expect.stringContains block "readonly both?: string | null;" "may be either"
                    }

                    test "a union inside a list is parenthesised" {
                        let block = blockFor "Nullables" (TypeScript.emitOne Nullables.schema)

                        // `string | null[]` would mean "a string, or an array of
                        // nulls", which is a different type and a silent lie.
                        Expect.stringContains block "readonly names: readonly (string | null)[];" "parenthesised"
                    }

                    test "a set of permitted values becomes a union of literals" {
                        let block = blockFor "Membership" (TypeScript.emitOne Membership.schema)

                        // A typo in the frontend is then a compile error rather
                        // than a request the server rejects at runtime.
                        Expect.stringContains block "readonly role: \"admin\" | \"member\" | \"guest\";" "literals"
                    }

                    test "a constrained optional keeps both its literals and its question mark" {
                        let block = blockFor "Membership" (TypeScript.emitOne Membership.schema)
                        Expect.stringContains block "readonly tier?: \"free\" | \"paid\";" "both at once"
                    }

                    test "field documentation is carried across" {
                        let block = blockFor "Membership" (TypeScript.emitOne Membership.schema)
                        Expect.stringContains block "/** What this member may do */" "the prose survives"
                    }

                    test "a case with no payload is the tag alone" {
                        let block = blockFor "Shape" (TypeScript.emitOne Shape.schema)
                        Expect.stringContains block "{ readonly kind: \"point\" }" "no value field"

                        Expect.stringContains
                            block
                            "{ readonly kind: \"circle\"; readonly value: number }"
                            "and a case with one keeps it"
                    }

                    test "a union closes on its last case" {
                        let generated = TypeScript.emitOne Shape.schema

                        // A semicolon alone on the next line is valid TypeScript
                        // and looks like a mistake in a reviewed file.
                        Expect.stringContains generated "{ readonly kind: \"point\" };" "closed in place"
                        Expect.isFalse (generated.Contains "\n;") "no orphan semicolon"
                    }

                    test "a number TypeScript cannot hold says so" {
                        let block = blockFor "Widths" (TypeScript.emitOne Widths.schema)

                        // int64 and decimal both become `number`, because that is
                        // genuinely what goes on the wire. Saying only that would
                        // let the frontend believe a precision JSON does not have.
                        Expect.stringContains block "beyond 2^53" "the int64 warning"
                        Expect.stringContains block "exactness is not preserved" "the decimal warning"
                    }

                    test "an int that fits is left alone" {
                        let block = blockFor "Widths" (TypeScript.emitOne Widths.schema)
                        let lines = block.Split '\n'

                        let smallIndex = lines |> Array.findIndex (fun l -> l.Contains "readonly small")

                        Expect.isFalse
                            (lines[smallIndex - 1].Contains "/**")
                            "an int32 round-trips exactly, so a warning would be noise"
                    }

                    test "a warning reaches a number nested in a list" {
                        let schema =
                            Schema.object "Ledger" {
                                let! amounts =
                                    Schema.required "amounts" (Schema.list Schema.decimal) (fun (x: decimal list) -> x)

                                return amounts
                            }

                        let block = blockFor "Ledger" (TypeScript.emitOne schema)
                        Expect.stringContains block "readonly amounts: readonly number[];" "the type"
                        Expect.stringContains block "exactness is not preserved" "and the warning, through the list"
                    }

                    test "a documented field keeps its prose alongside the warning" {
                        let schema =
                            Schema.object "Invoice" {
                                let! total =
                                    Schema.required "total" Schema.decimal (fun (x: decimal) -> x)
                                    |> Schema.doc "Amount due, in cents."

                                return total
                            }

                        let block = blockFor "Invoice" (TypeScript.emitOne schema)
                        Expect.stringContains block "Amount due, in cents." "the prose"
                        Expect.stringContains block "exactness is not preserved" "and the warning, in one comment"

                        let comments =
                            block.Split '\n' |> Array.filter (fun l -> l.Contains "/**") |> Array.length

                        Expect.equal comments 1 "not two comments stacked on one field"
                    }

                    test "a map becomes an index signature" {
                        let block = blockFor "Widths" (TypeScript.emitOne Widths.schema)
                        Expect.stringContains block "readonly counts: { readonly [key: string]: number };" "index"
                    }

                    test "every field is readonly" {
                        let generated = TypeScript.emitOne Person.schema

                        let fields =
                            generated.Split '\n'
                            |> Array.filter (fun line -> line.StartsWith "  " && line.TrimEnd().EndsWith ";")

                        Expect.isNonEmpty fields "there are fields to check"

                        for field in fields do
                            Expect.stringStarts
                                (field.Trim())
                                "readonly "
                                "a generated type mirrors an immutable F# record, so nothing is assignable"
                    }
                ]

            testList
                "the file is meant to be committed"
                [
                    test "line endings are LF on every platform" {
                        // The output is written to a .d.ts file and checked in. If
                        // it were CRLF on Windows and LF elsewhere the file would
                        // churn in every cross-platform repository, which is
                        // exactly the reviewable diff this is supposed to produce.
                        let generated = TypeScript.emitOne Person.schema
                        Expect.isFalse (generated.Contains "\r") "no carriage returns, whatever generated it"
                    }

                    test "it ends with exactly one newline" {
                        let generated = TypeScript.emitOne Person.schema
                        Expect.stringEnds generated "}\n" "one trailing newline"
                        Expect.isFalse (generated.EndsWith "\n\n") "and not two"
                    }

                    test "it says it is generated" {
                        let generated = TypeScript.emitOne Person.schema
                        Expect.stringStarts generated "// Generated by Etymon. Do not edit by hand." "a warning first"
                    }

                    test "the same schema always produces the same bytes" {
                        Expect.equal
                            (TypeScript.emitOne Person.schema)
                            (TypeScript.emitOne Person.schema)
                            "no dictionary ordering leaking into the output"
                    }

                    test "the order the schemas are handed over does not matter" {
                        let forwards = TypeScript.emit [ Person.schema.Info; Membership.schema.Info ]
                        let backwards = TypeScript.emit [ Membership.schema.Info; Person.schema.Info ]

                        Expect.equal
                            forwards
                            backwards
                            "types are emitted in name order, so a reordered call site is not a diff"
                    }
                ]
        ]
