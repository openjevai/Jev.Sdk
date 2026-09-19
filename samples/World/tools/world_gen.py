"""world_gen.py — emit world documents and packages from structured definitions.

Why a generator rather than 25 hand-written files: a world document has a fixed skeleton (state,
template, judgements, rules) and hand-writing 25 of them invites the structural mistakes the loader
rejects — an undeclared state key, a fallback matching no option, a question missing `{{action}}`.
Generating from structured data makes those impossible by construction, so the authoring effort goes
into the CONTENT of each world rather than its syntax.

The output is ordinary markdown and ordinary json, identical to a hand-written world. The generator is
a convenience for the author, not a different format, which is the same "just text, autodetect" idea
the loader is built around.

Each world is written to worlds/classics/<id>.md and packaged as packages/<id>.zip.
"""

from __future__ import annotations

import json
import shutil
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

# --------------------------------------------------------------------------- authoring helpers

def bool_field(key: str, initial: bool, when_true: str, when_false: str) -> dict[str, Any]:
    return {
        "key": key, "type": "bool", "initial": initial,
        "describe": {"true": when_true, "false": when_false},
    }


def int_field(key: str, initial: int, describe: dict[str, str]) -> dict[str, Any]:
    return {"key": key, "type": "int", "initial": initial, "describe": describe}


def text_field(key: str, initial: str, describe: dict[str, str]) -> dict[str, Any]:
    return {"key": key, "type": "text", "initial": initial, "describe": describe}


def choice(key: str, question: str, options: dict[str, str], fallback: str = "other") -> dict[str, Any]:
    return {
        "key": key, "kind": "choice", "fallback": fallback, "question": question,
        "options": [{"key": k, "description": v} for k, v in options.items()],
    }


def noul(key: str, question: str, fallback: str = "0.5") -> dict[str, Any]:
    return {"key": key, "kind": "noul", "fallback": fallback, "question": question}


def score(key: str, question: str, levels: list[str], fallback: str) -> dict[str, Any]:
    return {"key": key, "kind": "score", "fallback": fallback, "question": question, "levels": levels}


# --- conditions -------------------------------------------------------------

# The wire format is camelCase and the authoring helpers are Python, so kwargs are translated here
# rather than typed in camelCase at 50 call sites. This was a real bug: `at_least=6` reached the
# loader as the unrecognised key `at_least`, the comparison was silently dropped, and every affected
# condition validated as "0 comparison(s)". Translating in one place is what stops that recurring.
_CAMEL = {
    "at_least": "atLeast",
    "below": "below",
    "equals": "equals",
    "is_one_of": "isOneOf",
    "chance_from": "chanceFrom",
    "chance": "chance",
    "scale": "scale",
}


def camel(kw: dict[str, Any]) -> dict[str, Any]:
    """Translates authoring kwargs to the wire's camelCase keys, rejecting anything unknown."""
    out: dict[str, Any] = {}

    for key, value in kw.items():
        if key not in _CAMEL:
            raise SystemExit(f"unknown condition key {key!r}; expected one of {sorted(_CAMEL)}")
        out[_CAMEL[key]] = value

    return out


def j(key: str, **kw) -> dict[str, Any]:
    """A comparison against a judgement."""
    return {"judgement": key, **camel(kw)}


def s(key: str, **kw) -> dict[str, Any]:
    """A comparison against a state field."""
    return {"state": key, **camel(kw)}


def all_(*parts: dict[str, Any]) -> dict[str, Any]:
    return {"all": list(parts)}


def any_(*parts: dict[str, Any]) -> dict[str, Any]:
    return {"any": list(parts)}


def not_(part: dict[str, Any]) -> dict[str, Any]:
    return {"not": part}


def roll(chance_from: str, at_least: float | None = None, scale: float | None = None) -> dict[str, Any]:
    """A condition that rolls the dice against a judgement's own probability."""
    body: dict[str, Any] = {"chanceFrom": chance_from}
    if at_least is not None:
        body["atLeast"] = at_least
    if scale is not None:
        body["scale"] = scale
    return {"roll": body}


def effects(*items: dict[str, Any]) -> list[dict[str, Any]]:
    """Collects several effects for one rule, so they are not mistaken for a condition."""
    return list(items)


# --- effects ---------------------------------------------------------------

def set_(key: str, to: Any) -> dict[str, Any]:
    return {"kind": "set", "state": key, "to": "true" if to is True else "false" if to is False else str(to)}


def inc(key: str, by: int) -> dict[str, Any]:
    return {"kind": "increment", "state": key, "by": by}


