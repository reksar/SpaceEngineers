using System;
using System.Collections.Generic;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage.Game;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace RCannon {

public sealed class Program : MyGridProgram {

	#region RCannon

	const float MAX_ROTOR_DISPLACEMENT = 11; // cm
	List<IMyMotorStator> Spring;
	List<IMyShipWelder> Welders;
	IMyShipMergeBlock WarheadMerge;
	IMyTextSurface MeLCD, MeKeyLCD;
	StateMachine FSM;
	delegate void StateMachine(string argument, UpdateType updateSource);

	public Program() {
		Runtime.UpdateFrequency = UpdateFrequency.Update1;

		IMyBlockGroup RCannon = GridTerminalSystem.GetBlockGroupWithName("RCannon");
		Spring = SelectType<IMyMotorStator>(RCannon);
		Spring.ForEach(rotor => {
			rotor.Enabled = false;
			rotor.RotorLock = true;
			rotor.Torque = 2000; // N*m
			rotor.BrakingTorque = 1000000; // N*m
			rotor.LowerLimitRad = 0;
			rotor.UpperLimitRad = 0;
			rotor.TargetVelocityRPM = 60;
		});
		Welders = SelectType<IMyShipWelder>(RCannon);
		Welders.ForEach(welder => {
			welder.Enabled = false;
			welder.UseConveyorSystem = true;
		});
		WarheadMerge = GridTerminalSystem.GetBlockWithName("RCannonMerge") as IMyShipMergeBlock;

		MeLCD = Me.GetSurface(0);
		MeLCD.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
		MeKeyLCD = Me.GetSurface(1);
		MeKeyLCD.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;

		FSM = FitSpring; // Init state.
	}

	public void Main(string argument, UpdateType updateSource) {
		FSM(argument, updateSource);
	}

	void FitSpring(string argument, UpdateType updateSource) {
		// TODO: Slow down the spring compression.

		IndicateNotReady();

		WarheadMerge.Enabled = true;

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
			rotor.Displacement = MAX_ROTOR_DISPLACEMENT;
		});

		if (UnalignedRotors.Count == 0) {
			FSM = WeldWarhead;
		}
	}

	void WeldWarhead(string argument, UpdateType updateSource) {

		IndicateNotReady();

		try {
			// Throws an exception if the block is not found.
			var Warhead = GridTerminalSystem.GetBlockWithName("RCannonWarhead") as IMyWarhead;

			Warhead.IsArmed = false;
			Welders.ForEach(welder => welder.Enabled = false);
			IndicateReady();
			FSM = Ready;
		} catch {
			// Can't GetBlockWithName "RCannonWarhead".
			Welders.ForEach(welder => welder.Enabled = true);
		}
	}

	void Ready(string argument, UpdateType updateSource) {
		if (updateSource != UpdateType.Update1 && argument == "Fire") {
			FSM = Fire;
		}
	}

	void Fire(string argument, UpdateType updateSource) {
		// TODO: Arm warhead.
		IndicateNotReady();
		Spring.ForEach(rotor => rotor.Displacement = MAX_ROTOR_DISPLACEMENT);
		WarheadMerge.Enabled = false;
		FSM = FitSpring;
	}

	List<T> SelectType<T>(IMyBlockGroup group) where T : class {
		var Blocks = new List<T>();
		group.GetBlocksOfType<T>(Blocks);
		return Blocks;
	}

	void IndicateNotReady() {
		MeKeyLCD.ClearImagesFromSelection();
		MeKeyLCD.AddImageToSelection("Cross");
	}

	void IndicateReady() {
		MeKeyLCD.ClearImagesFromSelection();
		MeKeyLCD.AddImageToSelection("Arrow");
	}

	#endregion // RCannon
}}