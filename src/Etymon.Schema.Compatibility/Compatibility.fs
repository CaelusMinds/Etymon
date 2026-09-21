namespace Etymon

open System

/// <summary>
/// Which direction a compatibility verdict is about.
/// </summary>
/// <remarks>
/// Both directions matter and they fail on different changes, so neither is
/// "compatibility" on its own. Prose in this package never says "compatible"
/// unqualified, and neither should yours.
/// </remarks>
[<RequireQualifiedAccess>]
type Direction =
    /// New code reading what was already written. The one you always need:
    /// losing it makes history unreadable.
    | Backward
    /// Already-deployed code reading what newer code writes. Matters during
    /// a rollout, when two versions run against one log.
    | Forward

/// Working with <see cref="T:Etymon.Direction"/>.
[<RequireQualifiedAccess>]
module Direction =

    /// <summary>What this direction means, as a phrase that fits in a sentence.</summary>
    let describe (direction: Direction) =
        match direction with
        | Direction.Backward -> "new code reading what was already written"
        | Direction.Forward -> "already-deployed code reading what new code writes"

/// <summary>One thing that changed between two versions of an event shape.</summary>
[<NoComparison>]
type ShapeChange =
    {
        /// The name of the shape the change is about.
        Shape: string
        /// The field, where the change is about one.
        Field: string option
        /// What changed, in a sentence.
        Description: string
        /// The directions this change breaks, and why.
        Breaks: (Direction * string) list
    }

/// <summary>
/// A rename that the author has declared, because it cannot be detected.
/// </summary>
/// <remarks>
/// A removal plus an addition is indistinguishable from a rename, and the
/// difference decides whether stored events can be read at all. This package
/// refuses to guess, so a rename is something you say.
/// </remarks>
[<NoComparison>]
type Rename =
    {
        /// The name of the shape the rename happened in.
        Shape: string
        /// What the field used to be called.
        From: string
        /// What it is called now.
        To: string
    }

