---
id: eighty-days-route-social
title: 80 Days — The Route — Social
schemaVersion: "1.0"
turnLimit: 40
goal: Eighty days, and a wager. You are in London with a valet, a case, and a decision about which way round the world to go. Get back to London inside the eighty days.
---

# 80 Days — The Route — Social

Eighty days, and a wager. You are in London with a valet, a case, and a decision about which way round the world to go. Get back to London inside the eighty days.

## About this variant

**Social style.** Adds another presence with a mood and a store of goodwill, so talking is a real route.



The wager, which is the whole clock. Money and days are both counters and they pull in

opposite directions: the fast route costs, the cheap route runs long. There is no

puzzle to solve, only a route and its price.

## Examples

- book passage to Paris
- ask Passepartout what he thinks
- count the money
- take the fast route
- wait for a better ship

## State

The facts this world keeps, and how each value reads to the model. The state is sent as
prose, because a question like "is this plausible" cannot be answered from a row of flags.

```json
[
  {
    "key": "days_left",
    "type": "int",
    "initial": 80,
    "describe": {
      "*": "The clock is running, and you can hear it."
    }
  },
  {
    "key": "money",
    "type": "int",
    "initial": 6,
    "describe": {
      "0": "You have nothing left to travel on.",
      "1": "You have barely enough for a meal, let alone a passage.",
      "*": "You have funds, for now."
    }
  },
  {
    "key": "place",
    "type": "text",
    "initial": "london",
    "describe": {
      "london": "You are in London, where the wager began.",
      "paris": "You are in Paris, and the continent is open ahead of you.",
      "suez": "You are at Suez, where the steamers stop.",
      "bombay": "You are in Bombay, three weeks out.",
      "yokohama": "You are in Yokohama, two thirds of the way round.",
      "newyork": "You are in New York, almost home.",
      "*": "You are somewhere on the route."
    }
  },
  {
    "key": "home",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are back in London.",
      "false": "You are still travelling."
    }
  },
  {
    "key": "valet_regard",
    "type": "int",
    "initial": 2,
    "describe": {
      "0": "Your valet is doing his job and nothing more.",
      "1": "Your valet is uneasy about the pace.",
      "2": "Your valet is with you.",
      "*": "Your valet would follow you anywhere."
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
You are travelling around the world to win a wager. {{place}} {{days_left}} {{money}} {{home}} {{valet_regard}} Every passage costs money and every wait costs days. {{goodwill}}
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
        "key": "travel",
        "description": "Travel onward, book passage, or take transport."
      },
      {
        "key": "wait",
        "description": "Wait, stay, or take a slower cheaper option."
      },
      {
        "key": "earn",
        "description": "Earn money, sell something, or do a piece of work."
      },
      {
        "key": "talk_valet",
        "description": "Talk to your valet, ask his opinion, or reassure him."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the route."
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
    "id": "out_of_time",
    "when": {
      "state": "days_left",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The eighty days are up.",
      "The wager is lost, and the world is very large."
    ]
  },
  {
    "id": "out_of_money",
    "when": {
      "state": "money",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "You cannot buy passage and you cannot walk the rest of the way."
    ]
  },
  {
    "id": "arrived_home",
    "when": {
      "all": [
        {
          "state": "place",
          "equals": "newyork"
        },
        {
          "state": "home",
          "equals": "false"
        },
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "judgement": "progress",
          "atLeast": 0.5
        }
      ]
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "The last crossing takes eleven days and you spend all of them on deck.",
      "Then the Thames, and London, and the Reform Club.",
      "You are back, and the clock is still running."
    ]
  },
  {
    "id": "travel_paris",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "place",
          "equals": "london"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "place",
        "to": "paris"
      },
      {
        "kind": "increment",
        "state": "money",
        "by": -1
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -2
      }
    ],
    "narrate": [
      "A boat and a train, and two days."
    ]
  },
  {
    "id": "travel_suez",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "place",
          "equals": "paris"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "place",
        "to": "suez"
      },
      {
        "kind": "increment",
        "state": "money",
        "by": -1
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -7
      }
    ],
    "narrate": [
      "Paris to a packet boat, and the long warm run to Suez."
    ]
  },
  {
    "id": "travel_bombay",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "place",
          "equals": "suez"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "place",
        "to": "bombay"
      },
      {
        "kind": "increment",
        "state": "money",
        "by": -2
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -8
      }
    ],
    "narrate": [
      "The steamer through the canal and down the Red Sea to Bombay."
    ]
  },
  {
    "id": "travel_yokohama",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "place",
          "equals": "bombay"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "place",
        "to": "yokohama"
      },
      {
        "kind": "increment",
        "state": "money",
        "by": -2
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -12
      }
    ],
    "narrate": [
      "Across India, then a ship, then Japan."
    ]
  },
  {
    "id": "travel_newyork",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "place",
          "equals": "yokohama"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "place",
        "to": "newyork"
      },
      {
        "kind": "increment",
        "state": "money",
        "by": -2
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -13
      }
    ],
    "narrate": [
      "The Pacific, at last, and then the whole width of America by rail."
    ]
  },
  {
    "id": "travel_from_home",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "place",
          "equals": "newyork"
        },
        {
          "state": "home",
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
      "You are home already."
    ]
  },
  {
    "id": "wait",
    "when": {
      "judgement": "intent",
      "equals": "wait"
    },
    "then": [
      {
        "kind": "increment",
        "state": "days_left",
        "by": -3
      }
    ],
    "narrate": [
      "You wait for the cheaper boat. It is cheaper, and it is three days late."
    ]
  },
  {
    "id": "earn",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "earn"
        },
        {
          "judgement": "progress",
          "atLeast": 0.5
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "money",
        "by": 1
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -2
      }
    ],
    "narrate": [
      "You find a way to be useful to someone with money, and are paid."
    ]
  },
  {
    "id": "earn_fails",
    "when": {
      "judgement": "intent",
      "equals": "earn"
    },
    "then": [
      {
        "kind": "increment",
        "state": "days_left",
        "by": -2
      }
    ],
    "narrate": [
      "Two days of asking, and nothing to show for it."
    ]
  },
  {
    "id": "talk_valet",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "talk_valet"
        },
        {
          "state": "valet_regard",
          "below": 3
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "valet_regard",
        "by": 1
      }
    ],
    "narrate": [
      "Passepartout listens, and says something unexpectedly sensible."
    ]
  },
  {
    "id": "valet_grumble",
    "when": {
      "all": [
        {
          "state": "valet_regard",
          "atLeast": 1
        },
        {
          "state": "days_left",
          "below": 30
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "valet_regard",
        "by": -1
      }
    ],
    "narrate": [
      "Your valet mentions, carefully, that there might have been another way."
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
  "won": "You won the wager.",
  "lost": "The wager is lost, and the world does not care."
}
```
