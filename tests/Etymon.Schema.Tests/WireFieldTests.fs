/// The crossing from a record that came off the wire.
///
/// Written because every consumer adopting Schema over records previously bound
/// by System.Text.Json writes this module themselves. These pin that the
/// crossing does what Option.ofObj at each field would have done, and that null
/// and absent are treated alike, because that is what a caller means by either.
module Etymon.Schema.Tests.WireFieldTests

open System
open Expecto
open Etymon

[<NoComparison>]
type BillLine =
    { Sku: string; Quantity: Nullable<int> }

[<NoComparison>]
type CreateBill =
    {
        BillNumber: string
        VendorId: Nullable<Guid>
        Total: Nullable<decimal>
        BillDate: Nullable<DateOnly>
        Approved: Nullable<bool>
        Lines: BillLine[]
    }

let private lineSchema =
    Schema.object "BillLine" {
        let! sku = WireField.text "sku" (fun l -> l.Sku)
        and! quantity = WireField.int "quantity" (fun l -> l.Quantity)

        return
            {
                Sku = Option.toObj sku
                Quantity = Option.toNullable quantity
            }
    }

let private schema =
    Schema.object "CreateBill" {
        let! billNumber = WireField.text "billNumber" (fun b -> b.BillNumber)
        and! vendorId = WireField.guid "vendorId" (fun b -> b.VendorId)
        and! total = WireField.decimal "total" (fun b -> b.Total)
        and! billDate = WireField.dateOnly "billDate" (fun b -> b.BillDate)
        and! approved = WireField.bool "approved" (fun b -> b.Approved)
        and! lines = WireField.array "lines" lineSchema (fun b -> b.Lines)

        return
            {
                BillNumber = Option.toObj billNumber
                VendorId = Option.toNullable vendorId
                Total = Option.toNullable total
                BillDate = Option.toNullable billDate
                Approved = Option.toNullable approved
                Lines = lines |> List.toArray
            }
    }

let tests =
    testList
        "WireField"
        [
            test "a record that is null throughout encodes as an object with nothing in it" {
                let empty =
                    {
                        BillNumber = null
                        VendorId = Nullable()
                        Total = Nullable()
                        BillDate = Nullable()
                        Approved = Nullable()
                        Lines = null
                    }

                // A null array writes as an empty one rather than vanishing,
                // because a caller reading "lines" should find a list.
                Expect.equal (Schema.toJson schema empty) """{"lines":[]}""" "absent is absent, not null"
            }

            test "values round-trip through the crossing" {
                let filled =
                    {
                        BillNumber = "KT-32"
                        VendorId = Nullable(Guid "00000000-0000-0000-0000-0000000000d1")
                        Total = Nullable 125.50m
                        BillDate = Nullable(DateOnly(2026, 3, 31))
                        Approved = Nullable true
                        Lines = [| { Sku = "A-1"; Quantity = Nullable 3 } |]
                    }

                match Schema.fromJson schema (Schema.toJson schema filled) with
                | Ok decoded ->
                    Expect.equal decoded.BillNumber "KT-32" "text"
                    Expect.equal decoded.VendorId filled.VendorId "guid"
                    Expect.equal decoded.Total filled.Total "decimal"
                    Expect.equal decoded.BillDate filled.BillDate "date"
                    Expect.equal decoded.Approved filled.Approved "bool"
                    Expect.equal decoded.Lines.Length 1 "and the nested array"
                    Expect.equal decoded.Lines[0].Quantity (Nullable 3) "with its own nullable field"
                | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "an absent key and an explicit null both arrive as nothing" {
                let fromAbsent = Schema.fromJson schema """{}"""
                let fromNull = Schema.fromJson schema """{"billNumber":null,"lines":null}"""

                match fromAbsent, fromNull with
                | Ok a, Ok b ->
                    Expect.isNull a.BillNumber "absent is null in the record"
                    Expect.isNull b.BillNumber "and so is an explicit null"
                    Expect.equal a.Lines b.Lines "and both give the same array"
                | _ -> failtest "both should read"
            }

            test "a constrained nullable field still enforces its rule" {
                // The crossing must not quietly drop the constraint, which is
                // the whole reason for going through a Schema at all.
                let roleSchema =
                    Schema.object "Member" {
                        let! role =
                            WireField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun (r: string) -> r)

                        return Option.toObj role
                    }

                Expect.isTrue (Validation.isOk (Schema.fromJson roleSchema """{"role":"admin"}""")) "a permitted value"

                match Validation.errorList (Schema.fromJson roleSchema """{"role":"emperor"}""") with
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
        ]
