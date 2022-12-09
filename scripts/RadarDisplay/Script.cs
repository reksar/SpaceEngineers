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

	// Settings

	// NOTE: should be > 0!
	const int MAX_RADAR_RANGE = 800; // m

	// NOTE: should be sorted in descending order!
	static readonly int[] PLANE_RANGES = { // m
		2000, // large artillery and railgun range
		1400, // small artillery and railgun range
		800, // rockets and small guns range, as well as the large turret controller range
		600, // small turret controller range
		50, // sensor block range
	};

	static readonly Color BACKGROUND = Color.Black;
	static readonly Color PLANE_BACKGROUND = new Color(5, 5, 5, 255);
	static readonly Color UI_COLOR = new Color(255, 255, 255, 20);
	static readonly Color FRIEND_COLOR = Color.Green;
	static readonly Color ENEMY_COLOR = Color.Red;
	static readonly Color NEUTRAL_COLOR = Color.White;

	const int UI_LINE_WIDTH = 1; // px

	// Front sector representing the view from a cockpit.
	static readonly float FRONT_SECTOR_HALF_ANGLE = MathHelper.ToRadians(60 / 2); // Sector 60°

	// Cos of the radar *plane angle* relative to the front of LCD surface (flat if angle is 0°).
	static readonly float PROJECTION_COS = (float)Math.Cos(MathHelper.ToRadians(50)); // Radar plane tilt is 50°

	// WARN: not a setting! Do not change!
	static readonly Vector2 PROJECTION_PLANE_SCALE = new Vector2(1, PROJECTION_COS);

	// Entity Markers
	const byte MARKER_COLOR_ALPHA = 15;
	static readonly Vector2 MARKER_SIZE = new Vector2(6);
	static MySprite MARKER_PLANE = new MySprite(SpriteType.TEXTURE, "Circle", size: MARKER_SIZE * PROJECTION_PLANE_SCALE);
	static MySprite MARKER_VERTICAL = new MySprite(SpriteType.TEXTURE, "SquareSimple");
	static MySprite MARKER_SMALL_GRID = new MySprite(SpriteType.TEXTURE, "Triangle", size: MARKER_SIZE);
	static MySprite MARKER_LARGE_GRID = new MySprite(SpriteType.TEXTURE, "SquareSimple", size: MARKER_SIZE);
	static MySprite MARKER_DEFAULT_TYPE = new MySprite(SpriteType.TEXTURE, "Circle", size: MARKER_SIZE - 2);

	// End of Settings

	// To receive entities detected by *Vision* script.
	readonly IMyBroadcastListener Vision;

	// To display the radar plane with projected *Vision* entities.
	List<RadarLCD> LCDs;

	class RadarLCD {
		readonly IMyTextSurface LCD;
		readonly LCDLayout Layout;
		readonly ImmutableList<MySprite> StaticUI;
		MySpriteDrawFrame Frame;

		public RadarLCD(IMyTextSurface surface) {

			LCD = surface;
			LCD.Script = "";
			LCD.ContentType = ContentType.SCRIPT;
			LCD.ScriptBackgroundColor = BACKGROUND;
			Layout = new LCDLayout(LCD);

			StaticUI = PlaneCircles().Concat(FrontSector()).ToImmutableList();
			Prepare();
			Draw();
		}

		public void Prepare() {
			Frame = LCD.DrawFrame();
			Frame.AddRange(StaticUI);
		}

		public void Draw() {
			Frame.Dispose();
		}

		/*
		 * Adds the entity's marker sprites to the LCD `Frame`.
		 * NOTE: the `position` must be relative to the radar and be fit into the radar orientation (WorldMatrix).
		 */
		public void AddMarker(Vector3 position, MyDetectedEntityType type, MyRelationsBetweenPlayerAndBlock relation) {

			// Marker projection on a radar plane.
			var PlanePosition = new Vector2(position.X, position.Z);
			var MarkerPosition = Layout.Center + PlanePosition * Layout.Scale;
			var MarkerColor = RelationColor(relation);
			MARKER_PLANE.Position = MarkerPosition;
			MARKER_PLANE.Color = MarkerColor;
			Frame.Add(MARKER_PLANE);

			// Vertical line to indicate the height of the entity below/above the radar plane.
			var Height = position.Y * Layout.Scale.Y * 2;
			var HalfHeight = Height / 2;
			MarkerPosition.Y -= HalfHeight;
			MarkerColor.A = MARKER_COLOR_ALPHA;
			MARKER_VERTICAL.Size = new Vector2(UI_LINE_WIDTH, Height);
			MARKER_VERTICAL.Position = MarkerPosition;
			MARKER_VERTICAL.Color = MarkerColor;
			Frame.Add(MARKER_VERTICAL);

			// Icon to indicate the entity type.
			var Icon = TypeMarker(type);
			MarkerPosition.Y -= HalfHeight;
			Icon.Position = MarkerPosition;
			Icon.Color = MarkerColor;
			Frame.Add(Icon);
		}

		Color RelationColor(MyRelationsBetweenPlayerAndBlock relation) {
      switch (relation) {
				case MyRelationsBetweenPlayerAndBlock.Owner:
				case MyRelationsBetweenPlayerAndBlock.Friends:
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
					return FRIEND_COLOR;
				case MyRelationsBetweenPlayerAndBlock.Enemies:
					return ENEMY_COLOR;
				default:
					return NEUTRAL_COLOR;
			}
		}

		MySprite TypeMarker(MyDetectedEntityType type) {
      switch (type) {
				case MyDetectedEntityType.SmallGrid:
					return MARKER_SMALL_GRID;
				case MyDetectedEntityType.LargeGrid:
					return MARKER_LARGE_GRID;
				default:
					return MARKER_DEFAULT_TYPE;
			}
		}

		/*
		 * Calibrated concentric circles (planes) represents the radar ranges.
		 */
		IEnumerable<MySprite> PlaneCircles() {
			var MaxPlane = RangePlane(MAX_RADAR_RANGE);
			var Planes = PLANE_RANGES.SkipWhile(range => range >= MAX_RADAR_RANGE).Select(RangePlane);
			return Planes.Aggregate(MaxPlane, (planes, plane) => planes.Concat(plane));
		}

		/*
		 * Two concentric circles (ellipses in projection): the ring representing the given `range` and its background.
		 */
		IEnumerable<MySprite> RangePlane(int range) {
			float Scale = (float)range / MAX_RADAR_RANGE;
			var Size = Layout.ProjectionPlaneSize * Scale;
			var Circle = new MySprite(SpriteType.TEXTURE, "Circle", Layout.Center, Size, UI_COLOR);
			yield return Circle;
			Circle.Size -= UI_LINE_WIDTH;
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
		IEnumerable<MySprite> FrontSector() {
			var ViewingRange = Layout.ProjectionPlaneSize.Y / 2;
			yield return PlaneRadius(ViewingRange, FRONT_SECTOR_HALF_ANGLE);
			yield return PlaneRadius(ViewingRange, -FRONT_SECTOR_HALF_ANGLE);
		}

		/*
		 * Radius from the center of the radar plane. The `length` is a radius of a plain radar circle. If the radar plane
		 * is not plain, the `length` will be projected on the circle projection and become the radius of the ellipse.
		 *
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
			var Radius = 1 / (float)Math.Sqrt(Math.Pow(A, 2) + Math.Pow(B, 2));
			var Size = new Vector2(UI_LINE_WIDTH, Radius);
			var Offset = new Vector2(Sin, -Cos) * Radius / 2;
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
		public readonly Vector2 Scale;

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

			ProjectionPlaneSize = new Vector2(Width, Height) * PROJECTION_PLANE_SCALE;
			Scale = ProjectionPlaneSize / (MAX_RADAR_RANGE * 2);
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
			return position.Length() < MAX_RADAR_RANGE;
		}

		public Vector3 RelativePosition(Vector3 position) {
			return position - Anchor.GetPosition();
		}

		public Vector3 FitPosition(Vector3 position) {
			return Vector3.TransformNormal(position, Matrix.Transpose(Anchor.WorldMatrix));
		}
	}

	static IMyTextSurface dbg;

	public Program() {
		Runtime.UpdateFrequency = UpdateFrequency.Update10;
		Vision = IGC.RegisterBroadcastListener("Vision");
		LCDs = SelectLCDs().ConvertAll(LCD => new RadarLCD(LCD));
		Nav = new Navigation(GridTerminalSystem, Me);
	}

	public void Main(string argument, UpdateType updateSource) {
		// TODO: Nav.Update();
		LCDs.ForEach(LCD => LCD.Prepare());
		LCDsFillWithVision();
		LCDs.ForEach(LCD => LCD.Draw());
	}

	void LCDsFillWithVision() {

		// See `SerializeEntity` in the *Vision* script.
		Vision.AcceptMessage()

			.As<
				ImmutableList<
					MyTuple<
						MyTuple<long, string, int, Vector3D, bool, MatrixD>,
						MyTuple<Vector3, int, BoundingBoxD, long>>>>()

			.ForEach(tuple => {

				var EntityBounds = tuple.Item2.Item3;
				var Position = Nav.RelativePosition(EntityBounds.Center);

				if (Nav.InRadarRange(Position)) {
					LCDs.ForEach(LCD => LCD.AddMarker(
						Nav.FitPosition(Position),
						(MyDetectedEntityType)tuple.Item1.Item3,
						(MyRelationsBetweenPlayerAndBlock)tuple.Item2.Item2));
				}
			});
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