// RoomState.cs
// Part of Jev.Sdk.Game. The entire game world: one room, one guard, one key, one door, one window.
//
// This type holds no Jev code and makes no calls. Every rule in the game lives here as a pure
// transition, which is what lets the game be tested without an API key and what makes the demo's
// claim honest — the model supplies judgement, the game decides outcomes, and the two are separable.
//
// See ../../Game.md for the design this implements.

using System.Text.Json.Serialization;

namespace Jev.Sdk.Game;

/// <summary>
/// The complete state of the escape attempt. Small enough to print, and the only thing sent to the
/// model each turn.
/// </summary>
public sealed class RoomState
{
    /// <summary>Whether the player is holding the key.</summary>
    [JsonPropertyName("has_key")]
    public bool HasKey { get; set; }

    /// <summary>Whether the guard is awake. A sleeping guard notices nothing.</summary>
    [JsonPropertyName("guard_awake")]
    public bool GuardAwake { get; set; }

    /// <summary>Whether the window has been broken.</summary>
    [JsonPropertyName("window_broken")]
    public bool WindowBroken { get; set; }

    /// <summary>Whether the door is unlocked.</summary>
    [JsonPropertyName("door_unlocked")]
    public bool DoorUnlocked { get; set; }

    /// <summary>Whether the player has left the room, which ends the game.</summary>
    [JsonPropertyName("escaped")]
    public bool Escaped { get; set; }

    /// <summary>How many turns have been taken, so a run cannot go on forever.</summary>
    [JsonPropertyName("turns")]
    public int Turns { get; set; }

    /// <summary>
    /// A short, honest description of the room as it stands, for the model's benefit.
    /// </summary>
    /// <remarks>
    /// Written as plain prose rather than a dump of the flags, because the model is being asked to
    /// judge plausibility against the situation and "has_key: false" reads as a database row while
    /// "the key is still on the table" reads as a room. Both are true; only one is useful to reason
    /// about.
    /// </remarks>
    public string Describe() =>
        $"""
        You are in a small locked room. A heavy door is the only way out.
        The key is {(HasKey ? "in your pocket" : "on a table across the room")}.
        The door is {(DoorUnlocked ? "unlocked" : "locked")}.
        The window is {(WindowBroken ? "broken, with cold air coming through" : "intact")}.
        The guard is {(GuardAwake ? "awake and watching you" : "asleep in the corner")}.
        """;
}
