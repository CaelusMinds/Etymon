# Etymon.Invariants

**Rules that must always be true of a type, declared once and exposed as data.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core` and nothing else.

Single-field rules already have a home: `Constraint` in `Etymon.Core`, attached
to the field it restricts. This package is for the rules that cannot live on any
one field.

```fsharp
let invariants =
    Invariants.forType "Booking" [
        Invariant.after ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)

        Invariant.atMostOne "one-payment-method" [
            "cardId", fun b -> b.CardId.IsSome
            "invoiceId", fun b -> b.InvoiceId.IsSome
        ]

        Invariant.create "party-fits" "a booking of more than 4 guests must last at least two nights"
            (fun b -> b.Guests <= 4 || b.Nights >= 2)
        |> Invariant.about [ "guests"; "endDate" ]
    ]
```

## Every rule runs

```fsharp
Invariants.check invariants booking
```
```
endDate: endDate must be after startDate
cardId: at most one of cardId, invoiceId may be given
guests: a booking of more than 4 guests must last at least two nights
```

Three broken rules, three errors, in declaration order. An error lands on the
**first field the rule concerns**, so it appears next to the input a person would
look at, and every field it concerns is carried as data so a form can highlight
all of them.

`Name` is a stable code (`endDate-after-startDate`) that a caller can branch on;
`Description` is the prose. Nobody should have to parse a message.

## Composing with a schema

```fsharp
Schema.fromJson bookingSchema payload
|> Invariants.andThen bookingInvariants
```

Field-level errors and cross-field errors arrive together rather than in two
rounds. One caveat, stated because it is the only place in Etymon where an error
hides another: if a **field** fails to decode, the cross-field rules cannot run,
because there is no value to check. That is unavoidable rather than a design
choice.

## What this package cannot do

Your original instinct may have been "the constructor enforces the invariants."
**F# has no mechanism for that** short of a compiler plugin or IL weaving, and a
library claiming otherwise would be lying to you.

What it can do is make the call one line, and make the rules visible:

```fsharp
type Booking = private Booking of BookingFields

module Booking =
    let create fields =
        Invariants.enforce invariants fields |> Validation.map Booking
```

`enforce` is `check` under a different name, named for the place it belongs so
that its absence from a constructor reads as an omission rather than as nothing
at all. And because the rules are data, a type whose constructor does not enforce
its own invariants is something a reviewer — or a test — can notice.

## Why the rules are data

`Invariants.rules` hands back the list. That is what lets other packages read
them: `Etymon.Invariants.FsCheck` generates values that satisfy them,
`Etymon.Schema.Sql` can turn the expressible ones into database checks, and
documentation can list them without restating anything.

A rule written inline inside a constructor is a rule nothing else can see.

```fsharp
Invariants.describe bookingInvariants
// [ "endDate must be after startDate"
//   "at most one of cardId, invoiceId may be given"
//   "a booking of more than 4 guests must last at least two nights" ]
```

## Built-in shapes

`after` and `notBefore` (the one everybody writes first and gets subtly wrong by
allowing equality), `compare`, `allOrNone`, `atMostOne`, `exactlyOne`, and
`whenever` for a rule that only applies in some circumstances. `create` for
anything else.

## Public surface

The complete public surface of this package, every value with its full signature
and its documentation, is in `Surface.fsi` beside this file. It is written by the
build from the implementation, so it is where to learn what a function hands
back without compiling anything.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
