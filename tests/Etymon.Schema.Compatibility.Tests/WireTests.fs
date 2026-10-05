/// The wire-contract policy: same matrix, different consequences.
///
/// The half that matters is responses. A broken request can be rescued by an
/// upcaster you write; a broken response breaks code you do not ship and cannot
/// change. These pin that the package says so plainly, and never implies
/// otherwise.
module Etymon.Schema.Compatibility.Tests.WireTests

open Expecto
open Etymon

let private field name t required : ShapeField =
    {
        Name = name
        Type = t
        Required = required
        Constraints = []
        ElementConstraints = Some []
        Default = Some None
    }

let private shape name version fields : Shape =
    {
        Name = name
        Version = version
        Fields = fields
    }

let private text = FieldType.Scalar "string"
let private number = FieldType.Scalar "int32"

/// A response DTO that loses a field: the case nothing can fix.
let private invoiceV1 =
    shape "InvoiceDto" 1 [ field "id" text true; field "taxMinorUnits" number true ]

let private invoiceV2 = shape "InvoiceDto" 2 [ field "id" text true ]

/// A request DTO that gains a required field: the case an upcaster can fix.
let private createV1 = shape "CreateBillRequest" 1 [ field "vendorId" text true ]

let private createV2 =
    shape "CreateBillRequest" 2 [ field "vendorId" text true; field "periodStart" text true ]

let private snap s = ShapeSnapshot.of' [ s ]

