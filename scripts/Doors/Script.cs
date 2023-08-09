using System;
using System.Text;
using System.Collections.Generic;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace Doors {
public sealed class Program : MyGridProgram {
#region Doors

// --- Door Auto Close ---

// The script will automatically close a door `DELAY` milliseconds after it's being opened.
const double DOOR_DELAY = 1000; // ms

// Should hangar doors also be closed and after which time?
const bool MANAGE_HANGARS = true;
const double HANGAR_DELAY = 10000; // ms

// If you don't want to auto close specific doors, add this keyword to their `CustomData`.
const string MANUAL_DOOR = "manual";


// --- Simple Airlock ---

// By default, the script will try to find airlocks (two doors close to each other) and manage them. It will close
// the just opened door first, then open the other one and close it again (all depending on autoCloseSeconds).
// If you don't want this functionality, set this main trigger to false:
const bool MANAGE_AIRLOCKS = true;

// The script will detect airlocks within this block radius of a just opened door (like back to back sliding doors).
const int AIRLOCK_RADIUS = 1; // blocks

// To protect the airlock from being opened too early, the script deactivates the second door until the first one is
// closed. To change this behavior, set the following value to `false`:
const bool PROTECT_AIRLOCK = true;

// You can add an additional delay (in seconds) between closing the first airlock door and opening the second one.
// Default is 0.
const double AIRLOCK_DELAY = 0; // ms

// If two nearby doors are accidentally treated as an airlock but are in fact just regular doors, you can add this
// keyword to one or both door's `CustomData` to disable airlock functionality (autoclose still works).
const string NO_AIRLOCK = "no-airlock";

// Update doors every `ITERATIONS_BEFORE_UPDATE` calls to `Main`.
// NOTE: Ticks to update is `UpdateFrequency * ITERATIONS_BEFORE_UPDATE`.
// NOTE: There are 60 ticks per second.
const int ITERATIONS_BEFORE_UPDATE = 100;

int Iterations = 0; // `Main` calls
int ManagedDoorsCount = 0;
int BrokenDoorsCount = 0;
DateTime Time = new DateTime();
List<IMyDoor> ManagedDoors = new List<IMyDoor>();
Dictionary<IMyDoor, DateTime> OpenDoors = new Dictionary<IMyDoor, DateTime>();
Dictionary<IMyDoor, IMyDoor> Airlocks = new Dictionary<IMyDoor, IMyDoor>();
Dictionary<IMyDoor, DateTime> OpenAirlocks = new Dictionary<IMyDoor, DateTime>();
Dictionary<IMyDoor, int> OpenAirlocksPhase = new Dictionary<IMyDoor, int>();

IMyTextSurface LCD;

Program() {
  // TODO: Close all managed doors on init.
  InitLCD();
  Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

void Main() {
  if (Iterations == 0) UpdateDoors();
  if (MANAGE_AIRLOCKS) {
    if (Iterations == 0) UpdateAirlocks();
    ManageAirlocks();
  }
  ManageDoors();

  if (ITERATIONS_BEFORE_UPDATE <= ++Iterations) Iterations = 0;

  Time += Runtime.TimeSinceLastRun;

  DisplayStatus();
}

void UpdateDoors() {
  BrokenDoorsCount = 0;
  GridTerminalSystem.GetBlocksOfType(ManagedDoors, SelectDoor);
  if (ManagedDoors.Count != ManagedDoorsCount) {
    ManagedDoorsCount = ManagedDoors.Count;
    OpenDoors.Clear();
    Airlocks.Clear();
    OpenAirlocksPhase.Clear();
  }
}

bool SelectDoor (IMyDoor door) {
  if (!door.IsSameConstructAs(Me)) return false;
  if (door.CustomData.Contains(MANUAL_DOOR)) return false;
  if (!MANAGE_HANGARS && door is IMyAirtightHangarDoor) return false;
  if (!door.IsFunctional) {
    BrokenDoorsCount++;
    return false;
  }
  return true;
}

void UpdateAirlocks() {
  float distance;
  float min_distance;
  int closest_door_idx;
  var doors = ManagedDoors.FindAll(door => !(door is IMyAirtightHangarDoor || door.CustomData.Contains(NO_AIRLOCK)));
  Airlocks.Clear();
  foreach (var door in doors) {
    min_distance = float.MaxValue;
    closest_door_idx = -1;
    for (int i = 0; i < doors.Count; i++) {
      if (doors[i] == door) continue;
      distance = Vector3.Distance(door.Position, doors[i].Position);
      if (distance <= AIRLOCK_RADIUS && distance < min_distance) {
        min_distance = distance;
        closest_door_idx = i;
        if (distance == 1) break;
      }
    }
    if (closest_door_idx >= 0) {
      Airlocks[door] = doors[closest_door_idx];
    }
  }
}

void ManageAirlocks() {
  foreach (var door_pair in Airlocks) {
    var door = door_pair.Key;
    var sibling = door_pair.Value;
    var need_to_close = OpenAirlocks.ContainsKey(door) ? (Time - OpenAirlocks[door]).TotalMilliseconds >= AIRLOCK_DELAY : true;
    var phase = OpenAirlocksPhase.ContainsKey(door) ? OpenAirlocksPhase[door] : 0;
    if (PROTECT_AIRLOCK) {
      if (door.Status != DoorStatus.Closed || !need_to_close || phase == 1) {
        sibling.Enabled = false;
      } else {
        sibling.Enabled = true;
      }
    }
    if (OpenAirlocksPhase.ContainsKey(sibling)) continue;
    if (door.Status == DoorStatus.Open) OpenAirlocksPhase[door] = 1;
    if (OpenAirlocksPhase.ContainsKey(door)) {
      if (OpenAirlocksPhase[door] == 1 && door.Status == DoorStatus.Closed) {
        OpenAirlocks[door] = Time;
        OpenAirlocksPhase[door] = 2;
        continue;
      }
      if(OpenAirlocksPhase[door] == 2 && need_to_close) {
        OpenAirlocks.Remove(door);
        OpenAirlocksPhase[door] = 3;
        sibling.OpenDoor();
      }
      if (OpenAirlocksPhase[door] == 3 && sibling.Status == DoorStatus.Closed) {
        OpenAirlocksPhase.Remove(door);
      }
    }
  }
}

void ManageDoors() {
  ManagedDoors.FindAll(door => door.Status == DoorStatus.Open).ForEach(door => {
    if (!OpenDoors.ContainsKey(door)) {
      OpenDoors[door] = door is IMyAdvancedDoor ? Time + TimeSpan.FromSeconds(1) : Time;
    } else {
      var time_passed = (Time - OpenDoors[door]).TotalMilliseconds;
      var time_limit = door is IMyAirtightHangarDoor ? HANGAR_DELAY : DOOR_DELAY;
      if (time_limit <= time_passed) {
        door.CloseDoor();
        OpenDoors.Remove(door);
      }
    }
  });
}

void InitLCD() {
  LCD = Me.GetSurface(0);
  LCD.ContentType = ContentType.TEXT_AND_IMAGE;
  LCD.FontColor = Color.White;
  LCD.BackgroundColor = Color.Black;
  LCD.WriteText("");
  LCD.ClearImagesFromSelection();
}

void DisplayStatus() {
  var broken_doors = 0 < BrokenDoorsCount ? "("+BrokenDoorsCount+" broken)" : "";
  var all_doors = "All managed doors: " + ManagedDoorsCount + " " + broken_doors + "\n";
  var single_doors = "Single doors: " + (ManagedDoorsCount - Airlocks.Count) + "\n";
  var airlocks = "Airlocks: " + (Airlocks.Count / 2) + "\n";
  var status = all_doors + single_doors + airlocks;
  LCD.WriteText(status);
  Echo(status);
}

#endregion // Doors
}}