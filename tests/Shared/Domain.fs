/// The worked example used throughout these tests, kept in one place so that the
/// documented shape and the tested shape cannot drift apart.
///
/// Shared between the derivations rather than copied into each, because two
/// derivations of the same schema disagreeing is precisely the failure this
/// suite exists to prevent -- and a fixture that has drifted cannot show it.
module Etymon.Tests.Domain

open Etymon

// ---- constrained types ------------------------------------------------------

type Email = private Email of string

module Email =
    let refinement =
        Refine.ofString "Email"
        |> Refine.trimmed
        |> Refine.lowercased
        |> Refine.length (Some 3) (Some 254)
        |> Refine.format Format.Email
        |> Refine.wrap Email (fun (Email s) -> s)

    let schema = Schema.string |> Schema.refine refinement
    let create (s: string) = Refine.create refinement s
    let value (Email s) = s

type Zip = private Zip of string

module Zip =
    let refinement =
        Refine.ofString "Zip"
        |> Refine.exactLength 5
        |> Refine.pattern @"^\d+$"
        |> Refine.wrap Zip (fun (Zip s) -> s)

    let schema = Schema.string |> Schema.refine refinement
    let create (s: string) = Refine.create refinement s
    let value (Zip s) = s

// ---- records ----------------------------------------------------------------

type Address = { Street: string; Zip: Zip }

type Person =
    {
        Name: string
        Age: int
        Email: Email option
        Address: Address
        Tags: string list
    }

module Address =
    let schema =
        Schema.object "Address" {
            let! street =
                Schema.required "street" Schema.string (fun a -> a.Street)
                |> Schema.doc "Street address, including the number"

            and! zip = Schema.required "zip" Zip.schema (fun a -> a.Zip)
            return { Street = street; Zip = zip }
        }

module Person =
    let ageSchema =
        Schema.int
        |> Schema.constrain (Check.intRange (Some 0) (Some 130))
        |> Schema.describe "Age in whole years"

    let schema =
        Schema.object "Person" {
            let! name =
                Schema.required "name" (Schema.string |> Schema.constrain Check.nonEmpty) (fun p -> p.Name)

            and! age = Schema.required "age" ageSchema (fun p -> p.Age)
            and! email = Schema.optional "email" Email.schema (fun p -> p.Email)
            and! address = Schema.required "address" Address.schema (fun p -> p.Address)
            and! tags = Schema.required "tags" (Schema.list Schema.string) (fun p -> p.Tags)

            return
                {
                    Name = name
                    Age = age
                    Email = email
                    Address = address
                    Tags = tags
                }
        }

let sampleAddress =
    {
        Street = "1 Example Way"
        Zip =
            match Zip.create "12345" with
            | Ok z -> z
            | Error e -> failwith (ValidationErrors.format e)
    }

let samplePerson =
    {
        Name = "Ada"
        Age = 36
        Email =
            match Email.create "ada@example.com" with
            | Ok e -> Some e
            | Error e -> failwith (ValidationErrors.format e)
        Address = sampleAddress
        Tags = [ "mathematician"; "engineer" ]
    }

// ---- a union ----------------------------------------------------------------

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Point

module Shape =
    let rectangleSchema =
        Schema.object "Rectangle" {
            let! width = Schema.required "width" Schema.float fst
            and! height = Schema.required "height" Schema.float snd
            return (width, height)
        }

    let schema =
        Schema.union
            "Shape"
            "kind"
            [
                Schema.case
                    "circle"
                    Schema.float
                    Circle
                    (function
                    | Circle r -> ValueSome r
                    | _ -> ValueNone
                    )
                Schema.case
                    "rectangle"
                    rectangleSchema
                    Rectangle
                    (function
                    | Rectangle(w, h) -> ValueSome(w, h)
                    | _ -> ValueNone
                    )
                Schema.caseUnit
                    "point"
                    Point
                    (function
                    | Point -> true
                    | _ -> false
                    )
            ]

// ---- a recursive type -------------------------------------------------------

type Tree = { Value: int; Children: Tree list }

module Tree =
    let schema =
        Schema.recursive
            "Tree"
            (fun self ->
                Schema.object "Tree" {
                    let! value = Schema.required "value" Schema.int (fun t -> t.Value)
                    and! children = Schema.required "children" (Schema.list self) (fun t -> t.Children)
                    return { Value = value; Children = children }
                }
            )
