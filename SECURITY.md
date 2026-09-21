# Security Policy

## Supported versions

Etymon is pre-1.0. Only the latest released version receives security fixes.
Once 1.0 ships, this section will name the supported release line.

| Version | Supported |
| ------- | --------- |
| 0.1.x   | yes       |

## Reporting a vulnerability

**Do not open a public issue.**

Use GitHub's [private vulnerability reporting][gh] on this repository, or email
**security@caelusminds.com**.

[gh]: https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability

Please include what you can: which package and version, what an attacker can do,
and a minimal reproduction. A failing test is ideal but not required.

## What to expect

- **Within 3 working days** — acknowledgement that the report arrived.
- **Within 10 working days** — an assessment: whether it is a vulnerability, how
  severe, and a rough fix timeline.
- **On release** — a GitHub Security Advisory, a patch release, and a CHANGELOG
  entry. You will be credited unless you ask not to be.

If you do not hear back within 3 working days, please chase — assume the message
was lost rather than ignored.

## Scope

Etymon validates and decodes input, so the failure modes that matter most are:

- **Input that escapes validation.** A value that a refinement or schema should
  have rejected but accepted.
- **Denial of service through input.** Pathological input that makes decoding or
  checking take disproportionate time or memory. Note that `Check.pattern`
  already runs regular expressions under a one-second timeout for this reason; a
  way around that timeout is in scope.
- **Secrets in output.** Any route by which a `Secret<'T>` value reaches a log,
  an error message, a serialised payload or an exception.
- **Generated SQL.** Any input to `Etymon.Schema.Sql` that produces SQL an
  attacker controls.

Out of scope: vulnerabilities in .NET itself or in a dependency (report those
upstream), and anything requiring an attacker to already control the process.
