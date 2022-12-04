using System;
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

namespace Radar {

/*
 * Receives *Vision* entities using ICG, then represents them as detected entities on the radar plane and displays on
 * specified LCDs.
 */
public sealed class Program : MyGridProgram {

	#region Radar

	readonly static Color BACKGROUND = Color.Black;
	readonly static Color PLANE_BACKGROUND = new Color(5, 5, 5, 255);
	readonly static Color LINE_COLOR = new Color(255, 255, 255, 20);
	const float MAIN_LINE_WIDTH = 0.2f;
	const float LINE_WIDTH = 0.5f;

	// Front sector representing the view from a cockpit.
	readonly static float FRONT_SECTOR_HALF_ANGLE = MathHelper.ToRadians(60 / 2);

	// Cos of the radar plane angle relative to the LCD surface (radar plane is flat if angle is 0°).
	readonly static float PROJECTION_COS = (float)Math.Cos(MathHelper.ToRadians(50));

	// NOTE: should be > 0!
	const int MAX_RADAR_RANGE = 1400; // m

	// NOTE: should be sorted in descending order!
	readonly static int[] PLANE_RANGES = { // m
		2000, // big railgun range
		1400, // big turret max range
		1200, // big turret min range
		800, // small turret max range
		600, // small turret min range
		50, // sensor block max range
	};

	// To receive entities detected by *Vision*.
	IMyBroadcastListener Vision;

	// To display *Vision* entities.
	List<RadarLCD> Radars;

	class RadarLCD {

		// Where the radar should be displayed.
		IMyTextSurface LCD;
		MySpriteDrawFrame Frame;

		LCDLayout Layout;
		Vector2 PlaneMaxSize;
		Vector2 PlanePosition;

		public RadarLCD(IMyTextSurface lcd) {

			LCD = lcd;
			LCD.Script = "";
			LCD.ContentType = ContentType.SCRIPT;
			LCD.ScriptBackgroundColor = BACKGROUND;
			LCD.ClearImagesFromSelection();
			Frame = LCD.DrawFrame();

			Layout = new LCDLayout(LCD);
			PlaneMaxSize = new Vector2(Layout.Width, Layout.Height * PROJECTION_COS);
			PlanePosition = Layout.Center;

			Redraw();
		}

		void Redraw() {
			Frame.AddRange(Static);
			Frame.Dispose();
		}

		IEnumerable<MySprite> Static { get {
			return Planes.Concat(FrontSector);
		}}

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
			var Size = PlaneMaxSize * Scale;
			yield return new MySprite(SpriteType.TEXTURE, "Circle", Layout.Center, Size, LINE_COLOR);
			yield return new MySprite(SpriteType.TEXTURE, "Circle", Layout.Center, Size - MAIN_LINE_WIDTH, PLANE_BACKGROUND);
		}

		/*
		 *   \      /
		 *    \    /
		 *     \  /
		 *      \/
		 *       * radar center
		 */
		IEnumerable<MySprite> FrontSector { get {
			var ViewingRange = PlaneMaxSize.Y / 2;
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
		 *    * radar center
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
			return new MySprite(SpriteType.TEXTURE, "SquareSimple", Position, Size, LINE_COLOR, rotation: angle);
		}
	}

	struct LCDLayout {

		public readonly float Padding;
		public readonly float Height;
		public readonly float Width;
		public readonly Vector2 Center;

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
		 * LCD Width >= Height, so `LCD.TextureSize.Y >= LCD.SurfaceSize.Y` and `LCD.TextureSize.X == LCD.SurfaceSize.X`.
		 *
		 * NOTE: expects that `TextAlignment.CENTER` is set for texture sprites!
		 * NOTE: right half of wide LCD panels will be empty.
		 */
		public LCDLayout(IMyTextSurface LCD) {

			Padding = LCD.SurfaceSize.X / 50; // 2% of LCD width

			var empty = Padding * 2;
			Height = LCD.SurfaceSize.Y - empty;
			Width = LCD.TextureSize.Y - empty;

			// `Width` is chosen to positioning in the big `LCD.TextureSize` square as the largest dimension.
			Center = new Vector2(Padding + Width / 2);
		}
	}

	public Program() {
		Runtime.UpdateFrequency = UpdateFrequency.Update10;
		Vision = IGC.RegisterBroadcastListener("Vision");
		Radars = LCDs.ConvertAll(lcd => new RadarLCD(lcd));
	}

	public void Main(string argument, UpdateType updateSource) {
	}

	IEnumerable<MyDetectedEntityInfo> VisionEntities { get {
		return Vision.AcceptMessage().As<
			IEnumerable<
				MyTuple<
					MyTuple<long, string, int, Vector3D, bool, MatrixD>,
					MyTuple<Vector3, int, BoundingBoxD, long>>>>()
			.Select(DeserializeVisionEntity);
	}}

	/*
	 * See *Vision* `SerializeEntity` to understand deserialization.
	 */
	MyDetectedEntityInfo DeserializeVisionEntity(
			MyTuple<
				MyTuple<long, string, int, Vector3D, bool, MatrixD>,
				MyTuple<Vector3, int, BoundingBoxD, long>>
			serialized) {
		return new MyDetectedEntityInfo(
			entityId: serialized.Item1.Item1,
			name: serialized.Item1.Item2,
			type: (MyDetectedEntityType)serialized.Item1.Item3,
			hitPosition: serialized.Item1.Item5 ? serialized.Item1.Item4 : (Vector3D?)null,
			orientation: serialized.Item1.Item6,
			velocity: serialized.Item2.Item1,
			relationship: (MyRelationsBetweenPlayerAndBlock)serialized.Item2.Item2,
			boundingBox: serialized.Item2.Item3,
			timeStamp: serialized.Item2.Item4);
	}

	List<IMyTextSurface> LCDs { get {

			// LCD, just for testing.
			var lcds = new List<IMyTextSurface>();
			lcds.Add((GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit).GetSurface(0));
			lcds.Add((GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit).GetSurface(3));
			lcds.Add((GridTerminalSystem.GetBlockWithName("TestPanel") as IMyTextPanel));
			lcds.Add((GridTerminalSystem.GetBlockWithName("Wide LCD") as IMyTextPanel));
			lcds.Add((GridTerminalSystem.GetBlockWithName("LCD Panel") as IMyTextPanel));

			// Echo available sprite names.
			var s = new List<string>();
			lcds[0].GetSprites(s);
			Echo(String.Join("\n", s));
			return lcds;
	}}

	#endregion // Radar
}}