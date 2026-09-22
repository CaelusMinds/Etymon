
namespace FSharp



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
          Holds: ('T -> bool)
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
        val create:
          name: string ->
            description: string -> holds: ('T -> bool) -> Invariant<'T>
        
        /// <summary>
        /// Records which fields a rule concerns. The first is where the error lands;
        /// the rest are carried as data so that a caller can highlight them too.
        /// </summary>
        /// <example><code lang="fsharp">
        /// invariant |> Invariant.about [ "endDate"; "startDate" ]
        /// </code></example>
        val about:
          fields: string list -> invariant: Invariant<'T> -> Invariant<'T>
        
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
        val compare:
          name: string ->
            description: string ->
            string * ('T -> 'A) ->
              string * ('T -> 'A) ->
                satisfies: ('A -> 'A -> bool) -> Invariant<'T>
        
        /// <summary>
        /// Requires one part of a value to be strictly after another. The rule
        /// everybody writes first, and gets subtly wrong by allowing equality.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Invariant.after ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)
        /// // "endDate must be after startDate"; equal values are rejected
        /// </code></example>
        val after:
          string * ('T -> 'A) -> string * ('T -> 'A) -> Invariant<'T>
            when 'A: comparison
        
        /// <summary>Requires one part of a value to be at or after another.</summary>
        /// <example><code lang="fsharp">
        /// Invariant.notBefore ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)
        /// // equal values are allowed
        /// </code></example>
        val notBefore:
          string * ('T -> 'A) -> string * ('T -> 'A) -> Invariant<'T>
            when 'A: comparison
        
        /// <summary>
        /// Requires a set of optional fields to be either all present or all absent.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Invariant.allOrNone "billing-address"
        ///     [ "billingStreet", (fun o -> o.BillingStreet.IsSome)
        ///       "billingZip", (fun o -> o.BillingZip.IsSome) ]
        /// </code></example>
        val allOrNone:
          name: string -> fields: (string * ('T -> bool)) list -> Invariant<'T>
        
        /// <summary>Requires at most one of a set of fields to be present.</summary>
        /// <example><code lang="fsharp">
        /// Invariant.atMostOne "one-payment-method"
        ///     [ "cardId", (fun o -> o.CardId.IsSome); "invoiceId", (fun o -> o.InvoiceId.IsSome) ]
        /// </code></example>
        val atMostOne:
          name: string -> fields: (string * ('T -> bool)) list -> Invariant<'T>
        
        /// <summary>Requires exactly one of a set of fields to be present.</summary>
        /// <example><code lang="fsharp">
        /// Invariant.exactlyOne "a-payment-method"
        ///     [ "cardId", (fun o -> o.CardId.IsSome); "invoiceId", (fun o -> o.InvoiceId.IsSome) ]
        /// </code></example>
        val exactlyOne:
          name: string -> fields: (string * ('T -> bool)) list -> Invariant<'T>
        
        /// <summary>
        /// A rule that only applies in some circumstances. Stated as a pair rather
        /// than as <c>not p || q</c> so that the intent survives being read later.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Invariant.whenever "shipping-needs-address" "an order that ships must have an address"
        ///     (fun o -> o.RequiresShipping) (fun o -> o.Address.IsSome)
        /// </code></example>
        val whenever:
          name: string ->
            description: string ->
            applies: ('T -> bool) -> holds: ('T -> bool) -> Invariant<'T>
        
        /// <summary>
        /// The error a broken rule produces, at a given path. It lands on the first
        /// field the rule concerns, or on the value itself when it concerns none.
        /// </summary>
        /// <example><code lang="fsharp">
        /// (Invariant.toError booking Path.root invariant).ToString()
        /// // "endDate: endDate must be after startDate"
        /// </code></example>
        val toError: path: Path -> invariant: Invariant<'T> -> ValidationError
        
        /// <summary>Runs one rule, returning its error if it does not hold.</summary>
        /// <example><code lang="fsharp">
        /// Invariant.check Path.root invariant booking // None when it holds
        /// </code></example>
        val check:
          path: Path ->
            invariant: Invariant<'T> -> value: 'T -> ValidationError option

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
        val empty: typeName: string -> Invariants<'T>
        
        /// <summary>All the rules for a type.</summary>
        /// <example><code lang="fsharp">
        /// Invariants.forType "Booking" [
        ///     Invariant.after ("endDate", fun b -> b.EndDate) ("startDate", fun b -> b.StartDate)
        /// ]
        /// </code></example>
        val forType:
          typeName: string -> rules: Invariant<'T> list -> Invariants<'T>
        
        /// <summary>Adds a rule, keeping declaration order.</summary>
        /// <example><code lang="fsharp">
        /// Invariants.empty "Booking" |> Invariants.add guestCountRule
        /// </code></example>
        val add:
          rule: Invariant<'T> -> invariants: Invariants<'T> -> Invariants<'T>
        
        /// <summary>The rules, as data.</summary>
        /// <example><code lang="fsharp">
        /// Invariants.rules bookingInvariants |> List.map (fun r -> r.Name)
        /// // [ "endDate-after-startDate" ]
        /// </code></example>
        val rules: invariants: Invariants<'T> -> Invariant<'T> list
        
        /// <summary>
        /// One line per rule, for documentation or an error page. Every rule can
        /// describe itself, so nothing has to be written twice.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Invariants.describe bookingInvariants
        /// // [ "endDate must be after startDate" ]
        /// </code></example>
        val describe: invariants: Invariants<'T> -> string list
        
        /// <summary>
        /// Runs every rule at a given path, collecting every failure. Nothing
        /// short-circuits: a value breaking three rules reports three errors.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Invariants.checkAt (Path.root |> Path.field "booking") bookingInvariants value
        /// // Error [ "booking.endDate: endDate must be after startDate" ]
        /// </code></example>
        val checkAt:
          path: Path ->
            invariants: Invariants<'T> -> value: 'T -> Validation<'T>
        
        /// <summary>Runs every rule, collecting every failure.</summary>
        /// <example><code lang="fsharp">
        /// Invariants.check bookingInvariants booking |> Validation.errorCount // 0 when valid
        /// </code></example>
        val check: invariants: Invariants<'T> -> value: 'T -> Validation<'T>
        
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
        val enforce: invariants: Invariants<'T> -> value: 'T -> Validation<'T>
        
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
        val andThen:
          invariants: Invariants<'T> ->
            validated: Validation<'T> -> Validation<'T>

