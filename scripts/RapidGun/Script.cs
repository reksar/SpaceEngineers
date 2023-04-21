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

  // Precision to compare direction 3D vectors.
  const double EPSILON = 0.006;

  ImmutableList<Vector3I> RING_DIRECTIONS = ImmutableList.Create(new Vector3I[] {
    Vector3I.Forward, Vector3I.Right, Vector3I.Backward, Vector3I.Left
  });

  ImmutableList<float> ANGLE_CALIBRE = ImmutableList.Create(new float[] {
    0, MathHelper.PiOver2, MathHelper.Pi, 3 * MathHelper.PiOver2, MathHelper.TwoPi
  });

  IMyPistonBase Piston;
  IMyMotorAdvancedStator Rotor;
  List<List<IMyUserControllableGun>> GunRings = new List<List<IMyUserControllableGun>>();
  IMyUserControllableGun CurrentGun;
  int CurrentRingIdx;
  float BlockSize; // m

  Program() {
    SelectBlocks();
    if (GunRings.Count > 0) Init();
  }

  void Main(string argument, UpdateType updateSource) {
    if (!PistonInPosition) {
      Echo("piston");
      if (CurrentGun != null) CurrentGun.Enabled = false;
    } else if (!RotorInPosition) {
      Echo("rotor");
      if (CurrentGun != null) CurrentGun.Enabled = false;
      Rotor.TargetVelocityRad = MathHelper.Pi; // TODO: variable
      Rotor.RotorLock = false;
    } else PrepareGun();
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

  void PrepareGun() {
    Echo("stopped");
    Rotor.RotorLock = true;
    Rotor.TargetVelocityRad = 0;
    if (CurrentGun == null) CurrentGun = FindGunInCurrentFireDirection();
    if (CurrentGun == null) return; // Probably the swing amplitude is too big.
    if (CurrentGun.IsShooting) {
      CurrentGun.Enabled = false;
      CurrentGun = null;
      SetNextGun();
    } else CurrentGun.Enabled = true;
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

    // Then this will be calculated with the min delta.
    var gun_angles = gun_directions.Select(gun_direction => {

      // The direction vectors must be normalized, so the `Dot` product is enough instead of `MyMath.CosineDistance`.
      var dot = (float)fire_direction.Dot(gun_direction);

      // `dot` may be slightly out of the `cos` range due to floating point numbers, so we `Clamp` it to prevent
      // getting the NaN from `Acos`.
      var cos = MyMath.Clamp(dot, -1, 1);
      return (float)CopySign(Math.Acos(cos), cos);
    });

    var closest_gun_angle = gun_angles.MinBy(Math.Abs);
    var next_angle = AngleCalibre(Limit2Pi(closest_gun_angle + Rotor.Angle));
    Rotor.LowerLimitRad = next_angle;
    Rotor.UpperLimitRad = next_angle;
  }

  float AngleCalibre(float angle) {
    var angle_calibre = ANGLE_CALIBRE.MinBy(calibre => Math.Abs(calibre - angle));
    return angle_calibre == MathHelper.TwoPi ? 0 : angle_calibre;
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
    Rotor.LowerLimitRad = 0;
    Rotor.UpperLimitRad = 0;
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
   * NOTE: similar functions from `MathHelper` do not work!
   */
  float Limit2Pi(float radians) {
    return radians < 0 ? (radians % MathHelper.TwoPi + MathHelper.TwoPi) : (radians % MathHelper.TwoPi);
  }

  /*
   * Returns a value with the `magnitude` and the `sign`.
   * NOTE: `Math.CopySign` is not allowed inside a script!
   */
  double CopySign(double magnitude, double sign) {
    return IsNegative(magnitude) == IsNegative(sign) ? magnitude : -magnitude;
  }

  bool IsNegative(double value) {
    return Math.Sign(value) == -1;
  }

  #endregion // RapidGun
}}