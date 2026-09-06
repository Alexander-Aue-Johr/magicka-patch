using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PackLicensePatch
    {
        private static int yesLicense;
        private static int customLicense;
        private static int offlineState;
        private static PropertyInfo networkManagerInstance;
        private static PropertyInfo networkState;
        private static PropertyInfo networkInterface;
        private static PropertyInfo vacSecure;

        internal static readonly RuntimePatchDefinition ItemLicenseDefinition =
            RuntimePatchDefinition.Transpile(
                "ItemPack custom license assignment",
                "org.magickacommunitypatch.item-pack-license",
                assembly => PackLicenseTargetMethods.FindSetterIn(
                    assembly,
                    "Magicka.Levels.Packs.ItemPack",
                    "License"),
                typeof(PackLicensePatch).GetMethod("LicenseSetterTranspiler"));

        internal static readonly RuntimePatchDefinition ItemEnabledDefinition =
            RuntimePatchDefinition.Transpile(
                "ItemPack custom license enable",
                "org.magickacommunitypatch.item-pack-enabled",
                assembly => PackLicenseTargetMethods.FindSetterIn(
                    assembly,
                    "Magicka.Levels.Packs.ItemPack",
                    "Enabled"),
                typeof(PackLicensePatch).GetMethod("EnabledSetterTranspiler"));

        internal static readonly RuntimePatchDefinition MagickLicenseDefinition =
            RuntimePatchDefinition.Transpile(
                "MagickPack custom license assignment",
                "org.magickacommunitypatch.magick-pack-license",
                assembly => PackLicenseTargetMethods.FindSetterIn(
                    assembly,
                    "Magicka.Levels.Packs.MagickPack",
                    "License"),
                typeof(PackLicensePatch).GetMethod("LicenseSetterTranspiler"));

        internal static readonly RuntimePatchDefinition MagickEnabledDefinition =
            RuntimePatchDefinition.Transpile(
                "MagickPack custom license enable",
                "org.magickacommunitypatch.magick-pack-enabled",
                assembly => PackLicenseTargetMethods.FindSetterIn(
                    assembly,
                    "Magicka.Levels.Packs.MagickPack",
                    "Enabled"),
                typeof(PackLicensePatch).GetMethod("EnabledSetterTranspiler"));

        internal static void Configure(Assembly targetAssembly)
        {
            Type itemPack = targetAssembly.GetType("Magicka.Levels.Packs.ItemPack", true);
            Type licenseType = itemPack.GetProperty("License").PropertyType;
            Type managerType = targetAssembly.GetType("Magicka.Network.NetworkManager", true);
            Type stateType = targetAssembly.GetType("Magicka.Network.NetworkState", true);
            Type interfaceType = targetAssembly.GetType("Magicka.Network.NetworkInterface", true);

            yesLicense = Convert.ToInt32(Enum.Parse(licenseType, "Yes"));
            customLicense = Convert.ToInt32(Enum.Parse(licenseType, "Custom"));
            offlineState = Convert.ToInt32(Enum.Parse(stateType, "Offline"));
            networkManagerInstance = managerType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            networkState = managerType.GetProperty(
                "State",
                BindingFlags.Instance | BindingFlags.Public);
            networkInterface = managerType.GetProperty(
                "Interface",
                BindingFlags.Instance | BindingFlags.Public);
            vacSecure = interfaceType.GetProperty(
                "IsVACSecure",
                BindingFlags.Instance | BindingFlags.Public);
            if (networkManagerInstance == null || networkState == null ||
                networkInterface == null || vacSecure == null)
                throw new MissingMemberException(
                    "Pack license network state members are incomplete.");
        }

        public static bool AllowsLicense(int license)
        {
            if (license == yesLicense)
                return true;
            if (license != customLicense)
                return false;

            object manager = networkManagerInstance.GetValue(null, null);
            int state = Convert.ToInt32(networkState.GetValue(manager, null));
            if (state == offlineState)
                return true;
            object network = networkInterface.GetValue(manager, null);
            if (network == null)
                throw new NullReferenceException("Network interface is unavailable.");
            return !(bool)vacSecure.GetValue(network, null);
        }

        public static IEnumerable<CodeInstruction> LicenseSetterTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int replacement = -1;
            for (int index = 1; index < result.Count - 1; index++)
            {
                if (!IsLoadInt(result[index], yesLicense) ||
                    result[index - 1].opcode != OpCodes.Ldarg_1 ||
                    (result[index + 1].opcode != OpCodes.Beq &&
                        result[index + 1].opcode != OpCodes.Beq_S))
                    continue;
                if (replacement >= 0)
                    throw new InvalidOperationException(
                        "Multiple pack License comparisons matched.");
                replacement = index;
            }
            if (replacement < 0)
                throw new InvalidOperationException(
                    "Pack License comparison was not found.");

            result[replacement].opcode = OpCodes.Call;
            result[replacement].operand = typeof(PackLicensePatch).GetMethod("AllowsLicense");
            result[replacement + 1].opcode =
                result[replacement + 1].opcode == OpCodes.Beq_S
                    ? OpCodes.Brtrue_S
                    : OpCodes.Brtrue;
            return result;
        }

        public static IEnumerable<CodeInstruction> EnabledSetterTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int replacement = -1;
            for (int index = 0; index < result.Count - 2; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode != OpCodes.Ldfld ||
                    field == null || field.Name != "mLicense" ||
                    !IsLoadInt(result[index + 1], yesLicense) ||
                    result[index + 2].opcode != OpCodes.Ceq)
                    continue;
                if (replacement >= 0)
                    throw new InvalidOperationException(
                        "Multiple pack Enabled license comparisons matched.");
                replacement = index + 1;
            }
            if (replacement < 0)
                throw new InvalidOperationException(
                    "Pack Enabled license comparison was not found.");

            result[replacement].opcode = OpCodes.Call;
            result[replacement].operand = typeof(PackLicensePatch).GetMethod("AllowsLicense");
            result[replacement + 1].opcode = OpCodes.Nop;
            result[replacement + 1].operand = null;
            return result;
        }

        private static bool IsLoadInt(CodeInstruction instruction, int value)
        {
            if (value == -1)
                return instruction.opcode == OpCodes.Ldc_I4_M1;
            if (value >= 0 && value <= 8)
                return instruction.opcode == new OpCode[]
                {
                    OpCodes.Ldc_I4_0,
                    OpCodes.Ldc_I4_1,
                    OpCodes.Ldc_I4_2,
                    OpCodes.Ldc_I4_3,
                    OpCodes.Ldc_I4_4,
                    OpCodes.Ldc_I4_5,
                    OpCodes.Ldc_I4_6,
                    OpCodes.Ldc_I4_7,
                    OpCodes.Ldc_I4_8
                }[value];
            if (instruction.opcode == OpCodes.Ldc_I4)
                return Convert.ToInt32(instruction.operand) == value;
            if (instruction.opcode == OpCodes.Ldc_I4_S)
                return Convert.ToInt32(instruction.operand) == value;
            return false;
        }
    }
}
