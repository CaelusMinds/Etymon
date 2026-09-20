namespace Etymon

open System
open System.Text.Json.Nodes
open FsCheck
open FsCheck.FSharp

/// <summary>
/// Why a generator could not produce a value.
/// </summary>
/// <remarks>
/// Raised rather than returned. A generator that cannot satisfy a schema is a
/// mistake in the schema or the generator, not an outcome a property test can
/// meaningfully handle — and silently producing fewer cases, which is what
/// filtering normally does, hides the mistake until the day the test was needed.
/// </remarks>
exception GenerationFailed of constraintName: string * attempts: int * message: string

/// <summary>
/// Generates JSON that satisfies a <see cref="T:Etymon.SchemaInfo"/>.
/// </summary>
/// <remarks>
/// <para>
/// Values are generated as JSON and then decoded through the schema, rather than
/// being built directly. That reuses the decoder — already the most heavily
/// tested code in the suite — instead of writing a second construction path that
/// could disagree with it. A generated value is valid by the same definition of
/// valid that production code uses.
/// </para>
/// <para>
/// Constraints are satisfied constructively wherever Etymon understands them
/// well enough: a length bound picks a string of that length, a range picks a
/// number inside it, an enumeration picks a member. Where it cannot — a regular
/// expression, or an <c>Opaque</c> predicate — it generates and filters, and
/// raises <see cref="T:Etymon.GenerationFailed"/> if the budget runs out rather
/// than quietly yielding a thinner sample.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module JsonGen =

    /// How many times to retry a constraint that can only be satisfied by
    /// filtering. Generous, because failing is much worse than being slow, and
    /// still finite, because hanging is worse than either.
    [<Literal>]
    let private FilterAttempts = 300

    let private node (value: string) : JsonNode = JsonValue.Create value
    let private numberNode (value: int64) : JsonNode = JsonValue.Create value
    let private decimalNode (value: decimal) : JsonNode = JsonValue.Create value
    let private floatNode (value: float) : JsonNode = JsonValue.Create value
    let private boolNode (value: bool) : JsonNode = JsonValue.Create value

    // ---- reading constraints -------------------------------------------------

    let private lengthBounds (constraints: Constraint list) =
        constraints
        |> List.tryPick (
            function
            | Constraint.Length(min, max) -> Some(defaultArg min 0, defaultArg max 32)
            | _ -> None
        )
        |> Option.defaultValue (0, 24)

    let private itemBounds (constraints: Constraint list) =
        constraints
        |> List.tryPick (
            function
            | Constraint.Items(min, max, _) -> Some(defaultArg min 0, defaultArg max 5)
            | _ -> None
        )
        |> Option.defaultValue (0, 5)

    let private mustBeDistinct (constraints: Constraint list) =
        constraints
        |> List.exists (
            function
            | Constraint.Items(_, _, unique) -> unique
            | _ -> false
        )

    let private enumValues (constraints: Constraint list) =
        constraints
        |> List.tryPick (
            function
            | Constraint.OneOf values when not (List.isEmpty values) -> Some values
            | _ -> None
        )

    let private formatOf (constraints: Constraint list) =
        constraints
        |> List.tryPick (
            function
            | Constraint.HasFormat format -> Some format
            | _ -> None
        )

    let private rangeOf (constraints: Constraint list) =
        constraints
        |> List.tryPick (
            function
            | Constraint.Range(min, max, exMin, exMax) -> Some(min, max, exMin, exMax)
            | _ -> None
        )

    // ---- constructive generators for the named formats -----------------------

    let private letters = Gen.elements [ 'a' .. 'z' ]

    let private word (shortest: int) (longest: int) =
        gen {
            let low = max 1 shortest
            let high = max low longest
            let! length = Gen.choose (low, high)
            let! chars = Gen.listOfLength length letters
            return String(List.toArray chars)
        }

    let private formatGen (format: Format) : Gen<string> option =
        match format with
        | Format.Email ->
            Some(
                gen {
                    let! local = word 1 8
                    let! domain = word 2 8
                    let! tld = word 2 3
                    return $"%s{local}@%s{domain}.%s{tld}"
                }
            )
        | Format.Uuid -> Some(Gen.constant () |> Gen.map (fun () -> Guid.NewGuid().ToString "D"))
        | Format.DateTime ->
            Some(
                Gen.choose (0, 3_000_000)
                |> Gen.map (fun offset ->
                    DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(float offset).ToString
                        "yyyy-MM-ddTHH:mm:ssZ"
                )
            )
        | Format.Date ->
            Some(
                Gen.choose (0, 30000)
                |> Gen.map (fun offset -> DateOnly(2000, 1, 1).AddDays(offset).ToString "yyyy-MM-dd")
            )
        | Format.Time ->
            Some(
                Gen.choose (0, 86399)
                |> Gen.map (fun seconds ->
                    TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(float seconds)).ToString "HH:mm:ss"
                )
            )
        | Format.Duration -> Some(Gen.choose (1, 500) |> Gen.map (fun days -> $"P%d{days}D"))
        | Format.Uri ->
            Some(
                gen {
                    let! host = word 2 8
                    let! path = word 1 6
                    return $"https://%s{host}.example/%s{path}"
                }
            )
        | Format.UriReference -> Some(word 1 8 |> Gen.map (fun p -> $"/%s{p}"))
        | Format.Hostname ->
            Some(
                gen {
                    let! host = word 2 8
                    let! tld = word 2 3
                    return $"%s{host}.%s{tld}"
                }
            )
        | Format.Ipv4 ->
            Some(
                gen {
                    let! parts = Gen.listOfLength 4 (Gen.choose (0, 255))
                    return String.Join(".", parts)
                }
            )
        | Format.Ipv6 -> Some(Gen.choose (1, 65535) |> Gen.map (fun n -> $"2001:db8::%x{n}"))
        // Etymon does not know what this format means, so it cannot generate one.
        | Format.Custom _ -> None

    /// Applies the constraints that can only be checked, never constructed.
    /// Raises rather than thinning the sample, so that an impossible constraint
    /// is a loud failure at the moment it happens.
    let private filterBy
        (constraints: Constraint list)
        (satisfies: Constraint -> string -> bool)
        (source: Gen<string>)
        =
        let filtering =
            constraints
            |> List.filter (
                function
                | Constraint.Pattern _
                | Constraint.Opaque _ -> true
                | _ -> false
            )

        if List.isEmpty filtering then
            source
        else
            gen {
                let! candidates = Gen.listOfLength FilterAttempts source

                match
                    candidates
                    |> List.tryFind (fun c -> filtering |> List.forall (fun r -> satisfies r c))
                with
                | Some value -> return value
                | None ->
                    let name =
                        filtering
                        |> List.map (fun c ->
                            match c with
                            | Constraint.Pattern regex -> $"Pattern %s{regex}"
                            | Constraint.Opaque(code, _) -> $"Opaque %s{code}"
                            | other -> string other
                        )
                        |> String.concat ", "

                    return
                        raise (
                            GenerationFailed(
                                name,
                                FilterAttempts,
                                $"Could not generate a value satisfying %s{name} in %d{FilterAttempts} attempts. Etymon cannot construct a value for this kind of rule, only filter for one. Supply a generator for this field, or express the rule as a constraint Etymon understands."
                            )
                        )
            }

    // ---- the generator -------------------------------------------------------

    let private stringGen (constraints: Constraint list) =
        let satisfiesFiltered (c: Constraint) (candidate: string) =
            match c with
            | Constraint.Pattern regex -> (Check.pattern regex).Satisfies candidate
            | Constraint.Opaque(code, description) ->
                // The predicate is not carried in the constraint data -- only its
                // name is -- so there is nothing to test against. Anything
                // generated here is a guess, and saying so is better than pretending.
                raise (
                    GenerationFailed(
                        code,
                        0,
                        $"The rule '%s{description}' is opaque: its predicate is behaviour, not data, so a generator cannot see it. Supply a generator for this field explicitly."
                    )
                )
            | _ -> true

        let minLength, maxLength = lengthBounds constraints

        let baseGen =
            match enumValues constraints with
            | Some values -> Gen.elements values
            | None ->
                match formatOf constraints |> Option.bind formatGen with
                | Some formatted -> formatted
                | None -> word minLength maxLength

        baseGen |> filterBy constraints satisfiesFiltered |> Gen.map node

    let private intGen (constraints: Constraint list) (floor: int64) (ceiling: int64) =
        let low, high =
            match rangeOf constraints with
            | Some(min, max, exMin, exMax) ->
                let toInt64 n =
                    match n with
                    | Num.Int v -> v
                    | Num.Dec v -> int64 v
                    | Num.Float v -> int64 v

                let low =
                    min
                    |> Option.map (fun m -> toInt64 m + (if exMin then 1L else 0L))
                    |> Option.defaultValue floor

                let high =
                    max
                    |> Option.map (fun m -> toInt64 m - (if exMax then 1L else 0L))
                    |> Option.defaultValue ceiling

                low, high
            | None -> floor, ceiling

        if low > high then
            gen {
                return
                    raise (
                        GenerationFailed(
                            "Range",
                            0,
                            $"The range %d{low}..%d{high} contains no value, so no value can satisfy it."
                        )
                    )
            }
        else
            Gen.choose64 (low, high) |> Gen.map numberNode

    let private decimalGen (constraints: Constraint list) =
        let low, high =
            match rangeOf constraints with
            | Some(min, max, _, _) ->
                let toDecimal n =
                    match n with
                    | Num.Int v -> decimal v
                    | Num.Dec v -> v
                    | Num.Float v -> decimal v

                (min |> Option.map toDecimal |> Option.defaultValue -10000m),
                (max |> Option.map toDecimal |> Option.defaultValue 10000m)
            | None -> -10000m, 10000m

        // Two decimal places, which is what a decimal is nearly always for.
        let lowCents = int64 (low * 100m)
        let highCents = int64 (high * 100m)

        if lowCents > highCents then
            gen { return raise (GenerationFailed("Range", 0, "The range contains no value.")) }
        else
            Gen.choose64 (lowCents, highCents)
            |> Gen.map (fun cents -> decimalNode (decimal cents / 100m))

    /// <summary>
    /// A generator of JSON satisfying a schema description.
    /// </summary>
    /// <example><code lang="fsharp">
    /// JsonGen.forInfo (Schema.info personSchema) |> Gen.sample 1
    /// </code></example>
    let rec forInfo (info: SchemaInfo) : Gen<JsonNode> =
        let meta = SchemaInfo.meta info
        let constraints = meta.Constraints

        match SchemaInfo.strip info with
        | SPrim PrimKind.String -> stringGen constraints
        | SPrim PrimKind.Int -> intGen constraints -100000L 100000L
        | SPrim PrimKind.Int64 -> intGen constraints -1000000L 1000000L
        | SPrim PrimKind.Decimal -> decimalGen constraints
        | SPrim PrimKind.Float -> Gen.choose (-100000, 100000) |> Gen.map (fun n -> floatNode (float n / 100.0))
        | SPrim PrimKind.Bool -> Gen.elements [ true; false ] |> Gen.map boolNode
        | SPrim PrimKind.Guid -> Gen.constant () |> Gen.map (fun () -> node (Guid.NewGuid().ToString "D"))
        | SPrim PrimKind.DateTimeOffset -> (formatGen Format.DateTime).Value |> Gen.map node
        | SPrim PrimKind.DateOnly -> (formatGen Format.Date).Value |> Gen.map node
        | SPrim PrimKind.TimeOnly -> (formatGen Format.Time).Value |> Gen.map node
        | SPrim PrimKind.TimeSpan ->
            Gen.choose (0, 86399)
            |> Gen.map (fun s -> node (TimeSpan.FromSeconds(float s).ToString "c"))
        | SPrim PrimKind.Bytes ->
            gen {
                let! length = Gen.choose (0, 8)
                let! values = Gen.listOfLength length (Gen.choose (0, 255))
                return node (Convert.ToBase64String(values |> List.map byte |> List.toArray))
            }
        | SPrim PrimKind.Raw -> Gen.constant (numberNode 1L)

        | SNullable inner -> Gen.frequency [ 1, Gen.constant (null: JsonNode); 4, forInfo inner ]

        | SList inner ->
            let minItems, maxItems = itemBounds constraints
            let distinct = mustBeDistinct constraints

            gen {
                let! count = Gen.choose (minItems, max minItems maxItems)
                let! items = Gen.listOfLength count (forInfo inner)

                let items =
                    if distinct then
                        items
                        |> List.distinctBy (fun n -> if isNull n then "null" else n.ToJsonString())
                    else
                        items

                let array = JsonArray()

                for item in items do
                    array.Add(if isNull item then null else item.DeepClone())

                return array :> JsonNode
            }

        | SMap inner ->
            gen {
                let! count = Gen.choose (0, 4)
                let! keys = Gen.listOfLength count (word 1 6)
                let! values = Gen.listOfLength count (forInfo inner)
                let object = JsonObject()

                for key, value in
                    List.zip (List.distinct keys) (List.truncate (List.length (List.distinct keys)) values) do
                    object.Add(key, (if isNull value then null else value.DeepClone()))

                return object :> JsonNode
            }

        | SObject(_, fields) ->
            gen {
                let object = JsonObject()

                for field in fields do
                    let! include' =
                        if field.Required then
                            Gen.constant true
                        else
                            Gen.elements [ true; false ]

                    if include' then
                        let! value = forInfo field.Schema
                        object.Add(field.Name, (if isNull value then null else value.DeepClone()))

                return object :> JsonNode
            }

        | SUnion(_, tag, cases) ->
            gen {
                let! caseTag, casePayload = Gen.elements cases
                let object = JsonObject()
                object.Add(tag, node caseTag)

                match SchemaInfo.strip casePayload with
                | SPrim PrimKind.Raw -> ()
                | _ ->
                    let! value = forInfo casePayload
                    object.Add("value", (if isNull value then null else value.DeepClone()))

                return object :> JsonNode
            }

        | SRef name ->
            // A recursive schema has no fixed point a generator can reach without
            // a depth budget, and Etymon does not carry one here.
            raise (
                GenerationFailed(
                    name,
                    0,
                    $"Cannot generate a value for the recursive schema '%s{name}': a generator would not terminate. Supply one explicitly for this type."
                )
            )

        | SAnnotated _ -> failwith "unreachable: annotations are stripped before this point"
