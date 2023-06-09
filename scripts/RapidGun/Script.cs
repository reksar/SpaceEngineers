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
   *
   * I have not decided yet whether to convert this program to a state machine. This *may* make debugging easier.
   * But now it's easier to unravel the thread of Ariadne, starting from the `Main`.
   */

  #region RapidGun

  static class U { // Utility

    public static List<IMyBlockGroup> SelectGroups(
      IMyGridTerminalSystem grid,
      Func<IMyBlockGroup, bool> filter = null
    ) {
      var groups = new List<IMyBlockGroup>();
      grid.GetBlockGroups(groups, filter);
      return groups;
    }

    public static IEnumerable<T> Select<T>(
      IMyGridTerminalSystem grid,
      Func<IMyTerminalBlock, bool> filter = null
    ) where T : class {
      var blocks = new List<IMyTerminalBlock>();
      grid.GetBlocksOfType<T>(blocks, filter);
      return blocks.Cast<T>();
    }

    public static List<T> Select<T>(IMyBlockGroup group, Func<T, bool> filter = null) where T : class {
      var blocks = new List<T>();
      group.GetBlocksOfType<T>(blocks, filter);
      return blocks;
    }

    public static IEnumerable<T> Select<T>(IMyCubeGrid grid, IEnumerable<Vector3I> positions) where T : class {
      return positions
        .Select(position => grid.GetCubeBlock(position)).OfType<IMySlimBlock>()
        .Select(block => block.FatBlock as T).OfType<T>();
    }

    // Angle between normalized direction vectors `a` and `b` in range [-π .. π].
    public static float DirectionAngle(Vector3D a, Vector3D b) {
  
      // `Dot` product is enough for normalized vectors. Instead of `MyMath.CosineDistance`.
      var dot = (float)a.Dot(b);

      // `dot` may be slightly out of the `cos` range due to floating point numbers.
      // `Clamp` prevents further receipt of NaN from `Acos`.
      var cos = MyMath.Clamp(dot, -1, 1);

      var angle = (float)Math.Acos(cos);

      return CopySign(angle, cos);
    }

    public static float CopySign(float value, float sign) {
      return (value < 0) == (sign < 0) ? value : -value;
    }

    // Returns radians in the range [0 .. 2π].
    // NOTE: similar function from `MathHelper` do not work!
    public static float Limit2Pi(float radians) {
      var arc = radians % MathHelper.TwoPi;
      if (arc < 0) arc += MathHelper.TwoPi;
      return arc;
    }

    // Returns radians in the range [-π .. π].
    // NOTE: similar function from `MathHelper` do not work!
    public static float LimitPi(float radians) {
      if (radians > MathHelper.Pi) return radians % MathHelper.Pi - MathHelper.Pi;
      if (radians < -MathHelper.Pi) return radians % MathHelper.Pi + MathHelper.Pi;
      return radians;
    }
  }

  // The pivot of the entire gun system. Sliding the piston will change the `Barrel` level.
  IMyPistonBase Piston;

  // Attached to the `Piston` top and holds the gun `Barrel`. Rotating the `Rotor` will change the `Gun` within the
  // `CurrentBarrelLevel`.
  IMyMotorAdvancedStator Rotor;
  ITerminalProperty<float> RotorVelocity;

  // `Rotor`s top grid is the `Barrel`. Consists of identical *levels* grouped in a `List`. Each *level* is radially
  // symmetrical and has a gun in each of the 4 base directions on the plane. An `int` key of the `Dictionary` is the
  // `GunBaseAngle` of the related gun.
  List<Dictionary<int, IMyUserControllableGun>> Barrel;
  int CurrentBarrelLevel;

  // Current active gun in `FireDirection`.
  IMyUserControllableGun Gun; 

  // Hinges and attached wheels are used to brake the `Rotor`.
  // NOTE: this mechanic is currently buggy (see `InitGunSystem`).
  // NOTE: There is no interface for the *hinge* terminal block, but `IMyMotorStator` should do.
  List<IMyMotorStator> Hinges;

  // Will be different for *large* and *small* grids.
  float BlockSize; // m

  IMyTextSurface LCD, KeyLCD;

  const float MAX_ROTOR_TORQUE = 1000000000; // N*m

  // Directions on a 2D square grid representing a gun plane - the barrel cross section.
  // TODO: move it to a local scope.
  ImmutableList<Vector3I> SURROUNDING_DIRECTIONS = ImmutableList.Create(new Vector3I[] {
    Vector3I.Forward,
    Vector3I.Right,
    Vector3I.Backward,
    Vector3I.Left
  });
  ImmutableList<Vector3I> DIAGONAL_DIRECTIONS = ImmutableList.Create(new Vector3I[] {
    Vector3I.Forward + Vector3I.Left,
    Vector3I.Forward + Vector3I.Right,
    Vector3I.Backward + Vector3I.Left,
    Vector3I.Backward + Vector3I.Right
  });

  Program() {
    InitLCDs();
    if (SetGunSystem()) InitGunSystem(); else DisplayInitError();
  }

  // Will run with the `UpdateFrequency` set at the end of `InitGunSystem`. Or won't run on init fail.
  void Main(string argument, UpdateType updateSource) {

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

      var block = piston.TopGrid.GetCubeBlock(Vector3I.Up);
      if (block == null) return false;

      Rotor = block.FatBlock as IMyMotorAdvancedStator;
      return Rotor != null && SetBarrel(Rotor.TopGrid);
    });

    return Piston != null;
  }

  bool SetBarrel(IMyCubeGrid barrel) {

    Barrel = UpByAxis()
      .Select(SurroundingPositions)
      .Select(positions =>
        U.Select<IMyUserControllableGun>(barrel, positions).ToDictionary(GunWorkingAngle, gun => gun))
      .TakeWhile(guns => guns.Count > 0)
      .ToList();

    if (Barrel.Count <= 0) return false;

    Hinges = UpByAxis()
      .Select(DiagonalPositions)
      .Select(positions => U.Select<IMyMotorStator>(barrel, positions).ToList())
      .TakeWhile(hinges => hinges.Count > 0)
      .SelectMany(hinges => hinges)
      .ToList();

    return Hinges.Count > 0;
  }

  void InitGunSystem() {

    Barrel.SelectMany(level => level.Values).ToList().ForEach(gun => gun.Enabled = false);
    Gun = null;
    CurrentBarrelLevel = 0;

    Piston.MinLimit = 0; // m
    Piston.MaxLimit = 0; // m
    BlockSize = Me.CubeGrid.GridSize; // m
    UpdatePistonVelocity();

    Rotor.Displacement = -0.3f; // m
    Rotor.TargetVelocityRad = 0;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
    Rotor.RotorLock = true;
    RotorAngle = 0;
    RotorVelocity = Rotor.GetProperty("Velocity").AsFloat();

    Hinges.ForEach(hinge => {

      // NOTE: Mind the blocks orientation! The 1x1 wheels (without suspension) connected to a hinge top part must fall
      // into the holes in the hull blocks when braking the `Rotor`.
      //
      // NOTE: This mechanic is currently buggy, but it is enough have at least one hinge in neutral position
      // (when wheel is up and not brakes the `Rotor`) and 4 hangar blocks connected to the `Rotor` base (stator) in
      // the form of a cross (in `SURROUNDING_DIRECTIONS`). No additional hinge control is needed other than this init.
      hinge.LowerLimitRad = 0;
      hinge.UpperLimitRad = MathHelper.PiOver2;
      hinge.TargetVelocityRad = -MathHelper.Pi; // rad/s, max

      hinge.Torque = 33000000; // N*m
      hinge.BrakingTorque = 0;
    });

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
  }

  void DisplayInitError() {
		LCD.ClearImagesFromSelection();
    LCD.AddImageToSelection("Cross");
		KeyLCD.ClearImagesFromSelection();
    KeyLCD.AddImageToSelection("Cross");
  }

  void DisplayStatus() {

    var gun_ready = GunReady(Gun) && Gun.Enabled;

    string state;
    if (gun_ready) state = "Gun Ready";
    else if (!PistonInPosition) state = "Sliding ...";
    else if (!RotorInPosition) state = "Rotating ...";
    else if (!RotorStopped) state = "Braking ...";
    else if (Gun == null || Gun.IsShooting) state = "Selecting Gun ...";
    else state = "Unknown state";

    var rotor_angle_abs = MathHelper.RoundToInt(MathHelper.ToDegrees(Rotor.Angle));
    var rotor_angle = MathHelper.RoundToInt(MathHelper.ToDegrees(RotorAngle));
    var target_angle = Math.Round(Rotor.UpperLimitDeg);
    var locked = Rotor.RotorLock ? "Locked" : "";

    LCD.WriteText(
      state+"\n"+
      "Barrel Level: "+CurrentBarrelLevel+"\n"+
      "Angle: "+rotor_angle_abs+" ("+rotor_angle+")"+"° / "+target_angle+"° "+locked
    );

		KeyLCD.ClearImagesFromSelection();
    KeyLCD.AddImageToSelection(gun_ready ? "Arrow" : "Danger");
  }

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

  Dictionary<int, IMyUserControllableGun> GunPlane { get {
    return Barrel[CurrentBarrelLevel];
  }}

  // Where should the `CurrentGun` be fired.
  Vector3D FireDirection { get {
    return Me.CubeGrid.WorldMatrix.Forward;
  }}

  Vector3D RotorDirection { get {
    // You can choose any direction in the rotation plane: Forward, Backward, Left, Right.
    // Will be used to get some angles relative to this direction.
    return Rotor.Top.WorldMatrix.Forward;
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

  // Angle (offset) between `FireDirection` and `RotorDirection` when `RotorAngle` is 0.
  float RotorToFireAngle() {
    // NOTE: `LimitPi` reflects the angle if needed.
    return Math.Abs(U.DirectionAngle(FireDirection, RotorDirection)) - Math.Abs(U.LimitPi(RotorAngle));
  }

  void Slide() {
    DisableGun();
    UpdatePistonVelocity();
  }

  void Rotate() {
    DisableGun();
    Rotor.TargetVelocityRad = TargetVelocity();
    Rotor.RotorLock = false;
  }

  void Brake() {
    DisableGun();
    Rotor.RotorLock = true;
    Rotor.TargetVelocityRad = 0;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
  }

  void DisableGun() {
    if (Gun != null) Gun.Enabled = false;
  }

  float TargetVelocity() {
    // TODO: decrease braking due to hinges?
    // TODO: refactoring

    var delta = Rotor.UpperLimitRad - RotorAngle;
    if (delta < -MathHelper.Pi) delta += MathHelper.TwoPi;

    // NOTE: the `delta` range is [-2π .. 2π] and the sinus here is stretched so:
    // `sin`(-2π) = -1, `sin`(π) = 1, `sin`(0) = 0; using a divisor 4.
    var sin = (float)Math.Sin(delta / 4);

    Rotor.Torque = MAX_ROTOR_TORQUE * Math.Abs(sin);
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE - Rotor.Torque;

    // NOTE: max rotor velocity is ±π rad/s and ±2π is used to increase the period of max `Rotor` velocity.
    return MathHelper.TwoPi * sin;
  }

  // NOTE: The `Rotor` is expected to be locked in a calibrated position - `ANGLE_CALIBRE`!
  void PrepareGun() {

    if (Gun == null) Gun = GunPlane[CalibratedAngle(Rotor.Angle)];

    if (GunReady(Gun)) Gun.Enabled = true;
    else SwitchGun();
  }

  // We have 4 gun positions on a circle (2π), so 2π / 4 = π/2 - is the angle calibre.
  int CalibratedAngle(float angle) {
    const int PER_CIRCLE = 4; // Like 2π limit, but for `int` calibre.
    return MathHelper.RoundToInt(Math.Abs(angle) / MathHelper.PiOver2) % PER_CIRCLE;
  }

  void SwitchGun() {

    DisableGun();
    Gun = null;

    // TODO: search for a closest gun in the entire system
    var available_angles = GunPlane.Where(i => GunReady(i.Value)).Select(i => i.Key).ToList();
    if (available_angles.Count > 0) SetRotorAngle(available_angles);
    else SetNextGunPlane();
  }

  bool GunReady(IMyUserControllableGun gun) {
    return gun != null && !gun.IsShooting && gun.IsFunctional;
  }

  void SetRotorAngle(List<int> angles) {
    var current_angle = CalibratedAngle(Rotor.Angle);
    var next_angle = angles.MinBy(angle => Math.Abs(current_angle - angle));
    RotorAngle = MathHelper.PiOver2 * next_angle;
  }

  void UpdatePistonVelocity() {
    if (Piston.MinLimit < Piston.MaxLimit) Piston.Velocity = Piston.MaxVelocity;
    else Piston.Velocity = -Piston.MaxVelocity;
  }

  void SetNextGunPlane() {
    CurrentBarrelLevel++;
    if (Barrel.Count <= CurrentBarrelLevel) CurrentBarrelLevel = 0;
    // NOTE: expects `GunPlanes` are close together! Thus we iterate through the planes with `BlockSize`.
    Piston.MaxLimit = CurrentBarrelLevel * BlockSize; // m
    UpdatePistonVelocity();
  }

  // Calibrated angle between `gun` *Forward* direction and `FireDirection` when `RotorAngle` is 0.
  // When this angle equals to `CalibratedAngle(Rotor.Angle)`, then given `gun` can fire.
  int GunWorkingAngle(IMyUserControllableGun gun) {
    var gun_to_rotor_angle = U.DirectionAngle(gun.WorldMatrix.Forward, RotorDirection);
    var gun_to_fire_angle = gun_to_rotor_angle + RotorToFireAngle();
    var flipped_fire_angle = MathHelper.TwoPi - gun_to_fire_angle; // Flip 0.5π and 1.5π
    return CalibratedAngle(flipped_fire_angle);
  }

  IEnumerable<Vector3I> UpByAxis() {
    // Start from `Zero`.
    var position = Vector3I.Zero;
    // Then go `Up` the axis.
    while (true) yield return position += Vector3I.Up;
  }

  // Positions surrounding the `center` in 4 `SURROUNDING_DIRECTIONS`.
  IEnumerable<Vector3I> SurroundingPositions(Vector3I center) {
    return SURROUNDING_DIRECTIONS.Select(direction => direction + center);
  }

  // Positions from the `center` in 4 `DIAGONAL_DIRECTIONS`.
  IEnumerable<Vector3I> DiagonalPositions(Vector3I center) {
    return DIAGONAL_DIRECTIONS.Select(direction => direction + center);
  }

  #endregion // RapidGun
}}