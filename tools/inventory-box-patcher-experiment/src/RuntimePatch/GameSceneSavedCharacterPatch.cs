using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSceneSavedCharacterPatch
    {
        private const BindingFlags Members = BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo avatarGetter;
        private static MethodInfo templateGetter;
        private static MethodInfo uniqueIdGetter;
        private static MethodInfo avatarInitialize;
        private static MethodInfo characterBodyGetter;
        private static MethodInfo moveTo;
        private static MethodInfo aiGetter;
        private static MethodInfo aiEnable;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo entityManagerGetter;
        private static MethodInfo addEntity;
        private static FieldInfo scenePlayStateField;
        private static FieldInfo characterTemplateField;
        private static MethodInfo templateIdGetter;
        private static MethodInfo cachedTemplateGetter;
        private static MethodInfo applyTemplate;
        private static FieldInfo[] renderFields;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "GameScene saved avatar initialization",
                "org.magickacommunitypatch.game-scene-saved-avatar",
                FindInitialize,
                typeof(GameSceneSavedCharacterPatch).GetMethod("InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition SavedEntitiesDefinition =
            RuntimePatchDefinition.Transpile(
                "GameScene saved NPC template restore",
                "org.magickacommunitypatch.game-scene-saved-npc",
                FindAddSavedEntities,
                typeof(GameSceneSavedCharacterPatch).GetMethod("SavedEntitiesTranspiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", false);
            return scene != null && FindByName(scene, "Initialize", 3) != null &&
                FindByName(scene, "AddSavedEntities", 0) != null;
        }

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Resolve(assembly);
            return FindByName(assembly.GetType("Magicka.Levels.GameScene", true),
                "Initialize", 3);
        }

        private static MethodInfo FindAddSavedEntities(Assembly assembly)
        {
            Resolve(assembly);
            return FindByName(assembly.GetType("Magicka.Levels.GameScene", true),
                "AddSavedEntities", 0);
        }

        private static void Resolve(Assembly assembly)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            Type player = assembly.GetType("Magicka.GameLogic.Player", true);
            Type avatar = assembly.GetType("Magicka.GameLogic.Entities.Avatar", true);
            Type entity = assembly.GetType("Magicka.GameLogic.Entities.Entity", true);
            Type character = assembly.GetType("Magicka.GameLogic.Entities.Character", true);
            Type npc = assembly.GetType("Magicka.GameLogic.Entities.NonPlayerCharacter", true);
            Type template = assembly.GetType("Magicka.GameLogic.Entities.CharacterTemplate", true);
            Type playState = assembly.GetType("Magicka.GameLogic.GameStates.PlayState", true);
            Type manager = assembly.GetType("Magicka.GameLogic.Entities.EntityManager", true);

            avatarGetter = Getter(player, "Avatar");
            templateGetter = Getter(character, "Template");
            uniqueIdGetter = Getter(entity, "UniqueID");
            characterBodyGetter = Getter(character, "CharacterBody");
            aiGetter = Getter(npc, "AI");
            recentPlayStateGetter = Getter(playState, "RecentPlayState");
            entityManagerGetter = Getter(playState, "EntityManager");
            scenePlayStateField = scene.GetField("mPlayState", Members);
            characterTemplateField = character.GetField("mTemplate", Members);
            templateIdGetter = Getter(template, "ID");
            if (scenePlayStateField == null || characterTemplateField == null)
                throw new MissingFieldException("GameScene/Character lifecycle field");

            MethodInfo[] avatarMethods = avatar.GetMethods(Members);
            avatarInitialize = null;
            for (int i = 0; i < avatarMethods.Length; i++)
            {
                ParameterInfo[] parameters = avatarMethods[i].GetParameters();
                if (avatarMethods[i].Name == "Initialize" && parameters.Length == 3 &&
                    parameters[0].ParameterType == template &&
                    parameters[2].ParameterType == typeof(int))
                    avatarInitialize = avatarMethods[i];
            }
            Type body = characterBodyGetter.ReturnType;
            MethodInfo[] bodyMethods = body.GetMethods(Members);
            moveTo = null;
            for (int i = 0; i < bodyMethods.Length; i++)
                if (bodyMethods[i].Name == "MoveTo" &&
                    bodyMethods[i].GetParameters().Length == 2 &&
                    !bodyMethods[i].GetParameters()[0].ParameterType.IsByRef)
                    moveTo = bodyMethods[i];
            aiEnable = aiGetter.ReturnType.GetMethod("Enable", Members, null,
                Type.EmptyTypes, null);
            addEntity = manager.GetMethod("AddEntity", Members, null,
                new Type[] { entity }, null);
            cachedTemplateGetter = template.GetMethod("GetCachedTemplate", Members,
                null, new Type[] { typeof(int) }, null);
            applyTemplate = character.GetMethod("ApplyTemplate", Members, null,
                new Type[] { template, typeof(int).MakeByRefType() }, null);
            if (avatarInitialize == null || moveTo == null || aiEnable == null ||
                addEntity == null || cachedTemplateGetter == null || applyTemplate == null)
                throw new MissingMethodException("Saved-character lifecycle method");

            string[] names = new string[] { "mRenderData", "mNormalDistortionRenderData",
                "mShieldSkinRenderData", "mFocusedSkinRenderData",
                "mHighlightRenderData", "mLightningZapRenderData" };
            List<FieldInfo> availableRenderFields = new List<FieldInfo>();
            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = character.GetField(names[i], Members);
                if (field != null)
                    availableRenderFields.Add(field);
            }
            if (availableRenderFields.Count == 0)
                throw new MissingFieldException(character.FullName, "render data");
            renderFields = availableRenderFields.ToArray();
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int move = FindNamedCall(result, "MoveTo", 2, false);
            int bodyGetter = PreviousNamedCall(result, move, "get_CharacterBody");
            if (bodyGetter < 2 || move - bodyGetter < 3)
                throw new InvalidOperationException("Saved avatar MoveTo shape changed: move=" +
                    move + ", body=" + bodyGetter + ", getter=" +
                    characterBodyGetter.DeclaringType.FullName + "." +
                    characterBodyGetter.Name + ".");
            CodeInstruction playerLoad = Clone(result[bodyGetter - 2]);
            CodeInstruction positionLoad = Clone(result[move - 2]);
            List<CodeInstruction> added = new List<CodeInstruction>();
            added.Add(Clone(playerLoad));
            added.Add(new CodeInstruction(OpCodes.Callvirt, avatarGetter));
            added.Add(new CodeInstruction(OpCodes.Dup));
            added.Add(new CodeInstruction(OpCodes.Callvirt, templateGetter));
            added.Add(positionLoad);
            added.Add(Clone(playerLoad));
            added.Add(new CodeInstruction(OpCodes.Callvirt, avatarGetter));
            added.Add(new CodeInstruction(OpCodes.Callvirt, uniqueIdGetter));
            added.Add(new CodeInstruction(OpCodes.Callvirt, avatarInitialize));
            added[0].labels.AddRange(result[bodyGetter - 2].labels);
            result[bodyGetter - 2].labels.Clear();
            result.InsertRange(bodyGetter - 2, added);
            return result;
        }

        public static IEnumerable<CodeInstruction> SavedEntitiesTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int ai = FindCall(result, aiEnable);
            int aiLoad = PreviousCall(result, ai, aiGetter);
            if (aiLoad < 1)
                throw new InvalidOperationException("Saved NPC AI shape changed.");
            CodeInstruction npcLoad = Clone(result[aiLoad - 1]);
            npcLoad.labels.AddRange(result[aiLoad - 1].labels);
            result[aiLoad - 1].labels.Clear();
            result.Insert(aiLoad - 1, npcLoad);
            result.Insert(aiLoad, new CodeInstruction(OpCodes.Call,
                typeof(GameSceneSavedCharacterPatch).GetMethod("ReapplyNpcTemplate")));
            result.Insert(aiLoad + 1, new CodeInstruction(OpCodes.Pop));

            int playStateLoads = 0;
            for (int i = 0; i < result.Count; i++)
                if (result[i].opcode == OpCodes.Ldfld &&
                    Object.Equals(result[i].operand, scenePlayStateField))
                {
                    result[i - 1].opcode = OpCodes.Nop;
                    result[i - 1].operand = null;
                    result[i].opcode = OpCodes.Call;
                    result[i].operand = recentPlayStateGetter;
                    playStateLoads++;
                }
            if (playStateLoads != 1 || FindCall(result, entityManagerGetter) < 0 ||
                FindCall(result, addEntity) < 0)
                throw new InvalidOperationException("Saved EntityManager shape changed.");
            return result;
        }

        public static bool ReapplyNpcTemplate(object npc)
        {
            if (npc == null)
                return false;
            object previousTemplate = characterTemplateField.GetValue(npc);
            int type;
            if (previousTemplate != null)
            {
                type = Convert.ToInt32(
                    templateIdGetter.Invoke(previousTemplate, null));
                CharacterTemplateIdentityStore.Remember(npc, type);
            }
            else if (!CharacterTemplateIdentityStore.TryGet(npc, out type))
            {
                return false;
            }
            object template = cachedTemplateGetter.Invoke(null, new object[] { type });
            if (template == null)
                return false;
            object[] arguments = new object[] { template, -1 };
            applyTemplate.Invoke(npc, arguments);
            for (int fieldIndex = 0; fieldIndex < renderFields.Length; fieldIndex++)
            {
                Array renderData = renderFields[fieldIndex].GetValue(npc) as Array;
                if (renderData == null)
                    continue;
                for (int index = 0; index < renderData.Length; index++)
                {
                    object value = renderData.GetValue(index);
                    if (value != null)
                        value.GetType().GetMethod("SetMeshDirty", Members,
                            null, Type.EmptyTypes, null).Invoke(value, null);
                }
            }
            return characterTemplateField.GetValue(npc) != null;
        }

        internal static void InitializeFor(Assembly assembly)
        {
            Resolve(assembly);
        }

        private static MethodInfo FindByName(Type type, string name, int count)
        {
            MethodInfo[] methods = type.GetMethods(Members);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == name && methods[i].GetParameters().Length == count)
                    return methods[i];
            return null;
        }

        private static MethodInfo Getter(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, Members);
            MethodInfo getter = property == null ? null : property.GetGetMethod(true);
            if (getter == null)
                throw new MissingMethodException(type.FullName, "get_" + name);
            return getter;
        }

        private static int FindCall(IList<CodeInstruction> body, MethodInfo method)
        {
            int found = -1;
            for (int i = 0; i < body.Count; i++)
                if ((body[i].opcode == OpCodes.Call || body[i].opcode == OpCodes.Callvirt) &&
                    Object.Equals(body[i].operand, method))
                {
                    if (found >= 0)
                        throw new InvalidOperationException("Expected one call to " + method.Name + ".");
                    found = i;
                }
            return found;
        }

        private static int PreviousCall(IList<CodeInstruction> body, int before,
            MethodInfo method)
        {
            for (int i = before - 1; i >= Math.Max(0, before - 12); i--)
                if ((body[i].opcode == OpCodes.Call || body[i].opcode == OpCodes.Callvirt) &&
                    Object.Equals(body[i].operand, method))
                    return i;
            return -1;
        }

        private static int FindNamedCall(IList<CodeInstruction> body, string name,
            int parameterCount, bool firstParameterByRef)
        {
            int found = -1;
            for (int i = 0; i < body.Count; i++)
            {
                MethodInfo method = body[i].operand as MethodInfo;
                if (method == null || method.Name != name ||
                    method.GetParameters().Length != parameterCount ||
                    method.GetParameters()[0].ParameterType.IsByRef != firstParameterByRef)
                    continue;
                if (found >= 0)
                    throw new InvalidOperationException("Expected one call to " + name + ".");
                found = i;
            }
            return found;
        }

        private static int PreviousNamedCall(IList<CodeInstruction> body,
            int before, string name)
        {
            for (int i = before - 1; i >= Math.Max(0, before - 12); i--)
            {
                MethodInfo method = body[i].operand as MethodInfo;
                if (method != null && method.Name == name)
                    return i;
            }
            return -1;
        }

        private static CodeInstruction Clone(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }
    }
}
