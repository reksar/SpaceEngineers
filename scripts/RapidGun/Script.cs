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

  // Precision to compare the direction of 3D vectors.
  const double EPSILON = 0.006;

  ImmutableList<Vector3I> RING_DIRECTIONS = ImmutableList.Create(new Vector3I[] {
    Vector3I.Forward, Vector3I.Right, Vector3I.Backward, Vector3I.Left
  });

  // 0°, 90°, 180°, 270°, 360°
  ImmutableList<float> ANGLE_CALIBRE = ImmutableList.Create(new float[] {
    0, MathHelper.PiOver2, MathHelper.Pi, 3 * MathHelper.PiOver2, MathHelper.TwoPi
  });

  IMyPistonBase Piston;
  IMyMotorAdvancedStator Rotor;
  List<List<IMyUserControllableGun>> GunRings = new List<List<IMyUserControllableGun>>();
  IMyUserControllableGun CurrentGun;
  int CurrentRingIdx;
  float BlockSize; // m

  StringBuilder Status = new StringBuilder();
  IMyTextSurface LCD;
  string SavedStatus = "";

  Program() {

		LCD = Me.GetSurface(0);
		LCD.ContentType = ContentType.TEXT_AND_IMAGE;
    LCD.BackgroundColor = Color.Black;

    SelectBlocks();

    if (GunRings.Count > 0) Init();
  }

  void Main(string argument, UpdateType updateSource) {

    Status.Clear();

    if (!PistonInPosition) Slide();
    else if (!RotorInPosition) Rotate();
    else PrepareGun();

    Status.Append(SavedStatus);
    LCD.WriteText(Status);
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

  IMyUserControllableGun FindGunInCurrentFireDirection() {
    return GunRings[CurrentRingIdx].Find(gun => gun.WorldMatrix.Forward.Equals(FireDirection, EPSILON));
  }

  void Slide() {
    Status.Append("Sliding ...\n");
    if (CurrentGun != null) CurrentGun.Enabled = false;
    // TODO:
  }

  void Rotate() {
    Status.Append("Rotating: ");
    if (CurrentGun != null) CurrentGun.Enabled = false;
    Rotor.TargetVelocityRad = TargetVelocity();
    Rotor.RotorLock = false;
  }

  float TargetVelocity() {

    // NOTE: `Rotor.Angle` can exceed 2π more than 2 times, but it can't be seen in the in-game rotor properties!
    var current_angle = Limit2Pi(Rotor.Angle);

    var delta = Rotor.UpperLimitRad - current_angle;
    if (delta < -MathHelper.Pi) delta += MathHelper.TwoPi;

    Status.Append(Math.Round(Rotor.UpperLimitRad, 2)+" - "+Math.Round(current_angle, 2)+" = ");
    Status.Append(Math.Round(delta, 2)+" rad\n");

    var k = (float)Math.Sin(delta * delta / MathHelper.TwoPi);

    // TODO: move
    Rotor.Torque = 1000000000 * Math.Abs(k);
    Rotor.BrakingTorque = 1000000000 - Rotor.Torque - 40000;

    // NOTE: max rotor velocity is ±π rad/s
    return MathHelper.Pi * k * 3;
  }

  void PrepareGun() {
    Rotor.RotorLock = true;
    Rotor.TargetVelocityRad = 0;
    if (CurrentGun == null) CurrentGun = FindGunInCurrentFireDirection();
    if (CurrentGun == null) return; // Probably the swing amplitude is too big.
    if (CurrentGun.IsShooting) {
      CurrentGun.Enabled = false;
      CurrentGun = null;
      SetNextGun();
    } else {
      Status.Append("Ready: "+Math.Round(Rotor.Angle, 2)+" rad\n");
      CurrentGun.Enabled = true;
    }
  }

  void SetNextGun() {
    var ready_guns = GunRings[CurrentRingIdx].FindAll(gun => !gun.IsShooting);
    if (ready_guns.Count > 0) SetNextRotorAngle(ready_guns);
    else SetNextRing();
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

    Status.Append("\nSelected Gun: "+Math.Round(closest_gun_angle, 2)+" << ");
    foreach (var angle in gun_angles) Status.Append(Math.Round(angle, 2)+" ");
    Status.Append("\nSelected Angle: "+Math.Round(next_angle, 2)+"\n");

    Status.Append("Raw: "+next_angle_raw+"\n");
    Status.Append("Limited: "+next_angle_limited+"\n");

    SavedStatus = Status.ToString();

    SetRotorLimitRad(next_angle);
  }

  void SetRotorLimitRad(float radians) {
    if (radians < Rotor.LowerLimitRad) {
      Rotor.LowerLimitRad = radians;
      Rotor.UpperLimitRad = radians;
    } else {
      Rotor.UpperLimitRad = radians;
      Rotor.LowerLimitRad = radians;
    }
  }

  float AngleCalibre(float radians) {
    var angle = ANGLE_CALIBRE.MinBy(calibre => Math.Abs(calibre - radians));
    return angle < MathHelper.TwoPi ? angle : 0;
  }

  void SetNextRing() {
    // TODO:
  }

  void Init() {

    Runtime.UpdateFrequency = UpdateFrequency.Update10;

    BlockSize = Me.CubeGrid.GridSize;

    Piston.MinLimit = 0;
    Piston.MaxLimit = 0;
    Piston.Velocity = -Piston.MaxVelocity;

    Rotor.TargetVelocityRad = 0;
    Rotor.Displacement = -0.3f; // m
    Rotor.Torque = 2000000; // N*m
    Rotor.BrakingTorque = 500000000; // N*m
    SetRotorLimitRad(0);
    Rotor.RotorLock = true;

    GunRings.ForEach(ring => ring.ForEach(gun => gun.Enabled = false));
    CurrentRingIdx = 0;
    CurrentGun = null;
  }

   void SelectBlocks() {

    var my_groups = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(my_groups, group =>
      Select<IMyProgrammableBlock>(group, block => block == Me).Count == 1);

    var piston_and_rotor = my_groups
      .Select(group => MyTuple.Create(
        Select<IMyPistonBase>(group).FirstOrDefault(),
        Select<IMyMotorAdvancedStator>(group).FirstOrDefault()
      ))
      .FirstOrDefault(tuple => tuple.Item1 != null && tuple.Item2 != null);

    Piston = piston_and_rotor.Item1;
    Rotor = piston_and_rotor.Item2;

    SelectGuns();
  }

  void SelectGuns() {

    if (Rotor == null) return;

    var grid = Rotor.TopGrid;
    var axis = Vector3I.Up;
    var guns = GunRing(grid, axis);

    while (guns.Count > 0) {
      GunRings.Add(guns);
      axis += Vector3I.Up;
      guns = GunRing(grid, axis);
    }

    GunRings.Reverse();
  }

  List<IMyUserControllableGun> GunRing(IMyCubeGrid grid, Vector3I axis) {
    return RING_DIRECTIONS
      .Select(direction => grid.GetCubeBlock(axis + direction))
      .Where(block => block != null)
      .Select(block => block.FatBlock as IMyUserControllableGun)
      .Where(gun => gun != null)
      .ToList();
  }

  List<T> Select<T>(IMyBlockGroup group, Func<T, bool> filter = null) where T : class {
		var blocks = new List<T>();
		group.GetBlocksOfType<T>(blocks, filter);
		return blocks;
	}

  /*
   * Returns radians in the range [0 .. 2Pi].
   * NOTE: similar function from `MathHelper` do not work!
   */
  float Limit2Pi(float radians) {
    return radians < 0 ? (radians % MathHelper.TwoPi + MathHelper.TwoPi) : (radians % MathHelper.TwoPi);
  }

  #endregion // RapidGun
}}