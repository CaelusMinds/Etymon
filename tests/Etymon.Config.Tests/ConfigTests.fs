module Etymon.Config.Tests.ConfigTests

open System
open Expecto
open Etymon

// The shape of a real application's settings: nested, with a mix of required
// and defaulted values, a constrained number, and a secret.
[<NoComparison>]
type Database =
    {
        Host: string
        Port: int
        Password: Secret<string>
    }

[<NoComparison>]
type Settings =
    {
        Database: Database
        ServiceName: string
        Retries: int
        Verbose: bool
    }

module Settings =
    let databaseSchema =
        Schema.object "Database" {
            let! host = Schema.required "host" Schema.string (fun d -> d.Host)

            and! port =
                Schema.required
                    "port"
                    (Schema.int |> Schema.constrain (Check.intRange (Some 1) (Some 65535)))
                    (fun d -> d.Port)

            and! password =
                Schema.required
                    "password"
                    (Schema.string |> Schema.sensitive |> Schema.convert Secret.create Secret.reveal)
                    (fun d -> d.Password)

            return
                {
                    Host = host
                    Port = port
                    Password = password
                }
        }

    let schema =
        Schema.object "Settings" {
            let! database = Schema.required "database" databaseSchema (fun s -> s.Database)

            and! serviceName =
                Schema.required "serviceName" Schema.string (fun s -> s.ServiceName)

            and! retries = Schema.defaulted "retries" Schema.int 3 (fun s -> s.Retries)
            and! verbose = Schema.defaulted "verbose" Schema.bool false (fun s -> s.Verbose)

            return
                {
                    Database = database
                    ServiceName = serviceName
                    Retries = retries
                    Verbose = verbose
                }
        }

let private complete =
    Source.inMemory
        "defaults"
        [
            "database__host", "db.internal"
            "database__port", "5432"
            "database__password", "hunter2"
            "serviceName", "billing"
        ]

