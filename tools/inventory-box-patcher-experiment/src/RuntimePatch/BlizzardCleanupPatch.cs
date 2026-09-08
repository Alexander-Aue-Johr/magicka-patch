using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class BlizzardCleanupPatch
    {
        private const string BlizzardTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard";

        private static FieldInfo ttlField;
        private static FieldInfo sceneField;
        private static FieldInfo casterField;
        private static FieldInfo ambienceField;
        private static FieldInfo playStateField;
        private static MethodInfo stopMethod;
        private static MethodInfo recentPlayStateGetter;
        private static object asAuthored;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Blizzard vector play-state release",
                "org.magickacommunitypatch.blizzard-vector-play-state-release",
                FindVectorExecute,
                typeof(BlizzardCleanupPatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Blizzard owner play-state release",
                "org.magickacommunitypatch.blizzard-owner-play-state-release",
                FindOwnerExecute,
                typeof(BlizzardCleanupPatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Blizzard current execute play state",
                "org.magickacommunitypatch.blizzard-current-execute-state",
                FindPrivateExecute,
                typeof(BlizzardCleanupPatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Blizzard current update play state",
                "org.magickacommunitypatch.blizzard-current-update-state",
                FindUpdate,
                typeof(BlizzardCleanupPatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Blizzard singleton reference cleanup",
                "org.magickacommunitypatch.blizzard-reference-cleanup",
                FindOnRemove,
                CreatePrefix);

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, false);
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, true);
        }

        private static MethodInfo FindPublicExecute(
            Assembly targetAssembly,
            bool ownerOverload)
        {
            Type blizzard;
            Type playState;
            ConfigurePlayState(targetAssembly, out blizzard, out playState);
            Type firstParameter = ownerOverload
                ? targetAssembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            MethodInfo execute = blizzard.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playState },
                null);
            if (execute == null || execute.ReturnType != typeof(bool))
                throw new MissingMethodException(blizzard.FullName, "Execute");
            return execute;
        }

        private static MethodInfo FindPrivateExecute(Assembly targetAssembly)
        {
            Type blizzard;
            Type ignoredPlayState;
            ConfigurePlayState(
                targetAssembly,
                out blizzard,
                out ignoredPlayState);
            MethodInfo execute = blizzard.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (execute == null || execute.ReturnType != typeof(bool))
                throw new MissingMethodException(blizzard.FullName, "Execute");
            return execute;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type blizzard;
            Type ignoredPlayState;
            ConfigurePlayState(
                targetAssembly,
                out blizzard,
                out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo update = blizzard.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(blizzard.FullName, "Update");
            return update;
        }

        private static void ConfigurePlayState(
            Assembly targetAssembly,
            out Type blizzard,
            out Type playState)
        {
            blizzard = targetAssembly.GetType(BlizzardTypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = RequireField(
                blizzard,
                "mPlayState",
                playState);
            PropertyInfo recentPlayState = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> ReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigurePlayStateTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    writes++;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple Blizzard play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one Blizzard play-state assignment, found " +
                    writes + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 3, "Execute");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 4, "Update");
        }

        private static IEnumerable<CodeInstruction> ReplacePlayStateReads(
            IEnumerable<CodeInstruction> instructions,
            int expectedReplacements,
            string methodName)
        {
            ConfigurePlayStateTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != expectedReplacements)
                throw new InvalidOperationException(
                    "Expected " + expectedReplacements + " Blizzard " +
                    methodName + " play-state reads, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigurePlayStateTranspiler()
        {
            Type blizzard = RuntimeMember.FindLoadedType(BlizzardTypeName);
            Type ignoredBlizzard;
            Type ignoredPlayState;
            ConfigurePlayState(
                blizzard.Assembly,
                out ignoredBlizzard,
                out ignoredPlayState);
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(BlizzardCleanupPatch).GetMethod("Prefix");
        }

        private static MethodInfo FindOnRemove(Assembly targetAssembly)
        {
            Type blizzard = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard",
                true);
            Type scene = targetAssembly.GetType("Magicka.Levels.GameScene", true);
            Type spellCaster = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type cue = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.Cue");
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");

            ttlField = RequireField(blizzard, "mTTL", typeof(float));
            sceneField = RequireField(blizzard, "mScene", scene);
            casterField = RequireField(blizzard, "mCaster", spellCaster);
            ambienceField = RequireField(blizzard, "mAmbience", cue);
            stopMethod = cue.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { stopOptions },
                null);
            if (stopMethod == null || stopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(cue.FullName, "Stop");
            asAuthored = Enum.Parse(stopOptions, "AsAuthored");

            MethodInfo onRemove = blizzard.GetMethod(
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (onRemove == null || onRemove.ReturnType != typeof(void))
                throw new MissingMethodException(blizzard.FullName, "OnRemove");
            return onRemove;
        }

        public static bool Prefix(object __instance)
        {
            if (ttlField == null || sceneField == null || casterField == null ||
                ambienceField == null || stopMethod == null || asAuthored == null)
                throw new InvalidOperationException(
                    "Blizzard cleanup contract has not been initialized.");

            ttlField.SetValue(__instance, 0f);
            object ambience = ambienceField.GetValue(__instance);
            sceneField.SetValue(__instance, null);
            casterField.SetValue(__instance, null);
            ambienceField.SetValue(__instance, null);
            if (ambience != null)
                InvokeStop(ambience);
            return false;
        }

        private static void InvokeStop(object ambience)
        {
            try
            {
                stopMethod.Invoke(ambience, new object[] { asAuthored });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
