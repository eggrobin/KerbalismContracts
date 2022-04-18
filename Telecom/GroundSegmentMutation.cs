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
      connection_monitors_ = node.GetValues("connection_monitor");
      Enum.TryParse(node.GetNode("condition").GetValue("state"), out state_);
      parameters_ = node.GetNode("condition").GetValues("parameter");
      return ok;
    }
    public override ContractBehaviour Generate(ConfiguredContract contract) {
      return new GroundSegmentMutation(Operation(), stations_, customers_, connection_monitors_, state_, parameters_);
    }

    protected abstract GroundSegmentMutation.Operation Operation();

    private string[] stations_;
    private string[] customers_;
    private string[] connection_monitors_;
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
    private string[] connection_monitors_;

    public GroundSegmentMutation(Operation operation,
                                 string[] stations,
                                 string[] customers,
                                 string[] connection_monitors,
                                 State state,
                                 string[] parameters) 
      : base(state, parameters.ToList()) {
      operation_ = operation;
      stations_ = stations;
      customers_ = customers;
      connection_monitors_ = connection_monitors;
    }

    protected override void TriggerAction() {
      if (operation_ == GroundSegmentMutation.Operation.ADD) {
        Telecom.Instance.network.AddStations(stations_);
        Telecom.Instance.network.AddCustomers(customers_);
        Telecom.Instance.network.AddConnectionMonitors(connection_monitors_);
      } else {
        Telecom.Instance.network.RemoveStations(stations_);
        Telecom.Instance.network.RemoveCustomers(customers_);
        Telecom.Instance.network.RemoveConnectionMonitors(connection_monitors_);
      }
    }
  }
}
