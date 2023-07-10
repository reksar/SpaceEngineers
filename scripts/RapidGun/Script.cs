using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

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

namespace RapidGun {

public sealed class Program : MyGridProgram {

  /*
   * If the `Program` constructor has successfully initialized the gun system, then the `Main` method will run
   * automatically with the `UpdateFrequency`.
   */

  #region RapidGun

  // The pivot of the entire gun system. Sliding the piston will change the `Barrel` level.
  IMyPistonBase Piston;

  // Attached to the `Piston` top and holds the gun `Barrel`. Rotating the `Rotor` will change the `Gun` within the
  // `CurrentBarrelLevel`.
  IMyMotorAdvancedStator Rotor;
  ITerminalProperty<float> RotorVelocity;

  // `Rotor`s top grid is the gun `Barrel` - the `List` of identical *levels*. Each *level* is radially symmetrical and
  // has a gun in each of the 4 base directions on the plane. Guns are sorted by its direction (see `FindBarrel`).
  List<List<IMyUserControllableGun>> Barrel;
  int CurrentBarrelLevel;

  // Current active gun.
  IMyUserControllableGun Gun;

  // Will be different for *large* and *small* grids.
  float BlockSize; // m

  IMyTextSurface LCD, KeyLCD;

  const float MAX_ROTOR_TORQUE = 1000000000; // N*m
  const int QUARTERS_TOTAL = 4;
  const int NOWHERE = -1;

  static class U { // Utility

    public static IEnumerable<T> Select<T>(
      IMyGridTerminalSystem grid,
      Func<IMyTerminalBlock, bool> filter = null
    ) where T : class {
      var blocks = new List<IMyTerminalBlock>();
      grid.GetBlocksOfType<T>(blocks, filter);
      return blocks.Cast<T>();
    }

    public static IEnumerable<T> Select<T>(IMyCubeGrid grid, IEnumerable<Vector3I> positions) where T : class {
      return positions
        .Select(position => grid.GetCubeBlock(position)).OfType<IMySlimBlock>()
        .Select(block => block.FatBlock as T).OfType<T>();
    }

    // Returns radians in the range [0 .. 2π].
    // NOTE: similar function from `MathHelper` do not work!
    public static float Limit2Pi(float radians) {
      var arc = radians % MathHelper.TwoPi;
      if (arc < 0) arc += MathHelper.TwoPi;
      return arc;
    }

    // Positions surrounding the `center` in 4 base directions.
    public static ImmutableList<Vector3I> CrissCrossPositions(Vector3I center) {
      return ImmutableList.Create(new Vector3I[] {
        Vector3I.Forward + center,
        Vector3I.Right + center,
        Vector3I.Backward + center,
        Vector3I.Left + center
      });
    }

    public static IEnumerable<Vector3I> EndlessUp() {
      var position = Vector3I.Zero; // NOTE: Initial value will not be yielded!
      while (true) yield return position += Vector3I.Up;
    }
  }

  Program() {
    InitLCDs();
    if (SetGunSystem()) InitGunSystem(); else DisplayInitError();
  }

  // Will run with the `UpdateFrequency` set at the end of `InitGunSystem`. Or won't run on init fail.
  void Main(string argument, UpdateType updateSource) {

    // TODO: slide and rotate at the same time.
    // Mind the order!
    if (!PistonInPosition) Slide();
    else if (!RotorInPosition) Rotate();
    else if (!RotorStopped) Brake();
    else PrepareGun();

    DisplayStatus();
  }

  void InitLCDs() {
		LCD = Me.GetSurface(0);
		KeyLCD = Me.GetSurface(1);
    InitLCD(LCD);
    InitLCD(KeyLCD);
  }

  void InitLCD(IMyTextSurface lcd) {
		lcd.ContentType = ContentType.TEXT_AND_IMAGE;
    lcd.BackgroundColor = Color.Black;
    lcd.WriteText("");
		lcd.ClearImagesFromSelection();
  }

  bool SetGunSystem() {

    Piston = U.Select<IMyPistonBase>(GridTerminalSystem).FirstOrDefault(piston => {

      Rotor = FindRotor(piston);
      if (Rotor == null) return false;

      Barrel = FindBarrel(piston);
      return Barrel.Count > 0;
    });

    return Piston != null;
  }

