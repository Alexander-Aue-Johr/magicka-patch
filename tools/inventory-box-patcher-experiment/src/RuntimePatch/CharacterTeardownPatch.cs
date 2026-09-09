using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterTeardownPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static Type characterType;
        private static FieldInfo instancesField;
        private static FieldInfo currentSpellField;
        private static FieldInfo statusEffectCuesField;
        private static FieldInfo chargeCueField;
        private static FieldInfo attachedSoundCuesField;
        private static FieldInfo attachedEffectsField;
        private static FieldInfo aurasField;
        private static FieldInfo buffEffectsField;
        private static FieldInfo spellLightField;
        private static FieldInfo statusEffectLightField;
        private static FieldInfo pointLightField;
        private static FieldInfo pointLightHolderField;
        private static FieldInfo pointLightEnabledField;
        private static FieldInfo animationControllerField;
        private static EventInfo animationLoopedEvent;
        private static EventInfo crossfadeFinishedEvent;
        private static MethodInfo animationLoopedHandler;
        private static MethodInfo crossfadeFinishedHandler;
        private static FieldInfo equipmentField;
        private static FieldInfo attachmentItemField;
        private static FieldInfo grippedCharacterField;
        private static FieldInfo gripperField;
        private static FieldInfo currentSummonsField;
        private static FieldInfo currentSummonCountField;
        private static FieldInfo undeadSummonCountField;
        private static FieldInfo flamerSummonCountField;
        private static FieldInfo[] referenceFields;
        private static FieldInfo[] collectionFields;
        private static FieldInfo[] nullFields;
        private static FieldInfo auraEffectField;
        private static FieldInfo effectHashField;
        private static MethodInfo effectManagerInstanceGetter;
        private static MethodInfo stopEffectMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character final teardown",
                "org.magickacommunitypatch.character-final-teardown",
                FindClearHandles,
                target => typeof(CharacterTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            Type entityType = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            characterType = assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type attachmentType = assembly.GetType(
                "Magicka.GameLogic.Entities.Items.Attachment",
                true);
            Type effectManagerType = assembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);

            instancesField = RequireField(entityType, "mInstances");
            currentSpellField = RequireField(characterType, "mCurrentSpell");
            statusEffectCuesField = RequireField(
                characterType,
                "mStatusEffectCues");
            chargeCueField = RequireField(characterType, "ChargeCue");
            attachedSoundCuesField = RequireField(
                characterType,
                "mAttachedSoundCues");
            attachedEffectsField = RequireField(
                characterType,
                "mAttachedEffects");
            aurasField = RequireField(characterType, "mAuras");
            buffEffectsField = RequireField(characterType, "mBuffEffects");
            spellLightField = RequireField(characterType, "mSpellLight");
            statusEffectLightField = RequireField(
                characterType,
                "mStatusEffectLight");
            pointLightField = RequireField(characterType, "mPointLight");
            pointLightHolderField = RequireField(
                characterType,
                "mPointLightHolder");
            pointLightEnabledField = RequireField(
                pointLightHolderField.FieldType,
                "Enabled");
            animationControllerField = RequireField(
                characterType,
                "mAnimationController");
            animationLoopedEvent = RequireEvent(
                animationControllerField.FieldType,
                "AnimationLooped");
            crossfadeFinishedEvent = RequireEvent(
                animationControllerField.FieldType,
                "CrossfadeFinished");
            animationLoopedHandler = RequireMethod(
                characterType,
                "OnAnimationLooped");
            crossfadeFinishedHandler = RequireMethod(
                characterType,
                "OnCrossfadeFinished");
            equipmentField = RequireField(characterType, "mEquipment");
            attachmentItemField = RequireField(attachmentType, "mItem");
            grippedCharacterField = RequireField(
                characterType,
                "mGrippedCharacter");
            gripperField = RequireField(characterType, "mGripper");
            currentSummonsField = RequireField(
                characterType,
                "mCurrentSummons");
            currentSummonCountField = RequireField(
                characterType,
                "mNumCurrentSummons");
            undeadSummonCountField = RequireField(
                characterType,
                "mNumCurrentUndeadSummons");
            flamerSummonCountField = RequireField(
                characterType,
                "mNumCurrentFlamerSummons");

            referenceFields = RequireFields(
                characterType,
                new string[]
                {
                    "mFearedBy",
                    "mBloatKiller",
                    "mLastAccumulationDamager",
                    "mLastAttacker",
                    "mCharmOwner"
                });
            collectionFields = RequireFields(
                characterType,
                new string[]
                {
                    "mSpellQueue",
                    "mExecutedActions",
                    "mDeadActions",
                    "mHitList",
                    "mBuffs",
                    "mBuffDecals",
                    "mGibs"
                });
            nullFields = ExistingFields(
                characterType,
                new string[]
                {
                    "mTemplate",
                    "mModel",
                    "mRenderData",
                    "mNormalDistortionRenderData",
                    "mShieldSkinRenderData",
                    "mFocusedSkinRenderData",
                    "mBarrierSkinRenderData",
                    "mArmourRenderData",
                    "mLightningZapRenderData",
                    "mHighlightRenderData",
                    "mAnimationClips",
                    "mCurrentActions",
                    "mMoveAnimations",
                    "mEntaglement",
                    "mPreviousState",
                    "mCurrentState",
                    "mEventConditions",
                    "mResistances",
                    "mSpecialAbility",
                    "mAttachedEffects",
                    "mAttachedSounds"
                });

            Type auraType = aurasField.FieldType.GetGenericArguments()[0];
            auraEffectField = RequireField(auraType, "mEffect");
            Type effectType = attachedEffectsField.FieldType.GetElementType();
            effectHashField = RequireField(effectType, "Hash");
            PropertyInfo effectManagerInstance = effectManagerType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            effectManagerInstanceGetter = effectManagerInstance == null
                ? null
                : effectManagerInstance.GetGetMethod();
            stopEffectMethod = effectManagerType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { effectType.MakeByRefType() },
                null);

            MethodInfo clearHandles = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (!typeof(IList).IsAssignableFrom(instancesField.FieldType) ||
                pointLightEnabledField.FieldType != typeof(bool) ||
                currentSummonCountField.FieldType != typeof(int) ||
                undeadSummonCountField.FieldType != typeof(int) ||
                flamerSummonCountField.FieldType != typeof(int) ||
                effectManagerInstanceGetter == null ||
                effectManagerInstanceGetter.ReturnType != effectManagerType ||
                stopEffectMethod == null ||
                stopEffectMethod.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "Character teardown contract is incomplete.");
            if (clearHandles == null || clearHandles.ReturnType != typeof(void))
                throw new MissingMethodException(
                    entityType.FullName,
                    "ClearHandles");
            return clearHandles;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static FieldInfo[] RequireFields(Type type, string[] names)
        {
            FieldInfo[] fields = new FieldInfo[names.Length];
            for (int index = 0; index < names.Length; index++)
                fields[index] = RequireField(type, names[index]);
            return fields;
        }

        private static FieldInfo[] ExistingFields(Type type, string[] names)
        {
            ArrayList fields = new ArrayList();
            for (int index = 0; index < names.Length; index++)
            {
                FieldInfo field = type.GetField(
                    names[index],
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    fields.Add(field);
            }
            return (FieldInfo[])fields.ToArray(typeof(FieldInfo));
        }

        private static EventInfo RequireEvent(Type type, string name)
        {
            EventInfo eventInfo = type.GetEvent(name, InstanceMembers);
            if (eventInfo == null)
                throw new MissingMemberException(type.FullName, name);
            return eventInfo;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type.GetMethod(
                name,
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static void Prefix()
        {
            IList instances = instancesField.GetValue(null) as IList;
            if (instances == null)
                return;
            for (int index = 0; index < instances.Count; index++)
            {
                object entity = instances[index];
                if (entity != null && characterType.IsInstanceOfType(entity))
                    CleanupFinal(entity);
            }
        }

        public static void CleanupFinal(object character)
        {
            if (character == null || !characterType.IsInstanceOfType(character))
                return;

            object spell = currentSpellField.GetValue(character);
            if (spell != null)
                TryDeinitializeSpell(spell, character);
            currentSpellField.SetValue(character, null);

            StopCueArray(statusEffectCuesField.GetValue(character) as Array);
            object chargeCue = chargeCueField.GetValue(character);
            TryStopCue(chargeCue);
            chargeCueField.SetValue(character, null);
            StopCueArray(attachedSoundCuesField.GetValue(character) as Array);

            StopAttachedEffects(character);
            StopAuraEffects(character);
            StopBuffEffects(character);
            StopLight(character, spellLightField);
            StopLight(character, statusEffectLightField);
            StopLight(character, pointLightField);
            DisablePointLightHolder(character);
            ReleaseAnimationController(character);
            ReleaseAttachments(character);
            ReleaseGrip(character);

            for (int index = 0; index < referenceFields.Length; index++)
                referenceFields[index].SetValue(character, null);

            Array summons = currentSummonsField.GetValue(character) as Array;
            if (summons != null)
                Array.Clear(summons, 0, summons.Length);
            currentSummonCountField.SetValue(character, 0);
            undeadSummonCountField.SetValue(character, 0);
            flamerSummonCountField.SetValue(character, 0);

            for (int index = 0; index < collectionFields.Length; index++)
                TryClear(collectionFields[index].GetValue(character));
            for (int index = 0; index < nullFields.Length; index++)
                nullFields[index].SetValue(character, null);
        }

        private static void TryDeinitializeSpell(object spell, object character)
        {
            try
            {
                MethodInfo[] methods = spell.GetType().GetMethods(
                    InstanceMembers);
                for (int index = 0; index < methods.Length; index++)
                {
                    MethodInfo method = methods[index];
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name == "DeInitialize" &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType.IsInstanceOfType(character))
                    {
                        method.Invoke(spell, new object[] { character });
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static void StopCueArray(Array cues)
        {
            if (cues == null)
                return;
            for (int index = 0; index < cues.Length; index++)
            {
                TryStopCue(cues.GetValue(index));
                cues.SetValue(null, index);
            }
        }

        private static void TryStopCue(object cue)
        {
            if (cue == null)
                return;
            try
            {
                PropertyInfo disposedProperty = cue.GetType().GetProperty(
                    "IsDisposed",
                    BindingFlags.Instance | BindingFlags.Public);
                if (disposedProperty != null &&
                    (bool)disposedProperty.GetValue(cue, null))
                    return;
                MethodInfo[] methods = cue.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public);
                for (int index = 0; index < methods.Length; index++)
                {
                    MethodInfo method = methods[index];
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name != "Stop" || parameters.Length != 1 ||
                        !parameters[0].ParameterType.IsEnum)
                        continue;
                    object immediate = Enum.Parse(
                        parameters[0].ParameterType,
                        "Immediate");
                    method.Invoke(cue, new object[] { immediate });
                    return;
                }
            }
            catch
            {
            }
        }

        private static void StopAttachedEffects(object character)
        {
            Array effects = attachedEffectsField.GetValue(character) as Array;
            if (effects == null)
                return;
            for (int index = 0; index < effects.Length; index++)
            {
                object effect = effects.GetValue(index);
                if ((int)effectHashField.GetValue(effect) == 0)
                    continue;
                effects.SetValue(TryStopEffect(effect), index);
            }
        }

        private static void StopAuraEffects(object character)
        {
            IList auras = aurasField.GetValue(character) as IList;
            if (auras == null)
                return;
            for (int index = 0; index < auras.Count; index++)
            {
                object aura = auras[index];
                if (aura != null)
                    TryStopEffect(auraEffectField.GetValue(aura));
            }
            auras.Clear();
        }

        private static void StopBuffEffects(object character)
        {
            IList effects = buffEffectsField.GetValue(character) as IList;
            if (effects == null)
                return;
            for (int index = 0; index < effects.Count; index++)
                TryStopEffect(effects[index]);
            effects.Clear();
        }

        private static object TryStopEffect(object effect)
        {
            if (effect == null)
                return null;
            try
            {
                object manager = effectManagerInstanceGetter.Invoke(null, null);
                object[] arguments = new object[] { effect };
                stopEffectMethod.Invoke(manager, arguments);
                return arguments[0];
            }
            catch
            {
                return effect;
            }
        }

        private static void StopLight(object character, FieldInfo field)
        {
            object light = field.GetValue(character);
            if (light == null)
                return;
            try
            {
                MethodInfo stop = light.GetType().GetMethod(
                    "Stop",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(bool) },
                    null);
                if (stop != null)
                    stop.Invoke(light, new object[] { true });
            }
            catch
            {
            }
            field.SetValue(character, null);
        }

        private static void DisablePointLightHolder(object character)
        {
            object holder = pointLightHolderField.GetValue(character);
            if (holder == null)
                return;
            pointLightEnabledField.SetValue(holder, false);
            pointLightHolderField.SetValue(character, holder);
        }

        private static void ReleaseAnimationController(object character)
        {
            object controller = animationControllerField.GetValue(character);
            if (controller != null)
            {
                TryRemoveHandler(
                    animationLoopedEvent,
                    animationLoopedHandler,
                    controller,
                    character);
                TryRemoveHandler(
                    crossfadeFinishedEvent,
                    crossfadeFinishedHandler,
                    controller,
                    character);
            }
            animationControllerField.SetValue(character, null);
        }

        private static void TryRemoveHandler(
            EventInfo eventInfo,
            MethodInfo handler,
            object source,
            object target)
        {
            try
            {
                Delegate callback = Delegate.CreateDelegate(
                    eventInfo.EventHandlerType,
                    target,
                    handler);
                eventInfo.RemoveEventHandler(source, callback);
            }
            catch
            {
            }
        }

        private static void ReleaseAttachments(object character)
        {
            Array equipment = equipmentField.GetValue(character) as Array;
            if (equipment == null)
                return;
            for (int index = 0; index < equipment.Length; index++)
            {
                object attachment = equipment.GetValue(index);
                if (attachment != null)
                    attachmentItemField.SetValue(attachment, null);
            }
        }

        private static void ReleaseGrip(object character)
        {
            object gripped = grippedCharacterField.GetValue(character);
            object gripper = gripperField.GetValue(character);
            grippedCharacterField.SetValue(character, null);
            gripperField.SetValue(character, null);
            if (gripped != null && Object.ReferenceEquals(
                gripperField.GetValue(gripped),
                character))
                gripperField.SetValue(gripped, null);
            if (gripper != null && Object.ReferenceEquals(
                grippedCharacterField.GetValue(gripper),
                character))
                grippedCharacterField.SetValue(gripper, null);
        }

        private static void TryClear(object value)
        {
            if (value == null)
                return;
            try
            {
                IList list = value as IList;
                if (list != null)
                {
                    list.Clear();
                    return;
                }
                MethodInfo clear = value.GetType().GetMethod(
                    "Clear",
                    InstanceMembers,
                    null,
                    Type.EmptyTypes,
                    null);
                if (clear != null)
                    clear.Invoke(value, null);
            }
            catch
            {
            }
        }
    }
}
