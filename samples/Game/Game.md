A good demo game would be **“Escape the Room”**: one tiny room, a locked door, three objects, and the player types free-form actions into the console.

The room could contain a **key**, a **window**, and a **sleeping guard**. The goal is simply to get out. The player can type anything, such as “pick up the key,” “throw the chair through the window,” “sneak past the guard,” or “yell at the guard to wake up.”

The nice part is that the game logic stays extremely small, while Jev demonstrates why its API is useful.

Each turn, you send the current game state plus the player's text to one `system_one` call. For example, the state would conceptually contain the player's action, whether they have the key, whether the guard is awake, whether the window is broken, and whether the door is unlocked.

Then ask several questions in parallel:

* **`Choice` — What action is the player attempting?**
  Criteria might be `take_key`, `unlock_door`, `break_window`, `sneak_past_guard`, `attack_guard`, `talk_to_guard`, `other`. This demonstrates semantic classification without requiring rigid command syntax.

* **`Noul` — Is this action physically plausible in the current state?**
  “Unlock the door with the key” is plausible if they have the key. “Fly through the ceiling” probably isn't. This gives you a simple probability you can threshold.

* **`Noul` — Would this action wake the guard?**
  “Quietly pick up the key” might return a low probability; “smash the window with a chair” should return a high one.

* **`Score` — How noisy is the action?**
  Use a tiny rubric like `silent`, `quiet`, `loud`, `extremely loud`. This demonstrates ordered judgments rather than just yes/no decisions.

* **`Noul` — Does this action successfully move the player toward escaping?**
  This can help resolve creative commands that don't map perfectly to your predefined actions.

The game itself can remain deterministic after Jev gives those judgments. For example, if the action is classified as `take_key` and it is plausible, set `has_key = true`. If the player tries to unlock the door and has the key, the door opens. If the noise score is high enough, set `guard_awake = true`. If the guard is awake and the player tries to sneak past, use another `Noul` like “Would this attempt succeed?” and compare it against a random roll.

That last part makes the probabilities visibly useful. The console could show something like:

```text
> I quietly creep over and slip the key off the table.

Jev:
  intent: take_key       92%
  plausible:             99%
  wakes_guard:           14%
  noise:                 quiet

You take the key.
The guard keeps sleeping.
```

Then:

```text
> I hurl the chair through the window.

Jev:
  intent: break_window   96%
  plausible:             94%
  wakes_guard:           99%
  noise:                 extremely loud

CRASH!

The window breaks.
The guard wakes up.
```

The strongest part of this demo is that the player **never has to learn commands**. They can type “grab key,” “pocket the little brass thing,” or “carefully take whatever opens the door,” and the game can interpret them through `Choice`.

It also demonstrates the Jev model nicely because a single player action can produce several independent judgments at once:

```text
player command
      ↓
   Jev call
      ↓
 ┌───────────────┐
 intent      Choice
 plausible   Noul
 noisy        Score
 wakes guard Noul
 succeeds    Noul
 └───────────────┘
      ↓
normal game logic
```

I'd keep the entire game to **one room, one guard, one key, one door, and one window**. The point isn't the game itself; the point is that after five minutes of playing, someone immediately understands what `Choice`, `Score`, `Noul`, shared state, parallel questions, and probability-based decisions are for.

