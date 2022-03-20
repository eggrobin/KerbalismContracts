using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using ContractConfigurator.Behaviour;

namespace skopos {
  class GroundSegmentMutationFactory : BehaviourFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      stations_ = node.GetValues("station");
      customers_ = node.GetValues("customers");
      parameters_ = node.GetNode("condition").GetValues("customers");
      Enum.TryParse(node.GetNode("condition").GetValue("state"), out state_);
      return ok;
    }
    public override ContractBehaviour Generate(ConfiguredContract contract) {
      return new GroundSegmentMutation(stations_, customers_, state_, parameters_);
    }

    private string[] stations_;
    private string[] customers_;
    private string[] parameters_;
    private TriggeredBehaviour.State state_;
  }

  class GroundSegmentMutation : TriggeredBehaviour {
    private string[] stations_;
    private string[] customers_;

    public GroundSegmentMutation(string[] stations, string[] customers, TriggeredBehaviour.State state, string[] parameters) 
      : base(state, parameters.ToList()) {
      stations_ = stations;
      customers_ = customers;
    }

    protected override void TriggerAction() {
      throw new NotImplementedException();
    }
  }
}
