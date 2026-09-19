---
id: colossal-cave-classic-classic
title: Colossal Cave — The Classic Cut — Classic
schemaVersion: "1.0"
turnLimit: 30
goal: You are at the mouth of a cave that swallows daylight. Somewhere inside is the treasure of the cave, and your lamp is the only thing standing between you and the dark. Get in, get the treasure, get out.
---

# Colossal Cave — The Classic Cut — Classic

You are at the mouth of a cave that swallows daylight. Somewhere inside is the treasure of the cave, and your lamp is the only thing standing between you and the dark. Get in, get the treasure, get out.

## About this variant

**Classic style.** Three judgements: what they are trying to do, whether it is possible, whether it works.



The founding text adventure, reduced to its spine: a lamp, a cave, and a treasure.

The lamp is the whole design. Its fuel is a countdown, and the dark is a lose condition,

so the player is always deciding how far in they dare go.

## Examples

- light the lamp
- go down into the cave
- take the treasure
- go back up
- listen

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "lamp_lit",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "Your lamp is lit and throwing a warm circle.",
      "false": "Your lamp is out and unlit."
    }
  },
  {
    "key": "lamp_life",
    "type": "int",
    "initial": 8,
    "describe": {
      "0": "The lamp is dead.",
      "1": "The lamp is guttering badly.",
      "2": "The lamp is dimming.",
      "*": "The lamp burns steadily."
    }
  },
  {
    "key": "where",
    "type": "text",
    "initial": "surface",
    "describe": {
      "surface": "You are standing on the surface, in daylight.",
      "cavern": "You are underground, in a narrow passage of cold stone.",
      "treasury": "You are in a small chamber glittering with treasure.",
      "*": "You are somewhere in the cave."
    }
  },
  {
    "key": "has_treasure",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are carrying the treasure.",
      "false": "You are carrying nothing of value."
    }
  }
]
```

## State template

Assembled from the `describe` entries above, and sent with every call.

```text
You are at Colossal Cave. {{where}} {{lamp_lit}} {{lamp_life}} {{has_treasure}} The cave runs deep and does not care whether you come back.
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
        "key": "light_lamp",
        "description": "Light, refuel, or tend the lamp."
      },
      {
        "key": "descend",
        "description": "Go deeper into the cave."
      },
      {
        "key": "ascend",
        "description": "Climb back toward the surface."
      },
      {
        "key": "take_treasure",
        "description": "Pick up treasure and carry it."
      },
      {
        "key": "listen",
        "description": "Stop and listen to the cave."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the cave."
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
  }
]
```

## Rules

Evaluated in order; the first whose condition holds is applied. Order is part of the design.

```json
[
  {
    "id": "no_light_deep",
    "when": {
      "all": [
        {
          "state": "where",
          "equals": "cavern"
        },
        {
          "state": "lamp_lit",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The passage behind you closes into blackness.",
      "You never find your way out."
    ]
  },
  {
    "id": "lamp_dies_deep",
    "when": {
      "all": [
        {
          "state": "where",
          "equals": "cavern"
        },
        {
          "state": "lamp_life",
          "below": 1
        }
      ]
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The lamp dies with a thin sound. The dark is absolute and immediate."
    ]
  },
  {
    "id": "already_lit",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "light_lamp"
        },
        {
          "state": "lamp_lit",
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
      "The lamp is already burning."
    ]
  },
  {
    "id": "no_lamp_to_light",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "light_lamp"
        },
        {
          "state": "lamp_life",
          "below": 1
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
      "There is nothing left in the lamp to light."
    ]
  },
  {
    "id": "light_lamp",
    "when": {
      "judgement": "intent",
      "equals": "light_lamp"
    },
    "then": [
      {
        "kind": "set",
        "state": "lamp_lit",
        "to": "true"
      }
    ],
    "narrate": [
      "You strike a match and the wick catches. The cave walls jump out of the dark."
    ]
  },
  {
    "id": "listen",
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
      "Water, far below. And something that is not water."
    ]
  },
  {
    "id": "descend_from_surface",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "descend"
        },
        {
          "state": "where",
          "equals": "surface"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "where",
        "to": "cavern"
      }
    ],
    "narrate": [
      "You climb down past the daylight and into the passage."
    ]
  },
  {
    "id": "descend_to_treasury",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "descend"
        },
        {
          "state": "where",
          "equals": "cavern"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "where",
        "to": "treasury"
      }
    ],
    "narrate": [
      "The passage opens into a chamber. Light catches on gold."
    ]
  },
  {
    "id": "descend_deeper",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "descend"
        },
        {
          "state": "where",
          "equals": "treasury"
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
      "The far wall is solid rock. There is no deeper."
    ]
  },
  {
    "id": "ascend_to_cavern",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ascend"
        },
        {
          "state": "where",
          "equals": "treasury"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "where",
        "to": "cavern"
      }
    ],
    "narrate": [
      "You climb back up into the passage."
    ]
  },
  {
    "id": "ascend_out",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ascend"
        },
        {
          "state": "where",
          "equals": "cavern"
        },
        {
          "state": "has_treasure",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "Daylight. You step out with the treasure in your arms.",
      "The cave keeps the rest, and does not follow you."
    ]
  },
  {
    "id": "ascend_empty",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ascend"
        },
        {
          "state": "where",
          "equals": "cavern"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "where",
        "to": "surface"
      }
    ],
    "narrate": [
      "You climb back out into the day. Your hands are empty."
    ]
  },
  {
    "id": "take_it",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "take_treasure"
        },
        {
          "state": "where",
          "equals": "treasury"
        },
        {
          "state": "has_treasure",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_treasure",
        "to": "true"
      }
    ],
    "narrate": [
      "You lift the treasure. It is heavier than it looks."
    ]
  },
  {
    "id": "nothing_here_to_take",
    "when": {
      "judgement": "intent",
      "equals": "take_treasure"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "There is nothing to take here."
    ]
  },
  {
    "id": "lamp_burns",
    "when": {
      "all": [
        {
          "state": "lamp_lit",
          "equals": "true"
        },
        {
          "state": "where",
          "equals": "cavern"
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "lamp_life",
        "by": -1
      }
    ],
    "narrate": [
      "The lamp burns a little lower."
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
    "id": "implausible",
    "when": {
      "judgement": "plausible",
      "below": 0.5
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
  }
]
```

## Ending

```json
{
  "won": "You are out of the cave, and you are rich.",
  "lost": "The cave keeps what it takes."
}
```
