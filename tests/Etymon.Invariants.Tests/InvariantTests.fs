module Etymon.Invariants.Tests.InvariantTests

open System
open Expecto
open Etymon
open Etymon.Invariants.Tests.Domain

let private messagesOf (v: Validation<'T>) =
    v |> Validation.errorList |> List.map (fun e -> e.ToString())

let tests =
    testList
        "Invariants"
        [
            testList
                "a single rule"
                [
                    test "create makes a rule about the whole value" {
                        let rule =
                            Invariant.create "non-empty" "must not be empty" (fun (s: string) -> s <> "")

                        Expect.isTrue (rule.Holds "a") "holds"
                        Expect.isFalse (rule.Holds "") "does not hold"
                        Expect.equal rule.Fields [] "and concerns no particular field"
                    }

                    test "about records which fields a rule concerns" {
                        let rule = Invariant.create "x" "y" (fun _ -> true) |> Invariant.about [ "a"; "b" ]

                        Expect.equal rule.Fields [ "a"; "b" ] "so a caller can highlight all of them"
                    }

                    test "after is strict about equality" {
                        // The rule everyone writes first and gets subtly wrong.
                        let rule = Invariant.after ("end", fst) ("start", snd)
                        Expect.isTrue (rule.Holds(2, 1)) "after holds"
                        Expect.isFalse (rule.Holds(1, 1)) "equal is not after"
                        Expect.isFalse (rule.Holds(0, 1)) "before does not hold"
                    }

                    test "notBefore allows equality" {
                        let rule = Invariant.notBefore ("end", fst) ("start", snd)
                        Expect.isTrue (rule.Holds(2, 1)) "after holds"
                        Expect.isTrue (rule.Holds(1, 1)) "equal holds"
                        Expect.isFalse (rule.Holds(0, 1)) "before does not"
                    }

                    test "after names and describes itself from the fields" {
                        let rule = Invariant.after ("endDate", fst) ("startDate", snd)
                        Expect.equal rule.Name "endDate-after-startDate" "a stable code"
                        Expect.equal rule.Description "endDate must be after startDate" "and readable prose"
                        Expect.equal rule.Fields [ "endDate"; "startDate" ] "both fields"
                    }

                    test "compare carries both field names" {
                        let rule =
                            Invariant.compare
                                "within"
                                "discount must not exceed total"
                                ("discount", fst)
                                ("total", snd)
                                (<=)

                        Expect.isTrue (rule.Holds(5, 10)) "within"
                        Expect.isFalse (rule.Holds(15, 10)) "beyond"
                        Expect.equal rule.Fields [ "discount"; "total" ] "both"
                    }

                    test "allOrNone" {
                        let rule = Invariant.allOrNone "address" [ "street", fst; "zip", snd ]

                        Expect.isTrue (rule.Holds(true, true)) "both present"
                        Expect.isTrue (rule.Holds(false, false)) "neither present"
                        Expect.isFalse (rule.Holds(true, false)) "one of two is the mistake"
                    }

                    test "atMostOne" {
                        let rule = Invariant.atMostOne "payment" [ "card", fst; "invoice", snd ]
                        Expect.isTrue (rule.Holds(false, false)) "neither"
                        Expect.isTrue (rule.Holds(true, false)) "one"
                        Expect.isFalse (rule.Holds(true, true)) "both"
                    }

                    test "exactlyOne" {
                        let rule = Invariant.exactlyOne "payment" [ "card", fst; "invoice", snd ]
                        Expect.isFalse (rule.Holds(false, false)) "neither is not one"
                        Expect.isTrue (rule.Holds(true, false)) "one"
                        Expect.isFalse (rule.Holds(true, true)) "both is not one"
                    }

                    test "whenever only applies when it applies" {
                        let rule =
                            Invariant.whenever "ships-needs-address" "an order that ships needs an address" fst snd

                        Expect.isTrue (rule.Holds(false, false)) "does not apply, so it holds"
                        Expect.isTrue (rule.Holds(true, true)) "applies and holds"
                        Expect.isFalse (rule.Holds(true, false)) "applies and does not hold"
                    }
                ]

            testList
                "errors"
                [
                    test "an error lands on the first field the rule concerns" {
                        let rule = Invariant.after ("endDate", fst) ("startDate", snd)
                        let error = Invariant.toError Path.root rule

                        Expect.equal (Path.toString error.Path) "endDate" "the field a user would look at"
                        Expect.equal error.Reason (ErrorReason.Rejected "endDate-after-startDate") "code, not prose"
                        Expect.equal error.Message "endDate must be after startDate" "and prose, separately"
                    }

                    test "a rule concerning no field lands on the value itself" {
                        let rule = Invariant.create "x" "must be so" (fun _ -> false)
                        Expect.equal (Path.toString (Invariant.toError Path.root rule).Path) "" "at the root"
                    }

                    test "errors nest under an enclosing path" {
                        let rule = Invariant.after ("endDate", fst) ("startDate", snd)
                        let path = Path.root |> Path.field "booking"

                        Expect.equal
                            (Path.toString (Invariant.toError path rule).Path)
                            "booking.endDate"
                            "so a nested value reports where it really is"
                    }
                ]

            testList
                "a set of rules"
                [
                    test "every rule runs" {
                        // Three rules broken at once: all three are reported.
                        let broken =
                            { valid with
                                StartDate = DateOnly(2026, 9, 25)
                                EndDate = DateOnly(2026, 9, 25)
                                Guests = 6
                                CardId = Some "card_1"
                                InvoiceId = Some "inv_1"
                            }

                        let v = Invariants.check Booking.invariants broken

                        Expect.equal
                            (messagesOf v)
                            [
                                "endDate: endDate must be after startDate"
                                "cardId: at most one of cardId, invoiceId may be given"
                                "guests: a booking of more than 4 guests must last at least two nights"
                            ]
                            "three broken rules, three errors, in declaration order"
                    }

                    test "a valid value passes" {
                        Expect.isTrue
                            (Validation.isOk (Invariants.check Booking.invariants valid))
                            "the sample is legal"
                    }

                    test "checkAt reports relative to an enclosing value" {
                        let broken = { valid with EndDate = valid.StartDate }

                        let v =
                            Invariants.checkAt (Path.root |> Path.field "booking") Booking.invariants broken

                        Expect.equal
                            (messagesOf v)
                            [ "booking.endDate: endDate must be after startDate" ]
                            "the path travels"
                    }

                    test "rules are exposed as data" {
                        let names = Invariants.rules Booking.invariants |> List.map (fun r -> r.Name)

                        Expect.equal
                            names
                            [ "endDate-after-startDate"; "one-payment-method"; "party-fits" ]
                            "so other packages can read them"
                    }

                    test "describe gives one line per rule" {
                        let lines = Invariants.describe Booking.invariants
                        Expect.equal (List.length lines) 3 "one each"
                        Expect.all lines (fun l -> l <> "") "and none empty"
                    }

                    test "add keeps declaration order" {
                        let rules =
                            Invariants.empty "X"
                            |> Invariants.add (Invariant.create "a" "a" (fun _ -> true))
                            |> Invariants.add (Invariant.create "b" "b" (fun _ -> true))

                        Expect.equal
                            (rules.Rules |> List.map (fun r -> r.Name))
                            [ "a"; "b" ]
                            "order is part of the output"
                    }

                    test "a type with no rules accepts everything" {
                        Expect.isTrue (Validation.isOk (Invariants.check (Invariants.empty "X") 42)) "nothing to break"
                    }
                ]

            testList
                "composing with a schema"
                [
                    test "andThen runs the rules after decoding" {
                        let json =
                            """{"reference":"ABC123","startDate":"2026-09-25","endDate":"2026-09-20","guests":2}"""

                        let v = Schema.fromJson Booking.schema json |> Invariants.andThen Booking.invariants

                        Expect.equal
                            (messagesOf v)
                            [ "endDate: endDate must be after startDate" ]
                            "field decoding succeeded, then the cross-field rule failed"
                    }

                    test "a field failure suppresses the cross-field rules" {
                        // Documented and unavoidable: the rules need a value, and
                        // there is no value when a field did not decode.
                        let json =
                            """{"reference":"TOOLONG","startDate":"2026-09-25","endDate":"2026-09-20"}"""

                        let v = Schema.fromJson Booking.schema json |> Invariants.andThen Booking.invariants

                        let messages = messagesOf v

                        Expect.isTrue
                            (messages |> List.exists (fun m -> m.StartsWith "reference"))
                            "field errors reported"

                        Expect.isFalse
                            (messages |> List.exists (fun m -> m.StartsWith "endDate:"))
                            "and the cross-field rule could not run"
                    }

                    test "enforce is what a smart constructor calls" {
                        match Booking.create valid with
                        | Ok booking -> Expect.equal booking.Reference "ABC123" "the value comes back"
                        | Error e -> failtestf "should have been accepted: %s" (ValidationErrors.format e)

                        Expect.isTrue
                            (Validation.isError (Booking.create { valid with EndDate = valid.StartDate }))
                            "and an illegal value does not"
                    }
                ]

            testList
                "properties"
                [
                    testProperty "the error count never exceeds the rule count"
                    <| fun (guests: int) (days: int) ->
                        let booking =
                            { valid with
                                Guests = guests
                                EndDate = valid.StartDate.AddDays(abs days % 10)
                            }

                        Validation.errorCount (Invariants.check Booking.invariants booking)
                        <= List.length Booking.invariants.Rules

                    testProperty "a value passes exactly when every rule holds"
                    <| fun (guests: int) (days: int) (card: bool) (invoice: bool) ->
                        let booking =
                            { valid with
                                Guests = guests
                                EndDate = valid.StartDate.AddDays(abs days % 10)
                                CardId = (if card then Some "c" else None)
                                InvoiceId = (if invoice then Some "i" else None)
                            }

                        let passes = Validation.isOk (Invariants.check Booking.invariants booking)
                        let allHold = Booking.invariants.Rules |> List.forall (fun r -> r.Holds booking)
                        passes = allHold
                ]
        ]
