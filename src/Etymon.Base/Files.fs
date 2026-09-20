namespace Etymon

open System
open System.IO

/// <summary>
/// Why a file operation did not work.
/// </summary>
/// <remarks>
/// The distinction that matters is between the cases a caller can reasonably act
/// on — the file is not there, we are not allowed, someone else has it — and
/// everything else, which is <c>IoFailure</c>. Branching on a message is not
/// something a caller should ever have to do, so each case that can be named is
/// named.
/// </remarks>
[<RequireQualifiedAccess>]
type FileError =
    /// The file does not exist.
    | NotFound of path: string
    /// A directory on the path does not exist.
    | DirectoryNotFound of path: string
    /// The operating system refused: permissions, or a read-only file.
    | AccessDenied of path: string
    /// Another process has the file open in a conflicting mode.
    | InUse of path: string
    /// The path is malformed, or longer than the platform allows.
    | InvalidPath of path: string
    /// The bytes were not valid in the expected text encoding.
    | NotValidText of path: string
    /// Anything else the operating system reported.
    | IoFailure of path: string * message: string

/// Working with <see cref="T:Etymon.FileError"/>.
[<RequireQualifiedAccess>]
module FileError =

    /// <summary>The path the failure was about.</summary>
    /// <example><code lang="fsharp">
    /// FileError.path (FileError.NotFound "a.txt") // "a.txt"
    /// </code></example>
    let path (error: FileError) =
        match error with
        | FileError.NotFound p
        | FileError.DirectoryNotFound p
        | FileError.AccessDenied p
        | FileError.InUse p
        | FileError.InvalidPath p
        | FileError.NotValidText p
        | FileError.IoFailure(p, _) -> p

    /// <summary>A sentence describing the failure, ready to log or show.</summary>
    /// <example><code lang="fsharp">
    /// FileError.describe (FileError.AccessDenied "C:\\x") // "access to C:\x was denied"
    /// </code></example>
    let describe (error: FileError) =
        match error with
        | FileError.NotFound p -> $"%s{p} does not exist"
        | FileError.DirectoryNotFound p -> $"the directory containing %s{p} does not exist"
        | FileError.AccessDenied p -> $"access to %s{p} was denied"
        | FileError.InUse p -> $"%s{p} is in use by another process"
        | FileError.InvalidPath p -> $"%s{p} is not a valid path"
        | FileError.NotValidText p -> $"%s{p} is not valid text in the expected encoding"
        | FileError.IoFailure(p, message) -> $"%s{p} could not be read or written: %s{message}"