/// Comparing two event snapshots.
[<RequireQualifiedAccess>]
module Compatibility =

    let private renamedTo (event: string) (from: string) (renames: Rename list) =
        renames
        |> List.tryFind (fun r ->
            String.Equals(r.Shape, event, StringComparison.Ordinal)
            && String.Equals(r.From, from, StringComparison.Ordinal)
        )
        |> Option.map (fun r -> r.To)

    let private renamedFrom (event: string) (to': string) (renames: Rename list) =
        renames
        |> List.exists (fun r ->
            String.Equals(r.Shape, event, StringComparison.Ordinal)
            && String.Equals(r.To, to', StringComparison.Ordinal)
        )

    let private change event field description breaks =
        {
            Shape = event
            Field = field
            Description = description
            Breaks = breaks
        }

    /// Whether the second set of rules admits everything the first does.
    ///
    /// Compared as sets rather than interpreted: this package knows that the
    /// rules differ, not what the difference permits. Narrowing and widening
    /// are therefore both reported, in the direction each could break.
    let private constraintsDiffer (before: Constraint list) (after: Constraint list) = before <> after

    let private compareFields (event: string) (renames: Rename list) (before: Shape) (after: Shape) =
        let removed =
            before.Fields
            |> List.collect (fun field ->
                match renamedTo event field.Name renames with
                | Some _ -> []
                | None ->
                    match Shape.tryField field.Name after with
                    | Some _ -> []
                    | None ->
                        if field.Required then
                            // The events carry it and nothing reads it now, which
                            // is fine going forward and fatal going back.
                            [
                                change
                                    event
                                    (Some field.Name)
                                    $"the required field '%s{field.Name}' was removed"
                                    [
                                        Direction.Forward,
                                        $"already-deployed code requires '%s{field.Name}' and what is written now will not carry it."
                                    ]
                            ]
                        else
                            []
            )

        let added =
            after.Fields
            |> List.collect (fun field ->
                if renamedFrom event field.Name renames then
                    []
                else
                    match Shape.tryField field.Name before with
                    | Some _ -> []
                    | None ->
                        if field.Required then
                            [
                                change
                                    event
                                    (Some field.Name)
                                    $"the required field '%s{field.Name}' was added"
                                    [ Direction.Backward, $"what was already written has no '%s{field.Name}'." ]
                            ]
                        else
                            []
            )

        let altered =
            after.Fields
            |> List.collect (fun afterField ->
                let beforeField =
                    match
                        renames
                        |> List.tryFind (fun r ->
                            String.Equals(r.Shape, event, StringComparison.Ordinal)
                            && String.Equals(r.To, afterField.Name, StringComparison.Ordinal)
                        )
                    with
                    | Some rename -> Shape.tryField rename.From before
                    | None -> Shape.tryField afterField.Name before

                match beforeField with
                | None -> []
                | Some beforeField ->
                    // Two unions of the same name differing only in their cases
                    // are not a changed type: adding a case and removing one
                    // break opposite directions, and reporting "the type
                    // changed" on top of that would claim both break, which is
                    // the wrong verdict in each case.
                    let sameUnionDifferentCases =
                        match beforeField.Type, afterField.Type with
                        | FieldType.Choice(beforeName, _), FieldType.Choice(afterName, _) -> beforeName = afterName
                        | _ -> false

                    [
                        if beforeField.Type <> afterField.Type && not sameUnionDifferentCases then
                            change
                                event
                                (Some afterField.Name)
                                $"the type of '%s{afterField.Name}' changed"
                                [
                                    Direction.Backward, "what was already written holds the old type."
                                    Direction.Forward, "already-deployed code expects the old type."
                                ]

                        if beforeField.Required && not afterField.Required then
                            change
                                event
                                (Some afterField.Name)
                                $"'%s{afterField.Name}' stopped being required"
                                [
                                    Direction.Forward,
                                    $"already-deployed code requires '%s{afterField.Name}' and what is written now may omit it."
                                ]

                        if not beforeField.Required && afterField.Required then
                            change
                                event
                                (Some afterField.Name)
                                $"'%s{afterField.Name}' became required"
                                [
                                    Direction.Backward, $"what was already written may omit '%s{afterField.Name}'."
                                ]

                        if constraintsDiffer beforeField.Constraints afterField.Constraints then
                            // Reported in both directions because this compares
                            // rules rather than interpreting them: a narrowing
                            // breaks reading old events, a widening breaks old
                            // code reading new ones, and which it is needs a
                            // person.
                            change
                                event
                                (Some afterField.Name)
                                $"the rules on '%s{afterField.Name}' changed"
                                [
                                    Direction.Backward,
                                    "what was already written may not satisfy the new rules, if they were narrowed."
                                    Direction.Forward,
                                    "already-deployed code may reject what is written now, if the rules were widened."
                                ]

                        match beforeField.Type, afterField.Type with
                        | FieldType.Choice(_, beforeCases), FieldType.Choice(_, afterCases) ->
                            let gained = afterCases |> List.except beforeCases
                            let lost = beforeCases |> List.except afterCases

                            let listed (cases: string list) = String.Join(", ", cases)

                            if not (List.isEmpty gained) then
                                let names = listed gained

                                change
                                    event
                                    (Some afterField.Name)
                                    $"'%s{afterField.Name}' gained the case(s) %s{names}"
                                    [
                                        Direction.Forward,
                                        "already-deployed code has no branch for a case it has never seen."
                                    ]

                            if not (List.isEmpty lost) then
                                let names = listed lost

                                change
                                    event
                                    (Some afterField.Name)
                                    $"'%s{afterField.Name}' lost the case(s) %s{names}"
                                    [
                                        Direction.Backward,
                                        "what was already written carries a case nothing reads now."
                                    ]
                        | _ -> ()
                    ]
            )

        removed @ added @ altered

    /// <summary>
    /// Every change between two versions of one event shape, with the direction
    /// each breaks.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Compatibility.betweenShapes [] raisedV1 raisedV2
    /// </code></example>
    let betweenShapes (renames: Rename list) (before: Shape) (after: Shape) =
        compareFields after.Name renames before after

    /// <summary>
    /// Every change between the latest shape of each event type in two
    /// snapshots.
    /// </summary>
    /// <remarks>
    /// Pure: two snapshots in, verdicts out. It never opens a connection,
    /// because the question "is this change safe against events already
    /// written?" is answerable from the shapes alone, and a package that needed
    /// a database to answer it could not run in a build.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Compatibility.between [] committedSnapshot currentSnapshot
    /// </code></example>
    let between (renames: Rename list) (before: ShapeSnapshot) (after: ShapeSnapshot) =
        ShapeSnapshot.names after
        |> List.collect (fun name ->
            match ShapeSnapshot.tryLatest name before, ShapeSnapshot.tryLatest name after with
            | Some previous, Some current when previous.Version <> current.Version ->
                betweenShapes renames previous current
            | Some previous, Some current -> betweenShapes renames previous current
            | None, _ ->
                // A new event type breaks nothing: no code reads it and no
                // events carry it.
                []
            | _, None -> []
        )

    /// <summary>Only the changes that break a given direction.</summary>
    /// <example><code lang="fsharp">
    /// changes |> Compatibility.breaking Direction.Backward
    /// </code></example>
    let breaking (direction: Direction) (changes: ShapeChange list) =
        changes
        |> List.filter (fun c -> c.Breaks |> List.exists (fun (d, _) -> d = direction))

    /// <summary>
    /// The changes as prose, one paragraph each, ready to print in a build.
    /// </summary>
    /// <remarks>
    /// Full sentences rather than codes. The verdict a developer reads at
    /// eleven at night is the entire product here, and "EVT0007" is not a
    /// verdict.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// printfn "%s" (Compatibility.report changes)
    /// </code></example>
    let report (changes: ShapeChange list) =
        if List.isEmpty changes then
            "No shape changed in a way that affects reading."
        else
            let lines =
                changes
                |> List.map (fun c ->
                    let breaks =
                        c.Breaks
                        |> List.map (fun (direction, why) ->
                            let label =
                                match direction with
                                | Direction.Backward -> "Backward"
                                | Direction.Forward -> "Forward"

                            $"    %s{label} ({Direction.describe direction}): %s{why}."
                        )

                    let heading = $"  %s{c.Shape}: %s{c.Description}."

                    String.Join(Environment.NewLine, heading :: breaks)
                )

            String.Join(Environment.NewLine + Environment.NewLine, lines)
