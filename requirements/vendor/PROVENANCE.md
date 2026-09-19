# Vendor document — provenance

Provenance for `typesafe-api-2026-09-19T081731Z.md`. This sidecar exists so the captured copy stays
byte-identical to what the vendor served: no header is prepended to the vendor's own file, and every
question about where it came from is answered here instead.

## The capture

| Field | Value |
| --- | --- |
| Source URL | `https://docs.typesafe.ai/api.md` |
| Fetched at (UTC) | `2026-09-19T08:17:31Z` |
| HTTP status | `200` |
| `Content-Type` | `text/markdown; charset=utf-8` |
| `Content-Length` | `10322` bytes |
| Bytes stored | `10322` bytes |
| Lines | `323` |
| SHA-256 | `b9b205096ac164e26f7797a52551c72f2bb3d270dffd923dc7180af3b1a21ae8` |

## Server-supplied metadata

Taken from the response headers at capture time, so an upstream change is detectable by comparing a
later capture against this block rather than by re-reading the document.

| Header | Value |
| --- | --- |
| `date` | `Sat, 19 Sep 2026 08:17:31 GMT` |
| `last-modified` | `Sat, 19 Sep 2026 08:17:31 GMT` |
| `cf-cache-status` | `EXPIRED` |
| `x-vercel-cache` | `HIT` |
| `x-matched-path` | `/_mintlify/_markdown/_sites/[subdomain]/[[...slug]]` |
| `x-robots-tag` | `noindex, nofollow` |
| `cache-control` | `public, max-age=0, must-revalidate` |

`last-modified` equalled `date`, which is a property of the documentation host rather than a claim
that the document changed at capture time. It carries no version signal.

The response links other machine-readable documents, not captured here. They are listed because a
later verification pass may want them:

```
</llms.txt>; rel="llms-txt"
</llms-full.txt>; rel="llms-full-txt"
</.well-known/api-catalog>; rel="api-catalog"
</.well-known/mcp/server-card.json>; rel="mcp-server-card"
</.well-known/agent-card.json>; rel="agent-card"
</.well-known/agent-skills/index.json>; rel="agent-skills"
```

## Reproducing this capture

```sh
curl -sS -D headers.txt -o "typesafe-api-<timestamp>.md" https://docs.typesafe.ai/api.md
sha256sum "typesafe-api-<timestamp>.md"
```

Compare the hash against the table above. A difference means the vendor changed the document, not
that the capture was faulty — in which case the earlier file stays as it is and a new dated file is
added beside it. These captures are never overwritten or corrected, so the set of them remains a
record of what the API documented on each date.

## What this document is, and is not

`api.md` is the vendor's prose reference: the request and response shapes, the three question kinds,
the three answer kinds, and the error table. It is **not** the machine-readable specification. The
contract this library is verified against is `https://api.typesafe.ai/openapi.json` (OpenAPI 3.1.0,
titled `TypeSafe 0.2.0`).

Where the two disagree, the OpenAPI document governs, because it is the artifact the service
validates against. `api.md` is captured here for three other reasons: it carries behavioural
guidance the specification does not, it is the document a human reviewer will read, and it is the
document the vendor is most likely to edit — so a dated copy turns "the docs say it differently now"
into a diff.

`docs/api-notes.md` records where the library diverges from either document and why.
