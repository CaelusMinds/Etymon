namespace Etymon

open System

/// <summary>
/// What to do with a nested object inside a record being mapped to a table.
/// </summary>
/// <remarks>
/// There is no right answer, which is exactly why Etymon will not pick one. A
/// nested address could be three more columns, a JSON document, or a row in
/// another table with a foreign key — and each of those is correct for some
/// application and wrong for the rest.
/// </remarks>
[<RequireQualifiedAccess>]
type NestedStrategy =
    /// Store the nested object as a JSON document in one column.
    | AsJson
    /// Give each leaf its own column, named with the parent prefixed.
    | Flatten of separator: string
    /// Leave it out of the table entirely.
    | Omit

/// <summary>
/// What to do with a list inside a record being mapped to a table.
/// </summary>
/// <remarks>
/// Same problem as nesting, with the same answer: a list of tags could be a JSON
/// array, a delimited string or a join table, and Etymon does not know which one
/// this is.
/// </remarks>
[<RequireQualifiedAccess>]
type CollectionStrategy =
    /// Store the whole list as a JSON array in one column.
    | AsJson
    /// Leave it out of the table entirely.
    | Omit

/// <summary>
/// The decisions Etymon will not make for you.
/// </summary>
/// <remarks>
/// A serialisation schema says nothing about primary keys, foreign keys, or how
/// a nested value should be stored. Deriving a table from one therefore means
/// guessing, and a guess that is wrong often enough poisons trust in everything
/// else the suite generates. So the ambiguous decisions are inputs, and leaving
/// one out is an error rather than a default.
/// </remarks>
type TableOptions =
    {
        /// The table name. Defaults to the schema's name when absent.
        TableName: string option
        /// The columns forming the primary key. Required: Etymon will not invent
        /// one, and a table without a key is a problem worth being told about.
        PrimaryKey: string list
        /// What to do with nested objects. Required only if the schema has any.
        Nested: NestedStrategy option
        /// What to do with lists. Required only if the schema has any.
        Collections: CollectionStrategy option
        /// Column types to use instead of the derived one, by column name.
        TypeOverrides: Map<string, SqlType>
        /// Uniqueness rules, which a schema cannot express.
        Unique: UniqueConstraint list
        /// References to other tables, which a schema cannot express either.
        ForeignKeys: ForeignKey list
    }

/// Why a schema could not be turned into a table.
[<RequireQualifiedAccess>]
type MappingError =
    /// The schema has a nested object and the options did not say what to do
    /// with it.
    | NestedNotDecided of path: string
    /// The schema has a list and the options did not say what to do with it.
    | CollectionNotDecided of path: string
    /// No primary key was given.
    | NoPrimaryKey of table: string
    /// The primary key names a column the table does not have.
    | UnknownKeyColumn of table: string * column: string
    /// The schema is not a record, so there is nothing to make a table from.
    | NotATable of what: string
    /// The schema contains something no table can hold.
    | Unsupported of path: string * what: string

