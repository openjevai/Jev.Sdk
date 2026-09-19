# The game, in a few paragraphs

**Escape the Room is a parser with no parser in it.** A player sits in one small locked room with a
key on a table, a window, and a guard asleep in the corner. They type whatever they like — "grab
key", "pocket the little brass thing", "I quietly creep over and slip it off the table" — and the game
works out what they meant. There are no verbs to learn, no `GET`/`USE`/`OPEN`, no autocomplete, and no
help text that must be read before the game can be played. That absence is the whole trick: the part
of a text adventure that is normally a lexicon, a thesaurus, and a pile of special cases is replaced
by a single call to a language model that is asked a handful of small, specific questions.

**The game asks five questions at once, and they are not the same kind of question.** What is the
player trying to do — that is a *classification*, one of seven named actions. Is it physically
possible from here, and would it wake the guard, and would it actually work — those are
*probabilities*, each an independent judgement about the same sentence. How loud is it — that is a
*rating* on an ordered scale from silent to extremely loud. Five questions of three different shapes,
all judged against the same room, all answered in one round trip. The player's sentence is never
parsed. It is *interpreted*, five ways, simultaneously.

**And then the model is put away, because none of that is the game.** The moment the answers come
back, the language model has no further role. A separate, entirely deterministic core reads the
judgement and applies rules a programmer wrote: the key can only be taken if there is a key, the door
can only be unlocked if the key is held, an action judged impossible changes nothing however confident
the classification was, and a smashed window wakes the guard no matter what probability the model
attached to the noise. This half is pure, has no network calls, and is tested exhaustively without an
API key. The model never touches the world.

**What the probabilities are for is the interesting part, and it is narrow.** Most actions do not need
a probability at all, and the game does not use one: taking a visible key either works or the key is
not there. Probabilities are reserved for the two situations a rule genuinely cannot decide —
sneaking past a *guard who is awake*, which is a contest, and a *creative action that matches none of
the seven intents*, where the player has typed something the designer never anticipated and the only
honest answer is a guess. In both cases the number is compared against a roll. This is what
distinguishes a probability from a yes/no answer, and the game is built so the player can see the
difference: the same words can succeed one turn and fail the next, and the transcript shows the
number that decided it.

**The state is the other half of the conversation.** Every call carries a short prose description of
the room as it currently stands — key held or on the table, door locked or open, guard asleep or
watching — because a judgement about "is this plausible" is meaningless without knowing the situation.
The same sentence, "unlock the door with the key", is judged differently depending on what the player
is carrying. The model is not told the rules; it is told the room, and asked what it thinks.

**What makes it a good demonstration is that it is small enough to hold in your head.** One room, one
guard, one key, one door, one window, five booleans of state, five questions, seven intents, four noise
levels. Nothing is hidden and nothing is clever. In five minutes of play someone can see what a
classification is for, what a rating is for, why a probability is not a boolean, why the model cannot
be the whole program, and why sending the state matters. The game is not the point; being small enough
that the *shape of the API use* is visible at a glance is the point.

**And it is a shape, not a game.** Strip away the room, the guard, and the key, and what remains is a
general pattern: *declare a world's state, declare a set of judgements to make about it, ask them all
at once, then run deterministic rules over the result.* Escape the Room is one instance of that
pattern with a deliberately tiny world. The interesting question is what happens when the world is not
tiny — and that is what `Jev.Sdk.World` is for.

---

*This file is the essay, kept as the design statement. The game itself is implemented in
`samples/Game/Jev.Sdk.Game/`, and the general pattern it demonstrates is implemented in
`samples/World/`.*
