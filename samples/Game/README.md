# Escape the Room

A demo game built on `Jev.Sdk`. One room, one guard, one key, one door, one window. The player types
anything they like and the game figures out what they meant.

The game itself is not the point. The point is that after five minutes of playing, it is obvious what
`Choice`, `Score`, and `Noul` are for, why sending state matters, and why probabilities are more useful
than a yes/no.

```text
> I quietly creep over and slip the key off the table.

Jev:
  intent:      take_key          99%
  plausible:   95%
  wakes_guard: 27%
  noise:       quiet
  progress:    74%

You take the key.

  key: held   door: locked   window: intact   guard: asleep
```

The player never learns a command. "grab key", "pocket the little brass thing", and "I quietly creep
over and slip the key off the table" all classify to `take_key`.

## Running it

```sh
dotnet run --project samples/Game/Jev.Sdk.Game
```

It needs an API key, because every turn asks the model what you are trying to do. The key is resolved
the same way the rest of the repository resolves it:

1. as the first command-line argument
2. from `TYPESAFE_API_KEY`
3. from `appSettings.json` or `appSettings.{MACHINE_NAME}.json` beside the executable

```sh
TYPESAFE_API_KEY=your-key dotnet run --project samples/Game/Jev.Sdk.Game
```

The example configuration files are the ones in `samples/Jev.Sdk.Sample`, copied beside the executable
at build time, so the same instructions apply: copy `.env.example` to `.env`, or edit
`appSettings.json`. A value still reading `REPLACE ME` counts as unconfigured and the game prompts.

## The five questions in one call

Every turn sends the room's description plus one `system_one` call carrying five questions of three
kinds. This is the whole demonstration:

```text
player command
      ↓
   one system_one call
      ↓
 ┌──────────────────┐
 intent       Choice     what are they trying to do
 plausible    Noul       is that physically doable at all
 wakes_guard  Noul       does it disturb the guard
 noise        Score      how loud is it, on an ordered scale
 progress     Noul       would it actually work
 └──────────────────┘
      ↓
game logic (no model involved)
```

## What is the model's job, and what is the game's

This separation is the design's central claim, and the code is arranged to make it visible.

**The model supplies judgement.** It never changes the world. `TurnJudgement` reads the five answers
into named fields and nothing else touches a raw `Answer`.

**The game supplies rules.** `Room.Apply` is a pure function of (state, judgement, dice). It has no
`JevClient`, makes no calls, and every outcome is deterministic given the same inputs — which is why
the game is fully testable with no API key.

The rules decide things no judgement should: taking the key only works if there is a key, unlocking
only works if you have it, and an action the model judged physically impossible changes nothing no
matter how confident it was about the intent.

The probabilities decide things no rule can: whether slipping past an awake guard works, and what
comes of a creative action that matches no known intent.

## Two prompt bugs worth knowing about

Both were found by playing the game against the live model, not by reading the code. Both made the
game unplayable, and both left every rule test passing — which is the lesson: **this game's behaviour
depends on prompt wording as much as on code.**

**Plausibility read as a precondition check.** The first wording asked whether an action was possible
"from the position described in the state". The model took that to mean *is the world already set up
for this*, and scored walking across the room to pick up the key at 0.55 and "grab the key" at 0.55 —
below the threshold, so almost nothing the player typed could ever work. Reworded to ask whether the
body could carry it out, it scores 0.77-0.95 for reachable actions and 0.02 for flying through the
ceiling.

**Noise scored 0.17 for everything.** The first wording asked "How loud is the action described in the
player's own words?" and returned roughly 0.17 for every action, *including smashing a window*. The
label came back "silent" every time, so the rule that wakes the guard on a loud action never fired.
Adding anchors to the instruction fixed it: 2.9 for breaking glass, 0.7-0.9 for quiet movement.

Both are now held down by live tests in `tests/Jev.Sdk.IntegrationTests/GamePromptTests.cs`, so a
future rewording fails there rather than in a play session.

**The live tests are not perfectly deterministic, and that is worth knowing.** Three consecutive runs
of the suite passed 74/74, but a fourth failed one row: the model is a judgement call, and a borderline
score can land on either side of the threshold. The tests assert clear-cut behaviour for that reason —
flying through the ceiling versus reaching for a visible object, not two similar actions. If a live row
fails, re-run before assuming the prompt broke.

**And one thing the model will not do.** Plausibility does not enforce inventory. "unlock the door
with the key" scores 0.69-0.78 with the key on a table and 0.90-0.94 with it held, consistently — the
model judges the motion possible either way. That is fine, because the game's own rules check the
state directly before acting. Trying to make this judgement carry that weight would mean a threshold
that also rejects real actions.

## Layout

| File | What it holds |
| --- | --- |
| `Game.md` | The design this implements, in the author's own words |
| `Jev.Sdk.Game/RoomState.cs` | The world: five booleans and a turn count, plus the description sent to the model |
| `Jev.Sdk.Game/QuestionSet.cs` | The five questions, and the intent and noise vocabularies |
| `Jev.Sdk.Game/TurnJudgement.cs` | Reading a response into named fields, with neutral fallbacks |
| `Jev.Sdk.Game/Room.cs` | The rules. No model, no I/O, deterministic given the dice |
| `Jev.Sdk.Game/ConsoleOutput.cs` | Every line the game prints |
| `Jev.Sdk.Game/Program.cs` | The turn loop |
| `Jev.Sdk.Game.Tests/` | 44 tests. No API key needed |

## Tests

```sh
# The rules and the answer reading. No API key, no network.
dotnet test samples/Game/Jev.Sdk.Game.Tests

# The prompt wording, against the live model. Skips without a key.
dotnet test tests/Jev.Sdk.IntegrationTests --filter "FullyQualifiedName~GamePromptTests"
```

The unit tests cover the rules including the escape path end to end, and the boundary that reads a
response — including what happens when the model returns something missing, of the wrong kind, or
invented. The live tests cover the wording, which is where the two bugs above lived.

## Things it deliberately does not do

- **No combat.** Attacking the guard wakes them and gains nothing. One guard who stays a threat is
  what makes sneaking and talking meaningful choices.
- **No inventory enforcement in the model.** See above: the rules do that.
- **No turn beyond 25.** A run ends rather than looping forever if the player never escapes.
- **No silent failures.** When the model returns something unusable the console says so and a neutral
  value is used, rather than presenting a substituted value as the model's answer.
