/// Schemas covering each shape the mapper has to have an answer for: flat,
/// nested, optional-nested, and one with a collection.
module Etymon.Migrations.Tests.Domain

open Etymon

type Address = { Street: string; Zip: string }

type Person =
    {
        Id: string
        Name: string
        Age: int
        Email: string option
    }

type PersonWithAddress =
    {
        Id: string
        Name: string
        Address: Address
    }

type PersonWithOptionalAddress =
    {
        Id: string
        Name: string
        Address: Address option
    }

type TaggedPerson = { Id: string; Tags: string list }

type FullPerson =
    {
        Id: string
        Address: Address
        Tags: string list
    }

module Person =
    let addressSchema =
        Schema.object "Address" {
            let! street = Schema.required "street" Schema.string (fun (a: Address) -> a.Street)

            and! zip =
                Schema.required
                    "zip"
                    (Schema.string |> Schema.constrain (Check.exactLength 5))
                    (fun (a: Address) -> a.Zip)

            return { Street = street; Zip = zip }
        }

    let flatSchema =
        Schema.object "Person" {
            let! id = Schema.required "id" Schema.string (fun (p: Person) -> p.Id)

            and! name =
                Schema.required
                    "name"
                    (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 100)))
                    (fun (p: Person) -> p.Name)

            and! age =
                Schema.required
                    "age"
                    (Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130)))
                    (fun (p: Person) -> p.Age)

            and! email = Schema.optional "email" Schema.string (fun (p: Person) -> p.Email)

            return
                {
                    Id = id
                    Name = name
                    Age = age
                    Email = email
                }
        }

    let nestedSchema =
        Schema.object "Person" {
            let! id = Schema.required "id" Schema.string (fun (p: PersonWithAddress) -> p.Id)

            and! name =
                Schema.required "name" Schema.string (fun (p: PersonWithAddress) -> p.Name)

            and! address =
                Schema.required "address" addressSchema (fun (p: PersonWithAddress) -> p.Address)

            return
                {
                    Id = id
                    Name = name
                    Address = address
                }
        }

    let optionalNestedSchema =
        Schema.object "Person" {
            let! id =
                Schema.required "id" Schema.string (fun (p: PersonWithOptionalAddress) -> p.Id)

            and! name =
                Schema.required "name" Schema.string (fun (p: PersonWithOptionalAddress) -> p.Name)

            and! address =
                Schema.optional "address" addressSchema (fun (p: PersonWithOptionalAddress) -> p.Address)

            return
                {
                    Id = id
                    Name = name
                    Address = address
                }
        }

    let taggedSchema =
        Schema.object "Person" {
            let! id = Schema.required "id" Schema.string (fun (p: TaggedPerson) -> p.Id)

            and! tags =
                Schema.required "tags" (Schema.list Schema.string) (fun (p: TaggedPerson) -> p.Tags)

            return { Id = id; Tags = tags }
        }

    let fullSchema =
        Schema.object "Person" {
            let! id = Schema.required "id" Schema.string (fun (p: FullPerson) -> p.Id)

            and! address =
                Schema.required "address" addressSchema (fun (p: FullPerson) -> p.Address)

            and! tags =
                Schema.required "tags" (Schema.list Schema.string) (fun (p: FullPerson) -> p.Tags)

            return
                {
                    Id = id
                    Address = address
                    Tags = tags
                }
        }

/// The table every diff and script test starts from.
let personTable =
    match Mapping.tableOf (Mapping.keyedBy "id") Person.flatSchema with
    | Ok table -> table
    | Error problems -> failwith (Mapping.report problems)

let personSnapshot = SqlModel.snapshotOf [ personTable ]
