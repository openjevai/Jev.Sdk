---
id: hobbit-riddles-mythic
title: The Hobbit — Riddles in the Dark — Mythic
schemaVersion: "1.0"
turnLimit: 20
goal: You are alone in a tunnel under the mountains with something in the water that wants to play a game. Answer well, and it will show you out.
---

# The Hobbit — Riddles in the Dark — Mythic

You are alone in a tunnel under the mountains with something in the water that wants to play a game. Answer well, and it will show you out.

## About this variant

**Mythic style.** Adds fate - which can make the impossible work - and a doom clock that advances every turn.



The riddle contest, which is the form's most famous single scene. The win condition is

OUTCOME-based rather than action-based: what matters is whether what you said was good,

which is exactly what a judgement question can assess and a keyword parser cannot.

## Examples

- riddle it back
- ask it a question
- feel along the wall
- guess wildly
- stay silent

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "standing",
    "type": "int",
    "initial": 0,
    "describe": {
      "0": "The two of you are level.",
      "1": "You have the better of it.",
      "2": "You are winning.",
      "*": "It is losing patience and interest."
    }
  },
  {
    "key": "rounds",
    "type": "int",
    "initial": 0,
    "describe": {
      "*": "The game goes on, turn after turn."
    }
  },
  {
    "key": "free",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The way out is behind you and it is not following.",
      "false": "You are still in the dark with it."
    }
  },
  {
    "key": "doom",
    "type": "int",
    "initial": 0,
    "describe": {
      "*": "Something is drawing closer. You can feel it."
    }
  }
]
```

## State template

Assembled from the `describe` entries above, and sent with every call.

```text
You are in a cold tunnel under the mountains, in the dark, with something in the water beside you. {{standing}} {{rounds}} {{free}} It is waiting for you to speak. {{doom}}
```

## Judgements

One API question per entry, all asked in a single call. `fallback` is what the engine uses
when the model returns something unusable for that question.

```json
[
  {
    "key": "intent",
    "kind": "choice",
    "fallback": "other",
    "question": "The player typed: \"{{action}}\". Which single action are they attempting? Choose 'other' unless the text describes an action they are actually trying to carry out - simply mentioning something is not an attempt to do something with it.",
    "options": [
      {
        "key": "riddle",
        "description": "Pose a riddle, a puzzle, or a question it must answer."
      },
      {
        "key": "guess",
        "description": "Answer it, guess, or make your own attempt at its riddle."
      },
      {
        "key": "listen",
        "description": "Listen, feel the walls, or look for the way out."
      },
      {
        "key": "silence",
        "description": "Say nothing, or refuse to play."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the thing in the water."
      }
    ]
  },
  {
    "key": "plausible",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". Could a person in this situation physically carry that out? Judge only whether they could do it, not whether it will succeed and not whether it is wise. Reaching for something within reach is possible, including walking to it first. Doing something no person could do, or acting on something that is not here at all, is not."
  },
  {
    "key": "progress",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". If they attempt that, how likely is it to actually work - to move them toward the goal rather than being a distraction or a wasted move? Answer with the probability that the attempt succeeds."
  },
  {
    "key": "fate",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". Setting aside whether it is possible: is this the sort of strange, bold, or uncanny move that the story rewards? Answer with the probability that fate favours it. Ordinary actions are unremarkable either way; attempts to bargain, defy, or trick something are where this matters."
  }
]
```

## Rules

Evaluated in order; the first whose condition holds is applied. Order is part of the design.

```json
[
  {
    "id": "doom_comes",
    "when": {
      "state": "doom",
      "atLeast": 6
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "Whatever was coming arrives.",
      "There is nothing more to try."
    ]
  },
  {
    "id": "fate_intervenes",
    "when": {
      "all": [
        {
          "judgement": "fate",
          "atLeast": 0.9
        },
        {
          "roll": {
            "chanceFrom": "fate"
          }
        }
      ]
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "The rules of this place bend, just once, and let you through."
    ]
  },
  {
    "id": "implausible",
    "when": {
      "judgement": "plausible",
      "below": 0.45
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "That is not something you can do here."
    ]
  },
  {
    "id": "freed",
    "when": {
      "state": "free",
      "equals": "true"
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "It tells you the way out, because it said it would.",
      "You go, and you do not look back."
    ]
  },
  {
    "id": "good_riddle",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "riddle"
        },
        {
          "judgement": "progress",
          "atLeast": 0.6
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "standing",
        "by": 1
      }
    ],
    "narrate": [
      "You put it to the dark, and the dark goes quiet.",
      "It has to think about that one."
    ]
  },
  {
    "id": "bad_riddle",
    "when": {
      "judgement": "intent",
      "equals": "riddle"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "It answers almost immediately. It was not a hard one."
    ]
  },
  {
    "id": "good_guess",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "guess"
        },
        {
          "judgement": "progress",
          "atLeast": 0.55
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "standing",
        "by": 1
      }
    ],
    "narrate": [
      "You guess. There is a pause, and then a sound that might be annoyance."
    ]
  },
  {
    "id": "bad_guess",
    "when": {
      "judgement": "intent",
      "equals": "guess"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Your answer hangs in the dark and is not accepted."
    ]
  },
  {
    "id": "escape_found",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "listen"
        },
        {
          "state": "standing",
          "atLeast": 2
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "free",
        "to": "true"
      }
    ],
    "narrate": [
      "Your hand finds a gap in the rock, and the gap is a passage."
    ]
  },
  {
    "id": "listen_no_gap",
    "when": {
      "judgement": "intent",
      "equals": "listen"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Cold stone, and water. No gap yet."
    ]
  },
  {
    "id": "silence_loses_it",
    "when": {
      "judgement": "intent",
      "equals": "silence"
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "It decides the game is over.",
      "It was always going to be over when it decided that."
    ]
  },
  {
    "id": "win_outright",
    "when": {
      "state": "standing",
      "atLeast": 3
    },
    "then": [
      {
        "kind": "set",
        "state": "free",
        "to": "true"
      }
    ],
    "narrate": [
      "It concedes. It is not pleased about it, but it keeps its word.",
      "There is a way out, and it is behind you."
    ]
  },
  {
    "id": "rounds_pass",
    "then": [
      {
        "kind": "increment",
        "state": "rounds",
        "by": 1
      }
    ],
    "narrate": [
      "The game goes on."
    ]
  },
  {
    "id": "nothing_happens",
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Nothing comes of it."
    ]
  },
  {
    "id": "doom_advances",
    "then": [
      {
        "kind": "increment",
        "state": "doom",
        "by": 1
      }
    ],
    "narrate": [
      "Something draws a little closer."
    ]
  }
]
```

## Ending

```json
{
  "won": "You are out from under the mountains.",
  "lost": "You never get out of the dark."
}
```
