using KERBALISM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KerbalismContracts
{
	public class ImagerData : EquipmentData<ModuleImager, ImagerData> { }

	public class ModuleImager : ModuleKsmContractEquipment<ModuleImager, ImagerData>
	{
		[KSPField] public double fieldOfViewInRadians;
		// TODO(egg): Discretize this into bands, allow for multi-band imagers (whose
		// images can be used for different purposes, but whose pointings are unified.
		[KSPField] public double wavelength;
		[KSPField] public double aperture;

		// The best angular resolution that this imager can achieve.
		// For dim targets, noise will be the limiting factor instead; only use
		// this directly for bright targets.
		public double sinDiffractionLimitedAngularResolution => 1.22 * wavelength / aperture;

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

		public Vessel platform => vessel ?? background_vessel;
		private Vessel background_vessel;

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
