using System;
using System.Collections.Generic;
using System.Linq;

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
				absSinSwathAngle = 0;
			}

			public SurfaceSatelliteGeometry(
				Vector3d bodyCentredVesselPosition,
				Vector3d swathNormal,
				SurfacePoint surfacePoint)
			{
				Vector3d surfaceToSatellite =
					bodyCentredVesselPosition - surfacePoint.bodyCentredSurfacePosition;
				range = surfaceToSatellite.magnitude;
				cosZenithalAngle =
					Vector3d.Dot(surfaceToSatellite, surfacePoint.vertical) / range;
				// The cosine of the angle between the satellite-surface ray and the swath normal
				// is the sine of the angle between that ray and the swath plane.
				absSinSwathAngle = Math.Abs(Vector3d.Dot(surfaceToSatellite, swathNormal) / range);
			}

			public double range;
			// The cosine of the satellite-surface-vertical angle.
			public double cosZenithalAngle;
			// The absolute value of the sine of the angle between the satellite-surface ray
			// and the swath plane.
			public double absSinSwathAngle;
			public bool Visible => cosZenithalAngle > 0 && absSinSwathAngle < sin5degrees;
		}

		public const double sin5degrees = 0.087155742747658174;
		public const double cos15degrees = 0.96592582628906829;
		public const double cos75degrees = 0.25881904510252076;
		public const double cos105degrees = -cos75degrees;

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
				surfaceSatelliteGeometry.absSinSwathAngle = 0;
				cosGlintAngle = Vector3d.Dot(satelliteDirection, sunSurfaceGeometry.reflectedRay);
			}
			public SunSurfaceSatelliteGeometry(
				Vector3d bodyCentredVesselPosition,
				Vector3d swathNormal,
				SurfacePoint surfacePoint,
				SunSurfaceGeometry sunSurfaceGeometry)
			{
				Vector3d surfaceToSatellite =
					bodyCentredVesselPosition - surfacePoint.bodyCentredSurfacePosition;
				surfaceSatelliteGeometry.range = surfaceToSatellite.magnitude;
				Vector3d satelliteDirection = surfaceToSatellite / surfaceSatelliteGeometry.range;
				surfaceSatelliteGeometry.cosZenithalAngle =
					Vector3d.Dot(satelliteDirection, surfacePoint.vertical);
				surfaceSatelliteGeometry.absSinSwathAngle =
					Math.Abs(Vector3d.Dot(satelliteDirection, swathNormal));
				cosGlintAngle = Vector3d.Dot(satelliteDirection, sunSurfaceGeometry.reflectedRay);
			}
			public SurfaceSatelliteGeometry surfaceSatelliteGeometry;
			public double cosGlintAngle;
		}

		public void RegisterImager(ModuleImager imager, IEnumerable<ImagingProduct> products)
		{
			activeImagers[imager.platform.id] = products.ToArray();
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
				lastUpdateUT = null;
				window.width = 0;
				window.height = 0;
				return;
			}
			if (pause)
			{
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
						midInfrared = new ImagingStatus[x_size],
						nightVisNIR = new ImagingStatus[x_size],
						unglintedReflectiveVisNIR = new ImagingStatus[x_size],
						glintedReflectiveVisNIR = new ImagingStatus[x_size],
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
							parallel.midInfrared[x].lastImagingTime = new double[resolutionThresholds.Length];
							parallel.nightVisNIR[x].lastImagingTime = new double[resolutionThresholds.Length];
							parallel.glintedReflectiveVisNIR[x].lastImagingTime = new double[resolutionThresholds.Length];
							parallel.unglintedReflectiveVisNIR[x].lastImagingTime = new double[resolutionThresholds.Length];
							for (int i = 0; i < resolutionThresholds.Length; ++i)
							{
								parallel.midInfrared[x].lastImagingTime[i] = double.NegativeInfinity;
								parallel.nightVisNIR[x].lastImagingTime[i] = double.NegativeInfinity;
								parallel.glintedReflectiveVisNIR[x].lastImagingTime[i] = double.NegativeInfinity;
								parallel.unglintedReflectiveVisNIR[x].lastImagingTime[i] = double.NegativeInfinity;
							}
						}
					}
				}
			}
			var imagers = activeImagers.ToArray();
			double Δt = 30;
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
					ImagingProduct[] products = imager.Value;
					Vector3d vesselFromKerbinInWorld;
					Vector3d kerbinCentredVesselVelocityInWorld;
					if (platform.orbit.referenceBody == kerbin)
					{
						var vesselFromKerbinInAlice = platform.orbit.getRelativePositionAtUT(t);
						var kerbinCentredVesselVelocityInAlice = platform.orbit.getOrbitalVelocityAtUT(t);
						vesselFromKerbinInWorld = vesselFromKerbinInAlice.xzy;
						kerbinCentredVesselVelocityInWorld = kerbinCentredVesselVelocityInAlice.xzy;
					}
					else
					{
						Vector3d vesselFromSunInAlice = Vector3d.zero;
						Vector3d kerbinFromSunInAlice = Vector3d.zero;
						Vector3d heliocentricVesselVelocityInAlice = Vector3d.zero;
						Vector3d heliocentricKerbinVelocityInAlice = Vector3d.zero;
						for (var orbit = platform.orbit; orbit != null; orbit = orbit.referenceBody?.orbit)
						{
							vesselFromSunInAlice += orbit.getRelativePositionAtUT(t);
							heliocentricVesselVelocityInAlice += orbit.getOrbitalVelocityAtUT(t);
						}
						for (var orbit = kerbin.orbit; orbit != null; orbit = orbit.referenceBody?.orbit)
						{
							kerbinFromSunInAlice += orbit.getRelativePositionAtUT(t);
							heliocentricKerbinVelocityInAlice += orbit.getOrbitalVelocityAtUT(t);
						}
						vesselFromKerbinInWorld = (vesselFromSunInAlice - kerbinFromSunInAlice).xzy;
						kerbinCentredVesselVelocityInWorld =
							(heliocentricVesselVelocityInAlice - heliocentricKerbinVelocityInAlice).xzy;
					}
					Vector3d swathNormal;
					if (platform.loaded) {
						// The rotation transforms from part coordinates to
						// world coordinates.
						// TODO(egg): that products[0] is a hack.
						swathNormal = world_to_kerbin *
								(products[0].part.rb.rotation * Vector3d.up);
					} else {
						// TODO(egg): Fetch from Principia if available.
						swathNormal = world_to_kerbin * kerbinCentredVesselVelocityInWorld.normalized;
					}
					Vector3d vesselInSurfaceFrame = world_to_kerbin * vesselFromKerbinInWorld;
					UnityEngine.Vector2d subsatellitePoint = kerbin.GetLatitudeAndLongitude(
						kerbin.position + current_kerbin_to_world * world_to_kerbin * vesselFromKerbinInWorld);
					double latitude = subsatellitePoint.x;
					double longitude = subsatellitePoint.y;
					double xVessel = (0.5 + longitude / 360) % 1;
					double yVessel = (0.5 + latitude / 180) % 1;
					for (int y = (int)(yVessel * y_size); y < y_size; ++y)
					{
						var parallel = kerbin_imaging[y];
						UpdateParallel(
							t, parallel, vesselInSurfaceFrame, swathNormal, products,
							out bool parallelVisibleAtRelevantResolution);
						if (!parallelVisibleAtRelevantResolution)
						{
							break;
						}
					}
					for (int y = (int)(yVessel * y_size) - 1; y >= 0; --y)
					{
						var parallel = kerbin_imaging[y];
						UpdateParallel(
							t, parallel, vesselInSurfaceFrame, swathNormal, products,
							out bool parallelVisibleAtRelevantResolution);
						if (!parallelVisibleAtRelevantResolution)
						{
							break;
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

		private void UpdateParallel(
			double t,
			ImagingParallel parallel,
			Vector3d vesselInSurfaceFrame,
			Vector3d swathNormal,
			ImagingProduct[] products,
			out bool parallelVisibleAtRelevantResolution)
		{
			parallelVisibleAtRelevantResolution = false;
			for (int x = parallel.x_begin; x != parallel.x_end; ++x)
			{
				SunSurfaceSatelliteGeometry geometry = products[0].pushbroom
					? new SunSurfaceSatelliteGeometry(
						vesselInSurfaceFrame,
						swathNormal,
						parallel.surface[x],
						parallel.sun[x])
					: new SunSurfaceSatelliteGeometry(
						vesselInSurfaceFrame,
						parallel.surface[x],
						parallel.sun[x]);
				if (geometry.surfaceSatelliteGeometry.Visible)
				{
					foreach (var product in products)
					{
						bool sunlit = parallel.sun[x].cosSolarZenithAngle > cos75degrees;
						bool night = parallel.sun[x].cosSolarZenithAngle < cos105degrees;
						if (product.band == OpticalBand.VisNIR && !sunlit && !night)
						{
							continue;
						}
						double resolution = product.HorizontalResolution(geometry.surfaceSatelliteGeometry);
						for (int i = 0; i < resolutionThresholds.Length; ++i)
						{
							if (product.HorizontalResolution(geometry.surfaceSatelliteGeometry) <
								resolutionThresholds[i])
							{
								parallelVisibleAtRelevantResolution = true;
								for (; i < resolutionThresholds.Length; ++i)
								{
									switch (product.band)
									{
										case OpticalBand.MidInfrared:
											parallel.midInfrared[x].lastImagingTime[i] =
												Math.Max(parallel.midInfrared[x].lastImagingTime[i], t);
											break;
										case OpticalBand.VisNIR:
											if (sunlit)
											{
												if (geometry.cosGlintAngle > cos15degrees)
												{
													parallel.glintedReflectiveVisNIR[x].lastImagingTime[i] =
														Math.Max(parallel.glintedReflectiveVisNIR[x].lastImagingTime[i], t);
												}
												else
												{
													parallel.unglintedReflectiveVisNIR[x].lastImagingTime[i] =
														Math.Max(parallel.unglintedReflectiveVisNIR[x].lastImagingTime[i], t);
												}
											}
											else if (night)
											{
												parallel.nightVisNIR[x].lastImagingTime[i] =
													Math.Max(parallel.nightVisNIR[x].lastImagingTime[i], t);
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
			UnityEngine.Color32 beige = XKCDColors.Beige;
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
					var unglintedStatus = mapProduct switch
					{
						MapProduct.EmissiveMIR => parallel.midInfrared[x],
						MapProduct.NightVisNIR => parallel.nightVisNIR[x],
						MapProduct.ReflectiveVisNIR => parallel.unglintedReflectiveVisNIR[x],
						_ => throw new ArgumentException($"Unexpected map band {mapProduct}")
					};
					var glintedStatus = mapProduct switch
					{
						MapProduct.EmissiveMIR => parallel.midInfrared[x],
						MapProduct.NightVisNIR => parallel.nightVisNIR[x],
						MapProduct.ReflectiveVisNIR => parallel.glintedReflectiveVisNIR[x],
						_ => throw new ArgumentException($"Unexpected map band {mapProduct}")
					};
					if (!map.on_map)
					{
						pixels[pixel] = black;
					}
					else if (map.ocean && !showOceans)
					{
						pixels[pixel] = sea;
					}
					else if (!map.ocean && !showLand)
					{
						pixels[pixel] = beige;
					}
					else
					{
						++map_pixels;
						if (mapType == MapType.Freshness)
						{
							double leastImagingAge = double.PositiveInfinity;
							if (mapProduct != MapProduct.ReflectiveVisNIR || showUnglinted)
							{
								leastImagingAge = Math.Min(
									leastImagingAge, t - unglintedStatus.lastImagingTime[chosenResolutionIndex]);
							}
							if (mapProduct == MapProduct.ReflectiveVisNIR && showGlinted)
							{
								leastImagingAge = Math.Min(
									leastImagingAge, t - glintedStatus.lastImagingTime[chosenResolutionIndex]);
							}
							if (leastImagingAge > freshnessThresholds.Last())
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
							if (mapProduct != MapProduct.ReflectiveVisNIR || showUnglinted)
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
							if (mapProduct == MapProduct.ReflectiveVisNIR && showGlinted)
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
						if (showLand && showOceans && map.ocean && (
							(x - 1 > parallel.x_begin && !parallel.map[x - 1].ocean) ||
							(x + 1 < parallel.x_end && !parallel.map[x + 1].ocean)))
						{
							pixels[pixel] = sea;
						}
					}
					if (map.on_map && showSun)
					{
						if (parallel.sun[x].cosSolarZenithAngle < cos105degrees)
						{
							var c = pixels[pixel];
							c.r /= 2;
							c.g /= 2;
							c.b /= 2;
							pixels[pixel] = c;
						}
						else if (parallel.sun[x].cosSolarZenithAngle < cos75degrees)
						{
							var c = pixels[pixel];
							c.r = (byte)(2 * c.r / 3);
							c.g = (byte)(2 * c.g / 3);
							c.b = (byte)(2 * c.b / 3);
							pixels[pixel] = c;
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
				UnityEngine.GUILayout.Label("Sunglint (reflective only): ");
				showGlinted = UnityEngine.GUILayout.Toggle(showGlinted, "affected");
				showUnglinted = UnityEngine.GUILayout.Toggle(showUnglinted, "unaffected");
			}
		}

		private void DrawLandSeaSelector()
		{
			using (new UnityEngine.GUILayout.HorizontalScope())
			{
				showLand = UnityEngine.GUILayout.Toggle(showLand, " Land imaging");
				showOceans = UnityEngine.GUILayout.Toggle(showOceans, " Sea imaging");
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
				if (UnityEngine.GUILayout.Toggle(mapProduct == MapProduct.EmissiveMIR, "Mid-IR (emissive)"))
				{
					mapProduct = MapProduct.EmissiveMIR;
				}
				if (UnityEngine.GUILayout.Toggle(mapProduct == MapProduct.NightVisNIR, "Vis-NIR (night emissive)"))
				{
					mapProduct = MapProduct.NightVisNIR;
				}
				if (UnityEngine.GUILayout.Toggle(mapProduct == MapProduct.ReflectiveVisNIR, "Vis-NIR (reflective)"))
				{
					mapProduct = MapProduct.ReflectiveVisNIR;
				}
			}
		}

		private string CoverageSummary()
		{
			string result = "";
			switch (mapType)
			{
				case MapType.Freshness:
					result = $@"Freshness at a resolution of {
						(chosenResolution > 1000 ? $"{chosenResolution / 1000:N0} km" : $"{chosenResolution:N0} m")}";
					for (int i = 0; i < freshnessThresholds.Length; ++i)
					{
						result += $@" | {(freshnessThresholds[i] > 3600
								? $"{freshnessThresholds[i] / 3600:N0} h"
								: "current")} ({freshnessCoverage[i]:P1})";
					}
					break;
				case MapType.Resolution:
					result = $@"Resolution at a freshness of {
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
				solarParallax = UnityEngine.GUILayout.Toggle(solarParallax, "Solar parallax (slow, likely pointless)");
				reset |= UnityEngine.GUILayout.Button("Reset");
				small = UnityEngine.GUILayout.Toggle(small, "Small map (effective on reset)");
				pause = UnityEngine.GUILayout.Toggle(pause, "Pause imaging analysis");
				reset |= pause;
				UnityEngine.GUILayout.Label("—————");
				UnityEngine.GUILayout.TextArea(CoverageSummary());
				DrawMapTypeSelector();
				showSun = UnityEngine.GUILayout.Toggle(showSun, "Show current day/night");
				UnityEngine.GUILayout.Box(minimap);
				UnityEngine.GUILayout.Label("——— Product options ———");
				DrawBandSelector();
				DrawLandSeaSelector();
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
				UnityEngine.GUILayout.Label("Preset products: ");
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					if (UnityEngine.GUILayout.Button("Weather forecasting"))
					{
						mapProduct = MapProduct.EmissiveMIR;
						showSun = false;
						chosenResolutionIndex = resolutionThresholds.IndexOf(10e3);
						chosenFreshnessIndex = freshnessThresholds.IndexOf(6 * 3600);
						showLand = true;
						showOceans = true;
					}
					if (UnityEngine.GUILayout.Button("Fire monitoring"))
					{
						mapProduct = MapProduct.EmissiveMIR;
						showSun = false;
						chosenResolutionIndex = resolutionThresholds.IndexOf(100);
						chosenFreshnessIndex = freshnessThresholds.IndexOf(24 * 3600);
						showLand = true;
						showOceans = false;
					}
					if (UnityEngine.GUILayout.Button("Ocean colour"))
					{
						mapProduct = MapProduct.ReflectiveVisNIR;
						showSun = true;
						chosenResolutionIndex = resolutionThresholds.IndexOf(1e3);
						chosenFreshnessIndex = freshnessThresholds.IndexOf(48 * 3600);
						showLand = false;
						showOceans = true;
						showGlinted = false;
						showUnglinted = true;
					}
					if (UnityEngine.GUILayout.Button("Oil spill"))
					{
						mapProduct = MapProduct.ReflectiveVisNIR;
						showSun = true;
						chosenResolutionIndex = resolutionThresholds.IndexOf(1e3);
						chosenFreshnessIndex = freshnessThresholds.IndexOf(48 * 3600);
						showLand = false;
						showOceans = true;
						showGlinted = true;
						showUnglinted = false;
					}
				}
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					if (UnityEngine.GUILayout.Button("Urbanization/Light pollution"))
					{
						mapProduct = MapProduct.NightVisNIR;
						showSun = true;
						chosenResolutionIndex = resolutionThresholds.IndexOf(1e3);
						chosenFreshnessIndex = freshnessThresholds.IndexOf(48 * 3600);
						showLand = true;
						showOceans = false;
					}
				}
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
			public ImagingStatus[] midInfrared;
			public ImagingStatus[] nightVisNIR;
			public ImagingStatus[] unglintedReflectiveVisNIR;
			public ImagingStatus[] glintedReflectiveVisNIR;
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

		private enum MapProduct
		{
			ReflectiveVisNIR,
			NightVisNIR,
			EmissiveMIR,
		}
		private static MapProduct mapProduct = MapProduct.ReflectiveVisNIR;
		private static bool showGlinted = true;
		private static bool showUnglinted = true;
		private static bool showOceans;
		private static bool showLand = true;
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

		private static bool showSun = true;
		private static bool pause = false;

		private UnityEngine.Quaternion? lastKerbinRotation;
		private TimeSpan timeSpentInUpdate;
		private TimeSpan timeSpentInTextureUpdate;
		private TimeSpan timeSpentInIllumination;
		private readonly Dictionary<Guid, ImagingProduct[]> activeImagers = new Dictionary<Guid, ImagingProduct[]>();
	}
}