let tests =
    testList
        "Wire"
        [
            testList
                "responses: the ones nothing can fix"
                [
                    test "a removed response field breaks clients, and says so" {
                        let verdicts = Wire.responses [] (snap invoiceV1) (snap invoiceV2)

                        match verdicts with
                        | [ verdict ] ->
                            Expect.equal verdict.Contract "InvoiceDto" "the contract"
                            Expect.equal verdict.Side WireSide.Response "on the side they read"
                            Expect.isFalse verdict.Fixable "and nothing shipped can fix it"

                            Expect.stringContains
                                verdict.Consequence
                                "the code that breaks is theirs"
                                "which it says rather than implies"

                            Expect.stringContains
                                verdict.Consequence
                                "Version the endpoint"
                                "with the only real options"
                        | other -> failtestf "expected one verdict, got %A" other
                    }

                    test "the report names the audience, not the direction" {
                        // "Forward" and "backward" are the two words people
                        // reliably get the wrong way round, and this is read by
                        // somebody deciding whether to ship.
                        let report = Wire.report (Wire.responses [] (snap invoiceV1) (snap invoiceV2))

                        Expect.stringContains report "Clients already written" "who is affected"
                        Expect.stringContains report "they read what you send" "and which way it travels"
                        Expect.isFalse (report.Contains "Forward") "the word nobody reads correctly is absent"
                    }

                    test "unfixable filters to exactly the ones to decide before shipping" {
                        let breaks = Wire.responses [] (snap invoiceV1) (snap invoiceV2) |> Wire.unfixable

                        Expect.isNonEmpty breaks "this one has to be decided now"
                    }

                    test "a removed response shape breaks clients, and nothing can fix it" {
                        let before = ShapeSnapshot.of' [ invoiceV1; createV1 ]
                        let after = snap createV1

                        match Wire.responses [] before after |> Wire.unfixable with
                        | [ verdict ] ->
                            Expect.equal verdict.Contract "InvoiceDto" "the contract that went"
                            Expect.stringContains verdict.Change "was removed" "named as a removal"
                        | other -> failtestf "expected one unfixable verdict, got %A" other
                    }

                    test "a declared rename on a response breaks clients, and nothing can fix it" {
                        // The declaration settles the ambiguity; the verdict
                        // stays, because the client still reads the old name.
                        let renames =
                            [
                                {
                                    Shape = "InvoiceDto"
                                    From = "taxMinorUnits"
                                    To = "tax"
                                }
                            ]

                        let renamed = shape "InvoiceDto" 2 [ field "id" text true; field "tax" number true ]

                        match Wire.responses renames (snap invoiceV1) (snap renamed) |> Wire.unfixable with
                        | [ verdict ] -> Expect.stringContains verdict.Change "was renamed to 'tax'" "named as a rename"
                        | other -> failtestf "expected one unfixable verdict, got %A" other
                    }

                    test "adding an optional response field breaks nobody" {
                        let widened = shape "InvoiceDto" 2 [ field "id" text true; field "note" text false ]

                        let narrower = shape "InvoiceDto" 1 [ field "id" text true ]

                        Expect.isEmpty
                            (Wire.responses [] (snap narrower) (snap widened))
                            "clients ignore what they do not read"
                    }
                ]

            testList
                "requests: the ones an upcaster can fix"
                [
                    test "a new required request field demands an upcaster" {
                        let verdicts = Wire.requests [] (snap createV1) (snap createV2)

                        match verdicts with
                        | [ verdict ] ->
                            Expect.equal verdict.Side WireSide.Request "on the side they send"
                            Expect.isTrue verdict.Fixable "and this one can be rescued"
                            Expect.stringContains verdict.Consequence "upcaster" "by writing one"
                        | other -> failtestf "expected one verdict, got %A" other
                    }

                    test "a removed request shape breaks what clients already send, and no upcaster can fix it" {
                        let before = ShapeSnapshot.of' [ invoiceV1; createV1 ]
                        let after = snap invoiceV1

                        match Wire.requests [] before after with
                        | [ verdict ] ->
                            Expect.equal verdict.Contract "CreateBillRequest" "the contract that went"
                            Expect.isFalse verdict.Fixable "nothing can be upcast into a body nothing reads"
                            Expect.stringContains verdict.Consequence "version the endpoint" "the real options"
                        | other -> failtestf "expected one verdict, got %A" other
                    }

                    test "the request report names the other direction of travel" {
                        let report = Wire.report (Wire.requests [] (snap createV1) (snap createV2))
                        Expect.stringContains report "they send what you read" "the opposite crossing"
                    }

                    test "requests are checked for renames and upcaster gaps like events" {
                        // A request body has versions and upcasters for the same
                        // reason an event does. A response body has neither.
                        let renamed = shape "CreateBillRequest" 2 [ field "supplierId" text true ]

                        // The rename among the report; the same change also
                        // strands v1 and leaves v2 unrecorded, and those are
                        // pinned in UnrecordedTests.
                        let refusals =
                            Wire.unresolvedRequests [] [] (snap createV1) (snap renamed)
                            |> List.filter (fun p -> p.Message.Contains "either a rename")

                        match refusals with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "'vendorId'" "what went"
                            Expect.stringContains problem.Message "'supplierId'" "and what arrived"
                        | other -> failtestf "expected the rename to be refused, got %A" other
                    }

                    test "a request refusal talks about bodies, never about stored events" {
                        // The checks are shared with the event policy and the
                        // words are not. A reader looking at an HTTP body who is
                        // told the question is whether stored events can be read
                        // is being pointed at a system they do not have.
                        let renamed = shape "CreateBillRequest" 2 [ field "supplierId" text true ]

                        // The rename among the report; the same change also
                        // strands v1 and leaves v2 unrecorded, and those are
                        // pinned in UnrecordedTests.
                        let refusals =
                            Wire.unresolvedRequests [] [] (snap createV1) (snap renamed)
                            |> List.filter (fun p -> p.Message.Contains "either a rename")

                        match refusals with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "bodies already sent" "the wire noun"

                            Expect.isFalse
                                (problem.Message.Contains "stored events")
                                "and not the event one, which is what leaked before"
                        | other -> failtestf "expected the rename to be refused, got %A" other
                    }

                    test "a stranded request version is stranded bodies, not stranded storage" {
                        let v3 = shape "CreateBillRequest" 3 [ field "vendorId" text true ]
                        let snapshot = ShapeSnapshot.of' [ createV1; v3 ]

                        match Wire.unresolvedRequests [] [] (snap createV1) snapshot with
                        | problems ->
                            let text = Wire.reportUnresolved problems
                            Expect.stringContains text "bodies already sent" "named for what a request is"

                            Expect.isFalse (text.Contains "stored shapes") "not for what an event store holds"
                    }

                    test "the event policy is unchanged by all this" {
                        // The shared check took a parameter; the event wording it
                        // used to hard-code is still exactly what Events emits.
                        let renamed = shape "CreateBillRequest" 2 [ field "supplierId" text true ]

                        match Events.undeclaredRenames [] (snap createV1) (snap renamed) with
                        | [ problem ] ->
                            Expect.stringContains problem.Message "whether stored events can be read" "unchanged"
                        | other -> failtestf "expected the rename to be refused, got %A" other
                    }
                ]

            testList
                "the two policies read one matrix"
                [
                    test "the same change is fixable as a request and not as a response" {
                        // The point of the split. One matrix, two policies, and
                        // the difference is who owns the code that breaks.
                        let before = snap (shape "Thing" 1 [ field "a" text true ])

                        let after = snap (shape "Thing" 2 [ field "a" text true; field "b" text true ])

                        let asRequest = Wire.requests [] before after
                        let asResponse = Wire.responses [] before after

                        Expect.isNonEmpty asRequest "adding a required field breaks what clients already send"
                        Expect.all asRequest (fun v -> v.Fixable) "and an upcaster rescues it"

                        Expect.isEmpty asResponse "while sending an extra field breaks nobody reading it"
                    }

                    test "nothing to say says so" {
                        Expect.stringContains
                            (Wire.report [])
                            "No wire contract changed"
                            "rather than printing nothing at all"
                    }
                ]

            testList
                "a field that comes to admit null"
                [
                    // Every body a client already sends holds a string or leaves
                    // the key out, and both still read under string | null. On a
                    // response it is the break with no remedy. The matrix says
                    // which direction; the policies say what that means.
                    test "is not a break for clients already written" {
                        let before = snap (shape "OpenAccountRequest" 1 [ field "code" text false ])

                        let after =
                            snap (shape "OpenAccountRequest" 2 [ field "code" (FieldType.Nullable text) false ])

                        Expect.isEmpty (Wire.requests [] before after) "what they already send still reads"
                        Expect.isNonEmpty (Wire.responses [] before after) "but what they already read may now be null"
                    }

                    test "the reverse is a break for clients already written, and only them" {
                        let before =
                            snap (shape "OpenAccountRequest" 1 [ field "code" (FieldType.Nullable text) false ])

                        let after = snap (shape "OpenAccountRequest" 2 [ field "code" text false ])
                        Expect.isNonEmpty (Wire.requests [] before after) "a body already sent may hold null"
                        Expect.isEmpty (Wire.responses [] before after) "and a client reading a string still can"
                    }

                    test "at any depth" {
                        let before = snap (shape "R" 1 [ field "tags" (FieldType.Sequence text) true ])

                        let after =
                            snap (shape "R" 2 [ field "tags" (FieldType.Sequence(FieldType.Nullable text)) true ])

                        Expect.isEmpty
                            (Wire.requests [] before after)
                            "elements that admit null still read what was sent"
                    }
                ]
        ]
