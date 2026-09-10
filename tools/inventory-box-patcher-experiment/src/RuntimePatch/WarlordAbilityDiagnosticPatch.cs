using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class WarlordAbilityDiagnosticPatch
    {
        private static Type meleeType;
        private static PropertyInfo abilitiesProperty;
        private static PropertyInfo templateAbilitiesProperty;
        private static PropertyInfo templateIdProperty;
        private static PropertyInfo templateNameProperty;
        private static MethodInfo templateDisposedMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Warlord primary-ability diagnostic",
                "org.magickacommunitypatch.warlord-ability-diagnostic",
                FindApplyTemplate,
                typeof(WarlordAbilityDiagnosticPatch).GetMethod(
                    "Transpiler"));

        private static MethodInfo FindApplyTemplate(Assembly assembly)
        {
            Type warlord = assembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.WarlordCharacter",
                true);
            Type template = assembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            meleeType = assembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.Melee",
                true);
            abilitiesProperty = RequireProperty(warlord, "Abilities");
            templateAbilitiesProperty = RequireProperty(template, "Abilities");
            templateIdProperty = RequireProperty(template, "ID");
            templateNameProperty = RequireProperty(template, "Name");
            templateDisposedMethod = template.GetMethod(
                "CommunityPatchIsDisposed",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            MethodInfo method = warlord.GetMethod(
                "ApplyTemplate",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { template, typeof(int).MakeByRefType() },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    warlord.FullName,
                    "ApplyTemplate");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int cast = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Isinst ||
                    !Object.Equals(result[index].operand, meleeType))
                    continue;
                if (cast >= 0)
                    throw new InvalidOperationException(
                        "Multiple Warlord Melee casts matched.");
                cast = index;
            }
            if (cast < 0)
                throw new InvalidOperationException(
                    "Warlord Melee cast was not found.");
            result.Insert(cast, new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(cast + 1, new CodeInstruction(OpCodes.Ldarg_1));
            result.Insert(
                cast + 2,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(WarlordAbilityDiagnosticPatch).GetMethod(
                        "Inspect")));
            return result;
        }

        public static void Inspect(object warlord, object template)
        {
            try
            {
                Array abilities = abilitiesProperty.GetValue(
                    warlord,
                    null) as Array;
                object primary = abilities == null || abilities.Length == 0
                    ? null
                    : abilities.GetValue(0);
                if (primary != null && meleeType.IsInstanceOfType(primary))
                    return;

                bool disposed = false;
                if (template != null && templateDisposedMethod != null)
                    disposed = (bool)templateDisposedMethod.Invoke(
                        template,
                        null);
                object templateAbilities = template == null
                    ? null
                    : templateAbilitiesProperty.GetValue(template, null);
                StringBuilder details = new StringBuilder();
                details.Append("template_null=").Append(template == null);
                details.Append(";template_disposed=").Append(disposed);
                details.Append(";template_id=");
                if (template != null)
                    details.Append(templateIdProperty.GetValue(template, null));
                details.Append(";abilities_null=").Append(abilities == null);
                details.Append(";ability_count=").Append(
                    abilities == null ? 0 : abilities.Length);
                details.Append(";primary_null=").Append(primary == null);
                details.Append(";shares_template_abilities=").Append(
                    template != null &&
                    Object.ReferenceEquals(abilities, templateAbilities));
                RuntimePatchTelemetry.SendRuntimeGuard(
                    "magicka_patch_warlord_ability_diagnostic",
                    "warlord_primary_ability_not_melee",
                    "NonPlayerCharacter.Abilities",
                    primary == null ? "null" : primary.GetType().FullName,
                    details.ToString(),
                    template == null
                        ? String.Empty
                        : Convert.ToString(
                            templateNameProperty.GetValue(template, null)));
            }
            catch
            {
            }
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || property.GetGetMethod(true) == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
