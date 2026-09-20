module Etymon.TrimGate.Program

open Etymon

// Touches the parts of Core a consumer would actually reach for, so the trimmer
// has to keep and analyse them rather than discarding the lot as unreachable.
type private Email = Email of string

let private emailRefinement =
    Refine.ofString "Email"
    |> Refine.trimmed
    |> Refine.length (Some 3) (Some 254)
    |> Refine.format Format.Email
    |> Refine.wrap Email (fun (Email s) -> s)

[<EntryPoint>]
let main argv =
    let raw = if argv.Length > 0 then argv[0] else "someone@example.com"

    let result =
        validation {
            let! email = Refine.create emailRefinement raw |> Validation.underField "email"

            and! age =
                Refine.create (Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)) 42
                |> Validation.underField "age"

            return email, age
        }

    match result with
    | Ok(Email e, age) ->
        printfn "%s %d" e age
        0
    | Error errors ->
        eprintfn "%s" (ValidationErrors.format errors)
        1
