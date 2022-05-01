using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using Contracts;

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

    private string connection_;
    private double availability_;
  }
}