let private messagesOf (v: Validation<'T>) =
    v |> Validation.errorList |> List.map (fun e -> e.ToString())

/// Runs a body with environment variables set, and removes them afterwards.
let private withEnv (values: (string * string) list) (body: unit -> unit) =
    try
        for name, value in values do
            Environment.SetEnvironmentVariable(name, value)

        body ()
    finally
        for name, _ in values do
            Environment.SetEnvironmentVariable(name, null)

let tests =
    testList
        "Config"
        [
            testList
                "loading"
                [
                    test "a complete configuration loads" {
                        match Config.load Settings.schema [ complete ] with
                        | Ok settings ->
                            Expect.equal settings.Database.Host "db.internal" "nested value"
                            Expect.equal settings.Database.Port 5432 "a string became a number"
                            Expect.equal settings.ServiceName "billing" "top-level value"
                            Expect.equal settings.Retries 3 "the default applied"
                            Expect.isFalse settings.Verbose "and so did this one"
                        | Error e -> failtestf "should have loaded: %s" (Config.report e)
                    }

                    test "everything is a string, and coercion handles it" {
                        // The reason Config decodes in coercing mode at all.
                        let source =
                            Source.inMemory
                                "env-like"
                                [
                                    "database__host", "h"
                                    "database__port", "5432"
                                    "database__password", "p"
                                    "serviceName", "s"
                                    "retries", "7"
                                    "verbose", "yes"
                                ]

                        match Config.load Settings.schema [ source ] with
                        | Ok settings ->
                            Expect.equal settings.Retries 7 "a quoted number"
                            Expect.isTrue settings.Verbose "and 'yes' is a boolean, as it is everywhere but JSON"
                        | Error e -> failtestf "should have loaded: %s" (Config.report e)
                    }

                    test "coercion still refuses nonsense" {
                        let source =
                            Source.inMemory
                                "bad"
                                [
                                    "database__host", "h"
                                    "database__port", "eighty"
                                    "database__password", "p"
                                    "serviceName", "s"
                                ]

                        Expect.isTrue
                            (Validation.isError (Config.load Settings.schema [ source ]))
                            "'eighty' is not a port"
                    }
                ]

            testList
                "reporting every problem at once"
                [
                    test "a missing setting says what it needs and where it looked" {
                        let partial = Source.inMemory "defaults" [ "database__host", "h" ]
                        let v = Config.load Settings.schema [ partial ]

                        Expect.equal
                            (messagesOf v)
                            [
                                "database.port: is required, and must be an integer (searched: defaults)"
                                "database.password: is required, and must be a string (searched: defaults). The value is not shown because this setting is sensitive."
                                "serviceName: is required, and must be a string (searched: defaults)"
                            ]
                            "three missing settings, three lines, each actionable"
                    }

                    test "a bad value says which source supplied it" {
                        // The bug that costs an afternoon is not a wrong value but
                        // the wrong source winning.
                        let file =
                            Source.inMemory
                                "appsettings.json"
                                [
                                    "database__host", "h"
                                    "database__password", "p"
                                    "serviceName", "s"
                                    "database__port", "5432"
                                ]

                        let env = Source.inMemory "environment" [ "database__port", "not-a-port" ]

                        let v = Config.load Settings.schema [ file; env ]

                        Expect.equal
                            (messagesOf v)
                            [ "database.port: expected an integer (from environment)" ]
                            "naming the source is the whole point"
                    }

                    test "an unreadable source is a problem like any other" {
                        let missing = Source.jsonFile "definitely-not-here.json"
                        let v = Config.load Settings.schema [ missing ]
                        let messages = messagesOf v

                        Expect.isTrue
                            (messages |> List.exists (fun m -> m.Contains "could not be read"))
                            "reported rather than thrown"

                        Expect.isTrue
                            (messages |> List.exists (fun m -> m.Contains "serviceName"))
                            "and alongside the settings it could not supply"
                    }

                    test "an optional file that is absent is not a problem" {
                        match Config.load Settings.schema [ complete; Source.optionalJsonFile "not-here.json" ] with
                        | Ok _ -> ()
                        | Error e -> failtestf "absence is allowed: %s" (Config.report e)
                    }

                    test "an optional file that exists but does not parse still is" {
                        // Silently ignoring it is how a deployment runs for a week on
                        // defaults nobody meant.
                        let path = IO.Path.Combine(IO.Path.GetTempPath(), $"etymon-{Guid.NewGuid():N}.json")

                        try
                            IO.File.WriteAllText(path, "{ not json")
                            let v = Config.load Settings.schema [ complete; Source.optionalJsonFile path ]

                            Expect.isTrue
                                (Validation.isError v)
                                "a malformed file is a problem even when the file was optional"
                        finally
                            IO.File.Delete path
                    }

                    test "report renders one problem per line with a count" {
                        let v = Config.load Settings.schema [ Source.inMemory "s" [] ]

                        match v with
                        | Error errors ->
                            let rendered = Config.report errors
                            Expect.stringContains rendered "configuration problems:" "a heading"
                            Expect.isGreaterThan (rendered.Split('\n').Length) 2 "and a line each"
                        | Ok _ -> failtest "should have failed"
                    }
                ]

            testList
                "precedence"
                [
                    test "the last source that holds a setting wins" {
                        let file =
                            Source.inMemory
                                "appsettings.json"
                                [
                                    "database__host", "from-file"
                                    "database__port", "1"
                                    "database__password", "p"
                                    "serviceName", "s"
                                ]

                        let env = Source.inMemory "environment" [ "database__host", "from-env" ]

                        match Config.load Settings.schema [ file; env ] with
                        | Ok settings -> Expect.equal settings.Database.Host "from-env" "the overlay wins"
                        | Error e -> failtestf "should have loaded: %s" (Config.report e)
                    }

                    test "a source that does not hold a setting does not erase it" {
                        let file =
                            Source.inMemory
                                "appsettings.json"
                                [
                                    "database__host", "from-file"
                                    "database__port", "1"
                                    "database__password", "p"
                                    "serviceName", "s"
                                ]

                        let env = Source.inMemory "environment" [ "retries", "9" ]

                        match Config.load Settings.schema [ file; env ] with
                        | Ok settings ->
                            Expect.equal settings.Database.Host "from-file" "kept"
                            Expect.equal settings.Retries 9 "and the overlay added its own"
                        | Error e -> failtestf "should have loaded: %s" (Config.report e)
                    }
                ]

            testList
                "environment variables"
                [
                    test "the prefix is required and stripped" {
                        withEnv
                            [
                                "ETYTEST__DATABASE__HOST", "env-host"
                                "ETYTEST__DATABASE__PORT", "5432"
                                "ETYTEST__DATABASE__PASSWORD", "secret"
                                "ETYTEST__SERVICENAME", "svc"
                            ]
                            (fun () ->
                                match Config.load Settings.schema [ Source.environment "ETYTEST" ] with
                                | Ok settings ->
                                    Expect.equal settings.Database.Host "env-host" "nested through __"
                                    Expect.equal settings.ServiceName "svc" "and case does not matter"
                                | Error e -> failtestf "should have loaded: %s" (Config.report e)
                            )
                    }

                    test "variables outside the prefix are ignored" {
                        // Without a prefix, PATH starts meaning something.
                        withEnv
                            [
                                "ETYTEST__DATABASE__HOST", "env-host"
                                "ETYTEST__DATABASE__PORT", "5432"
                                "ETYTEST__DATABASE__PASSWORD", "secret"
                                "ETYTEST__SERVICENAME", "svc"
                                "SOMETHINGELSE__SERVICENAME", "wrong"
                            ]
                            (fun () ->
                                match Config.load Settings.schema [ Source.environment "ETYTEST" ] with
                                | Ok settings ->
                                    Expect.equal settings.ServiceName "svc" "the other prefix was not read"
                                | Error e -> failtestf "should have loaded: %s" (Config.report e)
                            )
                    }
                ]

            testList
                "secrets"
                [
                    test "a sensitive value never appears in an error" {
                        let source =
                            Source.inMemory
                                "s"
                                [
                                    "database__host", "h"
                                    "database__port", "1"
                                    "database__password", "hunter2"
                                ]

                        let v = Config.load Settings.schema [ source ]
                        let rendered = String.Join("\n", messagesOf v)
                        Expect.isFalse (rendered.Contains "hunter2") "the value is not echoed back"
                    }

                    test "a sensitive value is redacted in the explanation" {
                        let rendered = Config.explain Settings.schema [ complete ]
                        Expect.isFalse (rendered.Contains "hunter2") "not shown"
                        Expect.stringContains rendered "<redacted>" "shown as redacted"
                        Expect.stringContains rendered "db.internal" "while ordinary values are shown"
                    }

                    test "a loaded secret does not print itself" {
                        match Config.load Settings.schema [ complete ] with
                        | Ok settings ->
                            let rendered = sprintf "%A" settings
                            Expect.isFalse (rendered.Contains "hunter2") "printing the whole record is safe"
                            Expect.equal (Secret.reveal settings.Database.Password) "hunter2" "but the value is usable"
                        | Error e -> failtestf "should have loaded: %s" (Config.report e)
                    }
                ]

            testList
                "explaining what was loaded"
                [
                    test "every expected setting appears, with its source" {
                        let file =
                            Source.inMemory
                                "appsettings.json"
                                [
                                    "database__host", "h"
                                    "database__port", "1"
                                    "database__password", "p"
                                    "serviceName", "s"
                                ]

                        let env = Source.inMemory "environment" [ "database__host", "overridden" ]

                        let rows = Config.settings Settings.schema [ file; env ]

                        Expect.equal
                            (rows |> List.map (fun r -> r.Path))
                            [
                                "database.host"
                                "database.port"
                                "database.password"
                                "serviceName"
                                "retries"
                                "verbose"
                            ]
                            "every leaf the schema expects, in declaration order"

                        let host = rows |> List.find (fun r -> r.Path = "database.host")
                        Expect.equal host.Source (Some "environment") "which source won"

                        let port = rows |> List.find (fun r -> r.Path = "database.port")
                        Expect.equal port.Source (Some "appsettings.json") "and which did not"
                    }

                    test "a setting nothing supplied says so" {
                        let rows = Config.settings Settings.schema [ complete ]
                        let retries = rows |> List.find (fun r -> r.Path = "retries")
                        Expect.equal retries.Source None "nothing supplied it"
                        Expect.equal retries.Value "(not set)" "so the schema default applies"
                    }
                ]
        ]
