using System;
using System.Text;
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

  #region RapidGun

  // Gun System {{{
  IMyPistonBase Piston;
  IMyMotorAdvancedStator Rotor;
  IMyUserControllableGun CurrentGun;
  List<List<IMyUserControllableGun>> GunPlanes;
  int CurrentPlaneIdx;
  List<IMyMotorSuspension> Wheels; // Expects 1x1 wheels in diagonal barrel positions (adjacent to guns).
  // Gun System }}}

  float BlockSize; // m
  ITerminalProperty<float> Velocity;

  // Precision to compare the direction of 3D vectors.
  const double EPSILON = 0.006;

  const float MAX_ROTOR_TORQUE = 1000000000; // N*m

  // Directions on a 2D square grid.
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
    MathHelper.TwoPi
  });

  StringBuilder Status = new StringBuilder();
  IMyTextSurface LCD, KeyLCD;

  Program() {

		LCD = Me.GetSurface(0);
		LCD.ContentType = ContentType.TEXT_AND_IMAGE;
    LCD.BackgroundColor = Color.Black;

		KeyLCD = Me.GetSurface(1);
		KeyLCD.ContentType = ContentType.TEXT_AND_IMAGE;
    KeyLCD.BackgroundColor = Color.Black;

    if (BlockGroups().FirstOrDefault(SetGunSystem) != null) InitGunSystem();
  }

  void Main(string argument, UpdateType updateSource) {

    if (!PistonInPosition) Slide();
    else if (!RotorInPosition) Rotate();
    else if (!RotorStopped) Brake();
    else PrepareGun();

    IndicateStatus();
    DisplayStatus();
  }

  Vector3D FireDirection { get {
    return Me.CubeGrid.WorldMatrix.Forward;
  }}

  bool PistonInPosition { get {
    return Piston.CurrentPosition == (Piston.Velocity < 0 ? Piston.MinLimit : Piston.MaxLimit);
  }}

  bool RotorInPosition { get {
    return Rotor.Angle == Rotor.UpperLimitRad;
  }}

  bool RotorStopped { get {
    return Rotor.RotorLock && Velocity.GetValue(Rotor) == 0;
  }}

  List<IMyUserControllableGun> CurrentGunPlane { get {
    return GunPlanes[CurrentPlaneIdx];
  }}

  IMyUserControllableGun FindGunInCurrentFireDirection() {
    return CurrentGunPlane.Find(gun => gun.WorldMatrix.Forward.Equals(FireDirection, EPSILON));
  }

  void Slide() {
    DisableCurrentGun();
    // TODO:
  }

  void Rotate() {
    DisableCurrentGun();
    Rotor.TargetVelocityRad = TargetVelocity();
    Rotor.RotorLock = false;
  }

  void Brake() {
    Rotor.RotorLock = true;
    Rotor.TargetVelocityRad = 0;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
  }

  void DisableCurrentGun() {
    if (CurrentGun != null) CurrentGun.Enabled = false;
  }

  float TargetVelocity() {

    // NOTE: `Rotor.Angle` can exceed 2π more than 2 times, but it can't be seen in the in-game rotor properties!
    var current_angle = Limit2Pi(Rotor.Angle);

    var delta = Rotor.UpperLimitRad - current_angle;
    if (delta < -MathHelper.Pi) delta += MathHelper.TwoPi;

    // TODO: adapt to wheels
    var k = (float)Math.Sin(delta * delta / MathHelper.TwoPi);

    // TODO: move
    // TODO: adapt to wheels
    Rotor.Torque = MAX_ROTOR_TORQUE * Math.Abs(k);
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE - Rotor.Torque - 40000;

    // TODO: adapt to wheels
    // NOTE: max rotor velocity is ±π rad/s
    return MathHelper.Pi * MathHelper.Pi * k;
  }

  void PrepareGun() {
    if (CurrentGun == null) CurrentGun = FindGunInCurrentFireDirection();
    if (CurrentGun == null) return; // Probably the swing amplitude is too big.
    if (CurrentGun.IsShooting) {
      CurrentGun.Enabled = false;
      CurrentGun = null;
      SetNextGun();
    } else {
      CurrentGun.Enabled = true;
    }
  }

  void SetNextGun() {
    var ready_guns = CurrentGunPlane.FindAll(gun => !gun.IsShooting);
    if (ready_guns.Count > 0) SetNextRotorAngle(ready_guns);
    else SetNextGunPlane();
  }

  void SetNextRotorAngle(List<IMyUserControllableGun> guns) {

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

  float AngleCalibre(float radians) {
    var angle = ANGLE_CALIBRE.MinBy(calibre => Math.Abs(calibre - radians));
    return angle < MathHelper.TwoPi ? angle : 0;
  }

  void SetNextGunPlane() {
    // TODO:
  }

  void InitGunSystem() {

    BlockSize = Me.CubeGrid.GridSize;
    Velocity = Rotor.GetProperty("Velocity").AsFloat();

    Piston.MinLimit = 0;
    Piston.MaxLimit = 0;
    Piston.Velocity = -Piston.MaxVelocity;

    Rotor.Displacement = -0.3f; // m
    Rotor.TargetVelocityRad = 0;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
    Rotor.RotorLock = true;
    BarrelAngle = 0;

    GunPlanes.ForEach(plane => plane.ForEach(gun => gun.Enabled = false));
    CurrentPlaneIdx = 0;
    CurrentGun = null;

    // TODO: tweak
    Wheels.ForEach(wheel => {
      wheel.Height = 0.3f; // m
    });

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
  }

  bool SetGunSystem(IMyBlockGroup group) {

    var me = Select<IMyProgrammableBlock>(group, block => block == Me).SingleOrDefault();
    if (me == null) return false;

    Piston = Select<IMyPistonBase>(group).SingleOrDefault();
    if (Piston == null) return false;

    Rotor = Select<IMyMotorAdvancedStator>(group).SingleOrDefault();
    if (Rotor == null) return false;

    return SetBarrel(Rotor.TopGrid);
  }

  bool SetBarrel(IMyCubeGrid barrel) {

    GunPlanes = GoUp()
      .Select(SurroundingPositions)
      .Select(positions => Select<IMyUserControllableGun>(barrel, positions).ToList())
      .TakeWhile(guns => guns.Count > 0)
      .Reverse()
      .ToList();

    if (GunPlanes.Count <= 0) return false;

    Wheels = GoUp()
      .Select(DiagonalPositions)
      .Select(positions => Select<IMyMotorSuspension>(barrel, positions).ToList())
      .TakeWhile(wheels => wheels.Count > 0)
      .SelectMany(wheels => wheels)
      .ToList();

    return true;
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

  // Positions near the `center` in 4 `DIAGONAL_DIRECTIONS`.
  IEnumerable<Vector3I> DiagonalPositions(Vector3I center) {
    return DIAGONAL_DIRECTIONS.Select(direction => direction + center);
  }

  void IndicateStatus() {
    var image = (CurrentGun != null && CurrentGun.Enabled) ? "Arrow" : "Danger";
		KeyLCD.ClearImagesFromSelection();
    KeyLCD.AddImageToSelection(image);
  }

  void DisplayStatus() {
    var current_angle = Math.Round(MathHelper.ToDegrees(Rotor.Angle));
    var target_angle = Math.Round(Rotor.UpperLimitDeg);
    var locked = Rotor.RotorLock ? "Locked" : "";
    Status.Clear();
    Status.Append("Plane: "+CurrentPlaneIdx+"; Angle: "+current_angle+" / "+target_angle+"° "+locked+"\n");
    LCD.WriteText(Status);
  }

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

  /*
   * Returns radians in the range [0 .. 2Pi].
   * NOTE: similar function from `MathHelper` do not work!
   */
  float Limit2Pi(float radians) {
    var arc = radians % MathHelper.TwoPi;
    if (arc < 0) arc += MathHelper.TwoPi;
    return arc;
  }

  #endregion // RapidGun
}}