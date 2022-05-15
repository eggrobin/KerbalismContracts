using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using Contracts;
using RealAntennas;

namespace skopos {
  public class ConnectionAvailabilityFactory : ParameterFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue<string>(node, "connection", x => connection_ = x, this);
      ok &= ConfigNodeUtil.ParseValue<double>(node, "availability", x => availability_ = x, this);
      return ok;
    }
    public override ContractParameter Generate(Contract contract) {
      return new ConnectionAvailability(connection_, availability_);
    }
    private string connection_;
    private double availability_;
  }

  public class ConnectionAvailability : ContractParameter {
    public ConnectionAvailability() {}

    public ConnectionAvailability(string connection, double availability) {
      connection_ = connection;
      availability_ = availability;
      disableOnStateChange = false;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      if (Telecom.Instance.network.Monitor(connection_).availability >= availability_) {
        SetComplete();
      } else {
        SetIncomplete();
      }
    }

    protected override void OnLoad(ConfigNode node) {
      connection_ = node.GetValue("connection");
      availability_ = double.Parse(node.GetValue("availability"));
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("connection", connection_);
      node.AddValue("availability", availability_);
    }

    protected override string GetMessageComplete() {
      return "meow complete";
    }

    protected override string GetMessageIncomplete() {
      return "meow incomplete";
    }

    protected override string GetMessageFailed() {
      return "meow failed";
    }

    protected override string GetNotes() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      string data_rate = RATools.PrettyPrintDataRate(connection.rate_threshold);
      double latency = connection.latency_threshold;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";
      return $"At least {data_rate}, with a latency of at most {pretty_latency}";
    }

    protected override string GetTitle() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      var tx = Telecom.Instance.network.GetStation(connection.tx_name);
      var rx = Telecom.Instance.network.GetStation(connection.rx_name);
      return $"{tx.displaynodeName} to {rx.displaynodeName}:\n" +
             $"{connection.availability:P0} availability (target: {availability_:P0})";
    }

    private string connection_;
    private double availability_;
  }
}
