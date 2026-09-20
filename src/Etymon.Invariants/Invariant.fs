namespace Etymon

/// <summary>
/// A rule that must always be true of a whole value.
/// </summary>
/// <remarks>
/// <para>
/// Single-field rules already have a home: <see cref="T:Etymon.Constraint"/> in
/// <c>Etymon.Core</c>, attached to the field they restrict. An invariant is for
/// the rules that cannot live on any one field — <c>EndDate</c> must be after
/// <c>StartDate</c>, a discount may not exceed a total, either both of two fields
/// are present or neither is.
/// </para>
/// <para>
/// The rule is data as well as behaviour. <c>Fields</c> says which parts of the
/// value it concerns, so an error can be reported against the right input and a
/// form can highlight every field involved; <c>Name</c> is a stable code a caller
/// can branch on without reading prose.
/// </para>
/// </remarks>
[<NoEquality; NoComparison>]
type Invariant<'T> =
    {
        /// A stable identifier, used as the error code. Kebab case by convention:
        /// <c>end-after-start</c>.
        Name: string
        /// What the rule requires, phrased so that it reads as a statement about
        /// the value: "end date must be after start date".
        Description: string
        /// The fields the rule concerns. The first one is where an error is
        /// reported; all of them are worth highlighting in a user interface.
        Fields: string list
        /// Whether a value satisfies the rule.
        Holds: 'T -> bool
    }

