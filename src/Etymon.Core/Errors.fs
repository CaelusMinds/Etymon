namespace Etymon

open System

/// Why a value was rejected, in a form a program can branch on. The human-readable
/// wording lives separately on <see cref="T:Etymon.ValidationError"/>, so that
/// callers can re-word an error without having to parse it.
[<RequireQualifiedAccess>]
type ErrorReason =
    /// A required value was not present at all.
    | Missing
    /// A value was present but of the wrong shape.
    | TypeMismatch of expected: string * actual: string
    /// A value was of the right shape but broke a constraint.
    | ConstraintFailed of failed: Constraint
    /// A value was rejected by a rule identified by a stable code.
    | Rejected of code: string

/// A single thing that was wrong, and where.
[<StructuredFormatDisplay("{Display}")>]
type ValidationError =
    {
        /// Where in the value the problem was found.
        Path: Path
        /// Why it was rejected, in machine-readable form.
        Reason: ErrorReason
        /// What to tell a human. Phrased to read after the path.
        Message: string
    }

    /// Renders as <c>path: message</c>, or just the message at the root.
    override this.ToString() =
        let rendered = Path.toString this.Path

        if rendered = "" then
            this.Message
        else
            rendered + ": " + this.Message

    /// Used by F# <c>%A</c> formatting.
    member this.Display = this.ToString()

/// <summary>
/// One or more validation errors. Never empty: if you are holding one of these,
/// something was wrong.
/// </summary>
/// <remarks>
/// Etymon accumulates by default. A decode that finds four bad fields produces four
/// errors, not the first one. The only operations that short-circuit are the ones
/// that genuinely cannot continue, such as binding on a value that was never produced.
/// </remarks>
[<StructuredFormatDisplay("{Display}"); CustomEquality; NoComparison>]
type ValidationErrors =
    // A concatenation tree, not a list.
    //
    // Accumulation joins collections, and joining two lists copies the left one.
    // Validating a 200,000-element array therefore cost 200,000 joins of a
    // growing list -- quadratic, and measured at five minutes of CPU for an
    // 800 KB request body. Somebody else's application should not be able to
    // have a core taken off it by sending a large array of the wrong thing.
    //
    // Joining two trees is one allocation regardless of size, and the list is
    // built once, iteratively, when somebody asks for it.
    private
    | Single of ValidationError
    | Join of left: ValidationErrors * right: ValidationErrors

    /// <summary>
    /// Every error, in the order they were found.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive: a long run of joins is a deep tree, and
    /// a deep tree is exactly what arrives when the input was large.
    /// </remarks>
    member this.ToList() =
        let mutable acc = []
        let mutable remaining = [ this ]

        while not remaining.IsEmpty do
            match remaining with
            | Single error :: rest ->
                acc <- error :: acc
                remaining <- rest
            // Right before left, because acc is built by prepending.
            | Join(left, right) :: rest -> remaining <- right :: left :: rest
            | [] -> ()

        acc

    /// Renders every error, one per line.
    override this.ToString() =
        String.Join(Environment.NewLine, this.ToList() |> List.map (fun e -> e.ToString()))

    /// Used by F# <c>%A</c> formatting.
    member this.Display = this.ToString()

    // Two collections holding the same errors are the same collection, whatever
    // shape the joins left behind. Without this, structural equality would
    // compare tree shapes, and `a joined to (b joined to c)` would differ from
    // `(a joined to b) joined to c`.
    override this.Equals(other: obj) =
        match other with
        | :? ValidationErrors as that -> this.ToList() = that.ToList()
        | _ -> false

    override this.GetHashCode() = this.ToList().GetHashCode()

