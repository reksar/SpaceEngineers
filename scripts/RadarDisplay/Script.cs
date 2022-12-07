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

namespace Radar {

/*
 * Receives *Vision* entities using ICG, then represents them on the radar plane and displays on specified `LCDs`.
 */
public sealed class Program : MyGridProgram {

	#region Radar

	#region Settings
	static readonly Color BACKGROUND = Color.Black;
	static readonly Color PLANE_BACKGROUND = new Color(5, 5, 5, 255);
	static readonly Color UI_COLOR = new Color(255, 255, 255, 20);
	static readonly Color FRIEND_COLOR = Color.Green;
	static readonly Color ENEMY_COLOR = Color.Red;
	static readonly Color NEUTRAL_COLOR = Color.White;

	// UI lines.
	const float LINE_WIDTH = 0.5f;
	// Line of the circle that represents specific radar range.
	const float RANGE_CIRCLE_WIDTH = 0.2f;

	// Size of a representation on the Radar plane.
	static readonly Vector2 SMALL_GRID_SIZE = new Vector2(6);
	static readonly Vector2 LARGE_GRID_SIZE = new Vector2(8);
	static readonly Vector2 DEFAULT_ENTITY_SIZE = new Vector2(4);

	// Front sector representing the view from a cockpit.
	static readonly float FRONT_SECTOR_HALF_ANGLE = MathHelper.ToRadians(60 / 2);

	// Cos of the radar plane angle relative to the LCD surface (radar plane is flat if angle is 0°).
	static readonly float PROJECTION_COS = (float)Math.Cos(MathHelper.ToRadians(50));

	// NOTE: should be > 0!
	const int MAX_RADAR_RANGE = 600; // m

	// NOTE: should be sorted in descending order!
	static readonly int[] PLANE_RANGES = { // m
		2000, // large artillery and railgun range
		1400, // small artillery and railgun range
		800, // rockets and small guns range, as well as the large turret controller range
		600, // small turret controller range
		50, // sensor block range
	};
	#endregion // Settings

	// To receive entities detected by *Vision* script.
	readonly IMyBroadcastListener Vision;

	// To display the radar plane with projected *Vision* entities.
	List<RadarLCD> LCDs;

	class RadarLCD {
		readonly IMyTextSurface LCD;
		readonly LCDLayout Layout;
		ImmutableList<MySprite> StaticUI;

		public RadarLCD(IMyTextSurface lcd) {

			LCD = lcd;
			LCD.Script = "";
			LCD.ContentType = ContentType.SCRIPT;
			LCD.ScriptBackgroundColor = BACKGROUND;

			Layout = new LCDLayout(LCD);
			StaticUI = Planes.Concat(FrontSector).ToImmutableList();

			Draw();
		}

		/*
		 * Draw the `StaticUI` only.
		 */
		public void Draw() {
			var Frame = LCD.DrawFrame();
			Frame.AddRange(StaticUI);
			Frame.Dispose();
		}

		/*
		 * Draw entities on the radar with the `StaticUI`.
		 */
		public void Draw(IEnumerable<MySprite> sprites) {
			var Frame = LCD.DrawFrame();
			Frame.AddRange(StaticUI);
			Frame.AddRange(sprites);
			Frame.Dispose();
		}

		public void Draw(IEnumerable<RadarEntity> entities) {
			Draw(entities.Select(EntitySprite));
		}

		MySprite EntitySprite(RadarEntity entity) {

			Color SpriteColor;

      switch (entity.Relation) {
				case MyRelationsBetweenPlayerAndBlock.Owner:
				case MyRelationsBetweenPlayerAndBlock.Friends:
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
					SpriteColor = FRIEND_COLOR;
					break;
				case MyRelationsBetweenPlayerAndBlock.Enemies:
					SpriteColor = ENEMY_COLOR;
					break;
				default:
					SpriteColor = NEUTRAL_COLOR;
					break;
			}

			switch (entity.Type) {
				case MyDetectedEntityType.SmallGrid:
					return SmallGrid(entity.Position, entity.Height, SpriteColor);
				case MyDetectedEntityType.LargeGrid:
					return LargeGrid(entity.Position, entity.Height, SpriteColor);
				default:
					return DefaultEntity(entity.Position, entity.Height, SpriteColor);
			}
		}

		MySprite SmallGrid(Vector2 position, float height, Color color) {
			return new MySprite(SpriteType.TEXTURE, "Triangle", Layout.Plane(position), SMALL_GRID_SIZE, color);
		}

		MySprite LargeGrid(Vector2 position, float height, Color color) {
			return new MySprite(SpriteType.TEXTURE, "SquareSimple", Layout.Plane(position), LARGE_GRID_SIZE, color);
		}

