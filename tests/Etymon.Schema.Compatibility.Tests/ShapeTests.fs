/// Deriving a shape from the Schema that does the work.
///
/// A snapshot matches its entries by name. A shape recorded under a name its
/// Schema does not carry diffs cleanly against itself forever and is never
/// compared with the thing it describes -- the failure is silence, which is the
/// failure this package exists to remove. These pin that the name cannot be
/// supplied wrongly, because it is not supplied at all.
module Etymon.Schema.Compatibility.Tests.ShapeTests

open System
open Expecto
open Etymon

type private SignIn = { Email: string; Password: string }

let private signInSchema =
    Schema.object "SignInRequest" {
        let! email = Schema.required "email" Schema.string (fun r -> r.Email)
        and! password = Schema.required "password" Schema.string (fun r -> r.Password)
        return { Email = email; Password = password }
    }

type private Suspended =
    {
        Reason: string
        Reasons: string list
        History: string list option
    }

let private reasonRule = (Check.oneOf [ "fired"; "left" ]).Constraint

let private suspendedSchema =
    let reason = Schema.string |> Schema.constrain (Check.oneOf [ "fired"; "left" ])

    Schema.object "Suspended" {
        let! r = Schema.required "reason" reason (fun x -> x.Reason)
        and! rs = Schema.required "reasons" (Schema.list reason) (fun x -> x.Reasons)

        and! history =
            Schema.required "history" (Schema.nullable (Schema.list reason)) (fun x -> x.History)

        return
            {
                Reason = r
                Reasons = rs
                History = history
            }
    }

let tests =
    testList
        "Shape.ofSchema"
        [
            test "the name comes from the Schema, so it cannot disagree with it" {
                let shape = Shape.ofSchema 1 signInSchema
                Expect.equal shape.Name "SignInRequest" "the Schema already said so"
                Expect.equal shape.Version 1 "and the version is still the caller's to give"
            }

            test "the fields are the Schema's fields" {
                let shape = Shape.ofSchema 1 signInSchema

                Expect.equal (shape.Fields |> List.map (fun f -> f.Name)) [ "email"; "password" ] "in declaration order"

                Expect.isTrue (shape.Fields |> List.forall (fun f -> f.Required)) "both required"
            }

            test "the derived shape is the named one, given the same name" {
                // ofSchemaNamed is the escape hatch, not a different derivation.
                Expect.equal
                    (Shape.ofSchema 3 signInSchema)
                    (Shape.ofSchemaNamed "SignInRequest" 3 signInSchema)
                    "same shape either way"
            }

            test "a deliberate rename keeps the old entry findable" {
                // The reason the escape hatch exists: a snapshot entry outlives
                // the name the code uses for it.
                let shape = Shape.ofSchemaNamed "LegacySignIn" 3 signInSchema
                Expect.equal shape.Name "LegacySignIn" "recorded under the name the snapshot knows"
            }

            test "an unnamed Schema is refused, rather than recorded namelessly" {
                // A shape with an empty name is the silent failure. Saying so is
                // the whole point of taking the name away from the caller.
                let unnamed = Schema.list Schema.string

                let message =
                    try
                        Shape.ofSchema 1 unnamed |> ignore
                        None
                    with :? ArgumentException as e ->
                        Some e.Message

                match message with
                | Some text -> Expect.stringContains text "ofSchemaNamed" "and says what to do instead"
                | None -> failtest "an unnamed schema was accepted, and would snapshot under no name"
            }

            test "the rules on a sequence's elements are carried, not dropped" {
                // The fields most likely to hold a code set are the list-typed
                // ones -- roles, permissions, grants -- which were exactly the
                // ones left unchecked.
                let shape = Shape.ofSchema 1 suspendedSchema

                let field name =
                    shape.Fields |> List.find (fun f -> f.Name = name)

                Expect.equal (field "reason").Constraints [ reasonRule ] "a scalar's rules, as before"
                Expect.equal (field "reason").ElementConstraints (Some []) "and none recorded on a scalar"
                Expect.equal (field "reasons").Constraints [] "a sequence has no rules of its own"
                Expect.equal (field "reasons").ElementConstraints (Some [ reasonRule ]) "its elements do"
            }

            test "element rules are found through a nullable list" {
                let shape = Shape.ofSchema 1 suspendedSchema
                let field = shape.Fields |> List.find (fun f -> f.Name = "history")

                Expect.equal
                    field.ElementConstraints
                    (Some [ reasonRule ])
                    "a list that may itself be null still reports them"
            }
        ]
