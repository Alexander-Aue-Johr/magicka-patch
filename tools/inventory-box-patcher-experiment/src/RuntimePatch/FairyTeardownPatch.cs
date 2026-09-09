using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class FairyTeardownPatch
    {
        private static Type fairyType;
        private static Type avatarType;
        private static Type npcType;
        private static FieldInfo initializedField;
        private static FieldInfo entityManagerField;
        private static FieldInfo entitiesField;
        private static FieldInfo avatarFairyField;
        private static FieldInfo npcFairyField;
        private static FieldInfo ownerField;
        private static FieldInfo activeField;
        private static FieldInfo effectField;
        private static FieldInfo crashEffectField;
        private static FieldInfo greetingField;
        private static FieldInfo tipField;
        private static FieldInfo dialogIdsField;
        private static MethodInfo effectManagerInstanceGetter;
        private static MethodInfo stopEffectMethod;
        private static MethodInfo dialogManagerInstanceGetter;
        private static MethodInfo endDialogMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Fairy level teardown",
                "org.magickacommunitypatch.fairy-level-teardown",
                FindPlayStateDispose,
                target => typeof(FairyTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindPlayStateDispose(Assembly assembly)
        {
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type entityManager = assembly.GetType(
                "Magicka.GameLogic.Entities.EntityManager",
                true);
            fairyType = assembly.GetType(
                "Magicka.GameLogic.Entities.Fairy",
                true);
            avatarType = assembly.GetType(
                "Magicka.GameLogic.Entities.Avatar",
                true);
            npcType = assembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            Type effectManager = assembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            Type dialogManager = assembly.GetType(
                "Magicka.GameLogic.UI.DialogManager",
                true);

            initializedField = RequireField(playState, "mInitialized");
            entityManagerField = RequireField(playState, "mEntityManager");
            entitiesField = RequireField(entityManager, "mEntities");
            avatarFairyField = RequireField(avatarType, "mFairy");
            npcFairyField = RequireField(npcType, "mFairy");
            ownerField = RequireField(fairyType, "mOwner");
            activeField = RequireField(fairyType, "mActive");
            effectField = RequireField(fairyType, "mEffectRef");
            crashEffectField = RequireField(fairyType, "mCrashEffectRef");
            greetingField = RequireField(fairyType, "mLastDialogGreeting");
            tipField = RequireField(fairyType, "mLastDialogTip");
            dialogIdsField = fairyType.GetField(
                "DIALOG_ID",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            effectManagerInstanceGetter = RequireInstanceGetter(effectManager);
            dialogManagerInstanceGetter = RequireInstanceGetter(dialogManager);
            stopEffectMethod = effectManager.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { effectField.FieldType.MakeByRefType() },
                null);
            endDialogMethod = dialogManager.GetMethod(
                "End",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(int) },
                null);
            MethodInfo dispose = playState.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);

            if (initializedField.FieldType != typeof(bool) ||
                entityManagerField.FieldType != entityManager ||
                !typeof(IEnumerable).IsAssignableFrom(entitiesField.FieldType) ||
                avatarFairyField.FieldType != fairyType ||
                npcFairyField.FieldType != fairyType ||
                activeField.FieldType != typeof(bool) ||
                effectField.FieldType != crashEffectField.FieldType ||
                greetingField.FieldType != typeof(int) ||
                tipField.FieldType != typeof(int) ||
                dialogIdsField == null || dialogIdsField.FieldType != typeof(int[][]) ||
                stopEffectMethod == null || stopEffectMethod.ReturnType != typeof(void) ||
                endDialogMethod == null || endDialogMethod.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "Fairy level teardown members are incomplete.");
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playState.FullName, "Dispose");
            return dispose;
        }

        public static void Prefix(object __instance)
        {
            if (!(bool)initializedField.GetValue(__instance))
                return;
            object manager = entityManagerField.GetValue(__instance);
            if (manager != null)
                CleanupEntities(entitiesField.GetValue(manager));
        }

        public static void CleanupEntities(object source)
        {
            IEnumerable entities = source as IEnumerable;
            if (entities == null)
                return;
            ArrayList fairies = new ArrayList();
            foreach (object entity in entities)
            {
                if (entity == null)
                    continue;
                if (fairyType.IsInstanceOfType(entity))
                    AddUnique(fairies, entity);
                if (avatarType.IsInstanceOfType(entity))
                    AddUnique(fairies, avatarFairyField.GetValue(entity));
                if (npcType.IsInstanceOfType(entity))
                    AddUnique(fairies, npcFairyField.GetValue(entity));
            }
            for (int index = 0; index < fairies.Count; index++)
                CleanupFairy(fairies[index]);
        }

        private static void AddUnique(ArrayList fairies, object fairy)
        {
            if (fairy == null)
                return;
            for (int index = 0; index < fairies.Count; index++)
            {
                if (Object.ReferenceEquals(fairies[index], fairy))
                    return;
            }
            fairies.Add(fairy);
        }

        private static void CleanupFairy(object fairy)
        {
            activeField.SetValue(fairy, false);
            StopEffect(fairy, effectField);
            StopEffect(fairy, crashEffectField);

            int greeting = (int)greetingField.GetValue(fairy);
            int tip = (int)tipField.GetValue(fairy);
            if (greeting >= 0 && tip >= 0)
            {
                try
                {
                    int[][] dialogIds = (int[][])dialogIdsField.GetValue(null);
                    object manager = dialogManagerInstanceGetter.Invoke(null, null);
                    endDialogMethod.Invoke(
                        manager,
                        new object[] { dialogIds[greeting][tip] });
                }
                catch
                {
                }
                greetingField.SetValue(fairy, -1);
                tipField.SetValue(fairy, -1);
            }
            ownerField.SetValue(fairy, null);
        }

        private static void StopEffect(object fairy, FieldInfo field)
        {
            object[] arguments = new object[] { field.GetValue(fairy) };
            try
            {
                object manager = effectManagerInstanceGetter.Invoke(null, null);
                stopEffectMethod.Invoke(manager, arguments);
            }
            catch
            {
            }
            field.SetValue(fairy, arguments[0]);
        }

        private static MethodInfo RequireInstanceGetter(Type type)
        {
            PropertyInfo property = type.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo getter = property == null ? null : property.GetGetMethod();
            if (getter == null || getter.ReturnType != type)
                throw new MissingMethodException(type.FullName, "get_Instance");
            return getter;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
