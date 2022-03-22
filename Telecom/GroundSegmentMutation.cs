using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using ContractConfigurator.Behaviour;

namespace skopos {
  public abstract class GroundSegmentMutationFactory : BehaviourFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      stations_ = node.GetValues("station");
      customers_ = node.GetValues("customer");
      monitored_connections_ = node.GetValues("monitored_connection");
      Enum.TryParse(node.GetNode("condition").GetValue("state"), out state_);
      parameters_ = node.GetNode("condition").GetValues("parameter");
      return ok;
    }
    public override ContractBehaviour Generate(ConfiguredContract contract) {
      return new GroundSegmentMutation(Operation(), stations_, customers_, monitored_connections_, state_, parameters_);
    }

    protected abstract GroundSegmentMutation.Operation Operation();

    private string[] stations_;
    private string[] customers_;
    private string[] monitored_connections_;
    private string[] parameters_;
    private TriggeredBehaviour.State state_;
  }

  public class AddToGroundSegmentFactory : GroundSegmentMutationFactory {
    protected override GroundSegmentMutation.Operation Operation() {
      return GroundSegmentMutation.Operation.ADD;
    }
  }
  public class RemoveFromGroundSegmentFactory : GroundSegmentMutationFactory {
    protected override GroundSegmentMutation.Operation Operation() {
      return GroundSegmentMutation.Operation.REMOVE;
    }
  }

  public class GroundSegmentMutation : TriggeredBehaviour {
    public enum Operation {
      ADD,
      REMOVE,
    }

    private Operation operation_;
    private string[] stations_;
    private string[] customers_;
    private string[] monitored_connections_;

    public GroundSegmentMutation(Operation operation,
                                 string[] stations,
                                 string[] customers,
                                 string[] monitored_connections,
                                 State state,
                                 string[] parameters) 
      : base(state, parameters.ToList()) {
      operation_ = operation;
      stations_ = stations;
      customers_ = customers;
      monitored_connections_ = monitored_connections;
    }

    protected override void TriggerAction() {
      if (operation_ == GroundSegmentMutation.Operation.ADD) {
        Telecom.Instance.network.AddStations(stations_);
        Telecom.Instance.network.AddCustomers(customers_);
        Telecom.Instance.network.AddMonitoredConnections(monitored_connections_);
      } else {
        Telecom.Instance.network.RemoveStations(stations_);
        Telecom.Instance.network.RemoveCustomers(customers_);
        Telecom.Instance.network.RemoveMonitoredConnections(monitored_connections_);
      }
    }
  }
}
