/// The shapes the benchmarks measure, and the hand-written codec they are
/// measured against.
///
/// The baseline matters more than the absolute numbers. "Etymon decodes a
/// record in 900ns" says nothing on its own; "Etymon decodes it in 900ns where
/// a hand-written `Utf8JsonReader` loop takes 400ns and `JsonSerializer` takes
/// 1100ns" says what it costs to stop hand-writing codecs, which is the
/// decision a reader is actually making.
module Etymon.Benchmarks.Domain

open System
open System.Text.Json
open Etymon

// ---------------------------------------------------------------------------
// A flat record: the common case, and the one where per-field overhead shows.
// ---------------------------------------------------------------------------

type Customer =
    {
        Id: Guid
        Name: string
        Email: string
        Country: string
        Age: int
        Balance: int64
        Active: bool
        JoinedOn: DateOnly
    }

let customerSchema =
    Schema.object "Customer" {
        let! id = Schema.required "id" Schema.guid (fun c -> c.Id)

        and! name =
            Schema.required
                "name"
                (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 80)))
                (fun c -> c.Name)

        and! email =
            Schema.required "email" (Schema.string |> Schema.constrain (Check.format Format.Email)) (fun c -> c.Email)

        and! country =
            Schema.required "country" (Schema.string |> Schema.constrain (Check.exactLength 2)) (fun c -> c.Country)

        and! age =
            Schema.required "age" (Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))) (fun c -> c.Age)

        and! balance = Schema.required "balance" Schema.int64 (fun c -> c.Balance)
        and! active = Schema.required "active" Schema.bool (fun c -> c.Active)
        and! joinedOn = Schema.required "joinedOn" Schema.dateOnly (fun c -> c.JoinedOn)

        return
            {
                Id = id
                Name = name
                Email = email
                Country = country
                Age = age
                Balance = balance
                Active = active
                JoinedOn = joinedOn
            }
    }

let sampleCustomer =
    {
        Id = Guid "8e1f4b2a-0000-4000-8000-000000000001"
        Name = "Ada Lovelace"
        Email = "ada@example.com"
        Country = "GB"
        Age = 36
        Balance = 125_00L
        Active = true
        JoinedOn = DateOnly(2026, 3, 1)
    }

let customerJson = Schema.toJson customerSchema sampleCustomer

/// The same payload with four fields wrong, so the error path is measured on
/// something that accumulates rather than on a single failure.
let invalidCustomerJson =
    """{"id":"not-a-guid","name":"","email":"nope","country":"GBR","age":900,"balance":1,"active":true,"joinedOn":"2026-03-01"}"""

// ---------------------------------------------------------------------------
// A nested, repeated shape: where path building and list decoding show up.
// ---------------------------------------------------------------------------

type OrderLine =
    {
        Sku: string
        Quantity: int
        UnitPriceMinor: int64
    }

type Order =
    {
        Reference: string
        Placed: DateTimeOffset
        Lines: OrderLine list
    }

let orderLineSchema =
    Schema.object "OrderLine" {
        let! sku =
            Schema.required "sku" (Schema.string |> Schema.constrain Check.nonEmpty) (fun l -> l.Sku)

        and! quantity =
            Schema.required
                "quantity"
                (Schema.int |> Schema.constrain (Check.intRange (Some 1) None))
                (fun l -> l.Quantity)

        and! price =
            Schema.required "unitPriceMinor" Schema.int64 (fun l -> l.UnitPriceMinor)

        return
            {
                Sku = sku
                Quantity = quantity
                UnitPriceMinor = price
            }
    }

let orderSchema =
    Schema.object "Order" {
        let! reference = Schema.required "reference" Schema.string (fun o -> o.Reference)
        and! placed = Schema.required "placed" Schema.dateTimeOffset (fun o -> o.Placed)
        and! lines = Schema.required "lines" (Schema.list orderLineSchema) (fun o -> o.Lines)

        return
            {
                Reference = reference
                Placed = placed
                Lines = lines
            }
    }

let sampleOrder =
    {
        Reference = "SO-1001"
        Placed = DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero)
        Lines =
            [
                for i in 1..20 ->
                    {
                        Sku = $"SKU-%04d{i}"
                        Quantity = i
                        UnitPriceMinor = int64 i * 199L
                    }
            ]
    }

let orderJson = Schema.toJson orderSchema sampleOrder

/// Ten thousand elements of the wrong type, as one array.
let manyBadElements =
    "[" + System.String.Join(",", Array.create 10_000 "\"x\"") + "]"

// ---------------------------------------------------------------------------
// The baselines.
// ---------------------------------------------------------------------------

/// System.Text.Json over the same record, reflecting over the type. It performs
/// no validation at all, so it is a floor rather than a fair comparison: the
/// gap is what the rules cost.
///
/// Reflection rather than the source generator, because the generator does not
/// run for F#. That makes this the slower of the two System.Text.Json paths and
/// the comparison correspondingly kinder to Etymon, which is worth saying.
let systemTextJsonOptions =
    JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

/// A codec written the way somebody writes one when they are not using a
/// library: straight at the reader, no abstraction, no validation. The fastest
/// thing anyone would realistically hand-write, and therefore the honest
/// ceiling.
let decodeByHand (json: string) : Customer =
    use document = JsonDocument.Parse json
    let root = document.RootElement

    {
        Id = root.GetProperty("id").GetGuid()
        Name = root.GetProperty("name").GetString()
        Email = root.GetProperty("email").GetString()
        Country = root.GetProperty("country").GetString()
        Age = root.GetProperty("age").GetInt32()
        Balance = root.GetProperty("balance").GetInt64()
        Active = root.GetProperty("active").GetBoolean()
        JoinedOn = DateOnly.Parse(root.GetProperty("joinedOn").GetString())
    }
