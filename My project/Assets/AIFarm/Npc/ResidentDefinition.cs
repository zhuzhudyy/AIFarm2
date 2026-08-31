using System;

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

        public ResidentId ResidentId { get; }

        public string DisplayName { get; }

        public NpcPersonaDefinition Persona { get; }
    }
}
