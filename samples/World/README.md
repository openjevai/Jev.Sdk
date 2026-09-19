# Jev.Sdk.World — a world engine played by the Jev API

Escape the Room is a game. This is the shape of that game, extracted.

Strip the room, the guard, and the key away from `samples/Game/` and what is left is a pattern:

> **declare a world's state, declare the judgements to make about it, ask them all in one call, then
> run deterministic rules over the answers.**

`Jev.Sdk.World` is that pattern as an engine. A world is a **document**, not a program — its state,
its questions, and its rules are all read from a file. Nothing about a particular world is compiled
in, so a new world is a new markdown file and no C# at all.

`ABOUT-THE-GAME.md` in this folder is the essay: what the game is and why it works as a demonstration.
This file is the reference: how to write a world and how to run one.

## Running one

```
dotnet build samples/World/Jev.Sdk.World.slnx

# play it
Jev.Sdk.World.Play escape-the-room

# check it loads, without spending a call
Jev.Sdk.World.Play --validate worlds/the-lighthouse.json

# see what is available
Jev.Sdk.World.Play --list
```

A world source can be any of these, detected by content rather than by extension alone:

| Source | What happens |
| --- | --- |
| `world.zip` | unpacked to a temporary folder, then the world document inside is found and played |
| a folder | the world document inside is found — this is an unpacked package |
| `world.md` | the markdown document *is* the world |
| `world.json` | a JSON object with `rules` and `state` is a world definition |
| `world.json` holding an array | a list of candidates; the first that loads is played |
| `world.json` holding a string | the string is markdown, and is parsed as a world |

The last three are what "just text, autodetect" means in practice: a generator that emits a JSON array,
or wraps markdown in a JSON property, still produces something playable.

## Shipping one as a zip

A published world is a package:

```
escape-the-room.zip
├── world.md      the world
└── README.md     anything else, for whoever reads it next
```

`world.md` is looked for by name — along with `world.json`, `game.md`, `game.json`, and `README.md`, in
that order. Failing those, any `.md` or `.json` at the top level that loads is used. A package is
allowed exactly one world; extra files are supporting material.

```
Jev.Sdk.World.Play escape-the-room.zip
```

The temporary folder is removed on exit, including on Ctrl+C.

## Writing one

A world is markdown with a fenced JSON block under each heading. The prose is not decoration — it is
read by whoever edits the world next, and the two prompt-wording bugs recorded in `ABOUT-THE-GAME.md`
are exactly the kind of thing that needs a paragraph sitting next to the instruction it explains.

    ---
    id: my-world
    title: My World
    schemaVersion: "1.0"
    turnLimit: 25
    ---

    # My World
    One sentence telling the player what they are trying to do.

    ## Examples
    - do a thing

    ## State
    ```json
    [ { "key": "flag", "type": "bool", "initial": false,
        "describe": { "true": "It is set.", "false": "It is not." } } ]
    ```

    ## State template
    ```text
    A room. {{flag}}
    ```

    ## Judgements
    ```json
    [ { "key": "do", "kind": "noul", "fallback": "0.5",
        "question": "The player typed \"{{action}}\". Would it work?" } ]
    ```

    ## Rules
    ```json
    [ { "id": "always", "narrate": [ "Nothing." ],
        "then": [ { "kind": "narrate", "text": "nothing" } ] } ]
    ```

    ## Ending
    ```json
    { "won": "You are out.", "lost": "You did not." }
    ```

The machine-readable form of all this is `schema/world.schema.json`.

### State

A field is a `bool`, an `int`, or some `text`. Every field carries `describe`, which is how it reads in
the state description sent to the model. A `bool` must describe both values; an `int` or `text` may use
`"*"` as a fallback sentence, so a counter reaching an unforeseen value still reads as prose rather
than as a bare number.

**The state is sent as prose, not as flags.** That is the point: "is this plausible" cannot be
answered from `has_key: false`. A world's `describe` entries are what turn the state into a place.

### Judgements

One entry per API question. All of them go in a single call.

| Kind | API type | Comes back as | Use it for |
| --- | --- | --- | --- |
| `noul` | `Noul` | a probability 0–1 | possibility, risk, likelihood of success |
| `choice` | `Choice` | one of a named set | classification, intent |
| `score` | `Score` | a position on an ordered scale | ratings, severity, loudness |

`fallback` is mandatory and is what the engine uses when the model returns something unusable. It is
declared per judgement because the engine cannot guess: for a probability the honest fallback decides
nothing, and for a choice it is whichever option means "none of the above". The console says which
judgements were substituted, so a fallback is never presented as the model's answer.

`question` must use `{{action}}` — a question that never mentions the player is judging nothing — and
may use `{{state}}`. Wording is behaviour here. See `ABOUT-THE-GAME.md` for two measured cases where
the same rules and different phrasing made a world unplayable, and note that both were found by
playing, not by reading.