def say(text: str) -> dict[str, Any]:
    # The engine requires a narrate effect to carry text; the narration itself comes from `narrate`.
    return {"kind": "narrate", "text": text}


def win() -> dict[str, Any]:
    return {"kind": "win"}


def lose() -> dict[str, Any]:
    return {"kind": "lose"}


def rule(id: str, *effects: dict[str, Any], when: dict[str, Any] | None = None,
         narrate: list[str] | None = None) -> dict[str, Any]:
    body: dict[str, Any] = {"id": id}
    if when is not None:
        body["when"] = when
    body["then"] = list(effects) if effects else [say("nothing")]
    body["narrate"] = narrate if narrate is not None else ["Nothing comes of it."]
    return body


# --- the world -------------------------------------------------------------

@dataclass
class World:
    id: str
    title: str
    goal: str
    examples: list[str]
    state: list[dict[str, Any]]
    template: str
    judgements: list[dict[str, Any]]
    rules: list[dict[str, Any]]
    won: str
    lost: str
    turn_limit: int = 25
    notes: list[str] = field(default_factory=list)
    """Prose emitted above the machine sections, so a reader knows what this variant is."""

    def front_matter(self) -> str:
        return "\n".join([
            "---",
            f"id: {self.id}",
            f"title: {self.title}",
            'schemaVersion: "1.0"',
            f"turnLimit: {self.turn_limit}",
            f"goal: {self.goal}",
            "---",
        ])

    def to_markdown(self) -> str:
        def block(value: Any, lang: str) -> str:
            body = json.dumps(value, indent=2, ensure_ascii=False) if lang == "json" else str(value)
            return f"```{lang}\n{body}\n```"

        parts = [self.front_matter(), "", f"# {self.title}", "", self.goal, ""]

        if self.notes:
            parts.extend(["## About this variant", ""])
            parts.extend(line for note in self.notes for line in (note, ""))

        parts.extend([
            "## Examples", "",
            *[f"- {e}" for e in self.examples], "",
            "## State", "",
            "The facts this world keeps, and how each value reads to the model. The state is sent as",
            "prose, because a question like \"is this plausible\" cannot be answered from a row of flags.",
            "",
            block(self.state, "json"), "",
            "## State template", "",
            "Assembled from the `describe` entries above, and sent with every call.", "",
            block(self.template, "text"), "",
            "## Judgements", "",
            "One API question per entry, all asked in a single call. `fallback` is what the engine uses",
            "when the model returns something unusable for that question.", "",
            block(self.judgements, "json"), "",
            "## Rules", "",
            "Evaluated in order; the first whose condition holds is applied. Order is part of the design.",
            "",
            block(self.rules, "json"), "",
            "## Ending", "",
            block({"won": self.won, "lost": self.lost}, "json"), "",
        ])

        return "\n".join(parts)


# --------------------------------------------------------------------------- emission

def emit(worlds: list[World], root: Path) -> tuple[int, int]:
    """Writes each world as markdown and as a zip package. Returns (documents, packages)."""
    worlds_dir = root / "worlds" / "classics"
    packages_dir = root / "packages"

    worlds_dir.mkdir(parents=True, exist_ok=True)
    packages_dir.mkdir(parents=True, exist_ok=True)

    seen: set[str] = set()

    for world in worlds:
        if world.id in seen:
            raise SystemExit(f"duplicate world id: {world.id}")
        seen.add(world.id)

        markdown = world.to_markdown()
        document = worlds_dir / f"{world.id}.md"
        document.write_text(markdown, encoding="utf-8")

        # A package is the world document named 'world.md', which is what the player looks for first.
        staging = root / ".staging" / world.id
        shutil.rmtree(staging, ignore_errors=True)
        staging.mkdir(parents=True, exist_ok=True)
        (staging / "world.md").write_text(markdown, encoding="utf-8")
        (staging / "README.md").write_text(
            f"# {world.title}\n\n"
            f"{world.goal}\n\n"
            f"A Jev world package. Play it with:\n\n"
            f"    Jev.Sdk.World.Play {world.id}.zip\n\n"
            f"`world.md` is the world; everything the engine needs is inside it.\n",
            encoding="utf-8",
        )

        package = packages_dir / f"{world.id}.zip"
        with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as archive:
            for item in sorted(staging.iterdir()):
                archive.write(item, arcname=item.name)

        shutil.rmtree(staging, ignore_errors=True)

    shutil.rmtree(root / ".staging", ignore_errors=True)

    return len(worlds), len(worlds)
