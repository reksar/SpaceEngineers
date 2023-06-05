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

  // The pivot of the entire gun system. Sliding the piston will change the `Barrel` level.
  IMyPistonBase Piston;

  // Attached to the `Piston` top and holds the gun `Barrel`. Rotating the `Rotor` will change the `Gun` within the
  // `CurrentBarrelLevel`.
  IMyMotorAdvancedStator Rotor;

  // `Rotor` top grid. Consists of identical levels with radial symmetry: a gun in each of 4 base directions on the
  // plane.
  List<List<IMyUserControllableGun>> Barrel;
  int CurrentBarrelLevel;

  // Current active gun in `FireDirection`.
  IMyUserControllableGun Gun; 

  // Hinges and attached wheels are used to brake the `Rotor`.
  // NOTE: this mechanic is currently buggy (see `InitGunSystem`).
  // NOTE: There is no interface for the *hinge* terminal block, but `IMyMotorStator` should do.
  List<IMyMotorStator> Hinges;

  // Will be different for *large* and *small* grids.
  float BlockSize; // m

  ITerminalProperty<float> RotorVelocity;
  IMyTextSurface LCD, KeyLCD;

  const float MAX_ROTOR_TORQUE = 1000000000; // N*m

  // Directions on a 2D square grid representing a gun plane - the barrel cross section.
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

  // Represents the working positions of the gun barrel: 0°, 90°, 180°, 270°, 360°.
  ImmutableList<float> ANGLE_CALIBRE = ImmutableList.Create(new float[] {
    0,
    MathHelper.PiOver2,
    MathHelper.Pi,
    3 * MathHelper.PiOver2,
    MathHelper.TwoPi // TODO: try to do without this item
  });

  Program() {

    InitLCD();

    if (BlockGroups().FirstOrDefault(SetGunSystem) != null) InitGunSystem();
    // TODO: display init error
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

  void InitLCD() {

		LCD = Me.GetSurface(0);
		LCD.ContentType = ContentType.TEXT_AND_IMAGE;
    LCD.BackgroundColor = Color.Black;
    LCD.WriteText("");

		KeyLCD = Me.GetSurface(1);
		KeyLCD.ContentType = ContentType.TEXT_AND_IMAGE;
    KeyLCD.BackgroundColor = Color.Black;
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

  List<IMyUserControllableGun> GunPlane { get {
    return Barrel[CurrentBarrelLevel];
  }}

  bool GunReady { get {
    return Gun != null && Gun.Enabled && !Gun.IsShooting;
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

  // `Rotor.Angle` can exceed 2π several times, but this is not visible in the in-game rotor properties!
  float RotorAngle { get {
    return Limit2Pi(Rotor.Angle); // [0 .. 2π]
  }}

  // Angle (offset) between `FireDirection` and `RotorDirection` when `RotorAngle` is 0.
  float RotorToFireAngle() {
    // NOTE: `LimitPi` reflects the angle if needed.
    return Math.Abs(DirectionAngle(FireDirection, RotorDirection)) - Math.Abs(LimitPi(RotorAngle));
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

  // NOTE: The `Rotor` is expected is locked in a calibrated position - `ANGLE_CALIBRE`!
  void PrepareGun() {

    if (Gun == null) SetGun();

    // TODO: check if null or not working.
    if (Gun.IsShooting) SwitchGun();
    else Gun.Enabled = true;
  }

  void SetGun() {

    // According to the order of `GunBaseAngle`.
    var gun_idx = MathHelper.RoundToInt(RotorAngle / MathHelper.PiOver2);

    Gun = Barrel[CurrentBarrelLevel][gun_idx];
  }

  void SwitchGun() {
    DisableGun();
    Gun = null;
    // TODO: change plane if ready guns on current plane are not available in [-π/2 .. π/2] rad, but are available on
    // the next plane.
    var ready_guns = GunPlane.FindAll(gun => !gun.IsShooting);
    if (ready_guns.Count > 0) SetNextRotorAngle(ready_guns);
    else SetNextGunPlane();
  }

  void SetNextRotorAngle(List<IMyUserControllableGun> guns) {
    // TODO: refactoring

    // Precalculate this first, because it can change quickly as the grid quickly rotates.
    var fire_direction = FireDirection;
    var gun_directions = guns.Select(gun => gun.WorldMatrix.Forward).ToImmutableList();

    // Then this will be calculated with the min deviation.
    var gun_angles = gun_directions.Select(gun_direction => {

      // The direction vectors must be normalized, so the `Dot` product is enough instead of `MyMath.CosineDistance`.
      var dot = (float)fire_direction.Dot(gun_direction);

      // `dot` may be slightly out of the `cos` range due to floating point numbers, so we `Clamp` it to prevent
      // getting the NaN from `Acos`.
      var cos = MyMath.Clamp(dot, -1, 1);

      var angle = (float)Math.Acos(cos);

      return cos < 0 ? MathHelper.TwoPi - angle : angle; 
    }).ToArray();

    var closest_gun_angle = gun_angles.MinBy(angle => {
      var delta = Math.Abs(Rotor.Angle - angle);
      // Ideally `delta == 0 ?`, but floating point numbers cause problems.
      return delta < MathHelper.PiOver4 ? MathHelper.TwoPi : delta;
    });

    var next_angle_raw = Rotor.Angle + Math.Abs(Rotor.Angle - closest_gun_angle);
    var next_angle_limited = Limit2Pi(next_angle_raw);
    var next_angle = AngleCalibre(next_angle_limited);

    BarrelAngle = next_angle;
  }

  float BarrelAngle { set {
    if (value < Rotor.LowerLimitRad) {
      Rotor.LowerLimitRad = value;
      Rotor.UpperLimitRad = value;
    } else {
      Rotor.UpperLimitRad = value;
      Rotor.LowerLimitRad = value;
    }
  }}

  void UpdatePistonVelocity() {
    if (Piston.MinLimit < Piston.MaxLimit) Piston.Velocity = Piston.MaxVelocity;
    else Piston.Velocity = -Piston.MaxVelocity;
  }

  float AngleCalibre(float radians) {
    var angle = ANGLE_CALIBRE.MinBy(calibre => Math.Abs(calibre - radians));
    return angle < MathHelper.TwoPi ? angle : 0;
  }

  void SetNextGunPlane() {
    CurrentBarrelLevel++;
    if (Barrel.Count <= CurrentBarrelLevel) CurrentBarrelLevel = 0;
    // NOTE: expects `GunPlanes` are close together! Thus we iterate through the planes with `BlockSize`.
    Piston.MaxLimit = CurrentBarrelLevel * BlockSize; // m
    UpdatePistonVelocity();
  }

  void InitGunSystem() {

    Barrel.ForEach(level => level.ForEach(gun => gun.Enabled = false));
    CurrentBarrelLevel = 0;
    Gun = null;

    Piston.MinLimit = 0; // m
    Piston.MaxLimit = 0; // m
    BlockSize = Me.CubeGrid.GridSize; // m
    UpdatePistonVelocity();

    Rotor.Displacement = -0.3f; // m
    Rotor.TargetVelocityRad = 0;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
    Rotor.RotorLock = true;
    BarrelAngle = 0;
    RotorVelocity = Rotor.GetProperty("Velocity").AsFloat();

    Hinges.ForEach(hinge => {

      // NOTE: mind the blocks orientation! The 1x1 wheels (without suspension) connected to a hinge top part must fall
      // into the holes in the hull blocks when braking the `Rotor`.
      // NOTE: this mechanic is currently buggy, but it is enough have at leat one hinge in neutral position
      // (when wheel is up and not brakes the `Rotor`) and 4 hangar blocks connected to the `Rotor` base (stator) in
      // the form of a cross (in `SURROUNDING_DIRECTIONS`). No additional hinge control is needed other than init here.
      hinge.LowerLimitRad = 0;
      hinge.UpperLimitRad = MathHelper.PiOver2;
      hinge.TargetVelocityRad = -MathHelper.Pi; // rad/s, max

      hinge.Torque = 33000000; // N*m
      hinge.BrakingTorque = 0;
    });

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
  }

  bool SetGunSystem(IMyBlockGroup group) {

    var me = Select<IMyProgrammableBlock>(group, block => block == Me).FirstOrDefault();
    if (me == null) return false;

    Piston = Select<IMyPistonBase>(group).FirstOrDefault();
    if (Piston == null) return false;

    Rotor = Select<IMyMotorAdvancedStator>(group).FirstOrDefault();
    if (Rotor == null) return false;

    return SetBarrel(Rotor.TopGrid);
  }

  bool SetBarrel(IMyCubeGrid barrel) {

    Barrel = GoUp()
      .Select(SurroundingPositions)
      .Select(positions => Select<IMyUserControllableGun>(barrel, positions).OrderBy(GunBaseAngle).ToList())
      .TakeWhile(guns => guns.Count > 0)
      .Reverse()
      .ToList();

    if (Barrel.Count <= 0) return false;

    Hinges = GoUp()
      .Select(DiagonalPositions)
      .Select(positions => Select<IMyMotorStator>(barrel, positions).ToList())
      .TakeWhile(hinges => hinges.Count > 0)
      .SelectMany(hinges => hinges)
      .ToList();

    return Hinges.Count > 0;
  }

  // Angle between `gun` *Forward* direction and `FireDirection` when `RotorAngle` is 0.
  float GunBaseAngle(IMyUserControllableGun gun) {
    return Limit2Pi(DirectionAngle(gun.WorldMatrix.Forward, RotorDirection) + RotorToFireAngle());
  }

  IEnumerable<Vector3I> GoUp() {
    // Start at the position of the attachable block, e.g. rotor top.
    var position = Vector3I.Zero;
    // Then go up the axis.
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

  void DisplayStatus() {

    var gun_ready = GunReady;

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

  // TODO: group {{{
  List<IMyBlockGroup> BlockGroups(Func<IMyBlockGroup, bool> filter = null) {
    var groups = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(groups, filter);
    return groups;
  }

  List<T> Select<T>(IMyBlockGroup group, Func<T, bool> filter = null) where T : class {
		var blocks = new List<T>();
		group.GetBlocksOfType<T>(blocks, filter);
		return blocks;
	}

  IEnumerable<T> Select<T>(IMyCubeGrid grid, IEnumerable<Vector3I> positions) where T : class {
    return positions
      .Select(position => grid.GetCubeBlock(position)).OfType<IMySlimBlock>()
      .Select(block => block.FatBlock as T).OfType<T>();
  }

  // Angle between normalized direction vectors `a` and `b` in range [-π .. π].
  float DirectionAngle(Vector3D a, Vector3D b) {

    // `Dot` product is enough for normalized vectors. Instead of `MyMath.CosineDistance`.
    var dot = (float)a.Dot(b);

    // `dot` may be slightly out of the `cos` range due to floating point numbers.
    // `Clamp` prevents further receipt of NaN from `Acos`.
    var cos = MyMath.Clamp(dot, -1, 1);

    var angle = (float)Math.Acos(cos);

    return CopySign(angle, cos);
  }

  float CopySign(float value, float sign) {
    return (value < 0) == (sign < 0) ? value : -value;
  }

  // Returns radians in the range [0 .. 2π].
  // NOTE: similar function from `MathHelper` do not work!
  float Limit2Pi(float radians) {
    var arc = radians % MathHelper.TwoPi;
    if (arc < 0) arc += MathHelper.TwoPi;
    return arc;
  }

  // Returns radians in the range [-π .. π].
  // NOTE: similar function from `MathHelper` do not work!
  float LimitPi(float radians) {
    if (radians > MathHelper.Pi) return radians % MathHelper.Pi - MathHelper.Pi;
    if (radians < -MathHelper.Pi) return radians % MathHelper.Pi + MathHelper.Pi;
    return radians;
  }
  // TODO: group }}}

  #endregion // RapidGun
}}