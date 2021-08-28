using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using RealAntennas.Network;
using RealAntennas.MapUI;

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
			}
			min_rate_ = double.PositiveInfinity;
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
						RACommLink link = l.start[l.end] as RACommLink;
						rate = Math.Min(rate, link.FwdDataRate);
						length += (l.a.position - l.b.position).magnitude;
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
					UnityEngine.GUILayout.Label($"{tx + 1}: {ground_segment_[tx].nodeName}");
				}
			}
			UnityEngine.GUI.DragWindow();
		}

		private List<RACommNetHome> ground_segment_;
		private List<GroundStationSiteNode> ground_station_nodes_;
		private double min_rate_;
		private double[,] rate_matrix_;
		private double[,] latency_matrix_;
		private UnityEngine.Rect window_;
	}
}
