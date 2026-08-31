using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIFarm.Npc
{
    public sealed class ResidentDefinition
    {
        public ResidentDefinition(
            ResidentId residentId,
            string displayName,
            NpcPersonaDefinition persona)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException("A resident definition requires a valid ResidentId.", nameof(residentId));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A resident definition requires a display name.", nameof(displayName));
            }

            ResidentId = residentId;
            DisplayName = displayName.Trim();
            Persona = persona ?? throw new ArgumentNullException(nameof(persona));
        }

        public static ResidentDefinition Yaya { get; } = new ResidentDefinition(
            ResidentIds.Yaya,
            "芽芽",
            NpcPersonaDefinition.Yaya);

        public static ResidentDefinition Amu { get; } = new ResidentDefinition(
            ResidentIds.Amu,
            "阿木",
            NpcPersonaDefinition.Amu);

        public static ResidentDefinition Xiaosui { get; } = new ResidentDefinition(
            ResidentIds.Xiaosui,
            "小穗",
            NpcPersonaDefinition.Xiaosui);

        public static ResidentDefinition Momo { get; } = new ResidentDefinition(
            ResidentIds.Momo,
            "墨墨",
            NpcPersonaDefinition.Momo);

        public static IReadOnlyList<ResidentDefinition> TownResidents { get; } =
            new ReadOnlyCollection<ResidentDefinition>(new[]
            {
                Yaya,
                Amu,
                Xiaosui,
                Momo
            });

        public ResidentId ResidentId { get; }

        public string DisplayName { get; }

        public NpcPersonaDefinition Persona { get; }
    }
}
