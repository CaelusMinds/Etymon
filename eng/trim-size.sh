#!/bin/bash
# What Etymon.Core actually costs a Blazor WebAssembly payload, after trimming.
# The README quotes a number; this is where the number comes from.
dotnet publish eng/TrimGate -c Release -f net10.0 -r linux-x64 \
  -p:PublishTrimmed=true -p:TrimmerSingleWarn=false -p:ArtifactsPath=/tmp/art --nologo 2>&1 \
  | grep -iE " warn| error" | head -5
find /tmp/art -path '*publish*' -name 'Etymon.*.dll' -printf '%s\t%f\n' | sort -n
