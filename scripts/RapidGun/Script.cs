using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

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

  //   G1 G5
  //   G2 G6
  //   G3 G7
  //   G4 G8
  //   S1 S2
  // P__|__|
  //    T  C
  //
  // `Pivot` *P* is the main piston to which 2 more are attached via conveyors.
  IMyPistonBase Pivot;
  // Slider *S1* is closest to the `Pivot` and is attached by a T-conveyor.
  // Slider *S2* is attached to the free end of the T-conveyor via C-conveyor.
  // 4 artillery guns are attached to the head of each slider. Artillery reload time is 8 seconds, so there are 8 guns
  // *G1* .. *G8* to be able to shoot every second.
  // Each slider with guns connected is a gun battery.
  List<MyTuple<IMyPistonBase, List<IMyUserControllableGun>>> GunBatteries =
    new List<MyTuple<IMyPistonBase, List<IMyUserControllableGun>>>();
  int CurrentBatteryIdx, CurrentGunIdx;

  float BlockSize; // m

  StringBuilder Status = new StringBuilder();
  // To show the `Status`.
  IMyTextSurface LCD;
  // To `Indicate` the current state.
  IMyTextSurface KeyLCD;

	const string READY = "Arrow";
	const string NOT_READY = "Cross";
	const string RELOADING = "Danger";

  Program() {

    LCD = Me.GetSurface(0);
    LCD.ContentType = ContentType.TEXT_AND_IMAGE;
    LCD.FontColor = Color.White;
    LCD.BackgroundColor = Color.Black;

    KeyLCD = Me.GetSurface(1);
    KeyLCD.ContentType = ContentType.TEXT_AND_IMAGE;
    KeyLCD.FontColor = Color.White;
    KeyLCD.BackgroundColor = Color.Black;

    AssignBlocks();

    // TODO: check gun system
    if (true) {
      Init();
      Runtime.UpdateFrequency = UpdateFrequency.Update10;
    }
  }

  void AssignBlocks() {

    BlockSize = Me.CubeGrid.GridSize;
    
    foreach (var group in MyGroups) {

      var pistons = Select<IMyPistonBase>(group);
      var sliders = pistons.FindAll(piston => IsGun(piston.TopGrid.GetCubeBlock(Vector3I.Up)));
      Pivot = pistons.Except(sliders).FirstOrDefault();

      if (Pivot == null || sliders.Count <= 0) continue;

      GunBatteries.Clear();
      sliders.ForEach(slider => GunBatteries.Add(MyTuple.Create(slider, ConnectedGuns(slider))));
      GunBatteries.Sort(ByPivotDistance);
      GunBatteries.Reverse();

      Echo("BlockSize: " + BlockSize.ToString());
      Echo("Sliders: " + sliders.Count.ToString());
    }
  }

  void Main(string argument, UpdateType updateSource) {
    var battery = GunBatteries[CurrentBatteryIdx];
    var slider = battery.Item1;
    var guns = battery.Item2;
    var gun = guns[CurrentGunIdx];
    // TODO: check Pivot position
    if (!gun.Enabled && slider.CurrentPosition == slider.MaxLimit) {
      gun.Enabled = true;
    } else if (gun.IsShooting) {
      gun.Enabled = false;
      CurrentGunIdx++;
      if (CurrentGunIdx >= guns.Count) {
        CurrentGunIdx = 0;
        slider.MaxLimit = 0;
        slider.Velocity = -slider.MaxVelocity;
        CurrentBatteryIdx++;
        if (CurrentBatteryIdx >= GunBatteries.Count) {
          CurrentBatteryIdx = 0;
          Pivot.MaxLimit = 0;
          Pivot.Velocity = -Pivot.MaxVelocity;
        } else {
          Pivot.MaxLimit += BlockSize;
          Pivot.Velocity = Pivot.MaxVelocity;
        }
      } else {
        slider.MaxLimit += BlockSize;
        slider.Velocity = slider.MaxVelocity;
      }
    }
  }

  void Init() {
    const float PISTON_IMPULSE = 20000000f; // N
    Pivot.MinLimit = 0;
    Pivot.MaxLimit = BlockSize * GunBatteries.Count;
    Pivot.SetValue("MaxImpulseAxis", PISTON_IMPULSE);
    Pivot.Velocity = -Pivot.MaxVelocity;
    GunBatteries.ForEach(battery => {
      var slider = battery.Item1;
      slider.MinLimit = 0;
      slider.MaxLimit = 0;
      slider.SetValue("MaxImpulseAxis", PISTON_IMPULSE);
      slider.Velocity = -Pivot.MaxVelocity;
      battery.Item2.ForEach(gun => gun.Enabled = false);
    });
    CurrentBatteryIdx = 0;
    CurrentGunIdx = 0;
  }

  void Indicate(string image) {
		KeyLCD.ClearImagesFromSelection();
		KeyLCD.AddImageToSelection(image);
  }

  void ShowStatus() {
    LCD.WriteText(Status);
    Echo(Status.ToString());
  }

  List<IMyBlockGroup> MyGroups { get {
    var groups = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(groups, group =>
      Select<IMyProgrammableBlock>(group, block =>
        block == Me).Count == 1);
    return groups;
  }}

  List<T> Select<T>(IMyBlockGroup group, Func<T, bool> filter = null) where T : class {
		var blocks = new List<T>();
		group.GetBlocksOfType<T>(blocks, filter);
		return blocks;
	}

  List<IMyUserControllableGun> ConnectedGuns(IMyPistonBase slider) {

    var guns = new List<IMyUserControllableGun>();
    var grid = slider.TopGrid;

    var position = Vector3I.Up;
    var block = grid.GetCubeBlock(position);

    while (IsGun(block)) {
      guns.Add(block.FatBlock as IMyUserControllableGun);

      position += Vector3I.Up;
      block = grid.GetCubeBlock(position);
    }

    guns.Sort((gun1, gun2) => gun1.Position.Y - gun2.Position.Y);
    guns.Reverse();
    return guns;
  }

  bool IsGun(IMySlimBlock block) {
    return null != block && block.FatBlock is IMyUserControllableGun;
  }

  int ByPivotDistance(
    MyTuple<IMyPistonBase, List<IMyUserControllableGun>> battery1,
    MyTuple<IMyPistonBase, List<IMyUserControllableGun>> battery2
  ) {
    var slider1 = battery1.Item1;
    var slider2 = battery2.Item1;
    var distance1 = Vector3.Distance(Pivot.GetPosition(), slider1.GetPosition());
    var distance2 = Vector3.Distance(Pivot.GetPosition(), slider2.GetPosition());
    return (int)(distance1 - distance2);
  }

  #endregion // RapidGun
}}