namespace Etymon

open System

/// <summary>
/// One difference between two snapshots.
/// </summary>
/// <remarks>
/// Kept as data rather than generated straight to SQL, so that a change can be
/// examined — is it destructive? is it reversible? — before anything decides
/// what to write.
/// </remarks>
[<RequireQualifiedAccess>]
type Change =
    /// A table that did not exist before.
    | CreateTable of table: Table
    /// A table that no longer exists. Destructive.
    | DropTable of table: Table
    /// A column added to an existing table.
    | AddColumn of table: string * column: Column
    /// A column removed from an existing table. Destructive.
    | DropColumn of table: string * column: Column
    /// A column whose type changed. Destructive when the new type is narrower.
    | AlterColumnType of table: string * column: string * before: SqlType * after: SqlType
    /// A column that stopped accepting nulls. Destructive: existing rows may
    /// hold one.
    | MakeNotNull of table: string * column: string
    /// A column that started accepting nulls.
    | MakeNullable of table: string * column: string
    /// A uniqueness rule added. Destructive: existing rows may already break it.
    | AddUnique of table: string * constraint': UniqueConstraint
    /// A uniqueness rule removed.
    | DropUnique of table: string * constraint': UniqueConstraint

/// Comparing snapshots.
[<RequireQualifiedAccess>]
module Diff =

    /// <summary>
    /// Whether applying a change can lose data or fail on data that already
    /// exists.
    /// </summary>
    /// <remarks>
    /// Etymon errs toward calling a change destructive. The cost of an
    /// unnecessary review is a minute; the cost of a silent truncation is a
    /// support ticket a month later that nobody connects to a migration.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Diff.isDestructive (Change.DropColumn("person", column)) // true
    /// Diff.isDestructive (Change.AddColumn("person", column)) // false
    /// </code></example>
    let isDestructive (change: Change) =
        match change with
        | Change.DropTable _
        | Change.DropColumn _
        | Change.MakeNotNull _
        // Adding a uniqueness rule cannot lose data, but it can fail against
        // rows that already exist, which needs the same amount of thought.
        | Change.AddUnique _ -> true
        | Change.AlterColumnType(_, _, before, after) -> SqlModel.narrows before after
        | Change.CreateTable _
        | Change.AddColumn _
        | Change.MakeNullable _
        | Change.DropUnique _ -> false

    /// <summary>What a change does, in a sentence.</summary>
    /// <example><code lang="fsharp">
    /// Diff.describe (Change.DropColumn("person", column))
    /// // "drop the column person.nickname, losing whatever it holds"
    /// </code></example>
    let describe (change: Change) =
        match change with
        | Change.CreateTable table -> $"create the table %s{table.Name}"
        | Change.DropTable table -> $"drop the table %s{table.Name}, losing every row in it"
        | Change.AddColumn(table, column) -> $"add the column %s{table}.%s{column.Name}"
        | Change.DropColumn(table, column) -> $"drop the column %s{table}.%s{column.Name}, losing whatever it holds"
        | Change.AlterColumnType(table, column, before, after) ->
            let verb = if SqlModel.narrows before after then "narrow" else "widen"
            $"%s{verb} the type of %s{table}.%s{column} from %A{before} to %A{after}"
        | Change.MakeNotNull(table, column) ->
            $"require %s{table}.%s{column} to be present, which fails if any existing row leaves it null"
        | Change.MakeNullable(table, column) -> $"allow %s{table}.%s{column} to be null"
        | Change.AddUnique(table, c) ->
            let columns = String.Join(", ", c.Columns)

            $"require %s{table} (%s{columns}) to be unique, which fails if existing rows already repeat"
        | Change.DropUnique(table, c) -> $"drop the uniqueness rule %s{c.Name} on %s{table}"

    /// <summary>
    /// The change that undoes another, where one exists.
    /// </summary>
    /// <remarks>
    /// Only changes that lose nothing can be reversed. Dropping a table is not
    /// reversible by recreating it: the rows are gone, and a script that
    /// pretended otherwise would be worse than one that admits it cannot help.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Diff.invert (Change.AddColumn("person", column)) // Some (DropColumn ...)
    /// Diff.invert (Change.DropTable table) // None -- the rows are gone
    /// </code></example>
    let invert (change: Change) =
        match change with
        | Change.CreateTable table -> Some(Change.DropTable table)
        | Change.AddColumn(table, column) -> Some(Change.DropColumn(table, column))
        | Change.MakeNullable(table, column) -> Some(Change.MakeNotNull(table, column))
        | Change.DropUnique(table, c) -> Some(Change.AddUnique(table, c))
        | Change.AlterColumnType(table, column, before, after) ->
            Some(Change.AlterColumnType(table, column, after, before))
        // Everything below this line destroyed something. Recreating the shape
        // does not bring back what was in it.
        | Change.DropTable _
        | Change.DropColumn _
        | Change.MakeNotNull _
        | Change.AddUnique _ -> None

    let private sameName (a: string) (b: string) =
        String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

    /// <summary>
    /// Every difference between two snapshots, in an order that can be applied.
    /// </summary>
    /// <remarks>
    /// Tables are created before columns are added to them, and dropped last, so
    /// that the list can be executed from top to bottom without reordering.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Diff.plan previousSnapshot currentSnapshot
    /// </code></example>
    let plan (before: Snapshot) (after: Snapshot) : Change list =
        let created =
            after.Tables
            |> List.filter (fun t -> (SqlModel.tryTable t.Name before).IsNone)
            |> List.map Change.CreateTable

        let dropped =
            before.Tables
            |> List.filter (fun t -> (SqlModel.tryTable t.Name after).IsNone)
            |> List.map Change.DropTable

        let altered =
            after.Tables
            |> List.collect (fun afterTable ->
                match SqlModel.tryTable afterTable.Name before with
                | None -> []
                | Some beforeTable ->
                    let addedColumns =
                        afterTable.Columns
                        |> List.filter (fun c -> (SqlModel.tryColumn c.Name beforeTable).IsNone)
                        |> List.map (fun c -> Change.AddColumn(afterTable.Name, c))

                    let droppedColumns =
                        beforeTable.Columns
                        |> List.filter (fun c -> (SqlModel.tryColumn c.Name afterTable).IsNone)
                        |> List.map (fun c -> Change.DropColumn(afterTable.Name, c))

                    let changedColumns =
                        afterTable.Columns
                        |> List.collect (fun afterColumn ->
                            match SqlModel.tryColumn afterColumn.Name beforeTable with
                            | None -> []
                            | Some beforeColumn ->
                                [
                                    if beforeColumn.Type <> afterColumn.Type then
                                        Change.AlterColumnType(
                                            afterTable.Name,
                                            afterColumn.Name,
                                            beforeColumn.Type,
                                            afterColumn.Type
                                        )
                                    if beforeColumn.Nullable && not afterColumn.Nullable then
                                        Change.MakeNotNull(afterTable.Name, afterColumn.Name)
                                    if not beforeColumn.Nullable && afterColumn.Nullable then
                                        Change.MakeNullable(afterTable.Name, afterColumn.Name)
                                ]
                        )

                    let addedUnique =
                        afterTable.Unique
                        |> List.filter (fun u ->
                            beforeTable.Unique |> List.exists (fun b -> sameName b.Name u.Name) |> not
                        )
                        |> List.map (fun u -> Change.AddUnique(afterTable.Name, u))

                    let droppedUnique =
                        beforeTable.Unique
                        |> List.filter (fun u ->
                            afterTable.Unique |> List.exists (fun a -> sameName a.Name u.Name) |> not
                        )
                        |> List.map (fun u -> Change.DropUnique(afterTable.Name, u))

                    addedColumns @ changedColumns @ addedUnique @ droppedUnique @ droppedColumns
            )

        created @ altered @ dropped