/// Building and running a single <see cref="T:Etymon.Invariant`1"/>.
[<RequireQualifiedAccess>]
module Invariant =

    /// <summary>
    /// A rule about a whole value, concerning no particular field. Errors are
    /// reported against the value itself.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariant.create "non-empty-order" "an order must contain at least one line" (fun o ->
    ///     not (List.isEmpty o.Lines))
    /// </code></example>
    let create (name: string) (description: string) (holds: 'T -> bool) : Invariant<'T> =
        {
            Name = name
            Description = description
            Fields = []
            Holds = holds
        }

    /// <summary>
    /// Records which fields a rule concerns. The first is where the error lands;
    /// the rest are carried as data so that a caller can highlight them too.
    /// </summary>
    /// <example><code lang="fsharp">
    /// invariant |> Invariant.about [ "endDate"; "startDate" ]
    /// </code></example>
    let about (fields: string list) (invariant: Invariant<'T>) = { invariant with Fields = fields }

    /// <summary>
    /// A rule comparing two parts of a value. The commonest shape by far, and
    /// worth having so that the fields and the comparison cannot disagree.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariant.compare
    ///     "discount-within-total" "discount must not exceed the total"
    ///     ("discount", fun o -> o.Discount) ("total", fun o -> o.Total)
    ///     (&lt;=)
    /// </code></example>
    let compare
        (name: string)
        (description: string)
        (left: string * ('T -> 'A))
        (right: string * ('T -> 'A))
        (satisfies: 'A -> 'A -> bool)
        : Invariant<'T>
        =
        let leftName, getLeft = left
        let rightName, getRight = right

        {
            Name = name
            Description = description
            Fields = [ leftName; rightName ]
            Holds = fun value -> satisfies (getLeft value) (getRight value)
        }

    /// <summary>
    /// Requires one part of a value to be strictly after another. The rule
    /// everybody writes first, and gets subtly wrong by allowing equality.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariant.after ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)
    /// // "endDate must be after startDate"; equal values are rejected
    /// </code></example>
    let after (later: string * ('T -> 'A)) (earlier: string * ('T -> 'A)) : Invariant<'T> =
        let laterName, _ = later
        let earlierName, _ = earlier

        compare
            $"%s{laterName}-after-%s{earlierName}"
            $"%s{laterName} must be after %s{earlierName}"
            later
            earlier
            (fun l e -> l > e)

    /// <summary>Requires one part of a value to be at or after another.</summary>
    /// <example><code lang="fsharp">
    /// Invariant.notBefore ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)
    /// // equal values are allowed
    /// </code></example>
    let notBefore (later: string * ('T -> 'A)) (earlier: string * ('T -> 'A)) : Invariant<'T> =
        let laterName, _ = later
        let earlierName, _ = earlier

        compare
            $"%s{laterName}-not-before-%s{earlierName}"
            $"%s{laterName} must not be before %s{earlierName}"
            later
            earlier
            (fun l e -> l >= e)

    /// <summary>
    /// Requires a set of optional fields to be either all present or all absent.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariant.allOrNone "billing-address"
    ///     [ "billingStreet", (fun o -> o.BillingStreet.IsSome)
    ///       "billingZip", (fun o -> o.BillingZip.IsSome) ]
    /// </code></example>
    let allOrNone (name: string) (fields: (string * ('T -> bool)) list) : Invariant<'T> =
        let names = fields |> List.map fst

        {
            Name = name
            Description =
                "either all of "
                + System.String.Join(", ", names)
                + " must be given, or none of them"
            Fields = names
            Holds =
                fun value ->
                    let present = fields |> List.filter (fun (_, isPresent) -> isPresent value)
                    List.isEmpty present || List.length present = List.length fields
        }

    /// <summary>Requires at most one of a set of fields to be present.</summary>
    /// <example><code lang="fsharp">
    /// Invariant.atMostOne "one-payment-method"
    ///     [ "cardId", (fun o -> o.CardId.IsSome); "invoiceId", (fun o -> o.InvoiceId.IsSome) ]
    /// </code></example>
    let atMostOne (name: string) (fields: (string * ('T -> bool)) list) : Invariant<'T> =
        let names = fields |> List.map fst

        {
            Name = name
            Description = "at most one of " + System.String.Join(", ", names) + " may be given"
            Fields = names
            Holds =
                fun value ->
                    fields |> List.filter (fun (_, isPresent) -> isPresent value) |> List.length
                    <= 1
        }

    /// <summary>Requires exactly one of a set of fields to be present.</summary>
    /// <example><code lang="fsharp">
    /// Invariant.exactlyOne "a-payment-method"
    ///     [ "cardId", (fun o -> o.CardId.IsSome); "invoiceId", (fun o -> o.InvoiceId.IsSome) ]
    /// </code></example>
    let exactlyOne (name: string) (fields: (string * ('T -> bool)) list) : Invariant<'T> =
        let names = fields |> List.map fst

        {
            Name = name
            Description = "exactly one of " + System.String.Join(", ", names) + " must be given"
            Fields = names
            Holds = fun value -> fields |> List.filter (fun (_, isPresent) -> isPresent value) |> List.length = 1
        }

    /// <summary>
    /// A rule that only applies in some circumstances. Stated as a pair rather
    /// than as <c>not p || q</c> so that the intent survives being read later.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariant.whenever "shipping-needs-address" "an order that ships must have an address"
    ///     (fun o -> o.RequiresShipping) (fun o -> o.Address.IsSome)
    /// </code></example>
    let whenever (name: string) (description: string) (applies: 'T -> bool) (holds: 'T -> bool) : Invariant<'T> =
        {
            Name = name
            Description = description
            Fields = []
            Holds = fun value -> not (applies value) || holds value
        }

    /// <summary>
    /// The error a broken rule produces, at a given path. It lands on the first
    /// field the rule concerns, or on the value itself when it concerns none.
    /// </summary>
    /// <example><code lang="fsharp">
    /// (Invariant.toError booking Path.root invariant).ToString()
    /// // "endDate: endDate must be after startDate"
    /// </code></example>
    let toError (path: Path) (invariant: Invariant<'T>) : ValidationError =
        let target =
            match invariant.Fields with
            | [] -> path
            | first :: _ -> Path.field first path

        {
            Path = target
            Reason = ErrorReason.Rejected invariant.Name
            Message = invariant.Description
        }

    /// <summary>Runs one rule, returning its error if it does not hold.</summary>
    /// <example><code lang="fsharp">
    /// Invariant.check Path.root invariant booking // None when it holds
    /// </code></example>
    let check (path: Path) (invariant: Invariant<'T>) (value: 'T) =
        if invariant.Holds value then
            None
        else
            Some(toError path invariant)
