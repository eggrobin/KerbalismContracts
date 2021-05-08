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
		public struct SurfaceSatelliteGeometry {
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
		}
		
		public const double cos15degrees = 0.96592582628906829;
		public const double cos75degrees = 0.25881904510252076;

		public struct SunSurfaceGeometry {
			public SunSurfaceGeometry(
				SurfacePoint surfacePoint,
				Vector3d bodyCentredSunPosition)
			{
				Vector3d surfaceToSun = bodyCentredSunPosition - surfacePoint.bodyCentredSurfacePosition;
				Vector3d sunDirection = surfaceToSun / surfaceToSun.magnitude;
				cosSolarZenithAngle = Vector3d.Dot(surfacePoint.vertical, sunDirection);
				reflectedRay = -sunDirection + 2 * cosSolarZenithAngle * surfacePoint.vertical;
			}
			public Vector3d reflectedRay;
			public double cosSolarZenithAngle;
		}

		public struct SunSurfaceSatelliteGeometry {
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
			activeImagers[imager.platform.id] = imager.properties;
		}

		public void UnregisterImager(ModuleImager imager) {
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
			if (reset) {
				reset = false;
				kerbin_imaging = null;
				return;
			}
			if (kerbin_imaging == null)
			{
				UnityEngine.Debug.LogWarning("Rebuilding map...");
				x_size = 512;
				y_size = 256;
				kerbin_imaging = new ImagingParallel[y_size];
				lowResImagingMap = new UnityEngine.Texture2D(x_size, y_size);
				for (int y = 0; y < y_size; ++y)
				{
					// Mollweide on [-2, 2] × [-1, 1].
					double y_normalized = 2.0 * y / y_size - 1;
					double θ = Math.Asin(y_normalized);
					double sinφ = (2 * θ + Math.Sin(2 * θ)) / Math.PI;
					double φ = Math.Asin(sinφ);
					double φ_deg = φ * (180 / Math.PI);
					var parallel = kerbin_imaging[y] = new ImagingParallel{
						cosLatitude = Math.Cos(φ),
						sinLatitude = sinφ,
						status = new ImagingStatus[x_size],
						sun = new SunSurfaceGeometry[x_size],
						surface = new SurfacePoint[x_size]};
					bool entered_map = false;
					parallel.x_end = x_size;
					for (int x = 0; x < x_size; ++x)
					{
						double x_normalized = 4.0 * x / x_size - 2;
						double λ = Math.PI * x_normalized / (2 * Math.Cos(θ));
						double λ_deg = λ * (180 / Math.PI);

						if (double.IsNaN(φ) || double.IsNaN(λ) || Math.Abs(λ) > Math.PI)
						{
							parallel.status[x].on_map = false;
							if (entered_map && parallel.x_end == x_size)
							{
								parallel.x_end = x;
							}
						}
						else
						{
							parallel.status[x].on_map = true;
							if (!entered_map)
							{
								parallel.x_begin = x;
							}
							entered_map = true;
							double altitude = kerbin.TerrainAltitude(φ_deg, λ_deg);
							parallel.surface[x] = new SurfacePoint(
								kerbin.scaledBody.transform.rotation.Inverse() *
								(kerbin.GetWorldSurfacePosition(φ_deg, λ_deg, altitude) - kerbin.position));
							parallel.status[x].ocean = altitude == 0;
						}
					}
				}
			}
			var imagers = activeImagers.ToArray();
			double Δt = 2 * 60;
			if (lastUpdateUT == null || kerbin.scaledBody.transform.rotation == null) {
				lastUpdateUT = Planetarium.GetUniversalTime();
				lastKerbinRotation = kerbin.scaledBody.transform.rotation;
			}
			var current_kerbin_to_world = kerbin.scaledBody.transform.rotation;
			for (int n = (int)Math.Floor((Planetarium.GetUniversalTime() - lastUpdateUT.Value) / Δt); n >= 0; --n) {
				double t = Planetarium.GetUniversalTime() - n * Δt;
				// TODO(egg): This will fail hilariously if the interval between updates is greater than half a day.
				// It is probably broken in other ways.
				var kerbin_to_world = UnityEngine.Quaternion.Slerp(
					lastKerbinRotation.Value, current_kerbin_to_world,
					(float)((t - lastUpdateUT) / (Planetarium.GetUniversalTime() - lastUpdateUT)));
				var world_to_kerbin = kerbin_to_world.Inverse();
				double kerbin_radius = kerbin.Radius;/*
				var kerbin_world_position = kerbin.orbit.getPositionAtUT(t);
				Vector3d sunInSurfaceFrame = world_to_kerbin *
					(kerbin.referenceBody.getPositionAtUT(t) -kerbin_world_position);
				DateTime startIllumination = DateTime.UtcNow;
				for (int y = 0; y != y_size; ++y)
				{
					var parallel = kerbin_imaging[y];
					for (int x = parallel.x_begin; x != parallel.x_end; ++x)
					{
						//parallel.status[x].bestResolution = double.PositiveInfinity;
						parallel.status[x].glint = false;
						parallel.sun[x] = new SunSurfaceGeometry(
							parallel.surface[x],
							sunInSurfaceFrame);
					}
				}
				DateTime stopIllumination = DateTime.UtcNow;*/
				foreach (var imager in imagers)
				{
					Vessel platform = FlightGlobals.FindVessel(imager.Key);
					var vesselFromKerbinInAlice = platform.orbit.getRelativePositionAtUT(t);
					var vesselFromKerbinInWorld = vesselFromKerbinInAlice.xzy;
					Vector3d vesselInSurfaceFrame = world_to_kerbin * vesselFromKerbinInWorld;
					double xVessel = (0.5 + kerbin.GetLongitude(
						kerbin.position + current_kerbin_to_world * world_to_kerbin * vesselFromKerbinInWorld) / 360) % 1;
					for (int y = 0; y != y_size; ++y)
					{
						var parallel = kerbin_imaging[y];
						var closestPoint = parallel.surface[
							(int)(parallel.x_begin + xVessel * (parallel.x_end - parallel.x_begin))];
						if (!imager.Value.IsVisibleFrom(new SurfaceSatelliteGeometry(
								vesselInSurfaceFrame,
								new SurfacePoint(closestPoint.vertical * kerbin_radius))))
						{
							continue;
						}
						for (int x = parallel.x_begin; x != parallel.x_end; ++x)
						{
							SunSurfaceSatelliteGeometry geometry = new SunSurfaceSatelliteGeometry(
								vesselInSurfaceFrame,
								parallel.surface[x],
								parallel.sun[x]);
							if (imager.Value.IsVisibleFrom(geometry.surfaceSatelliteGeometry))
							{
								if(imager.Value.HorizontalResolution(geometry.surfaceSatelliteGeometry) < 10e3)
								{
									parallel.status[x].last10kmImagingTime = Math.Max(parallel.status[x].last10kmImagingTime, t);
								}
							}
						}
					}
				}

				DateTime textureUpdateStart = DateTime.UtcNow;
				var lowResPixels = lowResImagingMap.GetRawTextureData<UnityEngine.Color32>();
				UnityEngine.Color32 black = XKCDColors.Black;
				UnityEngine.Color32 lightSeafoam = XKCDColors.LightSeafoam;
				UnityEngine.Color32 sea = XKCDColors.Sea;
				UnityEngine.Color32 red = XKCDColors.Red;
				UnityEngine.Color32 orangered = XKCDColors.Orangered;
				UnityEngine.Color32 orange = XKCDColors.Orange;
				UnityEngine.Color32 yellow = XKCDColors.Yellow;
				UnityEngine.Color32 white = XKCDColors.White;
				UnityEngine.Color32 grey = XKCDColors.Grey;
				int i = 0;
				int map_pixels = 0;
				int covered_pixels6h = 0;
				int covered_pixels3h = 0;
				int covered_pixelsNow = 0;
				for (int y = 0; y != y_size; ++y)
				{
					var parallel = kerbin_imaging[y];
					for (int x = 0; x != x_size; ++x)
					{
						var status = parallel.status[x];
						if (!status.on_map)
						{
							lowResPixels[i] = black;
						}
						else
						{
							++map_pixels;
							{
								if (t - status.last10kmImagingTime <= 60) {
									++covered_pixelsNow;
									++covered_pixels3h;
									++covered_pixels6h;
									lowResPixels[i] = red;
								} else if (t - status.last10kmImagingTime  <= 60 * 60 * 3) {
									++covered_pixels3h;
									++covered_pixels6h;
									lowResPixels[i] = orangered;
								} else if (t - status.last10kmImagingTime  <= 60 * 60 * 6) {
									++covered_pixels6h;
									lowResPixels[i] = orange;
								} else {
									lowResPixels[i] = grey;
								}
							}
							if (status.ocean)
							{
								UnityEngine.Color32 blue = lowResPixels[i];
								blue.b = 255;
								lowResPixels[i] = blue;
							}
						}
						++i;
					}
				}
				//timeSpentInIllumination += stopIllumination - startIllumination;
				timeSpentInTextureUpdate += DateTime.UtcNow - textureUpdateStart;
				coverageNow = (double)covered_pixelsNow / map_pixels;
				coverage3h = (double)covered_pixels3h / map_pixels;
				coverage6h = (double)covered_pixels6h / map_pixels;
				lastUpdateUT = t;
				lastKerbinRotation = current_kerbin_to_world;
			}
			lowResImagingMap.Apply(updateMipmaps: false);
			timeSpentInUpdate = DateTime.UtcNow - start;
		}

		public void DrawDebugUI()
		{
			window = UnityEngine.GUILayout.Window(GetHashCode(), window, DrawDebugWindow, "Imaging");
		}

		public void DrawDebugWindow(int id)
		{
			using (new UnityEngine.GUILayout.VerticalScope())
			{
				UnityEngine.GUILayout.TextArea($"{activeImagers.Count} active imagers");
				UnityEngine.GUILayout.TextArea($"Update: {timeSpentInUpdate.TotalMilliseconds} ms");
				//UnityEngine.GUILayout.TextArea($"> illumination: {timeSpentInIllumination.TotalMilliseconds} ms");
				UnityEngine.GUILayout.TextArea($"> texture: {timeSpentInTextureUpdate.TotalMilliseconds} ms");
				reset |= UnityEngine.GUILayout.Button("Reset");
				UnityEngine.GUILayout.TextArea(
					$@"1 μm at 10 km spatial resolution, current {
					coverageNow:P1} / 3 h ({coverage3h:P1}) / 6 h ({coverage6h:P1})");
				UnityEngine.GUILayout.Box(lowResImagingMap);
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
			public ImagingStatus[] status;
			public SunSurfaceGeometry[] sun;
			public SurfacePoint[] surface;
		}
		struct ImagingStatus
		{
			public bool on_map;
			public bool ocean;
			//public bool glint;
			public double last10kmImagingTime;
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
		private UnityEngine.Texture2D lowResImagingMap;
		private double coverageNow;
		private double coverage3h;
		private double coverage6h;
		private double? lastUpdateUT;
		private UnityEngine.Quaternion? lastKerbinRotation;
		private TimeSpan timeSpentInUpdate;
		private TimeSpan timeSpentInTextureUpdate;
		private TimeSpan timeSpentInIllumination;
		private readonly Dictionary<Guid, ImagerProperties> activeImagers = new Dictionary<Guid, ImagerProperties>();
		private static readonly double[] resolutionThresholds = new double[] { 1000, 100, 10, 1 };
	}
}
