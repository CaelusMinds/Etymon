module Etymon.Core.Tests.ValidationTests

open Expecto
open Etymon

/// A failure at a named field, for tests that only care about how many there are.
let private fail name : Validation<int> =
    Validation.error (Path.root |> Path.field name) ErrorReason.Missing "is required"

let private messagesOf (v: Validation<'T>) =
    v |> Validation.errorList |> List.map (fun e -> e.ToString())

let tests =
    testList
        "Validation"
        [
            testList
                "basics"
                [
                    test "ok carries the value" { Expect.equal (Validation.ok 42) (Ok 42) "ok is Ok" }

                    test "error carries one error" {
                        let v = fail "name"
                        Expect.equal (Validation.errorCount v) 1 "one failure is one error"
                        Expect.equal (messagesOf v) [ "name: is required" ] "the path prefixes the message"
                    }

                    test "isOk and isError disagree" {
                        Expect.isTrue (Validation.isOk (Validation.ok 1)) "a value passed"
                        Expect.isFalse (Validation.isError (Validation.ok 1)) "a value passed"
                        Expect.isTrue (Validation.isError (fail "x")) "a value was rejected"
                    }

                    test "map transforms a passing value" {
                        Expect.equal (Validation.ok 2 |> Validation.map ((*) 10)) (Ok 20) "map applies to Ok"
                    }

                    test "map leaves errors alone" {
                        let v = fail "x" |> Validation.map ((*) 10)
                        Expect.equal (Validation.errorCount v) 1 "map must not lose errors"
                    }

                    test "errorCount is zero for a passing value" {
                        Expect.equal (Validation.errorCount (Validation.ok 1)) 0 "nothing was wrong"
                        Expect.equal (Validation.errorList (Validation.ok 1)) [] "nothing to list"
                    }
                ]

            testList
                "accumulation"
                [
                    test "apply collects both sides" {
                        let v = Validation.apply (Validation.map (+) (fail "a")) (fail "b")
                        Expect.equal (Validation.errorCount v) 2 "two bad inputs, two errors"
                        Expect.equal (messagesOf v) [ "a: is required"; "b: is required" ] "left errors come first"
                    }

                    test "apply keeps the one error when only one side failed" {
                        let good = Validation.map (+) (Validation.ok 1)
                        Expect.equal (Validation.errorCount (Validation.apply good (fail "b"))) 1 "only b was bad"

                        let bad = Validation.map (+) (fail "a")
                        Expect.equal (Validation.errorCount (Validation.apply bad (Validation.ok 2))) 1 "only a was bad"
                    }

                    test "map2 and map3 accumulate" {
                        let two = Validation.map2 (+) (fail "a") (fail "b")
                        Expect.equal (Validation.errorCount two) 2 "map2 collects two"

                        let three =
                            Validation.map3 (fun a b c -> a + b + c) (fail "a") (fail "b") (fail "c")

                        Expect.equal (Validation.errorCount three) 3 "map3 collects three"
                    }

                    test "map3 passes all three values through" {
                        let v =
                            Validation.map3
                                (fun a b c -> a + b + c)
                                (Validation.ok 1)
                                (Validation.ok 2)
                                (Validation.ok 3)

                        Expect.equal v (Ok 6) "the function sees every argument"
                    }

                    test "zip pairs two passing values" {
                        Expect.equal (Validation.zip (Validation.ok 1) (Validation.ok "a")) (Ok(1, "a")) "zip pairs"
                    }

                    test "bind stops at the first failure" {
                        // The documented exception: bind cannot accumulate, because the second
                        // step needs a value the first step never produced.
                        let mutable ran = false

                        let v =
                            fail "a"
                            |> Validation.bind (fun x ->
                                ran <- true
                                fail "b" |> Validation.map (fun y -> x + y)
                            )

                        Expect.isFalse ran "the second step must not run"
                        Expect.equal (Validation.errorCount v) 1 "only the first error survives"
                    }

                    test "bind runs the second step when the first passed" {
                        let v = Validation.ok 1 |> Validation.bind (fun x -> Validation.ok (x + 1))
                        Expect.equal v (Ok 2) "bind threads the value"
                    }

                    testProperty "N failures produce exactly N errors"
                    <| fun (count: byte) ->
                        let n = 1 + int count % 24

                        let combined =
                            [ 1..n ] |> List.map (fun i -> fail $"field%d{i}") |> Validation.sequence

                        Validation.errorCount combined = n
                ]

            testList
                "traverse"
                [
                    test "traverse collects every element's errors" {
                        let v =
                            [ "1"; "x"; "y" ]
                            |> Validation.traverse (fun s ->
                                match System.Int32.TryParse s with
                                | true, n -> Validation.ok n
                                | false, _ ->
                                    Validation.error Path.root (ErrorReason.Rejected "int") "must be a number"
                            )

                        Expect.equal (Validation.errorCount v) 2 "two elements were bad"
                    }

                    test "traverse keeps order on success" {
                        let v = [ 1; 2; 3 ] |> Validation.traverse Validation.ok
                        Expect.equal v (Ok [ 1; 2; 3 ]) "order must be preserved"
                    }

                    test "traverse of an empty list succeeds" {
                        let v = ([]: int list) |> Validation.traverse Validation.ok
                        Expect.equal v (Ok []) "nothing to reject"
                    }

                    test "traverseIndexed labels each element with its position" {
                        let v =
                            [ "ok"; "bad"; "bad" ]
                            |> Validation.traverseIndexed (fun _ s ->
                                if s = "ok" then
                                    Validation.ok s
                                else
                                    Validation.error Path.root (ErrorReason.Rejected "bad") "is not ok"
                            )

                        Expect.equal
                            (messagesOf v)
                            [ "[1]: is not ok"; "[2]: is not ok" ]
                            "each error carries its index"
                    }

                    test "sequence is traverse with id" {
                        let v = Validation.sequence [ Validation.ok 1; fail "b"; fail "c" ]
                        Expect.equal (Validation.errorCount v) 2 "two of three failed"
                    }
                ]

            testList
                "paths"
                [
                    test "underField re-roots errors" {
                        let v = fail "zip" |> Validation.underField "address"
                        Expect.equal (messagesOf v) [ "address.zip: is required" ] "the outer field is prepended"
                    }

                    test "underIndex re-roots errors" {
                        let v = fail "name" |> Validation.underIndex 3
                        Expect.equal (messagesOf v) [ "[3].name: is required" ] "the index is prepended"
                    }

                    test "under nests repeatedly in the right order" {
                        let v =
                            fail "zip"
                            |> Validation.underField "address"
                            |> Validation.underIndex 0
                            |> Validation.underField "people"

                        Expect.equal (messagesOf v) [ "people[0].address.zip: is required" ] "outermost wrapper wins"
                    }

                    test "re-rooting a passing value changes nothing" {
                        Expect.equal (Validation.ok 1 |> Validation.underField "x") (Ok 1) "nothing to re-root"
                    }
                ]

            testList
                "interop"
                [
                    test "ofOption turns None into a failure" {
                        let v =
                            None
                            |> Validation.ofOption (Path.root |> Path.field "name") ErrorReason.Missing "is required"

                        Expect.equal (messagesOf v) [ "name: is required" ] "None becomes one error"

                        Expect.equal
                            (Some 1 |> Validation.ofOption Path.root ErrorReason.Missing "x")
                            (Ok 1)
                            "Some passes"
                    }

                    test "ofResult lifts a plain Result" {
                        let toError (m: string) =
                            {
                                Path = Path.root |> Path.field "x"
                                Reason = ErrorReason.Rejected "nope"
                                Message = m
                            }

                        let v: Validation<int> = Error "was rejected" |> Validation.ofResult toError
                        Expect.equal (messagesOf v) [ "x: was rejected" ] "the mapping decides the error"
                        Expect.equal (Ok 5 |> Validation.ofResult toError) (Ok 5) "Ok passes through"
                    }

                    test "defaultValue and defaultWith recover" {
                        Expect.equal (Validation.defaultValue 0 (fail "x")) 0 "the fallback is used"
                        Expect.equal (Validation.defaultValue 0 (Validation.ok 7)) 7 "the value wins"

                        Expect.equal
                            (fail "x" |> Validation.defaultWith ValidationErrors.count)
                            1
                            "the fallback can read the errors"
                    }

                    test "a Validation is a Result and matches as one" {
                        match Validation.ok 1 with
                        | Ok v -> Expect.equal v 1 "Ok destructures"
                        | Error _ -> failtest "should have passed"
                    }
                ]

            testList
                "the validation computation expression"
                [
                    test "and! collects errors from every binding" {
                        // This is the behaviour the whole error model exists for.
                        let v =
                            validation {
                                let! a = fail "name"
                                and! b = fail "age"
                                and! c = fail "email"
                                return a + b + c
                            }

                        Expect.equal (Validation.errorCount v) 3 "three bad fields, three errors"

                        Expect.equal
                            (messagesOf v)
                            [ "name: is required"; "age: is required"; "email: is required" ]
                            "errors arrive in binding order"
                    }

                    test "and! returns the built value when everything passes" {
                        let v =
                            validation {
                                let! a = Validation.ok 1
                                and! b = Validation.ok 2
                                and! c = Validation.ok 3
                                return a + b + c
                            }

                        Expect.equal v (Ok 6) "every binding reaches the body"
                    }

                    test "and! reports the single failure among passing bindings" {
                        let v =
                            validation {
                                let! a = Validation.ok 1
                                and! b = fail "age"
                                and! c = Validation.ok 3
                                return a + b + c
                            }

                        Expect.equal (messagesOf v) [ "age: is required" ] "only the bad binding reports"
                    }

                    test "sequential let! bindings short-circuit" {
                        // Documented and intended: a second let! depends on the first value,
                        // so there is nothing to accumulate.
                        let mutable secondRan = false

                        let v =
                            validation {
                                let! a = fail "first"

                                let! b =
                                    secondRan <- true
                                    fail "second"

                                return a + b
                            }

                        Expect.isFalse secondRan "the dependent binding must not run"
                        Expect.equal (Validation.errorCount v) 1 "only the first error"
                    }

                    test "return! passes a validation through" {
                        let v = validation { return! Validation.ok 9 }
                        Expect.equal v (Ok 9) "return! is identity"
                    }

                    testProperty "an N-way and! chain reports every failure"
                    <| fun (a: bool, b: bool, c: bool, d: bool) ->
                        let pick flag name =
                            if flag then Validation.ok 1 else fail name

                        let v =
                            validation {
                                let! w = pick a "a"
                                and! x = pick b "b"
                                and! y = pick c "c"
                                and! z = pick d "d"
                                return w + x + y + z
                            }

                        let expected = [ a; b; c; d ] |> List.filter not |> List.length

                        Validation.errorCount v = expected
                ]

            testList
                "ValidationErrors"
                [
                    test "one holds exactly one error" {
                        let e =
                            {
                                Path = Path.root
                                Reason = ErrorReason.Missing
                                Message = "is required"
                            }

                        Expect.equal (ValidationErrors.one e |> ValidationErrors.count) 1 "one is one"
                        Expect.equal (ValidationErrors.one e |> ValidationErrors.head) e "head is the error"
                    }

                    test "ofList rejects emptiness" {
                        let anError =
                            {
                                Path = Path.root
                                Reason = ErrorReason.Missing
                                Message = "is required"
                            }

                        Expect.equal (ValidationErrors.ofList []) None "no errors is not an error state"
                        Expect.isSome (ValidationErrors.ofList [ anError ]) "a non-empty list builds"
                    }

                    test "ofHeadTail is non-empty by construction" {
                        let anError =
                            {
                                Path = Path.root
                                Reason = ErrorReason.Missing
                                Message = "is required"
                            }

                        Expect.equal (ValidationErrors.ofHeadTail anError [] |> ValidationErrors.count) 1 "head alone"

                        Expect.equal
                            (ValidationErrors.ofHeadTail anError [ anError; anError ]
                             |> ValidationErrors.count)
                            3
                            "head plus tail"
                    }

                    test "append keeps left-hand errors first" {
                        let left = ValidationErrors.create Path.root ErrorReason.Missing "left"
                        let right = ValidationErrors.create Path.root ErrorReason.Missing "right"

                        let joined = ValidationErrors.append left right

                        Expect.equal
                            (joined |> ValidationErrors.toList |> List.map (fun e -> e.Message))
                            [ "left"; "right" ]
                            "order is preserved"
                    }

                    test "format prints one error per line" {
                        let errs =
                            ValidationErrors.append
                                (ValidationErrors.create (Path.root |> Path.field "a") ErrorReason.Missing "is required")
                                (ValidationErrors.create (Path.root |> Path.field "b") ErrorReason.Missing "is required")

                        let lines = (ValidationErrors.format errs).Split(System.Environment.NewLine)

                        Expect.equal lines [| "a: is required"; "b: is required" |] "one line each"
                    }

                    test "the reason survives independently of the message" {
                        let errs =
                            ValidationErrors.create
                                Path.root
                                (ErrorReason.TypeMismatch("string", "number"))
                                "wrong type"

                        match (ValidationErrors.head errs).Reason with
                        | ErrorReason.TypeMismatch(expected, actual) ->
                            Expect.equal expected "string" "the expected type is machine-readable"
                            Expect.equal actual "number" "so is the actual one"
                        | other -> failtestf "expected a TypeMismatch, got %A" other
                    }
                ]
        ]
