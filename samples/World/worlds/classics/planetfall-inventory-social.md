---
id: planetfall-inventory-social
title: Planetfall — The Stores — Social
schemaVersion: "1.0"
turnLimit: 15
goal: The station is being abandoned and the stores are open. You cannot carry much and the pod is leaving. Take what matters.
---

# Planetfall — The Stores — Social

The station is being abandoned and the stores are open. You cannot carry much and the pod is leaving. Take what matters.

## About this variant

**Social style.** Adds another presence with a mood and a store of goodwill, so talking is a real route.



Planetfall's most-remembered mechanic, and the form's clearest example of a choice about

VALUE rather than survival: you cannot take everything, and what you leave behind is the

decision. The win condition asks whether what you took was worth carrying.

## Examples

- take the medical kit
- take the radio
- leave some things behind
- run for the pod
- search the stores

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "carried",
    "type": "int",
    "initial": 0,
    "describe": {
      "0": "Your hands are empty.",
      "1": "You are carrying one thing.",
      "2": "You are carrying two things.",
      "*": "Your arms are full."
    }
  },
  {
    "key": "has_medical",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You have the medical kit.",
      "false": "You do not have the medical kit."
    }
  },
  {
    "key": "has_radio",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You have the radio.",
      "false": "You do not have the radio."
    }
  },
  {
    "key": "has_archive",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You have the station archive.",
      "false": "The archive is still in the stores."
    }
  },
  {
    "key": "aboard",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are aboard the pod.",
      "false": "You are still in the stores."
    }
  },
  {
    "key": "goodwill",
    "type": "int",
    "initial": 1,
    "describe": {
      "0": "They want nothing to do with you.",
      "1": "They are watchful and uncommitted.",
      "2": "They are willing to hear you out.",
      "3": "They are on your side.",
      "*": "Their goodwill is hard to read."
    }
  }
]
```

## State template

Assembled from the `describe` entries above, and sent with every call.

```text
You are in the station's stores as the evacuation finishes. {{carried}} {{has_medical}} {{has_radio}} {{has_archive}} {{aboard}} The pod will not wait much longer. {{goodwill}}
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
        "key": "take",
        "description": "Take, pick up, or pocket an item."
      },
      {
        "key": "drop",
        "description": "Put something down or leave it behind."
      },
      {
        "key": "board",
        "description": "Get aboard the escape pod and go."
      },
      {
        "key": "search",
        "description": "Search, look around, or open a container."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the stores."
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
    "key": "mood",
    "kind": "score",
    "fallback": "wary",
    "question": "The player typed: \"{{action}}\". How does the other presence here take that - hostile at the low end, indifferent in the middle, warm and helpful at the top?",
    "levels": [
      "hostile",
      "wary",
      "civil",
      "warm"
    ]
  },
  {
    "key": "improves_goodwill",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". Would that make the other presence here more willing to help them, or less? Answer with the probability that it improves their goodwill rather than damaging it."
  }
]
```

## Rules

Evaluated in order; the first whose condition holds is applied. Order is part of the design.

```json
[
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
    "id": "away_with_something",
    "when": {
      "all": [
        {
          "state": "aboard",
          "equals": "true"
        },
        {
          "any": [
            {
              "state": "has_medical",
              "equals": "true"
            },
            {
              "state": "has_radio",
              "equals": "true"
            },
            {
              "state": "has_archive",
              "equals": "true"
            }
          ]
        }
      ]
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "The pod lights up and the station drops away.",
      "You got out, and you did not leave empty-handed."
    ]
  },
  {
    "id": "gone_with_nothing",
    "when": {
      "all": [
        {
          "state": "aboard",
          "equals": "true"
        },
        {
          "state": "carried",
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
      "The pod goes. You are aboard with empty hands.",
      "The stores are still there, and now nobody will ever open them."
    ]
  },
  {
    "id": "hands_full",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "take"
        },
        {
          "state": "carried",
          "atLeast": 2
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
      "Your arms are already full. Something has to go back."
    ]
  },
  {
    "id": "take_medical",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "take"
        },
        {
          "state": "has_medical",
          "equals": "false"
        },
        {
          "state": "carried",
          "below": 2
        },
        {
          "judgement": "progress",
          "atLeast": 0.5
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_medical",
        "to": "true"
      },
      {
        "kind": "increment",
        "state": "carried",
        "by": 1
      }
    ],
    "narrate": [
      "You take the medical kit. It is heavier than it looks."
    ]
  },
  {
    "id": "take_radio",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "take"
        },
        {
          "state": "has_radio",
          "equals": "false"
        },
        {
          "state": "carried",
          "below": 2
        },
        {
          "judgement": "progress",
          "atLeast": 0.5
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_radio",
        "to": "true"
      },
      {
        "kind": "increment",
        "state": "carried",
        "by": 1
      }
    ],
    "narrate": [
      "You take the radio. It fits under one arm."
    ]
  },
  {
    "id": "take_archive",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "take"
        },
        {
          "state": "has_archive",
          "equals": "false"
        },
        {
          "state": "carried",
          "below": 2
        },
        {
          "judgement": "progress",
          "atLeast": 0.5
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_archive",
        "to": "true"
      },
      {
        "kind": "increment",
        "state": "carried",
        "by": 1
      }
    ],
    "narrate": [
      "You take the station archive. It is a small case and it is very heavy."
    ]
  },
  {
    "id": "take_nothing_there",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "take"
        },
        {
          "state": "carried",
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
      "There is nothing there to take."
    ]
  },
  {
    "id": "drop_medical",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "drop"
        },
        {
          "state": "has_medical",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_medical",
        "to": "false"
      },
      {
        "kind": "increment",
        "state": "carried",
        "by": -1
      }
    ],
    "narrate": [
      "You set the medical kit down."
    ]
  },
  {
    "id": "drop_radio",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "drop"
        },
        {
          "state": "has_radio",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_radio",
        "to": "false"
      },
      {
        "kind": "increment",
        "state": "carried",
        "by": -1
      }
    ],
    "narrate": [
      "You set the radio down."
    ]
  },
  {
    "id": "drop_archive",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "drop"
        },
        {
          "state": "has_archive",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "has_archive",
        "to": "false"
      },
      {
        "kind": "increment",
        "state": "carried",
        "by": -1
      }
    ],
    "narrate": [
      "You set the archive down."
    ]
  },
  {
    "id": "search",
    "when": {
      "judgement": "intent",
      "equals": "search"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Shelves, mostly empty now. Someone took the good things first."
    ]
  },
  {
    "id": "board",
    "when": {
      "judgement": "intent",
      "equals": "board"
    },
    "then": [
      {
        "kind": "set",
        "state": "aboard",
        "to": "true"
      }
    ],
    "narrate": [
      "You climb in and pull the hatch."
    ]
  },
  {
    "id": "archive_is_the_point",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "board"
        },
        {
          "state": "has_archive",
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
      "You hesitate at the hatch. The station archive is still on its shelf."
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
    "id": "goodwill_up",
    "when": {
      "all": [
        {
          "judgement": "improves_goodwill",
          "atLeast": 0.6
        },
        {
          "state": "goodwill",
          "below": 3
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "goodwill",
        "by": 1
      }
    ],
    "narrate": [
      "They seem a little more inclined to help."
    ]
  },
  {
    "id": "goodwill_down",
    "when": {
      "all": [
        {
          "judgement": "improves_goodwill",
          "below": 0.3
        },
        {
          "state": "goodwill",
          "atLeast": 1
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "goodwill",
        "by": -1
      }
    ],
    "narrate": [
      "You have made this harder."
    ]
  }
]
```

## Ending

```json
{
  "won": "You leave with the things that mattered.",
  "lost": "You leave the station with nothing worth carrying."
}
```
