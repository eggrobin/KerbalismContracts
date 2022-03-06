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
			if (network_ == null) {
				network_ = new Network(GameDatabase.Instance.GetConfigs("skopos_telecom")[0].config);
			}
			network_.Refresh();
		}

		private void DrawWindow(int id)
		{
			using (new UnityEngine.GUILayout.VerticalScope())
			{
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					if (UnityEngine.GUILayout.Button("Add nominal location") && FlightGlobals.ActiveVessel != null)
					{
						network_.AddNominalLocation(FlightGlobals.ActiveVessel);
						return;
					}
					if (UnityEngine.GUILayout.Button("Clear nominal locations"))
					{
						network_.ClearNominalLocations();
						return;
					}
					network_.freeze_customers_ = UnityEngine.GUILayout.Toggle(network_.freeze_customers_, "Freeze customers");
				}
				foreach (Vector3d location in network_.GetNominalLocationLatLonAlts())
				{
					UnityEngine.GUILayout.Label($"{location.x:F2}°, {location.y:F2}°, {location.z/1000:F0} km");
				}
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					if (int.TryParse(UnityEngine.GUILayout.TextField(network_.customer_pool_size.ToString()), out int pool_size))
					{
						network_.customer_pool_size = Math.Max(pool_size, 0);
					}
				}
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					UnityEngine.GUILayout.Label(@"Tx\Rx", UnityEngine.GUILayout.Width(3 * 20));
					for (int rx = 0; rx < network_.all_ground_.Length; ++rx)
					{
						UnityEngine.GUILayout.Label($"{rx + 1}", UnityEngine.GUILayout.Width(6 * 20));
					}
				}
				for (int tx = 0; tx < network_.all_ground_.Length; ++tx)
				{
					using (new UnityEngine.GUILayout.HorizontalScope())
					{
						UnityEngine.GUILayout.Label($"{tx + 1}", UnityEngine.GUILayout.Width(3 * 20));
						for (int rx = 0; rx < network_.all_ground_.Length; ++rx)
						{
							double rate = network_.connections_[tx, rx].current_rate;
							double availability = network_.connections_[tx, rx].rate_availability;
							double threshold = network_.connections_[tx, rx].rate_threshold;
							UnityEngine.GUILayout.Label(
								double.IsNaN(rate)
									? "—"
									: (RATools.PrettyPrintDataRate(rate) + "\n" +
									   availability.ToString("P2") + " > " +
									   RATools.PrettyPrintDataRate(threshold)),
								UnityEngine.GUILayout.Width(6 * 20));
						}
					}
				}
				using (new UnityEngine.GUILayout.HorizontalScope())
				{
					UnityEngine.GUILayout.Label(@"Tx\Rx", UnityEngine.GUILayout.Width(3 * 20));
					for (int rx = 0; rx < network_.all_ground_.Length; ++rx)
					{
						UnityEngine.GUILayout.Label($"{rx + 1}", UnityEngine.GUILayout.Width(6 * 20));
					}
				}
				for (int tx = 0; tx < network_.all_ground_.Length; ++tx)
				{
					using (new UnityEngine.GUILayout.HorizontalScope())
					{
						UnityEngine.GUILayout.Label($"{tx + 1}", UnityEngine.GUILayout.Width(3 * 20));
						for (int rx = 0; rx < network_.all_ground_.Length; ++rx)
						{
							double latency = network_.connections_[tx, rx].current_latency;
							double availability = network_.connections_[tx, rx].rate_availability;
							double threshold = network_.connections_[tx, rx].latency_threshold;
							UnityEngine.GUILayout.Label(
								double.IsNaN(latency)
									? "—"
									: $"{latency * 1000:F0} ms\n{availability:P2} < {threshold * 1000:F0} ms",
								UnityEngine.GUILayout.Width(6 * 20));
						}
					}
				}
				for (int i = 0; i < network_.all_ground_.Length; ++i)
				{
					var station = network_.all_ground_[i];
					var antenna = station.Comm.RAAntennaList[0];
					string role =
						(network_.tx_.Contains(station) ? "T" : "") +
						(network_.rx_.Contains(station) ? "R" : "") + "x";
					UnityEngine.GUILayout.Label(
						$@"{i + 1}: {role} {station.nodeName}; CanTarget={
							antenna.CanTarget}, Target={antenna.Target?.ToString() ?? "null"}");
				}
				foreach (var vessel_time in network_.space_segment_)
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
							network_.space_segment_.Remove(vessel_time.Key);
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
			if (ui == null)
			{
				return;
			}
			foreach (var station in network_.all_ground_)
			{
				ui.OverrideShownCones.Add(station.Comm);
			}
			foreach (var satellite in network_.space_segment_.Keys)
			{
				ui.OverrideShownCones.Add(satellite.Connection.Comm as RACommNode);
			}
			if (show_active_links_)
			{
				ui.OverrideShownLinks.AddRange(network_.active_links_);
			}
			else
			{
				foreach(var station in network_.all_ground_)
				{
					ui.OverrideShownLinks.AddRange(station.Comm.Values);
				}
				foreach(var satellite in network_.space_segment_.Keys)
				{
					foreach(var link in satellite.Connection.Comm.Values)
					{
						Vessel vessel = (link.b as RACommNode).ParentVessel;
						if (vessel != null && network_.space_segment_.ContainsKey(vessel))
						{
							ui.OverrideShownLinks.Add(link);
						}
					}
				}
			}
		}

		private Network network_;
		private bool show_network_ = true;
		private bool show_active_links_ = true;
		private UnityEngine.Rect window_;
	}
}
