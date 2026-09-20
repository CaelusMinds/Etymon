namespace Etymon

/// <summary>
/// The result of validating something: the value, or every reason it was rejected.
/// </summary>
/// <remarks>
/// <para>
/// This is an abbreviation for <c>Result&lt;'T, ValidationErrors&gt;</c>, not a distinct
/// type. You can pattern-match it with <c>Ok</c> and <c>Error</c>, and it interoperates
/// with the <c>Result</c> module and with other libraries for free.
/// </para>
/// <para>
/// The trade-off: <see cref="M:Etymon.Validation.apply"/> accumulates errors, whereas an
/// <c>apply</c> from a general-purpose <c>Result</c> library short-circuits on the first
/// failure. Reach for the <c>Validation</c> module when you want every error, and for
/// <c>Validation.bind</c> only when a later step genuinely needs an earlier step's value.
/// </para>
/// </remarks>
type Validation<'T> = Result<'T, ValidationErrors>

/// Combinators over <see cref="T:Etymon.Validation`1"/>. Everything here accumulates
/// errors except <c>bind</c>, which cannot.
[<RequireQualifiedAccess>]
module Validation =

    /// <summary>A successful validation.</summary>
    /// <example><code lang="fsharp">
    /// Validation.ok 42 // Ok 42
    /// </code></example>
    let ok (value: 'T) : Validation<'T> = Ok value

    /// <summary>A failed validation carrying an existing error collection.</summary>
    /// <example><code lang="fsharp">
    /// Validation.errors existing : Validation&lt;int&gt;
    /// </code></example>
    let errors (e: ValidationErrors) : Validation<'T> = Error e

    /// <summary>A failed validation carrying exactly one error.</summary>
    /// <example><code lang="fsharp">
    /// Validation.error (Path.root |> Path.field "age") ErrorReason.Missing "is required"
    /// </code></example>
    let error (path: Path) (reason: ErrorReason) (message: string) : Validation<'T> =
        Error(ValidationErrors.create path reason message)

    /// <summary>True when the value passed.</summary>
    /// <example><code lang="fsharp">
    /// Validation.isOk (Validation.ok 1) // true
    /// </code></example>
    let isOk (v: Validation<'T>) =
        match v with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>True when the value was rejected.</summary>
    /// <example><code lang="fsharp">
    /// Validation.isError (Validation.ok 1) // false
    /// </code></example>
    let isError (v: Validation<'T>) = not (isOk v)

    /// <summary>Transforms a value that passed, leaving errors untouched.</summary>
    /// <example><code lang="fsharp">
    /// Validation.ok 2 |> Validation.map ((*) 10) // Ok 20
    /// </code></example>
    let map (f: 'T -> 'U) (v: Validation<'T>) : Validation<'U> =
        match v with
        | Ok value -> Ok(f value)
        | Error e -> Error e

    /// <summary>Rewrites the errors, leaving a passing value untouched.</summary>
    /// <example><code lang="fsharp">
    /// v |> Validation.mapErrors (ValidationErrors.underField "address")
    /// </code></example>
    let mapErrors (f: ValidationErrors -> ValidationErrors) (v: Validation<'T>) : Validation<'T> =
        match v with
        | Ok value -> Ok value
        | Error e -> Error(f e)

    /// <summary>
    /// Runs a second validation that needs the first one's value. This is the one
    /// combinator that cannot accumulate: if the first step failed there is no value
    /// to give the second.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Validation.ok "42" |> Validation.bind parseAge
    /// </code></example>
    let bind (f: 'T -> Validation<'U>) (v: Validation<'T>) : Validation<'U> =
        match v with
        | Ok value -> f value
        | Error e -> Error e

    /// <summary>
    /// Applies a validated function to a validated argument, collecting the errors of
    /// both sides. This is the primitive every other accumulating combinator is built on.
    /// </summary>
    /// <example><code lang="fsharp">
    /// // two bad fields produce two errors, not one
    /// Validation.apply (Validation.map (+) badA) badB |> Validation.errorCount // 2
    /// </code></example>
    let apply (f: Validation<'T -> 'U>) (v: Validation<'T>) : Validation<'U> =
        match f, v with
        | Ok g, Ok value -> Ok(g value)
        | Error e1, Error e2 -> Error(ValidationErrors.append e1 e2)
        | Error e, Ok _ -> Error e
        | Ok _, Error e -> Error e

    /// <summary>Combines two validations, collecting errors from both.</summary>
    /// <example><code lang="fsharp">
    /// Validation.map2 (fun a b -> a + b) (Validation.ok 1) (Validation.ok 2) // Ok 3
    /// </code></example>
    let map2 (f: 'A -> 'B -> 'C) (a: Validation<'A>) (b: Validation<'B>) : Validation<'C> = apply (map f a) b

    /// <summary>Combines three validations, collecting errors from all of them.</summary>
    /// <example><code lang="fsharp">
    /// Validation.map3 (fun a b c -> a + b + c) (Validation.ok 1) (Validation.ok 2) (Validation.ok 3)
    /// // Ok 6
    /// </code></example>
    let map3 (f: 'A -> 'B -> 'C -> 'D) (a: Validation<'A>) (b: Validation<'B>) (c: Validation<'C>) : Validation<'D> =
        apply (apply (map f a) b) c

    /// <summary>Pairs two validations, collecting errors from both.</summary>
    /// <example><code lang="fsharp">
    /// Validation.zip (Validation.ok 1) (Validation.ok "a") // Ok (1, "a")
    /// </code></example>
    let zip (a: Validation<'A>) (b: Validation<'B>) : Validation<'A * 'B> = map2 (fun x y -> x, y) a b

    /// <summary>
    /// Validates every element, collecting errors from all of them. Error paths are left
    /// alone; use <see cref="M:Etymon.Validation.traverseIndexed"/> when they should carry
    /// the element position.
    /// </summary>
    /// <example><code lang="fsharp">
    /// [ "1"; "x"; "y" ] |> Validation.traverse parseInt |> Validation.errorCount // 2
    /// </code></example>
    let traverse (f: 'T -> Validation<'U>) (items: 'T list) : Validation<'U list> =
        let mutable oks = []
        let mutable errs = []

        for item in items do
            match f item with
            | Ok value -> oks <- value :: oks
            | Error e -> errs <- e :: errs

        match errs with
        | [] -> Ok(List.rev oks)
        | _ -> Error(errs |> List.rev |> List.reduce ValidationErrors.append)

    /// <summary>
    /// Validates every element, collecting errors from all of them and re-rooting each
    /// element's errors under its position.
    /// </summary>
    /// <example><code lang="fsharp">
    /// [ "1"; "x" ] |> Validation.traverseIndexed (fun _ s -> parseInt s)
    /// // Error [ "[1]: must be a number" ]
    /// </code></example>
    let traverseIndexed (f: int -> 'T -> Validation<'U>) (items: 'T list) : Validation<'U list> =
        items
        |> List.mapi (fun i item -> f i item |> mapErrors (ValidationErrors.underIndex i))
        |> traverse id

    /// <summary>Collects a list of validations into a validation of a list.</summary>
    /// <example><code lang="fsharp">
    /// Validation.sequence [ Validation.ok 1; Validation.ok 2 ] // Ok [ 1; 2 ]
    /// </code></example>
    let sequence (items: Validation<'T> list) : Validation<'T list> = traverse id items

    /// <summary>Re-roots every error one step deeper.</summary>
    /// <example><code lang="fsharp">
    /// v |> Validation.under (PathSegment.Field "address")
    /// </code></example>
    let under (segment: PathSegment) (v: Validation<'T>) : Validation<'T> =
        mapErrors (ValidationErrors.under segment) v

    /// <summary>Re-roots every error under a named field.</summary>
    /// <example><code lang="fsharp">
    /// decodeZip json |> Validation.underField "address" // errors read "address.zip: ..."
    /// </code></example>
    let underField (name: string) (v: Validation<'T>) : Validation<'T> =
        mapErrors (ValidationErrors.underField name) v

    /// <summary>Re-roots every error under a collection position.</summary>
    /// <example><code lang="fsharp">
    /// decodeItem json |> Validation.underIndex 3 // errors read "[3]: ..."
    /// </code></example>
    let underIndex (position: int) (v: Validation<'T>) : Validation<'T> =
        mapErrors (ValidationErrors.underIndex position) v

    /// <summary>Turns an absent value into a validation failure.</summary>
    /// <example><code lang="fsharp">
    /// None |> Validation.ofOption (Path.root |> Path.field "name") ErrorReason.Missing "is required"
    /// </code></example>
    let ofOption (path: Path) (reason: ErrorReason) (message: string) (value: 'T option) : Validation<'T> =
        match value with
        | Some v -> Ok v
        | None -> error path reason message

    /// <summary>Lifts a plain <c>Result</c> by describing how its error becomes a validation error.</summary>
    /// <example><code lang="fsharp">
    /// Error "nope" |> Validation.ofResult (fun m -> { Path = Path.root; Reason = ErrorReason.Rejected "nope"; Message = m })
    /// </code></example>
    let ofResult (toError: 'E -> ValidationError) (result: Result<'T, 'E>) : Validation<'T> =
        match result with
        | Ok value -> Ok value
        | Error e -> Error(ValidationErrors.one (toError e))

    /// <summary>The value, or a fallback if it was rejected.</summary>
    /// <example><code lang="fsharp">
    /// Validation.defaultValue 0 (Validation.error Path.root ErrorReason.Missing "x") // 0
    /// </code></example>
    let defaultValue (fallback: 'T) (v: Validation<'T>) =
        match v with
        | Ok value -> value
        | Error _ -> fallback

    /// <summary>The value, or a fallback computed from the errors.</summary>
    /// <example><code lang="fsharp">
    /// v |> Validation.defaultWith (fun errs -> failwith (ValidationErrors.format errs))
    /// </code></example>
    let defaultWith (fallback: ValidationErrors -> 'T) (v: Validation<'T>) =
        match v with
        | Ok value -> value
        | Error e -> fallback e

    /// <summary>How many things were wrong; zero when the value passed.</summary>
    /// <example><code lang="fsharp">
    /// Validation.errorCount (Validation.ok 1) // 0
    /// </code></example>
    let errorCount (v: Validation<'T>) =
        match v with
        | Ok _ -> 0
        | Error e -> ValidationErrors.count e

    /// <summary>Every error, or an empty list when the value passed.</summary>
    /// <example><code lang="fsharp">
    /// Validation.errorList v |> List.map (fun e -> Path.toString e.Path)
    /// </code></example>
    let errorList (v: Validation<'T>) =
        match v with
        | Ok _ -> []
        | Error e -> ValidationErrors.toList e

/// <summary>
/// The computation expression for <see cref="T:Etymon.Validation`1"/>.
/// </summary>
/// <remarks>
/// Use <c>let!</c> with <c>and!</c> to validate independent things and collect every
/// error. A plain sequence of <c>let!</c> bindings uses <c>bind</c> instead and will
/// stop at the first failure, which is almost never what you want here.
/// </remarks>
type ValidationBuilder() =
    /// Wraps a plain value.
    member _.Return(value: 'T) : Validation<'T> = Ok value

    /// Passes a validation through unchanged.
    member _.ReturnFrom(v: Validation<'T>) : Validation<'T> = v

    /// Sequential binding. Short-circuits, because the second step needs the first value.
    member _.Bind(v: Validation<'T>, f: 'T -> Validation<'U>) : Validation<'U> = Validation.bind f v

    /// The final mapping step of an applicative chain.
    member _.BindReturn(v: Validation<'T>, f: 'T -> 'U) : Validation<'U> = Validation.map f v

    /// Combines two independent validations, collecting errors from both. This is what
    /// makes <c>and!</c> accumulate.
    member _.MergeSources(a: Validation<'A>, b: Validation<'B>) : Validation<'A * 'B> = Validation.zip a b

    /// Identity on sources.
    member _.Source(v: Validation<'T>) : Validation<'T> = v

/// Exposes the <c>validation { ... }</c> computation expression.
[<AutoOpen>]
module ValidationBuilderExtensions =

    /// <summary>
    /// Validates several things at once, collecting every error.
    /// </summary>
    /// <example><code lang="fsharp">
    /// validation {
    ///     let! name = NonEmpty.create raw.Name |> Validation.underField "name"
    ///     and! age = Age.create raw.Age |> Validation.underField "age"
    ///     return { Name = name; Age = age }
    /// }
    /// // a bad name AND a bad age produce two errors
    /// </code></example>
    let validation = ValidationBuilder()
