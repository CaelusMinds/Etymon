namespace Etymon

open System

/// <summary>
/// A declaration that one version of an event can be read into another.
/// </summary>
/// <remarks>
/// <para>
/// The function itself is not here, and cannot be: "what due date should a 2024
/// invoice get?" is a business judgement, not a derivation. What is checked is
/// that a declaration exists for every gap, so that no stored version is left
/// with nothing able to read it.
/// </para>
/// <para>
/// Note the direction. An upcaster transforms <em>on read</em>. Old events are
/// never rewritten, which is why this is a permanent property of the code and
/// not a one-time change to data — and why it is not a migration.
/// </para>
/// </remarks>
[<NoComparison>]
type Upcaster =
    {
        /// The event type it applies to.
        Event: string
        /// The version it reads.
        Reads: int
        /// The version it produces.
        Produces: int
    }

/// <summary>Something that must be settled before a change is safe to ship.</summary>
[<NoComparison>]
type Unresolved =
    {
        /// The event type it is about.
        Event: string
        /// What is unresolved, and what to do about it, in full sentences.
        Message: string
    }

/// <summary>
/// Checks that do not compare two shapes, but ask whether the code can read
/// everything the log holds.
/// </summary>
[<RequireQualifiedAccess>]
module Upcasters =

    /// <summary>
    /// Every stored version that no chain of upcasters can bring to the current
    /// one.
    /// </summary>
    /// <remarks>
    /// Pure graph reachability. For each version present in the snapshot, follow
    /// the declared upcasters and see whether the current version is reached.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Upcasters.unreachable upcasters snapshot
    /// // "'InvoiceRaised' has stored shapes at v1 and v2, and the current shape
    /// //  is v3. Upcasters exist for v2 -> v3. Nothing reads v1."
    /// </code></example>
    let unreachable (upcasters: Upcaster list) (snapshot: EventSnapshot) : Unresolved list =
        EventSnapshot.names snapshot
        |> List.choose (fun name ->
            let versions = EventSnapshot.versionsOf name snapshot

            match versions with
            | [] -> None
            | _ ->
                let current = List.max versions

                let edges =
                    upcasters
                    |> List.filter (fun u -> String.Equals(u.Event, name, StringComparison.Ordinal))

                // Walk forward from a version until nothing new is reachable.
                let reaches (start: int) =
                    let mutable seen = Set.singleton start
                    let mutable frontier = [ start ]

                    while not frontier.IsEmpty do
                        let next =
                            frontier
                            |> List.collect (fun v -> edges |> List.filter (fun e -> e.Reads = v))
                            |> List.map (fun e -> e.Produces)
                            |> List.filter (fun v -> not (seen.Contains v))

                        seen <- next |> List.fold (fun acc v -> acc.Add v) seen
                        frontier <- next |> List.distinct

                    seen.Contains current

                let stranded = versions |> List.filter (fun v -> v <> current && not (reaches v))

                if List.isEmpty stranded then
                    None
                else
                    let storedVersions =
                        versions |> List.map (fun v -> $"v%d{v}") |> fun vs -> String.Join(" and ", vs)

                    let declared =
                        match edges with
                        | [] -> "No upcasters are declared for it."
                        | _ ->
                            let chains =
                                edges
                                |> List.sortBy (fun e -> e.Reads)
                                |> List.map (fun e -> $"v%d{e.Reads} -> v%d{e.Produces}")

                            let listed = String.Join(", ", chains)
                            $"Upcasters exist for %s{listed}."

                    let missing =
                        stranded |> List.map (fun v -> $"v%d{v}") |> fun vs -> String.Join(", ", vs)

                    Some
                        {
                            Event = name
                            Message =
                                $"'%s{name}' has stored shapes at %s{storedVersions}, and the current shape is v%d{current}. %s{declared} Nothing reads %s{missing}."
                        }
        )

    /// <summary>
    /// Field removals and additions in the same version that might be a rename,
    /// and must be declared one way or the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rename is indistinguishable from a removal plus an addition, and the
    /// difference decides whether stored events can be read. This package's
    /// stated principle is that ambiguity is an error rather than a guess, so
    /// it refuses.
    /// </para>
    /// <para>
    /// Declare the rename, or confirm the two changes are separate by listing
    /// neither — in which case the compatibility report will describe both, as
    /// it should.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Upcasters.undeclaredRenames [] previous current
    /// </code></example>
    let undeclaredRenames (renames: Rename list) (before: EventSnapshot) (after: EventSnapshot) : Unresolved list =
        EventSnapshot.names after
        |> List.collect (fun name ->
            match EventSnapshot.tryLatest name before, EventSnapshot.tryLatest name after with
            | Some previous, Some current ->
                let declared (field: string) fromSide =
                    renames
                    |> List.exists (fun r ->
                        String.Equals(r.Event, name, StringComparison.Ordinal)
                        && String.Equals((if fromSide then r.From else r.To), field, StringComparison.Ordinal)
                    )

                let removed =
                    previous.Fields
                    |> List.filter (fun f -> (EventShape.tryField f.Name current).IsNone && not (declared f.Name true))

                let added =
                    current.Fields
                    |> List.filter (fun f ->
                        (EventShape.tryField f.Name previous).IsNone && not (declared f.Name false)
                    )

                // Only ambiguous when both happened. A removal alone is a
                // removal; an addition alone is an addition.
                match removed, added with
                | [], _
                | _, [] -> []
                | removed, added ->
                    let gone = String.Join(", ", removed |> List.map (fun f -> $"'%s{f.Name}'"))
                    let arrived = String.Join(", ", added |> List.map (fun f -> $"'%s{f.Name}'"))

                    let suggestion =
                        match removed, added with
                        | [ one ], [ other ] -> $"Declare it with Rename (\"%s{one.Name}\", \"%s{other.Name}\")"
                        | _ -> "Declare any renames among them"

                    [
                        {
                            Event = name
                            Message =
                                $"'%s{name}' v%d{current.Version} removes %s{gone} and adds %s{arrived}. That is either a rename or separate changes, and the difference decides whether stored events can be read. %s{suggestion}, or confirm they are separate."
                        }
                    ]
            | _ -> []
        )

    /// <summary>Everything unresolved about a change, as one report.</summary>
    /// <example><code lang="fsharp">
    /// match Upcasters.check upcasters renames previous current with
    /// | [] -> ()
    /// | problems -> failwith (Upcasters.report problems)
    /// </code></example>
    let check (upcasters: Upcaster list) (renames: Rename list) (before: EventSnapshot) (after: EventSnapshot) =
        undeclaredRenames renames before after @ unreachable upcasters after

    /// <summary>The unresolved items as prose, one per paragraph.</summary>
    let report (problems: Unresolved list) =
        if List.isEmpty problems then
            "Nothing is unresolved."
        else
            String.Join(Environment.NewLine + Environment.NewLine, problems |> List.map (fun p -> "  " + p.Message))
