# The classics, as playable worlds

Five console choose-your-path classics, each rendered in five styles. Fifty worlds, fifty zip
packages, all generated from `tools/` and all validated by the engine that plays them.

## Why these five

"Top 5 of all time" has no authoritative ranking, so here is the reasoning rather than a claim of
canon. These are chosen on documented influence, and on being genuinely **choose-your-path** - a
branching narrative driven by typed decisions:

| # | Classic | Year | Why it is here |
| - | - | - | - |
| 1 | **Colossal Cave Adventure** | 1976 | The first interactive fiction. The cave, the lamp, and "xyzzy" are the form's founding vocabulary. |
| 2 | **Zork** | 1977-79 | The most commercially and culturally influential text adventure. The white house and the Great Underground Empire. |
| 3 | **The Hobbit** | 1982 | The most influential text adventure on home computers, and the first with a genuinely independent NPC. |
| 4 | **Planetfall** | 1983 | The canonical proof that the form could do comedy and character, not only puzzles. Floyd is its most-remembered inhabitant. |
| 5 | **80 Days** | 2014 | The modern high-water mark of branching narrative, and the clearest case that the form's subject is decisions and their cost. |

**What this excludes, and why.** Zork is here but the parser-world genre is not: a fixed map is not a
branching narrative. Rogue and its descendants are procedural, which is a different shape again. If
you want those, they are a different exercise - the engine would handle them, but they would not be
*choose your path*.

## The two axes of variation

**Axis one: the scenario.** Each classic is two scenarios, not one, because "the same classic" can
honestly mean two different games. Colossal Cave looting treasure and Colossal Cave surveying the
cave share a place and share nothing else. That is the more interesting axis, because it changes what
the player is trying to do.

**Axis two: the judgement style.** Each scenario is rendered five ways. A style is not a coat of
paint - it changes what the world *asks the model* and what its rules *do with the answers*:

| Style | What it adds |
| - | - |
| **Classic** | The original shape: intent, plausibility, progress. |
| **Peril** | Danger and a countdown. Attempts can wound you, and time is always running out. |
| **Social** | Another presence with a mood and a store of goodwill, so talking is a real route. |
| **Resource** | A supply counter that actions consume. Waste, and you run dry. |
| **Mythic** | Fate, which can make the impossible work, and a doom clock that advances every turn. |

Mythic is the one worth trying first if you only try one: it evaluates fate *before* plausibility, so
an impossible attempt can succeed when fate favours it. Order is the mechanism, not a decoration.

## Every world

### Colossal Cave Adventure (1976)

| World | State | Questions | Rules | Package |
| - | -: | -: | -: | - |
| `colossal-cave-classic-classic` | 4 | 3 | 17 | `colossal-cave-classic-classic.zip` |
| `colossal-cave-classic-mythic` | 5 | 4 | 20 | `colossal-cave-classic-mythic.zip` |
| `colossal-cave-classic-peril` | 6 | 4 | 20 | `colossal-cave-classic-peril.zip` |
| `colossal-cave-classic-resource` | 5 | 4 | 19 | `colossal-cave-classic-resource.zip` |
| `colossal-cave-classic-social` | 5 | 5 | 19 | `colossal-cave-classic-social.zip` |
| `colossal-cave-survey-classic` | 3 | 3 | 11 | `colossal-cave-survey-classic.zip` |
| `colossal-cave-survey-mythic` | 4 | 4 | 14 | `colossal-cave-survey-mythic.zip` |
| `colossal-cave-survey-peril` | 5 | 4 | 14 | `colossal-cave-survey-peril.zip` |
| `colossal-cave-survey-resource` | 4 | 4 | 13 | `colossal-cave-survey-resource.zip` |
| `colossal-cave-survey-social` | 4 | 5 | 13 | `colossal-cave-survey-social.zip` |

### 80 Days (2014)

| World | State | Questions | Rules | Package |
| - | -: | -: | -: | - |
| `eighty-days-diplomacy-classic` | 4 | 3 | 16 | `eighty-days-diplomacy-classic.zip` |
| `eighty-days-diplomacy-mythic` | 5 | 4 | 19 | `eighty-days-diplomacy-mythic.zip` |
| `eighty-days-diplomacy-peril` | 6 | 4 | 19 | `eighty-days-diplomacy-peril.zip` |
| `eighty-days-diplomacy-resource` | 5 | 4 | 18 | `eighty-days-diplomacy-resource.zip` |
| `eighty-days-diplomacy-social` | 5 | 5 | 18 | `eighty-days-diplomacy-social.zip` |
| `eighty-days-route-classic` | 5 | 3 | 16 | `eighty-days-route-classic.zip` |
| `eighty-days-route-mythic` | 6 | 4 | 19 | `eighty-days-route-mythic.zip` |
| `eighty-days-route-peril` | 7 | 4 | 19 | `eighty-days-route-peril.zip` |
| `eighty-days-route-resource` | 6 | 4 | 18 | `eighty-days-route-resource.zip` |
| `eighty-days-route-social` | 6 | 5 | 18 | `eighty-days-route-social.zip` |

### The Hobbit (1982)

