---
id: zork-house-resource
title: Zork — The White House — Resource
schemaVersion: "1.0"
turnLimit: 20
goal: You are in front of a white house with a boarded front door. Below it lies the Great Underground Empire. Find the entrance, and get below.
---

# Zork — The White House — Resource

You are in front of a white house with a boarded front door. Below it lies the Great Underground Empire. Find the entrance, and get below.

## About this variant

**Resource style.** Adds a supply counter that actions consume. Waste, and you run dry.



Zork's opening, which is probably the most-read paragraph in interactive fiction.

The scenario is 'find the way in', so the win condition is reaching the underground

rather than escaping anything. The house is a shell with one hidden entrance.

## Examples

- open the window
- go around the back
- enter the house
- go down
- read the note

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "window_open",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The window is open.",
      "false": "The window is latched shut."
    }
  },
  {
    "key": "note_read",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You have read the note on the door.",
      "false": "There is a note on the door you have not read."
    }
  },
  {
    "key": "entered",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are inside the white house.",
      "false": "You are still outside."
    }
  },
  {
    "key": "below",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are below the house, in the dark of the Empire.",
      "false": "You are above ground."
    }
  },
  {
    "key": "moves",
    "type": "int",
    "initial": 0,
    "describe": {
      "*": "You have been at this a while."
    }
  },
  {
    "key": "supply",
    "type": "int",
    "initial": 6,
    "describe": {
      "0": "You are out of supplies entirely.",
      "1": "A single charge or ration remains.",
      "2": "You have a little left.",
      "*": "You have enough, for now."
    }
  }
]
```

## State template

Assembled from the `describe` entries above, and sent with every call.

```text
You stand before a white house with a boarded front door. {{window_open}} {{note_read}} {{entered}} {{below}} {{moves}} A leaflet is nailed to the door. {{supply}}
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
        "key": "open_window",
        "description": "Unlatch, force, or break the window so it is no longer shut."
      },
      {
        "key": "enter",
        "description": "Get themselves inside the house - climbing through an open window, stepping through a doorway, or any other way in. Not the act of opening it."
      },
      {
        "key": "read",
        "description": "Read, look at, or examine something."
      },
      {
        "key": "go_down",
        "description": "Go down, descend a staircase, or climb down into the dark."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the house."
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
    "key": "supply_cost",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". How much of their limited supplies would that consume - a light, a ration, a charge, a tool worn down? Answer with the fraction of a unit used. Simply looking at or reasoning about something uses none."
  }
]
```

## Rules

Evaluated in order; the first whose condition holds is applied. Order is part of the design.

```json
[
  {
    "id": "out_of_supply",
    "when": {
      "state": "supply",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "You have nothing left to work with."
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
    "id": "read_note",
    "when": {
      "judgement": "intent",
      "equals": "read"
    },
    "then": [
      {
        "kind": "set",
        "state": "note_read",
        "to": "true"
      }
    ],
    "narrate": [
      "\"WELCOME TO ZORK. The white house is a good starting point.\""
    ]
  },
  {
    "id": "open_window",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "open_window"
        },
        {
          "state": "window_open",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "window_open",
        "to": "true"
      }
    ],
    "narrate": [
      "The latch gives. The window swings in."
    ]
  },
  {
    "id": "window_already",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "open_window"
        },
        {
          "state": "window_open",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "It is already open."
    ]
  },
  {
    "id": "enter_without_window",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "enter"
        },
        {
          "state": "window_open",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "The front door is boarded and the window is latched. There is no way in."
    ]
  },
  {
    "id": "enter",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "enter"
        },
        {
          "state": "window_open",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "entered",
        "to": "true"
      }
    ],
    "narrate": [
      "You climb through into a dim room. A staircase leads down."
    ]
  },
  {
    "id": "go_down_in_house",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "go_down"
        },
        {
          "state": "entered",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "below",
        "to": "true"
      },
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "You descend. The stairs go on far longer than the house is tall.",
      "You are below, in the dark, and the Great Underground Empire is open around you."
    ]
  },
  {
    "id": "go_down_outside",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "go_down"
        },
        {
          "state": "entered",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "There is open ground here. Nothing to descend."
    ]
  },
  {
    "id": "time_passes",
    "then": [
      {
        "kind": "increment",
        "state": "moves",
        "by": 1
      }
    ],
    "narrate": [
      "Time passes."
    ]
  },
  {
    "id": "consumed",
    "when": {
      "all": [
        {
          "judgement": "supply_cost",
          "atLeast": 0.5
        },
        {
          "state": "supply",
          "atLeast": 1
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "supply",
        "by": -1
      }
    ],
    "narrate": [
      "That used something up."
    ]
  }
]
```

## Ending

```json
{
  "won": "You are standing in the dark at the top of the Empire.",
  "lost": "You never find your way in."
}
```
