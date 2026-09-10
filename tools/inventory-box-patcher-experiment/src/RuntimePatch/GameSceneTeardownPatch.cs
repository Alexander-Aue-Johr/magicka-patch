using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSceneTeardownPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo contentField;
        private static FieldInfo levelField;
        private static FieldInfo modelField;
        private static FieldInfo liquidsField;
        private static FieldInfo rulesetField;
        private static FieldInfo globalSoundsField;
        private static FieldInfo soundsField;
        private static FieldInfo triggersField;
        private static FieldInfo killPlaneField;
        private static FieldInfo swayTargetField;
        private static FieldInfo swayEffectField;
        private static FieldInfo swayDeclarationField;
        private static FieldInfo swayVerticesField;
        private static FieldInfo callbackField;
        private static FieldInfo triggerActionsField;
        private static FieldInfo triggerConditionsField;
        private static FieldInfo triggerSceneField;
        private static FieldInfo triggerIdField;
        private static PropertyInfo skinTagProperty;
        private static PropertyInfo skinCollisionSystemProperty;
        private static PropertyInfo collisionSkinsProperty;
        private static MethodInfo removeCollisionSkinMethod;
        private static MethodInfo rulesetDeinitializeMethod;
        private static MethodInfo globalAudioStopMethod;
        private static MethodInfo locatorStopMethod;
        private static object immediateAudioStop;
        private static FieldInfo[] collectionFields;
        private static FieldInfo[] referenceFields;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "GameScene complete teardown",
                "org.magickacommunitypatch.game-scene-teardown",
                FindDispose,
                target => typeof(GameSceneTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindDispose(Assembly assembly)
        {
            Type gameScene = assembly.GetType("Magicka.Levels.GameScene", true);
            Type trigger = assembly.GetType("Magicka.Levels.Triggers.Trigger", true);
            contentField = RequireField(gameScene, "mContent");
            levelField = RequireField(gameScene, "mLevel");
            modelField = RequireField(gameScene, "mModel");
            liquidsField = RequireField(gameScene, "mLiquids");
            rulesetField = RequireField(gameScene, "mRuleset");
            globalSoundsField = RequireField(gameScene, "mGlobalSounds");
            soundsField = RequireField(gameScene, "mSounds");
            triggersField = RequireField(gameScene, "mTriggers");
            killPlaneField = RequireField(gameScene, "mKillPlane");
            swayTargetField = RequireField(gameScene, "mSwayTarget");
            swayEffectField = RequireField(gameScene, "mSwayEffect");
            swayDeclarationField = RequireField(gameScene, "mSwayVertexDeclaration");
            swayVerticesField = RequireField(gameScene, "mSwayVertices");

            callbackField = RequireField(killPlaneField.FieldType, "callbackFn");
            skinTagProperty = RequireReadableWritableProperty(
                killPlaneField.FieldType,
                "Tag");
            skinCollisionSystemProperty = RequireReadableProperty(
                killPlaneField.FieldType,
                "CollisionSystem");
            collisionSkinsProperty = RequireReadableProperty(
                skinCollisionSystemProperty.PropertyType,
                "CollisionSkins");
            removeCollisionSkinMethod = RequireMethod(
                skinCollisionSystemProperty.PropertyType,
                "RemoveCollisionSkin",
                new Type[] { killPlaneField.FieldType },
                typeof(bool));

            rulesetDeinitializeMethod = RequireMethod(
                rulesetField.FieldType,
                "DeInitialize",
                Type.EmptyTypes,
                typeof(void));
            globalAudioStopMethod = FindStopMethod(
                DictionaryValueType(globalSoundsField));
            locatorStopMethod = FindStopMethod(
                DictionaryValueType(soundsField));
            if (globalAudioStopMethod.GetParameters()[0].ParameterType !=
                locatorStopMethod.GetParameters()[0].ParameterType)
                throw new InvalidOperationException(
                    "GameScene audio stop option types do not match.");
            immediateAudioStop = Enum.Parse(
                globalAudioStopMethod.GetParameters()[0].ParameterType,
                "Immediate");

            triggerActionsField = RequireField(trigger, "mActions");
            triggerConditionsField = RequireField(trigger, "mConditions");
            triggerSceneField = RequireField(trigger, "mGameScene");
            triggerIdField = RequireField(trigger, "mIDString");

            collectionFields = OptionalFields(gameScene, new string[]
            {
                "mWorldSyncTriggeredActions",
                "mTriggeredActions",
                "mStartupActions",
                "mSavedEntities",
                "mSavedAnimations",
                "mEffects"
            });
            referenceFields = OptionalFields(gameScene, new string[]
            {
                "mCloudTexture",
                "mSkyMap",
                "mSwayTexture",
                "mCharacterDisplacementTexture",
                "mDirectionalLightSettings",
                "mShaHash",
                "mModelName",
                "mSkyMapFileName",
                "mName"
            });

            MethodInfo dispose = gameScene.GetMethod(
                "Dispose",
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(gameScene.FullName, "Dispose");
            return dispose;
        }

        public static bool Prefix(object __instance)
        {
            if (__instance == null)
                return false;
            lock (__instance)
            {
                ReleaseKillPlane(__instance);
                DeinitializeRuleset(__instance);
                StopAndClear(__instance, globalSoundsField, globalAudioStopMethod);
                StopAndClear(__instance, soundsField, locatorStopMethod);
                DisposeAndClearTriggers(__instance);
                DisposeAndClear(__instance, modelField);
                liquidsField.SetValue(__instance, null);
                DisposeAndClear(__instance, swayTargetField);
                DisposeAndClear(__instance, swayEffectField);
                DisposeAndClear(__instance, swayDeclarationField);
                DisposeAndClear(__instance, swayVerticesField);
                for (int index = 0; index < collectionFields.Length; index++)
                    ClearAndSetNull(__instance, collectionFields[index]);
                for (int index = 0; index < referenceFields.Length; index++)
                    referenceFields[index].SetValue(__instance, null);
                levelField.SetValue(__instance, null);
                DisposeAndClear(__instance, contentField);
            }
            GC.SuppressFinalize(__instance);
            return false;
        }

        private static void ReleaseKillPlane(object scene)
        {
            object skin = killPlaneField.GetValue(scene);
            if (skin == null)
                return;
            Delegate callbacks = callbackField.GetValue(skin) as Delegate;
            if (callbacks != null)
            {
                Delegate[] entries = callbacks.GetInvocationList();
                for (int index = 0; index < entries.Length; index++)
                {
                    if (Object.ReferenceEquals(entries[index].Target, scene))
                        callbacks = Delegate.Remove(callbacks, entries[index]);
                }
                callbackField.SetValue(skin, callbacks);
            }
            try
            {
                object collisionSystem = skinCollisionSystemProperty.GetValue(
                    skin,
                    null);
                if (collisionSystem != null && Contains(
                    collisionSkinsProperty.GetValue(collisionSystem, null),
                    skin))
                    removeCollisionSkinMethod.Invoke(
                        collisionSystem,
                        new object[] { skin });
            }
            catch
            {
            }
            skinTagProperty.SetValue(skin, null, null);
            killPlaneField.SetValue(scene, null);
        }

        private static void DeinitializeRuleset(object scene)
        {
            object ruleset = rulesetField.GetValue(scene);
            if (ruleset != null)
                rulesetDeinitializeMethod.Invoke(ruleset, null);
            rulesetField.SetValue(scene, null);
        }

        private static void StopAndClear(
            object scene,
            FieldInfo field,
            MethodInfo stop)
        {
            IDictionary values = field.GetValue(scene) as IDictionary;
            if (values != null)
            {
                foreach (object value in values.Values)
                {
                    if (value != null)
                        stop.Invoke(value, new object[] { immediateAudioStop });
                }
                values.Clear();
            }
            field.SetValue(scene, null);
        }

        private static void DisposeAndClearTriggers(object scene)
        {
            IDictionary triggers = triggersField.GetValue(scene) as IDictionary;
            if (triggers != null)
            {
                foreach (object trigger in triggers.Values)
                    CleanupTrigger(trigger);
                triggers.Clear();
            }
            triggersField.SetValue(scene, null);
        }

        private static void CleanupTrigger(object trigger)
        {
            if (trigger == null)
                return;
            ClearArrays(trigger, triggerActionsField, true);
            ClearArrays(trigger, triggerConditionsField, false);
            triggerSceneField.SetValue(trigger, null);
            triggerIdField.SetValue(trigger, null);
        }

        private static void ClearArrays(
            object trigger,
            FieldInfo field,
            bool actions)
        {
            Array groups = field.GetValue(trigger) as Array;
            if (groups != null)
            {
                for (int group = 0; group < groups.Length; group++)
                {
                    Array values = groups.GetValue(group) as Array;
                    if (values == null)
                        continue;
                    for (int index = 0; index < values.Length; index++)
                    {
                        object value = values.GetValue(index);
                        if (actions)
                            ActionLifecyclePatch.Cleanup(value);
                        values.SetValue(null, index);
                    }
                    groups.SetValue(null, group);
                }
            }
            field.SetValue(trigger, null);
        }

        private static void DisposeAndClear(object owner, FieldInfo field)
        {
            object value = field.GetValue(owner);
            IDisposable disposable = value as IDisposable;
            if (disposable != null)
                disposable.Dispose();
            field.SetValue(owner, null);
        }

        private static void ClearAndSetNull(object owner, FieldInfo field)
        {
            IDictionary dictionary = field.GetValue(owner) as IDictionary;
            if (dictionary != null)
                dictionary.Clear();
            IList list = field.GetValue(owner) as IList;
            if (list != null)
                list.Clear();
            field.SetValue(owner, null);
        }

        private static MethodInfo FindStopMethod(Type type)
        {
            MethodInfo found = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "Stop" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 1 ||
                    parameters[0].ParameterType.FullName !=
                        "Microsoft.Xna.Framework.Audio.AudioStopOptions")
                    continue;
                if (found != null)
                    throw new AmbiguousMatchException(type.FullName + ".Stop");
                found = methods[index];
            }
            if (found == null)
                throw new MissingMethodException(type.FullName, "Stop");
            return found;
        }

        private static FieldInfo[] OptionalFields(Type type, string[] names)
        {
            List<FieldInfo> result = new List<FieldInfo>();
            for (int index = 0; index < names.Length; index++)
            {
                FieldInfo field = type.GetField(names[index], InstanceMembers);
                if (field != null)
                    result.Add(field);
            }
            return result.ToArray();
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, InstanceMembers);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireReadableProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || !property.CanRead)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static PropertyInfo RequireReadableWritableProperty(
            Type type,
            string name)
        {
            PropertyInfo property = RequireReadableProperty(type, name);
            if (!property.CanWrite)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] arguments,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                arguments,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static Type DictionaryValueType(FieldInfo field)
        {
            Type[] arguments = field.FieldType.GetGenericArguments();
            if (arguments.Length != 2)
                throw new MissingMemberException(
                    field.DeclaringType.FullName,
                    field.Name + " value type");
            return arguments[1];
        }

        private static bool Contains(object values, object expected)
        {
            IEnumerable enumerable = values as IEnumerable;
            if (enumerable == null)
                return false;
            foreach (object value in enumerable)
            {
                if (Object.ReferenceEquals(value, expected))
                    return true;
            }
            return false;
        }
    }
}