		MySprite DefaultEntity(Vector2 position, float height, Color color) {
			return new MySprite(SpriteType.TEXTURE, "Circle", Layout.Plane(position), DEFAULT_ENTITY_SIZE, color);
		}

		/*
		 * The caliber of the radar ranges represented as concentric circles (planes) for each range.
		 */
		IEnumerable<MySprite> Planes { get {
			var MaxPlane = RangePlane(MAX_RADAR_RANGE);
			var RangePlanes = PLANE_RANGES.SkipWhile(range => range >= MAX_RADAR_RANGE).Select(RangePlane);
			return RangePlanes.Aggregate(MaxPlane, (collection, plane) => collection.Concat(plane));
		}}

		/*
		 * Two concentric circles (ellipses in projection): the ring representing the given `range` and its background.
		 */
		IEnumerable<MySprite> RangePlane(int range) {
			float Scale = (float)range / MAX_RADAR_RANGE;
			var Size = Layout.ProjectionPlaneSize * Scale;
			var Circle = new MySprite(SpriteType.TEXTURE, "Circle", Layout.Center, Size, UI_COLOR);
			yield return Circle;
			Circle.Size -= RANGE_CIRCLE_WIDTH;
			Circle.Color = PLANE_BACKGROUND;
			yield return Circle;
		}

		/*
		 *   \      /
		 *    \    /
		 *     \  /
		 *      \/
		 *       * layout center
		 */
		IEnumerable<MySprite> FrontSector { get {
			var ViewingRange = Layout.ProjectionPlaneSize.Y / 2;
			yield return PlaneRadius(ViewingRange, FRONT_SECTOR_HALF_ANGLE);
			yield return PlaneRadius(ViewingRange, -FRONT_SECTOR_HALF_ANGLE);
		}}

		/*
		 * Radius `angle` is clockwise:
		 *
		 * normal
		 *   |   / radius length
		 *   |α /
		 *   |_/ radius angle
		 *   |/
		 *    * layout center
		 */
		MySprite PlaneRadius(float length, float angle) {
			var Sin = (float)Math.Sin(angle);
			var Cos = (float)Math.Cos(angle);
			var A = Cos / length;
			var B = (Sin / length) * PROJECTION_COS;
			var R = 1 / (float)Math.Sqrt(Math.Pow(A, 2) + Math.Pow(B, 2));
			var Size = new Vector2(LINE_WIDTH, R);
			var Offset = new Vector2(Sin, -Cos) * R / 2;
			var Position = Layout.Center + Offset;
			return new MySprite(SpriteType.TEXTURE, "SquareSimple", Position, Size, UI_COLOR, rotation: angle);
		}
	}

	struct LCDLayout {
		public readonly float Padding;
		public readonly float Height;
		public readonly float Width;
		public readonly Vector2 Center;
		public readonly Vector2 ProjectionPlaneSize;
		readonly Vector2 Scale;

		/*
		 * `LCD.SurfaceSize` - is the actual size of the visible screen. Can be square or rectangular.
		 * `LCD.TextureSize` is used for positioning. Always square of size equal to the LCD width.
		 * `LCD.SurfaceSize` is centered in the `LCD.TextureSize`:
		 *
		 *   +-------------------+
		 *   |    TextureSize    |
		 *   +-------------------+
		 *   |                   |
		 *   |    SurfaceSize    |
		 *   |                   |
		 *   +-------------------+
		 *   |                   |
		 *   +-------------------+
		 *
		 * LCD `Width >= Height`, so `LCD.TextureSize.Y >= LCD.SurfaceSize.Y` and `LCD.TextureSize.X == LCD.SurfaceSize.X`.
		 *
		 * NOTE: expects that `TextAlignment.CENTER` is set for texture sprites!
		 * NOTE: right half of wide LCD panels will be empty.
		 */
		public LCDLayout(IMyTextSurface LCD) {

			Padding = LCD.SurfaceSize.X / 50; // 2% of LCD `Width`

			var empty = Padding * 2;
			Height = LCD.SurfaceSize.Y - empty;
			Width = LCD.TextureSize.Y - empty;

			// `Width` is chosen to positioning in the big `LCD.TextureSize` square as the largest dimension.
			Center = new Vector2(Padding + Width / 2);

			ProjectionPlaneSize = new Vector2(Width, Height * PROJECTION_COS);
			Scale = ProjectionPlaneSize / (MAX_RADAR_RANGE * 2);
		}

		/*
		 * Converts the actual `position` relative to the `Center` and scales it on the `MaxRadarPlane`.
		 *
		 * NOTE: expects that the `position.Y` is already inverted.
		 */
		public Vector2 Plane(Vector2 position) {
			return Center + position * Scale;
		}
	}

	/*
	 * See `SerializeEntity` in the *Vision* script.
	 */
	struct VisionEntity {
		// TODO: Modify Position here instead of creation `RadarEntity` instance.
		public readonly Vector3 Position;
		public readonly MyDetectedEntityType Type;
		public readonly MyRelationsBetweenPlayerAndBlock Relation;

