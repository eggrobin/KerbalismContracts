using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using RealAntennas.Network;
using RealAntennas.MapUI;
using RealAntennas.Targeting;

namespace skopos
{
	[KSPScenario(
		ScenarioCreationOptions.AddToAllGames,
		new[] { GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT })]
	public sealed class Telecom : ScenarioModule
	{
		private void OnGUI()
		{
			window_ = UnityEngine.GUILayout.Window(
				GetHashCode(), window_, DrawWindow, "Skopos Telecom");
		}

		private void FixedUpdate()
		{
			var network = CommNet.CommNetNetwork.Instance.CommNet as RACommNetwork;
			if (network == null)
			{
				UnityEngine.Debug.LogError("No RA comm network");
				return;
			}
			if (ground_segment_ == null)
			{
				UnityEngine.Debug.Log("Defining ground segment");
				var telecom_node = GameDatabase.Instance.GetConfigs("skopos_telecom")[0].config;
				UnityEngine.Debug.Log("Got telecom node");
				UnityEngine.Debug.Log($"{telecom_node.GetNodes("antenna_definition").Length} antenna definitions");
				var ground_segment_node = telecom_node.GetNode("ground_segment");
				UnityEngine.Debug.Log("Got ground segment");
				var body = FlightGlobals.GetBodyByName(ground_segment_node.GetValue("body"));
				UnityEngine.Debug.Log($"Body is {body.name}");
				ground_segment_ = new List<RACommNetHome>();
				space_segment_ = new Dictionary<Vessel, double>();
				UnityEngine.Debug.Log($"{ground_segment_node.GetNodes("station").Length} ground stations");
				foreach (ConfigNode node in ground_segment_node.GetNodes("station"))
				{
					UnityEngine.Debug.Log($"Configuring station {node.GetValue("name")}");
					var station = new UnityEngine.GameObject(body.name).AddComponent<RACommNetHome>();
					station.Configure(node, body);
					ground_segment_.Add(station);
				}
				rate_matrix_ = new double[ground_segment_.Count, ground_segment_.Count];
				latency_matrix_ = new double[ground_segment_.Count, ground_segment_.Count];
			}
			if (ground_segment_[0].Comm == null)
			{
				UnityEngine.Debug.Log("CommNodes not yet created");
				return;
			}
			if (ground_station_nodes_ == null && MapView.fetch != null)
			{
				UnityEngine.Debug.Log("Creating ground nodes");
				ground_station_nodes_ = new List<GroundStationSiteNode>();
				foreach (var station in ground_segment_)
				{
					GroundStationSiteNode gs = new GroundStationSiteNode(station.Comm);
					ground_station_nodes_.Add(gs);
					SiteNode siteNode = SiteNode.Spawn(gs);
					UnityEngine.Texture2D stationTexture = GameDatabase.Instance.GetTexture(station.icon, false);
					siteNode.wayPoint.node.SetIcon(UnityEngine.Sprite.Create(
						stationTexture,
						new UnityEngine.Rect(0, 0, stationTexture.width, stationTexture.height),
						new UnityEngine.Vector2(0.5f, 0.5f),
						100f));
					siteNode.wayPoint.node.OnUpdateVisible += station.OnUpdateVisible;
				}
				UnityEngine.Debug.Log("Created ground nodes");
			}
			min_rate_ = double.PositiveInfinity;
			active_links_.Clear();
			for (int tx = 0; tx < ground_segment_.Count; ++tx)
			{
				ground_segment_[tx].Comm.isHome = false;
				for (int rx = 0; rx < ground_segment_.Count; ++rx)
				{
					if (rx == tx)
					{
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
					UnityEngine.Debug.Log(
						$@"{RATools.PrettyPrintDataRate(rate_matrix_[tx, rx])}, {
							latency_matrix_[tx, rx] * 1000} ms latency");
				}
			}
		}

		private void DrawWindow(int id)
		{
			using (new UnityEngine.GUILayout.VerticalScope())
			{
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					UnityEngine.GUILayout.Label(@"Tx\Rx", UnityEngine.GUILayout.Width(3 * 20));
					for (int rx = 0; rx < ground_segment_.Count; ++rx)
					{
						UnityEngine.GUILayout.Label($"{rx + 1}", UnityEngine.GUILayout.Width(6 * 20));
					}
				}
				for (int tx = 0; tx < ground_segment_.Count; ++tx)
				{
					using (new UnityEngine.GUILayout.HorizontalScope())
					{
						UnityEngine.GUILayout.Label($"{tx + 1}", UnityEngine.GUILayout.Width(3 * 20));
						for (int rx = 0; rx < ground_segment_.Count; ++rx)
						{
							UnityEngine.GUILayout.Label(
								rate_matrix_ == null || tx == rx
									? "—"
									: RATools.PrettyPrintDataRate(rate_matrix_[tx, rx]),
								UnityEngine.GUILayout.Width(6 * 20));
						}
					}
				}
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					UnityEngine.GUILayout.Label(@"Tx\Rx", UnityEngine.GUILayout.Width(3 * 20));
					for (int rx = 0; rx < ground_segment_.Count; ++rx)
					{
						UnityEngine.GUILayout.Label($"{rx + 1}", UnityEngine.GUILayout.Width(6 * 20));
					}
				}
				for (int tx = 0; tx < ground_segment_.Count; ++tx)
				{
					using (new UnityEngine.GUILayout.HorizontalScope())
					{
						UnityEngine.GUILayout.Label($"{tx + 1}", UnityEngine.GUILayout.Width(3 * 20));
						for (int rx = 0; rx < ground_segment_.Count; ++rx)
						{
							UnityEngine.GUILayout.Label(
								latency_matrix_ == null || tx == rx
									? "—"
									: $"{latency_matrix_[tx, rx] * 1000:F0} ms",
								UnityEngine.GUILayout.Width(6 * 20));
						}
					}
				}
				for (int tx = 0; tx < ground_segment_.Count; ++tx)
				{
					var antenna = ground_segment_[tx].Comm.RAAntennaList[0];
					UnityEngine.GUILayout.Label(
						$@"{tx + 1}: {ground_segment_[tx].nodeName}; CanTarget={
							antenna.CanTarget}, Target={antenna.Target}");
				}
				if (UnityEngine.GUILayout.Button("Target active vessel"))
				{
					for (int tx = 0; tx < ground_segment_.Count; ++tx)
					{
						var config = new ConfigNode(AntennaTarget.nodeName);
						config.AddValue("name", $"{AntennaTarget.TargetMode.Vessel}");
						config.AddValue("vesselId", FlightGlobals.ActiveVessel.id);
						var antenna = ground_segment_[tx].Comm.RAAntennaList[0];
						antenna.Target = AntennaTarget.LoadFromConfig(config, antenna);
					}
				}
				if (UnityEngine.GUILayout.Button("Target current alt./az."))
				{
					for (int tx = 0; tx < ground_segment_.Count; ++tx)
					{
						var q = FlightGlobals.ActiveVessel.GetWorldPos3D();
						ground_segment_[tx].Comm.ParentBody.GetLatLonAlt(
							q, out double lat, out double lon, out double alt);
						var config = new ConfigNode(AntennaTarget.nodeName);
						config.AddValue("name", $"{AntennaTarget.TargetMode.BodyLatLonAlt}");
						config.AddValue("bodyName", Planetarium.fetch.Home.name);
						config.AddValue("latLonAlt", new Vector3d(lat, lon, alt));
						var antenna = ground_segment_[tx].Comm.RAAntennaList[0];
						antenna.Target = AntennaTarget.LoadFromConfig(config, antenna);
					}
				}
				if (UnityEngine.GUILayout.Button("Clear target"))
				{
					for (int tx = 0; tx < ground_segment_.Count; ++tx)
					{
						var antenna = ground_segment_[tx].Comm.RAAntennaList[0];
						antenna.Target = null;
					}
				}
				foreach (var vessel_time in space_segment_)
				{
					double age_s = Planetarium.GetUniversalTime() - vessel_time.Value;
					string age = null;
					if (age_s > 2 * KSPUtil.dateTimeFormatter.Day)
					{
						age = $"{age_s / KSPUtil.dateTimeFormatter.Day:F0} days ago";
					}
					else if (age_s > 2 * KSPUtil.dateTimeFormatter.Hour)
					{
						age = $"{age_s / KSPUtil.dateTimeFormatter.Hour:F0} hours ago";
					}
					else if (age_s > 2 * KSPUtil.dateTimeFormatter.Minute)
					{
						age = $"{age_s / KSPUtil.dateTimeFormatter.Minute:F0} minutes ago";
					}
					else if (age_s > 2)
					{
						age = $"{age_s:F0} seconds ago";
					}
					using (new UnityEngine.GUILayout.HorizontalScope())
					{
						UnityEngine.GUILayout.Label($"{vessel_time.Key.name} {age}");
						if (age != null &&
							UnityEngine.GUILayout.Button("Remove", UnityEngine.GUILayout.Width(4 * 20)))
						{
							space_segment_.Remove(vessel_time.Key);
							return;
						}
					}
					show_network_ = UnityEngine.GUILayout.Toggle(show_network_, "Show network");
					show_active_links_ = UnityEngine.GUILayout.Toggle(show_active_links_, "Active links only");
				}
			}
			UnityEngine.GUI.DragWindow();
		}

