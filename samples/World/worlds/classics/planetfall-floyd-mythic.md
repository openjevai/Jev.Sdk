---
id: planetfall-floyd-mythic
title: Planetfall — Floyd — Mythic
schemaVersion: "1.0"
turnLimit: 25
goal: The station is dying and everyone else has gone. You are not alone, though: a small robot with a lot of opinions has decided to help. Get off the station.
---

# Planetfall — Floyd — Mythic

The station is dying and everyone else has gone. You are not alone, though: a small robot with a lot of opinions has decided to help. Get off the station.

## About this variant

**Mythic style.** Adds fate - which can make the impossible work - and a doom clock that advances every turn.



Planetfall's real subject was never the puzzles: it was the robot. This scenario makes

that explicit. Floyd's willingness is the resource, and the fastest routes through the

station are also the ones that put him in danger - the design's whole emotional engine,

expressed as a counter and a rule rather than as a scripted scene.

## Examples

- ask Floyd for help
- send Floyd into the dark
- talk to Floyd
- find the escape pod
- fix the reactor

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "floyd_trust",
    "type": "int",
    "initial": 2,
    "describe": {
      "0": "Floyd is frightened and will not go anywhere.",
      "1": "Floyd is uncertain and staying close.",
      "2": "Floyd is cheerfully following you.",
      "*": "Floyd would follow you anywhere."
    }
  },
  {
    "key": "floyd_here",
    "type": "bool",
    "initial": true,
    "describe": {
      "true": "Floyd is hovering at your shoulder.",
      "false": "Floyd is gone."
    }
  },
  {
    "key": "reactor_fixed",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "The reactor is stable.",
      "false": "The reactor is failing."
    }
  },
  {
    "key": "hull_integrity",
    "type": "int",
    "initial": 6,
    "describe": {
      "0": "The station is coming apart.",
      "1": "There is very little station left.",
      "*": "The hull is holding, for now."
    }
  },
  {
    "key": "escaped",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are in the pod and away.",
      "false": "You are still aboard."
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
You are aboard a failing space station. {{floyd_trust}} {{floyd_here}} {{reactor_fixed}} {{hull_integrity}} {{escaped}} Alarms are sounding somewhere below. {{doom}}
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
        "key": "ask_floyd",
        "description": "Ask Floyd for help."
      },
      {
        "key": "send_floyd",
        "description": "Send Floyd alone into somewhere dangerous."
      },
      {
        "key": "talk_floyd",
        "description": "Talk to Floyd, reassure him, or keep him company."
      },
      {
        "key": "repair",
        "description": "Fix, repair, or stabilise equipment - especially the reactor."
      },
      {
        "key": "escape",
        "description": "Get to the escape pod and launch."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the station."
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
    "id": "destroyed",
    "when": {
      "state": "hull_integrity",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The station comes apart faster than the alarms can say so."
    ]
  },
  {
    "id": "away",
    "when": {
      "state": "escaped",
      "equals": "true"
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "The pod separates and the station falls away behind you.",
      "Floyd is watching it through the port, and does not say anything."
    ]
  },
  {
    "id": "floyd_leaves",
    "when": {
      "all": [
        {
          "state": "floyd_trust",
          "below": 1
        },
        {
          "state": "floyd_here",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "floyd_here",
        "to": "false"
      }
    ],
    "narrate": [
      "Floyd goes quiet, then goes away. You do not see which way."
    ]
  },
  {
    "id": "ask_floyd_without_floyd",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ask_floyd"
        },
        {
          "state": "floyd_here",
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
      "There is no answer. Floyd is not here."
    ]
  },
  {
    "id": "ask_floyd_frightened",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ask_floyd"
        },
        {
          "state": "floyd_trust",
          "below": 1
        },
        {
          "state": "floyd_here",
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
      "Floyd hovers, and does not move toward the door."
    ]
  },
  {
    "id": "ask_floyd_helps",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ask_floyd"
        },
        {
          "state": "floyd_trust",
          "atLeast": 2
        },
        {
          "state": "reactor_fixed",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "reactor_fixed",
        "to": "true"
      }
    ],
    "narrate": [
      "Floyd sails through the hatch without being asked twice.",
      "\"I have got it, I have got it.\"",
      "The reactor noise drops to a hum."
    ]
  },
  {
    "id": "send_floyd_works",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "send_floyd"
        },
        {
          "state": "floyd_trust",
          "atLeast": 1
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "floyd_trust",
        "by": -1
      }
    ],
    "narrate": [
      "Floyd goes where you point him. He does not look enthusiastic.",
      "It works. It costs him something."
    ]
  },
  {
    "id": "send_floyd_refused",
    "when": {
      "judgement": "intent",
      "equals": "send_floyd"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Floyd does not go. He has decided not to."
    ]
  },
  {
    "id": "talk_floyd",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "talk_floyd"
        },
        {
          "state": "floyd_here",
          "equals": "true"
        },
        {
          "state": "floyd_trust",
          "below": 3
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "floyd_trust",
        "by": 1
      }
    ],
    "narrate": [
      "You tell him he is doing well. He tells you a very long story about a fish."
    ]
  },
  {
    "id": "repair_reactor",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "repair"
        },
        {
          "state": "reactor_fixed",
          "equals": "false"
        },
        {
          "judgement": "progress",
          "atLeast": 0.6
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "reactor_fixed",
        "to": "true"
      }
    ],
    "narrate": [
      "You get the coupling seated and the reactor settles."
    ]
  },
  {
    "id": "repair_fails",
    "when": {
      "judgement": "intent",
      "equals": "repair"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "You cannot reach the coupling from here."
    ]
  },
  {
    "id": "escape_without_reactor",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "escape"
        },
        {
          "state": "reactor_fixed",
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
      "The pod has no power while the reactor is failing. There has to be another way."
    ]
  },
  {
    "id": "escape_with_reactor",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "escape"
        },
        {
          "state": "reactor_fixed",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "escaped",
        "to": "true"
      }
    ],
    "narrate": [
      "The pod lights up. You strap in."
    ]
  },
  {
    "id": "station_fails",
    "when": {
      "state": "reactor_fixed",
      "equals": "false"
    },
    "then": [
      {
        "kind": "increment",
        "state": "hull_integrity",
        "by": -1
      }
    ],
    "narrate": [
      "Another alarm joins the others."
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
  "won": "You are clear of the station.",
  "lost": "The station wins, and it was never close."
}
```
