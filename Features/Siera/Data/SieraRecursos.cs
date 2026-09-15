namespace PlaywrightTests.Features.Siera.Data;

/// <summary>
/// Recursos da Siera. O nome é o mesmo na fachada da CCO.Api (<c>v1/Siera/{recurso}</c>) e no serviço de
/// integrações (<c>api/siera/{recurso}</c>), que a fachada repassa.
/// </summary>
public static class SieraRecursos
{
    public const string Fachada = "v1/Siera";

    public const string QueryUserVehicle = "queryUserVehicle";

    public const string DeviceStatus = "device-status";

    public const string DeviceOlStatus = "device-ol-status";

    public const string VehicleStatus = "vehicle-status";

    public const string VehicleAlarm = "vehicle-alarm";

    public const string AlarmDetail = "alarm-detail";

    public const string AlarmEvidence = "alarm-evidence";

    public const string VideoPlayer = "video/player";

    /// <summary>Tudo o que a tela /siera consome (SieraService.ts).</summary>
    public static readonly string[] DaTela =
    [
        QueryUserVehicle, DeviceStatus, DeviceOlStatus, VehicleStatus, VehicleAlarm, AlarmDetail, AlarmEvidence, VideoPlayer,
    ];

    public static string NaFachada(string recurso) => $"{Fachada}/{recurso}";
}
