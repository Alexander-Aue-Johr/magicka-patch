using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterSelectOfflineLeavePatch
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static FieldInfo countdownField;
        private static FieldInfo gameModeBoxField;
        private static FieldInfo versusSettingsField;
        private static FieldInfo currentStateField;
        private static FieldInfo currentControllerField;
        private static FieldInfo playerSlotsField;
        private static MethodInfo countActivePlayers;
        private static MethodInfo gamerSelected;
        private static MethodInfo returnToPreviousMenu;
        private static PropertyInfo isOffline;
        private static PropertyInfo gameInstance;
        private static PropertyInfo gamePlayers;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character-select last offline player leave",
                "org.magickacommunitypatch.character-select-offline-leave",
                FindTarget,
                CreatePrefix);

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                true);
            Type controller = assembly.GetType(
                "Magicka.GameLogic.Controls.Controller", true);
            Type network = assembly.GetType("Magicka.Network.NetworkManager", true);
            Type game = assembly.GetType("Magicka.Game", true);
            countdownField = FindField(type, "mLastCountDownNr");
            gameModeBoxField = FindField(type, "mGameModeBox");
            versusSettingsField = FindField(type, "mVersusSettings");
            currentStateField = FindField(type, "mCurrentState");
            currentControllerField = FindField(type, "mCurrentController");
            playerSlotsField = FindField(type, "mPlayerSlots");
            countActivePlayers = FindMethod(type, "CountActivePlayers", 0);
            gamerSelected = FindMethod(type, "GamerSelected", 2);
            returnToPreviousMenu = FindMethod(type, "ReturnToPreviousMenu", 0);
            isOffline = network.GetProperty(
                "IsOffline", BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            gameInstance = game.GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            gamePlayers = game.GetProperty("Players", AnyInstance);
            MethodInfo target = type.GetMethod(
                "ControllerB", AnyInstance, null,
                new Type[] { controller }, null);
            if (target == null || countdownField == null ||
                gameModeBoxField == null || versusSettingsField == null ||
                currentStateField == null || currentControllerField == null ||
                playerSlotsField == null || countActivePlayers == null ||
                gamerSelected == null || returnToPreviousMenu == null ||
                isOffline == null || gameInstance == null || gamePlayers == null)
                throw new MissingMemberException(type.FullName, "offline leave contract");
            return target;
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(CharacterSelectOfflineLeavePatch).GetMethod(
                "Prefix").MakeGenericMethod(target.GetParameters()[0].ParameterType);
        }

        public static bool Prefix<TController>(object __instance, TController iSender)
        {
            object sender = iSender;
            if (sender == null || IsEarlierBackActionActive(__instance, sender))
                return true;
            if (!(bool)isOffline.GetValue(null, null) ||
                (int)countActivePlayers.Invoke(__instance, null) > 1)
                return true;
            object senderPlayer = GetProperty(sender, "Player");
            object controller = sender;
            if (senderPlayer == null)
                controller = FindConnectedController();
            else if (!(bool)GetProperty(senderPlayer, "Connected"))
                return true;
            if (controller == null)
                return true;
            object player = GetProperty(controller, "Player");
            if (player == null)
                return true;
            int id = Convert.ToInt32(GetProperty(player, "ID"));
            Array slots = (Array)playerSlotsField.GetValue(__instance);
            if (id < 0 || id >= slots.Length)
                return true;
            object slot = slots.GetValue(id);
            object state = GetMember(slot, "State");
            if (state == null || state.ToString() != "Open")
                return true;
            gamerSelected.Invoke(__instance, new object[] { controller, null });
            returnToPreviousMenu.Invoke(__instance, null);
            return false;
        }

        private static bool IsEarlierBackActionActive(object instance, object sender)
        {
            int countdown = (int)countdownField.GetValue(instance);
            if (countdown <= 1 && countdown != -1)
                return true;
            if ((bool)GetMember(gameModeBoxField.GetValue(instance), "IsDown"))
                return true;
            object versus = versusSettingsField.GetValue(instance);
            IList items = GetMember(versus, "MenuItems") as IList;
            if (items != null)
                for (int index = 0; index < items.Count; index++)
                    if ((bool)GetMember(items[index], "IsDown"))
                        return true;
            object state = currentStateField.GetValue(instance);
            object current = currentControllerField.GetValue(instance);
            return state != null && state.ToString() != "Normal" &&
                ReferenceEquals(current, sender);
        }

        private static object FindConnectedController()
        {
            object game = gameInstance.GetValue(null, null);
            Array players = gamePlayers.GetValue(game, null) as Array;
            if (players == null)
                return null;
            for (int index = 0; index < players.Length; index++)
            {
                object player = players.GetValue(index);
                if (player != null && (bool)GetProperty(player, "Connected"))
                    return GetProperty(player, "Controller");
            }
            return null;
        }

        private static object GetProperty(object instance, string name)
        {
            return instance.GetType().GetProperty(name, AnyInstance)
                .GetValue(instance, null);
        }

        private static object GetMember(object instance, string name)
        {
            if (instance == null)
                return null;
            PropertyInfo property = instance.GetType().GetProperty(name, AnyInstance);
            if (property != null)
                return property.GetValue(instance, null);
            FieldInfo field = FindField(instance.GetType(), name);
            return field == null ? null : field.GetValue(instance);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo result = type.GetField(
                    name, AnyInstance | BindingFlags.DeclaredOnly);
                if (result != null)
                    return result;
                type = type.BaseType;
            }
            return null;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameters)
        {
            while (type != null)
            {
                MethodInfo[] methods = type.GetMethods(
                    AnyInstance | BindingFlags.DeclaredOnly);
                for (int index = 0; index < methods.Length; index++)
                    if (methods[index].Name == name &&
                        methods[index].GetParameters().Length == parameters)
                        return methods[index];
                type = type.BaseType;
            }
            return null;
        }
    }
}
