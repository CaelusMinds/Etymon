namespace Etymon

/// <summary>
/// Every rule that must hold of a type, declared once and exposed as data.
/// </summary>
/// <remarks>
/// <para>
/// Declaring the rules in one value is what lets other packages read them:
/// <c>Etymon.Invariants.FsCheck</c> generates values that satisfy them,
/// <c>Etymon.Schema.Sql</c> can turn the expressible ones into database checks,
/// and documentation can list them. A rule written inline inside a constructor
/// is a rule nothing else can see.
/// </para>
/// <para>
/// Etymon cannot make a constructor call these — F# has no mechanism for that
/// short of a compiler plugin, and a library that claimed otherwise would be
/// lying. What it can do is make the call one line, and make the rules visible
/// so that a missing call is obvious. See <see cref="M:Etymon.Invariants.enforce"/>.
/// </para>
/// </remarks>
[<NoEquality; NoComparison>]
type Invariants<'T> =
    {
        /// The type these rules are about, used in documentation and messages.
        TypeName: string
        /// The rules, in the order they were declared. Every one runs.
        Rules: Invariant<'T> list
    }

/// Building and running a set of <see cref="T:Etymon.Invariant`1"/> rules.
[<RequireQualifiedAccess>]
module Invariants =

    /// <summary>A type with no rules yet.</summary>
    /// <example><code lang="fsharp">
    /// Invariants.empty "Booking"
    /// </code></example>
    let empty (typeName: string) : Invariants<'T> = { TypeName = typeName; Rules = [] }

    /// <summary>All the rules for a type.</summary>
    /// <example><code lang="fsharp">
    /// Invariants.forType "Booking" [
    ///     Invariant.after ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)
    /// ]
    /// </code></example>
    let forType (typeName: string) (rules: Invariant<'T> list) : Invariants<'T> = { TypeName = typeName; Rules = rules }

    /// <summary>Adds a rule, keeping declaration order.</summary>
    /// <example><code lang="fsharp">
    /// Invariants.empty "Booking" |> Invariants.add guestCountRule
    /// </code></example>
    let add (rule: Invariant<'T>) (invariants: Invariants<'T>) =
        { invariants with
            Rules = invariants.Rules @ [ rule ]
        }

    /// <summary>The rules, as data.</summary>
    /// <example><code lang="fsharp">
    /// Invariants.rules bookingInvariants |> List.map (fun r -> r.Name)
    /// // [ "endDate-after-startDate" ]
    /// </code></example>
    let rules (invariants: Invariants<'T>) = invariants.Rules

    /// <summary>
    /// One line per rule, for documentation or an error page. Every rule can
    /// describe itself, so nothing has to be written twice.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariants.describe bookingInvariants
    /// // [ "endDate must be after startDate" ]
    /// </code></example>
    let describe (invariants: Invariants<'T>) =
        invariants.Rules |> List.map (fun rule -> rule.Description)

    /// <summary>
    /// Runs every rule at a given path, collecting every failure. Nothing
    /// short-circuits: a value breaking three rules reports three errors.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Invariants.checkAt (Path.root |> Path.field "booking") bookingInvariants value
    /// // Error [ "booking.endDate: endDate must be after startDate" ]
    /// </code></example>
    let checkAt (path: Path) (invariants: Invariants<'T>) (value: 'T) : Validation<'T> =
        match invariants.Rules |> List.choose (fun rule -> Invariant.check path rule value) with
        | [] -> Ok value
        | first :: rest -> Error(ValidationErrors.ofHeadTail first rest)

    /// <summary>Runs every rule, collecting every failure.</summary>
    /// <example><code lang="fsharp">
    /// Invariants.check bookingInvariants booking |> Validation.errorCount // 0 when valid
    /// </code></example>
    let check (invariants: Invariants<'T>) (value: 'T) : Validation<'T> = checkAt Path.root invariants value

    /// <summary>
    /// What a smart constructor calls. The same as
    /// <see cref="M:Etymon.Invariants.check"/>, named for the place it belongs so
    /// that its absence from a constructor reads as an omission.
    /// </summary>
    /// <remarks>
    /// Etymon cannot force a constructor to call this. What it can do is make the
    /// call one line and the rules visible, so that a type whose constructor does
    /// not enforce its own invariants is something a reviewer can see rather than
    /// something they have to reconstruct.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// type Booking = private Booking of BookingFields
    ///
    /// module Booking =
    ///     let create fields =
    ///         Invariants.enforce invariants fields |> Validation.map Booking
    /// </code></example>
    let enforce (invariants: Invariants<'T>) (value: 'T) : Validation<'T> = check invariants value

    /// <summary>
    /// Applies the rules to a value that has already passed its field-level
    /// checks, so that field errors and cross-field errors arrive together
    /// rather than in two rounds.
    /// </summary>
    /// <remarks>
    /// The rules cannot run on a value that was never built, so a field failure
    /// still suppresses them. This is the one place in Etymon where an error
    /// hides another, and it is unavoidable: there is nothing to check.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Schema.fromJson bookingSchema payload
    /// |> Invariants.andThen bookingInvariants
    /// </code></example>
    let andThen (invariants: Invariants<'T>) (validated: Validation<'T>) : Validation<'T> =
        validated |> Validation.bind (check invariants)
