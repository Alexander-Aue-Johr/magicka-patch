using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class BlizzardCleanupPatch
    {
        private static FieldInfo ttlField;
        private static FieldInfo sceneField;
        private static FieldInfo casterField;
        private static FieldInfo ambienceField;
        private static MethodInfo stopMethod;
        private static object asAuthored;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Blizzard singleton reference cleanup",
                "org.magickacommunitypatch.blizzard-reference-cleanup",
                FindOnRemove,
                CreatePrefix);

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
