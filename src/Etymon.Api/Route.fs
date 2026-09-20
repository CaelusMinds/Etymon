namespace Etymon

open System

/// The kind of a value carried in a path or a query string, named so that a
/// generator can render it without knowing the F# type it came from.
[<RequireQualifiedAccess>]
type ParamKind =
    /// Any text.
    | String
    /// A 32-bit integer.
    | Int
    /// A 64-bit integer.
    | Int64
    /// A UUID.
    | Guid
    /// A boolean.
    | Bool

/// <summary>
/// One named value read out of a URL.
/// </summary>
/// <remarks>
/// Carries its kind as data so that OpenAPI, TypeScript and the route template a
/// server framework needs can all be generated from it, and carries the parse
/// and render functions so that the same declaration binds the value on the
/// server and builds the URL on the client.
/// </remarks>
[<NoEquality; NoComparison>]
type RouteParam<'T> =
    {
        /// The name, as it appears in the path template.
        Name: string
        /// What kind of value it is.
        Kind: ParamKind
        /// Reads it from the URL.
        Parse: string -> 'T option
        /// Writes it into a URL.
        Render: 'T -> string
    }

/// One piece of a path.
[<RequireQualifiedAccess>]
type PathSegmentSpec =
    /// A fixed piece: the <c>users</c> in <c>/users/{id}</c>.
    | Literal of string
    /// A placeholder, with its name and kind.
    | Capture of name: string * kind: ParamKind

/// <summary>
/// A path, and how to read values out of it and put them back in.
/// </summary>
/// <remarks>
/// The path is <em>data</em>: a list of segments, not a format string. That is
/// what lets Etymon render the same route as an OpenAPI path template, an
/// ASP.NET route pattern, a Giraffe matcher and a client-side URL builder,
/// without any of them re-deriving it.
/// </remarks>
[<NoEquality; NoComparison>]
type Route<'T> =
    {
        /// The segments, in order.
        Segments: PathSegmentSpec list
        /// Reads the captured values, in the order they appear.
        Extract: string list -> 'T option
        /// Produces the captured values, in the order they appear.
        Fill: 'T -> string list
    }

/// Building <see cref="T:Etymon.RouteParam`1"/> values.
[<RequireQualifiedAccess>]
module Param =

    /// <summary>A text parameter.</summary>
    /// <example><code lang="fsharp">
    /// Param.string "slug"
    /// </code></example>
    let string (name: string) : RouteParam<string> =
        {
            Name = name
            Kind = ParamKind.String
            Parse = Some
            Render = id
        }

    /// <summary>A 32-bit integer parameter.</summary>
    /// <example><code lang="fsharp">
    /// Param.int "page"
    /// </code></example>
    let int (name: string) : RouteParam<int> =
        {
            Name = name
            Kind = ParamKind.Int
            Parse = Parse.int
            Render = Operators.string
        }

    /// <summary>A 64-bit integer parameter.</summary>
    /// <example><code lang="fsharp">
    /// Param.int64 "offset"
    /// </code></example>
    let int64 (name: string) : RouteParam<int64> =
        {
            Name = name
            Kind = ParamKind.Int64
            Parse = Parse.int64
            Render = Operators.string
        }

    /// <summary>A UUID parameter.</summary>
    /// <example><code lang="fsharp">
    /// Param.guid "id"
    /// </code></example>
    let guid (name: string) : RouteParam<Guid> =
        {
            Name = name
            Kind = ParamKind.Guid
            Parse = Parse.guid
            Render = fun (v: Guid) -> v.ToString "D"
        }

    /// <summary>A boolean parameter, accepting the forms that occur in URLs.</summary>
    /// <example><code lang="fsharp">
    /// Param.bool "includeArchived"
    /// </code></example>
    let bool (name: string) : RouteParam<bool> =
        {
            Name = name
            Kind = ParamKind.Bool
            Parse = Parse.bool
            Render = fun v -> if v then "true" else "false"
        }

