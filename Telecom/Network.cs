using RealAntennas;
using RealAntennas.Antenna;
using RealAntennas.MapUI;
using RealAntennas.Network;
using RealAntennas.Precompute;
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
				station_node.AddValue("icon", "RealAntennas/DSN");
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
			customer_templates_ = template.GetNodes("customer");
		}

		void SpawnCustomer(ConfigNode template)
		{
			HashSet<string> biomes = template.GetValues("biome").ToHashSet();
			double lat_deg;
			double lon_deg;
			do
			{
				const double degree = Math.PI / 180;
				double sin_lat_min =
					Math.Sin(double.Parse(template.GetValue("lat_min")) * degree);
				double sin_lat_max =
					Math.Sin(double.Parse(template.GetValue("lat_max")) * degree);
				double lon_min_deg = double.Parse(template.GetValue("lon_min"));
				double lon_max_deg = double.Parse(template.GetValue("lon_max"));
				lat_deg = Math.Asin(sin_lat_min + random_.NextDouble() * (sin_lat_max - sin_lat_min)) / degree;
				lon_deg = lon_min_deg + random_.NextDouble() * (lon_max_deg - lon_min_deg);
			} while (biomes.Contains(body_.BiomeMap.GetAtt(lat_deg, lon_deg).name));
			var customer =
				new UnityEngine.GameObject(body_.name).AddComponent<RACommNetHome>();
			var customer_node = new ConfigNode();
			customer_node.AddValue("objectName", $"{name_} customer");
			customer_node.AddValue("lat", lat_deg);
			customer_node.AddValue("lon", lon_deg);
			customer_node.AddValue("alt", body_.TerrainAltitude(lat_deg, lon_deg) + 10);
			customer_node.AddValue("isKSC", false);
			customer_node.AddValue("icon", "RealAntennas/radio-antenna");
			foreach (var antenna in template.GetNodes("Antenna"))
			{
				customer_node.AddNode(antenna);
			}
			customer.Configure(customer_node, body_);
			upcoming_customers_.Enqueue(customer);
			if (template.GetValue("role") == "tx")
			{
				tx_.Add(customer);
			}
			else if (template.GetValue("role") == "rx")
			{
				rx_.Add(customer);
			}
			else
			{
				tx_.Add(customer);
				rx_.Add(customer);
			}
		}

		public void Refresh()
		{
			if (ground_segment_.Any(station => station.Comm == null))
			{
				return;
			}
			CreateGroundSegmentNodesIfNeeded();
			while (upcoming_customers_.Count > 0 && upcoming_customers_.Peek().Comm != null)
			{
				customers_.Add(upcoming_customers_.Dequeue());
				customers_nodes_.Add(MakeSiteNode(customers_.Last()));
				ra_is_initialized_ = false;
			}
			if (!ra_is_initialized_)
			{
				InitializeRA();
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

		private void InitializeRA()
		{
			var precompute = new Precompute();
			precompute.Initialize();
			precompute.DoThings();
			precompute.SimulateComplete(
				ref RACommNetScenario.RACN.connectionDebugger,
				RACommNetScenario.RACN.Nodes);
			ra_is_initialized_ = true;
		}

		private void UpdateConnections() {
			var network = CommNet.CommNetNetwork.Instance.CommNet as RACommNetwork;
			if (network == null)
			{
				UnityEngine.Debug.LogError("No RA comm network");
				return;
			}
			all_ground_ = ground_segment_.Concat(customers_).ToArray();
			if (rate_matrix_.GetLength(0) != all_ground_.Length)
			{
				rate_matrix_ = new double[all_ground_.Length, all_ground_.Length];
				latency_matrix_ = new double[all_ground_.Length, all_ground_.Length];
			}
			min_rate_ = double.PositiveInfinity;
			active_links_.Clear();
			for (int tx = 0; tx < all_ground_.Length; ++tx)
			{
				all_ground_[tx].Comm.isHome = false;
				for (int rx = 0; rx < rx_.Count; ++rx)
				{
					if (rx == tx || tx_.Contains(ground_segment_[tx]) || !rx_.Contains(ground_segment_[rx]))
					{
						rate_matrix_[tx, rx] = double.NaN;
						latency_matrix_[tx, rx] = double.NaN;
						continue;
					}
					var path = new CommNet.CommPath();
					network.FindClosestWhere(
						ground_segment_[tx].Comm, path, (_, n) => n == ground_segment_[rx].Comm);
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
					rate_matrix_[tx, rx] = rate;
					latency_matrix_[tx, rx] = length / 299792458;
					min_rate_ = Math.Min(min_rate_, rate);
				}
			}
		}

		private CelestialBody body_;
		private bool active_;
		private readonly Queue<RACommNetHome> upcoming_customers_ = new Queue<RACommNetHome>();
		private readonly List<RACommNetHome> customers_ = new List<RACommNetHome>();
		private readonly List<SiteNode> customers_nodes_ = new List<SiteNode>();
		private readonly List<RACommNetHome> ground_segment_ = new List<RACommNetHome>();
		private List<SiteNode> ground_segment_nodes_;
		private bool ra_is_initialized_ = false;
		private readonly HashSet<RACommNetHome> tx_ = new HashSet<RACommNetHome>();
		private readonly HashSet<RACommNetHome> rx_ = new HashSet<RACommNetHome>();
		public readonly Dictionary<Vessel, double> space_segment_ = new Dictionary<Vessel, double>();
		private readonly List<Vector3d> nominal_satellite_locations_ = new List<Vector3d>();
		private readonly ConfigNode[] customer_templates_;
		private readonly Random random_ = new Random();
		public readonly List<CommNet.CommLink> active_links_ = new List<CommNet.CommLink>();
		private readonly string name_;
		public RACommNetHome[] all_ground_ = {};
		public double[,] rate_matrix_ = {};
		public double[,] latency_matrix_ = {};
		public double min_rate_;
	}
}
