---
id: escape-the-room
title: Escape the Room
schemaVersion: "1.0"
turnLimit: 25
goal: You are in a small locked room. There is a key on a table, a window, and a guard asleep in the corner. Get out.
---

# Escape the Room

You are in a small locked room. There is a key on a table, a window, and a guard asleep in the
corner. Get out.

The room is small enough to hold in your head: four facts about it, five questions asked each turn,
and a list of rules deciding what happens. Everything the game knows is written below, and nothing
about this room is compiled into the engine that plays it.

## Examples

- grab the key
- I quietly creep over and slip the key off the table
- throw the chair through the window
- sneak past the guard
- yell at the guard to wake up

## State

Four facts, each with the initial value. The `describe` entries are how each value reads to the
model — the state is sent as prose, because a question like "is this plausible" cannot be answered
from a row of booleans.

```json
[
  {
    "key": "has_key",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The key is in your pocket.",
      "false": "The key is on a table across the room."
    }
  },
  {
    "key": "door_unlocked",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The door is unlocked.",
      "false": "The door is locked."
    }
  },
  {
    "key": "window_broken",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The window is broken, with cold air coming through.",
      "false": "The window is intact."
    }
  },
  {
    "key": "guard_awake",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The guard is awake and watching you.",
      "false": "The guard is asleep in the corner."
    }
  }
]
```

## State template

The description sent with every call, assembled from the `describe` entries above.

```text
You are in a small locked room. A heavy door is the only way out. {{has_key}} {{door_unlocked}} {{window_broken}} {{guard_awake}}
```

## Judgements

Five questions asked **in one call**: one choice, three probabilities, one rating. The `{{action}}`
placeholder is replaced with the player's own words.

Wording is behaviour here, not decoration. Two measured examples:

- An earlier plausibility question asked whether an action was possible *"from the position described
  in the state"*. The model read that as a precondition check and scored walking across the room to
  pick up a visible key at 0.55 — below the threshold, so almost nothing a player typed could work.
  The wording below scores that at 0.77–0.95 and flying through the ceiling at 0.02.
- An earlier noise question asked *"How loud is the action described in the player's own words?"* and
  returned roughly 0.17 for everything, including smashing a window, so the label always came back
  "silent" and the rule that wakes the guard on a loud action never fired. Anchoring the scale with
  examples gives 2.9 for breaking glass against 0.7–0.9 for quiet movement.

`fallback` is what the engine uses when the model returns something unusable for that question. It is
declared per judgement because the engine cannot guess: for a probability the honest fallback is a
value that decides nothing, and for a choice it is whichever option means "none of the above".

```json
[
  {
    "key": "intent",
    "kind": "choice",
    "fallback": "other",
    "question": "The player typed: \"{{action}}\". Which single action are they attempting? Choose 'other' unless the text describes an action they are actually trying to carry out - simply mentioning an object is not an attempt to do something to it.",
    "options": [
      { "key": "take_key",         "description": "Pick up or pocket the key, however described: grab it, take it, swipe it, slip it off the table." },
      { "key": "unlock_door",      "description": "Unlock the door, including opening it once it is already unlocked." },
      { "key": "break_window",     "description": "Break, smash, or force the window." },
      { "key": "sneak_past_guard", "description": "Try to get past the guard or out of the room without being noticed." },
      { "key": "attack_guard",     "description": "Attack, restrain, or otherwise physically overpower the guard." },
      { "key": "talk_to_guard",    "description": "Speak to the guard, wake them deliberately, or try to persuade them." },
      { "key": "other",            "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do." }
    ]
  },
  {
    "key": "plausible",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". Could a person standing in this room physically carry that out? Judge only whether the body could do it, not whether it will succeed and not whether it is a good idea. Reaching for a visible object on a table is possible, including walking over to it first. Flying through the ceiling is not."
  },
  {
    "key": "wakes_guard",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". Would carrying that out disturb or wake the guard? Answer with the probability that the guard is disturbed by it."
  },
  {
    "key": "noise",
    "kind": "score",
    "fallback": "quiet",
    "levels": [ "silent", "quiet", "loud", "extremely loud" ],
    "question": "The player typed: \"{{action}}\". How much noise would carrying that out make in this room? Shouting and breaking glass are extremely loud. Picking something up quietly is silent or quiet."
  },
  {
    "key": "progress",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". If they attempt that, how likely is it to actually work - to move them closer to getting out of the room rather than being a distraction or a wasted move? Answer with the probability that the attempt succeeds at getting them closer to escape."
  }
]
```

## Rules

Evaluated in order. The **first** rule whose condition holds is the one applied, so order is part of
the design: `implausible` sits first, which is why an action the model judged impossible cannot change
the room however confident the classification was. `nothing` at the end is the catch-all — without it
a turn matching no rule would report that the world has no answer, which is true but unhelpful.

The two rules carrying a `roll` are where the probabilities do work no rule could. `sneak_awake_guard_success`
is a contest against an awake guard, and its chance comes from the model's own judgement that the
attempt would work. `creative_success` is the case the designer never anticipated: the player typed
something matching no named action, and one probability decides whether it does anything. The
`atLeast` floor on each keeps a hopeless attempt hopeless — without it, a 2%-likely action would still
succeed on a lucky roll, which reads as the game ignoring its own judgement.

