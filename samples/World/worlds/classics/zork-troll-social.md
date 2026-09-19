---
id: zork-troll-social
title: Zork — The Troll and the Torch — Social
schemaVersion: "1.0"
turnLimit: 20
goal: A troll holds the bridge. It wants something, and you are carrying a torch that is burning down. Get across, one way or another.
---

# Zork — The Troll and the Torch — Social

A troll holds the bridge. It wants something, and you are carrying a torch that is burning down. Get across, one way or another.

## About this variant

**Social style.** Adds another presence with a mood and a store of goodwill, so talking is a real route.



Zork's troll encounter, rebuilt as a scenario with three real routes across: fight, buy,

or talk. Each route costs something different, and the torch is burning the whole time,

so a player who freezes is also deciding.

## Examples

- wave the torch at the troll
- throw the torch at it
- offer it the torch
- talk to it
- run past

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "torch_lit",
    "type": "bool",
    "initial": true,
    "describe": {
      "true": "Your torch is burning.",
      "false": "Your torch is out."
    }
  },
  {
    "key": "torch_life",
    "type": "int",
    "initial": 6,
    "describe": {
      "0": "The torch is out.",
      "1": "The torch is nearly gone.",
      "*": "The torch still burns."
    }
  },
  {
    "key": "troll_patience",
    "type": "int",
    "initial": 2,
    "describe": {
      "0": "The troll has lost interest in anything but violence.",
      "1": "The troll is close to the end of its patience.",
      "*": "The troll is watching, not yet committed."
    }
  },
  {
    "key": "crossed",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are across the bridge.",
      "false": "The bridge is still blocked."
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
A rope bridge crosses a chasm. A troll stands in the middle of it. {{torch_lit}} {{torch_life}} {{troll_patience}} {{crossed}} The troll has not decided about you yet. {{goodwill}}
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
        "key": "threaten",
        "description": "Threaten, brandish, or drive the troll back."
      },
      {
        "key": "bribe",
        "description": "Offer, give, or trade something to the troll."
      },
      {
        "key": "talk",
        "description": "Speak to the troll, reason, or negotiate."
      },
      {
        "key": "cross",
        "description": "Try to get across the bridge."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the troll."
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
    "id": "crossed_win",
    "when": {
      "state": "crossed",
      "equals": "true"
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "You are across. The troll does not follow."
    ]
  },
  {
    "id": "troll_loses_patience",
    "when": {
      "state": "troll_patience",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The troll stops considering and simply moves.",
      "It is faster than something that size should be."
    ]
  },
  {
    "id": "threaten_works",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "threaten"
        },
        {
          "state": "torch_lit",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "troll_patience",
        "by": -1
      }
    ],
    "narrate": [
      "You thrust the torch forward. The troll leans back from the flame."
    ]
  },
  {
    "id": "threaten_without_torch",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "threaten"
        },
        {
          "state": "torch_lit",
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
      "You have nothing to threaten it with. It does not move."
    ]
  },
  {
    "id": "bribe_torch",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "bribe"
        },
        {
          "state": "torch_lit",
          "equals": "true"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "torch_lit",
        "to": "false"
      },
      {
        "kind": "set",
        "state": "crossed",
        "to": "true"
      }
    ],
    "narrate": [
      "You hand over the torch. The troll takes it, stares into the flame, and steps aside."
    ]
  },
  {
    "id": "bribe_nothing",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "bribe"
        },
        {
          "state": "torch_lit",
          "equals": "false"
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "troll_patience",
        "by": -1
      }
    ],
    "narrate": [
      "You have nothing to offer. The troll's patience thins."
    ]
  },
  {
    "id": "talk_works",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "talk"
        },
        {
          "state": "troll_patience",
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
        "state": "crossed",
        "to": "true"
      }
    ],
    "narrate": [
      "You ask it, plainly, to let you pass. It considers you for a long moment.",
      "Then it steps to one side."
    ]
  },
  {
    "id": "talk_fails",
    "when": {
      "judgement": "intent",
      "equals": "talk"
    },
    "then": [
      {
        "kind": "increment",
        "state": "troll_patience",
        "by": -1
      }
    ],
    "narrate": [
      "You talk. The troll's expression does not change."
    ]
  },
  {
    "id": "cross_works",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "cross"
        },
        {
          "judgement": "progress",
          "atLeast": 0.75
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "crossed",
        "to": "true"
      }
    ],
    "narrate": [
      "You wait for the troll to look away, then go. The bridge sways, and holds."
    ]
  },
  {
    "id": "cross_fails",
    "when": {
      "judgement": "intent",
      "equals": "cross"
    },
    "then": [
      {
        "kind": "increment",
        "state": "troll_patience",
        "by": -1
      }
    ],
    "narrate": [
      "You make it two paces before the troll turns."
    ]
  },
  {
    "id": "torch_burns",
    "when": {
      "state": "torch_lit",
      "equals": "true"
    },
    "then": [
      {
        "kind": "increment",
        "state": "torch_life",
        "by": -1
      }
    ],
    "narrate": [
      "The torch burns lower."
    ]
  },
  {
    "id": "torch_out",
    "when": {
      "state": "torch_life",
      "below": 1
    },
    "then": [
      {
        "kind": "set",
        "state": "torch_lit",
        "to": "false"
      }
    ],
    "narrate": [
      "The torch gutters and goes out."
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
  "won": "You are across the bridge.",
  "lost": "The troll does not let you past."
}
```
