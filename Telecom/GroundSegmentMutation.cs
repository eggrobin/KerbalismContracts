using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using ContractConfigurator.Behaviour;

namespace skopos {
  public abstract class GroundSegmentMutationFactory : BehaviourFactory {
    private static readonly List<string> empty_ = new List<string>();
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue(node, "station", x => stations_ = x, this, empty_);
      ok &= ConfigNodeUtil.ParseValue(node, "customer", x => customers_ = x, this, empty_);
      ok &= ConfigNodeUtil.ParseValue(node, "connection", x => connections_ = x, this, empty_);
      var condition = ConfigNodeUtil.GetChildNode(node, "condition");
      ok &= ConfigNodeUtil.ParseValue<TriggeredBehaviour.State>(condition, "state", x => state_ = x, this);
      ok &= ConfigNodeUtil.ParseValue(condition, "parameter", x => parameters_ = x, this, empty_);
      return ok;
    }
    public override ContractBehaviour Generate(ConfiguredContract contract) {
      return new GroundSegmentMutation(Operation(), stations_, customers_, connections_, state_, parameters_);
    }

    protected abstract GroundSegmentMutation.Operation Operation();

    private List<string> stations_;
    private List<string> customers_;
    private List<string> connections_;
    private List<string> parameters_;
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
    private List<string> stations_;
    private List<string> customers_;
    private List<string> connections_;

    public GroundSegmentMutation(Operation operation,
                                 List<string> stations,
                                 List<string> customers,
                                 List<string> connections,
                                 State state,
                                 List<string> parameters) 
      : base(state, parameters.ToList()) {
      operation_ = operation;
      stations_ = stations;
      customers_ = customers;
      connections_ = connections;
    }

    protected override void TriggerAction() {
      if (operation_ == Operation.ADD) {
        Telecom.Instance.network.AddStations(stations_);
        Telecom.Instance.network.AddCustomers(customers_);
        Telecom.Instance.network.AddConnections(connections_);
      } else {
        Telecom.Instance.network.RemoveStations(stations_);
        Telecom.Instance.network.RemoveCustomers(customers_);
        Telecom.Instance.network.RemoveConnections(connections_);
      }
    }
  }
}
