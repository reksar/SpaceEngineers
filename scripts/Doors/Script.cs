using System;
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

// NOTE: still 99 iterations, even after decreasing the update frequency by x10.
const int BLOCKS_UPDATING_RATE = 99; // Iterations

// --- Door Auto Close ---
// =======================================================================================

// The script will automatically close a door 1 seconds after it's being opened. Change this value here if needed:
double autoCloseSeconds = 1;

// Should hangar doors also be closed and after which time?
bool autoCloseHangarDoors = true;
double autoCloseHangarDoorsSeconds = 10;

// If you don't want to auto close specific doors, add the manual door keyword to their names
// Note: blockname changes are only noticed every ~17 seconds, so it takes some time until your door is really excluded!
string manualDoorKeyword = "!manual";


// --- Simple Airlock ---
// =======================================================================================

// By default, the script will try to find airlocks (two doors close to each other) and manage them. It will close
// the just opened door first, then open the other one and close it again (all depending on autoCloseSeconds).
// If you don't want this functionality, set this main trigger to false:
bool manageAirlocks = true;

// The script will detect airlocks within a 2 block radius of a just opened door (like back to back sliding doors).
// Change this value, if your airlocks are wider:
int airlockRadius = 1;

// To protect the airlock from being opened too early, the script deactivates the second door until the first one is closed
// To change this behavior, set the following value to false:
bool protectAirlock = true;

// You can add an additional delay (in seconds) between closing the first airlock door and opening the second one (Default: 0).
double airlockDelaySeconds = 0;

// If two nearby doors are accidentally treated as an airlock but are in fact just regular doors, you can add this keyword
// to one or both door's names to disable airlock functionality (autoclose still works).
// Note: blockname changes are only noticed every ~17 seconds, so it takes some time until your door is really excluded!
string noAirlockKeyword = "!noAirlock";


int IterationCount = 0;
int ControlledDoorsCount = 0;
DateTime Time = new DateTime();
List<IMyDoor> ControlledDoors = new List<IMyDoor>();
List<IMyDoor> BrokenDoors = new List<IMyDoor>();
List<IMyDoor> Doors = new List<IMyDoor>(); // TODO: make it local to the function?
Dictionary<IMyDoor, DateTime> OpenDoors = new Dictionary<IMyDoor, DateTime>();
Dictionary<IMyDoor, IMyDoor> Airlocks = new Dictionary<IMyDoor, IMyDoor>();
Dictionary<IMyDoor, DateTime> OpenAirlocks = new Dictionary<IMyDoor, DateTime>();
Dictionary<IMyDoor, int> OpenAirlocksPhase = new Dictionary<IMyDoor, int>();

public Program() {
  Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

public void Main() {
  if (IterationCount == 0) UpdateDoors();
  if (manageAirlocks) {
    if (IterationCount == 0) UpdateAirlocks();
    ManageAirlocks();
  }
  ManageDoors();
  Time += Runtime.TimeSinceLastRun;
  if (BLOCKS_UPDATING_RATE <= ++IterationCount) IterationCount = 0;
}

void UpdateDoors() {
  BrokenDoors.Clear();
  GridTerminalSystem.GetBlocksOfType(ControlledDoors, DoorToControl);
  if (ControlledDoors.Count != ControlledDoorsCount) {
    ControlledDoorsCount = ControlledDoors.Count;
    OpenDoors.Clear();
    Airlocks.Clear();
    OpenAirlocksPhase.Clear();
  }
}

bool DoorToControl (IMyDoor door) {
  if (!door.IsSameConstructAs(Me)) return false;
  if (!door.IsFunctional) {
    BrokenDoors.Add(door);
    return false;
  }
  if (door.CustomName.Contains(manualDoorKeyword)) return false;
  if (!autoCloseHangarDoors && door is IMyAirtightHangarDoor) return false;
  return true;
}

void UpdateAirlocks() {
  Vector3 position = new Vector3();
  float distance = 0;
  float min_distance = float.MaxValue;
  int closest_door_idx = -1;
  Doors.Clear();
  Airlocks.Clear();
  Doors = ControlledDoors.FindAll(I => !(I is IMyAirtightHangarDoor));
  foreach (var door in Doors) {
    if (door.CustomName.Contains(noAirlockKeyword)) continue;
    position = door.Position;
    min_distance = float.MaxValue;
    closest_door_idx = -1;
    for (int i = 0; i < Doors.Count; i++) {
      if (Doors[i] == door) continue;
      if (Doors[i].CustomName.Contains(noAirlockKeyword)) continue;
      distance = Vector3.Distance(position, Doors[i].Position);
      if (distance <= airlockRadius && distance < min_distance) {
        min_distance = distance;
        closest_door_idx = i;
        if (distance == 1) break;
      }
    }
    if (closest_door_idx >= 0) {
      Airlocks[door] = Doors[closest_door_idx];
    }
  }
}

void ManageAirlocks() {
  foreach (var door_pair in Airlocks) {
    IMyDoor door = door_pair.Key;
    IMyDoor sibling = door_pair.Value;
    bool NeedToClose = OpenAirlocks.ContainsKey(door) ? (Time - OpenAirlocks[door]).TotalMilliseconds >= airlockDelaySeconds * 1000 : true;
    int phase = OpenAirlocksPhase.ContainsKey(door) ? OpenAirlocksPhase[door] : 0;
    if (protectAirlock) {
      if (door.Status != DoorStatus.Closed || !NeedToClose || phase == 1) {
        sibling.Enabled = false;
      } else {
        sibling.Enabled = true;
      }
    }
    if (OpenAirlocksPhase.ContainsKey(sibling)) continue;
    if (door.Status == DoorStatus.Open) {
      OpenAirlocksPhase[door] = 1;
    }
    if (OpenAirlocksPhase.ContainsKey(door)) {
      if (OpenAirlocksPhase[door] == 1 && door.Status == DoorStatus.Closed) {
        OpenAirlocks[door] = Time;
        OpenAirlocksPhase[door] = 2;
        continue;
      }
      if(OpenAirlocksPhase[door] == 2 && NeedToClose) {
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
  foreach (var door in ControlledDoors) {
    if (door.Status == DoorStatus.Open) {
      if (!OpenDoors.ContainsKey(door)) {
        OpenDoors[door] = door is IMyAdvancedDoor ? Time + TimeSpan.FromSeconds(1) : Time;
        continue;
      }
      if (door is IMyAirtightHangarDoor) {
        if((Time - OpenDoors[door]).TotalMilliseconds >= autoCloseHangarDoorsSeconds * 1000) {
          door.CloseDoor();
          OpenDoors.Remove(door);
        }
      } else {
        if((Time - OpenDoors[door]).TotalMilliseconds >= autoCloseSeconds * 1000) {
          door.CloseDoor();
          OpenDoors.Remove(door);
        }
      }
    }
  }
}

#endregion // Doors
}}