/// Turning a <see cref="T:Etymon.Schema`1"/> into a <see cref="T:Etymon.Table"/>.
[<RequireQualifiedAccess>]
module Mapping =

    /// <summary>
    /// Options that decide nothing. Start here and supply what your schema needs;
    /// anything left undecided that the schema actually uses is reported.
    /// </summary>
    /// <example><code lang="fsharp">
    /// { Mapping.options with PrimaryKey = [ "id" ]; Nested = Some (NestedStrategy.Flatten "_") }
    /// </code></example>
    let options =
        {
            TableName = None
            PrimaryKey = []
            Nested = None
            Collections = None
            TypeOverrides = Map.empty
            Unique = []
            ForeignKeys = []
        }

    /// <summary>Options for the common case: a flat record with one key column.</summary>
    /// <example><code lang="fsharp">
    /// Mapping.keyedBy "id"
    /// </code></example>
    let keyedBy (column: string) =
        { options with PrimaryKey = [ column ] }

    /// <summary>What went wrong, in a sentence.</summary>
    /// <example><code lang="fsharp">
    /// MappingError.NestedNotDecided "address" |> Mapping.describe
    /// // "'address' is a nested object, and the options did not say ..."
    /// </code></example>
    let describe (error: MappingError) =
        match error with
        | MappingError.NestedNotDecided path ->
            $"'%s{path}' is a nested object, and the options did not say what to do with it. It could be flattened into columns, stored as JSON, or omitted. Set TableOptions.Nested."
        | MappingError.CollectionNotDecided path ->
            $"'%s{path}' is a list, and the options did not say what to do with it. It could be stored as a JSON array or omitted; a join table has to be modelled separately. Set TableOptions.Collections."
        | MappingError.NoPrimaryKey table ->
            $"the table '%s{table}' has no primary key. Etymon will not invent one; set TableOptions.PrimaryKey."
        | MappingError.UnknownKeyColumn(table, column) ->
            $"the primary key of '%s{table}' names the column '%s{column}', which the table does not have."
        | MappingError.NotATable what -> $"a table can only be made from a record, and this schema describes %s{what}."
        | MappingError.Unsupported(path, what) -> $"'%s{path}' is %s{what}, which a table column cannot hold."

    /// <summary>Every mapping problem, one per line.</summary>
    /// <example><code lang="fsharp">
    /// match Mapping.tableOf opts schema with
    /// | Error problems -> eprintfn "%s" (Mapping.report problems)
    /// | Ok table -> ()
    /// </code></example>
    let report (errors: MappingError list) =
        errors
        |> List.map (fun e -> "  " + describe e)
        |> String.concat Environment.NewLine

    // ---- deriving a column type ----------------------------------------------

    let private lengthOf (constraints: Constraint list) =
        constraints
        |> List.tryPick (
            function
            | Constraint.Length(_, Some max) -> Some max
            | _ -> None
        )

    /// The type a leaf becomes. A string with a maximum length becomes VARCHAR of
    /// that length, so the constraint the schema already carries turns into one
    /// the database enforces rather than being restated.
    let private typeOf (info: SchemaInfo) =
        let constraints = SchemaInfo.constraints info

        match SchemaInfo.strip info with
        | SPrim PrimKind.String ->
            match lengthOf constraints with
            | Some max -> SqlType.VarChar max
            | None -> SqlType.Text
        | SPrim PrimKind.Int -> SqlType.Integer
        | SPrim PrimKind.Int64 -> SqlType.BigInt
        | SPrim PrimKind.Decimal -> SqlType.Numeric(18, 2)
        | SPrim PrimKind.Float -> SqlType.Double
        | SPrim PrimKind.Bool -> SqlType.Boolean
        | SPrim PrimKind.Guid -> SqlType.Uuid
        | SPrim PrimKind.DateTimeOffset -> SqlType.Timestamp
        | SPrim PrimKind.DateOnly -> SqlType.Date
        | SPrim PrimKind.TimeOnly -> SqlType.Time
        | SPrim PrimKind.TimeSpan -> SqlType.Interval
        | SPrim PrimKind.Bytes -> SqlType.Binary
        | _ -> SqlType.Json

    // ---- walking the schema --------------------------------------------------

    let private columnName (separator: string) (path: string list) = String.Join(separator, path)

    /// Produces the columns for one field, or the reasons it could not.
    let rec private columnsFor
        (opts: TableOptions)
        (prefix: string list)
        (required: bool)
        (info: SchemaInfo)
        : Result<Column list, MappingError list>
        =
        let path = String.Join(".", prefix)
        let stripped = SchemaInfo.strip info

        match stripped with
        | SNullable inner -> columnsFor opts prefix false inner

        | SObject _ ->
            match opts.Nested with
            | None -> Error [ MappingError.NestedNotDecided path ]
            | Some NestedStrategy.Omit -> Ok []
            | Some NestedStrategy.AsJson ->
                Ok
                    [
                        {
                            Name = columnName "_" prefix
                            Type = SqlType.Json
                            Nullable = not required
                            Default = None
                            Constraints = []
                        }
                    ]
            | Some(NestedStrategy.Flatten separator) ->
                match stripped with
                | SObject(_, fields) ->
                    let results =
                        fields
                        |> List.map (fun field ->
                            // A field inside an optional parent is itself
                            // optional: there is no row shape in which it is
                            // required but its parent is absent.
                            columnsFor opts (prefix @ [ field.Name ]) (required && field.Required) field.Schema
                        )

                    let errors =
                        results
                        |> List.collect (
                            function
                            | Error e -> e
                            | Ok _ -> []
                        )

                    if List.isEmpty errors then
                        Ok(
                            results
                            |> List.collect (
                                function
                                | Ok columns -> columns
                                | Error _ -> []
                            )
                            |> List.map (fun column ->
                                { column with
                                    Name = column.Name.Replace(".", separator)
                                }
                            )
                        )
                    else
                        Error errors
                | _ -> Ok []

        | SList _
        | SMap _ ->
            match opts.Collections with
            | None -> Error [ MappingError.CollectionNotDecided path ]
            | Some CollectionStrategy.Omit -> Ok []
            | Some CollectionStrategy.AsJson ->
                Ok
                    [
                        {
                            Name = columnName "_" prefix
                            Type = SqlType.Json
                            Nullable = not required
                            Default = None
                            Constraints = SchemaInfo.constraints info
                        }
                    ]

        | SUnion _ ->
            // A union has no fixed set of columns, so JSON is the only faithful
            // answer a table can give.
            Ok
                [
                    {
                        Name = columnName "_" prefix
                        Type = SqlType.Json
                        Nullable = not required
                        Default = None
                        Constraints = []
                    }
                ]

        | SRef name ->
            Error
                [
                    MappingError.Unsupported(path, $"a reference to the recursive schema '%s{name}'")
                ]

        | SPrim _ ->
            let name = columnName "." prefix

            Ok
                [
                    {
                        Name = name
                        Type =
                            match Map.tryFind name opts.TypeOverrides with
                            | Some overridden -> overridden
                            | None -> typeOf info
                        Nullable = not required
                        Default = None
                        Constraints = SchemaInfo.constraints info
                    }
                ]

        | SAnnotated _ -> failwith "unreachable: annotations are stripped before this point"

    /// <summary>
    /// Derives a table from a schema, or reports every decision that was not made.
    /// </summary>
    /// <remarks>
    /// A schema describes a value on a wire; a table describes a row in a
    /// database. Where the two do not line up, this returns errors rather than
    /// choosing, because the choice is one you have to live with and a wrong
    /// default would be discovered in production.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Mapping.tableOf (Mapping.keyedBy "id") personSchema
    ///
    /// // With a nested address and no strategy:
    /// // Error [ NestedNotDecided "address" ]
    /// // "'address' is a nested object, and the options did not say what to do
    /// //  with it. It could be flattened into columns, stored as JSON, or
    /// //  omitted. Set TableOptions.Nested."
    /// </code></example>
    let tableOf (opts: TableOptions) (schema: Schema<'T>) : Result<Table, MappingError list> =
        match SchemaInfo.strip schema.Info with
        | SObject(schemaName, fields) ->
            let tableName = defaultArg opts.TableName schemaName

            let results =
                fields
                |> List.map (fun field -> columnsFor opts [ field.Name ] field.Required field.Schema)

            let mappingErrors =
                results
                |> List.collect (
                    function
                    | Error e -> e
                    | Ok _ -> []
                )

            let columns =
                results
                |> List.collect (
                    function
                    | Ok c -> c
                    | Error _ -> []
                )

            let keyErrors =
                if List.isEmpty opts.PrimaryKey then
                    [ MappingError.NoPrimaryKey tableName ]
                else
                    opts.PrimaryKey
                    |> List.filter (fun key ->
                        columns
                        |> List.exists (fun c -> String.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
                        |> not
                    )
                    |> List.map (fun key -> MappingError.UnknownKeyColumn(tableName, key))

            match mappingErrors @ keyErrors with
            | [] ->
                // A key column cannot be null, whatever the schema said about the
                // field it came from.
                let columns =
                    columns
                    |> List.map (fun column ->
                        if
                            opts.PrimaryKey
                            |> List.exists (fun k -> String.Equals(k, column.Name, StringComparison.OrdinalIgnoreCase))
                        then
                            { column with Nullable = false }
                        else
                            column
                    )

                Ok
                    {
                        Name = tableName
                        Columns = columns
                        PrimaryKey = opts.PrimaryKey
                        Unique = opts.Unique
                        ForeignKeys = opts.ForeignKeys
                    }
            | problems -> Error problems

        | SUnion(name, _, _) -> Error [ MappingError.NotATable $"the union '%s{name}'" ]
        | _ -> Error [ MappingError.NotATable "a single value" ]
