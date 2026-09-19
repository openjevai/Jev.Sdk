---
id: colossal-cave-survey-resource
title: Colossal Cave — The Survey — Resource
schemaVersion: "1.0"
turnLimit: 30
goal: You are not here for gold. You are here to map the cave. Three passages, and you need each one recorded before you leave.
---

# Colossal Cave — The Survey — Resource

You are not here for gold. You are here to map the cave. Three passages, and you need each one recorded before you leave.

## About this variant

**Resource style.** Adds a supply counter that actions consume. Waste, and you run dry.



The same cave, a different game: the goal is knowledge rather than loot, and the win

condition counts mapped passages instead of a carried object. This is the 'different

styles' axis applied to the SCENARIO rather than the judgement frame - the place is

identical and the point of being there is not.

## Examples

- map the passage
- go deeper
- sketch the chamber
- return to the surface

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
      "true": "Your lamp is lit.",
      "false": "Your lamp is out."
    }
  },
  {
    "key": "mapped",
    "type": "int",
    "initial": 0,
    "describe": {
      "0": "You have mapped nothing yet.",
      "1": "One passage is recorded.",
      "2": "Two passages are recorded.",
      "*": "Your survey is nearly complete."
    }
  },
  {
    "key": "where",
    "type": "text",
    "initial": "surface",
    "describe": {
      "surface": "You are at the cave mouth.",
      "cavern": "You are in the first passage.",
      "treasury": "You are in a chamber beyond the passage.",
      "*": "You are somewhere in the cave."
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
You are surveying Colossal Cave. {{where}} {{lamp_lit}} {{mapped}} You are mapping, not looting; you need every passage recorded. {{supply}}
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
        "description": "Light the lamp."
      },
      {
        "key": "survey",
        "description": "Map, sketch, or record the place you are in."
      },
      {
        "key": "descend",
        "description": "Move deeper into the cave."
      },
      {
        "key": "ascend",
        "description": "Move back toward the surface."
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
      "The lamp catches."
    ]
  },
  {
    "id": "survey_nothing_here",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "survey"
        },
        {
          "state": "where",
          "equals": "surface"
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
      "You are outdoors. There is nothing here to survey."
    ]
  },
  {
    "id": "survey_cavern",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "survey"
        },
        {
          "state": "where",
          "equals": "cavern"
        },
        {
          "state": "mapped",
          "below": 2
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "mapped",
        "to": "1"
      },
      {
        "kind": "set",
        "state": "where",
        "to": "cavern"
      }
    ],
    "narrate": [
      "You pace the passage and mark its turns. One recorded."
    ]
  },
  {
    "id": "survey_treasury",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "survey"
        },
        {
          "state": "where",
          "equals": "treasury"
        },
        {
          "state": "mapped",
          "below": 2
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "mapped",
        "to": "2"
      }
    ],
    "narrate": [
      "You sketch the chamber's width and depth. Two recorded."
    ]
  },
  {
    "id": "survey_done",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "survey"
        },
        {
          "state": "mapped",
          "atLeast": 2
        }
      ]
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "Every passage is on the page.",
      "You climb out with a full survey."
    ]
  },
  {
    "id": "descend_to_cavern",
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
      "You go in."
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
      "A chamber opens ahead."
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
      "You climb back to the passage."
    ]
  },
  {
    "id": "ascend_out_empty",
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
          "state": "mapped",
          "below": 2
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
      "You come out, but the survey is unfinished. You will have to go back in."
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
  "won": "The survey is complete and legible.",
  "lost": "You never finish the map."
}
```
