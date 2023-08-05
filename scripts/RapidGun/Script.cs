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
      return positions.Select(grid.GetCubeBlock).OfType<IMySlimBlock>()
        .Select(block => block.FatBlock as T).OfType<T>();
    }

    // Returns radians in the range [0 .. 2π].
    // NOTE: similar function from `MathHelper` do not work!
    public static float Limit2Pi(float radians) {
      var arc = radians % MathHelper.TwoPi;
      return (arc < 0) ? (arc + MathHelper.TwoPi) : arc;
    }

    // Positions surrounding the `center` in 4 base directions.
    public static IEnumerable<Vector3I> CrissCrossPositions(Vector3I center) {
      yield return Vector3I.Forward + center;
      yield return Vector3I.Right + center;
      yield return Vector3I.Backward + center;
      yield return Vector3I.Left + center;
    }

    public static IEnumerable<Vector3I> EndlessUp() {
      var position = Vector3I.Zero; // Initial value will not be yielded!
      while (true) yield return position += Vector3I.Up;
    }
  }

  Program() {
    InitLCDs();
    if (SetGunSystem()) InitGunSystem(); else DisplayInitError();
  }

  // Will run with the `UpdateFrequency` set at the end of `InitGunSystem`. Or won't run on init fail.
  void Main(string argument, UpdateType updateSource) {

    var need_to_slide = !PistonInPosition;
    var need_to_rotate = !RotorInPosition;

    if (need_to_slide) Slide();
    if (need_to_rotate) Rotate();

    if (!(need_to_slide || need_to_rotate)) {
      if (RotorStopped) PrepareGun(); else Brake();
    }

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

    RotorVelocity = Rotor.GetProperty("Velocity").AsFloat(); // rad/s
    Rotor.Displacement = -0.3f; // m
    RotorAngle = 0; // rad
    StopRotor();

    Piston.GetProperty("MaxImpulseAxis").AsFloat().SetValue(Piston, 100000); // N
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

  // The `Rotor` is expected to be locked in a calibrated position (see `RotorQuarter`)!
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
      ChangePistonPosition(level);
      ChangeRotorPosition(quarter);
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
    if (Gun != null) {
      Gun.Enabled = false;
      Gun = null;
    }
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

  // Faster alternative for `Quarter(Base6Directions.Direction.Forward)`.
  int ForwardQuarter() {
    return RotorQuarter() % QUARTERS_TOTAL;
  }

  // There are 4 calibrated angles of the `Rotor` where it locked: 0, 0.5π, π, 1.5π.
  // They correspond to 4 quarters of a circle: 0 / 0.5π = 0, 0.5π / 0.5π = 1, π / 0.5π = 2, 1.5π / 0.5π = 3.
  int RotorQuarter() {
    return MathHelper.Floor(RotorAngle / MathHelper.PiOver2);
  }

  // The global `Piston` has not been set yet, so pass it in the `piston` arg.
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
    DisplayImage(LCD, status_image);
  }

  void DisplayImage(IMyTextSurface lcd, string image) {
    lcd.WriteText("");
    lcd.ClearImagesFromSelection();
    lcd.AddImageToSelection(image);
  }

  IEnumerable<int> BarrelLevels { get {
    return Enumerable.Range(0, Barrel.Count);
  }}

  IEnumerable<int> Quarters { get {
    return Enumerable.Range(0, QUARTERS_TOTAL);
  }}

  bool PistonInPosition { get {
    return PistonDisposition == 0;
  }}

  float PistonDisposition { get {
    return Piston.MaxLimit - Piston.CurrentPosition;
  }}

  bool RotorInPosition { get {
    return RotorAngle == Rotor.UpperLimitRad;
  }}

  bool RotorStopped { get {
    return Rotor.RotorLock && RotorVelocity.GetValue(Rotor) == 0;
  }}

  float RotorAngle {

    // `Rotor.Angle` can exceed 2π several times, but this is not visible in the in-game properties!
    get {
      return U.Limit2Pi(Rotor.Angle);
    }

    // Try playing with the related property sliders in the game if you don't understand this order.
    set {
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

  void ChangeRotorPosition(int quarter) {
    RotorAngle = quarter * MathHelper.PiOver2;
    RotateRotor();
  }

  void ChangePistonPosition(int level) {
    // Expects levels are close together!
    Piston.MaxLimit = level * BlockSize; // m
    CurrentBarrelLevel = level;
    SetPistonVelocity();
  }

  void SetPistonVelocity() {
    Piston.Velocity = PistonDisposition < 0 ? -Piston.MaxVelocity : Piston.MaxVelocity;
  }

  void StopRotor() {
    Rotor.RotorLock = true;
    Rotor.Torque = 0;
    Rotor.BrakingTorque = MAX_ROTOR_TORQUE;
    Rotor.TargetVelocityRad = 0;
  }

  // The direction of rotation depends on the `Rotor.Torque` sign! The velocity sign is always positive. We need to
  // decrease the torque in 85 (an empirical value) times to stabilize the rotation in the negative direction.
  void RotateRotor() {
    var rotate_radians = Rotor.UpperLimitRad - RotorAngle;
    var reverse = (-MathHelper.Pi < rotate_radians && rotate_radians < 0) || MathHelper.Pi < rotate_radians;
    Rotor.Torque = reverse ? MAX_ROTOR_TORQUE / -85 : MAX_ROTOR_TORQUE;
    Rotor.BrakingTorque = 0;
    Rotor.TargetVelocityRad = MathHelper.Pi; // max
    Rotor.RotorLock = false;
  }

  #endregion // RapidGun
}}