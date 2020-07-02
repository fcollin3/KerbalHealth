﻿using System;
using System.Collections.Generic;

namespace KerbalHealth
{
    /// <summary>
    /// Defines how the base chance of a condition or an outcome changes
    /// </summary>
    public class ChanceModifier
    {
        public enum OperationType { Multiply, Add, Power };

        /// <summary>
        /// Type of modification: multiply the base chance, add to it or find power of it
        /// </summary>
        public OperationType Modification { get; set; } = OperationType.Multiply;

        /// <summary>
        /// Constant value by which the base chance is modified
        /// </summary>
        public double Value { get; set; } = 1;

        public string UseAttribute { get; set; } = null;

        /// <summary>
        /// Logic that defines if the modifier is applied
        /// </summary>
        public Logic Logic { get; set; } = new Logic();

        /// <summary>
        /// Returns the chance for pcm modified according to this modifier's rules
        /// </summary>
        /// <param name="baseValue"></param>
        /// <param name="pcm"></param>
        /// <returns></returns>
        public double Calculate(double baseValue, ProtoCrewMember pcm)
        {
            if (!Logic.Test(pcm))
                return baseValue;
            double v = Value;

            if (UseAttribute != null)
                switch (UseAttribute.ToLower())
                {
                    case "courage":
                        v *= pcm.courage;
                        break;
                    case "stupidity":
                        v *= pcm.stupidity;
                        break;
                }

            switch (Modification)
            {
                case OperationType.Multiply:
                    v *= baseValue;
                    break;
                case OperationType.Add:
                    v += baseValue;
                    break;
                case OperationType.Power:
                    v = Math.Pow(baseValue, v);
                    break;
            }

            return v;
        }

        /// <summary>
        /// Applies all modifiers in the list to baseValue chance for pcm and returns resulting chance
        /// </summary>
        /// <param name="modifiers"></param>
        /// <param name="baseValue"></param>
        /// <param name="pcm"></param>
        /// <returns></returns>
        public static double Calculate(List<ChanceModifier> modifiers, double baseValue, ProtoCrewMember pcm)
        {
            double v = baseValue;
            foreach (ChanceModifier m in modifiers)
                v = m.Calculate(v, pcm);
            Core.Log("Base chance: " + baseValue + "; modified chance: " + v);
            return v;
        }

        public ConfigNode ConfigNode
        {
            set
            {
                if (value.HasValue("modification"))
                    Modification = (OperationType)Enum.Parse(typeof(OperationType), value.GetValue("modification"), true);
                Value = value.GetDouble("value", Modification == OperationType.Add ? 0 : 1);
                UseAttribute = value.GetString("useAttribute");
                Logic.ConfigNode = value;
            }
        }

        public override string ToString()
        {
            string res = "";
            switch (Modification)
            {
                case OperationType.Multiply:
                    res = "Multiply base chance by ";
                    break;
                case OperationType.Add:
                    res = "Increase base chance by ";
                    break;
                case OperationType.Power:
                    res = "Base chance's power of ";
                    break;
            }
            res += Value + "\r\nLogic: " + Logic;
            return res;
        }

        public ChanceModifier(ConfigNode node) => ConfigNode = node;
    }
}
