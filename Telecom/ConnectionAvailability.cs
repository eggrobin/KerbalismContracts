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
      connection_ = node.GetValue("connection");
      availability_ = double.Parse(node.GetValue("availability"));
      return ok;
    }
    public override ContractParameter Generate(Contract contract) {
      return new ConnectionAvailability(connection_, availability_);
    }
    private string connection_;
    private double availability_;
  }

  public class ConnectionAvailability : ContractParameter {
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

    private string connection_;
    private double availability_;
  }
}
