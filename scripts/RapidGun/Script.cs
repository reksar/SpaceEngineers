﻿using System;
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
  // Gun System }}}

  float BlockSize; // m

  // Precision to compare the direction of 3D vectors.
  const double EPSILON = 0.006;

  const float MAX_ROTOR_TORQUE = 1000000000; // N*m

  // Directions on a 2D plane.
  ImmutableList<Vector3I> PLANE_DIRECTIONS = ImmutableList.Create(new Vector3I[] {
    Vector3I.Forward, Vector3I.Right, Vector3I.Backward, Vector3I.Left
  });

  // Represents the working positions of the gun barrel: 0°, 90°, 180°, 270°, 360°.
  ImmutableList<float> ANGLE_CALIBRE = ImmutableList.Create(new float[] {
    0, MathHelper.PiOver2, MathHelper.Pi, 3 * MathHelper.PiOver2, MathHelper.TwoPi
  });

  StringBuilder Status = new StringBuilder();
  IMyTextSurface LCD;
  string SavedStatus = "";

  Program() {

		LCD = Me.GetSurface(0);
		LCD.ContentType = ContentType.TEXT_AND_IMAGE;
    LCD.BackgroundColor = Color.Black;

    if (BlockGroups().FirstOrDefault(SetGunSystem) != null) Init();
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

  List<IMyUserControllableGun> CurrentGunPlane { get {
    return GunPlanes[CurrentPlaneIdx];
  }}

  IMyUserControllableGun FindGunInCurrentFireDirection() {
    return CurrentGunPlane.Find(gun => gun.WorldMatrix.Forward.Equals(FireDirection, EPSILON));
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
    Rotor.Torque = MAX_ROTOR_TORQUE * Math.Abs(k);
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE - Rotor.Torque - 40000;

    // NOTE: max rotor velocity is ±π rad/s
    return MathHelper.Pi * MathHelper.Pi * k;
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

    Status.Append("\nSelected Gun: "+Math.Round(closest_gun_angle, 2)+" << ");
    foreach (var angle in gun_angles) Status.Append(Math.Round(angle, 2)+" ");
    Status.Append("\nSelected Angle: "+Math.Round(next_angle, 2)+"\n");

    Status.Append("Raw: "+next_angle_raw+"\n");
    Status.Append("Limited: "+next_angle_limited+"\n");

    SavedStatus = Status.ToString();

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

  void Init() {

    BlockSize = Me.CubeGrid.GridSize;

    Piston.MinLimit = 0;
    Piston.MaxLimit = 0;
    Piston.Velocity = -Piston.MaxVelocity;

    Rotor.Displacement = -0.3f; // m
    Rotor.TargetVelocityRad = 0;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
    Rotor.RotorLock = true;
    BarrelAngle = 0;

    GunPlanes.ForEach(ring => ring.ForEach(gun => gun.Enabled = false));
    CurrentPlaneIdx = 0;
    CurrentGun = null;

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
  }

  bool SetGunSystem(IMyBlockGroup group) {

    var me = Select<IMyProgrammableBlock>(group, block => block == Me);
    if (me.Count != 1) return false;

    var pistons = Select<IMyPistonBase>(group);
    if (pistons.Count != 1) return false;

    var rotors = Select<IMyMotorAdvancedStator>(group);
    if (rotors.Count != 1) return false;

    Piston = pistons.First();
    Rotor = rotors.First();
    GunPlanes = GoUp().Select(Guns).TakeWhile(guns => guns.Count > 0).Reverse().ToList();

    return GunPlanes.Count > 0;
  }

  List<IMyUserControllableGun> Guns(Vector3I plane_center) {
    return PLANE_DIRECTIONS
      .Select(direction => Rotor.TopGrid.GetCubeBlock(plane_center + direction)).Where(block => block != null)
      .Select(block => block.FatBlock as IMyUserControllableGun).Where(gun => gun != null)
      .ToList();
  }

  IEnumerable<Vector3I> GoUp() {
    var position = Vector3I.Zero;
    while (true) yield return position += Vector3I.Up;
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