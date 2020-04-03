﻿using System;
using System.Collections.Generic;
using UnityEngine;
using KERBALISM;
using System.Text;

namespace KerbalismContracts
{
	public class KerbalismContractRequirement
	{
		public string name { get; private set; }
		public string title { get; private set; }
		public string notes { get; private set; }
		public List<SubRequirement> SubRequirements { get; private set; }

		public KerbalismContractRequirement(ConfigNode node)
		{
			name = Lib.ConfigValue(node, "name", "");
			title = Lib.ConfigValue(node, "title", "");
			notes = Lib.ConfigValue(node, "notes", "");

			Utils.LogDebug($"Loading requirement '{name}'");

			SubRequirements = new List<SubRequirement>();

			foreach(var requirementNode in node.GetNodes("Requirement"))
			{
				SubRequirement sr = SubRequirement.Load(this, requirementNode);

				if (sr == null)
				{
					Utils.Log($"Unknown requirement type '{Lib.ConfigValue(requirementNode, "name", "")}'", LogLevel.Error);
				}
				else
				{
					if(SubRequirements.Find(s => s.type == sr.type) != null)
					{
						Utils.Log($"Requirement '{name}' contains more than one instances of sub requirement type {sr.type}. This is not supported, discarding second instance!", LogLevel.Error);
						continue;
					}
					SubRequirements.Add(sr);
				}
			}

			if(SubRequirements.Count == 0)
			{
				Utils.Log($"Requirement '{name}' has no sub requirements", LogLevel.Error);
			}
		}

		internal string GetNotes(Contracts.Contract contract)
		{
			return notes;
		}

		internal bool NeedsWaypoint()
		{
			foreach (var sr in SubRequirements)
				if (sr.NeedsWaypoint())
					return true;
			return false;
		}
	}
}