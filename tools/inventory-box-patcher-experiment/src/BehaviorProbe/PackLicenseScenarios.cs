using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class PackLicenseScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        PackLicenseHarness harness =
            new PackLicenseHarness(magicka, runtimePatchEnabled);
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
        report.Add("pack_license.draw_custom_offline",
            harness.DrawAllows("Custom", PackNetworkMode.Offline, true));
        report.Add("pack_license.draw_custom_insecure",
            harness.DrawAllows("Custom", PackNetworkMode.Insecure, true));
        report.Add("pack_license.draw_custom_secure",
            harness.DrawAllows("Custom", PackNetworkMode.Secure, false));
        report.Add("pack_license.draw_yes",
            harness.DrawAllows("Yes", PackNetworkMode.Offline, true));
        report.Add("pack_license.draw_no",
            harness.DrawAllows("No", PackNetworkMode.Offline, false));
        report.Add("pack_license.draw_callsites",
            harness.DrawCallSites());
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
    private readonly MethodInfo manualAllowsLicense;
    private readonly MethodInfo runtimeAllowsLicense;
    private readonly bool drawCallSitesInstalled;

    internal PackLicenseHarness(Assembly magicka, bool runtimePatchEnabled)
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
        manualAllowsLicense = packManagerType.GetMethod(
            "CommunityPatchAllowsPackLicense",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new Type[] { licenseType },
            null);
        if (runtimePatchEnabled)
        {
            runtimeAllowsLicense =
                typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                    .GetType(
                        "Magicka.CommunityPatch.Runtime.PackLicensePatch",
                        true)
                    .GetMethod(
                        "AllowsLicense",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new Type[] { typeof(int) },
                        null);
        }
        MethodInfo drawPacksList = magicka.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                true)
            .GetMethod(
                "DrawPacksList",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(float) },
                null);
        if (drawPacksList == null)
            throw new MissingMethodException(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                "DrawPacksList");
        if (manualAllowsLicense != null)
        {
            drawCallSitesInstalled = true;
        }
        else if (runtimePatchEnabled)
        {
            Patches patches = HarmonyInstance.Create(
                    "org.magickacommunitypatch.behavior-probe-pack-display")
                .GetPatchInfo(drawPacksList);
            if (patches != null)
            {
                foreach (Patch patch in patches.Transpilers)
                {
                    if (patch.owner ==
                        "org.magickacommunitypatch.character-select-pack-display")
                    {
                        drawCallSitesInstalled = true;
                        break;
                    }
                }
            }
        }
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

    internal ScenarioResult DrawAllows(
        string licenseName,
        PackNetworkMode mode,
        bool expected)
    {
        ConfigureNetwork(mode);
        object license = Enum.Parse(licenseType, licenseName);
        bool actual;
        if (manualAllowsLicense != null)
        {
            actual = (bool)manualAllowsLicense.Invoke(
                null,
                new object[] { license });
        }
        else if (runtimeAllowsLicense != null)
        {
            actual = (bool)runtimeAllowsLicense.Invoke(
                null,
                new object[] { Convert.ToInt32(license) });
        }
        else
        {
            actual = licenseName == "Yes";
        }
        return new ScenarioResult(
            actual == expected,
            "allowed:" + actual,
            "allowed:" + expected);
    }

    internal ScenarioResult DrawCallSites()
    {
        return new ScenarioResult(
            drawCallSitesInstalled,
            "installed:" + drawCallSitesInstalled,
            "installed:True");
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
