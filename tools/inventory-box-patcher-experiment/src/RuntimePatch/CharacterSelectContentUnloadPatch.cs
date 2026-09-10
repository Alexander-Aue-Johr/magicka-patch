using System;
using System.Reflection;
using System.Threading;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterSelectContentUnloadPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static PropertyInfo gameInstance;
        private static PropertyInfo graphicsDevice;
        private static MethodInfo addLoadTask;
        private static PropertyInfo tomeInstance;
        private static FieldInfo tomeState;
        private static FieldInfo renderData;
        private static FieldInfo menuStack;
        private static FieldInfo menuStackPosition;
        private static FieldInfo renderState;
        private static FieldInfo renderMenu;
        private static Type menuGameState;
        private static PropertyInfo gameStateManagerInstance;
        private static PropertyInfo currentGameState;
        private static Type openState;
        private static Type closedState;
        private static Type closedBackState;
        private static FieldInfo textureContent;
        private static MethodInfo unloadContent;
        private static FieldInfo genericStar;
        private static PropertyInfo genericStarTexture;
        private static FieldInfo[] textureFields;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character-select render-safe content unload",
                "org.magickacommunitypatch.character-select-content-unload",
                FindOnUnload,
                target => typeof(CharacterSelectContentUnloadPatch).GetMethod(
                    "Prefix"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type menu = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                false);
            return menu != null && menu.GetMethod("OnUnload",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null) != null;
        }

        private static MethodInfo FindOnUnload(Assembly assembly)
        {
            Type menu = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                true);
            MethodInfo target = RequireMethod(menu, "OnUnload", Type.EmptyTypes);
            textureContent = RequireField(menu, "mTextureContent");
            unloadContent = RequireMethod(
                textureContent.FieldType, "Unload", Type.EmptyTypes);
            genericStar = RequireField(menu, "mGenericStar");
            genericStarTexture = genericStar.FieldType.GetProperty(
                "Texture", InstanceMembers);
            if (genericStarTexture == null || !genericStarTexture.CanWrite)
                throw new MissingMemberException(
                    genericStar.FieldType.FullName, "Texture");
            string[] names = new string[]
            {
                "mCustomTexture", "mMagicksTexture", "mLevelLockedOverlay",
                "mLevelFreeOverlay", "mLevelFreeAndLockedOverlay",
                "mLevelNewOverlay", "mRobeLockedOverlay", "mRobeUnusedOverlay",
                "mRobeFreeOverlay", "mRobeFreeAndLockedOverlay",
                "mRobeFreeAndUnusedOverlay", "mRobeNewOverlay", "mStarTexture",
                "mLevelUnusedOverlay", "mCustomLevelOverlay",
                "mLevelFreeAndUnusedOverlay"
            };
            textureFields = new FieldInfo[names.Length];
            for (int index = 0; index < names.Length; index++)
                textureFields[index] = RequireField(menu, names[index]);

            Type game = assembly.GetType("Magicka.Game", true);
            gameInstance = RequireStaticProperty(game, "Instance");
            graphicsDevice = RequireProperty(game, "GraphicsDevice");
            addLoadTask = RequireMethod(game, "AddLoadTask",
                new Type[] { typeof(Action) });

            Type tome = assembly.GetType("Magicka.GameLogic.UI.Tome", true);
            tomeInstance = RequireStaticProperty(tome, "Instance");
            tomeState = RequireField(tome, "mCurrentState");
            renderData = RequireField(tome, "mRenderData");
            menuStack = RequireField(tome, "mMenuStack");
            menuStackPosition = RequireField(tome, "mMenuStackPosition");
            Type render = tome.GetNestedType("RenderData",
                BindingFlags.NonPublic);
            renderState = RequireField(render, "State");
            renderMenu = RequireField(render, "CurrentMenu");
            openState = RequireNested(tome, "OpenState");
            closedState = RequireNested(tome, "ClosedState");
            closedBackState = RequireNested(tome, "ClosedBack");

            Type manager = assembly.GetType(
                "Magicka.GameLogic.GameStates.GameStateManager", true);
            gameStateManagerInstance = RequireStaticProperty(manager, "Instance");
            currentGameState = RequireProperty(manager, "CurrentState");
            menuGameState = assembly.GetType(
                "Magicka.GameLogic.GameStates.MenuState", true);
            return target;
        }

        public static bool Prefix(object __instance)
        {
            object game = gameInstance.GetValue(null, null);
            addLoadTask.Invoke(game, new object[]
            {
                new Action(delegate { Unload(__instance); })
            });
            return false;
        }

        public static void Unload(object menu)
        {
            while (IsMenuReferenced(menu))
                Thread.Sleep(100);
            object game = gameInstance.GetValue(null, null);
            object device = graphicsDevice.GetValue(game, null);
            lock (device)
            {
                object content = textureContent.GetValue(menu);
                if (content != null)
                    unloadContent.Invoke(content, new object[0]);
                for (int index = 0; index < textureFields.Length; index++)
                    textureFields[index].SetValue(menu, null);
                object star = genericStar.GetValue(menu);
                if (star != null)
                    genericStarTexture.SetValue(star, null, null);
            }
        }

        public static bool IsMenuReferenced(object menu)
        {
            if (menu == null)
                return false;
            object manager = gameStateManagerInstance.GetValue(null, null);
            object gameState = currentGameState.GetValue(manager, null);
            if (!menuGameState.IsInstanceOfType(gameState))
                return false;
            object tome = tomeInstance.GetValue(null, null);
            bool canDrawOld = CanDrawOld(tomeState.GetValue(tome));
            Array channels = (Array)renderData.GetValue(tome);
            for (int index = 0; index < channels.Length; index++)
            {
                object channel = channels.GetValue(index);
                if (channel == null)
                    continue;
                if (Object.ReferenceEquals(renderMenu.GetValue(channel), menu))
                    return true;
                if (CanDrawOld(renderState.GetValue(channel)))
                    canDrawOld = true;
            }
            Array stack = (Array)menuStack.GetValue(tome);
            int position = (int)menuStackPosition.GetValue(tome);
            return IsStackReference(stack, position, menu, canDrawOld);
        }

        public static bool IsStackReference(
            Array stack,
            int position,
            object menu,
            bool canDrawOld)
        {
            if (stack == null || position < 0 || position >= stack.Length)
                return false;
            if (Object.ReferenceEquals(stack.GetValue(position), menu))
                return true;
            if (!canDrawOld)
                return false;
            return position > 0 &&
                    Object.ReferenceEquals(stack.GetValue(position - 1), menu) ||
                position + 1 < stack.Length &&
                    Object.ReferenceEquals(stack.GetValue(position + 1), menu);
        }

        private static bool CanDrawOld(object state)
        {
            return state != null && !openState.IsInstanceOfType(state) &&
                !closedState.IsInstanceOfType(state) &&
                !closedBackState.IsInstanceOfType(state);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name,
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static Type RequireNested(Type type, string name)
        {
            Type nested = type.GetNestedType(name,
                BindingFlags.Public | BindingFlags.NonPublic);
            if (nested == null)
                throw new TypeLoadException(type.FullName + "+" + name);
            return nested;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, InstanceMembers,
                null, parameters, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static PropertyInfo RequireStaticProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name,
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || !property.CanRead)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, InstanceMembers);
            if (property == null || !property.CanRead)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