| World | State | Questions | Rules | Package |
| - | -: | -: | -: | - |
| `hobbit-riddles-classic` | 3 | 3 | 12 | `hobbit-riddles-classic.zip` |
| `hobbit-riddles-mythic` | 4 | 4 | 15 | `hobbit-riddles-mythic.zip` |
| `hobbit-riddles-peril` | 5 | 4 | 15 | `hobbit-riddles-peril.zip` |
| `hobbit-riddles-resource` | 4 | 4 | 14 | `hobbit-riddles-resource.zip` |
| `hobbit-riddles-social` | 4 | 5 | 14 | `hobbit-riddles-social.zip` |
| `hobbit-thorin-classic` | 4 | 3 | 13 | `hobbit-thorin-classic.zip` |
| `hobbit-thorin-mythic` | 5 | 4 | 16 | `hobbit-thorin-mythic.zip` |
| `hobbit-thorin-peril` | 6 | 4 | 16 | `hobbit-thorin-peril.zip` |
| `hobbit-thorin-resource` | 5 | 4 | 15 | `hobbit-thorin-resource.zip` |
| `hobbit-thorin-social` | 5 | 5 | 15 | `hobbit-thorin-social.zip` |

### Planetfall (1983)

| World | State | Questions | Rules | Package |
| - | -: | -: | -: | - |
| `planetfall-floyd-classic` | 5 | 3 | 16 | `planetfall-floyd-classic.zip` |
| `planetfall-floyd-mythic` | 6 | 4 | 19 | `planetfall-floyd-mythic.zip` |
| `planetfall-floyd-peril` | 7 | 4 | 19 | `planetfall-floyd-peril.zip` |
| `planetfall-floyd-resource` | 6 | 4 | 18 | `planetfall-floyd-resource.zip` |
| `planetfall-floyd-social` | 6 | 5 | 18 | `planetfall-floyd-social.zip` |
| `planetfall-inventory-classic` | 5 | 3 | 15 | `planetfall-inventory-classic.zip` |
| `planetfall-inventory-mythic` | 6 | 4 | 18 | `planetfall-inventory-mythic.zip` |
| `planetfall-inventory-peril` | 7 | 4 | 18 | `planetfall-inventory-peril.zip` |
| `planetfall-inventory-resource` | 6 | 4 | 17 | `planetfall-inventory-resource.zip` |
| `planetfall-inventory-social` | 6 | 5 | 17 | `planetfall-inventory-social.zip` |

### Zork (1977-1979)

| World | State | Questions | Rules | Package |
| - | -: | -: | -: | - |
| `zork-house-classic` | 5 | 3 | 9 | `zork-house-classic.zip` |
| `zork-house-mythic` | 6 | 4 | 12 | `zork-house-mythic.zip` |
| `zork-house-peril` | 7 | 4 | 11 | `zork-house-peril.zip` |
| `zork-house-resource` | 6 | 4 | 11 | `zork-house-resource.zip` |
| `zork-house-social` | 6 | 5 | 11 | `zork-house-social.zip` |
| `zork-troll-classic` | 4 | 3 | 14 | `zork-troll-classic.zip` |
| `zork-troll-mythic` | 5 | 4 | 17 | `zork-troll-mythic.zip` |
| `zork-troll-peril` | 6 | 4 | 17 | `zork-troll-peril.zip` |
| `zork-troll-resource` | 5 | 4 | 16 | `zork-troll-resource.zip` |
| `zork-troll-social` | 5 | 5 | 16 | `zork-troll-social.zip` |

## Playing one

```sh
# from the repository root, after building
dotnet build samples/World/Jev.Sdk.World.slnx
dotnet samples/World/Jev.Sdk.World.Play/bin/Release/net10.0/Jev.Sdk.World.Play.dll \
    samples/World/packages/zork-house-peril.zip
```

Each package is a zip holding `world.md`. The player detects it, unpacks it to a temporary folder,
and plays from there - no unzipping by hand, and nothing left behind afterwards.

Needs an API key, since every turn asks the model what you are trying to do: set
`TYPESAFE_API_KEY`, or put one in a `.env` beside the executable. See `../README.md`.

## Regenerating

```sh
cd samples/World/tools
python3 compose.py
```

This rewrites every document and package. The generator is a convenience for the author, not a
different format: the output is ordinary markdown, identical to a hand-written world. The reason to
generate rather than hand-write 50 files is that a world has a fixed skeleton, and hand-writing
invites exactly the structural mistakes the loader rejects.

## What building these found

Four real defects, none of which reading the code would have shown:

1. **Every condition using `at_least` was silently dropped.** The Python authoring helpers emitted
   snake_case where the wire format is camelCase, so the comparison vanished and rules that plainly
   had a condition validated as empty. Fixed by translating in one place, which is now the only place
   a key can be misspelled.
2. **A win rule was shadowed in Zork.** Descending set the flag and the win rule could only fire on a
   *later* turn, so a run ending right after descending was scored as a loss.
3. **Planetfall's stores scenario could never be won at all.** Boarding always lost, and nothing won -
   so the scenario was a guaranteed defeat with no indication of it.
4. **A generated ending was reported as a loss after a win.** The escape set `escaped`, and the win
   rule sat second in the list, behind a rule that always matched first.

Cases 2, 3 and 4 are one class of bug: **an ending written as a condition can be permanently
shadowed by an earlier rule.** The validator cannot see it, because every rule involved is
individually valid and only their order is wrong. It is now impossible rather than merely detectable:
an ending rule is evaluated on the turn it becomes true, whatever else matched. Three of these were
found by *playing* the worlds, not by checking them.

