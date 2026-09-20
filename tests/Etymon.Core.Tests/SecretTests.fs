module Etymon.Core.Tests.SecretTests

open Expecto
open Etymon

/// Stands in for the config records Etymon.Config will produce, so that the tests
/// exercise the case that actually leaks secrets: printing the whole record.
[<NoComparison>]
type private Settings =
    {
        Host: string
        Password: Secret<string>
        Retries: int
    }

let tests =
    testList
        "Secret"
        [
            test "ToString is redacted" {
                Expect.equal ((Secret.create "hunter2").ToString()) "<redacted>" "ToString must never show the value"
            }

            test "string is redacted" {
                Expect.equal (string (Secret.create "hunter2")) "<redacted>" "string goes through ToString"
            }

            test "%O is redacted" {
                Expect.equal (sprintf "%O" (Secret.create "hunter2")) "<redacted>" "%O goes through ToString"
            }

            test "%A is redacted" {
                Expect.equal (sprintf "%A" (Secret.create "hunter2")) "<redacted>" "%A uses StructuredFormatDisplay"
            }

            test "a record containing a secret does not leak it" {
                // The case that matters: nobody writes `printfn "%A" password`, they write
                // `printfn "%A" config`.
                let settings =
                    {
                        Host = "db.internal"
                        Password = Secret.create "hunter2"
                        Retries = 3
                    }

                let rendered = sprintf "%A" settings

                Expect.isFalse (rendered.Contains "hunter2") "the secret must not appear anywhere in the output"
                Expect.stringContains rendered "<redacted>" "the field is shown as redacted"
                Expect.stringContains rendered "db.internal" "non-secret fields still print normally"
            }

            test "a list of secrets does not leak" {
                let rendered = sprintf "%A" [ Secret.create "a"; Secret.create "b" ]

                Expect.isFalse (rendered.Contains "\"a\"") "no element may print its value"
                Expect.stringContains rendered "<redacted>" "elements render redacted"
            }

            test "an option of a secret does not leak" {
                let rendered = sprintf "%A" (Some(Secret.create "hunter2"))
                Expect.isFalse (rendered.Contains "hunter2") "wrapping must not defeat redaction"
            }

            test "reveal returns the value" {
                Expect.equal (Secret.reveal (Secret.create "hunter2")) "hunter2" "the value is recoverable on purpose"
            }

            test "secrets compare by their hidden values" {
                Expect.equal (Secret.create "a") (Secret.create "a") "equal values, equal secrets"
                Expect.notEqual (Secret.create "a") (Secret.create "b") "different values, different secrets"
            }

            test "records containing secrets stay comparable" {
                // Without this, every test touching a config record would have to unwrap
                // by hand.
                let a =
                    {
                        Host = "h"
                        Password = Secret.create "p"
                        Retries = 1
                    }

                let b =
                    {
                        Host = "h"
                        Password = Secret.create "p"
                        Retries = 1
                    }

                Expect.equal a b "structural equality survives the custom equality"
            }

            test "secrets hash by their hidden values" {
                Expect.equal (hash (Secret.create "a")) (hash (Secret.create "a")) "equal secrets hash equally"

                // A Dictionary, not a Map: Secret is deliberately NoComparison, so it can
                // be a hash key but never a sorted one.
                let byKey = System.Collections.Generic.Dictionary<Secret<string>, int>()
                byKey[Secret.create "k"] <- 1
                Expect.isTrue (byKey.ContainsKey(Secret.create "k")) "so they work as dictionary keys"
            }

            test "a secret never equals a non-secret" {
                Expect.isFalse ((Secret.create "a").Equals("a")) "the wrapper is part of the identity"
            }

            test "map transforms without revealing" {
                let trimmed = Secret.create "  k  " |> Secret.map (fun s -> s.Trim())
                Expect.equal (Secret.reveal trimmed) "k" "the transformation applied"
                Expect.equal (sprintf "%A" trimmed) "<redacted>" "and the result is still redacted"
            }

            testProperty "no rendering of a secret ever contains its value"
            <| fun (value: string) ->
                // FsCheck will happily generate "" and strings that appear in the word
                // "redacted"; only non-trivial values make the assertion meaningful.
                if System.String.IsNullOrEmpty value || "<redacted>".Contains value then
                    true
                else
                    let secret = Secret.create value

                    not ((sprintf "%A" secret).Contains value)
                    && not ((sprintf "%O" secret).Contains value)
                    && not ((string secret).Contains value)
        ]
