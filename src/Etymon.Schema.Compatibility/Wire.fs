namespace Etymon

open System

/// <summary>
/// Which side of a wire contract a shape is on, which decides what can be done
/// about a break.
/// </summary>
/// <remarks>
/// This is the whole reason wire contracts are a separate policy rather than a
/// rerun of the event one. An event's old shape sits in your database and you
/// own the reader, so an upcaster can always rescue it. A DTO's old shape sits
/// in somebody else's code, and whether it can be rescued depends entirely on
/// which way it travels.
/// </remarks>
[<RequireQualifiedAccess>]
type WireSide =
    /// A body clients send and you read. Old clients send old shapes, so the
    /// direction that matters is backward — and an upcaster can take the old
    /// body and produce the current one.
    | Request
    /// A body you send and clients read. You write new shapes and their code
    /// reads them, so the direction that matters is forward — and there is no
    /// upcaster to write, because the code that would run it is not code you
    /// ship.
    | Response

/// <summary>What a wire change means, and for whom.</summary>
[<NoComparison>]
type WireVerdict =
    {
        /// The shape the verdict is about.
        Contract: string
        /// Which side of the wire it is on.
        Side: WireSide
        /// What changed, in a sentence.
        Change: string
        /// What it means for the people it affects, in sentences.
        Consequence: string
        /// Whether anything the author ships can fix it. False for every
        /// response break, because the code that breaks is the client's.
        Fixable: bool
    }

/// <summary>
/// The wire-contract policy over the shared compatibility matrix.
/// </summary>
/// <remarks>
/// <para>
/// The same matrix that <c>Events</c> reads, with a different policy on top,
/// because a DTO's old shape is not yours to fix.
/// </para>
/// <para>
/// Verdicts name the <em>audience</em> rather than the direction. "Forward" and
/// "backward" are the two words people reliably get the wrong way round, and a
/// verdict read at eleven at night is the entire product here.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module Wire =

    let private audience (side: WireSide) =
        match side with
        | WireSide.Request -> "Clients already written (they send what you read)"
        | WireSide.Response -> "Clients already written (they read what you send)"

    /// The direction that matters for a side. Requests are read by you, so the
    /// risk is to what has already been sent; responses are read by them, so
    /// the risk is to code already deployed elsewhere.
    let private directionFor (side: WireSide) =
        match side with
        | WireSide.Request -> Direction.Backward
        | WireSide.Response -> Direction.Forward

    let private verdictFor (side: WireSide) (change: ShapeChange) =
        let direction = directionFor side

        change.Breaks
        |> List.filter (fun (d, _) -> d = direction)
        |> List.map (fun (_, why) ->
            let consequence =
                match side with
                | WireSide.Request -> $"%s{why} An upcaster must take the old body and produce the current one."
                | WireSide.Response ->
                    // Deliberately blunt. The value of this verdict is that it
                    // arrives before the change ships, because afterwards there
                    // is nothing to do with it.
                    $"%s{why} Nothing you ship can fix this, because the code that breaks is theirs. Version the endpoint, or keep the shape as it was."

            {
                Contract = change.Shape
                Side = side
                Change = change.Description
                Consequence = consequence
                Fixable = (side = WireSide.Request)
            }
        )

    /// <summary>
    /// What changing a request body does to clients that have already been
    /// written.
    /// </summary>
    /// <remarks>
    /// Backward compatibility, and the event policy's answer applies almost
    /// unchanged: bodies already in the wild carry the old shape, and an
    /// upcaster can rescue them.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Wire.requests renames previousSnapshot currentSnapshot
    /// </code></example>
    let requests (renames: Rename list) (before: ShapeSnapshot) (after: ShapeSnapshot) =
        Compatibility.between renames before after
        |> List.collect (verdictFor WireSide.Request)

    /// <summary>
    /// What changing a response body does to clients that have already been
    /// written.
    /// </summary>
    /// <remarks>
    /// Forward compatibility, and there is no fix. This package offers no hook
    /// for one, because a hook that cannot help is worse than an honest refusal:
    /// it suggests the problem has been handled.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Wire.responses renames previousSnapshot currentSnapshot
    /// </code></example>
    let responses (renames: Rename list) (before: ShapeSnapshot) (after: ShapeSnapshot) =
        Compatibility.between renames before after
        |> List.collect (verdictFor WireSide.Response)

    /// <summary>
    /// Only the verdicts nothing you ship can fix, which are the ones that have
    /// to be decided before the change goes out.
    /// </summary>
    /// <example><code lang="fsharp">
    /// match Wire.responses [] previous current |> Wire.unfixable with
    /// | [] -> ()
    /// | breaks -> failwith (Wire.report breaks)
    /// </code></example>
    let unfixable (verdicts: WireVerdict list) =
        verdicts |> List.filter (fun v -> not v.Fixable)

    /// <summary>
    /// Renames and upcaster gaps on the request side, which must be settled the
    /// same way they are for events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Request bodies have versions and upcasters for the same reason events do.
    /// Response bodies have neither, which is why this takes only the request
    /// snapshot.
    /// </para>
    /// <para>
    /// The checks are the same as the event ones and the wording is not. An
    /// event's old shapes sit in your store; a request body's were already sent
    /// by somebody's client. Telling a reader looking at an HTTP body that the
    /// question is whether stored events can be read points them at a system
    /// they do not have.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// match Wire.unresolvedRequests upcasters renames previous current with
    /// | [] -> ()
    /// | problems -> failwith (Wire.reportUnresolved problems)
    /// </code></example>
    let unresolvedRequests
        (upcasters: Upcaster list)
        (renames: Rename list)
        (before: ShapeSnapshot)
        (after: ShapeSnapshot)
        =
        Detect.renameAmbiguities "whether bodies already sent can be read" renames before after
        @ Detect.strandedVersions "bodies already sent" upcasters after

    /// <summary>The unresolved request items as prose, one paragraph each.</summary>
    /// <remarks>
    /// Here so that printing a wire outcome never goes through <c>Events</c>.
    /// Reaching across for the renderer is how the event wording arrived in wire
    /// verdicts in the first place.
    /// </remarks>
    let reportUnresolved (problems: Unresolved list) = Detect.report problems

    /// <summary>The verdicts as prose, one paragraph each.</summary>
    /// <remarks>
    /// Named for the audience, not the direction, and it says plainly where
    /// nothing can be done.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// printfn "%s" (Wire.report verdicts)
    /// </code></example>
    let report (verdicts: WireVerdict list) =
        if List.isEmpty verdicts then
            "No wire contract changed in a way that affects a client."
        else
            let paragraphs =
                verdicts
                |> List.map (fun v ->
                    let heading = $"  %s{v.Contract}: %s{v.Change}."
                    let body = $"    %s{audience v.Side}: %s{v.Consequence}"
                    String.Join(Environment.NewLine, [ heading; body ])
                )

            String.Join(Environment.NewLine + Environment.NewLine, paragraphs)
