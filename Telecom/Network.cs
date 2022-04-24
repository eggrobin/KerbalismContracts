﻿using RealAntennas;
using RealAntennas.Antenna;
using RealAntennas.MapUI;
using RealAntennas.Network;
using RealAntennas.Precompute;
using RealAntennas.Targeting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace skopos {
  public class Network {

    static void Log(string message,
                    [CallerFilePath] string file = "",
                    [CallerLineNumber] int line = 0) {
      UnityEngine.Debug.Log($"[Σκοπος Telecom]: {message} ({file}:{line})");
    }

    static ConfigNode GetStationDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("skopos_telecom")) {
        foreach (var definition in block.config.GetNodes("station")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for station {name}");
    }

    static ConfigNode GetCustomerDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("skopos_telecom")) {
        foreach (var definition in block.config.GetNodes("customer")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for customer {name}");
    }

    static ConfigNode GetConnectionMonitorDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("skopos_telecom")) {
        foreach (var definition in block.config.GetNodes("connection_monitor")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for service level {name}");
    }

    static CelestialBody GetConfiguredBody(ConfigNode node) {
      string body_name = node.GetValue("body");
      return body_name != null ? FlightGlobals.GetBodyByName(body_name)
                               : FlightGlobals.GetHomeBody();
    }

    public Network(ConfigNode network_specification) {
      AddStations(network_specification.GetValues("station"));
      AddCustomers(network_specification.GetValues("customer"));
      AddConnectionMonitors(network_specification.GetValues("connection_monitor"));
    }

    private void RebuildConnections() {
      int n = stations_.Count + customers_.Count;
      connections_ = new Connection[n, n];
      for (int i = 0; i < n; i++) {
        for (int j = 0; j < n; ++j) {
          connections_[i, j] = new Connection();
        }
      }
      names_ = new string[n];
      int k = 0;
      foreach (string name in stations_.Keys) {
        names_[k++] = name;
      }
      foreach (string name in customers_.Keys) {
        names_[k++] = name;
      }
      foreach (var monitor in connection_monitors_.Values) {
        int tx = names_.IndexOf(monitor.tx_name);
        int rx = names_.IndexOf(monitor.tx_name);
        connections_[tx, rx].monitors_.Add(monitor);
      }
    }

    public void AddStations(IEnumerable<string> names) {
      foreach (var name in names) {
        if (stations_.ContainsKey(name)) {
          Log($"Station {name} already present");
          continue;
        }
        Log($"Adding station {name}");
        var node = GetStationDefinition(name);
        var body = GetConfiguredBody(node);
        var station =
          new UnityEngine.GameObject(body.name).AddComponent<RACommNetHome>();
        var station_node = new ConfigNode();
        foreach (string key in new[] { "objectName", "lat", "lon", "alt" }) {
          station_node.AddValue(key, node.GetValue(key));
        }
        station_node.AddValue("isKSC", false);
        station_node.AddValue("isHome", false);
        station_node.AddValue("icon", "RealAntennas/radio-antenna");
        foreach (var antenna in node.GetNodes("Antenna")) {
          station_node.AddNode(antenna);
        }
        station.Configure(station_node, body);
        stations_.Add(name, station);
        if (node.GetValue("role") == "tx") {
          tx_.Add(station);
        } else if (node.GetValue("role") == "rx") {
          rx_.Add(station);
        } else {
          tx_.Add(station);
          rx_.Add(station);
        }
      }
      connections_ = null;
    }
    public void AddCustomers(IEnumerable<string> names) {
      foreach (var name in names) {
        if (customers_.ContainsKey(name)) {
          Log($"Customer {name} already present");
          continue;
        }
        Log($"Adding customer {name}");
        customers_.Add(name, new Customer(GetCustomerDefinition(name), this));
      }
      connections_ = null;
    }
    public void AddConnectionMonitors(IEnumerable<string> names) {
      foreach (var name in names) {
        if (connection_monitors_.ContainsKey(name)) {
          Log($"Connection {name} already present");
          continue;
        }
        Log($"Adding connection {name}");
        connection_monitors_.Add(name, new ConnectionMonitor(GetConnectionMonitorDefinition(name)));
      }
      connections_ = null;
    }

    public void RemoveStations(IEnumerable<string> names) { }
    public void RemoveCustomers(IEnumerable<string> names) { }
    public void RemoveConnectionMonitors(IEnumerable<string> names) { }

    public void AddNominalLocation(Vessel v) {
      // TODO(egg): maybe this could be body-dependent.
      nominal_satellite_locations_.Add(
        UnityEngine.QuaternionD.Inverse(FlightGlobals.GetHomeBody().scaledBody.transform.rotation) *
          (v.GetWorldPos3D() - FlightGlobals.GetHomeBody().position));
      must_retarget_customers_ = true;
    }

    public Vector3d[] GetNominalLocationLatLonAlts() {
      var result = new List<Vector3d>(nominal_satellite_locations_.Count);
      foreach (var position in nominal_satellite_locations_) {
        FlightGlobals.GetHomeBody().GetLatLonAlt(
          FlightGlobals.GetHomeBody().scaledBody.transform.rotation * position +
          FlightGlobals.GetHomeBody().position,
          out double lat, out double lon, out double alt);
        result.Add(new Vector3d(lat, lon, alt));
      }
      return result.ToArray();
    }

    public void ClearNominalLocations() {
      nominal_satellite_locations_.Clear();
      must_retarget_customers_ = true;
    }

    public void Refresh() {
      if (connections_ == null) {
        RebuildConnections();
      }
      if (stations_.Values.Any(station => station.Comm == null)) {
        return;
      }
      foreach (var station in stations_.Values) {
        station.Comm.RAAntennaList[0].Target = null;
      }
      CreateGroundSegmentNodesIfNeeded();
      foreach (var customer in customers_.Values) {
        customer.Cycle();
      }
      if (customers_.Values.Any(customer => customer.station == null)) {
        return;
      }
      if (must_retarget_customers_) {
        foreach (var customer in customers_.Values) {
          customer.Retarget();
        }
        must_retarget_customers_ = false;
      }
      UpdateConnections();
    }

    private void CreateGroundSegmentNodesIfNeeded() {
      // TODO(egg): Rewrite taking mutability into account.
      if (ground_segment_nodes_ == null && MapView.fetch != null) {
        ground_segment_nodes_ = new List<SiteNode>();
        foreach (var station in stations_.Values) {
          ground_segment_nodes_.Add(MakeSiteNode(station));
        }
      }
    }

    private static SiteNode MakeSiteNode(RACommNetHome station) {
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

    private ConfigNode MakeTargetConfig(CelestialBody body, Vector3d station_world_position) {
      var config = new ConfigNode(AntennaTarget.nodeName);
      if (nominal_satellite_locations_.Count == 0) {
        return config;
      }
      Vector3d station_position =
          UnityEngine.QuaternionD.Inverse(body.scaledBody.transform.rotation) *
            (station_world_position - body.position);
      Vector3d station_zenith = station_position.normalized;
      Vector3d target = default;
      double max_cos_zenithal_angle = double.NegativeInfinity;
      foreach (var position in nominal_satellite_locations_) {
        double cos_zenithal_angle = Vector3d.Dot(station_zenith, position - station_position);
        if (cos_zenithal_angle > max_cos_zenithal_angle) {
          max_cos_zenithal_angle = cos_zenithal_angle;
          target = position;
        }
      }
      body.GetLatLonAlt(
        body.scaledBody.transform.rotation * target + body.position,
        out double lat, out double lon, out double alt);
      config.AddValue("name", $"{AntennaTarget.TargetMode.BodyLatLonAlt}");
      config.AddValue("bodyName", Planetarium.fetch.Home.name);
      config.AddValue("latLonAlt", new Vector3d(lat, lon, alt));
      return config;
    }

    private void UpdateConnections() {
      var network = CommNet.CommNetNetwork.Instance.CommNet as RACommNetwork;
      if (network == null) {
        Log("No RA comm network");
        return;
      }
      all_ground_ = stations_.Values.Concat(from customer in customers_.Values select customer.station).ToArray();
      min_rate_ = double.PositiveInfinity;
      active_links_.Clear();
      for (int tx = 0; tx < all_ground_.Length; ++tx) {
        for (int rx = 0; rx < all_ground_.Length; ++rx) {
          if (rx == tx || !tx_.Contains(all_ground_[tx]) || !rx_.Contains(all_ground_[rx])) {
            connections_[tx, rx].AddMeasurement(double.NaN, double.NaN);
            continue;
          }
          var path = new CommNet.CommPath();
          network.FindClosestWhere(
            all_ground_[tx].Comm, path, (_, n) => n == all_ground_[rx].Comm);
          double rate = double.PositiveInfinity;
          double length = 0;
          foreach (var l in path) {
            active_links_.Add(l);
            RACommLink link = l as RACommLink;
            rate = Math.Min(rate, link.FwdDataRate);
            length += (l.a.position - l.b.position).magnitude;
            if ((l.end as RACommNode).ParentVessel is Vessel vessel) {
              space_segment_[vessel] = Planetarium.GetUniversalTime();
            }
          }
          if (path.IsEmpty()) {
            rate = 0;
          }
          connections_[tx, rx].AddMeasurement(rate: rate, latency: length / 299792458);
          min_rate_ = Math.Min(min_rate_, rate);
        }
      }
    }

    public ConnectionMonitor Monitor(string name) {
      return connection_monitors_[name];
    }

    private class Customer {
      public Customer(ConfigNode template, Network network) {
        template_ = template;
        network_ = network;
        body_ = GetConfiguredBody(template_);
      }

      public void Cycle() {
        if (network_.freeze_customers_) {
          return;
        }
        if (imminent_station_ != null) {
          DestroyStation();
          station = imminent_station_;
          imminent_station_ = null;
        }
        if (imminent_station_ == null && upcoming_station_?.Comm != null) {
          imminent_station_ = upcoming_station_;
          upcoming_station_ = null;
          (RACommNetScenario.Instance as RACommNetScenario)?.Network?.InvalidateCache();
        }
        if (upcoming_station_ == null) {
          upcoming_station_ = MakeStation();
        }
      }
      public void Retarget() {
        var antenna = station.Comm.RAAntennaList[0];
        antenna.Target = AntennaTarget.LoadFromConfig(network_.MakeTargetConfig(body_, station.Comm.precisePosition), antenna);
      }

      private RACommNetHome MakeStation() {
        HashSet<string> biomes = template_.GetValues("biome").ToHashSet();
        const double degree = Math.PI / 180;
        double lat;
        double lon;
        int i = 0;
        do {
          ++i;
          double sin_lat_min =
            Math.Sin(double.Parse(template_.GetValue("lat_min")) * degree);
          double sin_lat_max =
            Math.Sin(double.Parse(template_.GetValue("lat_max")) * degree);
          double lon_min = double.Parse(template_.GetValue("lon_min")) * degree;
          double lon_max = double.Parse(template_.GetValue("lon_max")) * degree;
          lat = Math.Asin(sin_lat_min + network_.random_.NextDouble() * (sin_lat_max - sin_lat_min));
          lon = lon_min + network_.random_.NextDouble() * (lon_max - lon_min);
        } while (!biomes.Contains(body_.BiomeMap.GetAtt(lat, lon).name));
        var new_station =
          new UnityEngine.GameObject(body_.name).AddComponent<RACommNetHome>();
        var node = new ConfigNode();
        node.AddValue("objectName", $"{template_.GetValue("name")} @{lat / degree:F2}, {lon / degree:F2} ({i} tries)");
        node.AddValue("lat", lat / degree);
        node.AddValue("lon", lon / degree);
        double alt = body_.TerrainAltitude(lat / degree, lon / degree) + 10;
        node.AddValue("alt", alt);
        node.AddValue("isKSC", false);
        node.AddValue("isHome", false);
        node.AddValue("icon", "RealAntennas/DSN");
        Vector3d station_position = body_.GetWorldSurfacePosition(lat, lon, alt);
        foreach (var antenna in template_.GetNodes("Antenna")) {
          var targeted_antenna = antenna.CreateCopy();
          targeted_antenna.AddNode(network_.MakeTargetConfig(body_, station_position));
          node.AddNode(targeted_antenna);
        }
        new_station.Configure(node, body_);
        if (template_.GetValue("role") == "tx") {
          network_.tx_.Add(new_station);
        } else if (template_.GetValue("role") == "rx") {
          network_.rx_.Add(new_station);
        } else {
          network_.tx_.Add(new_station);
          network_.rx_.Add(new_station);
        }
        return new_station;
      }

      private void DestroyStation() {
        if (station == null) {
          return;
        }
        network_.tx_.Remove(station);
        network_.rx_.Remove(station);
        CommNet.CommNetNetwork.Instance.CommNet.Remove(station.Comm);
        if (node_ != null) {
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
      private CelestialBody body_;
      private Network network_;
    }

    public class ConnectionMonitor {
      public ConnectionMonitor(ConfigNode node) {
        tx_name = node.GetValue("tx");
        rx_name = node.GetValue("rx");
        latency_threshold = double.Parse(node.GetValue("latency"));
        rate_threshold = double.Parse(node.GetValue("rate"));
      }

        public void AddMeasurement(double latency, double rate, double Δt) {
        if (latency <= latency_threshold || rate >= rate_threshold) {
          time_within_sla_ += Δt;
        }
        total_measurement_time_ += Δt;
      }
      public double latency_threshold { get; }
      public double rate_threshold { get; }
      public double availability => time_within_sla_ / total_measurement_time_;

      public string tx_name { get; }
      public string rx_name { get; }

      // TODO(egg): Save these.
      private double time_within_sla_;
      private double total_measurement_time_;
    }

    public class Connection {
      public void AddMeasurement(double latency, double rate) {
        if (last_measurement_time_ != null) {
          double Δt = Planetarium.GetUniversalTime() - last_measurement_time_.Value;
          current_latency = latency;
          current_rate = rate;
          foreach (var monitor in monitors_) {
            monitor.AddMeasurement(latency, rate, Δt);
          }
        }
        last_measurement_time_ = Planetarium.GetUniversalTime();
      }
      public double current_latency { get; private set; }
      public double current_rate { get; private set; }
      public List<ConnectionMonitor> monitors_ = new List<ConnectionMonitor>();
      private double? last_measurement_time_;
    }

    public int customer_pool_size { get; set; }

    private readonly SortedDictionary<string, Customer> customers_ =
        new SortedDictionary<string, Customer>();
    private readonly SortedDictionary<string, RACommNetHome> stations_ =
        new SortedDictionary<string, RACommNetHome>();
    private readonly SortedDictionary<string, ConnectionMonitor> connection_monitors_ =
        new SortedDictionary<string, ConnectionMonitor>();
    private List<SiteNode> ground_segment_nodes_;
    public readonly HashSet<RACommNetHome> tx_ = new HashSet<RACommNetHome>();
    public readonly HashSet<RACommNetHome> rx_ = new HashSet<RACommNetHome>();
    public readonly Dictionary<Vessel, double> space_segment_ = new Dictionary<Vessel, double>();
    private readonly List<Vector3d> nominal_satellite_locations_ = new List<Vector3d>();
    bool must_retarget_customers_ = false;
    private readonly Random random_ = new Random();
    public readonly List<CommNet.CommLink> active_links_ = new List<CommNet.CommLink>();
    public RACommNetHome[] all_ground_ = { };
    public Connection[,] connections_ = { };
    public string[] names_ = { };
    public double min_rate_;
    public bool freeze_customers_;
  }
}
