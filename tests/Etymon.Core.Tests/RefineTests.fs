module Etymon.Core.Tests.RefineTests

open Expecto
open Etymon

// The worked example from the README, used here as the thing under test so that the
// documented shape and the tested shape cannot drift.
type Email = private Email of string

module Email =
    let refinement =
        Refine.ofString "Email"
        |> Refine.trimmed
        |> Refine.lowercased
        |> Refine.length (Some 3) (Some 254)
        |> Refine.format Format.Email
        |> Refine.wrap Email (fun (Email s) -> s)

    let create (s: string) = Refine.create refinement s
    let value (Email s) = s

type Zip = private Zip of string

module Zip =
    let refinement =
        Refine.ofString "Zip"
        |> Refine.exactLength 5
        |> Refine.pattern @"^\d+$"
        |> Refine.wrap Zip (fun (Zip s) -> s)

    let create (s: string) = Refine.create refinement s

let private messagesOf (v: Validation<'T>) =
    v |> Validation.errorList |> List.map (fun e -> e.Message)

// A coded value, as an application would carry a role or a status: the shape
// that made a partial parse look like the natural thing to wrap.
type Role =
    | Accountant
    | Bookkeeper

let private parseRole (s: string) =
    match s with
    | "accountant" -> Some Accountant
    | "bookkeeper" -> Some Bookkeeper
    | _ -> None

let private renderRole role =
    match role with
    | Accountant -> "accountant"
    | Bookkeeper -> "bookkeeper"

type Code = Code of string

let tests =
    testList
        "Refine"
        [
            testList
                "building"
                [
                    test "identity accepts anything" {
                        Expect.equal (Refine.create (Refine.identity "Anything") 42) (Ok 42) "no rules, no rejection"
                    }

                    test "the name is carried" {
                        Expect.equal Email.refinement.Name "Email" "the name reaches error messages and schemas"
                    }

                    test "rename replaces the name" {
                        let renamed = Refine.ofString "Text" |> Refine.rename "Slug"
                        Expect.equal renamed.Name "Slug" "rename is the only way the name changes"
                    }

                    test "constraints are exposed in the order they were added" {
                        Expect.equal
                            Zip.refinement.Constraints
                            [ Constraint.Length(Some 5, Some 5); Constraint.Pattern @"^\d+$" ]
                            "downstream packages read this list, so its order is part of the contract"
                    }

                    test "wrap preserves the constraints" {
                        Expect.equal
                            (List.length Email.refinement.Constraints)
                            2
                            "wrapping changes the type, not the rules"
                    }
                ]

            testList
                "accumulation"
                [
                    test "every broken rule is reported" {
                        // "abc" is both the wrong length and not all digits.
                        let v = Zip.create "abc"

                        Expect.equal
                            (messagesOf v)
                            [ "must be exactly 5 characters"; "must match ^\\d+$" ]
                            "both rules ran, and both reported"
                    }

                    test "one broken rule reports once" {
                        Expect.equal (messagesOf (Zip.create "abcde")) [ "must match ^\\d+$" ] "only the pattern failed"
                    }

                    test "a valid value reports nothing" {
                        Expect.isTrue (Validation.isOk (Zip.create "12345")) "12345 satisfies both rules"
                    }

                    testProperty "the error count never exceeds the rule count"
                    <| fun (s: string) ->
                        let s = if isNull s then "" else s
                        Validation.errorCount (Zip.create s) <= List.length Zip.refinement.Constraints
                ]

            testList
                "normalisation"
                [
                    test "trimming happens before the rules run" {
                        // "   " trims to "", which then fails the length rule. If trimming ran
                        // after, it would have passed.
                        let refinement = Refine.ofString "Name" |> Refine.trimmed |> Refine.nonEmpty

                        Expect.equal
                            (messagesOf (Refine.create refinement "   "))
                            [ "must be at least 1 character" ]
                            "the trimmed value is what gets checked"
                    }

                    test "normalisation applies to the value that is returned" {
                        let refinement = Refine.ofString "Name" |> Refine.trimmed

                        Expect.equal
                            (Refine.create refinement "  hello  ")
                            (Ok "hello")
                            "the caller gets the normalised value"
                    }

                    test "case folding is invariant" {
                        let lower = Refine.ofString "X" |> Refine.lowercased
                        let upper = Refine.ofString "X" |> Refine.uppercased
                        Expect.equal (Refine.create lower "ABC") (Ok "abc") "lowercased folds down"
                        Expect.equal (Refine.create upper "abc") (Ok "ABC") "uppercased folds up"
                    }

                    test "normalisation runs before rules wherever it is written" {
                        // Documented behaviour: conversions all run, then all checks run.
                        let refinement = Refine.ofString "Name" |> Refine.nonEmpty |> Refine.trimmed

                        Expect.equal
                            (messagesOf (Refine.create refinement "   "))
                            [ "must be at least 1 character" ]
                            "position in the pipeline does not change the ordering"
                    }
                ]

            testList
                "wrapping"
                [
                    test "the constructed type comes back" {
                        match Email.create "Someone@Example.COM" with
                        | Ok email -> Expect.equal (Email.value email) "someone@example.com" "normalised and wrapped"
                        | Error e -> failtestf "should have been accepted: %s" (ValidationErrors.format e)
                    }

                    test "rules still run after wrapping" {
                        Expect.equal
                            (messagesOf (Email.create "nope"))
                            [ "must be a valid email" ]
                            "wrap maps the checks too"
                    }

                    test "extract recovers the raw value" {
                        match Email.create "someone@example.com" with
                        | Ok email ->
                            Expect.equal
                                (Refine.extract Email.refinement email)
                                "someone@example.com"
                                "extract is the inverse of construction"
                        | Error e -> failtestf "should have been accepted: %s" (ValidationErrors.format e)
                    }

                    test "a whitespace-only email fails on length and format together" {
                        let v = Email.create "  "
                        Expect.equal (Validation.errorCount v) 2 "trimmed to empty, so both rules reject it"
                    }
                ]

            testList
                "paths"
                [
                    test "create reports at the root" {
                        match Validation.errorList (Zip.create "abc") with
                        | first :: _ -> Expect.equal (Path.toString first.Path) "" "no enclosing value was named"
                        | [] -> failtest "expected errors"
                    }

                    test "createAt reports relative to the enclosing value" {
                        let path = Path.root |> Path.field "address" |> Path.field "zip"
                        let v = Refine.createAt path Zip.refinement "abc"

                        Expect.equal
                            (v |> Validation.errorList |> List.map (fun e -> e.ToString()) |> List.head)
                            "address.zip: must be exactly 5 characters"
                            "the path travels with the error"
                    }

                    test "every accumulated error gets the same path" {
                        let path = Path.root |> Path.field "zip"

                        let paths =
                            Refine.createAt path Zip.refinement "abc"
                            |> Validation.errorList
                            |> List.map (fun e -> Path.toString e.Path)

                        Expect.equal paths [ "zip"; "zip" ] "both errors are about the same field"
                    }
                ]

            testList
                "validate"
                [
                    test "an already-built value can be re-checked" {
                        let ageRefinement = Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)
                        Expect.isTrue (Validation.isOk (Refine.validate ageRefinement 30)) "30 is a valid age"
                        Expect.equal (Validation.errorCount (Refine.validate ageRefinement 200)) 1 "200 is not"
                    }

                    test "validate skips normalisation" {
                        // validate checks a value you already hold; it does not convert one.
                        let refinement = Refine.ofString "Name" |> Refine.trimmed |> Refine.nonEmpty

                        Expect.equal
                            (Validation.errorCount (Refine.validate refinement "  "))
                            0
                            "the rules see the value as given"
                    }
                ]

            testList
                "the escape hatch"
                [
                    test "satisfies enforces an arbitrary predicate" {
                        let refinement =
                            Refine.ofInt "Even"
                            |> Refine.satisfies "even" "must be even" (fun n -> n % 2 = 0)

                        Expect.equal (Refine.create refinement 4) (Ok 4) "4 is even"
                        Expect.equal (messagesOf (Refine.create refinement 5)) [ "must be even" ] "5 is not"
                    }

                    test "an opaque rule is visible as data but marked underivable" {
                        let refinement =
                            Refine.ofInt "Even"
                            |> Refine.satisfies "even" "must be even" (fun n -> n % 2 = 0)

                        Expect.equal
                            refinement.Constraints
                            [ Constraint.Opaque("even", "must be even") ]
                            "downstream packages can see it exists and choose to skip it"
                    }
                ]

            testList
                "numeric and collection rules"
                [
                    test "intRange rejects outside the bounds" {
                        let age = Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)

                        Expect.equal
                            (messagesOf (Refine.create age -1))
                            [ "must be between 0 and 130" ]
                            "below the floor"

                        Expect.equal
                            (messagesOf (Refine.create age 131))
                            [ "must be between 0 and 130" ]
                            "above the ceiling"
                    }

                    test "greaterThan excludes the bound" {
                        let price = Refine.ofDecimal "Price" |> Refine.greaterThan 0m

                        Expect.equal
                            (messagesOf (Refine.create price 0m))
                            [ "must be greater than 0" ]
                            "zero is excluded"

                        Expect.isTrue (Validation.isOk (Refine.create price 0.01m)) "anything above passes"
                    }

                    test "itemCount and distinct compose" {
                        let tags =
                            Refine.ofList "Tags" |> Refine.itemCount (Some 1) (Some 3) |> Refine.distinct

                        Expect.isTrue (Validation.isOk (Refine.create tags [ "a"; "b" ])) "two distinct tags"

                        Expect.equal
                            (Validation.errorCount (Refine.create tags [ "a"; "a" ]))
                            1
                            "a duplicate is rejected"

                        Expect.equal
                            (Validation.errorCount (Refine.create tags ([]: string list)))
                            1
                            "empty is rejected"

                        Expect.equal
                            (Validation.errorCount (Refine.create tags [ "a"; "a"; "a"; "a" ]))
                            2
                            "too many and not distinct is two errors"
                    }
                ]
            testList
                "ordering"
                [
                    test "a rule before wrap guards the conversion after it" {
                        // The rules before a wrap run before it. A check that a
                        // code is known, then a wrap that looks it up, must
                        // refuse "god" rather than throw from inside the decoder
                        // -- which, on a replay of stored events, reads as data
                        // corruption rather than as a validation error.
                        let role =
                            Refine.identity "Role"
                            |> Refine.satisfies "known_role" "is not a role" (fun s -> (parseRole s).IsSome)
                            |> Refine.wrap (fun s -> (parseRole s).Value) renderRole

                        Expect.equal (Refine.create role "accountant") (Ok Accountant) "a known code converts"

                        Expect.equal
                            (messagesOf (Refine.create role "god"))
                            [ "is not a role" ]
                            "an unknown one is refused, not thrown"
                    }

                    test "parse refuses with its own rule, at the path" {
                        let role =
                            Refine.identity "Role"
                            |> Refine.parse "known_role" "is not a role" parseRole renderRole

                        let path = Path.field "role" Path.root

                        match Validation.errorList (Refine.createAt path role "god") with
                        | [ e ] ->
                            Expect.equal e.Message "is not a role" "the description given"

                            Expect.equal
                                e.Reason
                                (ErrorReason.ConstraintFailed(Constraint.Opaque("known_role", "is not a role")))
                                "the rule, carrying the code given"

                            Expect.equal e.Path path "at the value's path"
                        | other -> failtestf "expected one refusal, got %A" other

                        Expect.equal (Refine.create role "bookkeeper") (Ok Bookkeeper) "and a known code converts"
                    }

                    test "parse publishes its rule like any other" {
                        let role =
                            Refine.identity "Role"
                            |> Refine.parse "known_role" "is not a role" parseRole renderRole

                        Expect.equal
                            role.Constraints
                            [ Constraint.Opaque("known_role", "is not a role") ]
                            "in the constraints"
                    }

                    test "validate still runs the rules before a wrap" {
                        // wrap keeps them in Checks, mapped back through destruct,
                        // so a value built by another route is still held to them.
                        let code =
                            Refine.ofString "Code"
                            |> Refine.nonEmpty
                            |> Refine.wrap Code (fun (Code s) -> s)

                        Expect.isError (Refine.validate code (Code "")) "an empty code fails the rule it never met"
                        Expect.isOk (Refine.validate code (Code "x")) "and a non-empty one passes"
                    }

                    test "a rule before wrap refuses before a rule after it sees a value" {
                        let code =
                            Refine.ofString "Code"
                            |> Refine.nonEmpty
                            |> Refine.wrap Code (fun (Code s) -> s)
                            |> Refine.satisfies "short" "must be short" (fun (Code s) -> s.Length <= 3)

                        Expect.equal
                            (messagesOf (Refine.create code "toolong"))
                            [ "must be short" ]
                            "the post-wrap rule runs"

                        Expect.equal
                            (Validation.errorCount (Refine.create code ""))
                            1
                            "only the pre-wrap rule reports on an empty code"
                    }
                ]
        ]
