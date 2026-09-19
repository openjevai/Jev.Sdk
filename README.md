# Jev.Sdk

A .NET client library for the [TypeSafe AI](https://typesafe.ai) System One API
(`https://api.typesafe.ai`).

Ask typed questions about a body of content and get structured, probability-backed
answers back. Every question is declared with a kind and answered within the
constraints you supplied, so nothing has to be parsed out of prose.

> **Status: requirements / design phase. No implementation exists yet.**
> This repository currently contains the requirements, decisions, and verified
> API notes. Nothing here compiles or runs, and the examples below describe the
> intended shape, not shipped behaviour.

## Not affiliated

This is an independent client library. It is **not** affiliated with, sponsored
by, or endorsed by TypeSafe AI. "TypeSafe" and "System One" are used only to
describe the API this library talks to. For the API, the service, the models, or
anything about accounts, billing, or uptime, contact TypeSafe AI directly.

## Intended usage

```csharp
using Jev.Sdk;

var client = new JevClient();   // reads TYPESAFE_API_KEY from the environment

var response = await client.SystemOneAsync(
    state: "Hi, I've been trying to connect my Stripe account for 3 days and it keeps failing.",
    questions: new Dictionary<string, Question>
    {
        ["is_urgent"] = Question.Noul("Does this convey urgency?"),
        ["department"] = Question.Choice(
            "Which team should handle this?",
            new Dictionary<string, string?>
            {
                ["billing"]   = "Payments, invoicing, refunds",
                ["technical"] = "Bugs, outages, integrations",
                ["sales"]     = "Pricing, upgrades, new accounts",
            }),
        ["frustration"] = Question.Score(
            "How frustrated is the customer?",
            ["Calm", "Frustrated", "Very angry"]),
    },
    cancellationToken: cancellationToken);

double urgency = response.Answers["is_urgent"].AsNoul().Noul;
string dept    = response.Answers["department"].AsChoice().Choice;
double score   = response.Answers["frustration"].AsScore().Score;
```

Zero configuration beyond one environment variable. Everything is overridable;
nothing is required.

## Design in one paragraph

All I/O is asynchronous and cancellation-aware. The core library depends on
little and performs no file I/O — it emits BCL telemetry (`ILogger`,
`ActivitySource`, `Meter`) and never depends on a telemetry exporter. JSON is
`System.Text.Json` with source generation, so trimming and Native AOT work.
Unknown question and answer kinds are preserved rather than rejected, so the
library is never the reason a new API feature cannot be used.

## Repository layout

| Path | Contents |
| --- | --- |
| `requirements/` | Requirements, design decisions, and open items |
| `docs/` | Verified API notes and wire-level reference |
| `src/` | Library source (not yet scaffolded) |
| `tests/` | Test projects (not yet scaffolded) |
| `samples/` | Runnable examples (not yet scaffolded) |

## Documentation

- [requirements/requirements.md](requirements/requirements.md) — the binding requirements and decisions
- [docs/api-notes.md](docs/api-notes.md) — verified API facts, including where the vendor docs and the OpenAPI spec disagree

## Attribution

Authored by **deepseek-v4.1-flash** (via Hermes Agent), under deep human review.

The model drafted the requirements, the decision ledger, and the API analysis.
Every decision recorded in [requirements/requirements.md](requirements/requirements.md)
was reviewed, challenged, and explicitly approved by a human before being locked.
Where the model made a recommendation the human disagreed with, the human's call
is what the document records.

## License

MIT. See [LICENSE](LICENSE).