  void InitGunSystem() {

    Barrel.ForEach(level => level.ForEach(gun => gun.Enabled = false));
    CurrentBarrelLevel = 0;
    Gun = null;

    RotorVelocity = Rotor.GetProperty("Velocity").AsFloat();
    Rotor.Displacement = -0.3f; // m
    RotorAngle = 0;
    StopRotor();

    Piston.MinLimit = 0; // m
    Piston.MaxLimit = 0; // m
    BlockSize = Me.CubeGrid.GridSize; // m
    SetPistonVelocity();

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
  }

  void Slide() {
    DisableGun();
    SetPistonVelocity();
  }

  void Rotate() {
    DisableGun();
    RotateRotor();
  }

  void Brake() {
    DisableGun();
    StopRotor();
  }

  // NOTE: The `Rotor` is expected to be locked in a calibrated position (see `RotorQuarter`)!
  void PrepareGun() {
    if (Gun == null) Gun = Barrel[CurrentBarrelLevel][ForwardQuarter()];
    if (GunAvailable(Gun)) Gun.Enabled = true; else SwitchGun();
  }

  void SwitchGun() {

    DisableGun();

    var position = ClosestGunPosition();
    var level = position.Item1;
    var quarter = position.Item2;

    if (NOWHERE < level && NOWHERE < quarter) {
      SetBarrelLevel(level);
      SetRotorAngle(quarter);
    }
  }

  MyTuple<int, int> ClosestGunPosition() {

    // In order by closest gun positions.
    var quarters = Quarters.OrderBy(QuarterOffset).ToArray();
    var levels = BarrelLevels.OrderBy(LevelOffset);

    foreach (var level in levels)
      foreach (var quarter in quarters)
        if (GunAvailable(Barrel[level][quarter]))
          return MyTuple.Create(level, quarter);

    return MyTuple.Create(NOWHERE, NOWHERE);
  }

  int LevelOffset(int level) {
    return Math.Abs(CurrentBarrelLevel - level);
  }

  // Make Right and Left quarters same in priority.
  // Order: Forward (0), Left and Right (1), Backward (2).
  int QuarterOffset(int quarter) {
    var right_quarter = RelativeQuarter(Base6Directions.Direction.Right);
    var left_quarter = RelativeQuarter(Base6Directions.Direction.Left);
    var quarter_offset = Math.Abs(RotorQuarter() - quarter);
    return quarter_offset < right_quarter ? quarter_offset : left_quarter;
  }

  void DisableGun() {
    if (Gun != null) Gun.Enabled = false;
    Gun = null;
  }

  IMyMotorAdvancedStator FindRotor(IMyPistonBase piston) {
    var block = piston.TopGrid.GetCubeBlock(Vector3I.Up);
    return block == null ? null : block.FatBlock as IMyMotorAdvancedStator;
  }

  List<List<IMyUserControllableGun>> FindBarrel(IMyPistonBase piston) {
    return U.EndlessUp().Select(U.CrissCrossPositions).Select(gun_positions => {
      return U.Select<IMyUserControllableGun>(Rotor.TopGrid, gun_positions).OrderBy(gun => {
        return RelativeQuarter(Base6Directions.GetForward(RelativeGunOrientation(gun, piston)));
      }).ToList();
    }).TakeWhile(guns => guns.Count > 0).ToList();
  }

  // Increases counter-clockwise, because the `Rotor` rotates clockwise.
  int RelativeQuarter(Base6Directions.Direction direction) {
    switch (direction) {
      case Base6Directions.Direction.Forward: return 0;
      case Base6Directions.Direction.Left: return 1;
      case Base6Directions.Direction.Backward: return 2;
      case Base6Directions.Direction.Right: return 3;
      default: return NOWHERE;
    }
  }

  int Quarter(Base6Directions.Direction direction) {
    return (RelativeQuarter(direction) + RotorQuarter()) % QUARTERS_TOTAL;
  }

