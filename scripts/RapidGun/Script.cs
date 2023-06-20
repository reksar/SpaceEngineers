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

    // Positions surrounding the `center` in 4 base directions.
    public static ImmutableList<Vector3I> CrissCrossPositions(Vector3I center) {
      return ImmutableList.Create(new Vector3I[] {
        Vector3I.Forward + center,
        Vector3I.Right + center,
        Vector3I.Backward + center,
        Vector3I.Left + center
      });
    }

    // Positions from the `center` in 4 diagonal directions.
    public static ImmutableList<Vector3I> DiagonalPositions(Vector3I center) {
      return ImmutableList.Create(new Vector3I[] {
        Vector3I.Forward + Vector3I.Left + center,
        Vector3I.Forward + Vector3I.Right + center,
        Vector3I.Backward + Vector3I.Left + center,
        Vector3I.Backward + Vector3I.Right + center
      });
    }

    public static IEnumerable<Vector3I> EndlessUp() {
      var position = Vector3I.Zero; // Initial value will not be yielded.
      while (true) yield return position += Vector3I.Up;
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
  List<Dictionary<int, IMyUserControllableGun>> Barrel; // TODO: replace with TestBarrel
  List<List<IMyUserControllableGun>> TestBarrel;
  int CurrentBarrelLevel;

  // Current active gun in `FireDirection`.
  IMyUserControllableGun Gun; 

  // Will be different for *large* and *small* grids.
  float BlockSize; // m

  IMyTextSurface LCD, KeyLCD;

  const float MAX_ROTOR_TORQUE = 1000000000; // N*m

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

    if (Piston != null)
      TestBarrel = U.EndlessUp().Select(U.CrissCrossPositions).Select(CrissCrossGuns).TakeWhile(guns => guns.Count > 0)
        .ToList();

    return Piston != null;
  }

  bool SetBarrel(IMyCubeGrid barrel) {

    Barrel = U.EndlessUp()
      .Select(U.CrissCrossPositions)
      .Select(positions =>
        U.Select<IMyUserControllableGun>(barrel, positions).ToDictionary(GunWorkingAngle, gun => gun))
      .TakeWhile(guns => guns.Count > 0)
      .ToList();

    return Barrel.Count > 0;
  }

  List<IMyUserControllableGun> CrissCrossGuns(IEnumerable<Vector3I> positions) {
    return U.Select<IMyUserControllableGun>(Rotor.TopGrid, positions).OrderBy(GunQuarter).ToList();
  }

  int GunQuarter(IMyCubeBlock gun) {
    return RelativeQuarter(Base6Directions.GetForward(RelativeGunOrientation(gun)));
  }

  Quaternion RelativeGunOrientation(IMyCubeBlock gun) {

    // Orientation of the fire direction of an active gun relative to the main grid.
    var fire_orientation = Base6Directions.GetOrientation(
      Base6Directions.Direction.Forward,
      Base6Directions.Direction.Up
    );

    Quaternion piston_orientation;
    Piston.Orientation.GetQuaternion(out piston_orientation);

    Quaternion stator_orientation;
    Rotor.Orientation.GetQuaternion(out stator_orientation);

    Quaternion gun_orientation;
    gun.Orientation.GetQuaternion(out gun_orientation);

    return fire_orientation * piston_orientation * stator_orientation * gun_orientation;
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

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
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
    var angle_info = current_angle + (Rotor.RotorLock ? "" : "/" + desired_angle) + "°";

    var level_info = (PistonInPosition ? "" : (Piston.Velocity < 0 ? "falls" : "rises") + " to ") + "level " +
      CurrentBarrelLevel.ToString();

    var diagram = Rotor.RotorLock ? BarrelDiagramLocked() : BarrelDiagramRotation();

    LCD.WriteText(
      state+" "+angle_info+" "+level_info+"\n\n"+
      diagram
    );

    lcd.ClearImagesFromSelection();
  }

  //     +
  //     ^
  // + +   + +
  //     +
  //     +
  string BarrelDiagramLocked() {

    string spacer_y = new string(' ', 2 * TestBarrel.Count);

    string diagram = "";

    GunsInDirection(Base6Directions.Direction.Forward).ToList()
      .ForEach(gun => diagram += spacer_y + GunChar(gun) + "\n");

    GunsInDirection(Base6Directions.Direction.Left).ToList()
      .ForEach(gun => diagram += GunChar(gun) + " ");

    diagram += " ";

    GunsInDirection(Base6Directions.Direction.Right).ToList()
      .ForEach(gun => diagram += GunChar(gun) + " ");

    diagram += "\n";

    GunsInDirection(Base6Directions.Direction.Backward).ToList()
      .ForEach(gun => diagram += spacer_y + GunChar(gun) + "\n");

    return diagram;
  }

  // TODO:
  // +      +
  //   +  +
  //   +  +
  // +      +
  string BarrelDiagramRotation() {

    string diagram = "";

    return diagram;
  }

  // All guns on the `Barrel` pointing in actual `direction`.
  IEnumerable<IMyUserControllableGun> GunsInDirection(Base6Directions.Direction direction) {
    return TestBarrel.Select(guns => guns[Quarter(direction)]);
  }

  // Increases counter-clockwise, because the `Rotor` rotates clockwise.
  int RelativeQuarter(Base6Directions.Direction direction) {
    switch (direction) {
      case Base6Directions.Direction.Forward: return 0;
      case Base6Directions.Direction.Left: return 1;
      case Base6Directions.Direction.Backward: return 2;
      case Base6Directions.Direction.Right: return 3;
      default: return -1;
    }
  }

  int Quarter(Base6Directions.Direction direction) {
    return (RelativeQuarter(direction) + RotorQuarter) % 4;
  }

  // There are 4 calibrated angles of the `Rotor` where it locked: 0, 0.5π, π, 1.5π.
  // They correspond to 4 quarters of a circle: 0 / 0.5π = 0, 0.5π / 0.5π = 1, π / 0.5π = 2, 1.5π / 0.5π = 3.
  int RotorQuarter { get {
    return MathHelper.RoundToInt(RotorAngle / MathHelper.PiOver2);
  }}

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

    if (GunAvailable(Gun)) Gun.Enabled = true;
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
    var available_angles = GunPlane.Where(i => GunAvailable(i.Value)).Select(i => i.Key).ToList();
    if (available_angles.Count > 0) SetRotorAngle(available_angles);
    else SetNextGunPlane();
  }

  bool GunAvailable(IMyUserControllableGun gun) {
    return gun != null && !gun.IsShooting && gun.IsFunctional;
  }

  // TODO: change this after debugging
  bool GunReady(IMyUserControllableGun gun) {
    return GunAvailable(gun) && gun.Enabled;
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



  #endregion // RapidGun
}}