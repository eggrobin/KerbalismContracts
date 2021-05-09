using KERBALISM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KerbalismContracts
{
	public class ImagerData : EquipmentData<ModuleImager, ImagerData> { }

	public enum OpticalBand {
		NearUltraviolet,
		Visible,
		NearInfrared,
		MidInfrared,
		FarInfrared,
	}

	public static class OpticalBandExtensions {
		public static double RepresentativeWavelength(this OpticalBand band) {
			return band switch
			{
				OpticalBand.NearUltraviolet =>  66e-9,  // Photometric U band.
				OpticalBand.Visible => 555e-9,  // 540 THz Green.
				OpticalBand.NearInfrared => 1e-6,
				OpticalBand.MidInfrared => 10e-6,
				OpticalBand.FarInfrared => 100e-6,
				_ => throw new ArgumentException($"Unexpected optical band {band}"),
			};
		}
	}

	public class ImagerProperties {

		public double fieldOfViewInRadians;
		public OpticalBand band;
		public double aperture;

		// The best angular resolution that this imager can achieve.
		// For dim targets, noise will be the limiting factor instead; only use
		// this directly for bright targets.
		public double sinDiffractionLimitedAngularResolution => 1.22 * band.RepresentativeWavelength() / aperture;

		// No assumption is made regarding visibility; the caller must ensure
		// that surfacePoint is within the unoccluded field of view of the
		// imager.
		public double HorizontalResolution(Imaging.SurfaceSatelliteGeometry geometry)
		{
			// TODO(egg): Incorporate atmospheric effects, which vary depending on the zenithal angle.
			return geometry.range * sinDiffractionLimitedAngularResolution / geometry.cosZenithalAngle;
		}

		// Whether the satellite is above the surface of the horizon.
		public bool IsVisibleFrom(Imaging.SurfaceSatelliteGeometry geometry)
		{
			// TODO(egg): consider raycasting against the terrain at low altitudes, to
			// avoid planes looking through a mountain range.
			return geometry.cosZenithalAngle > 0;
		}
	}

	public class ModuleImager : ModuleKsmContractEquipment<ModuleImager, ImagerData>
	{
		[KSPField] public double fieldOfViewInRadians;
		[KSPField] public string opticalBands;
		[KSPField] public double aperture;

		public Vessel platform => vessel ?? background_vessel;
		private Vessel background_vessel;
		public IEnumerable<OpticalBand> Bands { get; private set; }

		public override void OnLoad(ConfigNode node)
		{
			base.OnLoad(node);
			var bands = new List<OpticalBand>();
			foreach (string name in opticalBands.Split(' '))
			{
				if (!Enum.TryParse(name, out OpticalBand band))
				{
					UnityEngine.Debug.LogException(new ArgumentException(
						$"Unexpected band {name}"));
				}
				bands.Add(band);
			}
			Bands = bands;
		}

		public IEnumerable<ImagerProperties> properties =>
			from band in Bands select new ImagerProperties{
				fieldOfViewInRadians=fieldOfViewInRadians,
				band=band, aperture=aperture};

		protected override void EquipmentUpdate(ImagerData ed, Vessel vessel)
		{
			background_vessel = vessel;
			if (ed.state == EquipmentState.nominal)
			{
				KerbalismContracts.Imaging.RegisterImager(this);
			}
			else
			{
				KerbalismContracts.Imaging.UnregisterImager(this);
			}
		}
	}
}
