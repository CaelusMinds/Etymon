module Etymon.Schema.Sql.Tests.MappingTests

open Expecto
open Etymon
open Etymon.Schema.Sql.Tests.Domain

let tests =
    testList
        "Mapping"
        [
            testList
                "deriving a table"
                [
                    test "a flat record becomes a table" {
                        match Mapping.tableOf (Mapping.keyedBy "id") Person.flatSchema with
                        | Ok table ->
                            Expect.equal table.Name "Person" "named after the schema"

                            Expect.equal
                                (table.Columns |> List.map (fun c -> c.Name))
                                [ "id"; "name"; "age"; "email" ]
                                "one column per field, in declaration order"

                            Expect.equal table.PrimaryKey [ "id" ] "the key that was given"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "a length constraint becomes a bounded type" {
                        match Mapping.tableOf (Mapping.keyedBy "id") Person.flatSchema with
                        | Ok table ->
                            let name = table.Columns |> List.find (fun c -> c.Name = "name")

                            Expect.equal
                                name.Type
                                (SqlType.VarChar 100)
                                "the rule the schema already carries becomes one the database enforces"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "optional fields become nullable columns" {
                        match Mapping.tableOf (Mapping.keyedBy "id") Person.flatSchema with
                        | Ok table ->
                            let email = table.Columns |> List.find (fun c -> c.Name = "email")
                            let name = table.Columns |> List.find (fun c -> c.Name = "name")
                            Expect.isTrue email.Nullable "an optional field"
                            Expect.isFalse name.Nullable "a required one"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "a key column is never nullable, whatever the schema said" {
                        let opts =
                            { Mapping.options with
                                PrimaryKey = [ "email" ]
                            }

                        match Mapping.tableOf opts Person.flatSchema with
                        | Ok table ->
                            let email = table.Columns |> List.find (fun c -> c.Name = "email")
                            Expect.isFalse email.Nullable "a key cannot be null"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "the table can be named explicitly" {
                        let opts =
                            { Mapping.keyedBy "id" with
                                TableName = Some "people"
                            }

                        match Mapping.tableOf opts Person.flatSchema with
                        | Ok table -> Expect.equal table.Name "people" "the given name wins"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "a type can be overridden" {
                        let opts =
                            { Mapping.keyedBy "id" with
                                TypeOverrides = Map.ofList [ "age", SqlType.BigInt ]
                            }

                        match Mapping.tableOf opts Person.flatSchema with
                        | Ok table ->
                            let age = table.Columns |> List.find (fun c -> c.Name = "age")
                            Expect.equal age.Type SqlType.BigInt "the override wins over the derived type"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }
                ]

            testList
                "ambiguity is an error, not a guess"
                [
                    test "a nested object with no strategy is refused" {
                        // The heart of this package's design. A nested address could
                        // be columns, JSON, or another table with a foreign key, and
                        // each is correct for some application.
                        match Mapping.tableOf (Mapping.keyedBy "id") Person.nestedSchema with
                        | Ok _ -> failtest "should have refused to guess"
                        | Error problems ->
                            Expect.equal problems [ MappingError.NestedNotDecided "address" ] "named the field"

                            Expect.stringContains
                                (Mapping.describe problems.Head)
                                "flattened into columns, stored as JSON, or omitted"
                                "and said what the options are"
                    }

                    test "a list with no strategy is refused" {
                        match Mapping.tableOf (Mapping.keyedBy "id") Person.taggedSchema with
                        | Ok _ -> failtest "should have refused to guess"
                        | Error problems ->
                            Expect.equal problems [ MappingError.CollectionNotDecided "tags" ] "named it"
                    }

                    test "no primary key is refused" {
                        match Mapping.tableOf Mapping.options Person.flatSchema with
                        | Ok _ -> failtest "a table without a key is a problem worth being told about"
                        | Error problems -> Expect.equal problems [ MappingError.NoPrimaryKey "Person" ] "said so"
                    }

                    test "a key naming a column that does not exist is refused" {
                        match Mapping.tableOf (Mapping.keyedBy "nope") Person.flatSchema with
                        | Ok _ -> failtest "should have refused"
                        | Error problems ->
                            Expect.equal problems [ MappingError.UnknownKeyColumn("Person", "nope") ] "named the column"
                    }

                    test "every problem is reported at once" {
                        // Not one, then another after the first is fixed.
                        match Mapping.tableOf Mapping.options Person.fullSchema with
                        | Ok _ -> failtest "should have refused"
                        | Error problems -> Expect.equal (List.length problems) 3 "nested, collection, and no key"
                    }

                    test "a schema that is not a record is refused" {
                        match Mapping.tableOf (Mapping.keyedBy "id") Schema.int with
                        | Ok _ -> failtest "a table cannot be made from a single value"
                        | Error problems -> Expect.equal problems [ MappingError.NotATable "a single value" ] "said why"
                    }
                ]

            testList
                "deciding"
                [
                    test "flattening gives each leaf its own column" {
                        let opts =
                            { Mapping.keyedBy "id" with
                                Nested = Some(NestedStrategy.Flatten "_")
                            }

                        match Mapping.tableOf opts Person.nestedSchema with
                        | Ok table ->
                            Expect.equal
                                (table.Columns |> List.map (fun c -> c.Name))
                                [ "id"; "name"; "address_street"; "address_zip" ]
                                "prefixed with the parent"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "a field inside an optional parent is nullable" {
                        // There is no row shape where a field is required but its
                        // parent is absent.
                        let opts =
                            { Mapping.keyedBy "id" with
                                Nested = Some(NestedStrategy.Flatten "_")
                            }

                        match Mapping.tableOf opts Person.optionalNestedSchema with
                        | Ok table ->
                            let street = table.Columns |> List.find (fun c -> c.Name = "address_street")
                            Expect.isTrue street.Nullable "required inside an optional parent is still optional"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "JSON gives the nested object one column" {
                        let opts =
                            { Mapping.keyedBy "id" with
                                Nested = Some NestedStrategy.AsJson
                            }

                        match Mapping.tableOf opts Person.nestedSchema with
                        | Ok table ->
                            let address = table.Columns |> List.find (fun c -> c.Name = "address")
                            Expect.equal address.Type SqlType.Json "one column, whole object"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "omitting leaves it out entirely" {
                        let opts =
                            { Mapping.keyedBy "id" with
                                Nested = Some NestedStrategy.Omit
                            }

                        match Mapping.tableOf opts Person.nestedSchema with
                        | Ok table ->
                            Expect.isFalse
                                (table.Columns |> List.exists (fun c -> c.Name.StartsWith "address"))
                                "no trace of it"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }

                    test "a list becomes a JSON column" {
                        let opts =
                            { Mapping.keyedBy "id" with
                                Collections = Some CollectionStrategy.AsJson
                            }

                        match Mapping.tableOf opts Person.taggedSchema with
                        | Ok table ->
                            let tags = table.Columns |> List.find (fun c -> c.Name = "tags")
                            Expect.equal tags.Type SqlType.Json "a JSON array"
                        | Error problems -> failtestf "should have mapped:\n%s" (Mapping.report problems)
                    }
                ]

            testList
                "narrowing"
                [
                    test "widening is safe, narrowing is not" {
                        Expect.isFalse (SqlModel.narrows SqlType.Integer SqlType.BigInt) "wider"
                        Expect.isTrue (SqlModel.narrows SqlType.BigInt SqlType.Integer) "narrower"
                        Expect.isFalse (SqlModel.narrows (SqlType.VarChar 50) (SqlType.VarChar 200)) "longer"
                        Expect.isTrue (SqlModel.narrows (SqlType.VarChar 200) (SqlType.VarChar 50)) "shorter"
                        Expect.isFalse (SqlModel.narrows (SqlType.VarChar 50) SqlType.Text) "unbounded is wider"
                        Expect.isTrue (SqlModel.narrows SqlType.Text (SqlType.VarChar 50)) "and bounding it is not"
                    }

                    test "an unrelated change counts as narrowing" {
                        // Etymon errs toward calling a change destructive: an
                        // unnecessary review costs a minute, a silent truncation
                        // costs a support ticket a month later.
                        Expect.isTrue (SqlModel.narrows SqlType.Text SqlType.Integer) "no database converts this safely"
                        Expect.isFalse (SqlModel.narrows SqlType.Text SqlType.Text) "and no change is no risk"
                    }
                ]
        ]
