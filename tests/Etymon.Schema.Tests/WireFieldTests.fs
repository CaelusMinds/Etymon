/// The crossing from a record that came off the wire.
///
/// This project compiles with F# nullness enabled, and that is the point of it.
/// WireField shipped in preview.5 with getters typed `'T -> string`, which
/// compiles fine against a record whose fields are not annotated and fails at
/// every call site in a consumer that has nullness on — which is the entire
/// audience for the module. The records below are declared the way
/// System.Text.Json leaves them, so the signatures are exercised as a consumer
/// would exercise them.
module Etymon.Schema.Tests.WireFieldTests

open System
open System.Collections.Generic
open Expecto
open Etymon

[<NoComparison>]
type BillLine =
    {
        Sku: string | null
        Quantity: Nullable<int>
    }

[<NoComparison>]
type CreateBill =
    {
        BillNumber: string | null
        Role: string | null
        VendorId: Nullable<Guid>
        Total: Nullable<decimal>
        BillDate: Nullable<DateOnly>
        Approved: Nullable<bool>
        Lines: BillLine[] | null
        Dimensions: IDictionary<string, string> | null
    }

let private lineSchema =
    Schema.object "BillLine" {
        let! sku = WireField.text "sku" (fun l -> l.Sku)
        and! quantity = WireField.int "quantity" (fun l -> l.Quantity)

        // No conversion: the bound values are already the record's own shapes.
        return { Sku = sku; Quantity = quantity }
    }

let private schema =
    Schema.object "CreateBill" {
        let! billNumber = WireField.text "billNumber" (fun b -> b.BillNumber)

        and! role =
            WireField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun b -> b.Role)

        and! vendorId = WireField.guid "vendorId" (fun b -> b.VendorId)
        and! total = WireField.decimal "total" (fun b -> b.Total)
        and! billDate = WireField.dateOnly "billDate" (fun b -> b.BillDate)
        and! approved = WireField.bool "approved" (fun b -> b.Approved)
        and! lines = WireField.array "lines" lineSchema (fun b -> b.Lines)

        and! dimensions =
            WireField.dictionary "dimensions" Schema.string (fun b -> b.Dimensions)

        return
            {
                BillNumber = billNumber
                Role = role
                VendorId = vendorId
                Total = total
                BillDate = billDate
                Approved = approved
                Lines = lines
                Dimensions = dimensions
            }
    }

