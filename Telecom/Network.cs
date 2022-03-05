using RealAntennas;
using RealAntennas.Antenna;
using RealAntennas.MapUI;
using RealAntennas.Network;
using RealAntennas.Precompute;
using RealAntennas.Targeting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace skopos
{
	class Network
	{
		public Network(ConfigNode template)
		{
			name_ = template.GetValue("name");
			var ground_segment_node = template.GetNode("ground_segment");
			body_ = FlightGlobals.GetBodyByName(ground_segment_node.GetValue("body"));
			foreach (ConfigNode node in ground_segment_node.GetNodes("station"))
			{
				var station =
				  new UnityEngine.GameObject(body_.name).AddComponent<RACommNetHome>();
				var station_node = new ConfigNode();
				foreach (string key in new[] { "objectName", "lat", "lon", "alt" })
				{
					station_node.AddValue(key, node.GetValue(key));
				}
				station_node.AddValue("isKSC", false);
				station_node.AddValue("icon", "RealAntennas/radio-antenna");
				foreach (var antenna in node.GetNodes("Antenna"))
				{
					station_node.AddNode(antenna);
				}
				station.Configure(station_node, body_);
				ground_segment_.Add(station);
				if (node.GetValue("role") == "tx")
				{
					tx_.Add(station);
				}
				else if (node.GetValue("role") == "rx")
				{
					rx_.Add(station);
				}
				else
				{
					tx_.Add(station);
					rx_.Add(station);
				}
			}
			customers_ = (from customer_template in template.GetNodes("customer")
						  select new Customer(customer_template, this)).ToArray();
			int n = ground_segment_.Count + customers_.Length;
			connections_ = new Connection[n, n];
			for (int i = 0; i < n; i++)
			{
				for (int j = 0; j < n; ++j)
				{
					connections_[i, j] = new Connection();
				}
			}
			foreach (var node in template.GetNodes("service_level"))
			{
				// TODO(egg): tx/rx-specific clauses.
				for (int i = 0; i < n; i++)
				{
					for (int j = 0; j < n; ++j)
					{
						connections_[i, j].latency_threshold = double.Parse(node.GetValue("latency"));
						connections_[i, j].rate_threshold = double.Parse(node.GetValue("rate"));
						connections_[i, j].target_latency_availability = double.Parse(node.GetValue("latency_availability"));
						connections_[i, j].target_rate_availability = double.Parse(node.GetValue("rate_availability"));
					}
				}
			}
		}

		public void AddNominalLocation(Vessel v)
		{
			nominal_satellite_locations_.Add(
				UnityEngine.QuaternionD.Inverse(body_.scaledBody.transform.rotation) *
					(v.GetWorldPos3D() - body_.position));
			must_retarget_customers_ = true;
		}

		public Vector3d[] GetNominalLocationLatLonAlts()
		{
			var result = new List<Vector3d>(nominal_satellite_locations_.Count);
			foreach (var position in nominal_satellite_locations_)
			{
				body_.GetLatLonAlt(
					body_.scaledBody.transform.rotation * position + body_.position,
					out double lat, out double lon, out double alt);
				result.Add(new Vector3d(lat, lon, alt));
			}
			return result.ToArray();
		}

		public void ClearNominalLocations()
		{
			nominal_satellite_locations_.Clear();
			must_retarget_customers_ = true;
		}

		public void Refresh()
		{
			if (ground_segment_.Any(station => station.Comm == null))
			{
				return;
			}
			foreach (var customer in customers_)
			{
				customer.Cycle();
			}
			if (customers_.Any(customer => customer.station == null))
			{
				return;
			}
			if (must_retarget_customers_)
			{
				foreach (var customer in customers_)
				{
					Retarget(customer.station);
				}
			}
			UpdateConnections();
		}

		private void CreateGroundSegmentNodesIfNeeded()
		{
			if (ground_segment_nodes_ == null && MapView.fetch != null)
			{
				ground_segment_nodes_ = new List<SiteNode>();
				foreach (var station in ground_segment_)
				{
					ground_segment_nodes_.Add(MakeSiteNode(station));
				}
			}
		}

		private static SiteNode MakeSiteNode(RACommNetHome station)
		{
			SiteNode site_node = SiteNode.Spawn(new GroundStationSiteNode(station.Comm));
			UnityEngine.Texture2D stationTexture = GameDatabase.Instance.GetTexture(station.icon, false);
			site_node.wayPoint.node.SetIcon(UnityEngine.Sprite.Create(
				stationTexture,
				new UnityEngine.Rect(0, 0, stationTexture.width, stationTexture.height),
				new UnityEngine.Vector2(0.5f, 0.5f),
				100f));
			site_node.wayPoint.node.OnUpdateVisible += station.OnUpdateVisible;
			return site_node;
		}

		private void Retarget(RACommNetHome station)
		{
			if (nominal_satellite_locations_.Count == 0)
			{
				station.Comm.RAAntennaList[0].Target = null;
			}
			Vector3d station_position =
					UnityEngine.QuaternionD.Inverse(body_.scaledBody.transform.rotation) *
						(station.Comm.precisePosition - body_.position);
			Vector3d station_zenith = station_position.normalized;
			Vector3d target = default;
			double max_cos_zenithal_angle = double.NegativeInfinity;
			foreach (var position in nominal_satellite_locations_)
			{
				double cos_zenithal_angle = Vector3d.Dot(station_zenith, position - station_position);
				if (cos_zenithal_angle > max_cos_zenithal_angle)
				{
					max_cos_zenithal_angle = cos_zenithal_angle;
					target = position;
				}
			}
			body_.GetLatLonAlt(
				body_.scaledBody.transform.rotation * target + body_.position,
				out double lat, out double lon, out double alt);
			var config = new ConfigNode(AntennaTarget.nodeName);
			config.AddValue("name", $"{AntennaTarget.TargetMode.BodyLatLonAlt}");
			config.AddValue("bodyName", Planetarium.fetch.Home.name);
			config.AddValue("latLonAlt", new Vector3d(lat, lon, alt));
			var antenna = station.Comm.RAAntennaList[0];
			antenna.Target = AntennaTarget.LoadFromConfig(config, antenna);
		}

		private void UpdateConnections() {
			var network = CommNet.CommNetNetwork.Instance.CommNet as RACommNetwork;
			if (network == null)
			{
				UnityEngine.Debug.LogError("No RA comm network");
				return;
			}
			all_ground_ = ground_segment_.Concat(from customer in customers_ select customer.station).ToArray();
			min_rate_ = double.PositiveInfinity;
			active_links_.Clear();
			for (int tx = 0; tx < all_ground_.Length; ++tx)
			{
				all_ground_[tx].Comm.isHome = false;
				for (int rx = 0; rx < all_ground_.Length; ++rx)
				{
					if (rx == tx || !tx_.Contains(all_ground_[tx]) || !rx_.Contains(all_ground_[rx]))
					{
						connections_[tx, rx].AddMeasurement(double.NaN, double.NaN);
						continue;
					}
					var path = new CommNet.CommPath();
					network.FindClosestWhere(
						all_ground_[tx].Comm, path, (_, n) => n == all_ground_[rx].Comm);
					double rate = double.PositiveInfinity;
					double length = 0;
					foreach (var l in path)
					{
						active_links_.Add(l);
						RACommLink link = l.start[l.end] as RACommLink;
						rate = Math.Min(rate, link.FwdDataRate);
						length += (l.a.position - l.b.position).magnitude;
						if ((l.end as RACommNode).ParentVessel is Vessel vessel)
						{
							space_segment_[vessel] = Planetarium.GetUniversalTime();
						}
					}
					if (path.IsEmpty())
					{
						rate = 0;
					}
					connections_[tx, rx].AddMeasurement(rate: rate, latency: length / 299792458);
					min_rate_ = Math.Min(min_rate_, rate);
				}
			}
		}

		private class Customer
		{
			public Customer(ConfigNode template, Network network)
			{
				template_ = template;
				network_ = network;
				upcoming_station_ = MakeStation();
			}
			public void Cycle()
			{
				if (imminent_station_ != null)
				{
					DestroyStation();
					station = imminent_station_;
					imminent_station_ = null;
					try
					{
						node_ = MakeSiteNode(station);
					} catch (NullReferenceException)
					{
						UnityEngine.Debug.LogError("NullReferenceException while making customer site node.");
						node_ = null;
					}
					network_.Retarget(station);
					(RACommNetScenario.Instance as RACommNetScenario)?.Network?.InvalidateCache();
				}
				if (upcoming_station_?.Comm != null)
				{
					imminent_station_ = upcoming_station_;
					upcoming_station_ = MakeStation();
				}
			}

			private RACommNetHome MakeStation()
			{
				HashSet<string> biomes = template_.GetValues("biome").ToHashSet();
				const double degree = Math.PI / 180;
				double lat;
				double lon;
				int i = 0;
				do
				{
					++i;
					double sin_lat_min =
						Math.Sin(double.Parse(template_.GetValue("lat_min")) * degree);
					double sin_lat_max =
						Math.Sin(double.Parse(template_.GetValue("lat_max")) * degree);
					double lon_min = double.Parse(template_.GetValue("lon_min")) * degree;
					double lon_max = double.Parse(template_.GetValue("lon_max")) * degree;
					lat = Math.Asin(sin_lat_min + network_.random_.NextDouble() * (sin_lat_max - sin_lat_min));
					lon = lon_min + network_.random_.NextDouble() * (lon_max - lon_min);
				} while (!biomes.Contains(network_.body_.BiomeMap.GetAtt(lat, lon).name));
				var new_station =
					new UnityEngine.GameObject(network_.body_.name).AddComponent<RACommNetHome>();
				var node = new ConfigNode();
				node.AddValue("objectName", $"{network_.name_} customer @{lat / degree:F2}, {lon / degree:F2} ({i} tries)");
				node.AddValue("lat", lat / degree);
				node.AddValue("lon", lon / degree);
				node.AddValue("alt", network_.body_.TerrainAltitude(lat / degree, lon / degree) + 10);
				node.AddValue("isKSC", false);
				node.AddValue("icon", "RealAntennas/DSN");
				foreach (var antenna in template_.GetNodes("Antenna"))
				{
					node.AddNode(antenna);
				}
				new_station.Configure(node, network_.body_);
				if (template_.GetValue("role") == "tx")
				{
					network_.tx_.Add(new_station);
				}
				else if (template_.GetValue("role") == "rx")
				{
					network_.rx_.Add(new_station);
				}
				else
				{
					network_.tx_.Add(new_station);
					network_.rx_.Add(new_station);
				}
				return new_station;
			}

			private void DestroyStation()
			{
				if (station == null)
				{
					return;
				}
				network_.tx_.Remove(station);
				network_.rx_.Remove(station);
				CommNet.CommNetNetwork.Instance.CommNet.Remove(station.Comm);
				if (node_ != null)
				{
					FinePrint.WaypointManager.RemoveWaypoint(node_.wayPoint);
					UnityEngine.Object.Destroy(node_.gameObject);
				}
				UnityEngine.Object.Destroy(station);
				station = null;
				node_ = null;
			}

			private RACommNetHome upcoming_station_;
			private RACommNetHome imminent_station_;
			public RACommNetHome station { get; private set; }
			private SiteNode node_;

			private ConfigNode template_;
			private Network network_;
		}

		public class Connection
		{
			public void AddMeasurement(double latency, double rate)
			{
				if (last_measurement_time_ != null)
				{
					double Δt = Planetarium.GetUniversalTime() - last_measurement_time_.Value;
					current_latency = latency;
					current_rate = rate;
					if (latency <= latency_threshold)
					{
						time_below_latency_threshold_ += Δt;
					}
					if (rate >= rate_threshold)
					{
						time_above_rate_threshold_ += Δt;
					}
					total_measurement_time_ += Δt;
				}
				last_measurement_time_ = Planetarium.GetUniversalTime();
			}
			public double current_latency { get; private set; }
			public double current_rate { get; private set; }
			public double latency_threshold { get; set; }
			public double rate_threshold { get; set; }
			public double target_latency_availability { get; set; }
			public double target_rate_availability { get; set; }
			public double latency_availability => time_below_latency_threshold_ / total_measurement_time_;
			public double rate_availability => time_above_rate_threshold_ / total_measurement_time_;
			private double time_below_latency_threshold_;
			private double time_above_rate_threshold_;
			private double total_measurement_time_;
			private double? last_measurement_time_;
		}

		public int customer_pool_size { get; set; }

		private CelestialBody body_;
		private bool active_;
		private readonly Customer[] customers_;
		private readonly List<RACommNetHome> ground_segment_ = new List<RACommNetHome>();
		private List<SiteNode> ground_segment_nodes_;
		public readonly HashSet<RACommNetHome> tx_ = new HashSet<RACommNetHome>();
		public readonly HashSet<RACommNetHome> rx_ = new HashSet<RACommNetHome>();
		public readonly Dictionary<Vessel, double> space_segment_ = new Dictionary<Vessel, double>();
		private readonly List<Vector3d> nominal_satellite_locations_ = new List<Vector3d>();
		bool must_retarget_customers_ = false;
		private readonly Random random_ = new Random();
		public readonly List<CommNet.CommLink> active_links_ = new List<CommNet.CommLink>();
		private readonly string name_;
		public RACommNetHome[] all_ground_ = {};
		public Connection[,] connections_ = {};
		public double min_rate_;
	}
}
