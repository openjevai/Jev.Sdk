"""compose.py — merge a scenario core with a judgement style, and emit every world.

The two axes are deliberately separate:

  SCENARIO  supplies the place, its state, its intent vocabulary, and its rules.
  STYLE     supplies the judgement frame - which questions are asked, and how the answers bite.

Composition has to resolve overlaps rather than emit them, because the loader rejects a world that
declares the same judgement twice or reuses a rule id. The style owns `plausible` and `progress` and
the implausibility rule, so the core's copies are dropped here rather than fought over at load time.
Keeping that rule in one place is what lets the styles differ at all.
"""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).parent))

from classics import CLASSICS  # noqa: E402
from styles import STYLES  # noqa: E402
from world_gen import World, emit  # noqa: E402

# The names a style owns. A core that also declares these has its copy dropped.
STYLE_JUDGEMENTS = {"plausible", "progress"}


def compose(core: World, style_factory) -> World:
    """Builds one world from a scenario core and a style."""
    style: dict[str, Any] = style_factory()

    style_judgement_keys = {j["key"] for j in style["judgements"]}
    style_rule_ids = {r["id"] for r in style["frame_before"] + style["frame_after"]}

    # The core keeps only what the style does not own.
    core_judgements = [j for j in core.judgements if j["key"] not in style_judgement_keys]
    core_rules = [r for r in core.rules if r["id"] not in style_judgement_keys and r["id"] not in style_rule_ids]

    # A style's own state fields have to reach the model, so their placeholders are appended to the
    # scenario's description. Without this a style's counters would be invisible to the judgements
    # that read them - which is the quiet way a style stops mattering.
    style_placeholders = " ".join(f"{{{{{f['key']}}}}}" for f in style["state"])
    template = core.template if not style_placeholders else f"{core.template} {style_placeholders}"

    return World(
        id=f"{core.id}-{style['id']}",
        title=f"{core.title} — {style['label']}",
        goal=core.goal,
        examples=core.examples,
        state=core.state + style["state"],
        template=template,
        judgements=core_judgements + style["judgements"],
        rules=style["frame_before"] + core_rules + style["frame_after"],
        won=core.won,
        lost=core.lost,
        turn_limit=core.turn_limit,
        notes=[
            f"**{style['label']} style.** {style['blurb']}",
            "",
            *core.notes,
        ],
    )


def every_world() -> list[tuple[str, str, World]]:
    """Returns (classic, style, world) for every combination."""
    built = []

    # Styles are the outer loop so a single style is grouped together in the emitted listing, which
    # makes the family resemblance visible: all five Peril worlds sit next to each other.
    for style_factory in STYLES:
        for classic, cores in CLASSICS:
            for core in cores:
                built.append((classic, style_factory()["label"], compose(core, style_factory)))

    return built


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    built = every_world()

    worlds = [w for _, _, w in built]
    documents, packages = emit(worlds, root)

    print(f"emitted {documents} world documents and {packages} packages under {root}")

    by_classic: dict[str, int] = {}
    by_style: dict[str, int] = {}

    for classic, style, _ in built:
        by_classic[classic] = by_classic.get(classic, 0) + 1
        by_style[style] = by_style.get(style, 0) + 1

    print("\nby classic:")
    for name, count in by_classic.items():
        print(f"  {count:2d}  {name}")

    print("\nby style:")
    for name, count in by_style.items():
        print(f"  {count:2d}  {name}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
