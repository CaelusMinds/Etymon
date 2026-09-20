module Etymon.Std.Tests.FilesTests

open System
open System.IO
open Expecto
open Etymon

/// A directory that exists for the duration of one test and is then gone.
let private withTempDir (body: string -> unit) =
    let dir =
        Path.Combine(Path.GetTempPath(), "etymon-tests-" + Guid.NewGuid().ToString "N")

    Directory.CreateDirectory dir |> ignore

    try
        body dir
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

let private expectError (expected: FileError) (actual: Result<'T, FileError>) (message: string) =
    match actual with
    | Ok _ -> failtestf "expected %A, but it succeeded: %s" expected message
    | Error e -> Expect.equal e expected message

let tests =
    testList
        "File and Dir"
        [
            testList
                "reading"
                [
                    test "reads back what was written" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "a.txt")
                            Expect.equal (File.tryWriteAllText path "hello") (Ok()) "write succeeds"
                            Expect.equal (File.tryReadAllText path) (Ok "hello") "and reads back identically"
                        )
                    }

                    test "a missing file is NotFound, not an exception" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "missing.txt")

                            expectError
                                (FileError.NotFound path)
                                (File.tryReadAllText path)
                                "the case a caller can act on"
                        )
                    }

                    test "a missing directory is DirectoryNotFound" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "nope", "missing.txt")

                            match File.tryReadAllText path with
                            | Error(FileError.DirectoryNotFound _) -> ()
                            | other -> failtestf "expected DirectoryNotFound, got %A" other
                        )
                    }

                    test "reads lines" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "lines.txt")
                            File.tryWriteAllText path "one\ntwo\nthree" |> ignore

                            match File.tryReadAllLines path with
                            | Ok lines -> Expect.equal (List.ofArray lines) [ "one"; "two"; "three" ] "every line"
                            | Error e -> failtestf "should have read: %s" (FileError.describe e)
                        )
                    }

                    test "reads bytes" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "b.bin")
                            let payload = [| 0uy; 1uy; 255uy |]
                            File.tryWriteAllBytes path payload |> ignore
                            Expect.equal (File.tryReadAllBytes path) (Ok payload) "bytes survive exactly"
                        )
                    }

                    test "text is UTF-8 without a byte order mark" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "bom.txt")
                            File.tryWriteAllText path "hi" |> ignore

                            match File.tryReadAllBytes path with
                            | Ok bytes ->
                                Expect.equal
                                    bytes
                                    [| 0x68uy; 0x69uy |]
                                    "no BOM, because everything downstream parses this"
                            | Error e -> failtestf "should have read: %s" (FileError.describe e)
                        )
                    }

                    test "non-ASCII text round-trips" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "utf8.txt")
                            let text = "naïve — 日本語 — 🌱"
                            File.tryWriteAllText path text |> ignore
                            Expect.equal (File.tryReadAllText path) (Ok text) "UTF-8 throughout"
                        )
                    }
                ]

            testList
                "writing"
                [
                    test "creates the containing directory" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "deep", "deeper", "a.txt")

                            Expect.equal
                                (File.tryWriteAllText path "hello")
                                (Ok())
                                "not doing this only ever produces a failure the caller must fix by doing it"

                            Expect.equal (File.tryReadAllText path) (Ok "hello") "and the file is really there"
                        )
                    }

                    test "overwrites an existing file" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "a.txt")
                            File.tryWriteAllText path "first" |> ignore
                            File.tryWriteAllText path "second" |> ignore
                            Expect.equal (File.tryReadAllText path) (Ok "second") "replaced, not appended"
                        )
                    }

                    test "append adds without replacing" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "log.txt")
                            File.tryAppendText path "a" |> ignore
                            File.tryAppendText path "b" |> ignore
                            Expect.equal (File.tryReadAllText path) (Ok "ab") "both writes are there"
                        )
                    }

                    test "append creates the file when it is absent" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "new.txt")
                            Expect.equal (File.tryAppendText path "a") (Ok()) "no need to create it first"
                        )
                    }
                ]

            testList
                "deleting, copying, moving"
                [
                    test "deleting an absent file succeeds" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "never-existed.txt")

                            Expect.equal (File.tryDelete path) (Ok()) "the caller wanted it gone, and it is gone"
                        )
                    }

                    test "delete removes an existing file" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "a.txt")
                            File.tryWriteAllText path "x" |> ignore
                            Expect.equal (File.tryDelete path) (Ok()) "delete succeeds"
                            Expect.isFalse (File.exists path) "and it is really gone"
                        )
                    }

                    test "copy refuses to replace, copyOver does not" {
                        withTempDir (fun dir ->
                            let source = Path.Combine(dir, "a.txt")
                            let destination = Path.Combine(dir, "b.txt")
                            File.tryWriteAllText source "source" |> ignore
                            File.tryWriteAllText destination "destination" |> ignore

                            Expect.isTrue (Result.isError (File.tryCopy source destination)) "copy will not clobber"
                            Expect.equal (File.tryReadAllText destination) (Ok "destination") "and did not"

                            Expect.equal (File.tryCopyOver source destination) (Ok()) "copyOver will"
                            Expect.equal (File.tryReadAllText destination) (Ok "source") "and did"
                        )
                    }

                    test "copying a missing source is NotFound" {
                        withTempDir (fun dir ->
                            let source = Path.Combine(dir, "missing.txt")
                            let destination = Path.Combine(dir, "b.txt")

                            match File.tryCopy source destination with
                            | Error(FileError.NotFound _) -> ()
                            | other -> failtestf "expected NotFound, got %A" other
                        )
                    }

                    test "move relocates and replaces" {
                        withTempDir (fun dir ->
                            let source = Path.Combine(dir, "a.txt")
                            let destination = Path.Combine(dir, "b.txt")
                            File.tryWriteAllText source "payload" |> ignore

                            Expect.equal (File.tryMove source destination) (Ok()) "move succeeds"
                            Expect.isFalse (File.exists source) "source is gone"
                            Expect.equal (File.tryReadAllText destination) (Ok "payload") "destination has the payload"
                        )
                    }
                ]

            testList
                "locking"
                [
                    test "a file held exclusively reports InUse" {
                        // The one failure mode that is genuinely about concurrency, and
                        // the one a caller most wants to retry rather than give up on.
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "locked.txt")
                            File.tryWriteAllText path "x" |> ignore

                            use _held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)

                            match File.tryReadAllText path with
                            | Error(FileError.InUse _) -> ()
                            | Error(FileError.AccessDenied _) -> () // some platforms report it this way
                            | other -> failtestf "expected InUse or AccessDenied, got %A" other
                        )
                    }
                ]

            testList
                "FileError"
                [
                    test "path extracts the path from every case" {
                        let cases =
                            [
                                FileError.NotFound "a"
                                FileError.DirectoryNotFound "a"
                                FileError.AccessDenied "a"
                                FileError.InUse "a"
                                FileError.InvalidPath "a"
                                FileError.NotValidText "a"
                                FileError.IoFailure("a", "why")
                            ]

                        for case in cases do
                            Expect.equal (FileError.path case) "a" $"%A{case} should carry its path"
                    }

                    test "describe says something for every case" {
                        let cases =
                            [
                                FileError.NotFound "a"
                                FileError.DirectoryNotFound "a"
                                FileError.AccessDenied "a"
                                FileError.InUse "a"
                                FileError.InvalidPath "a"
                                FileError.NotValidText "a"
                                FileError.IoFailure("a", "why")
                            ]

                        for case in cases do
                            let described = FileError.describe case
                            Expect.isNotEmpty described $"%A{case} should describe itself"
                            Expect.stringContains described "a" "and mention the path"
                    }
                ]

            testList
                "Dir"
                [
                    test "tryCreate is idempotent" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "a", "b")
                            Expect.equal (Dir.tryCreate path) (Ok()) "creates with parents"
                            Expect.equal (Dir.tryCreate path) (Ok()) "and says yes again"
                            Expect.isTrue (Dir.exists path) "it is there"
                        )
                    }

                    test "listing is sorted, so anything built from it is reproducible" {
                        withTempDir (fun dir ->
                            for name in [ "c.fs"; "a.fs"; "b.fs"; "ignore.txt" ] do
                                File.tryWriteAllText (Path.Combine(dir, name)) "" |> ignore

                            match Dir.tryListFiles "*.fs" dir with
                            | Ok files ->
                                let names = files |> List.map Path.GetFileName
                                Expect.equal names [ "a.fs"; "b.fs"; "c.fs" ] "sorted, and the pattern filtered"
                            | Error e -> failtestf "should have listed: %s" (FileError.describe e)
                        )
                    }

                    test "recursive listing reaches subdirectories" {
                        withTempDir (fun dir ->
                            File.tryWriteAllText (Path.Combine(dir, "a.fs")) "" |> ignore
                            File.tryWriteAllText (Path.Combine(dir, "sub", "b.fs")) "" |> ignore

                            match Dir.tryListFilesRecursive "*.fs" dir with
                            | Ok files -> Expect.equal (List.length files) 2 "both levels"
                            | Error e -> failtestf "should have listed: %s" (FileError.describe e)
                        )
                    }

                    test "listing a missing directory is DirectoryNotFound" {
                        withTempDir (fun dir ->
                            let path = Path.Combine(dir, "nope")

                            match Dir.tryListFiles "*" path with
                            | Error(FileError.DirectoryNotFound _) -> ()
                            | other -> failtestf "expected DirectoryNotFound, got %A" other
                        )
                    }

                    test "tryListDirectories returns the immediate children, sorted" {
                        withTempDir (fun dir ->
                            for name in [ "z"; "a"; "m" ] do
                                Dir.tryCreate (Path.Combine(dir, name)) |> ignore

                            match Dir.tryListDirectories dir with
                            | Ok dirs -> Expect.equal (dirs |> List.map Path.GetFileName) [ "a"; "m"; "z" ] "sorted"
                            | Error e -> failtestf "should have listed: %s" (FileError.describe e)
                        )
                    }

                    test "deleting an absent directory succeeds" {
                        withTempDir (fun dir ->
                            Expect.equal (Dir.tryDelete (Path.Combine(dir, "nope"))) (Ok()) "the intent has been met"
                        )
                    }

                    test "delete removes a directory and its contents" {
                        withTempDir (fun dir ->
                            let target = Path.Combine(dir, "target")
                            File.tryWriteAllText (Path.Combine(target, "a.txt")) "x" |> ignore
                            Expect.equal (Dir.tryDelete target) (Ok()) "delete succeeds"
                            Expect.isFalse (Dir.exists target) "and it is gone"
                        )
                    }

                    test "exists never throws on a nonsense path" {
                        Expect.isFalse (Dir.exists "") "empty"
                        Expect.isFalse (Dir.exists null) "null"
                        Expect.isFalse (File.exists null) "and for files too"
                    }
                ]
        ]
