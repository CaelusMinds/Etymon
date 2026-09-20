namespace Etymon

open System.Text

/// One step into a value: a named field, or a position in a collection.
[<RequireQualifiedAccess>]
type PathSegment =
    /// A named field, such as the <c>zip</c> in <c>address.zip</c>.
    | Field of name: string
    /// A zero-based position, such as the <c>3</c> in <c>items[3]</c>.
    | Index of position: int

/// <summary>
/// A location inside a value. Every <see cref="T:Etymon.ValidationError"/> carries one,
/// so a caller can say not just <em>what</em> was wrong but <em>where</em>.
/// </summary>
/// <example>
/// <code lang="fsharp">
/// Path.root |> Path.field "address" |> Path.field "zip" |> Path.toString
/// // "address.zip"
/// </code>
/// </example>
[<StructuredFormatDisplay("{Display}")>]
type Path =
    // Segments are stored innermost-first so that pushing is O(1); `Path.segments`
    // reverses them back into reading order.
    private
    | Path of PathSegment list

    /// Renders the path in dotted form, for example <c>items[3].name</c>.
    override this.ToString() =
        let (Path reversed) = this

        if List.isEmpty reversed then
            ""
        else
            let sb = StringBuilder()

            let isBareName (name: string) =
                name.Length > 0
                && name
                   |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '-')

            reversed
            |> List.rev
            |> List.iteri (fun i segment ->
                match segment with
                | PathSegment.Index position -> sb.Append('[').Append(position).Append(']') |> ignore
                | PathSegment.Field name when isBareName name ->
                    if i > 0 then
                        sb.Append('.') |> ignore

                    sb.Append(name) |> ignore
                | PathSegment.Field name ->
                    // Keys that are not bare identifiers get quoted, so that a field
                    // literally called "a.b" is not mistaken for a nested path.
                    sb.Append("[\"") |> ignore
                    sb.Append(name.Replace("\\", "\\\\").Replace("\"", "\\\"")) |> ignore
                    sb.Append("\"]") |> ignore
            )

            sb.ToString()

    /// Used by F#'s <c>%A</c> formatting.
    member this.Display = this.ToString()

/// Building and rendering <see cref="T:Etymon.Path"/> values.
[<RequireQualifiedAccess>]
module Path =

    /// The empty path: the value itself, with no steps taken into it.
    let root = Path []

    /// <summary>Steps into a named field.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.field "address" |> Path.field "zip" |> Path.toString // "address.zip"
    /// </code></example>
    let field (name: string) (Path segments) =
        Path(PathSegment.Field name :: segments)

    /// <summary>Steps into a zero-based position in a collection.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.field "items" |> Path.index 3 |> Path.toString // "items[3]"
    /// </code></example>
    let index (position: int) (Path segments) =
        Path(PathSegment.Index position :: segments)

    /// <summary>Steps into an arbitrary segment.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.push (PathSegment.Field "name") |> Path.toString // "name"
    /// </code></example>
    let push (segment: PathSegment) (Path segments) = Path(segment :: segments)

    /// <summary>The segments of a path, outermost first.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.field "a" |> Path.index 0 |> Path.segments
    /// // [ PathSegment.Field "a"; PathSegment.Index 0 ]
    /// </code></example>
    let segments (Path reversed) = List.rev reversed

    /// <summary>Builds a path from segments given outermost first.</summary>
    /// <example><code lang="fsharp">
    /// Path.ofSegments [ PathSegment.Field "a"; PathSegment.Index 0 ] |> Path.toString // "a[0]"
    /// </code></example>
    let ofSegments (outermostFirst: PathSegment list) = Path(List.rev outermostFirst)

    /// <summary>True when the path points at the value itself.</summary>
    /// <example><code lang="fsharp">
    /// Path.isRoot Path.root // true
    /// </code></example>
    let isRoot (Path segments) = List.isEmpty segments

    /// <summary>How many steps the path takes.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.field "a" |> Path.index 0 |> Path.depth // 2
    /// </code></example>
    let depth (Path segments) = List.length segments

    /// <summary>The path with its last step removed, or <c>None</c> at the root.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.field "a" |> Path.field "b" |> Path.parent |> Option.map Path.toString
    /// // Some "a"
    /// </code></example>
    let parent (Path segments) =
        match segments with
        | [] -> None
        | _ :: rest -> Some(Path rest)

    /// <summary>
    /// Places <paramref name="inner"/> underneath <paramref name="outer"/>. Used when a
    /// nested decoder reports errors relative to its own root.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let outer = Path.root |> Path.field "address"
    /// let inner = Path.root |> Path.field "zip"
    /// Path.combine outer inner |> Path.toString // "address.zip"
    /// </code></example>
    let combine (outer: Path) (inner: Path) =
        let (Path outerSegments) = outer
        let (Path innerSegments) = inner
        Path(innerSegments @ outerSegments)

    /// <summary>Renders the path in dotted form. The root renders as the empty string.</summary>
    /// <example><code lang="fsharp">
    /// Path.root |> Path.field "items" |> Path.index 3 |> Path.field "name" |> Path.toString
    /// // "items[3].name"
    /// </code></example>
    let toString (path: Path) = path.ToString()

    /// <summary>
    /// Renders the path, substituting <paramref name="rootLabel"/> for the root so that
    /// a top-level error reads as something rather than nothing.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Path.toStringOr "(value)" Path.root // "(value)"
    /// </code></example>
    let toStringOr (rootLabel: string) (path: Path) =
        if isRoot path then rootLabel else path.ToString()
