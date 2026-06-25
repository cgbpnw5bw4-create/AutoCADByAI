namespace DomainSchemas;

public sealed record RuntimeMetadata(
    string RuntimeMode,
    string? RuntimeProvider,
    string? RuntimeModel,
    bool RuntimeFallbackUsed,
    string? RuntimeFallbackReason,
    bool ChiefEngineerRuntimeUsed)
{
    public static RuntimeMetadata Mock =>
        new("Mock", null, null, false, null, false);

    public static RuntimeMetadata MockMicrosoft(string? provider, string? model, string? fallbackReason = null) =>
        new("Microsoft", provider, model, fallbackReason is not null, fallbackReason, true);
}