		private void LateUpdate()
		{
			if (!show_network_)
			{
				return;
			}
			var ui = CommNet.CommNetUI.Instance as RACommNetUI;
			foreach (var station in ground_segment_)
			{
				ui.OverrideShownCones.Add(station.Comm);
			}
			foreach (var satellite in space_segment_.Keys)
			{
				ui.OverrideShownCones.Add(satellite.Connection.Comm as RACommNode);
			}
			if (show_active_links_)
			{
				ui.OverrideShownLinks.AddRange(active_links_);
			}
			else
			{
				foreach(var station in ground_segment_)
				{
					ui.OverrideShownLinks.AddRange(station.Comm.Values);
				}
				foreach(var satellite in space_segment_.Keys)
				{
					foreach(var link in satellite.Connection.Comm.Values)
					{
						if (space_segment_.ContainsKey((link.b as RACommNode).ParentVessel))
						{
							ui.OverrideShownLinks.Add(link);
						}
					}
				}
			}
		}

		private List<RACommNetHome> ground_segment_;
		private Dictionary<Vessel, double> space_segment_;  // Values are the last time on network.
		private List<CommNet.CommLink> active_links_ = new List<CommNet.CommLink>();
		private List<GroundStationSiteNode> ground_station_nodes_;
		private bool show_network_ = true;
		private bool show_active_links_ = true;
		private double min_rate_;
		private double[,] rate_matrix_;
		private double[,] latency_matrix_;
		private UnityEngine.Rect window_;
	}
}
