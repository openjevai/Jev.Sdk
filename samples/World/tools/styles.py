"""styles.py — the five judgement styles every classic is rendered in.

The point of the exercise is variation, and variation that is real rather than cosmetic: a "style"
changes what the world ASKS the model and what its rules DO with the answers. Five styles:

  classic   intent, plausibility, progress. The original shape. Clean and readable.
  peril     adds danger and injury on a roll, plus a countdown. Failures hurt.
  social    adds an NPC's mood (a rating) and their trust (a counter). Talking is a real route.
  resource  adds a supply counter that actions consume. Waste ends a run.
  mythic    adds fate as a probability that overrides plausibility, and a doom clock. Strange
            attempts are allowed and rewarded, and the world gets worse as it goes.

Each style returns extra state, extra judgements, and a frame of rules that wraps the scenario's own.
Composing them this way is what keeps 25 distinct worlds affordable to author: the scenario supplies
the place, the style supplies the pressure.
"""

from __future__ import annotations

from typing import Any

from world_gen import (  # noqa: F401
    all_, any_, bool_field, choice, inc, int_field, j, lose, not_, noul, roll, rule, s,
    say, score, set_, win,
)

# --------------------------------------------------------------------------- shared question wordings

# These wordings are copied verbatim from the escape-room world, which was measured against the live
# model. They are not decorative: an earlier plausibility question was read as a precondition check
# and scored walking across the room at 0.55, below the threshold, making a world unplayable while
# every rule test passed. Reusing wording that was measured is the point of putting it here.

PLAUSIBLE = (
    "The player typed: \"{{action}}\". Could a person in this situation physically carry that out? "
    "Judge only whether they could do it, not whether it will succeed and not whether it is wise. "
    "Reaching for something within reach is possible, including walking to it first. Doing something "
    "no person could do, or acting on something that is not here at all, is not."
)

PROGRESS = (
    "The player typed: \"{{action}}\". If they attempt that, how likely is it to actually work - to "
    "move them toward the goal rather than being a distraction or a wasted move? Answer with the "
    "probability that the attempt succeeds."
)

DANGER = (
    "The player typed: \"{{action}}\". How likely is that to hurt them - a wound, a fall, a burn, a "
    "shock? Answer with the probability of injury. Handling something sharp, climbing something wet, "
    "or forcing something that resists is dangerous. Looking, listening, or resting is not."
)

SUPPLY_COST = (
    "The player typed: \"{{action}}\". How much of their limited supplies would that consume - a "
    "light, a ration, a charge, a tool worn down? Answer with the fraction of a unit used. Simply "
    "looking at or reasoning about something uses none."
)

FATE = (
    "The player typed: \"{{action}}\". Setting aside whether it is possible: is this the sort of "
    "strange, bold, or uncanny move that the story rewards? Answer with the probability that fate "
    "favours it. Ordinary actions are unremarkable either way; attempts to bargain, defy, or trick "
    "something are where this matters."
)

MOOD = (
    "The player typed: \"{{action}}\". How does the other presence here take that - hostile at the "
    "low end, indifferent in the middle, warm and helpful at the top?"
)

TRUST = (
    "The player typed: \"{{action}}\". Would that make the other presence here more willing to help "
    "them, or less? Answer with the probability that it improves their goodwill rather than damaging it."
)


def other_option(what: str) -> dict[str, str]:
    return {
        "other": f"Anything else: statements, questions, observations, thinking aloud, or actions that make "
                 f"no sense here. Prefer this whenever the text does not describe something the player is "
                 f"actively trying to do to {what}.",
    }


def intent_question(subject: str) -> str:
    return (
        "The player typed: \"{{action}}\". Which single action are they attempting? Choose 'other' unless "
        "the text describes an action they are actually trying to carry out - simply mentioning something "
        "is not an attempt to do something with it."
    )


# --------------------------------------------------------------------------- the styles

def classic() -> dict[str, Any]:
    """The original shape: is it possible, and would it work."""
    return {
        "id": "classic",
        "label": "Classic",
        "blurb": "Three judgements: what they are trying to do, whether it is possible, whether it works.",
        "state": [],
        "judgements": [noul("plausible", PLAUSIBLE), noul("progress", PROGRESS)],
        "frame_before": [],
        "frame_after": [
            rule("implausible", say("nothing"), when=j("plausible", below=0.5),
                 narrate=["That is not something you can do here."]),
        ],
    }


