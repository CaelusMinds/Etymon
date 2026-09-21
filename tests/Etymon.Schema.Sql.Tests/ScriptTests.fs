module Etymon.Schema.Sql.Tests.ScriptTests

open Expecto
open Microsoft.Data.Sqlite
open Etymon
open Etymon.Tests
open Etymon.Schema.Sql.Tests.Domain

/// Runs SQL against a real in-memory SQLite database. A generated statement that
/// nobody executes is a string that looks like SQL.
let private runOnSqlite (statements: string) =
    // Not `use`: the caller needs the connection alive to query what was built,
    // and an in-memory database vanishes the moment its connection closes.
    let connection = new SqliteConnection("Data Source=:memory:")
    connection.Open()

    for statement in statements.Split ';' do
        let trimmed = statement.Trim()

        // Comments are how destructive statements are emitted by default, and
        // BEGIN/COMMIT are handled by the connection.
        let meaningful =
            trimmed.Split '\n'
            |> Array.filter (fun line -> not (line.TrimStart().StartsWith "--"))
            |> String.concat "\n"
            |> fun s -> s.Trim()

        if meaningful <> "" && meaningful <> "BEGIN" && meaningful <> "COMMIT" then
            use command = connection.CreateCommand()
            command.CommandText <- meaningful
            command.ExecuteNonQuery() |> ignore

    connection

let private widened =
    { personTable with
        Columns =
            personTable.Columns
            |> List.map (fun c ->
                if c.Name = "name" then
                    { c with Type = SqlType.Text }
                else
                    c
            )
    }

/// The same table with a tightened rule on `name`, and nothing else changed.
let private tightened =
    { personTable with
        Columns =
            personTable.Columns
            |> List.map (fun c ->
                if c.Name = "name" then
                    { c with
                        Constraints = [ Constraint.Length(Some 1, Some 40) ]
                    }
                else
                    c
            )
    }

let private withNickname =
    { personTable with
        Columns =
            personTable.Columns
            @ [
                {
                    Name = "nickname"
                    Type = SqlType.Text
                    Nullable = true
                    Default = None
                    Constraints = []
                }
            ]
    }

