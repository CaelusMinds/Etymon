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
        /// The name of the shape it applies to.
        Shape: string
        /// The version it reads.
        Reads: int
        /// The version it produces.
        Produces: int
    }

/// <summary>Something that must be settled before a change is safe to ship.</summary>
[<NoComparison>]
type Unresolved =
    {
        /// The name of the shape it is about.
        Shape: string
        /// What is unresolved, and what to do about it, in full sentences.
        Message: string
    }

/// <summary>
/// The two checks a versioned-shape policy has to make, without the vocabulary
/// of any one policy.
/// </summary>
/// <remarks>
/// <para>
/// Events and request bodies both carry versions, and both can be changed in
/// ways that cannot be settled by looking at the shapes alone. The computation
/// is identical: a removal plus an addition is indistinguishable from a rename
/// whichever kind of shape it is, and version reachability is graph walking
/// either way.
/// </para>
/// <para>
/// What differs is the noun. An event's old shapes are <em>stored</em>; a
/// request body's were <em>already sent</em>. The check states the fact and the
/// policy supplies the noun, which is the same layering the compatibility
/// matrix uses -- and for the same reason, because the alternative is a wire
/// verdict that talks about event stores.
/// </para>
/// </remarks>
module internal Detect =
    /// Versions no chain of upcasters brings to the current one. `oldShapes`
    /// names what the policy keeps: "stored shapes", "bodies already sent".
    let strandedVersions (oldShapes: string) (upcasters: Upcaster list) (snapshot: ShapeSnapshot) : Unresolved list =
        ShapeSnapshot.names snapshot
        |> List.choose (fun name ->
            let versions = ShapeSnapshot.versionsOf name snapshot

            match versions with
            | [] -> None
            | _ ->
                let current = List.max versions

                let edges =
                    upcasters
                    |> List.filter (fun u -> String.Equals(u.Shape, name, StringComparison.Ordinal))

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
                            Shape = name
                            Message =
                                $"'%s{name}' has %s{oldShapes} at %s{storedVersions}, and the current shape is v%d{current}. %s{declared} Nothing reads %s{missing}."
                        }
        )

    /// Removals and additions in one version that might be a rename. `decides`
    /// names what the ambiguity decides, which is the policy's to say.
    let renameAmbiguities
        (decides: string)
        (renames: Rename list)
        (before: ShapeSnapshot)
        (after: ShapeSnapshot)
        : Unresolved list
        =
        ShapeSnapshot.names after
        |> List.collect (fun name ->
            match ShapeSnapshot.tryLatest name before, ShapeSnapshot.tryLatest name after with
            | Some previous, Some current ->
                let declared (field: string) fromSide =
                    renames
                    |> List.exists (fun r ->
                        String.Equals(r.Shape, name, StringComparison.Ordinal)
                        && String.Equals((if fromSide then r.From else r.To), field, StringComparison.Ordinal)
                    )

                let removed =
                    previous.Fields
                    |> List.filter (fun f -> (Shape.tryField f.Name current).IsNone && not (declared f.Name true))

                let added =
                    current.Fields
                    |> List.filter (fun f -> (Shape.tryField f.Name previous).IsNone && not (declared f.Name false))

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
                            Shape = name
                            Message =
                                $"'%s{name}' v%d{current.Version} removes %s{gone} and adds %s{arrived}. That is either a rename or separate changes, and the difference decides %s{decides}. %s{suggestion}, or confirm they are separate."
                        }
                    ]
            | _ -> []
        )

    /// The items as prose, one paragraph each. Shared because the rendering is
    /// not a policy decision -- only the words being rendered are.
    let report (problems: Unresolved list) =
        if List.isEmpty problems then
            "Nothing is unresolved."
        else
            String.Join(Environment.NewLine + Environment.NewLine, problems |> List.map (fun p -> "  " + p.Message))
