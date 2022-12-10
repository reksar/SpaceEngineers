using System;
using System.Collections.Generic;
using System.Collections.Immutable;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage;
using VRage.Game;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace RCannon {

public sealed class Program : MyGridProgram {

	#region RCannon

	const float MAX_ROTOR_DISPLACEMENT = 0.11f; // m

	// Image names that will be displayed on the LCD at the Programming Block keyboard to indicate readiness to Fire.
	const string READY = "Arrow";
	const string NOT_READY = "Cross";
	const string CONSTRUCTION = "Construction";

	IMyTextSurface MeLCD, MeKeyLCD;

	List<IMyMotorStator> Spring;
	List<IMyShipWelder> Welders;
	IMyShipMergeBlock WarheadMerge;

	// To tracking targets.
	IMyBroadcastListener Vision;

	// Implements FSM.
	StateRunner State;
	delegate void StateRunner(string argument, UpdateType updateSource);

	public Program() {

		Runtime.UpdateFrequency = UpdateFrequency.Update1;

		WarheadMerge = GridTerminalSystem.GetBlockWithName("RCannonMerge") as IMyShipMergeBlock;
		IMyBlockGroup RCannon = GridTerminalSystem.GetBlockGroupWithName("RCannon");

		Spring = Select<IMyMotorStator>(RCannon);
		Spring.ForEach(rotor => {
			rotor.Enabled = false;
			rotor.RotorLock = true;
			rotor.Torque = 2000; // N*m
			rotor.BrakingTorque = 1000000; // N*m
			rotor.LowerLimitRad = 0;
			rotor.UpperLimitRad = 0;
			rotor.TargetVelocityRPM = 60;
		});

		Welders = Select<IMyShipWelder>(RCannon);
		Welders.ForEach(welder => {
			welder.Enabled = false;
			welder.UseConveyorSystem = true;
		});

		State = FitSpring;

		// Gives targets.
		Vision = IGC.RegisterBroadcastListener("Vision");

		// LCD will be used to show targets.
		MeLCD = Me.GetSurface(0);
		MeLCD.ContentType = ContentType.TEXT_AND_IMAGE;

		// Keyboard LCD will be used as indicator.
		MeKeyLCD = Me.GetSurface(1);
		MeKeyLCD.ContentType = ContentType.TEXT_AND_IMAGE;
	}

	public void Main(string argument, UpdateType updateSource) {
		State(argument, updateSource);
	}

	void FitSpring(string argument, UpdateType updateSource) {

		Indicate(NOT_READY);

		var AlignedRotors = Spring.FindAll(rotor => rotor.Angle == 0);
		AlignedRotors.ForEach(rotor => {
			rotor.Enabled = false;
			rotor.RotorLock = true;
			rotor.Displacement = -MAX_ROTOR_DISPLACEMENT;
		});

		var UnalignedRotors = Spring.FindAll(rotor => rotor.Angle != 0);
		UnalignedRotors.ForEach(rotor => {
			rotor.Enabled = true;
			rotor.RotorLock = false;
		});

		if (UnalignedRotors.Count == 0) {
			State = WeldWarhead;
		}
	}

	void WeldWarhead(string argument, UpdateType updateSource) {

		WarheadMerge.Enabled = true;

		// Assume that the warhead is welded if this block exists.
		var Sensor = GridTerminalSystem.GetBlockWithName("RCannonWarheadSensor") as IMySensorBlock;

		if (Sensor == null) {
			Indicate(CONSTRUCTION);
			Welders.ForEach(welder => welder.Enabled = true);
		} else {
			Indicate(READY);
			State = Ready;
			Welders.ForEach(welder => welder.Enabled = false);
		}
	}

	void Ready(string argument, UpdateType updateSource) {

		DisplayTargets();

		if (updateSource == UpdateType.Trigger && argument == "Fire") {
			State = Fire;
		}
	}

	void DisplayTargets() {

		var Targets = Vision.AcceptMessage().As<
			ImmutableList<
				MyTuple<
					MyTuple<long, string, int, Vector3D, bool, MatrixD>,
					MyTuple<Vector3, int, BoundingBoxD, long>>>>();
		
		if (Targets != null) {
			var Names = Targets.ConvertAll(entity => entity.Item1.Item2);
			MeLCD.WriteText(String.Join("\n", Names));
		}
	}

	void Fire(string argument, UpdateType updateSource) {

		Indicate(NOT_READY);

		try {
			var Warhead = GridTerminalSystem.GetBlockWithName("RCannonWarhead") as IMyWarhead;
			Warhead.IsArmed = true;

			// If the Warhead is not detonated by Sensor or by damage, it will be self-destruct after
			Warhead.DetonationTime = 60; // s
			Warhead.StartCountdown();

		} catch {
			Echo("ERR: Warhead is not found!");
		}

		Spring.ForEach(rotor => rotor.Displacement = MAX_ROTOR_DISPLACEMENT);
		WarheadMerge.Enabled = false;
		State = FitSpring;
	}

	List<T> Select<T>(IMyBlockGroup group) where T : class {
		var Blocks = new List<T>();
		group.GetBlocksOfType<T>(Blocks);
		return Blocks;
	}

	void Indicate(string image) {
		MeKeyLCD.ClearImagesFromSelection();
		MeKeyLCD.AddImageToSelection(image);
	}

	#endregion // RCannon
}}