/// Reads a collection the record declares nullable. WireField hands back a
/// non-null one, but the field is still declared `| null`, so a reader has to
/// say so -- which is the record's doing, not the library's.
let private present (value: 'a | null) =
    match value with
    | null -> failtest "expected a non-null collection"
    | v -> v

[<NoComparison>]
type Nested =
    {
        Line: BillLine | null
        Secret: string | null
    }

let private nestedSchema =
    Schema.object "Nested" {
        let! line = WireField.nullable "line" lineSchema (fun n -> n.Line)

        and! secret =
            WireField.nullable "secret" (Schema.string |> Schema.constrain (Check.nonEmpty)) (fun n -> n.Secret)

        return { Line = line; Secret = secret }
    }

[<NoComparison>]
type AlwaysThere =
    {
        Roles: string[]
        References: IDictionary<string, string>
    }

let private requiredSchema =
    Schema.object "AlwaysThere" {
        let! roles = WireField.requiredArray "roles" Schema.string (fun p -> p.Roles)

        and! references =
            WireField.requiredDictionary "references" Schema.string (fun p -> p.References)

        return
            {
                Roles = roles
                References = references
            }
    }

let private empty =
    {
        BillNumber = null
        Role = null
        VendorId = Nullable()
        Total = Nullable()
        BillDate = Nullable()
        Approved = Nullable()
        Lines = null
        Dimensions = null
    }

let tests =
    testList
        "WireField"
        [
            test "the record is rebuilt without a single conversion" {
                // The schema above is the assertion: it names its fields and
                // nothing else. If the crossing only went one way, every line of
                // that return would carry an Option.toObj.
                let filled =
                    { empty with
                        BillNumber = "KT-32"
                        Role = "admin"
                        VendorId = Nullable(Guid "00000000-0000-0000-0000-0000000000d1")
                        Total = Nullable 125.50m
                        BillDate = Nullable(DateOnly(2026, 3, 31))
                        Approved = Nullable true
                        Lines = [| { Sku = "A-1"; Quantity = Nullable 3 } |]
                        Dimensions = dict [ "department", "ops" ] |> Dictionary
                    }

                match Schema.fromJson schema (Schema.toJson schema filled) with
                | Ok decoded ->
                    Expect.equal decoded.BillNumber "KT-32" "text"
                    Expect.equal decoded.Role "admin" "constrained text"
                    Expect.equal decoded.VendorId filled.VendorId "guid"
                    Expect.equal decoded.Total filled.Total "decimal"
                    Expect.equal decoded.BillDate filled.BillDate "date"
                    Expect.equal decoded.Approved filled.Approved "bool"
                    let lines = present decoded.Lines
                    Expect.equal lines.Length 1 "the nested array"
                    Expect.equal lines[0].Quantity (Nullable 3) "with its own nullable field"
                    Expect.equal (present decoded.Dimensions).Count 1 "and the dictionary"
                | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "a record that is null throughout writes an object with nothing in it" {
                // The collections write as empty rather than vanishing, because
                // a caller reading "lines" should find a list.
                Expect.equal
                    (Schema.toJson schema empty)
                    """{"lines":[],"dimensions":{}}"""
                    "absent is absent, not null"
            }

            test "an absent key and an explicit null arrive the same way" {
                let fromAbsent = Schema.fromJson schema """{}"""

                let fromNull =
                    Schema.fromJson schema """{"billNumber":null,"lines":null,"dimensions":null}"""

                match fromAbsent, fromNull with
                | Ok a, Ok b ->
                    Expect.isNull a.BillNumber "absent is null in the record"
                    Expect.isNull b.BillNumber "and so is an explicit null"
                    Expect.equal (present a.Lines).Length 0 "a null array is no items"
                    Expect.equal (present b.Lines).Length 0 "and so is an absent one"
                    Expect.equal (present a.Dimensions).Count (present b.Dimensions).Count "same for the dictionary"
                | _ -> failtest "both should read"
            }

            test "collections come back non-null, so they need no guard at the call site" {
                // Keel carried a comment at three separate schemas explaining
                // why absent and empty must get the same answer. This is that
                // comment, once, as a test.
                match Schema.fromJson schema """{}""" with
                | Ok decoded ->
                    Expect.isNotNull decoded.Lines "never null"
                    Expect.isNotNull decoded.Dimensions "never null"
                | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "a constrained nullable field still enforces its rule" {
                Expect.isTrue (Validation.isOk (Schema.fromJson schema """{"role":"admin"}""")) "a permitted value"

                match Validation.errorList (Schema.fromJson schema """{"role":"emperor"}""") with
                | [ e ] -> Expect.stringContains (e.ToString()) "must be one of" "and one that is not"
                | other -> failtestf "expected one error, got %A" other
            }

            test "the rule the crossing encodes: nullable means optional" {
                // A field the record declares nullable is optional in the
                // Schema. Taking the record at its word is what made adoption
                // mechanical rather than a judgement on every field.
                match SchemaInfo.strip (Schema.info schema) with
                | SObject(_, fields) ->
                    let required =
                        fields |> List.filter (fun f -> f.Required) |> List.map (fun f -> f.Name)

                    Expect.isEmpty required "every nullable field became optional, none required"
                | other -> failtestf "expected an object, got %A" other
            }

            test "a nullable field of any Schema crosses without a conversion" {
                // The gap that sent nine call sites back to Option.ofObj: a
                // nested object, a sensitive string, a raw payload. None of them
                // is a type this module could have enumerated.
                match Schema.fromJson nestedSchema """{"line":{"sku":"A-1","quantity":3}}""" with
                | Ok decoded ->
                    match decoded.Line with
                    | null -> failtest "the nested object should have decoded"
                    | line -> Expect.equal line.Sku "A-1" "the nested object came back whole"
                | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "an absent nested object is null in the record, not an option" {
                match Schema.fromJson nestedSchema """{}""" with
                | Ok decoded -> Expect.isNull decoded.Line "absent is null, as the record declares it"
                | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "a constrained schema composes through nullable" {
                // Schema.sensitive and Schema.constrain do not compose with
                // WireField.text, which is what drove these fields back to the
                // manual form -- and they are the fields where going through a
                // Schema matters most.
                Expect.isTrue
                    (Validation.isOk (Schema.fromJson nestedSchema """{"secret":"hunter2"}"""))
                    "a value that satisfies it"

                match Validation.errorList (Schema.fromJson nestedSchema """{"secret":""}""") with
                | [ _ ] -> ()
                | other -> failtestf "expected the constraint to bite, got %A" other
            }

            test "a required collection is described as required, not optional" {
                // The bug this pair exists to make unreachable. WireField.array
                // compiles against a non-nullable field and hands back a
                // non-null array, so nothing complains -- and the field reaches
                // the OpenAPI document and the compatibility snapshot as
                // optional, which is what makes removing it later look safe.
                match SchemaInfo.strip (Schema.info requiredSchema) with
                | SObject(_, fields) ->
                    let required =
                        fields |> List.filter (fun f -> f.Required) |> List.map (fun f -> f.Name)

                    Expect.equal (List.sort required) [ "references"; "roles" ] "both say what they are"
                | other -> failtestf "expected an object, got %A" other
            }

            test "a required collection still round-trips into the record's own types" {
                let value =
                    {
                        Roles = [| "admin"; "member" |]
                        References = dict [ "ledger", "L-1" ] |> Dictionary
                    }

                match Schema.fromJson requiredSchema (Schema.toJson requiredSchema value) with
                | Ok decoded ->
                    Expect.equal decoded.Roles value.Roles "an array, not a list"
                    Expect.equal decoded.References.Count 1 "a dictionary, not a map"
                | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "a required collection that is absent is an error, not an empty one" {
                match Validation.errorList (Schema.fromJson requiredSchema """{}""") with
                | [] -> failtest "a required collection should be missed when it is absent"
                | errors -> Expect.equal errors.Length 2 "one for each"
            }
        ]
