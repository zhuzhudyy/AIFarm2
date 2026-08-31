namespace AIFarm.Npc
{
    // Stage-11 source compatibility. New multi-resident code uses ResidentRuntimeState.
    public sealed class NpcRuntimeState : ResidentRuntimeState
    {
        public NpcRuntimeState(
            NpcPersonaDefinition persona = null,
            MemoryStore memoryStore = null)
            : base(CreateDefinition(persona, memoryStore), memoryStore)
        {
        }

        public NpcRuntimeState(
            ResidentDefinition definition,
            MemoryStore memoryStore = null)
            : base(definition, memoryStore)
        {
        }

        private static ResidentDefinition CreateDefinition(
            NpcPersonaDefinition persona,
            MemoryStore memoryStore)
        {
            ResidentId residentId = memoryStore == null
                ? ResidentIds.Yaya
                : memoryStore.OwnerResidentId;
            NpcPersonaDefinition resolvedPersona = persona ?? NpcPersonaDefinition.Yaya;
            return residentId == ResidentIds.Yaya && resolvedPersona == NpcPersonaDefinition.Yaya
                ? ResidentDefinition.Yaya
                : new ResidentDefinition(residentId, resolvedPersona.Name, resolvedPersona);
        }
    }
}
