module Etymon.Base.Tests.DictEnvTests

open System
open System.Collections.Generic
open Expecto
open Etymon

let private sample () =
    Dict.ofList [ "accept", "application/json"; "retries", "3" ]

let tests =
    testList
        "Dict and Env"
        [
            testList
                "Dict"
                [
                    test "tryFind" {
                        let headers = sample ()
                        Expect.equal (Dict.tryFind "accept" headers) (Some "application/json") "present"
                        Expect.equal (Dict.tryFind "missing" headers) None "absent"
                    }

                    test "tryFind is case sensitive on an ordinary dictionary" {
                        let headers = sample ()
                        Expect.equal (Dict.tryFind "Accept" headers) None "the comparer decides, not the lookup"
                    }

                    test "ofListIgnoreCase builds one that is not" {
                        let headers = Dict.ofListIgnoreCase [ "Content-Type", "application/json" ]
                        Expect.equal (Dict.tryFind "content-type" headers) (Some "application/json") "case is ignored"

                        Expect.equal
                            (Dict.tryFind "CONTENT-TYPE" headers)
                            (Some "application/json")
                            "in either direction"
                    }

                    test "tryFindReadOnly works through the read-only interface" {
                        let headers = sample () :> IReadOnlyDictionary<string, string>
                        Expect.equal (Dict.tryFindReadOnly "accept" headers) (Some "application/json") "present"
                        Expect.equal (Dict.tryFindReadOnly "missing" headers) None "absent"
                    }

                    test "findOr falls back" {
                        let headers = sample ()
                        Expect.equal (Dict.findOr "*/*" "accept" headers) "application/json" "present wins"
                        Expect.equal (Dict.findOr "*/*" "missing" headers) "*/*" "absent falls back"
                    }

                    test "tryPick looks up and transforms in one step" {
                        let headers = sample ()
                        Expect.equal (Dict.tryPick Parse.int "retries" headers) (Some 3) "present and parses"
                        Expect.equal (Dict.tryPick Parse.int "accept" headers) None "present but does not parse"
                        Expect.equal (Dict.tryPick Parse.int "missing" headers) None "absent"
                    }

                    test "keys and values are complete" {
                        let headers = sample ()
                        Expect.equal (Dict.keys headers |> List.sort) [ "accept"; "retries" ] "every key"
                        Expect.equal (Dict.values headers |> List.sort) [ "3"; "application/json" ] "every value"
                    }

                    test "containsKey" {
                        let headers = sample ()
                        Expect.isTrue (Dict.containsKey "accept" headers) "present"
                        Expect.isFalse (Dict.containsKey "missing" headers) "absent"
                    }

                    test "toMap preserves the contents" {
                        let asMap = sample () |> Dict.toMap
                        Expect.equal (Map.tryFind "accept" asMap) (Some "application/json") "value survives"
                        Expect.equal (Map.count asMap) 2 "and so does the count"
                    }

                    test "ofList keeps the last value for a repeated key" {
                        let result = Dict.ofList [ "k", "first"; "k", "second" ]
                        Expect.equal (Dict.tryFind "k" result) (Some "second") "last write wins, as assignment does"
                    }

                    testProperty "a value put in can be found again"
                    <| fun (key: string) (value: int) ->
                        // A null key is not something a dictionary accepts, so it is
                        // not something this property is about.
                        if isNull key then
                            true
                        else
                            Dict.tryFind key (Dict.ofList [ key, value ]) = Some value
                ]

            testList
                "Env"
                [ // These set and unset real environment variables in the test
                    // process, which is safe because each uses a distinct name.
                    test "tryGet reads a variable that is set" {
                        let name = "ETYMON_TEST_TRYGET"

                        try
                            Environment.SetEnvironmentVariable(name, "value")
                            Expect.equal (Env.tryGet name) (Some "value") "set variables are readable"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }

                    test "tryGet treats unset and blank alike" {
                        let name = "ETYMON_TEST_BLANK"

                        try
                            Expect.equal (Env.tryGet name) None "unset is absent"
                            Environment.SetEnvironmentVariable(name, "")
                            Expect.equal (Env.tryGet name) None "empty is absent too"
                            Environment.SetEnvironmentVariable(name, "   ")
                            Expect.equal (Env.tryGet name) None "and so is whitespace"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }

                    test "tryGetAllowEmpty keeps whatever the platform stored" {
                        let name = "ETYMON_TEST_ALLOW_EMPTY"

                        try
                            Expect.equal (Env.tryGetAllowEmpty name) None "unset is still absent"
                            Environment.SetEnvironmentVariable(name, "")

                            // Windows discards a variable set to the empty string, and
                            // whether the runtime does that has changed between .NET
                            // versions. So assert our mapping against what the platform
                            // actually stored, rather than against what it ought to have.
                            match Environment.GetEnvironmentVariable name with
                            | null ->
                                Expect.equal
                                    (Env.tryGetAllowEmpty name)
                                    None
                                    "the platform discarded it, so it is absent"
                            | stored ->
                                Expect.equal
                                    (Env.tryGetAllowEmpty name)
                                    (Some stored)
                                    "the platform kept it, so it is a value"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }

                    test "getOr falls back when unset" {
                        Expect.equal
                            (Env.getOr "Production" "ETYMON_TEST_NEVER_SET")
                            "Production"
                            "the fallback is used"
                    }

                    test "tryParse and the typed readers" {
                        let name = "ETYMON_TEST_TYPED"

                        try
                            Environment.SetEnvironmentVariable(name, "8080")
                            Expect.equal (Env.tryGetInt name) (Some 8080) "reads an int"
                            Expect.equal (Env.tryParse Parse.int name) (Some 8080) "so does tryParse"
                            Expect.equal (Env.tryGetBool name) None "8080 is not a bool"

                            Environment.SetEnvironmentVariable(name, "yes")
                            Expect.equal (Env.tryGetBool name) (Some true) "reads the forms that actually occur"
                            Expect.equal (Env.tryGetInt name) None "and yes is not an int"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }

                    test "isEnabled defaults to off" {
                        let name = "ETYMON_TEST_SWITCH"

                        try
                            Expect.isFalse (Env.isEnabled name) "unset means off"
                            Environment.SetEnvironmentVariable(name, "1")
                            Expect.isTrue (Env.isEnabled name) "1 means on"
                            Environment.SetEnvironmentVariable(name, "nonsense")
                            Expect.isFalse (Env.isEnabled name) "unparseable means off, not a crash"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }

                    test "isSet" {
                        let name = "ETYMON_TEST_ISSET"

                        try
                            Expect.isFalse (Env.isSet name) "unset"
                            Environment.SetEnvironmentVariable(name, "x")
                            Expect.isTrue (Env.isSet name) "set"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }

                    test "all returns a case-insensitive view" {
                        let name = "ETYMON_TEST_ALL"

                        try
                            Environment.SetEnvironmentVariable(name, "value")
                            let everything = Env.all ()
                            Expect.equal (Dict.tryFind name everything) (Some "value") "exact name"

                            Expect.equal
                                (Dict.tryFind (name.ToLowerInvariant()) everything)
                                (Some "value")
                                "and lower case, on every platform"
                        finally
                            Environment.SetEnvironmentVariable(name, null)
                    }
                ]
        ]
