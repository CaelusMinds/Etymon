/// A domain with genuine cross-field rules, so the tests exercise the case the
/// package exists for rather than a rule that could have lived on one field.
module Etymon.Invariants.Tests.Domain

open System
open Etymon

type Booking =
    {
        Reference: string
        StartDate: DateOnly
        EndDate: DateOnly
        Guests: int
        CardId: string option
        InvoiceId: string option
    }

module Booking =
    let schema =
        Schema.object "Booking" {
            let! reference =
                Schema.required
                    "reference"
                    (Schema.string |> Schema.constrain (Check.length (Some 6) (Some 6)))
                    (fun b -> b.Reference)

            and! startDate = Schema.required "startDate" Schema.dateOnly (fun b -> b.StartDate)
            and! endDate = Schema.required "endDate" Schema.dateOnly (fun b -> b.EndDate)

            and! guests =
                Schema.required
                    "guests"
                    (Schema.int |> Schema.constrain (Check.intRange (Some 1) (Some 8)))
                    (fun b -> b.Guests)

            and! cardId = Schema.optional "cardId" Schema.string (fun b -> b.CardId)
            and! invoiceId = Schema.optional "invoiceId" Schema.string (fun b -> b.InvoiceId)

            return
                {
                    Reference = reference
                    StartDate = startDate
                    EndDate = endDate
                    Guests = guests
                    CardId = cardId
                    InvoiceId = invoiceId
                }
        }

    let invariants =
        Invariants.forType
            "Booking"
            [
                Invariant.after ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)

                Invariant.atMostOne
                    "one-payment-method"
                    [
                        "cardId", (fun b -> b.CardId.IsSome)
                        "invoiceId", (fun b -> b.InvoiceId.IsSome)
                    ]

                Invariant.create
                    "party-fits"
                    "a booking of more than 4 guests must last at least two nights"
                    (fun b -> b.Guests <= 4 || b.EndDate.DayNumber - b.StartDate.DayNumber >= 2)
                |> Invariant.about [ "guests"; "endDate" ]
            ]

    /// The shape a smart constructor takes, with the invariants enforced.
    let create (fields: Booking) = Invariants.enforce invariants fields

let valid =
    {
        Reference = "ABC123"
        StartDate = DateOnly(2026, 9, 20)
        EndDate = DateOnly(2026, 9, 25)
        Guests = 2
        CardId = Some "card_1"
        InvoiceId = None
    }

/// A schema with no cross-field rules, for the generator tests that should not
/// have to contend with filtering.
type Contact =
    {
        Email: string
        Nickname: string option
        Tags: string list
    }

module Contact =
    let schema =
        Schema.object "Contact" {
            let! email =
                Schema.required
                    "email"
                    (Schema.string |> Schema.constrain (Check.format Format.Email))
                    (fun c -> c.Email)

            and! nickname = Schema.optional "nickname" Schema.string (fun c -> c.Nickname)

            and! tags =
                Schema.required
                    "tags"
                    (Schema.list Schema.string
                     |> Schema.constrain (Check.itemCount (Some 0) (Some 3)))
                    (fun c -> c.Tags)

            return
                {
                    Email = email
                    Nickname = nickname
                    Tags = tags
                }
        }

/// A recursive type, for the case a generator cannot handle.
type Tree = { Value: int; Children: Tree list }

module Tree =
    let schema =
        Schema.recursive
            "Tree"
            (fun self ->
                Schema.object "Tree" {
                    let! value = Schema.required "value" Schema.int (fun t -> t.Value)
                    and! children = Schema.required "children" (Schema.list self) (fun t -> t.Children)
                    return { Value = value; Children = children }
                }
            )
