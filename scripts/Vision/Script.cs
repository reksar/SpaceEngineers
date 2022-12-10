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

namespace Vision {

public sealed class Program : MyGridProgram {

	#region Vision

	DetectorCollection Detectors;
	EntityDatabase Entities;
	Transceiver Com;
	MeInfo Info;

	class DetectorCollection {
		List<IMyFunctionalBlock> Detectors;
		readonly IMyGridTerminalSystem Grid;

		public DetectorCollection(IMyGridTerminalSystem grid) {
			Grid = grid;
			Detectors = new List<IMyFunctionalBlock>();
		}

		public void Update() {
			Detectors.Clear();
			Grid.GetBlocksOfType<IMyFunctionalBlock>(Detectors, IsDetector);
		}

		public IEnumerable<MyDetectedEntityInfo> Entities { get {
			return Detectors.Select(Entity).Where(entity => ! entity.IsEmpty());
		}}

		bool IsDetector(IMyFunctionalBlock block) {
			return block is IMyTurretControlBlock || block is IMyLargeTurretBase || block is IMySensorBlock;
		}

		MyDetectedEntityInfo Entity(IMyFunctionalBlock detector) {
			if (detector is IMyTurretControlBlock) {
				return (detector as IMyTurretControlBlock).GetTargetedEntity();
			} else if (detector is IMyLargeTurretBase) {
				return (detector as IMyLargeTurretBase).GetTargetedEntity();
			} else if (detector is IMySensorBlock) {
				return (detector as IMySensorBlock).LastDetectedEntity;
			}
			return new MyDetectedEntityInfo(); // Empty
		}
	}

	class EntityDatabase {
		HashSet<MyDetectedEntityInfo> ActualEntities;
		HashSet<MyDetectedEntityInfo> DeprecatedEntities;

		class DetectedEntityComparer : EqualityComparer<MyDetectedEntityInfo> {

			public override bool Equals(MyDetectedEntityInfo entity1, MyDetectedEntityInfo entity2) {
				return entity1.EntityId == entity2.EntityId;
			}

			public override int GetHashCode(MyDetectedEntityInfo entity) {
				return entity.EntityId.GetHashCode();
			}
		}

		public IEnumerable<MyDetectedEntityInfo> Actual { get { return ActualEntities; } }
		public IEnumerable<MyDetectedEntityInfo> Deprecated { get { return DeprecatedEntities; } }
		public IEnumerable<MyDetectedEntityInfo> All { get { return Actual.Concat(Deprecated); } }

		public EntityDatabase() {
			var Comparer = new DetectedEntityComparer();
			ActualEntities = new HashSet<MyDetectedEntityInfo>(Comparer);
			DeprecatedEntities = new HashSet<MyDetectedEntityInfo>(Comparer);
		}

		public void Update(IEnumerable<MyDetectedEntityInfo> entities) {
			DeprecatedEntities.UnionWith(ActualEntities);
			ActualEntities.Clear();
			ActualEntities.UnionWith(entities);
			DeprecatedEntities.ExceptWith(ActualEntities);
			// TODO: Remove Deprecated by timestamp
		}
	}

	class Transceiver {

		// Tags for broadcasting Vision `Entities` in the Intergrid Communication system - `MyGridProgram.IGC`.
		const string IGC_VISION = "Vision";
		// TODO: third-party detectors, e.g. RayCast.
		const string IGC_VISION_ADD = "VisionAdd";

		readonly IMyIntergridCommunicationSystem IGC;

		public Transceiver(IMyIntergridCommunicationSystem igc) {
			IGC = igc;
		}

		public void Broadcast(IEnumerable<MyDetectedEntityInfo> entities) {
			IGC.SendBroadcastMessage(IGC_VISION, Serialize(entities), TransmissionDistance.CurrentConstruct);
		}

		/*
		 * We can't just to `SendBroadcastMessage(<tag>, Entities, <distance>)`, because:
		 *   - `TData` is expected to be immutable
		 *   - `MyDetectedEntityInfo` is not allowed as the `TData` inside IGC context
		 *   - a lot of C# features (e.g. serialization) are not allowed in this context
		 *
		 * Here is a solution:
		 *   - convert `HashSet` -> `ImmutableList`
		 *   - convert `MyDetectedEntityInfo` -> `MyTuple<...>`.
		 *
		 * Result: `Entities` -> `BroadcastData`, i.e. `HashSet<MyDetectedEntityInfo>` -> `ImmutableList<MyTuple<...>>`.
		 */
		ImmutableList<
			MyTuple<
				MyTuple<long, string, int, Vector3D, bool, MatrixD>,
				MyTuple<Vector3, int, BoundingBoxD, long>>>
		Serialize(IEnumerable<MyDetectedEntityInfo> entities) {
			return entities.Select(SerializeEntity).ToImmutableList();
		}

		/*
		 * `MyDetectedEntityInfo` can't be used as the `TData` in IGC methods, so convert it to 2D `MyTuple`. The 2D is
		 * used, because the max `MyTuple` length is 6 while needed at least 9 to represent the `MyDetectedEntityInfo`
		 * properties, +1 to avoid the nullable `Vector3D? HitPosition` -> `Vector3D HitPosition`, `bool UsesRaycast`.
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
	}

	class MeInfo {
		readonly IMyTextSurface LCD;

		public MeInfo(IMyProgrammableBlock me) {
			LCD = me.GetSurface(0);
			LCD.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
			LCD.FontColor = Color.White;
			LCD.BackgroundColor = Color.Black;
		}

		public void Display(EntityDatabase entities) {
			LCD.WriteText(InfoList(entities.Actual) + "\n\n" + InfoList(entities.Deprecated));
		}

		string InfoList(IEnumerable<MyDetectedEntityInfo> entities) {
			return String.Join("\n", entities.Select(EntityInfo));
		}

		string EntityInfo(MyDetectedEntityInfo entity) {
			return entity.Name + " (" + entity.EntityId.ToString() + ")";
		}
	}

	public Program() {
		Runtime.UpdateFrequency = UpdateFrequency.Update10;
		Detectors = new DetectorCollection(GridTerminalSystem);
		Entities = new EntityDatabase();
		Com = new Transceiver(IGC);
		Info = new MeInfo(Me);
	}

	public void Main(string argument, UpdateType updateSource) {
		Detectors.Update();
		Entities.Update(Detectors.Entities);
		Com.Broadcast(Entities.All);
		Info.Display(Entities);
	}

	#endregion // Vision
}}