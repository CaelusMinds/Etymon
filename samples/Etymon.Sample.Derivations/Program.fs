/// One schema definition, six derivations, no restatement.
///
/// This is the whole claim of the suite in a file you can run:
///
///     dotnet run --project samples/Etymon.Sample.Derivations
///
/// Nothing below writes a JSON codec, a validation rule, an OpenAPI schema, a
/// CREATE TABLE, a TypeScript interface or a configuration reader. Every one of
/// them is read out of `Booking.schema`.
module Etymon.Sample.Derivations.Program

open System
open Etymon

// ---- 1. the one declaration -------------------------------------------------

type Booking =
    {
        Id: Guid
        Guest: string
        Email: string
        Room: string
        Nights: int
        Arrives: DateOnly
        Departs: DateOnly
        Notes: string option
    }

module Booking =
    /// The rules live here, once. Every section after this one reads them.
    let schema =
        Schema.object "Booking" {
            let! id = Schema.required "id" Schema.guid (fun b -> b.Id)

            and! guest =
                Schema.required
                    "guest"
                    (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 80)))
                    (fun b -> b.Guest)
                |> Schema.doc "Name the booking is held under"

            and! email =
                Schema.required
                    "email"
                    (Schema.string |> Schema.constrain (Check.format Format.Email))
                    (fun b -> b.Email)

            and! room =
                Schema.required
                    "room"
                    (Schema.string |> Schema.constrain (Check.oneOf [ "single"; "double"; "suite" ]))
                    (fun b -> b.Room)

            and! nights =
                Schema.required
                    "nights"
                    (Schema.int |> Schema.constrain (Check.intRange (Some 1) (Some 30)))
                    (fun b -> b.Nights)

            and! arrives = Schema.required "arrives" Schema.dateOnly (fun b -> b.Arrives)
            and! departs = Schema.required "departs" Schema.dateOnly (fun b -> b.Departs)
            and! notes = Schema.optional "notes" Schema.string (fun b -> b.Notes)

            return
                {
                    Id = id
                    Guest = guest
                    Email = email
                    Room = room
                    Nights = nights
                    Arrives = arrives
                    Departs = departs
                    Notes = notes
                }
        }

    /// A rule no single field can hold, so it cannot live in the schema. It is
    /// declared once here and checked alongside it.
    let invariants =
        Invariants.forType
            "Booking"
            [
                Invariant.after ("departs", fun b -> b.Departs) ("arrives", fun b -> b.Arrives)

                Invariant.create
                    "nights-match-dates"
                    "nights must equal the number of nights between the dates"
                    (fun b -> b.Nights = (b.Departs.DayNumber - b.Arrives.DayNumber))
                |> Invariant.about [ "nights"; "arrives"; "departs" ]
            ]

let private heading (text: string) =
    printfn ""
    printfn "== %s %s" text (String('=', max 0 (72 - text.Length)))
    printfn ""

[<EntryPoint>]
let main _ =

    // ---- 2. decoding, with every problem reported at once -------------------

    heading "Decoding rejects bad input and says what was wrong, all of it"

    let bad =
        """{"id":"not-a-guid","guest":"","email":"nope","room":"penthouse",
            "nights":0,"arrives":"2026-03-01","departs":"2026-03-04"}"""

    match Schema.fromJson Booking.schema bad with
    | Ok _ -> printfn "unexpectedly accepted"
    | Error errors ->
        // Five problems, five errors. Not the first one and a shrug.
        printfn "%s" (ValidationErrors.format errors)

    // ---- 3. encoding, and the cross-field rule ------------------------------

    heading "A valid booking round-trips, and the invariant is checked beside it"

    let good =
        """{"id":"8e1f4b2a-0000-4000-8000-000000000001","guest":"Ada Lovelace",
            "email":"ada@example.com","room":"suite","nights":3,
            "arrives":"2026-03-01","departs":"2026-03-04"}"""

    match Schema.fromJson Booking.schema good |> Invariants.andThen Booking.invariants with
    | Error errors -> printfn "%s" (ValidationErrors.format errors)
    | Ok booking ->
        printfn "decoded : %s, %d nights in a %s" booking.Guest booking.Nights booking.Room
        printfn "re-encoded : %s" (Schema.toJson Booking.schema booking)

        // The same value with the dates left alone and the count changed. The
        // schema is perfectly happy with it; no single field is wrong.
        let inconsistent = { booking with Nights = 9 }

        match Invariants.check Booking.invariants inconsistent with
        | Ok _ -> printfn "unexpectedly accepted"
        | Error errors -> printfn "changed nights to 9 -> %s" (ValidationErrors.format errors)

    // ---- 4. OpenAPI ---------------------------------------------------------

    heading "The same rules as an OpenAPI 3.1 schema"

    printfn "%s" (OpenApi.toJsonSchemaText Booking.schema)

    // ---- 5. TypeScript ------------------------------------------------------

    heading "The same rules as TypeScript declarations"

    printfn "%s" (TypeScript.emitOne Booking.schema)

    // ---- 6. SQL -------------------------------------------------------------

    heading "The same rules as a table, with the constraints carried over"

    let options =
        { Mapping.keyedBy "id" with
            TableName = Some "booking"
        }

    match Mapping.tableOf options Booking.schema with
    | Error problems -> printfn "%s" (Mapping.report problems)
    | Ok table ->
        // A migration from nothing to this table. In practice the "before" side
        // is a snapshot you committed last time, not the empty one.
        let migration =
            Migrations.between Dialect.postgres SqlModel.emptySnapshot (SqlModel.snapshotOf [ table ])

        printfn "%s" migration.Up

        // Anything the dialect cannot express is listed rather than silently
        // dropped, which is the difference between a migration you can trust
        // and one you have to read twice.
        for change, why in migration.Unsupported do
            printfn "-- %s: %s" (Diff.describe change) why

    // ---- 7. configuration ---------------------------------------------------

    heading "The same machinery reading configuration, reporting every problem"

    // A separate, smaller schema: configuration is a different shape from a
    // booking, but it is described the same way and read by the same code.
    let sources =
        [
            Source.jsonText "appsettings.json" """{"room":"double","nights":"2"}"""
            Source.inMemory "environment" [ "nights", "45" ]
        ]

    let configSchema =
        Schema.object "Defaults" {
            let! room =
                Schema.required
                    "room"
                    (Schema.string |> Schema.constrain (Check.oneOf [ "single"; "double"; "suite" ]))
                    fst

            and! nights =
                Schema.required "nights" (Schema.int |> Schema.constrain (Check.intRange (Some 1) (Some 30))) snd

            return (room, nights)
        }

    // Note the string "2" and "45": configuration arrives as text whatever it
    // means, so Config coerces where the schema says what the value should be.
    match Config.load configSchema sources with
    | Ok(room, nights) -> printfn "loaded: %s for %d nights" room nights
    | Error errors ->
        // 45 is out of range, and the report says which source supplied it.
        printfn "%s" (Config.report errors)

    printfn ""
    printfn "Explaining where each setting would come from:"
    printfn "%s" (Config.explain configSchema sources)

    0
