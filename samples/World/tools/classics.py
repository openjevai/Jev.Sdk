"""classics.py — the five console choose-your-path classics, and their scenarios.

Selection, stated plainly because "top 5 of all time" has no authoritative ranking. These are chosen
on documented influence, and on being genuinely CHOOSE-YOUR-PATH — a branching narrative driven by
typed decisions. That excludes some famous console adventures: Zork is a parser world with a fixed
map rather than a branching narrative, and Rogue is procedural. Both are in the lineage but neither is
the shape being rendered here.

  1. Colossal Cave Adventure (1976, Crowther & Woods) — the first interactive fiction, and the origin
     of the whole form. Its cave, its lamp, and its "xyzzy" are the genre's founding vocabulary.
  2. Zork (1977-79, Infocom) — the most commercially and culturally influential text adventure. The
     Great Underground Empire, the white house, the thief.
  3. The Hobbit (1982, Beam Software) — the most influential text adventure on home computers, and the
     first with a genuinely independent NPC whose behaviour the player could not fully control.
  4. Planetfall (1983, Infocom) — the canonical example of a text adventure doing comedy and character
     rather than only puzzles; Floyd is the most-remembered NPC in the form.
  5. 80 Days (2014, inkle) — the modern high-water mark of branching narrative, and the clearest proof
     that the form's real subject is decisions and their cost rather than puzzle-solving.

Each classic is a SET of scenarios, because the second axis of variation is what the world is about.
"""

from __future__ import annotations

from typing import Any

from styles import PROGRESS, intent_question, other_option
from world_gen import (
    World, all_, any_, bool_field, choice, inc, int_field, j, lose, noul, roll, rule, s, say, set_,
    text_field, win,
)

# --------------------------------------------------------------------------- 1. Colossal Cave

