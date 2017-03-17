﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KerbalHealth
{
    public class KerbalHealthStatus
    {
        public enum HealthCondition { OK, Exhausted }  // conditions

        string name;
        double hp;
        double lastChange = 0;  // Cached HP change per day (for unloaded vessels)
        double lastMarginalPositiveChange = 0, lastMarginalNegativeChange = 0;  // Cached marginal HP change (in %)
        HealthCondition condition = HealthCondition.OK;
        string trait = null;
        bool onEva = false;  // True if kerbal is on EVA

        // These dictionaries are used to calculate factor modifiers
        Dictionary<string, double> fmBonusSums = new Dictionary<string, double>(), fmFreeMultipliers = new Dictionary<string, double>();
        double minMultiplier, maxMultiplier;

        public string Name
        {
            get { return name; }
            set
            {
                name = value;
                pcmCached = null;
            }
        }

        public double HP
        {
            get { return hp; }
            set
            {
                if (value < Core.MinHP) hp = Core.MinHP;
                else if (value > MaxHP) hp = MaxHP;
                else hp = value;
            }
        }

        public double Health { get { return (HP - Core.MinHP) / (MaxHP - Core.MinHP); } }  // % of health relative to MaxHealth

        public double LastChange
        {
            get { return lastChange; }
            set { lastChange = value; }
        }

        public double LastMarginalPositiveChange
        {
            get { return lastMarginalPositiveChange; }
            set { lastMarginalPositiveChange = value; }
        }

        public double LastMarginalNegativeChange
        {
            get { return lastMarginalNegativeChange; }
            set { lastMarginalNegativeChange = value; }
        }

        public double MarginalChange
        { get { return (MaxHP - HP) * (LastMarginalPositiveChange / 100) - (HP - Core.MinHP) * (LastMarginalNegativeChange / 100); } }

        public double LastChangeTotal
        { get { return LastChange + MarginalChange; } }

        public HealthCondition Condition
        {
            get { return condition; }
            set
            {
                if (value == condition) return;
                switch (value)
                {
                    case HealthCondition.OK:
                        Core.Log("Reviving " + Name + " as " + Trait + "...", Core.LogLevel.Important);
                        if (PCM.type != ProtoCrewMember.KerbalType.Tourist) return;  // Apparently, the kerbal has been revived by another mod
                        PCM.type = ProtoCrewMember.KerbalType.Crew;
                        PCM.trait = Trait;
                        break;
                    case HealthCondition.Exhausted:
                        Core.Log(Name + " (" + Trait + ") is exhausted.", Core.LogLevel.Important);
                        Trait = PCM.trait;
                        PCM.type = ProtoCrewMember.KerbalType.Tourist;
                        break;
                }
                condition = value;
            }
        }

        string Trait
        {
            get { return trait ?? PCM.trait; }
            set { trait = value; }
        }

        public bool IsOnEVA
        {
            get { return onEva; }
            set { onEva = value; }
        }

        ProtoCrewMember pcmCached;
        public ProtoCrewMember PCM
        {
            get
            {
                if (pcmCached != null) return pcmCached;
                foreach (ProtoCrewMember pcm in HighLogic.fetch.currentGame.CrewRoster.Crew)
                    if (pcm.name == Name)
                    {
                        pcmCached = pcm;
                        return pcm;
                    }
                foreach (ProtoCrewMember pcm in HighLogic.fetch.currentGame.CrewRoster.Tourist)
                    if (pcm.name == Name)
                    {
                        pcmCached = pcm;
                        return pcm;
                    }
                return null;
            }
            set
            {
                Name = value.name;
                pcmCached = value;
            }
        }

        public static double GetMaxHP(ProtoCrewMember pcm)
        { return Core.BaseMaxHP + Core.HPPerLevel * pcm.experienceLevel; }

        public double MaxHP
        { get { return GetMaxHP(PCM); } }

        public double TimeToValue(double target)
        {
            double change = HealthChangePerDay();
            if (change == 0) return double.NaN;
            double res = (target - HP) / change;
            if (res < 0) return double.NaN;
            return res * 21600;
        }

        public double NextConditionHP()
        {
            if (HealthChangePerDay() > 0)
            {
                switch (Condition)
                {
                    case HealthCondition.OK:
                        return MaxHP;
                    case HealthCondition.Exhausted:
                        return Core.ExhaustionEndHealth * MaxHP;
                }
            }
            switch (Condition)
            {
                case HealthCondition.OK:
                    return Core.ExhaustionStartHealth * MaxHP;
                case HealthCondition.Exhausted:
                    return Core.DeathHealth * MaxHP;
            }
            return double.NaN;
        }

        public double TimeToNextCondition()
        { return TimeToValue(NextConditionHP()); }

        // Returns HP level when marginal HP change balances out "fixed" change. If <= 0, no such level
        public double GetBalanceHP()
        {
            Core.Log(Name + "'s last change: " + LastChange + ", MPC: " + LastMarginalPositiveChange + "%, MNC: " + LastMarginalNegativeChange + "%.");
            if (LastChange == 0) HealthChangePerDay();
            if (LastMarginalPositiveChange <= LastMarginalNegativeChange) return 0;
            return (MaxHP * LastMarginalPositiveChange + LastChange * 100) / (LastMarginalPositiveChange - LastMarginalNegativeChange);
        }

        bool IsInCrew(ProtoCrewMember[] crew)
        {
            foreach (ProtoCrewMember pcm in crew) if (pcm?.name == Name) return true;
            return false;
        }

        void ProcessPart(Part part, ProtoCrewMember[] crew, ref double change)
        {
            int i = 0;
            foreach (ModuleKerbalHealth mkh in part.FindModulesImplementing<ModuleKerbalHealth>())
            {
                Core.Log("Processing MKH #" + (++i) + "/" + part.FindModulesImplementing<ModuleKerbalHealth>().Count + " of " + part.name + "...\nCrew has " + crew.Length + " members.");
                if (mkh.IsModuleActive && (!mkh.partCrewOnly || IsInCrew(crew)))
                {
                    change += mkh.hpChangePerDay;
                    if (mkh.hpMarginalChangePerDay > 0) LastMarginalPositiveChange += mkh.hpMarginalChangePerDay;
                    else if (mkh.hpMarginalChangePerDay < 0) LastMarginalNegativeChange -= mkh.hpMarginalChangePerDay;
                    // Processing factor multiplier
                    if (mkh.multiplier != 1)
                    {
                        if (mkh.crewCap > 0) fmBonusSums[mkh.multiplyFactor] += (1 - mkh.multiplier) * Math.Min(mkh.crewCap, mkh.AffectedCrewCount);
                        else fmFreeMultipliers[mkh.multiplyFactor] *= mkh.multiplier;
                        if (mkh.multiplier > 1) maxMultiplier = Math.Max(maxMultiplier, mkh.multiplier);
                        else minMultiplier = Math.Min(minMultiplier, mkh.multiplier);
                    }
                }
                else Core.Log("This module doesn't affect " + Name + "(active: " + mkh.IsModuleActive + "; part crew only: " + mkh.partCrewOnly + "; in part's crew: " + IsInCrew(crew) + ")");
            }
        }

        double Multiplier(string factorId)
        {
            double res = 1 - fmBonusSums[factorId] / Core.GetCrewCount(PCM);
            if (res < 1) res = Math.Max(res, minMultiplier); else res = Math.Min(res, maxMultiplier);
            Core.Log("Multiplier for " + factorId + " for " + Name + " is " + res + " (bonus sum: " + fmBonusSums[factorId] + "; free multiplier: " + fmFreeMultipliers[factorId] + "; multipliers: " + minMultiplier + ".." + maxMultiplier + ")");
            return res * fmFreeMultipliers[factorId];
        }

        public double HealthChangePerDay()
        {
            double change = 0;
            ProtoCrewMember pcm = PCM;
            if (pcm == null)
            {
                Core.Log(Name + " not found in Core.KerbalHealthList!");
                return 0;
            }

            if (IsOnEVA && (Core.IsKerbalLoaded(pcm) || (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Assigned)))
            {
                Core.Log(Name + " is back from EVA.", Core.LogLevel.Important);
                IsOnEVA = false;
            }

            LastMarginalPositiveChange = LastMarginalNegativeChange = 0;
            fmBonusSums.Clear();
            fmBonusSums.Add("All", 0);
            fmFreeMultipliers.Clear();
            fmFreeMultipliers.Add("All", 1);
            foreach (HealthFactor f in Core.Factors)
            {
                fmBonusSums.Add(f.Name, 0);
                fmFreeMultipliers.Add(f.Name, 1);
            }
            minMultiplier = maxMultiplier = 1;

            // Processing parts
            if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned && Core.IsKerbalLoaded(pcm))
                foreach (Part p in Core.KerbalVessel(pcm).Parts) ProcessPart(p, p.protoModuleCrew.ToArray(), ref change);
            else if (Core.IsInEditor)
                foreach (PartCrewManifest p in ShipConstruction.ShipManifest.PartManifests) ProcessPart(p.PartInfo.partPrefab, p.GetPartCrew(), ref change);

            if (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Assigned || Core.IsKerbalLoaded(pcm) || IsOnEVA || Core.IsInEditor)
            {
                Core.Log("Processing all the " + Core.Factors.Count + " factors for " + Name + "...");
                LastChange = 0;
                foreach (HealthFactor f in Core.Factors)
                {
                    if (f.LoadedOnly && !(Core.IsInEditor || Core.IsKerbalLoaded(pcm) || IsOnEVA))
                    {
                        Core.Log(f.Name + " does not affect " + pcm.name + " (" + (Core.IsInEditor ? "" : "not ") + "in editor, " + (Core.IsKerbalLoaded(pcm) ? "" : "not ") + "in vessel, " + (IsOnEVA ? "" : "not ") + "on EVA).");
                        continue;
                    }
                    double c = f.ChangePerDay(pcm) * Multiplier(f.Name) * Multiplier("All");
                    Core.Log(f.Name + "'s effect on " + pcm.name + " is " + c + " HP/day.");
                    LastChange += c;
                }
            }
            double mc = MarginalChange;
            Core.Log("Marginal change: " + mc + "(+" + LastMarginalPositiveChange + "%, -" + LastMarginalNegativeChange + "%).");
            Core.Log("Total change: " + (LastChange + mc) + " HP/day.");
            return LastChange + mc;
        }

        public void Update(double interval)
        {
            Core.Log("Updating " + Name + "'s health.");
            //if (DFWrapper.APIReady && DFWrapper.DeepFreezeAPI.FrozenKerbals.ContainsKey(Name))
            //{
            //    Core.Log(Name + " is frozen with DeepFreeze; health will not be updated.");
            //    DFWrapper.KerbalInfo dfki;
            //    DFWrapper.DeepFreezeAPI.FrozenKerbals.TryGetValue(Name, out dfki);
            //    if (dfki == null) Core.Log("However, kerbal " + Name + " couldn't be retrieved from FrozenKerbals.");
            //    else Core.Log(Name + "'s rosters status: " + dfki.status + "; type: " + dfki.type);
            //    return;
            //}
            HP += HealthChangePerDay() / 21600 * interval;
            if (HP <= Core.DeathHealth * MaxHP)
            {
                Core.Log(Name + " dies due to having " + HP + " health.", Core.LogLevel.Important);
                if (PCM.seat != null) PCM.seat.part.RemoveCrewmember(PCM);
                PCM.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                if (Core.UseMessageSystem) KSP.UI.Screens.MessageSystem.Instance.AddMessage(new KSP.UI.Screens.MessageSystem.Message("Kerbal Health", Name + " dies of poor health!", KSP.UI.Screens.MessageSystemButton.MessageButtonColor.RED, KSP.UI.Screens.MessageSystemButton.ButtonIcons.ALERT));
                else ScreenMessages.PostScreenMessage(Name + " dies of poor health!");
            }
            if (Condition == HealthCondition.OK && HP <= Core.ExhaustionStartHealth * MaxHP)
            {
                Condition = HealthCondition.Exhausted;
                if (Core.UseMessageSystem) KSP.UI.Screens.MessageSystem.Instance.AddMessage(new KSP.UI.Screens.MessageSystem.Message("Kerbal Health", Name + " is exhausted!", KSP.UI.Screens.MessageSystemButton.MessageButtonColor.RED, KSP.UI.Screens.MessageSystemButton.ButtonIcons.ALERT));
                else ScreenMessages.PostScreenMessage(Name + " is exhausted!");
            }
            if (Condition == HealthCondition.Exhausted && HP >= Core.ExhaustionEndHealth * MaxHP)
            {
                Condition = HealthCondition.OK;
                if (Core.UseMessageSystem) KSP.UI.Screens.MessageSystem.Instance.AddMessage(new KSP.UI.Screens.MessageSystem.Message("Kerbal Health", Name + " has revived!", KSP.UI.Screens.MessageSystemButton.MessageButtonColor.RED, KSP.UI.Screens.MessageSystemButton.ButtonIcons.ALERT));
                else ScreenMessages.PostScreenMessage(Name + " has revived.");
            }
        }

        public ConfigNode ConfigNode
        {
            get
            {
                ConfigNode n = new ConfigNode("KerbalHealthStatus");
                n.AddValue("name", Name);
                n.AddValue("health", HP);
                n.AddValue("condition", Condition);
                if (Condition == HealthCondition.Exhausted) n.AddValue("trait", Trait);
                if (LastChange != 0) n.AddValue("lastChange", LastChange);
                if (LastMarginalPositiveChange != 0) n.AddValue("lastMarginalPositiveChange", LastMarginalPositiveChange);
                if (LastMarginalNegativeChange != 0) n.AddValue("lastMarginalNegativeChange", LastMarginalNegativeChange);
                if (IsOnEVA) n.AddValue("onEva", true);
                return n;
            }
            set
            {
                Name = value.GetValue("name");
                HP = Double.Parse(value.GetValue("health"));
                Condition = (KerbalHealthStatus.HealthCondition)Enum.Parse(typeof(HealthCondition), value.GetValue("condition"));
                if (Condition == HealthCondition.Exhausted) Trait = value.GetValue("trait");
                try { LastChange = double.Parse(value.GetValue("lastChange")); }
                catch (Exception) { LastChange = 0; }
                try { LastMarginalPositiveChange = double.Parse(value.GetValue("lastMarginalPositiveChange")); }
                catch (Exception) { LastMarginalPositiveChange = 0; }
                try { LastMarginalNegativeChange = double.Parse(value.GetValue("lastMarginalNegativeChange")); }
                catch (Exception) { LastMarginalNegativeChange = 0; }
                try { IsOnEVA = bool.Parse(value.GetValue("onEva")); }
                catch (Exception) { IsOnEVA = false; }
            }
        }

        public override bool Equals(object obj)
        { return ((KerbalHealthStatus)obj).Name.Equals(Name); }

        public override int GetHashCode()
        { return base.GetHashCode(); }

        public KerbalHealthStatus(string name)
        {
            Name = name;
            HP = MaxHP;
        }

        public KerbalHealthStatus(string name, double health)
        {
            Name = name;
            HP = health;
        }

        public KerbalHealthStatus(ConfigNode node)
        { ConfigNode = node; }
    }
}