/// Building and inspecting <see cref="T:Etymon.ValidationErrors"/>.
[<RequireQualifiedAccess>]
module ValidationErrors =

    /// <summary>A collection holding exactly one error.</summary>
    /// <example><code lang="fsharp">
    /// ValidationErrors.one { Path = Path.root; Reason = ErrorReason.Missing; Message = "is required" }
    /// </code></example>
    let one (error: ValidationError) = Single error

    /// <summary>Builds one error from its parts.</summary>
    /// <example><code lang="fsharp">
    /// ValidationErrors.create (Path.root |> Path.field "name") ErrorReason.Missing "is required"
    /// </code></example>
    let create (path: Path) (reason: ErrorReason) (message: string) =
        Single
            {
                Path = path
                Reason = reason
                Message = message
            }

    /// <summary>
    /// Builds from a list, or returns <c>None</c> when the list is empty. Emptiness is
    /// not an error condition, so it is not representable.
    /// </summary>
    /// <example><code lang="fsharp">
    /// ValidationErrors.ofList [] // None
    /// </code></example>
    let ofList (errors: ValidationError list) =
        match errors with
        | [] -> None
        | first :: rest -> Some(rest |> List.fold (fun acc error -> Join(acc, Single error)) (Single first))

    /// <summary>
    /// Builds from a first error and any that followed, so that non-emptiness holds by
    /// construction rather than by checking.
    /// </summary>
    /// <example><code lang="fsharp">
    /// match found with
    /// | [] -> Ok value
    /// | first :: rest -> Error(ValidationErrors.ofHeadTail first rest)
    /// </code></example>
    let ofHeadTail (first: ValidationError) (rest: ValidationError list) =
        rest |> List.fold (fun acc error -> Join(acc, Single error)) (Single first)

    /// <summary>Every error, in the order they were found.</summary>
    /// <example><code lang="fsharp">
    /// errs |> ValidationErrors.toList |> List.map (fun e -> e.Message)
    /// </code></example>
    let toList (errors: ValidationErrors) = errors.ToList()

    /// <summary>The first error found.</summary>
    /// <example><code lang="fsharp">
    /// (ValidationErrors.one e |> ValidationErrors.head).Message
    /// </code></example>
    let rec head (errors: ValidationErrors) =
        match errors with
        | Single error -> error
        | Join(left, _) -> head left

    /// <summary>How many things were wrong.</summary>
    /// <example><code lang="fsharp">
    /// ValidationErrors.count errs // 4
    /// </code></example>
    let count (errors: ValidationErrors) = List.length (errors.ToList())

    /// <summary>
    /// Joins two collections, keeping the left-hand errors first. This is how
    /// accumulation happens.
    /// </summary>
    /// <example><code lang="fsharp">
    /// ValidationErrors.append nameErrors ageErrors |> ValidationErrors.count // 2
    /// </code></example>
    let append (left: ValidationErrors) (right: ValidationErrors) = Join(left, right)

    /// <summary>Rewrites every error.</summary>
    /// <example><code lang="fsharp">
    /// errs |> ValidationErrors.map (fun e -> { e with Message = e.Message.ToUpperInvariant() })
    /// </code></example>
    let map (f: ValidationError -> ValidationError) (errors: ValidationErrors) =
        // Through the list rather than over the tree: rebuilding recursively
        // would recurse as deeply as the tree, which is as deep as the input
        // was large.
        match errors.ToList() |> List.map f with
        | [] -> errors
        | first :: rest -> rest |> List.fold (fun acc e -> Join(acc, Single e)) (Single first)

    /// <summary>
    /// Re-roots every error one step deeper, so that a nested decoder's errors read
    /// relative to the outer value.
    /// </summary>
    /// <example><code lang="fsharp">
    /// // an error at "zip" becomes an error at "address.zip"
    /// errs |> ValidationErrors.under (PathSegment.Field "address")
    /// </code></example>
    let under (segment: PathSegment) (errors: ValidationErrors) =
        let prefix = Path.push segment Path.root

        errors
        |> map (fun e ->
            { e with
                Path = Path.combine prefix e.Path
            }
        )

    /// <summary>Re-roots every error under a named field.</summary>
    /// <example><code lang="fsharp">
    /// errs |> ValidationErrors.underField "address"
    /// </code></example>
    let underField (name: string) (errors: ValidationErrors) = under (PathSegment.Field name) errors

    /// <summary>Re-roots every error under a collection position.</summary>
    /// <example><code lang="fsharp">
    /// errs |> ValidationErrors.underIndex 3
    /// </code></example>
    let underIndex (position: int) (errors: ValidationErrors) =
        under (PathSegment.Index position) errors

    /// <summary>Every error, one per line, ready to log or show.</summary>
    /// <example><code lang="fsharp">
    /// printfn "%s" (ValidationErrors.format errs)
    /// // name: is required
    /// // age: must be at least 0
    /// </code></example>
    let format (errors: ValidationErrors) = errors.ToString()
