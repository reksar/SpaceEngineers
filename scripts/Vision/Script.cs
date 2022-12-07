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
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace Vision {

public sealed class Program : MyGridProgram {

	#region Vision

	// Tags for broadcasting Vision `Entities` in the Intergrid Communication system - `MyGridProgram.IGC`.
	const string IGC_VISION = "Vision";
	// TODO: third-party sensors, e.g. RayCast.
	const string IGC_VISION_ADD = "VisionAdd";

	DetectedEntityComparer EntityComparer;
	HashSet<MyDetectedEntityInfo> ActualEntities;
	HashSet<MyDetectedEntityInfo> DeprecatedEntities;

	// Temporary storage for blocks selected from the grid when updating vision sources.
	List<IMyFunctionalBlock> BlockStorage;

	// Sources to recieve info about detected entities.
	List<Sensor> Sensors;

	// To display `Entities` here, just for debugging purposes.
	IMyTextSurface MeLCD;

	class DetectedEntityComparer : EqualityComparer<MyDetectedEntityInfo> {

		public override bool Equals(MyDetectedEntityInfo entity1, MyDetectedEntityInfo entity2) {
			return entity1.EntityId == entity2.EntityId;
		}

		public override int GetHashCode(MyDetectedEntityInfo entity) {
			return entity.EntityId.GetHashCode();
		}
	}

	/*
	 * Common adapter for getting `DetectedEntity`, because `IMyLargeTurretBase`, `IMyTurretControlBlock` and
	 * `IMySensorBlock` do not share a common base interface to get `MyDetectedEntityInfo`.
	 *
	 * NOTE: Cameras are not used here, because RayCast needs to be implemented separately.
	 */
	class Sensor {

		public static bool CanBe(IMyFunctionalBlock block) {
			return block is IMyTurretControlBlock || block is IMyLargeTurretBase || block is IMySensorBlock;
		}

		public EntityGetter Entity;
		public delegate MyDetectedEntityInfo EntityGetter();

		public Sensor(IMyFunctionalBlock block) {
			if (block is IMyTurretControlBlock) {
				Entity = (block as IMyTurretControlBlock).GetTargetedEntity;
			} else if (block is IMyLargeTurretBase) {
				Entity = (block as IMyLargeTurretBase).GetTargetedEntity;
			} else if (block is IMySensorBlock) {
				Entity = () => (block as IMySensorBlock).LastDetectedEntity;
			} else {
				throw new ArgumentException("Block is not a Sensor!");
			}
		}
	}

	public Program() {
		Runtime.UpdateFrequency = UpdateFrequency.Update1;

		EntityComparer = new DetectedEntityComparer();
		ActualEntities = new HashSet<MyDetectedEntityInfo>(EntityComparer);
		DeprecatedEntities = new HashSet<MyDetectedEntityInfo>(EntityComparer);

		BlockStorage = new List<IMyFunctionalBlock>();
		Sensors = new List<Sensor>();

		MeLCD = Me.GetSurface(0);
		MeLCD.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
	}

	public void Main(string argument, UpdateType updateSource) {
		UpdateSensors();
		UpdateEntities();
		IGC.SendBroadcastMessage(IGC_VISION, SerializeEntities(), TransmissionDistance.CurrentConstruct);
		MeLCD.WriteText(String.Join("\n", ActualEntities.ToImmutableList().ConvertAll(entity => entity.Name)));
	}

  /*
	 * We can't to `GetBlocksOfType<IMyLargeTurretBase>` or `GetBlocksOfType<IMyTurretControlBlock>` and runtime types,
	 * e.g. `SpaceEngineers.Game.Weapons.Guns.MyLargeGatlingTurret`, are prohibited in this context.
	 *
	 * But we can to `GetBlocksOfType<IMyFunctionalBlock>` and then filter them.
	 *
	 * Sensors can disappear as new ones appear. So we fully updating the list by checking the entire grid.
	 */
	void UpdateSensors() {
		BlockStorage.Clear();
		GridTerminalSystem.GetBlocksOfType<IMyFunctionalBlock>(BlockStorage, Sensor.CanBe);
		Sensors.Clear();
		Sensors.AddRange(BlockStorage.ConvertAll(block => new Sensor(block)));
	}

	IEnumerable<MyDetectedEntityInfo> DetectedEntities { get {
		return Sensors
			.ConvertAll(sensor => sensor.Entity())
			.FindAll(entity => ! entity.IsEmpty());
	}}

	void UpdateEntities() {
		DeprecatedEntities.UnionWith(ActualEntities);
		ActualEntities.Clear();
		ActualEntities.UnionWith(DetectedEntities);
		DeprecatedEntities.ExceptWith(ActualEntities);
		// TODO: Remove Deprecated by timestamp
	}

	/*
	 * We can't just to `SendBroadcastMessage(<tag>, Entities, <distance>)`, because:
	 *   - `TData` is expected to be immutable
	 *   - `MyDetectedEntityInfo` is not allowed as the `TData` inside IGC
	 *   - a lot of C# stuff (e.g. serializers) are not allowed in this context
	 *
	 * Here is a solution without using handy C# features:
	 *   - convert `HashSet` -> `ImmutableList`
	 *   - convert `MyDetectedEntityInfo` -> `MyTuple<...>`.
	 *
	 * As a result: `Entities` -> `BroadcastData`, i.e. `HashSet<MyDetectedEntityInfo>` -> `ImmutableList<MyTuple<...>>`.
	 */
	 // TODO: DeprecatedEntities broadcast.
	ImmutableList<
		MyTuple<
			MyTuple<long, string, int, Vector3D, bool, MatrixD>,
			MyTuple<Vector3, int, BoundingBoxD, long>>>
	SerializeEntities() {
		return ActualEntities.ToImmutableList().ConvertAll(SerializeEntity);
	}

	/*
	 * `MyDetectedEntityInfo` can't be used as the `TData` in IGC methods, so convert it to 2D `MyTuple`. The 2D is used,
	 * because the max `MyTuple` length is 6 while needed at least 9 to represent the `MyDetectedEntityInfo` properties,
	 * +1 to avoid the nullable `Vector3D? HitPosition` -> `Vector3D HitPosition`, `bool UsesRaycast`.
	 */
	MyTuple<
		MyTuple<long, string, int, Vector3D, bool, MatrixD>,
		MyTuple<Vector3, int, BoundingBoxD, long>>
	SerializeEntity(MyDetectedEntityInfo entity) {
		var HitPosition = entity.HitPosition ?? new Vector3D();
		var UsesRaycast = entity.HitPosition == null;
		return MyTuple.Create(
			MyTuple.Create(entity.EntityId, entity.Name, (int)entity.Type, HitPosition, UsesRaycast, entity.Orientation),
			MyTuple.Create(entity.Velocity, (int)entity.Relationship, entity.BoundingBox, entity.TimeStamp));
	}

	#endregion // Vision
}}