  // Faster alternative for `Quarter(Base6Directions.Direction.Forward)`.
  int ForwardQuarter() {
    return RotorQuarter() % QUARTERS_TOTAL;
  }

  // There are 4 calibrated angles of the `Rotor` where it locked: 0, 0.5π, π, 1.5π.
  // They correspond to 4 quarters of a circle: 0 / 0.5π = 0, 0.5π / 0.5π = 1, π / 0.5π = 2, 1.5π / 0.5π = 3.
  int RotorQuarter() {
    return MathHelper.Floor(RotorAngle / MathHelper.PiOver2);
  }

  // NOTE: the global `Piston` has not been set yet, so we pass it in the `piston` arg.
  Quaternion RelativeGunOrientation(IMyCubeBlock gun, IMyPistonBase piston) {

    // Orientation of the fire direction of an active `Gun` relative to the main grid.
    var fire_orientation = Base6Directions.GetOrientation(
      Base6Directions.Direction.Forward,
      Base6Directions.Direction.Up
    );

    Quaternion piston_orientation;
    piston.Orientation.GetQuaternion(out piston_orientation);

    Quaternion stator_orientation;
    Rotor.Orientation.GetQuaternion(out stator_orientation);

    Quaternion gun_orientation;
    gun.Orientation.GetQuaternion(out gun_orientation);

    return fire_orientation * piston_orientation * stator_orientation * gun_orientation;
  }

  void DisplayInitError() {
    DisplayImage(LCD, "Cross");
    DisplayImage(KeyLCD, "Cross");
  }

  void DisplayStatus() {
    var status_image = GunReady(Gun) ? "Arrow" : "Danger";
		DisplayImage(KeyLCD, status_image);
    if (InDebug) DisplayDebugInfo(LCD);
    else DisplayImage(LCD, status_image);
  }

  void DisplayImage(IMyTextSurface lcd, string image) {
    lcd.WriteText("");
		lcd.ClearImagesFromSelection();
    lcd.AddImageToSelection(image);
  }

  void DisplayDebugInfo(IMyTextSurface lcd) {

    string state;
    if (GunReady(Gun)) state = "Ready";
    else if (!PistonInPosition) state = "Sliding";
    else if (!RotorInPosition) state = "Rotating";
    else if (!RotorStopped) state = "Braking";
    else if (Gun == null || Gun.IsShooting) state = "Selecting Gun";
    else state = "Unknown State";

    var current_angle = MathHelper.RoundToInt(MathHelper.ToDegrees(RotorAngle)).ToString();
    var desired_angle = MathHelper.RoundToInt(Rotor.UpperLimitDeg).ToString();
    var angle = current_angle + (Rotor.RotorLock ? "" : "/" + desired_angle) + "°";

    var level = (PistonInPosition ? "" : (Piston.Velocity < 0 ? "falls" : "rises") + " to ") + "level " +
      CurrentBarrelLevel.ToString();

    // TODO: show missed guns on the barrel
    var diagram = Rotor.RotorLock ? BarrelDiagramLocked() : BarrelDiagramRotation();

    lcd.ClearImagesFromSelection();
    lcd.WriteText(state+" "+angle+" "+level+"\n\n" + diagram);
  }

  //     +
  //     ^
  // + +   + +
  //     +
  //     +
  string BarrelDiagramLocked() {

    string diagram = "";

    string left_spacer = new string(' ', 3 * Barrel.Count - 1);

    GunsInDirection(Base6Directions.Direction.Forward).Reverse().ToList()
      .ForEach(gun => diagram += left_spacer + GunChar(gun) + "\n");

    GunsInDirection(Base6Directions.Direction.Left).ToList()
      .ForEach(gun => diagram += GunChar(gun) + " ");

    diagram += " ";

    GunsInDirection(Base6Directions.Direction.Right).ToList()
      .ForEach(gun => diagram += GunChar(gun) + " ");

    diagram += "\n";

    GunsInDirection(Base6Directions.Direction.Backward).ToList()
      .ForEach(gun => diagram += left_spacer + GunChar(gun) + "\n");

    return diagram;
  }

