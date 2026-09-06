using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PackLicenseScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        PackLicenseHarness harness = new PackLicenseHarness(magicka);
        report.Add("pack_license.custom_offline_license",
            harness.SetLicense("Custom", PackNetworkMode.Offline, true));
        report.Add("pack_license.custom_offline_enabled",
            harness.SetEnabled("Custom", PackNetworkMode.Offline, true));
        report.Add("pack_license.custom_insecure_license",
            harness.SetLicense("Custom", PackNetworkMode.Insecure, true));
        report.Add("pack_license.custom_insecure_enabled",
            harness.SetEnabled("Custom", PackNetworkMode.Insecure, true));
        report.Add("pack_license.custom_secure_license",
            harness.SetLicense("Custom", PackNetworkMode.Secure, false));
        report.Add("pack_license.custom_secure_enabled",
            harness.SetEnabled("Custom", PackNetworkMode.Secure, false));
        report.Add("pack_license.yes_license",
            harness.SetLicense("Yes", PackNetworkMode.Offline, true));
        report.Add("pack_license.no_license",
            harness.SetLicense("No", PackNetworkMode.Offline, false));
    }
}

internal enum PackNetworkMode
{
    Offline,
    Insecure,
    Secure
}

internal sealed class PackLicenseHarness
{
    private readonly Type itemPackType;
    private readonly Type magickPackType;
    private readonly Type licenseType;
    private readonly Type networkClientType;
    private readonly Type networkManagerType;
    private readonly Type packManagerType;
    private readonly FieldInfo networkManagerSingleton;
    private readonly FieldInfo packManagerSingleton;

    internal PackLicenseHarness(Assembly magicka)
    {
        itemPackType = magicka.GetType("Magicka.Levels.Packs.ItemPack", true);
        magickPackType = magicka.GetType("Magicka.Levels.Packs.MagickPack", true);
        licenseType = itemPackType.GetProperty("License").PropertyType;
        networkClientType = magicka.GetType("Magicka.Network.NetworkClient", true);
        networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        packManagerType = magicka.GetType("Magicka.Levels.Packs.PackMan", true);
        networkManagerSingleton = RuntimeReflection.RequireField(
            networkManagerType,
            "sSingelton");
        packManagerSingleton = RuntimeReflection.RequireField(
            packManagerType,
            "sSingelton");
    }

    internal ScenarioResult SetLicense(
        string licenseName,
        PackNetworkMode mode,
        bool expected)
    {
        ConfigureNetwork(mode);
        object license = Enum.Parse(licenseType, licenseName);
        bool item = SetLicense(itemPackType, license);
        bool magick = SetLicense(magickPackType, license);
        return Result(item, magick, expected);
    }

    internal ScenarioResult SetEnabled(
        string licenseName,
        PackNetworkMode mode,
        bool expected)
    {
        ConfigureNetwork(mode);
        packManagerSingleton.SetValue(null, NewUninitialized(packManagerType));
        object license = Enum.Parse(licenseType, licenseName);
        bool item = SetEnabled(itemPackType, license);
        bool magick = SetEnabled(magickPackType, license);
        return Result(item, magick, expected);
    }

    private void ConfigureNetwork(PackNetworkMode mode)
    {
        object manager = NewUninitialized(networkManagerType);
        if (mode != PackNetworkMode.Offline)
        {
            object client = NewUninitialized(networkClientType);
            RuntimeReflection.WriteField(
                client,
                "mVAC",
                mode == PackNetworkMode.Secure);
            RuntimeReflection.WriteField(manager, "mInterface", client);
        }
        networkManagerSingleton.SetValue(null, manager);
    }

    private static bool SetLicense(Type packType, object license)
    {
        object pack = NewUninitialized(packType);
        RuntimeReflection.WriteField(pack, "mEnabled", true);
        packType.GetProperty("License").SetValue(pack, license, null);
        return (bool)RuntimeReflection.ReadField(pack, "mEnabled");
    }

    private static bool SetEnabled(Type packType, object license)
    {
        object pack = NewUninitialized(packType);
        RuntimeReflection.WriteField(pack, "mLicense", license);
        RuntimeReflection.WriteField(pack, "mEnabled", false);
        packType.GetProperty("Enabled").SetValue(pack, true, null);
        return (bool)RuntimeReflection.ReadField(pack, "mEnabled");
    }

    private static ScenarioResult Result(bool item, bool magick, bool expected)
    {
        bool passed = item == expected && magick == expected;
        return new ScenarioResult(
            passed,
            "item:" + item + ",magick:" + magick,
            "item:" + expected + ",magick:" + expected);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