/// <summary>
/// Building <see cref="T:Etymon.Route`1"/> values.
/// </summary>
/// <remarks>
/// <para>
/// Zero, one and two path parameters have their own constructors rather than
/// there being a general combinator. That is a deliberate trade: it costs a
/// little elegance and buys full type inference with no operator overloading,
/// and a route with three or more parameters is already telling you something
/// about the design. <see cref="M:Etymon.Route.many"/> is there for the rest.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module Route =

    let private literals = List.map PathSegmentSpec.Literal

    /// <summary>A path with no parameters.</summary>
    /// <example><code lang="fsharp">
    /// Route.literal [ "users" ] // /users
    /// </code></example>
    let literal (segments: string list) : Route<unit> =
        {
            Segments = literals segments
            Extract = fun _ -> Some()
            Fill = fun () -> []
        }

    /// <summary>A path with one parameter.</summary>
    /// <example><code lang="fsharp">
    /// Route.one [ "users" ] (Param.guid "id") [] // /users/{id}
    /// Route.one [ "users" ] (Param.guid "id") [ "orders" ] // /users/{id}/orders
    /// </code></example>
    let one (before: string list) (param: RouteParam<'A>) (after: string list) : Route<'A> =
        {
            Segments =
                literals before
                @ [ PathSegmentSpec.Capture(param.Name, param.Kind) ]
                @ literals after
            Extract =
                fun values ->
                    match values with
                    | [ only ] -> param.Parse only
                    | _ -> None
            Fill = fun value -> [ param.Render value ]
        }

    /// <summary>A path with two parameters.</summary>
    /// <example><code lang="fsharp">
    /// Route.two [ "users" ] (Param.guid "id") [ "orders" ] (Param.int "orderId") []
    /// // /users/{id}/orders/{orderId}
    /// </code></example>
    let two
        (before: string list)
        (first: RouteParam<'A>)
        (between: string list)
        (second: RouteParam<'B>)
        (after: string list)
        : Route<'A * 'B>
        =
        {
            Segments =
                literals before
                @ [ PathSegmentSpec.Capture(first.Name, first.Kind) ]
                @ literals between
                @ [ PathSegmentSpec.Capture(second.Name, second.Kind) ]
                @ literals after
            Extract =
                fun values ->
                    match values with
                    | [ a; b ] ->
                        match first.Parse a, second.Parse b with
                        | Some a, Some b -> Some(a, b)
                        | _ -> None
                    | _ -> None
            Fill = fun (a, b) -> [ first.Render a; second.Render b ]
        }

    /// <summary>
    /// A path with any number of parameters, all of the same kind, handed over as
    /// a list. The escape hatch for a route shape the typed constructors do not
    /// cover.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Route.many [ PathSegmentSpec.Literal "files"; PathSegmentSpec.Capture("path", ParamKind.String) ]
    /// </code></example>
    let many (segments: PathSegmentSpec list) : Route<string list> =
        {
            Segments = segments
            Extract = Some
            Fill = id
        }

    /// <summary>The parameters a route captures, in order.</summary>
    /// <example><code lang="fsharp">
    /// Route.parameters route // [ "id", ParamKind.Guid ]
    /// </code></example>
    let parameters (route: Route<'T>) =
        route.Segments
        |> List.choose (
            function
            | PathSegmentSpec.Capture(name, kind) -> Some(name, kind)
            | PathSegmentSpec.Literal _ -> None
        )

    /// <summary>
    /// The path as a template with braces, which is what OpenAPI and ASP.NET
    /// Core both use.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Route.template route // "/users/{id}/orders"
    /// </code></example>
    let template (route: Route<'T>) =
        let rendered =
            route.Segments
            |> List.map (
                function
                | PathSegmentSpec.Literal text -> text
                | PathSegmentSpec.Capture(name, _) -> "{" + name + "}"
            )

        "/" + String.Join("/", rendered)

    /// <summary>
    /// A concrete path for a value, ready to request. This is what makes a
    /// generated client possible without it re-deriving the URL shape.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Route.toPath userRoute someGuid // "/users/9d1c.../orders"
    /// </code></example>
    let toPath (route: Route<'T>) (value: 'T) =
        let mutable remaining = route.Fill value

        let rendered =
            route.Segments
            |> List.map (
                function
                | PathSegmentSpec.Literal text -> Uri.EscapeDataString text
                | PathSegmentSpec.Capture _ ->
                    match remaining with
                    | next :: rest ->
                        remaining <- rest
                        Uri.EscapeDataString next
                    | [] -> ""
            )

        "/" + String.Join("/", rendered)

    /// <summary>
    /// Matches a concrete path, returning the captured value when it fits.
    /// </summary>
    /// <remarks>
    /// Every adapter uses this rather than delegating to its framework's router,
    /// so that one route description behaves identically on Giraffe, on minimal
    /// APIs and in the client.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Route.matchPath userRoute "/users/9d1c4b2a-.../orders" // Some guid
    /// </code></example>
    let matchPath (route: Route<'T>) (path: string) : 'T option =
        let actual =
            path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            |> Array.map Uri.UnescapeDataString
            |> List.ofArray

        if List.length actual <> List.length route.Segments then
            None
        else
            let pairs = List.zip route.Segments actual

            let literalsMatch =
                pairs
                |> List.forall (fun (spec, value) ->
                    match spec with
                    | PathSegmentSpec.Literal expected -> String.Equals(expected, value, StringComparison.Ordinal)
                    | PathSegmentSpec.Capture _ -> true
                )

            if not literalsMatch then
                None
            else
                pairs
                |> List.choose (fun (spec, value) ->
                    match spec with
                    | PathSegmentSpec.Capture _ -> Some value
                    | PathSegmentSpec.Literal _ -> None
                )
                |> route.Extract
