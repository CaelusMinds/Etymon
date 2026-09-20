/// <summary>
/// Snapshot testing: pin generated output to a committed file, so that a change
/// to it shows up as a diff in a pull request rather than passing unnoticed.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately hand-written rather than taken from Verify. Verify 33
/// fails the build unless the consumer declares a sponsorship exemption, and the
/// exemptions are time-bounded — which means a build that breaks on a date
/// nobody has written down. The whole feature Etymon needs is "compare a string
/// to a file and write the difference out for review", which is this file.
/// </para>
/// <para>
/// Line endings are normalised to LF on both sides, so a snapshot written on
/// Windows and checked on Linux compares equal. <c>.gitattributes</c> keeps the
/// committed files that way too.
/// </para>
/// </remarks>
module Etymon.Tests.Snapshot

open System
open System.IO
open System.Reflection
open Expecto

/// The snapshot directory, injected by the test project so that it points at the
/// source tree rather than wherever the assembly happens to be running from.
let private directory =
    let assembly = Assembly.GetEntryAssembly()

    let fromMetadata =
        if isNull assembly then
            None
        else
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            |> Seq.tryFind (fun a -> a.Key = "SnapshotDirectory")
            |> Option.map (fun a -> a.Value)

    match fromMetadata with
    | Some path when not (String.IsNullOrWhiteSpace path) -> path
    | _ ->
        failwith
            "The test project must declare a SnapshotDirectory assembly metadata attribute; see any Etymon test project for the item group that does it."

let private normalise (text: string) =
    text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd() + "\n"

/// The first line at which two texts differ, for an error message that says
/// where to look rather than printing two whole documents.
let private firstDifference (expected: string) (actual: string) =
    let expectedLines = expected.Split '\n'
    let actualLines = actual.Split '\n'

    let differing =
        Seq.init (max expectedLines.Length actualLines.Length) id
        |> Seq.tryFind (fun i ->
            let e =
                if i < expectedLines.Length then
                    expectedLines[i]
                else
                    "<end of file>"

            let a =
                if i < actualLines.Length then
                    actualLines[i]
                else
                    "<end of file>"

            e <> a
        )

    match differing with
    | None -> "the files differ only in trailing whitespace"
    | Some i ->
        let e =
            if i < expectedLines.Length then
                expectedLines[i]
            else
                "<end of file>"

        let a =
            if i < actualLines.Length then
                actualLines[i]
            else
                "<end of file>"

        $"first difference at line %d{i + 1}:%s{Environment.NewLine}  committed: %s{e}%s{Environment.NewLine}  generated: %s{a}"

/// <summary>
/// Compares generated output against the committed snapshot of the same name.
/// </summary>
/// <remarks>
/// On a mismatch, or when no snapshot exists yet, the generated text is written
/// beside the committed one as <c>.received.txt</c> so it can be diffed and, if
/// the change was intended, renamed. Received files are never committed.
/// </remarks>
let verify (name: string) (generated: string) =
    Directory.CreateDirectory directory |> ignore
    let verifiedPath = Path.Combine(directory, name + ".verified.txt")
    let receivedPath = Path.Combine(directory, name + ".received.txt")
    let actual = normalise generated

    let writeReceived () =
        File.WriteAllText(receivedPath, actual, Text.UTF8Encoding false)

    if not (File.Exists verifiedPath) then
        writeReceived ()

        failtestf
            "No snapshot for '%s' yet. The generated output has been written to:\n  %s\nReview it, and if it is right, rename it to '%s.verified.txt' and commit it."
            name
            receivedPath
            name
    else
        let expected = normalise (File.ReadAllText verifiedPath)

        if expected = actual then
            // Tidy up after a previous failure, so a passing run leaves no litter.
            if File.Exists receivedPath then
                File.Delete receivedPath
        else
            writeReceived ()

            failtestf
                "Snapshot '%s' does not match.\n%s\n\nThe generated output is at:\n  %s\nIf the change was intended, replace the committed snapshot with it."
                name
                (firstDifference expected actual)
                receivedPath