### Rules

Evaluated in order. **The first whose condition holds is the one applied.** Order is therefore part of
the design, not an implementation detail: the escape room puts `implausible` first so an action the
model judged impossible cannot change the room however confident the classification was.

A condition is exactly one comparison or exactly one combinator — `all`, `any`, `not`, or `roll`.
Mixing forms is rejected at load time, because a condition that can express anything is a programming
language, and a world author should be writing data.

| Comparison | Meaning |
| --- | --- |
| `{"judgement": "x", "atLeast": 0.6}` | the probability is at or above |
| `{"judgement": "x", "below": 0.5}` | the probability is below |
| `{"judgement": "x", "equals": "take_key"}` | the option or level label matches |
| `{"judgement": "x", "isOneOf": ["a", "b"]}` | the option or level label is one of |
| `{"state": "has_key", "equals": "true"}` | a state field matches |

A `roll` is how a world uses a probability *as* a probability rather than as a threshold:

```json
{ "roll": { "chanceFrom": "progress", "atLeast": 0.6 } }
```

The judgement supplies the chance and the engine rolls against it. `atLeast` is a floor the chance must
clear **before** the dice are touched — without it, an attempt the model judged 2% likely would still
succeed on a lucky roll, which reads as the world ignoring its own judgement. That floor is why the
escape room has two sneak rules rather than one.

Effects are `set`, `increment`, `narrate`, `win`, and `lose`. `to` is always a string, interpreted
against the field's declared type, because a rule is data and `"true"` has to become a bool in a bool
field.

A world with no catch-all rule is legal, and the engine reports that nothing matched rather than
inventing an outcome.

## Validation

A broken world would otherwise produce a game that runs and behaves stupidly rather than one that
fails, so the loader rejects the mistakes that are silent:

- a rule comparing or setting a state field that does not exist — the highest-value check, because the
  result is a rule that never fires and looks like a design choice
- a rule comparing a judgement that was never declared
- a `stateTemplate` placeholder naming an undeclared field, which would send the model `{{has_keys}}`
- a question that does not use `{{action}}`
- a fallback that matches no declared option or level
- a `bool` field describing only one of its two values
- a condition mixing forms, or containing more than one comparison
- an unknown effect kind

Every message names the offending key. "Invalid world" is useless to an author; "rule 'take_key' sets
state 'has_keys', which no state field declares" is a fix.

## The pieces

| File | What it holds |
| --- | --- |
| `Jev.Sdk.World/WorldDefinition.cs` | The schema: state, judgements, rules, conditions, effects |
| `Jev.Sdk.World/WorldLoader.cs` | JSON loading, validation, and the placeholder syntax |
| `Jev.Sdk.World/MarkdownWorldLoader.cs` | Reading a world out of a markdown document |
| `Jev.Sdk.World/WorldPackage.cs` | Zip, folder, and content detection |
| `Jev.Sdk.World/WorldState.cs` | Live state, and the description sent to the model |
| `Jev.Sdk.World/Judgements.cs` | Building the API questions, and reading the answers back generically |
| `Jev.Sdk.World/RuleEngine.cs` | Conditions, dice, and effects. Knows nothing about any world |
| `Jev.Sdk.World/WorldRunner.cs` | One turn, as a function. No I/O, so a run is replayable in a test |
| `Jev.Sdk.World/WorldConsole.cs` | The console interface |
| `Jev.Sdk.World.Play/Program.cs` | The executable |
| `Jev.Sdk.World.Tests/` | 50 tests, no API key needed |

Sibling folders in `samples/`:

- `Game/` — the original hand-written Escape the Room, kept exactly as it was
- `World/` — this engine, with `worlds/escape-the-room.md` as the same game expressed as data

Inside `World/`:

- `worlds/` — hand-written worlds: Escape the Room, and The Lighthouse
- `worlds/classics/` — fifty generated worlds: five console classics in five styles. See `CLASSICS.md`
- `packages/` — those fifty, packaged as playable zips
- `tools/` — the generator that produced them, with its own schema documentation in `../schema/`

## Tests

```sh
# The engine: loading, validation, rules, state, packages. No key, no network.
dotnet test samples/World/Jev.Sdk.World.Tests
```

The engine is a pure function of (state, judgements, dice), so a world plays end to end here with the
dice pinned. The model's half — the wording — is covered by the live tests in
`tests/Jev.Sdk.IntegrationTests/GamePromptTests.cs`, which is the only place it can be.

## What the engine deliberately does not do

- **No arithmetic beyond `increment`.** A world that needs arithmetic needs a new effect kind, and that
  is a decision to make deliberately rather than a hole to leave open.
- **No nesting beyond `all`/`any`/`not`.** See above: a general expression language would stop this
  being data.
- **No world-specific code paths.** If a rule needs something the schema cannot express, the schema is
  what should change — visibly, in one place, for every world.