		public static IEnumerable<VisionEntity> Collect(IMyBroadcastListener vision) {
			return vision.AcceptMessage().As<
					IEnumerable<
						MyTuple<
							MyTuple<long, string, int, Vector3D, bool, MatrixD>,
							MyTuple<Vector3, int, BoundingBoxD, long>>>>()
				.Select(serialized => new VisionEntity(serialized));
		}

		public VisionEntity(
				MyTuple<
					MyTuple<long, string, int, Vector3D, bool, MatrixD>,
					MyTuple<Vector3, int, BoundingBoxD, long>>
				serialized) {
			Position = serialized.Item2.Item3.Center;
			Type = (MyDetectedEntityType)serialized.Item1.Item3;
			Relation = (MyRelationsBetweenPlayerAndBlock)serialized.Item2.Item2;
		}
	}

	struct RadarEntity {
		public readonly Vector2 Position;
		public readonly float Height;
		public readonly MyDetectedEntityType Type;
		public readonly MyRelationsBetweenPlayerAndBlock Relation;
		public RadarEntity(
				Vector2 position,
				float height,
				MyDetectedEntityType type,
				MyRelationsBetweenPlayerAndBlock relation) {
			Position = position;
			Height = height;
			Type = type;
			Relation = relation;
		}
	}

	readonly Navigation Nav;

	class Navigation {
		readonly IMyEntity Anchor;

		/*
		 * If there is no main Cockpit, the current Programmable Block is used instead. Mind the block orientation!
		 */
		public Navigation(IMyGridTerminalSystem grid, IMyProgrammableBlock me) {
			var cockpits = new List<IMyCockpit>();
			grid.GetBlocksOfType<IMyCockpit>(cockpits, cockpit => cockpit.IsMainCockpit);
			Anchor = cockpits.Count > 0 ? cockpits.First<IMyEntity>() : (IMyEntity)me;
		}

		public bool InRadarRange(Vector3 position) {
			return RelativePosition(position).Length() < MAX_RADAR_RANGE;
		}

		public Vector3 RelativePosition(Vector3 position) {
			return position - Anchor.GetPosition();
		}

		public Vector3 Project(Vector3 position) {
			return Vector3.TransformNormal(position, Matrix.Transpose(Anchor.WorldMatrix));
		}

		/*
		 * Position of the `projected` on a surface. The third coordinate (Y) is the height (above or below) a surface.
		 * 
		 * NOTE: the `projected.Z` or resulting `Vector2.Y` is actually inverted, but this will be handy further to calc
		 * the `Layout.Plane` position.
		 */
		public Vector2 ProjectedPosition(Vector3 projected) {
			return new Vector2(projected.X, projected.Z);
		}

		public float ProjectedHeight(Vector3 projected) {
			return projected.Y;
		}
	}

	static IMyTextSurface dbg;

	public Program() {
		Runtime.UpdateFrequency = UpdateFrequency.Update10;
		Vision = IGC.RegisterBroadcastListener("Vision");
		Nav = new Navigation(GridTerminalSystem, Me);
		LCDs = SelectLCDs().ConvertAll(LCD => new RadarLCD(LCD));
	}

	public void Main(string argument, UpdateType updateSource) {
		// TODO: filter entities with nested bounding boxes.
		// TODO: try to create common sprites that should be scaled on drawing.
		var Entities = VisionEntity.Collect(Vision).Where(InRadarRange).Select(ToRadarEntity);
		LCDs.ForEach(LCD => LCD.Draw(Entities));
	}

	bool InRadarRange(VisionEntity entity) {
		return Nav.InRadarRange(entity.Position);
	}

	RadarEntity ToRadarEntity(VisionEntity entity) {
		var RelativePosition = Nav.RelativePosition(entity.Position);
		var Projection = Nav.Project(RelativePosition);
		var PlanePosition = Nav.ProjectedPosition(Projection);
		var Height = Nav.ProjectedHeight(Projection);
		return new RadarEntity(PlanePosition, Height, entity.Type, entity.Relation);
	}

	List<IMyTextSurface> SelectLCDs() {

		dbg = (GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit).GetSurface(2);
		dbg.ContentType = ContentType.TEXT_AND_IMAGE;

		// LCD, just for testing.
		var lcds = new List<IMyTextSurface>();
		lcds.Add((GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit).GetSurface(0));
		lcds.Add((GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit).GetSurface(3));
		lcds.Add((GridTerminalSystem.GetBlockWithName("LCD Panel") as IMyTextPanel));

		// Echo available sprite names.
		var s = new List<string>();
		lcds[0].GetSprites(s);
		Echo(String.Join("\n", s));
		return lcds;
	}

	#endregion // Radar
}}