[<AutoOpen>]
module internal FileErrorMapping =

    /// Maps the exception the base library throws onto the case a caller can act
    /// on. Kept in one place so that every operation classifies failures the same
    /// way -- the alternative is each function inventing its own taxonomy.
    let classify (path: string) (exn: exn) =
        match exn with
        | :? FileNotFoundException -> FileError.NotFound path
        | :? DirectoryNotFoundException -> FileError.DirectoryNotFound path
        | :? UnauthorizedAccessException -> FileError.AccessDenied path
        | :? PathTooLongException -> FileError.InvalidPath path
        // DecoderFallbackException derives from ArgumentException, so it has to
        // be tested first or it could never match.
        | :? Text.DecoderFallbackException -> FileError.NotValidText path
        | :? ArgumentException -> FileError.InvalidPath path
        | :? NotSupportedException -> FileError.InvalidPath path
        | :? IOException as io ->
            // Win32 sharing violation (32) and lock violation (33) both mean
            // somebody else has it; there is no dedicated exception type.
            let code = io.HResult &&& 0xFFFF

            if code = 32 || code = 33 then
                FileError.InUse path
            else
                FileError.IoFailure(path, io.Message)
        | other -> FileError.IoFailure(path, other.Message)

    let attempt (path: string) (operation: unit -> 'T) : Result<'T, FileError> =
        try
            Ok(operation ())
        with exn ->
            Error(classify path exn)

/// <summary>
/// File operations that return <c>Result</c> instead of throwing.
/// </summary>
/// <remarks>
/// <para>
/// A missing file, a locked file and a permission refusal are all ordinary
/// outcomes of asking the filesystem for something, not bugs — so they are
/// values. Genuine bugs, such as passing a null path, still throw.
/// </para>
/// <para>
/// This module shadows <c>System.IO.File</c> for anyone who opens
/// <c>System.IO</c> after <c>Etymon</c>. If you need both, qualify:
/// <c>Etymon.File.tryReadAllText</c>.
/// </para>
/// <para>
/// Text is UTF-8 throughout, without a byte order mark on write, because a BOM
/// is a recurring source of trouble for anything that later parses the file.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module File =

    let private utf8NoBom = Text.UTF8Encoding(false, true)

    /// <summary>Whether the file exists. Never throws.</summary>
    /// <example><code lang="fsharp">
    /// File.exists "appsettings.json" // true
    /// </code></example>
    let exists (path: string) =
        not (String.IsNullOrWhiteSpace path) && File.Exists path

    /// <summary>Reads a whole file as UTF-8 text.</summary>
    /// <example><code lang="fsharp">
    /// match File.tryReadAllText "appsettings.json" with
    /// | Ok contents -> ...
    /// | Error e -> eprintfn "%s" (FileError.describe e)
    /// </code></example>
    let tryReadAllText (path: string) =
        attempt path (fun () -> File.ReadAllText(path, utf8NoBom))

    /// <summary>Reads a whole file as UTF-8 lines.</summary>
    /// <example><code lang="fsharp">
    /// File.tryReadAllLines ".gitignore" // Ok [| "bin/"; "obj/" |]
    /// </code></example>
    let tryReadAllLines (path: string) =
        attempt path (fun () -> File.ReadAllLines(path, utf8NoBom))

    /// <summary>Reads a whole file as bytes.</summary>
    /// <example><code lang="fsharp">
    /// File.tryReadAllBytes "icon.png" // Ok [| ... |]
    /// </code></example>
    let tryReadAllBytes (path: string) =
        attempt path (fun () -> File.ReadAllBytes path)

    /// <summary>
    /// Writes UTF-8 text, replacing the file if it is already there. Creates the
    /// containing directory if it is missing, since not doing so only ever
    /// produces a failure the caller would have to handle by creating it.
    /// </summary>
    /// <example><code lang="fsharp">
    /// File.tryWriteAllText "out/report.md" contents // Ok ()
    /// </code></example>
    let tryWriteAllText (path: string) (contents: string) =
        attempt
            path
            (fun () ->
                let directory = Path.GetDirectoryName path

                if not (String.IsNullOrEmpty directory) then
                    Directory.CreateDirectory directory |> ignore

                File.WriteAllText(path, contents, utf8NoBom)
            )

    /// <summary>Writes bytes, replacing the file if it is already there.</summary>
    /// <example><code lang="fsharp">
    /// File.tryWriteAllBytes "out/icon.png" bytes // Ok ()
    /// </code></example>
    let tryWriteAllBytes (path: string) (contents: byte[]) =
        attempt
            path
            (fun () ->
                let directory = Path.GetDirectoryName path

                if not (String.IsNullOrEmpty directory) then
                    Directory.CreateDirectory directory |> ignore

                File.WriteAllBytes(path, contents)
            )

    /// <summary>Appends UTF-8 text, creating the file if it is not there.</summary>
    /// <example><code lang="fsharp">
    /// File.tryAppendText "log.txt" "started\n" // Ok ()
    /// </code></example>
    let tryAppendText (path: string) (contents: string) =
        attempt path (fun () -> File.AppendAllText(path, contents, utf8NoBom))

    /// <summary>
    /// Deletes the file. Succeeds when it was already absent, because the
    /// caller's intent — that it not be there — has been met either way.
    /// </summary>
    /// <example><code lang="fsharp">
    /// File.tryDelete "temp.txt" // Ok () even if it never existed
    /// </code></example>
    let tryDelete (path: string) =
        attempt path (fun () -> File.Delete path)

    /// <summary>Copies a file, refusing to replace an existing destination.</summary>
    /// <example><code lang="fsharp">
    /// File.tryCopy "a.txt" "b.txt" // Error (IoFailure ...) if b.txt exists
    /// </code></example>
    let tryCopy (source: string) (destination: string) =
        attempt destination (fun () -> File.Copy(source, destination, false))

    /// <summary>Copies a file, replacing the destination if it is there.</summary>
    /// <example><code lang="fsharp">
    /// File.tryCopyOver "a.txt" "b.txt" // Ok ()
    /// </code></example>
    let tryCopyOver (source: string) (destination: string) =
        attempt destination (fun () -> File.Copy(source, destination, true))

    /// <summary>Moves a file, replacing the destination if it is there.</summary>
    /// <example><code lang="fsharp">
    /// File.tryMove "draft.md" "final.md" // Ok ()
    /// </code></example>
    let tryMove (source: string) (destination: string) =
        attempt destination (fun () -> File.Move(source, destination, true))

/// <summary>
/// Directory operations that return <c>Result</c> instead of throwing.
/// </summary>
/// <remarks>
/// Named <c>Dir</c> rather than <c>Directory</c> so that it does not shadow
/// <c>System.IO.Directory</c> for anyone who has both open.
/// </remarks>
[<RequireQualifiedAccess>]
module Dir =

    /// <summary>Whether the directory exists. Never throws.</summary>
    /// <example><code lang="fsharp">
    /// Dir.exists "src" // true
    /// </code></example>
    let exists (path: string) =
        not (String.IsNullOrWhiteSpace path) && Directory.Exists path

    /// <summary>
    /// Creates the directory and any missing parents. Succeeds when it is already
    /// there.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Dir.tryCreate "out/reports" // Ok ()
    /// </code></example>
    let tryCreate (path: string) =
        attempt path (fun () -> Directory.CreateDirectory path |> ignore)

    /// <summary>
    /// The files directly inside a directory, matching a pattern. Sorted, so that
    /// two runs over the same directory produce the same order — which is what
    /// makes anything built from the result reproducible.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Dir.tryListFiles "*.fs" "src/Etymon.Core" // Ok [ "...\\Check.fs"; ... ]
    /// </code></example>
    let tryListFiles (pattern: string) (path: string) =
        attempt path (fun () -> Directory.GetFiles(path, pattern) |> Array.sort |> List.ofArray)

    /// <summary>The files inside a directory and all of its subdirectories, sorted.</summary>
    /// <example><code lang="fsharp">
    /// Dir.tryListFilesRecursive "*.fs" "src" // Ok [ ... ]
    /// </code></example>
    let tryListFilesRecursive (pattern: string) (path: string) =
        attempt
            path
            (fun () ->
                Directory.GetFiles(path, pattern, SearchOption.AllDirectories)
                |> Array.sort
                |> List.ofArray
            )

    /// <summary>The immediate subdirectories, sorted.</summary>
    /// <example><code lang="fsharp">
    /// Dir.tryListDirectories "src" // Ok [ "src\\Etymon.Core"; "src\\Etymon.Base" ]
    /// </code></example>
    let tryListDirectories (path: string) =
        attempt path (fun () -> Directory.GetDirectories path |> Array.sort |> List.ofArray)

    /// <summary>
    /// Deletes a directory and everything in it. Succeeds when it was already
    /// absent.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Dir.tryDelete "out" // Ok ()
    /// </code></example>
    let tryDelete (path: string) =
        attempt
            path
            (fun () ->
                if Directory.Exists path then
                    Directory.Delete(path, true)
            )