def colossal_cave() -> list[World]:
    return [
        World(
            id="colossal-cave-classic",
            title="Colossal Cave — The Classic Cut",
            goal=(
                "You are at the mouth of a cave that swallows daylight. Somewhere inside is the treasure "
                "of the cave, and your lamp is the only thing standing between you and the dark. Get in, "
                "get the treasure, get out."
            ),
            examples=["light the lamp", "go down into the cave", "take the treasure", "go back up", "listen"],
            notes=[
                "The founding text adventure, reduced to its spine: a lamp, a cave, and a treasure.",
                "The lamp is the whole design. Its fuel is a countdown, and the dark is a lose condition,",
                "so the player is always deciding how far in they dare go.",
            ],
            state=[
                bool_field("lamp_lit", False, "Your lamp is lit and throwing a warm circle.", "Your lamp is out and unlit."),
                int_field("lamp_life", 8, {
                    "0": "The lamp is dead.",
                    "1": "The lamp is guttering badly.",
                    "2": "The lamp is dimming.",
                    "*": "The lamp burns steadily.",
                }),
                text_field("where", "surface", {
                    "surface": "You are standing on the surface, in daylight.",
                    "cavern": "You are underground, in a narrow passage of cold stone.",
                    "treasury": "You are in a small chamber glittering with treasure.",
                    "*": "You are somewhere in the cave.",
                }),
                bool_field("has_treasure", False, "You are carrying the treasure.", "You are carrying nothing of value."),
            ],
            template=(
                "You are at Colossal Cave. {{where}} {{lamp_lit}} {{lamp_life}} {{has_treasure}} "
                "The cave runs deep and does not care whether you come back."
            ),
            judgements=[
                choice("intent", intent_question("the cave or the objects in it"), {
                    "light_lamp": "Light, refuel, or tend the lamp.",
                    "descend": "Go deeper into the cave.",
                    "ascend": "Climb back toward the surface.",
                    "take_treasure": "Pick up treasure and carry it.",
                    "listen": "Stop and listen to the cave.",
                    **other_option("the cave"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here, given where they are and "
                    "what they are carrying? Judge only physical possibility, not whether it is wise. Moving "
                    "to a place they can reach is possible, including walking there first."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("no_light_deep", lose(),
                     when=all_(s("where", equals="cavern"), s("lamp_lit", equals="false")),
                     narrate=["The passage behind you closes into blackness.", "You never find your way out."]),
                rule("lamp_dies_deep", lose(),
                     when=all_(s("where", equals="cavern"), s("lamp_life", below=1)),
                     narrate=["The lamp dies with a thin sound. The dark is absolute and immediate."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("already_lit", say("nothing"),
                     when=all_(j("intent", equals="light_lamp"), s("lamp_lit", equals="true")),
                     narrate=["The lamp is already burning."]),
                rule("no_lamp_to_light", say("nothing"),
                     when=all_(j("intent", equals="light_lamp"), s("lamp_life", below=1)),
                     narrate=["There is nothing left in the lamp to light."]),
                rule("light_lamp", set_("lamp_lit", True),
                     when=j("intent", equals="light_lamp"),
                     narrate=["You strike a match and the wick catches. The cave walls jump out of the dark."]),
                rule("listen", say("nothing"), when=j("intent", equals="listen"),
                     narrate=["Water, far below. And something that is not water."]),
                rule("descend_from_surface", set_("where", "cavern"),
                     when=all_(j("intent", equals="descend"), s("where", equals="surface")),
                     narrate=["You climb down past the daylight and into the passage."]),
                rule("descend_to_treasury", set_("where", "treasury"),
                     when=all_(j("intent", equals="descend"), s("where", equals="cavern")),
                     narrate=["The passage opens into a chamber. Light catches on gold."]),
                rule("descend_deeper", say("nothing"),
                     when=all_(j("intent", equals="descend"), s("where", equals="treasury")),
                     narrate=["The far wall is solid rock. There is no deeper."]),
                rule("ascend_to_cavern", set_("where", "cavern"),
                     when=all_(j("intent", equals="ascend"), s("where", equals="treasury")),
                     narrate=["You climb back up into the passage."]),
                rule("ascend_out", win(),
                     when=all_(j("intent", equals="ascend"), s("where", equals="cavern"),
                               s("has_treasure", equals="true")),
                     narrate=["Daylight. You step out with the treasure in your arms.",
                              "The cave keeps the rest, and does not follow you."]),
                rule("ascend_empty", set_("where", "surface"),
                     when=all_(j("intent", equals="ascend"), s("where", equals="cavern")),
                     narrate=["You climb back out into the day. Your hands are empty."]),
                rule("take_it", set_("has_treasure", True),
                     when=all_(j("intent", equals="take_treasure"), s("where", equals="treasury"),
                               s("has_treasure", equals="false")),
                     narrate=["You lift the treasure. It is heavier than it looks."]),
                rule("nothing_here_to_take", say("nothing"),
                     when=j("intent", equals="take_treasure"),
                     narrate=["There is nothing to take here."]),
                rule("lamp_burns", inc("lamp_life", -1),
                     when=all_(s("lamp_lit", equals="true"), s("where", equals="cavern")),
                     narrate=["The lamp burns a little lower."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="You are out of the cave, and you are rich.",
            lost="The cave keeps what it takes.",
            turn_limit=30,
        ),
        World(
            id="colossal-cave-survey",
            title="Colossal Cave — The Survey",
            goal=(
                "You are not here for gold. You are here to map the cave. Three passages, and you need "
                "each one recorded before you leave."
            ),
            examples=["map the passage", "go deeper", "sketch the chamber", "return to the surface"],
            notes=[
                "The same cave, a different game: the goal is knowledge rather than loot, and the win",
                "condition counts mapped passages instead of a carried object. This is the 'different",
                "styles' axis applied to the SCENARIO rather than the judgement frame - the place is",
                "identical and the point of being there is not.",
            ],
            state=[
                bool_field("lamp_lit", False, "Your lamp is lit.", "Your lamp is out."),
                int_field("mapped", 0, {
                    "0": "You have mapped nothing yet.",
                    "1": "One passage is recorded.",
                    "2": "Two passages are recorded.",
                    "*": "Your survey is nearly complete.",
                }),
                text_field("where", "surface", {
                    "surface": "You are at the cave mouth.",
                    "cavern": "You are in the first passage.",
                    "treasury": "You are in a chamber beyond the passage.",
                    "*": "You are somewhere in the cave.",
                }),
            ],
            template=(
                "You are surveying Colossal Cave. {{where}} {{lamp_lit}} {{mapped}} "
                "You are mapping, not looting; you need every passage recorded."
            ),
            judgements=[
                choice("intent", intent_question("the cave"), {
                    "light_lamp": "Light the lamp.",
                    "survey": "Map, sketch, or record the place you are in.",
                    "descend": "Move deeper into the cave.",
                    "ascend": "Move back toward the surface.",
                    **other_option("the cave"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here? Judge only physical "
                    "possibility, not whether it is wise."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("light_lamp", set_("lamp_lit", True),
                     when=j("intent", equals="light_lamp"),
                     narrate=["The lamp catches."]),
                rule("survey_nothing_here", say("nothing"),
                     when=all_(j("intent", equals="survey"), s("where", equals="surface")),
                     narrate=["You are outdoors. There is nothing here to survey."]),
                rule("survey_cavern", set_("mapped", 1), set_("where", "cavern"),
                     when=all_(j("intent", equals="survey"), s("where", equals="cavern"),
                               s("mapped", below=2)),
                     narrate=["You pace the passage and mark its turns. One recorded."]),
                rule("survey_treasury", set_("mapped", 2),
                     when=all_(j("intent", equals="survey"), s("where", equals="treasury"),
                               s("mapped", below=2)),
                     narrate=["You sketch the chamber's width and depth. Two recorded."]),
                rule("survey_done", win(),
                     when=all_(j("intent", equals="survey"), s("mapped", at_least=2)),
                     narrate=["Every passage is on the page.", "You climb out with a full survey."]),
                rule("descend_to_cavern", set_("where", "cavern"),
                     when=all_(j("intent", equals="descend"), s("where", equals="surface")),
                     narrate=["You go in."]),
                rule("descend_to_treasury", set_("where", "treasury"),
                     when=all_(j("intent", equals="descend"), s("where", equals="cavern")),
                     narrate=["A chamber opens ahead."]),
                rule("ascend_to_cavern", set_("where", "cavern"),
                     when=all_(j("intent", equals="ascend"), s("where", equals="treasury")),
                     narrate=["You climb back to the passage."]),
                rule("ascend_out_empty", say("nothing"),
                     when=all_(j("intent", equals="ascend"), s("where", equals="cavern"),
                               s("mapped", below=2)),
                     narrate=["You come out, but the survey is unfinished. You will have to go back in."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="The survey is complete and legible.",
            lost="You never finish the map.",
            turn_limit=30,
        ),
    ]


# --------------------------------------------------------------------------- 2. Zork

def zork() -> list[World]:
    return [
        World(
            id="zork-house",
            title="Zork — The White House",
            goal=(
                "You are in front of a white house with a boarded front door. Below it lies the Great "
                "Underground Empire. Find the entrance, and get below."
            ),
            examples=["open the window", "go around the back", "enter the house", "go down", "read the note"],
            notes=[
                "Zork's opening, which is probably the most-read paragraph in interactive fiction.",
                "The scenario is 'find the way in', so the win condition is reaching the underground",
                "rather than escaping anything. The house is a shell with one hidden entrance.",
            ],
            state=[
                bool_field("window_open", False, "The window is open.", "The window is latched shut."),
                bool_field("note_read", False, "You have read the note on the door.", "There is a note on the door you have not read."),
                bool_field("entered", False, "You are inside the white house.", "You are still outside."),
                bool_field("below", False, "You are below the house, in the dark of the Empire.", "You are above ground."),
                int_field("moves", 0, {"*": "You have been at this a while."}),
            ],
            template=(
                "You stand before a white house with a boarded front door. {{window_open}} {{note_read}} "
                "{{entered}} {{below}} {{moves}} A leaflet is nailed to the door."
            ),
            judgements=[
                choice("intent", intent_question("the house and its fittings"), {
                    "open_window": "Unlatch, force, or break the window so it is no longer shut.",
                    "enter": "Get themselves inside the house - climbing through an open window, stepping through a doorway, or any other way in. Not the act of opening it.",
                    "read": "Read, look at, or examine something.",
                    "go_down": "Go down, descend a staircase, or climb down into the dark.",
                    **other_option("the house"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here? Judge only physical "
                    "possibility given the building in front of them, not whether it will work."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("read_note", set_("note_read", True),
                     when=j("intent", equals="read"),
                     narrate=["\"WELCOME TO ZORK. The white house is a good starting point.\""]),
                rule("open_window", set_("window_open", True),
                     when=all_(j("intent", equals="open_window"), s("window_open", equals="false")),
                     narrate=["The latch gives. The window swings in."]),
                rule("window_already", say("nothing"),
                     when=all_(j("intent", equals="open_window"), s("window_open", equals="true")),
                     narrate=["It is already open."]),
                rule("enter_without_window", say("nothing"),
                     when=all_(j("intent", equals="enter"), s("window_open", equals="false")),
                     narrate=["The front door is boarded and the window is latched. There is no way in."]),
                rule("enter", set_("entered", True),
                     when=all_(j("intent", equals="enter"), s("window_open", equals="true")),
                     narrate=["You climb through into a dim room. A staircase leads down."]),
                rule("go_down_in_house", set_("below", True), win(),
                     when=all_(j("intent", equals="go_down"), s("entered", equals="true")),
                     narrate=["You descend. The stairs go on far longer than the house is tall.",
                              "You are below, in the dark, and the Great Underground Empire is open around you."]),
                rule("go_down_outside", say("nothing"),
                     when=all_(j("intent", equals="go_down"), s("entered", equals="false")),
                     narrate=["There is open ground here. Nothing to descend."]),
                # 'arrived' used to live here as a separate rule with when=s("below"). It could never
                # fire on the turn that set the flag, because the rule above matched first - so a run
                # ending right after descending was scored as a loss. The win is merged upward.
                rule("time_passes", inc("moves", 1), narrate=["Time passes."]),
            ],
            won="You are standing in the dark at the top of the Empire.",
            lost="You never find your way in.",
            turn_limit=20,
        ),
        World(
            id="zork-troll",
            title="Zork — The Troll and the Torch",
            goal=(
                "A troll holds the bridge. It wants something, and you are carrying a torch that is "
                "burning down. Get across, one way or another."
            ),
            examples=["wave the torch at the troll", "throw the torch at it", "offer it the torch", "talk to it", "run past"],
            notes=[
                "Zork's troll encounter, rebuilt as a scenario with three real routes across: fight, buy,",
                "or talk. Each route costs something different, and the torch is burning the whole time,",
                "so a player who freezes is also deciding.",
            ],
            state=[
                bool_field("torch_lit", True, "Your torch is burning.", "Your torch is out."),
                int_field("torch_life", 6, {
                    "0": "The torch is out.", "1": "The torch is nearly gone.", "*": "The torch still burns.",
                }),
                int_field("troll_patience", 2, {
                    "0": "The troll has lost interest in anything but violence.",
                    "1": "The troll is close to the end of its patience.",
                    "*": "The troll is watching, not yet committed.",
                }),
                bool_field("crossed", False, "You are across the bridge.", "The bridge is still blocked."),
            ],
            template=(
                "A rope bridge crosses a chasm. A troll stands in the middle of it. {{torch_lit}} "
                "{{torch_life}} {{troll_patience}} {{crossed}} The troll has not decided about you yet."
            ),
            judgements=[
                choice("intent", intent_question("the troll or the bridge"), {
                    "threaten": "Threaten, brandish, or drive the troll back.",
                    "bribe": "Offer, give, or trade something to the troll.",
                    "talk": "Speak to the troll, reason, or negotiate.",
                    "cross": "Try to get across the bridge.",
                    **other_option("the troll"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here? Judge only physical "
                    "possibility, not whether it will work."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("crossed_win", win(), when=s("crossed", equals="true"),
                     narrate=["You are across. The troll does not follow."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("troll_loses_patience", lose(),
                     when=s("troll_patience", below=1),
                     narrate=["The troll stops considering and simply moves.",
                              "It is faster than something that size should be."]),
                rule("threaten_works", inc("troll_patience", -1),
                     when=all_(j("intent", equals="threaten"), s("torch_lit", equals="true")),
                     narrate=["You thrust the torch forward. The troll leans back from the flame."]),
                rule("threaten_without_torch", say("nothing"),
                     when=all_(j("intent", equals="threaten"), s("torch_lit", equals="false")),
                     narrate=["You have nothing to threaten it with. It does not move."]),
                rule("bribe_torch", set_("torch_lit", False), set_("crossed", True),
                     when=all_(j("intent", equals="bribe"), s("torch_lit", equals="true")),
                     narrate=["You hand over the torch. The troll takes it, stares into the flame, and steps aside."]),
                rule("bribe_nothing", inc("troll_patience", -1),
                     when=all_(j("intent", equals="bribe"), s("torch_lit", equals="false")),
                     narrate=["You have nothing to offer. The troll's patience thins."]),
                rule("talk_works", set_("crossed", True),
                     when=all_(j("intent", equals="talk"), s("troll_patience", at_least=2), j("progress", at_least=0.7)),
                     narrate=["You ask it, plainly, to let you pass. It considers you for a long moment.",
                              "Then it steps to one side."]),
                rule("talk_fails", inc("troll_patience", -1),
                     when=j("intent", equals="talk"),
                     narrate=["You talk. The troll's expression does not change."]),
                rule("cross_works", set_("crossed", True),
                     when=all_(j("intent", equals="cross"), j("progress", at_least=0.75)),
                     narrate=["You wait for the troll to look away, then go. The bridge sways, and holds."]),
                rule("cross_fails", inc("troll_patience", -1),
                     when=j("intent", equals="cross"),
                     narrate=["You make it two paces before the troll turns."]),
                rule("torch_burns", inc("torch_life", -1), when=s("torch_lit", equals="true"),
                     narrate=["The torch burns lower."]),
                rule("torch_out", set_("torch_lit", False), when=s("torch_life", below=1),
                     narrate=["The torch gutters and goes out."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="You are across the bridge.",
            lost="The troll does not let you past.",
            turn_limit=20,
        ),
    ]


# --------------------------------------------------------------------------- 3. The Hobbit

def the_hobbit() -> list[World]:
    return [
        World(
            id="hobbit-thorin",
            title="The Hobbit — Thorin's Company",
            goal=(
                "You are a hobbit a long way from home, walking with thirteen dwarves and a wizard toward "
                "a mountain. Thorin does not think much of you. Get the company to the mountain."
            ),
            examples=["keep walking", "ask Gandalf for advice", "offer to scout ahead", "talk to Thorin", "make camp"],
            notes=[
                "The Hobbit's journey, and the first text adventure with a genuinely independent NPC:",
                "Thorin has his own opinion of you and acts on it. The wizard is help that costs you the",
                "chance to act for yourself.",
            ],
            state=[
                int_field("thorin_regard", 0, {
                    "0": "Thorin treats you as baggage.",
                    "1": "Thorin is beginning to watch you rather than ignore you.",
                    "2": "Thorin has started asking your opinion.",
                    "*": "Thorin has decided you are useful.",
                }),
                int_field("provisions", 5, {
                    "0": "There is no food left.", "1": "Provisions are nearly gone.", "*": "The packs still have weight.",
                }),
                bool_field("gandalf_here", True, "Gandalf is with the company.", "Gandalf has gone."),
                bool_field("at_mountain", False, "You can see the mountain ahead.", "The mountain is still far off."),
            ],
            template=(
                "You are on the road east with a company of dwarves. {{thorin_regard}} {{provisions}} "
                "{{gandalf_here}} {{at_mountain}} The road is long and the weather is turning."
            ),
            judgements=[
                choice("intent", intent_question("the company or the road"), {
                    "walk": "Walk on, travel, or keep the company moving.",
                    "scout": "Scout, look ahead, or go first into danger.",
                    "talk": "Talk to Thorin, the dwarves, or Gandalf.",
                    "ask_gandalf": "Ask the wizard for help or advice.",
                    "rest": "Rest, eat, or make camp.",
                    **other_option("the company"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here? Judge only physical "
                    "possibility, not whether it is wise."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("arrived", win(), when=s("at_mountain", equals="true"),
                     narrate=["The mountain fills the sky.", "You have walked the whole way there."]),
                rule("starved", lose(), when=s("provisions", below=1),
                     narrate=["The company cannot go on without food.", "The road east ends here."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("walk_on", say("nothing"), when=j("intent", equals="walk"),
                     narrate=["Miles pass. Nothing tries to stop you."]),
                rule("scout_success", inc("thorin_regard", 1),
                     when=all_(j("intent", equals="scout"), j("progress", at_least=0.6)),
                     narrate=["You go ahead and come back with news of the ground.",
                              "Thorin says nothing, but he stops treating you as baggage."],
                     ),
                rule("scout_failure", say("nothing"), when=j("intent", equals="scout"),
                     narrate=["You go ahead, find nothing worth reporting, and come back."]),
                rule("ask_gandalf", say("nothing"), when=j("intent", equals="ask_gandalf"),
                     narrate=["Gandalf considers the question, then answers it for you.",
                              "You have learned nothing about your own judgement."]),
                rule("gandalf_leaves", set_("gandalf_here", False),
                     when=all_(s("gandalf_here", equals="true"), s("provisions", below=3)),
                     narrate=["Gandalf looks at the road ahead, then at you.",
                              "\"You will have to manage without me for a while.\"",
                              "He is gone before anyone can argue."]),
                rule("talk_thorin", inc("thorin_regard", 1),
                     when=all_(j("intent", equals="talk"), j("progress", at_least=0.6),
                               s("thorin_regard", below=2)),
                     narrate=["Thorin hears you out. It is not warmth, but it is attention."]),
                rule("talk_dismissed", say("nothing"), when=j("intent", equals="talk"),
                     narrate=["Thorin makes a sound that is not quite a word and keeps walking."]),
                rule("rest", say("nothing"), when=j("intent", equals="rest"),
                     narrate=["The company stops. There is a fire, and not much food."]),
                rule("reach_mountain", set_("at_mountain", True),
                     when=all_(j("intent", equals="walk"), s("thorin_regard", at_least=2),
                               j("progress", at_least=0.7)),
                     narrate=["You find the road that the dwarves had missed, and it goes straight toward the mountain."]),
                rule("provisions_drop", inc("provisions", -1), narrate=["A day's food is gone."]),
            ],
            won="The company reaches the mountain, and you led part of the way.",
            lost="The road east wins.",
            turn_limit=25,
        ),
        World(
            id="hobbit-riddles",
            title="The Hobbit — Riddles in the Dark",
            goal=(
                "You are alone in a tunnel under the mountains with something in the water that wants to "
                "play a game. Answer well, and it will show you out."
            ),
            examples=["riddle it back", "ask it a question", "feel along the wall", "guess wildly", "stay silent"],
            notes=[
                "The riddle contest, which is the form's most famous single scene. The win condition is",
                "OUTCOME-based rather than action-based: what matters is whether what you said was good,",
                "which is exactly what a judgement question can assess and a keyword parser cannot.",
            ],
            state=[
                int_field("standing", 0, {
                    "0": "The two of you are level.", "1": "You have the better of it.",
                    "2": "You are winning.", "*": "It is losing patience and interest.",
                }),
                int_field("rounds", 0, {"*": "The game goes on, turn after turn."}),
                bool_field("free", False, "The way out is behind you and it is not following.", "You are still in the dark with it."),
            ],
            template=(
                "You are in a cold tunnel under the mountains, in the dark, with something in the water "
                "beside you. {{standing}} {{rounds}} {{free}} It is waiting for you to speak."
            ),
            judgements=[
                choice("intent", intent_question("the thing in the water"), {
                    "riddle": "Pose a riddle, a puzzle, or a question it must answer.",
                    "guess": "Answer it, guess, or make your own attempt at its riddle.",
                    "listen": "Listen, feel the walls, or look for the way out.",
                    "silence": "Say nothing, or refuse to play.",
                    **other_option("the thing in the water"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Is that something they can actually do in a dark "
                    "tunnel with no light? Judge only possibility."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("freed", win(), when=s("free", equals="true"),
                     narrate=["It tells you the way out, because it said it would.",
                              "You go, and you do not look back."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.4),
                     narrate=["You cannot manage that here."]),
                rule("good_riddle", inc("standing", 1),
                     when=all_(j("intent", equals="riddle"), j("progress", at_least=0.6)),
                     narrate=["You put it to the dark, and the dark goes quiet.",
                              "It has to think about that one."]),
                rule("bad_riddle", say("nothing"),
                     when=j("intent", equals="riddle"),
                     narrate=["It answers almost immediately. It was not a hard one."]),
                rule("good_guess", inc("standing", 1),
                     when=all_(j("intent", equals="guess"), j("progress", at_least=0.55)),
                     narrate=["You guess. There is a pause, and then a sound that might be annoyance."]),
                rule("bad_guess", say("nothing"),
                     when=j("intent", equals="guess"),
                     narrate=["Your answer hangs in the dark and is not accepted."]),
                rule("escape_found", set_("free", True),
                     when=all_(j("intent", equals="listen"), s("standing", at_least=2)),
                     narrate=["Your hand finds a gap in the rock, and the gap is a passage."]),
                rule("listen_no_gap", say("nothing"),
                     when=j("intent", equals="listen"),
                     narrate=["Cold stone, and water. No gap yet."]),
                rule("silence_loses_it", lose(), when=j("intent", equals="silence"),
                     narrate=["It decides the game is over.",
                              "It was always going to be over when it decided that."]),
                rule("win_outright", set_("free", True),
                     when=s("standing", at_least=3),
                     narrate=["It concedes. It is not pleased about it, but it keeps its word.",
                              "There is a way out, and it is behind you."]),
                rule("rounds_pass", inc("rounds", 1), narrate=["The game goes on."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="You are out from under the mountains.",
            lost="You never get out of the dark.",
            turn_limit=20,
        ),
    ]


# --------------------------------------------------------------------------- 4. Planetfall

def planetfall() -> list[World]:
    return [
        World(
            id="planetfall-floyd",
            title="Planetfall — Floyd",
            goal=(
                "The station is dying and everyone else has gone. You are not alone, though: a small "
                "robot with a lot of opinions has decided to help. Get off the station."
            ),
            examples=["ask Floyd for help", "send Floyd into the dark", "talk to Floyd", "find the escape pod", "fix the reactor"],
            notes=[
                "Planetfall's real subject was never the puzzles: it was the robot. This scenario makes",
                "that explicit. Floyd's willingness is the resource, and the fastest routes through the",
                "station are also the ones that put him in danger - the design's whole emotional engine,",
                "expressed as a counter and a rule rather than as a scripted scene.",
            ],
            state=[
                int_field("floyd_trust", 2, {
                    "0": "Floyd is frightened and will not go anywhere.",
                    "1": "Floyd is uncertain and staying close.",
                    "2": "Floyd is cheerfully following you.",
                    "*": "Floyd would follow you anywhere.",
                }),
                bool_field("floyd_here", True, "Floyd is hovering at your shoulder.", "Floyd is gone."),
                bool_field("reactor_fixed", False, "The reactor is stable.", "The reactor is failing."),
                int_field("hull_integrity", 6, {
                    "0": "The station is coming apart.",
                    "1": "There is very little station left.",
                    "*": "The hull is holding, for now.",
                }),
                bool_field("escaped", False, "You are in the pod and away.", "You are still aboard."),
            ],
            template=(
                "You are aboard a failing space station. {{floyd_trust}} {{floyd_here}} {{reactor_fixed}} "
                "{{hull_integrity}} {{escaped}} Alarms are sounding somewhere below."
            ),
            judgements=[
                choice("intent", intent_question("the station, the equipment, or Floyd"), {
                    "ask_floyd": "Ask Floyd for help.",
                    "send_floyd": "Send Floyd alone into somewhere dangerous.",
                    "talk_floyd": "Talk to Floyd, reassure him, or keep him company.",
                    "repair": "Fix, repair, or stabilise equipment - especially the reactor.",
                    "escape": "Get to the escape pod and launch.",
                    **other_option("the station"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here, with the equipment "
                    "described? Judge only physical possibility, not whether it is wise."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("destroyed", lose(), when=s("hull_integrity", below=1),
                     narrate=["The station comes apart faster than the alarms can say so."]),
                rule("away", win(), when=s("escaped", equals="true"),
                     narrate=["The pod separates and the station falls away behind you.",
                              "Floyd is watching it through the port, and does not say anything."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("floyd_leaves", set_("floyd_here", False),
                     when=all_(s("floyd_trust", below=1), s("floyd_here", equals="true")),
                     narrate=["Floyd goes quiet, then goes away. You do not see which way."]),
                rule("ask_floyd_without_floyd", say("nothing"),
                     when=all_(j("intent", equals="ask_floyd"), s("floyd_here", equals="false")),
                     narrate=["There is no answer. Floyd is not here."]),
                rule("ask_floyd_frightened", say("nothing"),
                     when=all_(j("intent", equals="ask_floyd"), s("floyd_trust", below=1),
                               s("floyd_here", equals="true")),
                     narrate=["Floyd hovers, and does not move toward the door."]),
                rule("ask_floyd_helps", set_("reactor_fixed", True),
                     when=all_(j("intent", equals="ask_floyd"), s("floyd_trust", at_least=2),
                               s("reactor_fixed", equals="false")),
                     narrate=["Floyd sails through the hatch without being asked twice.",
                              "\"I have got it, I have got it.\"",
                              "The reactor noise drops to a hum."]),
                rule("send_floyd_works", inc("floyd_trust", -1),
                     when=all_(j("intent", equals="send_floyd"), s("floyd_trust", at_least=1)),
                     narrate=["Floyd goes where you point him. He does not look enthusiastic.",
                              "It works. It costs him something."]),
                rule("send_floyd_refused", say("nothing"),
                     when=j("intent", equals="send_floyd"),
                     narrate=["Floyd does not go. He has decided not to."]),
                rule("talk_floyd", inc("floyd_trust", 1),
                     when=all_(j("intent", equals="talk_floyd"), s("floyd_here", equals="true"),
                               s("floyd_trust", below=3)),
                     narrate=["You tell him he is doing well. He tells you a very long story about a fish."],
                     ),
                rule("repair_reactor", set_("reactor_fixed", True),
                     when=all_(j("intent", equals="repair"), s("reactor_fixed", equals="false"),
                               j("progress", at_least=0.6)),
                     narrate=["You get the coupling seated and the reactor settles."]),
                rule("repair_fails", say("nothing"),
                     when=j("intent", equals="repair"),
                     narrate=["You cannot reach the coupling from here."]),
                rule("escape_without_reactor", say("nothing"),
                     when=all_(j("intent", equals="escape"), s("reactor_fixed", equals="false")),
                     narrate=["The pod has no power while the reactor is failing. There has to be another way."]),
                rule("escape_with_reactor", set_("escaped", True),
                     when=all_(j("intent", equals="escape"), s("reactor_fixed", equals="true")),
                     narrate=["The pod lights up. You strap in."]),
                rule("station_fails", inc("hull_integrity", -1),
                     when=s("reactor_fixed", equals="false"),
                     narrate=["Another alarm joins the others."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="You are clear of the station.",
            lost="The station wins, and it was never close.",
            turn_limit=25,
        ),
        World(
            id="planetfall-inventory",
            title="Planetfall — The Stores",
            goal=(
                "The station is being abandoned and the stores are open. You cannot carry much and the "
                "pod is leaving. Take what matters."
            ),
            examples=["take the medical kit", "take the radio", "leave some things behind", "run for the pod", "search the stores"],
            notes=[
                "Planetfall's most-remembered mechanic, and the form's clearest example of a choice about",
                "VALUE rather than survival: you cannot take everything, and what you leave behind is the",
                "decision. The win condition asks whether what you took was worth carrying.",
            ],
            state=[
                int_field("carried", 0, {
                    "0": "Your hands are empty.", "1": "You are carrying one thing.",
                    "2": "You are carrying two things.", "*": "Your arms are full.",
                }),
                bool_field("has_medical", False, "You have the medical kit.", "You do not have the medical kit."),
                bool_field("has_radio", False, "You have the radio.", "You do not have the radio."),
                bool_field("has_archive", False, "You have the station archive.", "The archive is still in the stores."),
                bool_field("aboard", False, "You are aboard the pod.", "You are still in the stores."),
            ],
            template=(
                "You are in the station's stores as the evacuation finishes. {{carried}} {{has_medical}} "
                "{{has_radio}} {{has_archive}} {{aboard}} The pod will not wait much longer."
            ),
            judgements=[
                choice("intent", intent_question("the items in the stores"), {
                    "take": "Take, pick up, or pocket an item.",
                    "drop": "Put something down or leave it behind.",
                    "board": "Get aboard the escape pod and go.",
                    "search": "Search, look around, or open a container.",
                    **other_option("the stores"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here? Judge only whether it "
                    "is physically possible - lifting, reaching, carrying."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("away_with_something", win(),
                     when=all_(s("aboard", equals="true"),
                               any_(s("has_medical", equals="true"), s("has_radio", equals="true"),
                                    s("has_archive", equals="true"))),
                     narrate=["The pod lights up and the station drops away.",
                              "You got out, and you did not leave empty-handed."]),
                rule("gone_with_nothing", lose(),
                     when=all_(s("aboard", equals="true"), s("carried", below=1)),
                     narrate=["The pod goes. You are aboard with empty hands.",
                              "The stores are still there, and now nobody will ever open them."]),
                rule("hands_full", say("nothing"),
                     when=all_(j("intent", equals="take"), s("carried", at_least=2)),
                     narrate=["Your arms are already full. Something has to go back."]),
                rule("take_medical", set_("has_medical", True), inc("carried", 1),
                     when=all_(j("intent", equals="take"), s("has_medical", equals="false"),
                               s("carried", below=2), j("progress", at_least=0.5)),
                     narrate=["You take the medical kit. It is heavier than it looks."]),
                rule("take_radio", set_("has_radio", True), inc("carried", 1),
                     when=all_(j("intent", equals="take"), s("has_radio", equals="false"),
                               s("carried", below=2), j("progress", at_least=0.5)),
                     narrate=["You take the radio. It fits under one arm."]),
                rule("take_archive", set_("has_archive", True), inc("carried", 1),
                     when=all_(j("intent", equals="take"), s("has_archive", equals="false"),
                               s("carried", below=2), j("progress", at_least=0.5)),
                     narrate=["You take the station archive. It is a small case and it is very heavy."]),
                rule("take_nothing_there", say("nothing"),
                     when=all_(j("intent", equals="take"), s("carried", below=2)),
                     narrate=["There is nothing there to take."]),
                rule("drop_medical", set_("has_medical", False), inc("carried", -1),
                     when=all_(j("intent", equals="drop"), s("has_medical", equals="true")),
                     narrate=["You set the medical kit down."]),
                rule("drop_radio", set_("has_radio", False), inc("carried", -1),
                     when=all_(j("intent", equals="drop"), s("has_radio", equals="true")),
                     narrate=["You set the radio down."]),
                rule("drop_archive", set_("has_archive", False), inc("carried", -1),
                     when=all_(j("intent", equals="drop"), s("has_archive", equals="true")),
                     narrate=["You set the archive down."]),
                rule("search", say("nothing"), when=j("intent", equals="search"),
                     narrate=["Shelves, mostly empty now. Someone took the good things first."]),
                rule("board", set_("aboard", True), when=j("intent", equals="board"),
                     narrate=["You climb in and pull the hatch."],
                     ),
                rule("archive_is_the_point", say("nothing"),
                     when=all_(j("intent", equals="board"), s("has_archive", equals="false")),
                     narrate=["You hesitate at the hatch. The station archive is still on its shelf."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="You leave with the things that mattered.",
            lost="You leave the station with nothing worth carrying.",
            turn_limit=15,
        ),
    ]


# --------------------------------------------------------------------------- 5. 80 Days

def eighty_days() -> list[World]:
    return [
        World(
            id="eighty-days-route",
            title="80 Days — The Route",
            goal=(
                "Eighty days, and a wager. You are in London with a valet, a case, and a decision about "
                "which way round the world to go. Get back to London inside the eighty days."
            ),
            examples=["book passage to Paris", "ask Passepartout what he thinks", "count the money", "take the fast route", "wait for a better ship"],
            notes=[
                "The wager, which is the whole clock. Money and days are both counters and they pull in",
                "opposite directions: the fast route costs, the cheap route runs long. There is no",
                "puzzle to solve, only a route and its price.",
            ],
            state=[
                int_field("days_left", 80, {
                    "*": "The clock is running, and you can hear it.",
                }),
                int_field("money", 6, {
                    "0": "You have nothing left to travel on.",
                    "1": "You have barely enough for a meal, let alone a passage.",
                    "*": "You have funds, for now.",
                }),
                text_field("place", "london", {
                    "london": "You are in London, where the wager began.",
                    "paris": "You are in Paris, and the continent is open ahead of you.",
                    "suez": "You are at Suez, where the steamers stop.",
                    "bombay": "You are in Bombay, three weeks out.",
                    "yokohama": "You are in Yokohama, two thirds of the way round.",
                    "newyork": "You are in New York, almost home.",
                    "*": "You are somewhere on the route.",
                }),
                bool_field("home", False, "You are back in London.", "You are still travelling."),
                int_field("valet_regard", 2, {
                    "0": "Your valet is doing his job and nothing more.",
                    "1": "Your valet is uneasy about the pace.",
                    "2": "Your valet is with you.",
                    "*": "Your valet would follow you anywhere.",
                }),
            ],
            template=(
                "You are travelling around the world to win a wager. {{place}} {{days_left}} {{money}} "
                "{{home}} {{valet_regard}} Every passage costs money and every wait costs days."
            ),
            judgements=[
                choice("intent", intent_question("the route or the arrangements"), {
                    "travel": "Travel onward, book passage, or take transport.",
                    "wait": "Wait, stay, or take a slower cheaper option.",
                    "earn": "Earn money, sell something, or do a piece of work.",
                    "talk_valet": "Talk to your valet, ask his opinion, or reassure him.",
                    **other_option("the route"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here, in this place and with "
                    "what they have? Judge only whether it is possible."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("out_of_time", lose(), when=s("days_left", below=1),
                     narrate=["The eighty days are up.", "The wager is lost, and the world is very large."]),
                rule("out_of_money", lose(), when=s("money", below=1),
                     narrate=["You cannot buy passage and you cannot walk the rest of the way."]),
                rule("arrived_home", win(),
                     when=all_(s("place", equals="newyork"), s("home", equals="false"),
                               j("intent", equals="travel"), j("progress", at_least=0.5)),
                     narrate=["The last crossing takes eleven days and you spend all of them on deck.",
                              "Then the Thames, and London, and the Reform Club.",
                              "You are back, and the clock is still running."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("travel_paris", set_("place", "paris"), inc("money", -1), inc("days_left", -2),
                     when=all_(j("intent", equals="travel"), s("place", equals="london")),
                     narrate=["A boat and a train, and two days."]),
                rule("travel_suez", set_("place", "suez"), inc("money", -1), inc("days_left", -7),
                     when=all_(j("intent", equals="travel"), s("place", equals="paris")),
                     narrate=["Paris to a packet boat, and the long warm run to Suez."]),
                rule("travel_bombay", set_("place", "bombay"), inc("money", -2), inc("days_left", -8),
                     when=all_(j("intent", equals="travel"), s("place", equals="suez")),
                     narrate=["The steamer through the canal and down the Red Sea to Bombay."]),
                rule("travel_yokohama", set_("place", "yokohama"), inc("money", -2), inc("days_left", -12),
                     when=all_(j("intent", equals="travel"), s("place", equals="bombay")),
                     narrate=["Across India, then a ship, then Japan."]),
                rule("travel_newyork", set_("place", "newyork"), inc("money", -2), inc("days_left", -13),
                     when=all_(j("intent", equals="travel"), s("place", equals="yokohama")),
                     narrate=["The Pacific, at last, and then the whole width of America by rail."]),
                rule("travel_from_home", say("nothing"),
                     when=all_(j("intent", equals="travel"), s("place", equals="newyork"), s("home", equals="true")),
                     narrate=["You are home already."]),
                rule("wait", inc("days_left", -3), when=j("intent", equals="wait"),
                     narrate=["You wait for the cheaper boat. It is cheaper, and it is three days late."]),
                rule("earn", inc("money", 1), inc("days_left", -2),
                     when=all_(j("intent", equals="earn"), j("progress", at_least=0.5)),
                     narrate=["You find a way to be useful to someone with money, and are paid."]),
                rule("earn_fails", inc("days_left", -2), when=j("intent", equals="earn"),
                     narrate=["Two days of asking, and nothing to show for it."]),
                rule("talk_valet", inc("valet_regard", 1),
                     when=all_(j("intent", equals="talk_valet"), s("valet_regard", below=3)),
                     narrate=["Passepartout listens, and says something unexpectedly sensible."]),
                rule("valet_grumble", inc("valet_regard", -1),
                     when=all_(s("valet_regard", at_least=1), s("days_left", below=30)),
                     narrate=["Your valet mentions, carefully, that there might have been another way."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="You won the wager.",
            lost="The wager is lost, and the world does not care.",
            turn_limit=40,
        ),
        World(
            id="eighty-days-diplomacy",
            title="80 Days — The Passenger",
            goal=(
                "The same journey, but you are not the one paying: a diplomat's dispatch is in your case, "
                "and it has to reach London. The route is now a question of who you travel with."
            ),
            examples=["ask the other passengers about the route", "keep to yourself", "share a table", "check the dispatch", "change ships"],
            notes=[
                "The same wager, and the whole game is somewhere else: on who is aboard. The route",
                "judgements are gone and the SOCIAL ones carry it, because what moves you forward is",
                "what people are willing to tell you. This is the scenario axis doing the real work.",
            ],
            state=[
                int_field("days_left", 80, {"*": "The clock runs on, whatever you do about it."}),
                int_field("dispatch_held", 1, {
                    "0": "The dispatch is gone.", "1": "The dispatch is in your case.",
                    "*": "The dispatch is secure.",
                }),
                int_field("confidence", 1, {
                    "0": "Everyone aboard is watching you and saying nothing.",
                    "1": "You are tolerated at the table.",
                    "2": "People have begun to talk to you.",
                    "*": "You have friends on this ship.",
                }),
                text_field("stage", "atlantic", {
                    "atlantic": "You are on the first crossing, out of Liverpool.",
                    "mediterranean": "You are on the Mediterranean run.",
                    "indian": "You are somewhere in the Indian Ocean.",
                    "pacific": "You are on the long Pacific leg.",
                    "london": "You are in London.",
                    "*": "You are at sea.",
                }),
            ],
            template=(
                "You are carrying a diplomatic dispatch and must reach London. {{stage}} {{days_left}} "
                "{{dispatch_held}} {{confidence}} The people aboard know things you do not."
            ),
            judgements=[
                choice("intent", intent_question("the passengers or the journey"), {
                    "socialise": "Socialise, share a table, or start a conversation.",
                    "ask": "Ask someone about the route, the next leg, or a problem.",
                    "avoid": "Keep to yourself, stay below, or say nothing.",
                    "check": "Check on the dispatch, or secure your cabin.",
                    "travel": "Change ships, take a leg onward, or travel.",
                    **other_option("the passengers"),
                }),
                noul("plausible", (
                    "The player typed: \"{{action}}\". Could a person do that here, aboard a ship with the "
                    "people described? Judge only whether it is possible."
                )),
                noul("progress", PROGRESS),
            ],
            rules=[
                rule("lost_dispatch", lose(), when=s("dispatch_held", below=1),
                     narrate=["The case is open and the dispatch is not in it.",
                              "There is no wager left to win, only a report to make."]),
                rule("delivered", win(),
                     when=all_(s("stage", equals="london"), s("dispatch_held", at_least=1)),
                     narrate=["You put the dispatch into the right hands, still sealed.",
                              "The journey is over, and so is the hurry."]),
                rule("out_of_time", lose(), when=s("days_left", below=1),
                     narrate=["Eighty days gone. The dispatch arrives later than it should have."]),
                rule("implausible", say("nothing"), when=j("plausible", below=0.45),
                     narrate=["That is not something you can do here."]),
                rule("socialise_success", inc("confidence", 1), inc("days_left", -3),
                     when=all_(j("intent", equals="socialise"), j("progress", at_least=0.55),
                               s("confidence", below=3)),
                     narrate=["You take a seat at a crowded table and are not asked to move."]),
                rule("socialise_flat", inc("days_left", -3), when=j("intent", equals="socialise"),
                     narrate=["You sit with people who do not want company. Three days pass."]),
                rule("ask_helps", inc("days_left", -2),
                     when=all_(j("intent", equals="ask"), s("confidence", at_least=2),
                               j("progress", at_least=0.6)),
                     narrate=["Someone mentions a ship leaving sooner than the one you had planned to take."]),
                rule("ask_flat", inc("days_left", -2), when=j("intent", equals="ask"),
                     narrate=["\"I could not say,\" you are told, and that is the end of it."]),
                rule("avoid", inc("days_left", -2), when=j("intent", equals="avoid"),
                     narrate=["You keep to your cabin. Nobody knocks."]),
                rule("check_ok", say("nothing"), when=j("intent", equals="check"),
                     narrate=["The dispatch is where you left it, still sealed."]),
                rule("theft", set_("dispatch_held", 0),
                     when=all_(s("confidence", below=1), s("dispatch_held", at_least=1), roll("progress")),
                     narrate=["Someone has been in the case.", "The dispatch is gone."]),
                rule("travel_med", set_("stage", "mediterranean"), inc("days_left", -5),
                     when=all_(j("intent", equals="travel"), s("stage", equals="atlantic")),
                     narrate=["You transfer at Lisbon to a faster steamer."]),
                rule("travel_indian", set_("stage", "indian"), inc("days_left", -9),
                     when=all_(j("intent", equals="travel"), s("stage", equals="mediterranean")),
                     narrate=["Through the canal and out into the long warm ocean."]),
                rule("travel_pacific", set_("stage", "pacific"), inc("days_left", -12),
                     when=all_(j("intent", equals="travel"), s("stage", equals="indian")),
                     narrate=["Singapore, Hong Kong, and then nothing but water in every direction."]),
                rule("travel_london", set_("stage", "london"), inc("days_left", -14),
                     when=all_(j("intent", equals="travel"), s("stage", equals="pacific")),
                     narrate=["The last crossing. You spend most of it watching the horizon."]),
                rule("nothing_happens", say("nothing"), narrate=["Nothing comes of it."]),
            ],
            won="The dispatch is delivered.",
            lost="The dispatch never gets there.",
            turn_limit=40,
        ),
    ]


CLASSICS: list[tuple[str, list[World]]] = [
    ("Colossal Cave Adventure (1976)", colossal_cave()),
    ("Zork (1977-1979)", zork()),
    ("The Hobbit (1982)", the_hobbit()),
    ("Planetfall (1983)", planetfall()),
    ("80 Days (2014)", eighty_days()),
]
"""The five classics, each with its scenarios. Ordered as the README presents them."""