```json
[
  {
    "id": "implausible",
    "when": { "judgement": "plausible", "below": 0.5 },
    "narrate": [ "That does not work here." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  },
  {
    "id": "take_key",
    "when": { "all": [
      { "judgement": "intent", "equals": "take_key" },
      { "state": "has_key", "equals": "false" }
    ] },
    "narrate": [ "You take the key." ],
    "then": [ { "kind": "set", "state": "has_key", "to": "true" } ]
  },
  {
    "id": "already_has_key",
    "when": { "all": [
      { "judgement": "intent", "equals": "take_key" },
      { "state": "has_key", "equals": "true" }
    ] },
    "narrate": [ "You already have the key." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  },
  {
    "id": "unlock_without_key",
    "when": { "all": [
      { "judgement": "intent", "equals": "unlock_door" },
      { "state": "has_key", "equals": "false" }
    ] },
    "narrate": [ "The door is locked and you do not have the key." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  },
  {
    "id": "unlock_door",
    "when": { "all": [
      { "judgement": "intent", "equals": "unlock_door" },
      { "state": "has_key", "equals": "true" },
      { "state": "door_unlocked", "equals": "false" }
    ] },
    "narrate": [ "The key turns. The door is unlocked." ],
    "then": [ { "kind": "set", "state": "door_unlocked", "to": "true" } ]
  },
  {
    "id": "leave_through_open_door",
    "when": { "all": [
      { "judgement": "intent", "equals": "unlock_door" },
      { "state": "door_unlocked", "equals": "true" }
    ] },
    "narrate": [ "You open the door and step out into the corridor.", "You are out." ],
    "then": [ { "kind": "win" } ]
  },
  {
    "id": "break_window",
    "when": { "all": [
      { "judgement": "intent", "equals": "break_window" },
      { "state": "window_broken", "equals": "false" }
    ] },
    "narrate": [ "CRASH!", "The window breaks." ],
    "then": [ { "kind": "set", "state": "window_broken", "to": "true" } ]
  },
  {
    "id": "attack_guard",
    "when": { "judgement": "intent", "equals": "attack_guard" },
    "narrate": [ "You lunge at the guard. It does not go well.", "The guard is awake, and now they are watching you closely." ],
    "then": [ { "kind": "set", "state": "guard_awake", "to": "true" } ]
  },
  {
    "id": "talk_to_sleeping_guard",
    "when": { "all": [
      { "judgement": "intent", "equals": "talk_to_guard" },
      { "state": "guard_awake", "equals": "false" }
    ] },
    "narrate": [ "You speak. The guard stirs and opens their eyes." ],
    "then": [ { "kind": "set", "state": "guard_awake", "to": "true" } ]
  },
  {
    "id": "talk_to_awake_guard",
    "when": { "judgement": "intent", "equals": "talk_to_guard" },
    "narrate": [ "The guard hears you out, unmoved." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  },
  {
    "id": "sneak_locked_door",
    "when": { "all": [
      { "judgement": "intent", "equals": "sneak_past_guard" },
      { "state": "door_unlocked", "equals": "false" }
    ] },
    "narrate": [ "The door is still locked, so there is nowhere to go." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  },
  {
    "id": "sneak_sleeping_guard",
    "when": { "all": [
      { "judgement": "intent", "equals": "sneak_past_guard" },
      { "state": "guard_awake", "equals": "false" }
    ] },
    "narrate": [ "You slip past the sleeping guard.", "You open the door and step out into the corridor.", "You are out." ],
    "then": [ { "kind": "win" } ]
  },
  {
    "id": "sneak_awake_guard_success",
    "when": { "all": [
      { "judgement": "intent", "equals": "sneak_past_guard" },
      { "state": "guard_awake", "equals": "true" },
      { "state": "door_unlocked", "equals": "true" },
      { "roll": { "chanceFrom": "progress", "atLeast": 0.6 } }
    ] },
    "narrate": [ "You slip past the guard.", "You open the door and step out into the corridor.", "You are out." ],
    "then": [ { "kind": "win" } ]
  },
  {
    "id": "sneak_awake_guard_failure",
    "when": { "judgement": "intent", "equals": "sneak_past_guard" },
    "narrate": [ "The guard sees you move." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  },
  {
    "id": "creative_success",
    "when": { "all": [
      { "judgement": "intent", "equals": "other" },
      { "state": "guard_awake", "equals": "true" },
      { "roll": { "chanceFrom": "progress", "atLeast": 0.5 } }
    ] },
    "narrate": [ "It works, more or less.", "The guard is distracted." ],
    "then": [ { "kind": "set", "state": "guard_awake", "to": "false" } ]
  },
  {
    "id": "nothing",
    "narrate": [ "Nothing comes of it." ],
    "then": [ { "kind": "narrate", "text": "nothing" } ]
  }
]
```

## Ending

```json
{
  "won": "You are out.",
  "lost": "You did not get out. The guard is still there."
}
```