let tests =
    testList
        "Script"
        [
            testList
                "diffing"
                [
                    test "a new table is a create" {
                        let changes = Diff.plan SqlModel.emptySnapshot personSnapshot

                        match changes with
                        | [ Change.CreateTable table ] -> Expect.equal table.Name "Person" "the table"
                        | other -> failtestf "expected one create, got %A" other
                    }

                    test "a removed table is a drop, and it is destructive" {
                        let changes = Diff.plan personSnapshot SqlModel.emptySnapshot

                        match changes with
                        | [ Change.DropTable _ as change ] ->
                            Expect.isTrue (Diff.isDestructive change) "the rows go with it"
                            Expect.equal (Diff.invert change) None "and recreating the shape does not bring them back"
                        | other -> failtestf "expected one drop, got %A" other
                    }

                    test "an added column is not destructive, and is reversible" {
                        let changes = Diff.plan personSnapshot (SqlModel.snapshotOf [ withNickname ])

                        match changes with
                        | [ Change.AddColumn(_, column) as change ] ->
                            Expect.equal column.Name "nickname" "the column"
                            Expect.isFalse (Diff.isDestructive change) "nothing is lost"
                            Expect.isSome (Diff.invert change) "and it can be undone"
                        | other -> failtestf "expected one add, got %A" other
                    }

                    test "a dropped column is destructive" {
                        let changes = Diff.plan (SqlModel.snapshotOf [ withNickname ]) personSnapshot

                        match changes with
                        | [ Change.DropColumn _ as change ] ->
                            Expect.isTrue (Diff.isDestructive change) "whatever it held is gone"
                            Expect.equal (Diff.invert change) None "and cannot be restored"
                        | other -> failtestf "expected one drop, got %A" other
                    }

                    test "widening a type is safe; narrowing is not" {
                        let widening = Diff.plan personSnapshot (SqlModel.snapshotOf [ widened ])
                        let narrowing = Diff.plan (SqlModel.snapshotOf [ widened ]) personSnapshot

                        Expect.isFalse (Diff.isDestructive widening.Head) "varchar(100) to text loses nothing"
                        Expect.isTrue (Diff.isDestructive narrowing.Head) "text to varchar(100) can truncate"
                    }

                    test "no change is no changes" {
                        Expect.equal (Diff.plan personSnapshot personSnapshot) [] "nothing to do"
                    }

                    test "a changed rule is reported even though it cannot be written" {
                        // The hazard this exists for: a tightened CHECK used to
                        // produce no change at all. The script looked finished,
                        // the reviewer approved it, and the database went on
                        // enforcing the old rule. Silence was the failure mode.
                        let changes = Diff.plan personSnapshot (SqlModel.snapshotOf [ tightened ])

                        match changes with
                        | [ Change.ConstraintsDiffer(table, column, before, after) as change ] ->
                            Expect.equal table "Person" "the table is named"
                            Expect.equal column "name" "and the column"
                            Expect.equal before [ Constraint.Length(Some 1, Some 100) ] "the rule it had"
                            Expect.equal after [ Constraint.Length(Some 1, Some 40) ] "and the tighter one it has"
                            Expect.isFalse (Diff.isDestructive change) "nothing is emitted, so nothing is lost"
                        | other -> failtestf "expected the difference to be reported, got %A" other
                    }

                    test "the script refuses in writing rather than omitting it" {
                        let migration =
                            Migrations.between Dialect.postgres personSnapshot (SqlModel.snapshotOf [ tightened ])

                        Expect.stringContains migration.Up "NOT GENERATED" "the script says it did not write it"
                        Expect.stringContains migration.Up "Person.name" "and names what it skipped"

                        Expect.isNonEmpty
                            migration.Unsupported
                            "and it is listed, so a caller can fail a build on it rather than read for it"
                    }

                    test "the down script refuses the same way as the up script" {
                        // A constraint change is reversible in principle, so it
                        // must not silently vanish from the down script either.
                        let migration =
                            Migrations.between Dialect.postgres personSnapshot (SqlModel.snapshotOf [ tightened ])

                        match migration.Down with
                        | Some down -> Expect.stringContains down "NOT GENERATED" "the same refusal, going back"
                        | None -> failtest "a constraint difference should not make the migration irreversible"
                    }

                    test "tables are created before they are altered and dropped last" {
                        // So the list can be executed top to bottom without
                        // reordering.
                        let before = SqlModel.snapshotOf [ personTable ]

                        let after =
                            SqlModel.snapshotOf [ withNickname; { personTable with Name = "Other" } ]

                        let changes = Diff.plan before after

                        match changes with
                        | Change.CreateTable _ :: rest ->
                            Expect.isTrue
                                (rest
                                 |> List.forall (fun c ->
                                     match c with
                                     | Change.CreateTable _ -> false
                                     | _ -> true
                                 ))
                                "creates first"
                        | other -> failtestf "expected a create first, got %A" other
                    }
                ]

            testList
                "destructive changes are flagged, not emitted"
                [
                    test "by default a drop is commented out" {
                        // The failure this package exists to prevent is a destructive
                        // statement that ran because nobody read it.
                        let changes = Diff.plan (SqlModel.snapshotOf [ withNickname ]) personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes

                        Expect.stringContains migration.Up "-- DESTRUCTIVE" "flagged"
                        Expect.stringContains migration.Up "-- ALTER TABLE" "and commented out"

                        Expect.isFalse
                            (migration.Up.Split '\n'
                             |> Array.exists (fun l -> l.TrimStart().StartsWith "ALTER TABLE"))
                            "no live destructive statement anywhere"

                        Expect.equal (List.length migration.Destructive) 1 "and listed for a reviewer"
                    }

                    test "opting in emits them" {
                        let changes = Diff.plan (SqlModel.snapshotOf [ withNickname ]) personSnapshot

                        let migration =
                            Migrations.script
                                { Migrations.scriptOptions with
                                    IncludeDestructive = true
                                }
                                Dialect.postgres
                                changes

                        Expect.isTrue
                            (migration.Up.Split '\n'
                             |> Array.exists (fun l -> l.TrimStart().StartsWith "ALTER TABLE"))
                            "now it is real SQL"
                    }

                    test "every change is preceded by a plain-English comment" {
                        let changes = Diff.plan SqlModel.emptySnapshot personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes

                        Expect.stringContains
                            migration.Up
                            "-- create the table Person"
                            "a migration is read more often than it is written"
                    }
                ]

            testList
                "down migrations"
                [
                    test "a reversible change gets one" {
                        let changes = Diff.plan personSnapshot (SqlModel.snapshotOf [ withNickname ])
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes

                        match migration.Down with
                        | Some down -> Expect.stringContains down "DROP COLUMN" "the inverse"
                        | None -> failtest "adding a column is reversible"
                    }

                    test "an irreversible change means no down migration at all" {
                        // A partial down migration is a trap.
                        let changes = Diff.plan personSnapshot SqlModel.emptySnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes
                        Expect.equal migration.Down None "the rows are gone; nothing can restore them"
                    }
                ]

            testList
                "dialects refuse what they cannot do"
                [
                    test "SQLite will not fake a type change" {
                        // It needs a twelve-step table rebuild that depends on data
                        // Etymon has never seen.
                        let changes = Diff.plan personSnapshot (SqlModel.snapshotOf [ widened ])
                        let migration = Migrations.script Migrations.scriptOptions Dialect.sqlite changes

                        Expect.equal (List.length migration.Unsupported) 1 "reported"
                        Expect.stringContains migration.Up "-- NOT GENERATED" "and said so in the script"

                        Expect.stringContains
                            (snd migration.Unsupported.Head)
                            "rebuilding the table"
                            "with what it would actually take"
                    }

                    test "PostgreSQL can, and does" {
                        let changes = Diff.plan personSnapshot (SqlModel.snapshotOf [ widened ])
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes

                        Expect.equal migration.Unsupported [] "nothing refused"
                        Expect.stringContains migration.Up "ALTER COLUMN" "it just does it"
                    }

                    test "a NOT NULL column with no default is refused, in any dialect" {
                        // Because it fails on any table that already has rows.
                        let withRequired =
                            { personTable with
                                Columns =
                                    personTable.Columns
                                    @ [
                                        {
                                            Name = "department"
                                            Type = SqlType.Text
                                            Nullable = false
                                            Default = None
                                            Constraints = []
                                        }
                                    ]
                            }

                        let changes = Diff.plan personSnapshot (SqlModel.snapshotOf [ withRequired ])
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes

                        Expect.equal (List.length migration.Unsupported) 1 "refused"

                        Expect.stringContains
                            (snd migration.Unsupported.Head)
                            "needs a default"
                            "and said what to do instead"
                    }

                    test "a pattern becomes a CHECK in PostgreSQL but not SQLite" {
                        let zipColumn =
                            {
                                Name = "zip"
                                Type = SqlType.VarChar 5
                                Nullable = false
                                Default = None
                                Constraints = [ Constraint.Pattern @"^\d{5}$" ]
                            }

                        let table =
                            { personTable with
                                Columns = [ zipColumn ]
                                PrimaryKey = [ "zip" ]
                            }

                        let changes = [ Change.CreateTable table ]

                        let pg = Migrations.script Migrations.scriptOptions Dialect.postgres changes
                        let lite = Migrations.script Migrations.scriptOptions Dialect.sqlite changes

                        Expect.stringContains pg.Up "~" "PostgreSQL has a regex operator"
                        Expect.isFalse (lite.Up.Contains "~") "SQLite does not, so the rule stays documented only"
                    }
                ]

            testList
                "the SQL actually runs"
                [
                    test "a generated CREATE TABLE executes on SQLite" {
                        // A generated statement nobody executes is a string that
                        // looks like SQL.
                        let changes = Diff.plan SqlModel.emptySnapshot personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.sqlite changes
                        use connection = runOnSqlite migration.Up

                        use command = connection.CreateCommand()
                        command.CommandText <- "SELECT name FROM pragma_table_info('Person') ORDER BY cid"
                        use reader = command.ExecuteReader()

                        let columns =
                            [
                                while reader.Read() do
                                    yield reader.GetString 0
                            ]

                        Expect.equal columns [ "id"; "name"; "age"; "email" ] "the table SQLite actually made"
                    }

                    test "the constraints are real" {
                        let changes = Diff.plan SqlModel.emptySnapshot personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.sqlite changes
                        use connection = runOnSqlite migration.Up

                        let insert (id: string) (name: string) (age: int) =
                            use command = connection.CreateCommand()

                            command.CommandText <-
                                $"INSERT INTO \"Person\" (id, name, age) VALUES ('{id}', '{name}', {age})"

                            command.ExecuteNonQuery() |> ignore

                        insert "1" "Ada" 36

                        Expect.throws
                            (fun () -> insert "2" "Bob" 200)
                            "the age range from the schema is enforced by the database"

                        Expect.throws (fun () -> insert "3" "" 30) "and so is the length rule"
                    }

                    test "an added column executes too" {
                        let create =
                            Migrations.script
                                Migrations.scriptOptions
                                Dialect.sqlite
                                (Diff.plan SqlModel.emptySnapshot personSnapshot)

                        let alter =
                            Migrations.script
                                Migrations.scriptOptions
                                Dialect.sqlite
                                (Diff.plan personSnapshot (SqlModel.snapshotOf [ withNickname ]))

                        use connection = runOnSqlite (create.Up + "\n" + alter.Up)

                        use command = connection.CreateCommand()

                        command.CommandText <-
                            "SELECT count(*) FROM pragma_table_info('Person') WHERE name = 'nickname'"

                        Expect.equal (command.ExecuteScalar() :?> int64) 1L "the column is there"
                    }
                ]

            testList
                "snapshots on disk"
                [
                    test "a snapshot round-trips through its own schema" {
                        // Etymon describes its own migration format with its own
                        // schema type, so the format is validated and round-trip
                        // tested by the same machinery as everything else.
                        let text = Migrations.toJson personSnapshot

                        match Migrations.fromJson text with
                        | Ok restored -> Expect.equal restored personSnapshot "identical"
                        | Error e -> failtestf "should have parsed: %s" (ValidationErrors.format e)
                    }

                    test "a malformed snapshot is reported with a path" {
                        let v = Migrations.fromJson """{"formatVersion":1,"tables":[{"name":"x"}]}"""

                        match Validation.errorList v with
                        | errors ->
                            Expect.isNonEmpty errors "rejected"

                            Expect.isTrue
                                (errors |> List.exists (fun e -> (Path.toString e.Path).StartsWith "tables[0]"))
                                "with the path to the problem, not a stack trace"
                    }

                    test "snapshot JSON is stable" {
                        Expect.equal
                            (Migrations.toJson personSnapshot)
                            (Migrations.toJson personSnapshot)
                            "deterministic"
                    }
                ]

            testList
                "snapshots of generated SQL"
                [
                    test "creating a table in PostgreSQL" {
                        let changes = Diff.plan SqlModel.emptySnapshot personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes
                        Snapshot.verify "create-table-postgres" migration.Up
                    }

                    test "creating a table in SQLite" {
                        let changes = Diff.plan SqlModel.emptySnapshot personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.sqlite changes
                        Snapshot.verify "create-table-sqlite" migration.Up
                    }

                    test "a destructive change, commented out" {
                        let changes = Diff.plan (SqlModel.snapshotOf [ withNickname ]) personSnapshot
                        let migration = Migrations.script Migrations.scriptOptions Dialect.postgres changes
                        Snapshot.verify "destructive-postgres" migration.Up
                    }

                    test "the snapshot file format" {
                        Snapshot.verify "snapshot-file" (Migrations.toJson personSnapshot)
                    }
                ]
        ]
