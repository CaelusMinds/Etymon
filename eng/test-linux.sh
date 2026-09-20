#!/bin/bash
# Runs the whole suite on Linux, both target frameworks.
#
# Windows is not enough. Two bugs shipped because every local run was on
# Windows: Check.format Format.Uri accepted "/relative" on Unix because the
# parser adopts a rooted path as an implicit file: URI, and Files.classify
# recognised only the Win32 sharing-violation code, so an in-use file came back
# as IoFailure on Unix and no retry loop would have fired.
#
# Run it from the repository root:
#     docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash eng/test-linux.sh
#
# ArtifactsPath is redirected so a Linux build does not fight the host's
# artifacts/ directory over the same output paths.
set -u

if ! command -v dotnet-install.sh > /dev/null 2>&1; then
  # The SDK 10 image has no .NET 8 runtime, and the net8.0 tests need one.
  curl -sSL https://dot.net/v1/dotnet-install.sh \
    | bash -s -- --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet --no-path > /dev/null 2>&1
fi

dotnet build -c Release -p:ArtifactsPath=/tmp/art --nologo 2>&1 | grep -E " error |Error\(s\)" | head -5

failed=0

for tfm in net8.0 net10.0; do
  for project in tests/*.Tests; do
    name=$(basename "$project")
    raw=$(dotnet run --project "$project" -c Release -f "$tfm" -p:ArtifactsPath=/tmp/art \
            --no-build --no-restore -- --summary 2>&1 | sed 's/\x1b\[[0-9;]*[A-Za-z]//g')
    summary=$(echo "$raw" | grep -oE "[0-9]+ passed, [0-9]+ ignored, [0-9]+ failed, [0-9]+ errored" | tail -1)
    echo "$name $tfm: ${summary:-DID NOT RUN}"

    case "$summary" in
      *"0 failed, 0 errored") ;;
      *)
        failed=1
        echo "$raw" | grep -E "ERR\]|Actual|Expected|expected" | head -8
        ;;
    esac
  done
done

exit $failed