  // +      +
  //   +  +
  //   +  +
  // +      +
  string BarrelDiagramRotation() {

    string diagram = "";

    foreach (var level in BarrelLevels.Reverse()) {
      var guns = Barrel[level];
      var left_gun = guns[Quarter(Base6Directions.Direction.Left)];
      var forward_gun = guns[Quarter(Base6Directions.Direction.Forward)];
      diagram += LeftSpacer(level) + GunChar(left_gun) + CenterSpacer(level) + GunChar(forward_gun) + "\n";
    }

    foreach (var level in BarrelLevels) {
      var guns = Barrel[level];
      var backward_gun = guns[Quarter(Base6Directions.Direction.Backward)];
      var right_gun = guns[Quarter(Base6Directions.Direction.Right)];
      diagram += LeftSpacer(level) + GunChar(backward_gun) + CenterSpacer(level) + GunChar(right_gun) + "\n";
    }

    return diagram;
  }

  IEnumerable<int> BarrelLevels { get {
    return Enumerable.Range(0, Barrel.Count);
  }}

  IEnumerable<int> Quarters { get {
    return Enumerable.Range(0, QUARTERS_TOTAL);
  }}

  string LeftSpacer(int barrel_level) {
    return new string(' ', 2 * (Barrel.Count - barrel_level));
  }

  string CenterSpacer(int barrel_level) {
    return new string(' ', 4 * barrel_level + 2);
  }

  // All guns on the `Barrel` pointing in actual `direction`.
  IEnumerable<IMyUserControllableGun> GunsInDirection(Base6Directions.Direction direction) {
    return Barrel.Select(guns => guns[Quarter(direction)]);
  }

  char GunChar(IMyUserControllableGun gun) {
    return GunReady(gun) ? '^' : (GunAvailable(gun) ? '+' : '-');
  }

  bool InDebug { get {
    return Me.ShowOnHUD;
  }}

  bool PistonInPosition { get {
    return Piston.CurrentPosition == (Piston.Velocity < 0 ? Piston.MinLimit : Piston.MaxLimit);
  }}

  bool RotorInPosition { get {
    return RotorAngle == Rotor.UpperLimitRad;
  }}

  bool RotorStopped { get {
    // NOTE: there is no another way to get the current `Rotor` velocity.
    return Rotor.RotorLock && RotorVelocity.GetValue(Rotor) == 0;
  }}

  float RotorAngle {

    get {
      // `Rotor.Angle` can exceed 2π several times, but this is not visible in the in-game rotor properties!
      return U.Limit2Pi(Rotor.Angle);
    }

    set {
      // Try playing with the related property sliders in the game if you don't understand this order.
      if (value < Rotor.LowerLimitRad) {
        Rotor.LowerLimitRad = value;
        Rotor.UpperLimitRad = value;
      } else {
        Rotor.UpperLimitRad = value;
        Rotor.LowerLimitRad = value;
      }
    }
  }

  bool GunAvailable(IMyUserControllableGun gun) {
    return gun != null && !gun.IsShooting && gun.IsFunctional;
  }

  bool GunReady(IMyUserControllableGun gun) {
    return GunAvailable(gun) && gun.Enabled;
  }

  void SetRotorAngle(int quarter) {
    RotorAngle = MathHelper.PiOver2 * quarter;
    RotateRotor();
  }

  void SetBarrelLevel(int level) {
    // NOTE: expects levels are close together!
    Piston.MaxLimit = level * BlockSize; // m
    CurrentBarrelLevel = level;
    SetPistonVelocity();
  }

  void SetPistonVelocity() {
    if (Piston.MinLimit < Piston.MaxLimit) Piston.Velocity = Piston.MaxVelocity;
    else Piston.Velocity = -Piston.MaxVelocity;
  }

  void StopRotor() {
    Rotor.RotorLock = true;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
    Rotor.TargetVelocityRad = 0;
  }

  void RotateRotor() {
    Rotor.TargetVelocityRad = MathHelper.Pi;
    Rotor.Torque = MAX_ROTOR_TORQUE;
    Rotor.BrakingTorque = 0;
    Rotor.RotorLock = false;
  }

  #endregion // RapidGun
}}