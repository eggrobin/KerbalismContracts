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
			var ground_segment_node = template.GetNode("ground_segment");
			body_ = FlightGlobals.GetBodyByName(ground_segment_node.GetValue("body"));
			foreach (ConfigNode node in ground_segment_node.GetNodes("station"))
			{
				var station =
					new UnityEngine.GameObject(body_.name).AddComponent<RACommNetHome>();
				station.Configure(node, body_);
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
		}

		void Refresh(){
			foreach (var station in ground_segment_)
			{
				if (station.Comm == null)
				{
					return;
				}
			}
			while (upcoming_customers_.Peek().Comm != null) {
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
	}
}
