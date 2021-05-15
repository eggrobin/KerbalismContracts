using KERBALISM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KerbalismContracts
{
	public class ImagerData : EquipmentData<ModuleImager, ImagerData> {
		public bool isPushbroom;

		public override void OnLoad(ConfigNode node)
		{
			base.OnLoad(node);
			isPushbroom = Lib.ConfigValue(node, "isPushbroom", true);
		}

		public override void OnSave(ConfigNode node)
		{
			base.OnSave(node);
			node.AddValue("isPushBroom", isPushbroom);
		}
	}

	// The region of the spectrum spanned by these bands is one suitable for usual optics;
	// the same instrument may image in multiple of these bands.
	// On the shortwave end, extreme ultraviolet & X-rays lie outside of that region; they require
	// grazing incidence optics or normal incidence multilayer coated optics.
	// On the longwave end, microwaves lie outside of the region; this is the land of feed horns
	// and antennæ, and the instruments here are provided by RealAntennas.
	// The discretization is relatively coarse; bands are distinguished only if their properties
	// lead to different constraints on mission design.
	public enum OpticalBand {
		// Near, middle, and far ultraviolet.
		// The Earth’s atmosphere is opaque.
		// For remote sensing, largely limited to the study of the upper atmosphere.
		Ultraviolet,
		// Visible and near infrared.
		// The Earth’s atmosphere is somewhat transparent.
		// For remote sensing, this is mostly reflective, or situationally emissive (lights, fires).
		// For astronomy, thermal considerations are limited to the detector, as opposed to requiring
		// the instrument to be in a cool place.
		VisNIR,
		// Middle infrared.
		// The Earth’s atmosphere is somewhat transparent.
		// For remote sensing, this is (emissive) thermal imaging.
		// For astronomy, whole-instrument thermal considerations start to matter, so that the
		// atmosphere itself prohibits ground-based astronomy.
		MidInfrared,
		// Far infrared.
		// The Earth’s atmosphere is opaque.
		// For astronomy, cooling is critical; this is where HSO lives, or where the longer wavelengths
		// of SST lie.
		FarInfrared,
	}

	public static class OpticalBandExtensions {
		public static double RepresentativeWavelength(this OpticalBand band) {
			return band switch
			{
				OpticalBand.Ultraviolet =>  200e-9,
				OpticalBand.VisNIR => 555e-9,  // 540 THz Green.
				OpticalBand.MidInfrared => 10e-6,
				OpticalBand.FarInfrared => 100e-6,
				_ => throw new ArgumentException($"Unexpected optical band {band}"),
			};
		}
	}

	public class ImagingProduct {

		public double fieldOfViewInRadians;
		public OpticalBand band;
		public double aperture;
		public bool pushbroom;

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

		[KSPEvent(active = true, guiActive = true, guiActiveEditor = true, requireFullControl = true, guiName = "_")]
		public void TogglePushbroom()
		{
			moduleData.isPushbroom = !moduleData.isPushbroom;
		}

		public override void Update()
		{
			base.Update();
			Events["TogglePushbroom"].guiName = moduleData.isPushbroom ? "1D scan (pushbroom)" : "2D scan";
		}

		private IEnumerable<ImagingProduct> Products(ImagerData data)
		{
			return from band in Bands select new ImagingProduct{
				fieldOfViewInRadians=fieldOfViewInRadians,
				band=band, aperture=aperture, pushbroom=data.isPushbroom};
		}

		protected override void EquipmentUpdate(ImagerData ed, Vessel vessel)
		{
			background_vessel = vessel;
			if (ed.state == EquipmentState.nominal)
			{
				KerbalismContracts.Imaging.RegisterImager(this, Products(ed));
			}
			else
			{
				KerbalismContracts.Imaging.UnregisterImager(this);
			}
		}
	}
}
