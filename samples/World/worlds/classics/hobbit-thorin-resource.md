---
id: hobbit-thorin-resource
title: The Hobbit — Thorin's Company — Resource
schemaVersion: "1.0"
turnLimit: 25
goal: You are a hobbit a long way from home, walking with thirteen dwarves and a wizard toward a mountain. Thorin does not think much of you. Get the company to the mountain.
---

# The Hobbit — Thorin's Company — Resource

You are a hobbit a long way from home, walking with thirteen dwarves and a wizard toward a mountain. Thorin does not think much of you. Get the company to the mountain.

## About this variant

**Resource style.** Adds a supply counter that actions consume. Waste, and you run dry.



The Hobbit's journey, and the first text adventure with a genuinely independent NPC:

Thorin has his own opinion of you and acts on it. The wizard is help that costs you the

chance to act for yourself.

## Examples

- keep walking
- ask Gandalf for advice
- offer to scout ahead
- talk to Thorin
- make camp

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "thorin_regard",
    "type": "int",
    "initial": 0,
    "describe": {
      "0": "Thorin treats you as baggage.",
      "1": "Thorin is beginning to watch you rather than ignore you.",
      "2": "Thorin has started asking your opinion.",
      "*": "Thorin has decided you are useful."
    }
  },
  {
    "key": "provisions",
    "type": "int",
    "initial": 5,
    "describe": {
      "0": "There is no food left.",
      "1": "Provisions are nearly gone.",
      "*": "The packs still have weight."
    }
  },
  {
    "key": "gandalf_here",
    "type": "bool",
    "initial": true,
    "describe": {
      "true": "Gandalf is with the company.",
      "false": "Gandalf has gone."
    }
  },
  {
    "key": "at_mountain",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You can see the mountain ahead.",
      "false": "The mountain is still far off."
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
You are on the road east with a company of dwarves. {{thorin_regard}} {{provisions}} {{gandalf_here}} {{at_mountain}} The road is long and the weather is turning. {{supply}}
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
        "key": "walk",
        "description": "Walk on, travel, or keep the company moving."
      },
      {
        "key": "scout",
        "description": "Scout, look ahead, or go first into danger."
      },
      {
        "key": "talk",
        "description": "Talk to Thorin, the dwarves, or Gandalf."
      },
      {
        "key": "ask_gandalf",
        "description": "Ask the wizard for help or advice."
      },
      {
        "key": "rest",
        "description": "Rest, eat, or make camp."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the company."
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
    "id": "arrived",
    "when": {
      "state": "at_mountain",
      "equals": "true"
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "The mountain fills the sky.",
      "You have walked the whole way there."
    ]
  },
  {
    "id": "starved",
    "when": {
      "state": "provisions",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The company cannot go on without food.",
      "The road east ends here."
    ]
  },
  {
    "id": "walk_on",
    "when": {
      "judgement": "intent",
      "equals": "walk"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Miles pass. Nothing tries to stop you."
    ]
  },
  {
    "id": "scout_success",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "scout"
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
        "state": "thorin_regard",
        "by": 1
      }
    ],
    "narrate": [
      "You go ahead and come back with news of the ground.",
      "Thorin says nothing, but he stops treating you as baggage."
    ]
  },
  {
    "id": "scout_failure",
    "when": {
      "judgement": "intent",
      "equals": "scout"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "You go ahead, find nothing worth reporting, and come back."
    ]
  },
  {
    "id": "ask_gandalf",
    "when": {
      "judgement": "intent",
      "equals": "ask_gandalf"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Gandalf considers the question, then answers it for you.",
      "You have learned nothing about your own judgement."
    ]
  },
  {
    "id": "gandalf_leaves",
    "when": {
      "all": [
        {
          "state": "gandalf_here",
          "equals": "true"
        },
        {
          "state": "provisions",
          "below": 3
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "gandalf_here",
        "to": "false"
      }
    ],
    "narrate": [
      "Gandalf looks at the road ahead, then at you.",
      "\"You will have to manage without me for a while.\"",
      "He is gone before anyone can argue."
    ]
  },
  {
    "id": "talk_thorin",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "talk"
        },
        {
          "judgement": "progress",
          "atLeast": 0.6
        },
        {
          "state": "thorin_regard",
          "below": 2
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "thorin_regard",
        "by": 1
      }
    ],
    "narrate": [
      "Thorin hears you out. It is not warmth, but it is attention."
    ]
  },
  {
    "id": "talk_dismissed",
    "when": {
      "judgement": "intent",
      "equals": "talk"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "Thorin makes a sound that is not quite a word and keeps walking."
    ]
  },
  {
    "id": "rest",
    "when": {
      "judgement": "intent",
      "equals": "rest"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "The company stops. There is a fire, and not much food."
    ]
  },
  {
    "id": "reach_mountain",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "walk"
        },
        {
          "state": "thorin_regard",
          "atLeast": 2
        },
        {
          "judgement": "progress",
          "atLeast": 0.7
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "at_mountain",
        "to": "true"
      }
    ],
    "narrate": [
      "You find the road that the dwarves had missed, and it goes straight toward the mountain."
    ]
  },
  {
    "id": "provisions_drop",
    "then": [
      {
        "kind": "increment",
        "state": "provisions",
        "by": -1
      }
    ],
    "narrate": [
      "A day's food is gone."
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
  "won": "The company reaches the mountain, and you led part of the way.",
  "lost": "The road east wins."
}
```
