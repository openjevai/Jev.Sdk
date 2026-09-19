---
id: eighty-days-diplomacy-peril
title: 80 Days — The Passenger — Peril
schemaVersion: "1.0"
turnLimit: 40
goal: The same journey, but you are not the one paying: a diplomat's dispatch is in your case, and it has to reach London. The route is now a question of who you travel with.
---

# 80 Days — The Passenger — Peril

The same journey, but you are not the one paying: a diplomat's dispatch is in your case, and it has to reach London. The route is now a question of who you travel with.

## About this variant

**Peril style.** Adds danger and a countdown: an attempt can wound you, and time is always running out.



The same wager, and the whole game is somewhere else: on who is aboard. The route

judgements are gone and the SOCIAL ones carry it, because what moves you forward is

what people are willing to tell you. This is the scenario axis doing the real work.

## Examples

- ask the other passengers about the route
- keep to yourself
- share a table
- check the dispatch
- change ships

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
      "*": "The clock runs on, whatever you do about it."
    }
  },
  {
    "key": "dispatch_held",
    "type": "int",
    "initial": 1,
    "describe": {
      "0": "The dispatch is gone.",
      "1": "The dispatch is in your case.",
      "*": "The dispatch is secure."
    }
  },
  {
    "key": "confidence",
    "type": "int",
    "initial": 1,
    "describe": {
      "0": "Everyone aboard is watching you and saying nothing.",
      "1": "You are tolerated at the table.",
      "2": "People have begun to talk to you.",
      "*": "You have friends on this ship."
    }
  },
  {
    "key": "stage",
    "type": "text",
    "initial": "atlantic",
    "describe": {
      "atlantic": "You are on the first crossing, out of Liverpool.",
      "mediterranean": "You are on the Mediterranean run.",
      "indian": "You are somewhere in the Indian Ocean.",
      "pacific": "You are on the long Pacific leg.",
      "london": "You are in London.",
      "*": "You are at sea."
    }
  },
  {
    "key": "hurt",
    "type": "bool",
    "initial": false,
    "describe": {
      "true": "You are hurt, and it is slowing you down.",
      "false": "You are unhurt."
    }
  },
  {
    "key": "time_left",
    "type": "int",
    "initial": 10,
    "describe": {
      "10": "You have time in hand, for now.",
      "9": "You have time in hand, for now.",
      "6": "Time is beginning to press.",
      "4": "Time is short now.",
      "2": "Very little time remains.",
      "1": "You are nearly out of time.",
      "0": "Your time is up.",
      "*": "Time is running."
    }
  }
]
```

## State template

Assembled from the `describe` entries above, and sent with every call.

```text
You are carrying a diplomatic dispatch and must reach London. {{stage}} {{days_left}} {{dispatch_held}} {{confidence}} The people aboard know things you do not. {{hurt}} {{time_left}}
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
        "key": "socialise",
        "description": "Socialise, share a table, or start a conversation."
      },
      {
        "key": "ask",
        "description": "Ask someone about the route, the next leg, or a problem."
      },
      {
        "key": "avoid",
        "description": "Keep to yourself, stay below, or say nothing."
      },
      {
        "key": "check",
        "description": "Check on the dispatch, or secure your cabin."
      },
      {
        "key": "travel",
        "description": "Change ships, take a leg onward, or travel."
      },
      {
        "key": "other",
        "description": "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do to the passengers."
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
    "key": "danger",
    "kind": "noul",
    "fallback": "0.5",
    "question": "The player typed: \"{{action}}\". How likely is that to hurt them - a wound, a fall, a burn, a shock? Answer with the probability of injury. Handling something sharp, climbing something wet, or forcing something that resists is dangerous. Looking, listening, or resting is not."
  }
]
```

## Rules

Evaluated in order; the first whose condition holds is applied. Order is part of the design.

```json
[
  {
    "id": "time_runs_out",
    "when": {
      "state": "time_left",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "Your time here runs out."
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
    "id": "injury",
    "when": {
      "all": [
        {
          "state": "hurt",
          "equals": "false"
        },
        {
          "roll": {
            "chanceFrom": "danger",
            "atLeast": 0.6
          }
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "hurt",
        "to": "true"
      },
      {
        "kind": "increment",
        "state": "time_left",
        "by": -1
      }
    ],
    "narrate": [
      "Something tears. You are bleeding and slower now."
    ]
  },
  {
    "id": "lost_dispatch",
    "when": {
      "state": "dispatch_held",
      "below": 1
    },
    "then": [
      {
        "kind": "lose"
      }
    ],
    "narrate": [
      "The case is open and the dispatch is not in it.",
      "There is no wager left to win, only a report to make."
    ]
  },
  {
    "id": "delivered",
    "when": {
      "all": [
        {
          "state": "stage",
          "equals": "london"
        },
        {
          "state": "dispatch_held",
          "atLeast": 1
        }
      ]
    },
    "then": [
      {
        "kind": "win"
      }
    ],
    "narrate": [
      "You put the dispatch into the right hands, still sealed.",
      "The journey is over, and so is the hurry."
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
      "Eighty days gone. The dispatch arrives later than it should have."
    ]
  },
  {
    "id": "socialise_success",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "socialise"
        },
        {
          "judgement": "progress",
          "atLeast": 0.55
        },
        {
          "state": "confidence",
          "below": 3
        }
      ]
    },
    "then": [
      {
        "kind": "increment",
        "state": "confidence",
        "by": 1
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -3
      }
    ],
    "narrate": [
      "You take a seat at a crowded table and are not asked to move."
    ]
  },
  {
    "id": "socialise_flat",
    "when": {
      "judgement": "intent",
      "equals": "socialise"
    },
    "then": [
      {
        "kind": "increment",
        "state": "days_left",
        "by": -3
      }
    ],
    "narrate": [
      "You sit with people who do not want company. Three days pass."
    ]
  },
  {
    "id": "ask_helps",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "ask"
        },
        {
          "state": "confidence",
          "atLeast": 2
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
        "state": "days_left",
        "by": -2
      }
    ],
    "narrate": [
      "Someone mentions a ship leaving sooner than the one you had planned to take."
    ]
  },
  {
    "id": "ask_flat",
    "when": {
      "judgement": "intent",
      "equals": "ask"
    },
    "then": [
      {
        "kind": "increment",
        "state": "days_left",
        "by": -2
      }
    ],
    "narrate": [
      "\"I could not say,\" you are told, and that is the end of it."
    ]
  },
  {
    "id": "avoid",
    "when": {
      "judgement": "intent",
      "equals": "avoid"
    },
    "then": [
      {
        "kind": "increment",
        "state": "days_left",
        "by": -2
      }
    ],
    "narrate": [
      "You keep to your cabin. Nobody knocks."
    ]
  },
  {
    "id": "check_ok",
    "when": {
      "judgement": "intent",
      "equals": "check"
    },
    "then": [
      {
        "kind": "narrate",
        "text": "nothing"
      }
    ],
    "narrate": [
      "The dispatch is where you left it, still sealed."
    ]
  },
  {
    "id": "theft",
    "when": {
      "all": [
        {
          "state": "confidence",
          "below": 1
        },
        {
          "state": "dispatch_held",
          "atLeast": 1
        },
        {
          "roll": {
            "chanceFrom": "progress"
          }
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "dispatch_held",
        "to": "0"
      }
    ],
    "narrate": [
      "Someone has been in the case.",
      "The dispatch is gone."
    ]
  },
  {
    "id": "travel_med",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "stage",
          "equals": "atlantic"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "stage",
        "to": "mediterranean"
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -5
      }
    ],
    "narrate": [
      "You transfer at Lisbon to a faster steamer."
    ]
  },
  {
    "id": "travel_indian",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "stage",
          "equals": "mediterranean"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "stage",
        "to": "indian"
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -9
      }
    ],
    "narrate": [
      "Through the canal and out into the long warm ocean."
    ]
  },
  {
    "id": "travel_pacific",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "stage",
          "equals": "indian"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "stage",
        "to": "pacific"
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -12
      }
    ],
    "narrate": [
      "Singapore, Hong Kong, and then nothing but water in every direction."
    ]
  },
  {
    "id": "travel_london",
    "when": {
      "all": [
        {
          "judgement": "intent",
          "equals": "travel"
        },
        {
          "state": "stage",
          "equals": "pacific"
        }
      ]
    },
    "then": [
      {
        "kind": "set",
        "state": "stage",
        "to": "london"
      },
      {
        "kind": "increment",
        "state": "days_left",
        "by": -14
      }
    ],
    "narrate": [
      "The last crossing. You spend most of it watching the horizon."
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
    "id": "time_passes",
    "then": [
      {
        "kind": "increment",
        "state": "time_left",
        "by": -1
      }
    ],
    "narrate": [
      "Time passes."
    ]
  }
]
```

## Ending

```json
{
  "won": "The dispatch is delivered.",
  "lost": "The dispatch never gets there."
}
```
