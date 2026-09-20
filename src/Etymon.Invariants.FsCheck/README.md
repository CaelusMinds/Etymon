# Etymon.Invariants.FsCheck

**FsCheck generators that produce only values your schema accepts — and values it
should reject, for testing that it does.**

Part of the [Etymon](https://github.com/CaelusMinds/Etymon) suite. Depends on
`Etymon.Core`, `Etymon.Schema`, `Etymon.Invariants` and FsCheck.

```fsharp
Generate.valid personSchema           // Gen<Person>, all of them decodable
Generate.arbitrary personSchema       // the same, as an Arbitrary
Generate.invalid personSchema         // values that should be rejected, and where
Generate.validSatisfying invariants bookingSchema  // legal bookings, not merely decodable ones
```

## How a value is known to be valid

Values are generated as **JSON and decoded through the schema itself**, rather
than constructed directly. That reuses the decoder — already the most heavily
tested code in the suite — instead of adding a second construction path that
could drift away from it. A generated value is valid by exactly the definition
production code uses.

Constraints are satisfied **constructively** wherever Etymon understands them: a
length bound picks a string of that length, a range picks a number inside it, an
enumeration picks a member, `Format.Email` builds something with an `@` and a dot
rather than hoping.

## It fails loudly rather than quietly

Some rules can only be filtered for — a regular expression, an `Opaque`
predicate, a cross-field invariant. Where filtering runs out of budget, this
raises `GenerationFailed` with the rule's name and what to do about it.

That is deliberate. The normal behaviour of a filtered generator is to discard
and carry on, so a property test that should have run a hundred cases runs
eleven, passes, and tells you so confidently. A loud failure is worth much more
than a thin sample.

```
Could not generate a value of Booking satisfying its invariants
(endDate-after-startDate) in 500 attempts. Cross-field rules usually cannot be
satisfied by chance; supply a generator that constructs valid values directly.
```

The same applies to a recursive schema, which has no fixed point a generator can
reach, and to an unsatisfiable constraint like a range whose floor is above its
ceiling.

## Negative testing that checks more than "it failed"

`Generate.invalid` produces values the schema should reject, each carrying what
is wrong with it and **where the error should land**:

```fsharp
{ Description = "the required field 'email' removed"
  Json = """{"tags":[]}"""
  ExpectedPath = "email"
  ExpectedReason = ErrorReason.Missing }
```

Asserting only that decoding failed would pass just as well when it failed for
the wrong reason. Checking the path is what makes the assertion worth making.

## Ready-made properties

```fsharp
testProperty "Person round-trips"      (fun () -> Properties.roundTrips Person.schema)
testProperty "encoding is stable"      (fun () -> Properties.isDeterministic Person.schema)
testProperty "bad input is rejected"   (fun () -> Properties.invalidIsRejected Person.schema)
testProperty "everything at once"      (fun () -> Properties.all Person.schema)

testProperty "generated bookings are legal" (fun () ->
    Properties.generatedValuesSatisfy Booking.invariants Booking.schema)
```

`generatedValuesSatisfy` is the one that earns its keep. A failure means the
schema is **looser than the type**: something the decoder lets through is not a
legal value, which is precisely the gap a smart constructor exists to close.

## Licence

[MIT](https://github.com/CaelusMinds/Etymon/blob/main/LICENSE).
