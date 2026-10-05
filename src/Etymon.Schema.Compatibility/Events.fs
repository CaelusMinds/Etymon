namespace Etymon

open System

/// <summary>
/// The event-sourcing policy over the shared compatibility matrix.
/// </summary>
/// <remarks>
/// <para>
/// The matrix in <c>Compatibility</c> knows nothing about events. This module is
/// what makes it mean something for an event log: the direction that matters is
/// backward, the fix is an upcaster, and the events already written are never
/// rewritten.
/// </para>
/// <para>
/// The last of those is not a default. Migrating an event store means upcasting,
/// or rarely copy-forward. A tool offering to fix up the old rows would be
/// offering to destroy the only irreplaceable thing in the system.
/// </para>
/// <para>
/// Its sibling is <c>Wire</c>, which applies a different policy to the same
/// matrix because a DTO's old shape sits in somebody else's code rather than in
/// your database.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module Events =

    /// <summary>
    /// Every stored version that no chain of upcasters can bring to the current
    /// one.
    /// </summary>
    /// <remarks>
    /// Pure graph reachability. For each version present in the snapshot, follow
    /// the declared upcasters and see whether the current version is reached.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Events.unreachable upcasters snapshot
    /// // "'InvoiceRaised' has stored shapes at v1 and v2, and the current shape
    /// //  is v3. Upcasters exist for v2 -> v3. Nothing reads v1."
    /// </code></example>
    let unreachable (upcasters: Upcaster list) (snapshot: ShapeSnapshot) : Unresolved list =
        Detect.strandedVersions "stored shapes" upcasters snapshot

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
    /// Events.undeclaredRenames [] previous current
    /// </code></example>
    let undeclaredRenames (renames: Rename list) (before: ShapeSnapshot) (after: ShapeSnapshot) : Unresolved list =
        Detect.renameAmbiguities "whether stored events can be read" renames before after

    /// <summary>
    /// Every current shape the committed snapshot does not record as the shape
    /// is now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matrix is right that an optional field breaks neither direction, and
    /// that is exactly how a snapshot comes to lag: nothing demanded the file be
    /// regenerated, and the next change was measured against a file that did
    /// not describe the bytes. This says so, names the fields, and says to
    /// regenerate with <c>ShapeSnapshot.extend</c>.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Events.unrecorded committed current
    /// // "'InvoiceRaised' v1 is not recorded as the shape is now: the committed snapshot
    /// //  lacks 'note'. Regenerate the snapshot from ShapeSnapshot.extend committed
    /// //  current and commit the file, so the next change is measured against
    /// //  what was already written."
    /// </code></example>
    let unrecorded (committed: ShapeSnapshot) (current: ShapeSnapshot) : Unresolved list =
        Detect.unrecorded "what was already written" committed current

    /// <summary>Everything unresolved about a change, as one report.</summary>
    /// <remarks>
    /// Stranded versions are computed over both snapshots together, through
    /// <c>ShapeSnapshot.extend</c>, toward the version the code declares: the
    /// committed file is where the stored versions live, the current snapshot
    /// is what the code reads today, and a version in the file that the code
    /// no longer declares still needs an upcaster.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// match Events.check upcasters renames previous current with
    /// | [] -> ()
    /// | problems -> failwith (Events.report problems)
    /// </code></example>
    let check (upcasters: Upcaster list) (renames: Rename list) (before: ShapeSnapshot) (after: ShapeSnapshot) =
        undeclaredRenames renames before after
        @ Detect.strandedVersionsToward "stored shapes" upcasters after (ShapeSnapshot.extend before after)
        @ unrecorded before after
        @ Detect.unrecordedNested after

    /// <summary>The unresolved items as prose, one per paragraph.</summary>
    let report (problems: Unresolved list) = Detect.report problems
