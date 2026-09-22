/// The other direction of the crossing: what a request body accepts.
///
/// WireField describes what is written -- present and null, required with a
/// nullable type. A request body is read, not written, and read leniently, so
/// the honest description is optional and nullable. These pin that the two
/// modules describe the same DTO shape the same way but for Required -- and,
/// for a collection, whether null may arrive -- and agree on everything else.
module Etymon.Schema.Tests.RequestFieldTests

open System
open System.Collections.Generic
open Expecto
open Etymon

[<NoComparison>]
type CreateBill =
    {
        BillNumber: string | null
        Role: string | null
        Total: Nullable<decimal>
        Lines: string[] | null
        Dimensions: IDictionary<string, string> | null
    }

let private request =
    Schema.object "CreateBill" {
        let! billNumber = RequestField.text "billNumber" (fun b -> b.BillNumber)

        and! role =
            RequestField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun b -> b.Role)

        and! total = RequestField.decimal "total" (fun b -> b.Total)
        and! lines = RequestField.array "lines" Schema.string (fun b -> b.Lines)

        and! dimensions =
            RequestField.dictionary "dimensions" Schema.string (fun b -> b.Dimensions)

        return
            {
                BillNumber = billNumber
                Role = role
                Total = total
                Lines = lines
                Dimensions = dimensions
            }
    }

// The same shape, going out.
let private response =
    Schema.object "CreateBill" {
        let! billNumber = WireField.text "billNumber" (fun b -> b.BillNumber)

        and! role =
            WireField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun b -> b.Role)

        and! total = WireField.decimal "total" (fun b -> b.Total)
        and! lines = WireField.array "lines" Schema.string (fun b -> b.Lines)

        and! dimensions =
            WireField.dictionary "dimensions" Schema.string (fun b -> b.Dimensions)

        return
            {
                BillNumber = billNumber
                Role = role
                Total = total
                Lines = lines
                Dimensions = dimensions
            }
    }

let private empty =
    {
        BillNumber = null
        Role = null
        Total = Nullable()
        Lines = null
        Dimensions = null
    }

let private fieldsOf (schema: Schema<'T>) =
    match SchemaInfo.strip (Schema.info schema) with
    | SObject(_, fields) -> fields
    | other -> failtestf "expected an object, got %A" other

let private isNullable (f: FieldInfo) =
    match SchemaInfo.strip f.Schema with
    | SNullable _ -> true
    | _ -> false

/// Reads a collection the record declares nullable; the field's declaration,
/// not the library's doing.
let private present (value: 'a | null) =
    match value with
    | null -> failtest "expected a non-null collection"
    | v -> v

let tests =
    testList
        "RequestField"
        [
            test "every field is optional and nullable, in every derivation's source" {
                let fields = fieldsOf request
                Expect.isEmpty (fields |> List.filter (fun f -> f.Required)) "none required"
                Expect.isEmpty (fields |> List.filter (fun f -> not (isNullable f))) "all admit null"
            }

            test "absent and null both arrive as null, and collections as empty" {
                for body in
                    [
                        """{}"""
                        """{"billNumber":null,"role":null,"total":null,"lines":null,"dimensions":null}"""
                    ] do
                    match Schema.fromJson request body with
                    | Ok decoded ->
                        Expect.isNull decoded.BillNumber "text"
                        Expect.isFalse decoded.Total.HasValue "value type"
                        Expect.equal (present decoded.Lines).Length 0 "array, never null"
                        Expect.equal (present decoded.Dimensions).Count 0 "dictionary, never null"
                    | Error errors -> failtestf "did not decode: %s" (ValidationErrors.format errors)
            }

            test "a record that is null throughout writes every key, collections as empty" {
                // What the client package sends for such a record: the key with
                // null, which the description admits; a list where a list is read.
                Expect.equal
                    (Schema.toJson request empty)
                    """{"billNumber":null,"role":null,"total":null,"lines":[],"dimensions":{}}"""
                    "within the contract it describes"
            }

            test "the document says absent means null" {
                let field name =
                    fieldsOf request |> List.find (fun f -> f.Name = name)

                match (field "billNumber").Default with
                | Some d -> Expect.isTrue (isNull d) "a null default, which renders as \"default\": null"
                | None -> failtest "a default should be recorded"

                match (field "lines").Default with
                | Some d -> Expect.equal (d.ToJsonString()) "[]" "and a collection defaults to empty"
                | None -> failtest "a default should be recorded"
            }

            test "the two directions differ in Required, and a collection coming in admits null" {
                // Same DTO, described going in and going out. Required flips
                // on every field. A scalar has the same nullable type both
                // ways. A collection going out is never written as null, so
                // it is described as a plain array; coming in it may arrive
                // as null, so it is described as admitting it.
                let incoming = fieldsOf request
                let outgoing = fieldsOf response

                Expect.equal
                    (incoming |> List.map (fun f -> f.Name))
                    (outgoing |> List.map (fun f -> f.Name))
                    "same fields"

                for (i, o) in List.zip incoming outgoing do
                    Expect.isFalse i.Required $"'{i.Name}' is optional coming in"
                    Expect.isTrue o.Required $"'{o.Name}' is required going out"

                    match SchemaInfo.strip i.Schema, SchemaInfo.strip o.Schema with
                    | SNullable(SList _), SList _
                    | SNullable(SMap _), SMap _ -> ()
                    | incoming, outgoing -> Expect.equal incoming outgoing $"'{i.Name}' has the same type both ways"
            }

            test "a constrained field still enforces its rule" {
                Expect.isTrue (Validation.isOk (Schema.fromJson request """{"role":"admin"}""")) "a permitted value"

                match Validation.errorList (Schema.fromJson request """{"role":"emperor"}""") with
                | [ e ] -> Expect.stringContains (e.ToString()) "must be one of" "and one that is not"
                | other -> failtestf "expected one error, got %A" other
            }

            test "a required collection is required in both directions" {
                let schema =
                    Schema.object "Grant" {
                        let! roles =
                            RequestField.requiredArray "roles" Schema.string (fun (r: string[]) -> r)

                        return roles
                    }

                let field = fieldsOf schema |> List.exactlyOne
                Expect.isTrue field.Required "described required"
                Expect.isFalse (isNullable field) "and not nullable"

                Expect.isTrue
                    (Validation.isError (Schema.fromJson schema """{}"""))
                    "absent is refused, not read as empty"
            }
        ]
