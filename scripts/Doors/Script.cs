using System;
using System.Collections.Generic;
using System.Text;

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

public sealed class Program : MyGridProgram {

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


List<IMyDoor> T = new List<IMyDoor>();
List<IMyDoor> U = new List<IMyDoor>();
List<IMyDoor> V = new List<IMyDoor>();
int W = 0;
Dictionary<IMyDoor, DateTime> X = new Dictionary<IMyDoor, DateTime>();
Dictionary<IMyDoor, DateTime> Y = new Dictionary<IMyDoor, DateTime>();
Dictionary<IMyDoor, IMyDoor> Z = new Dictionary<IMyDoor, IMyDoor>();
Dictionary<IMyDoor, int> a = new Dictionary<IMyDoor, int>();
string[] b = {"/", "-", "\\", "|"};
DateTime c = new DateTime();
int e = 0;

Program() {
  Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

void Main() {
  if (e == 0) S();
  if (manageAirlocks) {
    if (e == 0) D();
    K();
  }
  Q();
  R();
  c += Runtime.TimeSinceLastRun;
  if (e >= 99) {
    e = 0;
  } else {
    e++;
  }
}

void S() {
  V.Clear();
  GridTerminalSystem.GetBlocksOfType(T, B);
  if (T.Count != W) {
    W = T.Count;
    X.Clear();
    Z.Clear();
    a.Clear();
  }
}

bool B (IMyDoor C) {
  if (!C.IsSameConstructAs(Me)) return false;
  if (!C.IsFunctional) {
    V.Add(C);
    return false;
  }
  if (C.CustomName.Contains(manualDoorKeyword)) return false;
  if (!autoCloseHangarDoors && C is IMyAirtightHangarDoor) return false;
  return true;
}

void D() {
  Vector3 E=new Vector3();
  float F = 0;
  float G = float.MaxValue;
  int H = -1;
  U.Clear();
  Z.Clear();
  U = T.FindAll(I => !(I is IMyAirtightHangarDoor));
  foreach(var C in U) {
    if (C.CustomName.Contains(noAirlockKeyword)) continue;
    E = C.Position;
    G = float.MaxValue;
    H = -1;
    for (int J = 0; J < U.Count; J++) {
      if (U[J] == C) continue;
      if (U[J].CustomName.Contains(noAirlockKeyword)) continue;
      F = Vector3.Distance(E, U[J].Position);
      if (F <= airlockRadius && F < G) {
        G = F;
        H = J;
        if (F == 1) break;
      }
    }
    if( H >= 0) {
      Z[C] = U[H];
    }
  }
}

void K() {
  foreach (var L in Z) {
    IMyDoor M = L.Key;
    IMyDoor N = L.Value;
    bool O = Y.ContainsKey(M) ? (c - Y[M]).TotalMilliseconds >= airlockDelaySeconds * 1000 : true;
    int P = a.ContainsKey(M) ? a[M] : 0;
    if (protectAirlock) {
      if (M.Status != DoorStatus.Closed || !O || P == 1) {
        N.Enabled = false;
      } else {
         N.Enabled = true;
      }
    }
    if (a.ContainsKey(N)) continue;
    if (M.Status == DoorStatus.Open) {
      a[M] = 1;
    }
    if (a.ContainsKey(M)) {
      if (a[M] == 1 && M.Status == DoorStatus.Closed) {
        Y[M] = c;
        a[M] = 2;
        continue;
      }
      if(a[M] == 2 && O) {
        Y.Remove(M);
        a[M] = 3;
        N.OpenDoor();
      }
      if (a[M] == 3 && N.Status == DoorStatus.Closed) {
        a.Remove(M);
      }
    }
  }
}

void Q() {
  foreach (var C in T) {
    if (C.Status == DoorStatus.Open) {
      if (!X.ContainsKey(C)) {
        X[C] = C is IMyAdvancedDoor ? c + TimeSpan.FromSeconds(1) : c;
        continue;
      }
      if (C is IMyAirtightHangarDoor) {
        if((c - X[C]).TotalMilliseconds >= autoCloseHangarDoorsSeconds * 1000) {
          C.CloseDoor();
          X.Remove(C);
        }
      } else {
        if((c - X[C]).TotalMilliseconds >= autoCloseSeconds * 1000) {
          C.CloseDoor();
          X.Remove(C);
        }
      }
    }
  }
}

void R() {
  StringBuilder A = new StringBuilder("Isy's Simple Doors "+b[e%4]+"\n================\n\n");
  A.Append("Refreshing cached doors in: "+Math.Ceiling((double)(99 - e)/6)+"s\n\n");
  A.Append("Managed doors: "+T.Count+"\n");
  A.Append("Door close seconds: "+autoCloseSeconds+"\n");
  if(autoCloseHangarDoors) {
    A.Append("Hangar door close seconds: "+autoCloseHangarDoorsSeconds+"\n");
  }
  if (manageAirlocks) {
    A.Append("\n");
    A.Append("Airlocks: "+Z.Count/2+"\n");
    A.Append("Airlock delay seconds: "+airlockDelaySeconds+"\n");
    A.Append("Airlock protection: "+(protectAirlock ? "true" : "false"));
    A.Append("\n");
  }
  if (V.Count > 0) {
    A.Append("\n");
    A.Append("Damaged doors: "+V.Count+"\n");
    foreach(var C in V) {
      A.Append("- "+C.CustomName+"\n");
    }
  }
  Echo(A.ToString());
}
}