def peril() -> dict[str, Any]:
    """Failure hurts, and a clock runs."""
    return {
        "id": "peril",
        "label": "Peril",
        "blurb": "Adds danger and a countdown: an attempt can wound you, and time is always running out.",
        "state": [
            bool_field("hurt", False, "You are hurt, and it is slowing you down.", "You are unhurt."),
            int_field("time_left", 10, {
                "10": "You have time in hand, for now.",
                "9": "You have time in hand, for now.",
                "6": "Time is beginning to press.",
                "4": "Time is short now.",
                "2": "Very little time remains.",
                "1": "You are nearly out of time.",
                "0": "Your time is up.",
                "*": "Time is running.",
            }),
        ],
        "judgements": [noul("plausible", PLAUSIBLE), noul("progress", PROGRESS), noul("danger", DANGER)],
        "frame_before": [
            rule("time_runs_out", lose(), when=s("time_left", below=1),
                 narrate=["Your time here runs out."]),
            rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                 narrate=["That is not something you can do here."]),
            # Injury rolls only while unhurt, so a run cannot be ended by a single unlucky attempt.
            rule("injury", set_("hurt", True), inc("time_left", -1),
                 when=all_(s("hurt", equals="false"), roll("danger", at_least=0.6)),
                 narrate=["Something tears. You are bleeding and slower now."]),
        ],
        "frame_after": [
            rule("time_passes", inc("time_left", -1), narrate=["Time passes."]),
        ],
    }


def social() -> dict[str, Any]:
    """Someone else is here, and they can be won over."""
    return {
        "id": "social",
        "label": "Social",
        "blurb": "Adds another presence with a mood and a store of goodwill, so talking is a real route.",
        "state": [
            int_field("goodwill", 1, {
                "0": "They want nothing to do with you.", "1": "They are watchful and uncommitted.",
                "2": "They are willing to hear you out.", "3": "They are on your side.",
                "*": "Their goodwill is hard to read.",
            }),
        ],
        "judgements": [
            noul("plausible", PLAUSIBLE),
            noul("progress", PROGRESS),
            score("mood", MOOD, ["hostile", "wary", "civil", "warm"], "wary"),
            noul("improves_goodwill", TRUST),
        ],
        "frame_before": [
            rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                 narrate=["That is not something you can do here."]),
        ],
        "frame_after": [
            rule("goodwill_up", inc("goodwill", 1),
                 when=all_(j("improves_goodwill", at_least=0.6), s("goodwill", below=3)),
                 narrate=["They seem a little more inclined to help."]),
            rule("goodwill_down", inc("goodwill", -1),
                 when=all_(j("improves_goodwill", below=0.3), s("goodwill", at_least=1)),
                 narrate=["You have made this harder."]),
        ],
    }


def resource() -> dict[str, Any]:
    """Everything costs, and running out ends the run."""
    return {
        "id": "resource",
        "label": "Resource",
        "blurb": "Adds a supply counter that actions consume. Waste, and you run dry.",
        "state": [
            int_field("supply", 6, {
                "0": "You are out of supplies entirely.",
                "1": "A single charge or ration remains.",
                "2": "You have a little left.",
                "*": "You have enough, for now.",
            }),
        ],
        "judgements": [
            noul("plausible", PLAUSIBLE),
            noul("progress", PROGRESS),
            noul("supply_cost", SUPPLY_COST),
        ],
        "frame_before": [
            rule("out_of_supply", lose(), when=s("supply", below=1),
                 narrate=["You have nothing left to work with."]),
            rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                 narrate=["That is not something you can do here."]),
        ],
        "frame_after": [
            # Anything that costs more than it returns drains the counter by one. A world does not need
            # fractional supply to make the judgement matter; it needs the judgement to gate a real loss.
            rule("consumed", inc("supply", -1),
                 when=all_(j("supply_cost", at_least=0.5), s("supply", at_least=1)),
                 narrate=["That used something up."]),
        ],
    }


def mythic() -> dict[str, Any]:
    """Fate rewards the strange, and the world darkens as it goes."""
    return {
        "id": "mythic",
        "label": "Mythic",
        "blurb": "Adds fate - which can make the impossible work - and a doom clock that advances every turn.",
        "state": [
            int_field("doom", 0, {"*": "Something is drawing closer. You can feel it."}),
        ],
        "judgements": [
            noul("plausible", PLAUSIBLE),
            noul("progress", PROGRESS),
            noul("fate", FATE),
        ],
        "frame_before": [
            rule("doom_comes", lose(), when=s("doom", at_least=6),
                 narrate=["Whatever was coming arrives.", "There is nothing more to try."]),
            # Fate is checked BEFORE plausibility, which is the style's whole character: an impossible
            # attempt can succeed if fate favours it. Order is the mechanism.
            rule("fate_intervenes", win(), when=all_(j("fate", at_least=0.9), roll("fate")),
                 narrate=["The rules of this place bend, just once, and let you through."]),
            rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                 narrate=["That is not something you can do here."]),
        ],
        "frame_after": [
            rule("doom_advances", inc("doom", 1), narrate=["Something draws a little closer."]),
        ],
    }


STYLES = [classic, peril, social, resource, mythic]
"""Every style, in the order they are rendered. Five per classic, twenty-five in total."""
