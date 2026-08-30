namespace AIFarm.Core
{
    public enum ActionFailureReason
    {
        None = 0,
        InvalidArgument,
        InvalidState,
        InsufficientResource,
        MissingGrowthCondition,
        CapacityExceeded
    }
}
