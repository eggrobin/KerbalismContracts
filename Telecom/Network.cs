using RealAntennas;
using RealAntennas.Antenna;
using RealAntennas.Network;
using RealAntennas.Precompute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Telecom
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
					ground_tx_.Add(station);
				}
				else if (node.GetValue("role") == "rx")
				{
					ground_rx_.Add(station);
				}
				else
				{
					ground_tx_.Add(station);
					ground_rx_.Add(station);
				}
			}
			customer_template_ = template.GetNode("customer");
		}

		void SpawnCustomer()
		{
			HashSet<string> biomes = customer_template_.GetValues("biome").ToHashSet();
			double lat_deg;
			double lon_deg;
			do
			{
				const double degree = Math.PI / 180;
				double sin_lat_min =
					Math.Sin(double.Parse(customer_template_.GetValue("lat_min")) * degree);
				double sin_lat_max =
					Math.Sin(double.Parse(customer_template_.GetValue("lat_max")) * degree);
				double lon_min_deg = double.Parse(customer_template_.GetValue("lon_min"));
				double lon_max_deg = double.Parse(customer_template_.GetValue("lon_max"));
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
			foreach (var antenna in customer_template_.GetNodes("Antenna"))
			{
				customer_node.AddNode(antenna);
			}
			customer.Configure(customer_node, body_);
			upcoming_customers_.Enqueue(customer);
		}

		void Refresh()
		{
			foreach (var station in ground_segment_)
			{
				if (station.Comm == null)
				{
					return;
				}
			}
			while (upcoming_customers_.Peek().Comm != null)
			{
				customers_.Add(upcoming_customers_.Dequeue());
				initialized_ = false;
			}
			if (!initialized_)
			{
				InitializeRA();
				initialized_ = true;
			}
		}

		private void InitializeRA()
		{
			var precompute = new Precompute();
			precompute.Initialize();
			precompute.DoThings();
			precompute.SimulateComplete(
			  ref RACommNetScenario.RACN.connectionDebugger,
			  RACommNetScenario.RACN.Nodes);
		}

		private CelestialBody body_;
		private BandInfo uplink_band_;
		private BandInfo downlink_band_;
		private bool active_;
		private Queue<RACommNetHome> upcoming_customers_ = new Queue<RACommNetHome>();
		private List<RACommNetHome> customers_ = new List<RACommNetHome>();
		private List<RACommNetHome> ground_segment_ = new List<RACommNetHome>();
		private bool initialized_ = false;
		private HashSet<RACommNetHome> ground_tx_ = new HashSet<RACommNetHome>();
		private HashSet<RACommNetHome> ground_rx_ = new HashSet<RACommNetHome>();
		private List<Vessel> space_segment_;
		private ConfigNode customer_template_;
		private Random random_ = new Random();
		private string name_;
	}
}
