using Microsoft.Extensions.Configuration;

namespace puredf;

internal static class FlashArraySettingsHelper
{
    internal static List<FlashArraySettings> ConvertToSettings(IConfigurationSection configSection)
    {
        return configSection.GetChildren().Select(p => new FlashArraySettings()
        {
            Name = p.Key,
            ClientId = p[Constants.AppSettingsFlashArrayClientIdKey],
            ManagementIpFqdn = p[Constants.AppSettingsFlashArrayManagementIpFqdnKey],
            DataIpFqdn = p[Constants.AppSettingsFlashArrayDataIpFqdnKey],
            Issuer = p[Constants.AppSettingsFlashArrayIssuerKey],
            KeyId = p[Constants.AppSettingsFlashArrayKeyIdKey],
            PrivateKeyPath = p[Constants.AppSettingsFlashArrayPrivateKeyPathKey],
            Username = p[Constants.AppSettingsFlashArrayUsernameKey]
        }).ToList();
    }
}