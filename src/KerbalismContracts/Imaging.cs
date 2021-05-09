using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace KerbalismContracts
{
	public class Imaging
	{
		public static bool IsVisible(
			Vector3d bodyCentredVesselPosition,
			SurfacePoint surfacePoint)
		{
			Vector3d surfaceToSatellite =
				bodyCentredVesselPosition - surfacePoint.bodyCentredSurfacePosition;
			return Vector3d.Dot(surfaceToSatellite, surfacePoint.vertical) > 0;
		}

		public struct SurfaceSatelliteGeometry
		{
			public SurfaceSatelliteGeometry(
				Vector3d bodyCentredVesselPosition,
				SurfacePoint surfacePoint)
			{
				Vector3d surfaceToSatellite =
					bodyCentredVesselPosition - surfacePoint.bodyCentredSurfacePosition;
				range = surfaceToSatellite.magnitude;
				cosZenithalAngle =
					Vector3d.Dot(surfaceToSatellite, surfacePoint.vertical) / range;
			}
			public double range;
			// The cosine of the satellite-surface-vertical angle.
			public double cosZenithalAngle;
			public bool Visible => cosZenithalAngle > 0;
		}

		public const double cos15degrees = 0.96592582628906829;
		public const double cos75degrees = 0.25881904510252076;

		public struct SunSurfaceGeometry
		{
			public static SunSurfaceGeometry FromSunPosition(
				SurfacePoint surfacePoint,
				Vector3d bodyCentredSunPosition)
			{
				Vector3d surfaceToSun = bodyCentredSunPosition - surfacePoint.bodyCentredSurfacePosition;
				Vector3d sunDirection = surfaceToSun / surfaceToSun.magnitude;
				double cosSolarZenithAngle = Vector3d.Dot(surfacePoint.vertical, sunDirection);
				return new SunSurfaceGeometry
				{
					cosSolarZenithAngle = cosSolarZenithAngle,
					reflectedRay = -sunDirection + 2 * cosSolarZenithAngle * surfacePoint.vertical
				};
			}

			public static SunSurfaceGeometry FromSunDirection(
				SurfacePoint surfacePoint,
				Vector3d bodyCentredSunDirection)
			{
				double cosSolarZenithAngle = Vector3d.Dot(surfacePoint.vertical, bodyCentredSunDirection);
				return new SunSurfaceGeometry
				{
					cosSolarZenithAngle = cosSolarZenithAngle,
					reflectedRay = -bodyCentredSunDirection + 2 * cosSolarZenithAngle * surfacePoint.vertical
				};
			}
			public Vector3d reflectedRay;
			public double cosSolarZenithAngle;
		}

		public struct SunSurfaceSatelliteGeometry
		{
			public SunSurfaceSatelliteGeometry(
				Vector3d bodyCentredVesselPosition,
				SurfacePoint surfacePoint,
				SunSurfaceGeometry sunSurfaceGeometry)
			{
				Vector3d surfaceToSatellite =
					bodyCentredVesselPosition - surfacePoint.bodyCentredSurfacePosition;
				surfaceSatelliteGeometry.range = surfaceToSatellite.magnitude;
				Vector3d satelliteDirection = surfaceToSatellite / surfaceSatelliteGeometry.range;
				surfaceSatelliteGeometry.cosZenithalAngle =
					Vector3d.Dot(satelliteDirection, surfacePoint.vertical);
				cosGlintAngle = Vector3d.Dot(satelliteDirection, sunSurfaceGeometry.reflectedRay);
			}
			public SurfaceSatelliteGeometry surfaceSatelliteGeometry;
			public double cosGlintAngle;
		}

		public void RegisterImager(ModuleImager imager)
		{
			activeImagers[imager.platform.id] = imager.properties.ToArray();
		}

		public void UnregisterImager(ModuleImager imager)
		{
			activeImagers.Remove(imager.platform.id);
		}

		public void ClearImagers()
		{
			activeImagers.Clear();
		}

		public void Update()
		{
			timeSpentInIllumination = TimeSpan.Zero;
			timeSpentInTextureUpdate = TimeSpan.Zero;
			DateTime start = DateTime.UtcNow;
			CelestialBody kerbin = FlightGlobals.GetHomeBody();
			if (reset)
			{
				reset = false;
				kerbin_imaging = null;
				window.width = 0;
				window.height = 0;
				return;
			}
			if (kerbin_imaging == null)
			{
				UnityEngine.Debug.LogWarning("Rebuilding map...");
				x_size = small ? 256 : 512;
				y_size = small ? 128 : 256;
				kerbin_imaging = new ImagingParallel[y_size];
				minimap = new UnityEngine.Texture2D(x_size, y_size);
				for (int y = 0; y < y_size; ++y)
				{
					// Mollweide on [-2, 2] × [-1, 1].
					double y_normalized = 2.0 * y / y_size - 1;
					double θ = Math.Asin(y_normalized);
					double sinφ = (2 * θ + Math.Sin(2 * θ)) / Math.PI;
					double φ = Math.Asin(sinφ);
					double φ_deg = φ * (180 / Math.PI);
					var parallel = kerbin_imaging[y] = new ImagingParallel
					{
						cosLatitude = Math.Cos(φ),
						sinLatitude = sinφ,
						map = new MapPoint[x_size],
						midInfraredStatus = new ImagingStatus[x_size],
						nearInfraredStatus = new ImagingStatus[x_size],
						unglintedVisibleStatus = new ImagingStatus[x_size],
						glintedVisibleStatus = new ImagingStatus[x_size],
						sun = new SunSurfaceGeometry[x_size],
						surface = new SurfacePoint[x_size]
					};
					bool entered_map = false;
					parallel.x_end = x_size;
					for (int x = 0; x < x_size; ++x)
					{
						double x_normalized = 4.0 * x / x_size - 2;
						double λ = Math.PI * x_normalized / (2 * Math.Cos(θ));
						double λ_deg = λ * (180 / Math.PI);

						if (double.IsNaN(φ) || double.IsNaN(λ) || Math.Abs(λ) > Math.PI)
						{
							parallel.map[x].on_map = false;
							if (entered_map && parallel.x_end == x_size)
							{
								parallel.x_end = x;
							}
						}
						else
						{
							parallel.map[x].on_map = true;
							if (!entered_map)
							{
								parallel.x_begin = x;
							}
							entered_map = true;
							double altitude = kerbin.TerrainAltitude(φ_deg, λ_deg);
							parallel.surface[x] = new SurfacePoint(
								kerbin.scaledBody.transform.rotation.Inverse() *
								(kerbin.GetWorldSurfacePosition(φ_deg, λ_deg, altitude) - kerbin.position));
							parallel.map[x].ocean = altitude == 0;
							parallel.midInfraredStatus[x].lastImagingTime = new double[resolutionThresholds.Length];
							parallel.nearInfraredStatus[x].lastImagingTime = new double[resolutionThresholds.Length];
							parallel.glintedVisibleStatus[x].lastImagingTime = new double[resolutionThresholds.Length];
							parallel.unglintedVisibleStatus[x].lastImagingTime = new double[resolutionThresholds.Length];
							for (int i = 0; i < resolutionThresholds.Length; ++i)
							{
								parallel.midInfraredStatus[x].lastImagingTime[i] = double.NegativeInfinity;
								parallel.nearInfraredStatus[x].lastImagingTime[i] = double.NegativeInfinity;
								parallel.glintedVisibleStatus[x].lastImagingTime[i] = double.NegativeInfinity;
								parallel.unglintedVisibleStatus[x].lastImagingTime[i] = double.NegativeInfinity;
							}
						}
					}
				}
			}
			var imagers = activeImagers.ToArray();
			double Δt = 2 * 60;
			if (lastUpdateUT == null || kerbin.scaledBody.transform.rotation == null)
			{
				lastUpdateUT = Planetarium.GetUniversalTime();
				lastKerbinRotation = kerbin.scaledBody.transform.rotation;
			}
			var current_kerbin_to_world = kerbin.scaledBody.transform.rotation;
			for (int n = (int)Math.Floor((Planetarium.GetUniversalTime() - lastUpdateUT.Value) / Δt); n >= 0; --n)
			{
				double t = Planetarium.GetUniversalTime() - n * Δt;
				// TODO(egg): This will fail hilariously if the interval between updates is greater than half a day.
				// It is probably broken in other ways.
				var kerbin_to_world = UnityEngine.Quaternion.Slerp(
					lastKerbinRotation.Value, current_kerbin_to_world,
					(float)((t - lastUpdateUT) / (Planetarium.GetUniversalTime() - lastUpdateUT)));
				var world_to_kerbin = kerbin_to_world.Inverse();
				double kerbin_radius = kerbin.Radius;
				var kerbin_world_position = kerbin.orbit.getPositionAtUT(t);
				Vector3d sunInSurfaceFrame = world_to_kerbin *
					(kerbin.referenceBody.getPositionAtUT(t) - kerbin_world_position);
				Vector3d sunDirection = sunInSurfaceFrame.normalized;
				for (int y = 0; y != y_size; ++y)
				{
					var parallel = kerbin_imaging[y];
					for (int x = parallel.x_begin; x != parallel.x_end; ++x)
					{
						if (solarParallax)
						{
							parallel.sun[x] = SunSurfaceGeometry.FromSunPosition(
								parallel.surface[x],
								sunInSurfaceFrame);
						}
						else
						{
							parallel.sun[x] = SunSurfaceGeometry.FromSunDirection(
								parallel.surface[x],
								sunDirection);
						}
					}
				}
				foreach (var imager in imagers)
				{
					Vessel platform = FlightGlobals.FindVessel(imager.Key);
					ImagerProperties[] instruments = imager.Value;
					Vector3d vesselFromKerbinInWorld;
					if (platform.orbit.referenceBody == kerbin)
					{
						var vesselFromKerbinInAlice = platform.orbit.getRelativePositionAtUT(t);
						vesselFromKerbinInWorld = vesselFromKerbinInAlice.xzy;
					}
					else
					{
						Vector3d vesselFromSunInAlice = Vector3d.zero;
						Vector3d kerbinFromSunInAlice = Vector3d.zero;
						for (var orbit = platform.orbit; orbit != null; orbit = orbit.referenceBody?.orbit)
						{
							vesselFromSunInAlice += orbit.getRelativePositionAtUT(t);
						}
						for (var orbit = kerbin.orbit; orbit != null; orbit = orbit.referenceBody?.orbit)
						{
							kerbinFromSunInAlice += orbit.getRelativePositionAtUT(t);
						}
						vesselFromKerbinInWorld = (vesselFromSunInAlice - kerbinFromSunInAlice).xzy;
					}
					Vector3d vesselInSurfaceFrame = world_to_kerbin * vesselFromKerbinInWorld;
					double xVessel = (0.5 + kerbin.GetLongitude(
						kerbin.position + current_kerbin_to_world * world_to_kerbin * vesselFromKerbinInWorld) / 360) % 1;
					for (int y = 0; y != y_size; ++y)
					{
						var parallel = kerbin_imaging[y];
						var closestPoint = parallel.surface[
							(int)(parallel.x_begin + xVessel * (parallel.x_end - parallel.x_begin))];
						if (!IsVisible(
							vesselInSurfaceFrame,
							new SurfacePoint(closestPoint.vertical * kerbin_radius)))
						{
							continue;
						}
						for (int x = parallel.x_begin; x != parallel.x_end; ++x)
						{
							SunSurfaceSatelliteGeometry geometry = new SunSurfaceSatelliteGeometry(
								vesselInSurfaceFrame,
								parallel.surface[x],
								parallel.sun[x]);
							if (geometry.surfaceSatelliteGeometry.Visible)
							{
								foreach (var instrument in instruments)
								{
									bool sunlit = parallel.sun[x].cosSolarZenithAngle > cos75degrees;
									if (!sunlit && instrument.band != OpticalBand.MidInfrared)
									{
										continue;
									}
									double resolution = instrument.HorizontalResolution(geometry.surfaceSatelliteGeometry);
									for (int i = 0; i < resolutionThresholds.Length; ++i)
									{
										if (instrument.HorizontalResolution(geometry.surfaceSatelliteGeometry) <
											resolutionThresholds[i])
										{
											for (; i < resolutionThresholds.Length; ++i)
											{
												switch (instrument.band)
												{
													case OpticalBand.MidInfrared:
														parallel.midInfraredStatus[x].lastImagingTime[i] =
															Math.Max(parallel.midInfraredStatus[x].lastImagingTime[i], t);
														break;
													case OpticalBand.NearInfrared:
														parallel.nearInfraredStatus[x].lastImagingTime[i] =
															Math.Max(parallel.nearInfraredStatus[x].lastImagingTime[i], t);
														break;
													case OpticalBand.Visible:
														if (geometry.cosGlintAngle < cos15degrees)
														{
															parallel.glintedVisibleStatus[x].lastImagingTime[i] =
																Math.Max(parallel.glintedVisibleStatus[x].lastImagingTime[i], t);
														}
														else
														{
															parallel.unglintedVisibleStatus[x].lastImagingTime[i] =
																Math.Max(parallel.unglintedVisibleStatus[x].lastImagingTime[i], t);
														}
														break;
												}
											}
										}
									}
								}
							}
						}
					}
				}
				lastUpdateUT = t;
				lastKerbinRotation = current_kerbin_to_world;
			}

			RefreshMap();

			minimap.Apply(updateMipmaps: false);
			timeSpentInUpdate = DateTime.UtcNow - start;
		}

		public void RefreshMap()
		{
			double t = Planetarium.GetUniversalTime();
			DateTime textureUpdateStart = DateTime.UtcNow;
			var pixels = minimap.GetRawTextureData<UnityEngine.Color32>();
			UnityEngine.Color32 black = XKCDColors.Black;
			UnityEngine.Color32 lightSeafoam = XKCDColors.LightSeafoam;
			UnityEngine.Color32 sea = XKCDColors.Sea;
			UnityEngine.Color32 red = XKCDColors.Red;
			UnityEngine.Color32 orangered = XKCDColors.Orangered;
			UnityEngine.Color32 orange = XKCDColors.Orange;
			UnityEngine.Color32 yellow = XKCDColors.Yellow;
			UnityEngine.Color32 white = XKCDColors.White;
			UnityEngine.Color32 grey = XKCDColors.Grey;
			int pixel = 0;
			int map_pixels = 0;
			int[] freshnessPixelCoverage = new int[freshnessCoverage.Length];
			int[] resolutionPixelCoverage = new int[resolutionCoverage.Length];
			for (int y = 0; y != y_size; ++y)
			{
				var parallel = kerbin_imaging[y];
				for (int x = 0; x != x_size; ++x)
				{
					var map = parallel.map[x];
					var unglintedStatus = mapBand switch
					{
						OpticalBand.MidInfrared => parallel.midInfraredStatus[x],
						OpticalBand.NearInfrared => parallel.nearInfraredStatus[x],
						OpticalBand.Visible => parallel.unglintedVisibleStatus[x],
						_ => throw new ArgumentException($"Unexpected map band {mapBand}")
					};
					var glintedStatus = mapBand switch
					{
						OpticalBand.MidInfrared => parallel.midInfraredStatus[x],
						OpticalBand.NearInfrared => parallel.nearInfraredStatus[x],
						OpticalBand.Visible => parallel.glintedVisibleStatus[x],
						_ => throw new ArgumentException($"Unexpected map band {mapBand}")
					};
					if (!map.on_map)
					{
						pixels[pixel] = black;
					}
					else
					{
						++map_pixels;
						if (mapType == MapType.Freshness)
						{
							double leastImagingAge = double.PositiveInfinity;
							if (mapBand != OpticalBand.Visible || showUnglinted)
							{
								leastImagingAge = Math.Min(
									leastImagingAge, t - unglintedStatus.lastImagingTime[chosenResolutionIndex]);
							}
							if (mapBand == OpticalBand.Visible && showGlinted)
							{
								leastImagingAge = Math.Min(
									leastImagingAge, t - glintedStatus.lastImagingTime[chosenResolutionIndex]);
							}
							if (leastImagingAge == double.PositiveInfinity)
							{
								pixels[pixel] = grey;
							}
							else
							{
								for (int i = 0; i < freshnessThresholds.Length; ++i)
								{
									if (leastImagingAge <= freshnessThresholds[i])
									{
										pixels[pixel] = freshnessColours[i];
										for (int j = i; j < freshnessThresholds.Length; ++j)
										{
											++freshnessPixelCoverage[j];
										}
										break;
									}
								}
							}
						}
						else if (mapType == MapType.Resolution)
						{
							int finestResolutionIndex = resolutionThresholds.Length;
							if (mapBand != OpticalBand.Visible || showUnglinted)
							{
								for (int i = 0; i < resolutionThresholds.Length; ++i)
								{
									if (t - unglintedStatus.lastImagingTime[i] <= chosenFreshness)
									{
										finestResolutionIndex = Math.Min(
											finestResolutionIndex, i);
										break;
									}
								}
							}
							if (mapBand == OpticalBand.Visible && showGlinted)
							{
								for (int i = 0; i < resolutionThresholds.Length; ++i)
								{
									if (t - glintedStatus.lastImagingTime[i] <= chosenFreshness)
									{
										finestResolutionIndex = Math.Min(
											finestResolutionIndex, i);
										break;
									}
								}
							}
							if (finestResolutionIndex == resolutionThresholds.Length)
							{
								pixels[pixel] = grey;
							}
							else
							{
								pixels[pixel] = resolutionColours[finestResolutionIndex];
								for (int i = finestResolutionIndex; i < resolutionThresholds.Length; ++i)
								{
									++resolutionPixelCoverage[i];
								}
							}
						}
						if (map.ocean)
						{
							UnityEngine.Color32 blue = pixels[pixel];
							blue.b = 255;
							pixels[pixel] = blue;
						}
					}
					++pixel;
				}
			}
			timeSpentInTextureUpdate += DateTime.UtcNow - textureUpdateStart;
			for (int i = 0; i < resolutionThresholds.Length; ++i)
			{
				resolutionCoverage[i] = (double)resolutionPixelCoverage[i] / map_pixels;
			}
			for (int i = 0; i < freshnessThresholds.Length; ++i)
			{
				freshnessCoverage[i] = (double)freshnessPixelCoverage[i] / map_pixels;
			}
		}

		public void DrawDebugUI()
		{
			window = UnityEngine.GUILayout.Window(GetHashCode(), window, DrawDebugWindow, "Imaging");
		}

		private void DrawGlintSelector()
		{
			using (new UnityEngine.GUILayout.HorizontalScope())
			{
				UnityEngine.GUILayout.Label("Sunglint (visible only): ");
				showGlinted = UnityEngine.GUILayout.Toggle(showGlinted, "affected");
				showUnglinted = UnityEngine.GUILayout.Toggle(showUnglinted, "unaffected");
			}
		}

		private void DrawResolutionSelector()
		{
			using (new UnityEngine.GUILayout.HorizontalScope())
			{
				for (int i = 0; i < resolutionThresholds.Length; ++i)
				{
					if (UnityEngine.GUILayout.Toggle(
						i == chosenResolutionIndex,
						resolutionThresholds[i] > 1000
							? $"{resolutionThresholds[i] / 1000:N0} km"
							: $"{resolutionThresholds[i]:N0} m"))
					{
						chosenResolutionIndex = i;
					}
				}
			}
		}

		private void DrawFreshnessSelector()
		{
			using (new UnityEngine.GUILayout.HorizontalScope())
			{
				for (int i = 0; i < freshnessThresholds.Length; ++i)
				{
					if (UnityEngine.GUILayout.Toggle(
						i == chosenFreshnessIndex,
						freshnessThresholds[i] > 3600
							? $"{freshnessThresholds[i] / 3600:N0} h"
							: "current"))
					{
						chosenFreshnessIndex = i;
					}
				}
			}
		}

		private void DrawMapTypeSelector()
		{
			using (new UnityEngine.GUILayout.HorizontalScope())
			{
				UnityEngine.GUILayout.Label("Map: ");
				if (UnityEngine.GUILayout.Toggle(mapType == MapType.Freshness, "Freshness"))
				{
					mapType = MapType.Freshness;
				}
				if (UnityEngine.GUILayout.Toggle(mapType == MapType.Resolution, "Resolution"))
				{
					mapType = MapType.Resolution;
				}
			}
		}

		private void DrawBandSelector()
		{
			using (new UnityEngine.GUILayout.HorizontalScope())
			{
				UnityEngine.GUILayout.Label("Band: ");
				if (UnityEngine.GUILayout.Toggle(mapBand == OpticalBand.MidInfrared, "Mid-IR (emissive)"))
				{
					mapBand = OpticalBand.MidInfrared;
				}
				if (UnityEngine.GUILayout.Toggle(mapBand == OpticalBand.NearInfrared, "Near-IR (reflective)"))
				{
					mapBand = OpticalBand.NearInfrared;
				}
				if (UnityEngine.GUILayout.Toggle(mapBand == OpticalBand.Visible, "Visible (reflective)"))
				{
					mapBand = OpticalBand.Visible;
				}
			}
		}

		private string CoverageSummary()
		{
			string result = "";
			switch (mapType)
			{
				case MapType.Freshness:
					result = $@"{mapBand} at {
						(chosenResolution > 1000 ? $"{chosenResolution / 1000:N0} km" : $"{chosenResolution:N0} m")}";
					for (int i = 0; i < freshnessThresholds.Length; ++i)
					{
						result += $@" | {(freshnessThresholds[i] > 3600
								? $"{freshnessThresholds[i] / 3600:N0} h"
								: "current")} ({freshnessCoverage[i]:P1})";
					}
					break;
				case MapType.Resolution:
					result = $@"{mapBand} at {
						(chosenFreshness > 3600 ? $"{chosenFreshness / 3600:N0} h" : $" current time")}";
					for (int i = 0; i < resolutionThresholds.Length; ++i)
					{
						result += $@" | {(resolutionThresholds[i] > 1000
								? $"{resolutionThresholds[i] / 1000:N0} km"
								: $"{resolutionThresholds[i]:N0} m")} ({resolutionCoverage[i]:P1})";
					}
					break;
			};
			return result;
		}

		public void DrawDebugWindow(int id)
		{
			using (new UnityEngine.GUILayout.VerticalScope())
			{
				UnityEngine.GUILayout.TextArea($"{activeImagers.Count} active imagers");
				UnityEngine.GUILayout.TextArea($"Update: {timeSpentInUpdate.TotalMilliseconds} ms");
				solarParallax = UnityEngine.GUILayout.Toggle(solarParallax, "Solar parallax");
				reset |= UnityEngine.GUILayout.Button("Reset");
				small = UnityEngine.GUILayout.Toggle(small, "Small map (effective on reset)");
				DrawMapTypeSelector();
				DrawBandSelector();
				DrawGlintSelector();
				switch (mapType)
				{
					case MapType.Freshness:
						DrawResolutionSelector();
						break;
					case MapType.Resolution:
						DrawFreshnessSelector();
						break;
				}
				UnityEngine.GUILayout.TextArea(CoverageSummary());
				UnityEngine.GUILayout.Box(minimap);
			}
			UnityEngine.GUI.DragWindow();
		}

		private UnityEngine.Rect window;
		class ImagingParallel
		{
			public double cosLatitude;
			public double sinLatitude;
			public int x_begin;
			public int x_end;
			public MapPoint[] map;
			public ImagingStatus[] midInfraredStatus;
			public ImagingStatus[] nearInfraredStatus;
			public ImagingStatus[] unglintedVisibleStatus;
			public ImagingStatus[] glintedVisibleStatus;
			public SunSurfaceGeometry[] sun;
			public SurfacePoint[] surface;
		}
		struct MapPoint
		{
			public bool on_map;
			public bool ocean;
		}
		struct ImagingStatus
		{
			public double[] lastImagingTime;
		}
		public struct SurfacePoint
		{
			public SurfacePoint(Vector3d bodyCentredSurfacePosition)
			{
				this.bodyCentredSurfacePosition = bodyCentredSurfacePosition;
				vertical = bodyCentredSurfacePosition.normalized;
			}
			public Vector3d bodyCentredSurfacePosition;
			// Equal to |bodyCentredSurfacePosition.normalized|.
			public Vector3d vertical;
		}
		private int x_size;
		private int y_size;
		private bool reset = true;
		private ImagingParallel[] kerbin_imaging;
		private UnityEngine.Texture2D minimap;
		private double? lastUpdateUT;
		private static bool small = false;
		private static bool solarParallax;

		private static OpticalBand mapBand;
		private bool showGlinted;
		private static bool showUnglinted;
		private enum MapType
		{
			Resolution,
			Freshness,
		}
		private static readonly double[] freshnessThresholds =
			new double[] { 1, 3 * 3600, 6 * 3600, 12 * 3600, 24 * 3600, 48 * 3600 };
		private static readonly UnityEngine.Color32[] freshnessColours = new UnityEngine.Color32[] {
			XKCDColors.Red, XKCDColors.OrangeRed, XKCDColors.Orange,
			XKCDColors.OrangeYellow, XKCDColors.Yellow, XKCDColors.White};
		private static int chosenFreshnessIndex;
		private double chosenFreshness => freshnessThresholds[chosenFreshnessIndex];
		private double[] freshnessCoverage = new double[6];

		private static readonly double[] resolutionThresholds = new double[] { 1, 10, 100, 1e3, 10e3 };
		private static readonly UnityEngine.Color32[] resolutionColours = new UnityEngine.Color32[] {
			XKCDColors.Green, XKCDColors.YellowishGreen, XKCDColors.GreenishYellow, XKCDColors.Yellow, XKCDColors.White};
		private static int chosenResolutionIndex;
		private double chosenResolution => resolutionThresholds[chosenResolutionIndex];
		private double[] resolutionCoverage = new double[5];
		private static MapType mapType;

		private UnityEngine.Quaternion? lastKerbinRotation;
		private TimeSpan timeSpentInUpdate;
		private TimeSpan timeSpentInTextureUpdate;
		private TimeSpan timeSpentInIllumination;
		private readonly Dictionary<Guid, ImagerProperties[]> activeImagers = new Dictionary<Guid, ImagerProperties[]>();
	}
}
