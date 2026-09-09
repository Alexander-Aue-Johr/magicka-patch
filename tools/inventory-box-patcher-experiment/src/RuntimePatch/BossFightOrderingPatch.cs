using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal sealed class PendingBossSetup
    {
        internal object Boss;
        internal int AreaHash;
        internal int UniqueId;
    }

    internal static class BossFightPendingState
    {
        private static readonly List<PendingBossSetup> pending =
            new List<PendingBossSetup>();
        private static bool pendingStart;

        internal static int Count
        {
            get { return pending.Count; }
        }

        internal static PendingBossSetup Get(int index)
        {
            return pending[index];
        }

        internal static void Queue(object boss, int areaHash, int uniqueId)
        {
            for (int index = 0; index < pending.Count; index++)
            {
                if (!Object.ReferenceEquals(pending[index].Boss, boss))
                    continue;
                pending[index].AreaHash = areaHash;
                pending[index].UniqueId = uniqueId;
                return;
            }
            PendingBossSetup setup = new PendingBossSetup();
            setup.Boss = boss;
            setup.AreaHash = areaHash;
            setup.UniqueId = uniqueId;
            pending.Add(setup);
        }

        internal static void Remove(object boss)
        {
            for (int index = 0; index < pending.Count; index++)
            {
                if (!Object.ReferenceEquals(pending[index].Boss, boss))
                    continue;
                pending.RemoveAt(index);
                return;
            }
        }

        internal static PendingBossSetup RemoveAt(int index)
        {
            PendingBossSetup setup = pending[index];
            pending.RemoveAt(index);
            return setup;
        }

        internal static bool ShouldRunStart(bool isClient)
        {
            if (!isClient || pending.Count == 0)
                return true;
            pendingStart = true;
            return false;
        }

        internal static bool TakePendingStartIfReady()
        {
            if (pending.Count != 0 || !pendingStart)
                return false;
            pendingStart = false;
            return true;
        }

        internal static void Clear()
        {
            pending.Clear();
            pendingStart = false;
        }
    }

    public static class BossFightOrderingPatch
    {
        private const string BossFightTypeName =
            "Magicka.GameLogic.Entities.Bosses.BossFight";
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo playStateField;
        private static FieldInfo initializeQueueField;
        private static FieldInfo bossIdField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo isClientGetter;
        private static MethodInfo networkManagerInstanceGetter;
        private static MethodInfo networkStateGetter;
        private static object clientNetworkState;
        private static MethodInfo bossInitializedGetter;
        private static MethodInfo getBossType;
        private static MethodInfo bossNetworkInitialize;
        private static MethodInfo initializeMethod;
        private static MethodInfo startMethod;

        internal static readonly RuntimePatchDefinition SetupDefinition =
            RuntimePatchDefinition.Transpile(
                "BossFight play-state setup release",
                "org.magickacommunitypatch.boss-fight-setup-state",
                FindSetup,
                typeof(BossFightOrderingPatch).GetMethod("SetupTranspiler"));

        internal static readonly RuntimePatchDefinition InitializePrefixDefinition =
            RuntimePatchDefinition.Prefix(
                "BossFight deferred client initialization",
                "org.magickacommunitypatch.boss-fight-initialize-prefix",
                FindInitialize,
                CreateInitializePrefix);

        internal static readonly RuntimePatchDefinition InitializeStateDefinition =
            RuntimePatchDefinition.Transpile(
                "BossFight current initialization play state",
                "org.magickacommunitypatch.boss-fight-initialize-state",
                FindInitialize,
                typeof(BossFightOrderingPatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition StartDefinition =
            RuntimePatchDefinition.Prefix(
                "BossFight deferred client start",
                "org.magickacommunitypatch.boss-fight-start",
                FindStart,
                FixedPrefix("StartPrefix"));

        internal static readonly RuntimePatchDefinition ClearDefinition =
            RuntimePatchDefinition.Postfix(
                "BossFight pending-state clear",
                "org.magickacommunitypatch.boss-fight-clear",
                FindClear,
                FixedPostfix("ClearPostfix"));

        internal static readonly RuntimePatchDefinition ResetDefinition =
            RuntimePatchDefinition.Postfix(
                "BossFight pending-state reset",
                "org.magickacommunitypatch.boss-fight-reset",
                FindReset,
                FixedPostfix("ClearPostfix"));

        internal static readonly RuntimePatchDefinition ResetStateDefinition =
            RuntimePatchDefinition.Transpile(
                "BossFight current reset play state",
                "org.magickacommunitypatch.boss-fight-reset-state",
                FindReset,
                typeof(BossFightOrderingPatch).GetMethod("ResetTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Prefix(
                "BossFight pending initialization retry",
                "org.magickacommunitypatch.boss-fight-update-prefix",
                FindUpdate,
                FixedPrefix("UpdatePrefix"));

        internal static readonly RuntimePatchDefinition UpdateStateDefinition =
            RuntimePatchDefinition.Transpile(
                "BossFight current update play state",
                "org.magickacommunitypatch.boss-fight-update-state",
                FindUpdate,
                typeof(BossFightOrderingPatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition NetworkInitializeDefinition =
            RuntimePatchDefinition.Prefix(
                "BossFight pending packet completion",
                "org.magickacommunitypatch.boss-fight-network-initialize",
                FindNetworkInitialize,
                CreateNetworkInitializePrefix);

        private static Func<MethodInfo, MethodInfo> FixedPrefix(string name)
        {
            MethodInfo prefix = typeof(BossFightOrderingPatch).GetMethod(name);
            return delegate { return prefix; };
        }

        private static Func<MethodInfo, MethodInfo> FixedPostfix(string name)
        {
            MethodInfo postfix = typeof(BossFightOrderingPatch).GetMethod(name);
            return delegate { return postfix; };
        }

        private static MethodInfo FindSetup(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            return RequireMethod(
                bossFight,
                "Setup",
                new Type[] {
                    playState,
                    typeof(float),
                    typeof(float),
                    typeof(float)
                },
                true);
        }

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            return initializeMethod;
        }

        private static MethodInfo FindStart(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            return startMethod;
        }

        private static MethodInfo FindClear(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            return RequireMethod(bossFight, "Clear", Type.EmptyTypes, true);
        }

        private static MethodInfo FindReset(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            return RequireMethod(bossFight, "Reset", Type.EmptyTypes, true);
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                bossFight,
                "Update",
                new Type[] { dataChannel, typeof(float) },
                true);
        }

        private static MethodInfo FindNetworkInitialize(Assembly assembly)
        {
            Type bossFight;
            Type playState;
            Configure(assembly, out bossFight, out playState);
            MethodInfo[] methods = bossFight.GetMethods(InstanceMembers);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "NetworkInitialize" &&
                    methods[index].ReturnType == typeof(void) &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType.IsByRef)
                    return methods[index];
            }
            throw new MissingMethodException(
                bossFight.FullName,
                "NetworkInitialize");
        }

        private static void Configure(
            Assembly assembly,
            out Type bossFight,
            out Type playState)
        {
            bossFight = assembly.GetType(BossFightTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = bossFight.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            initializeQueueField = bossFight.GetField(
                "mNetworkInitializeMessageQueue",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();

            initializeMethod = FindBossInitialize(bossFight);
            startMethod = RequireMethod(
                bossFight,
                "Start",
                Type.EmptyTypes,
                true);
            Type bossType = initializeMethod.GetParameters()[0].ParameterType;
            PropertyInfo initialized = bossType.GetProperty(
                "NetworkInitialized",
                BindingFlags.Instance | BindingFlags.Public);
            bossInitializedGetter = initialized == null
                ? null
                : initialized.GetGetMethod();
            getBossType = bossType.GetMethod(
                "GetBossType",
                BindingFlags.Instance | BindingFlags.Public);
            bossNetworkInitialize = FindBossNetworkInitialize(bossType);

            MethodInfo networkInitialize = FindNetworkInitializeWithoutConfigure(
                bossFight);
            Type messageType = networkInitialize.GetParameters()[0]
                .ParameterType.GetElementType();
            bossIdField = messageType.GetField(
                "BossID",
                BindingFlags.Instance | BindingFlags.Public);

            Type networkManager = assembly.GetType(
                "Magicka.Network.NetworkManager",
                true);
            PropertyInfo isClient = networkManager.GetProperty(
                "IsClient",
                BindingFlags.Static | BindingFlags.Public);
            isClientGetter = isClient == null ? null : isClient.GetGetMethod();
            networkManagerInstanceGetter = null;
            networkStateGetter = null;
            clientNetworkState = null;
            if (isClientGetter == null)
            {
                PropertyInfo instance = networkManager.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public);
                PropertyInfo state = networkManager.GetProperty(
                    "State",
                    BindingFlags.Instance | BindingFlags.Public);
                networkManagerInstanceGetter = instance == null
                    ? null
                    : instance.GetGetMethod();
                networkStateGetter = state == null
                    ? null
                    : state.GetGetMethod();
                if (networkStateGetter != null)
                    clientNetworkState = Enum.Parse(
                        networkStateGetter.ReturnType,
                        "Client");
            }

            if (playStateField == null || initializeQueueField == null ||
                recentPlayStateGetter == null ||
                bossInitializedGetter == null || getBossType == null ||
                bossNetworkInitialize == null || bossIdField == null ||
                (isClientGetter == null &&
                    (networkManagerInstanceGetter == null ||
                        networkStateGetter == null ||
                        clientNetworkState == null)))
                throw new MissingMemberException(
                    "BossFight ordering contract is incomplete.");
        }

        private static MethodInfo FindBossInitialize(Type bossFight)
        {
            MethodInfo[] methods = bossFight.GetMethods(InstanceMembers);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "Initialize" &&
                    methods[index].ReturnType == typeof(void) &&
                    parameters.Length == 3 &&
                    parameters[1].ParameterType == typeof(int) &&
                    parameters[2].ParameterType == typeof(int))
                    return methods[index];
            }
            throw new MissingMethodException(bossFight.FullName, "Initialize");
        }

        private static MethodInfo FindBossNetworkInitialize(Type bossType)
        {
            MethodInfo[] methods = bossType.GetMethods();
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "NetworkInitialize" &&
                    methods[index].ReturnType == typeof(void) &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType.IsByRef)
                    return methods[index];
            }
            return null;
        }

        private static MethodInfo FindNetworkInitializeWithoutConfigure(
            Type bossFight)
        {
            MethodInfo[] methods = bossFight.GetMethods(InstanceMembers);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "NetworkInitialize" &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType.IsByRef)
                    return methods[index];
            }
            throw new MissingMethodException(
                bossFight.FullName,
                "NetworkInitialize");
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            bool declaredOnly)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic;
            if (declaredOnly)
                flags |= BindingFlags.DeclaredOnly;
            MethodInfo method = type.GetMethod(
                name,
                flags,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static MethodInfo CreateInitializePrefix(MethodInfo target)
        {
            Type bossType = target.GetParameters()[0].ParameterType;
            Type adapter = typeof(BossFightInitializePrefix<>).MakeGenericType(
                bossType);
            return adapter.GetMethod("Prefix");
        }

        private static MethodInfo CreateNetworkInitializePrefix(MethodInfo target)
        {
            Type messageType = target.GetParameters()[0]
                .ParameterType.GetElementType();
            Type adapter = typeof(BossFightNetworkInitializePrefix<>)
                .MakeGenericType(messageType);
            return adapter.GetMethod("Prefix");
        }

        public static bool PrepareInitialization(
            object bossFight,
            object boss,
            int areaHash,
            int uniqueId)
        {
            ApplyQueuedInitializeMessages(bossFight, boss);
            if (IsClient() && !IsNetworkInitialized(boss))
            {
                BossFightPendingState.Queue(boss, areaHash, uniqueId);
                return false;
            }
            BossFightPendingState.Remove(boss);
            return true;
        }

        public static bool StartPrefix()
        {
            return BossFightPendingState.ShouldRunStart(IsClient());
        }

        public static void ClearPostfix()
        {
            BossFightPendingState.Clear();
        }

        public static void UpdatePrefix(object __instance)
        {
            IList queue = (IList)initializeQueueField.GetValue(__instance);
            if (queue.Count == 0)
                return;
            object message = queue[0];
            queue.RemoveAt(0);
            if (!TryCompletePending(__instance, ref message))
                queue.Add(message);
        }

        public static bool PrepareNetworkInitialize(
            object bossFight,
            ref object message)
        {
            return !TryCompletePending(bossFight, ref message);
        }

        private static void ApplyQueuedInitializeMessages(
            object bossFight,
            object boss)
        {
            IList queue = (IList)initializeQueueField.GetValue(bossFight);
            object bossType = GetBossType(boss);
            for (int index = 0; index < queue.Count; index++)
            {
                object message = queue[index];
                if (!Object.Equals(bossType, bossIdField.GetValue(message)))
                    continue;
                InvokeNetworkInitialize(boss, ref message);
                if (IsNetworkInitialized(boss))
                    queue.RemoveAt(index--);
            }
        }

        private static bool TryCompletePending(
            object bossFight,
            ref object message)
        {
            object messageBossType = bossIdField.GetValue(message);
            for (int index = 0; index < BossFightPendingState.Count; index++)
            {
                PendingBossSetup setup = BossFightPendingState.Get(index);
                if (!Object.Equals(
                        GetBossType(setup.Boss),
                        messageBossType))
                    continue;
                InvokeNetworkInitialize(setup.Boss, ref message);
                if (!IsNetworkInitialized(setup.Boss))
                    return false;
                setup = BossFightPendingState.RemoveAt(index);
                InvokeUnwrapped(
                    initializeMethod,
                    bossFight,
                    new object[] {
                        setup.Boss,
                        setup.AreaHash,
                        setup.UniqueId
                    });
                if (BossFightPendingState.TakePendingStartIfReady())
                    InvokeUnwrapped(startMethod, bossFight, new object[0]);
                return true;
            }
            return false;
        }

        private static void InvokeNetworkInitialize(
            object boss,
            ref object message)
        {
            object[] arguments = new object[] { message };
            InvokeUnwrapped(bossNetworkInitialize, boss, arguments);
            message = arguments[0];
        }

        private static object GetBossType(object boss)
        {
            return InvokeUnwrapped(getBossType, boss, null);
        }

        private static bool IsNetworkInitialized(object boss)
        {
            return (bool)InvokeUnwrapped(
                bossInitializedGetter,
                boss,
                null);
        }

        private static bool IsClient()
        {
            if (isClientGetter != null)
                return (bool)InvokeUnwrapped(isClientGetter, null, null);
            object manager = InvokeUnwrapped(
                networkManagerInstanceGetter,
                null,
                null);
            object state = InvokeUnwrapped(
                networkStateGetter,
                manager,
                null);
            return Object.Equals(state, clientNetworkState);
        }

        private static object InvokeUnwrapped(
            MethodInfo method,
            object instance,
            object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                    throw exception.InnerException;
                throw;
            }
        }

        public static IEnumerable<CodeInstruction> SetupTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemovePlayStateStore(instructions, "Setup");
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 2, "Initialize");
        }

        public static IEnumerable<CodeInstruction> ResetTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 2, "Reset");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 1, "Update");
        }

        private static IEnumerable<CodeInstruction> RemovePlayStateStore(
            IEnumerable<CodeInstruction> instructions,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            for (int index = 2; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !SameMember(result[index].operand, playStateField))
                    continue;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_1)
                    throw new InvalidOperationException(
                        "BossFight " + methodName +
                        " play-state store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            if (stores != 1)
                throw new InvalidOperationException(
                    "Expected one BossFight " + methodName +
                    " play-state store, found " + stores + ".");
            return result;
        }

        private static IEnumerable<CodeInstruction> ReplacePlayStateReads(
            IEnumerable<CodeInstruction> instructions,
            int expectedReads,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int reads = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !SameMember(result[index].operand, playStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "BossFight " + methodName +
                        " play-state read source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                reads++;
            }
            if (reads != expectedReads)
                throw new InvalidOperationException(
                    "Expected " + expectedReads + " BossFight " +
                    methodName + " play-state reads, found " + reads + ".");
            return result;
        }

        private static bool SameMember(object left, MemberInfo right)
        {
            MemberInfo member = left as MemberInfo;
            return member != null && right != null &&
                member.Module == right.Module &&
                member.MetadataToken == right.MetadataToken;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }

    public static class BossFightInitializePrefix<TBoss>
    {
        public static bool Prefix(
            object __instance,
            TBoss iBoss,
            int iAreaHash,
            int iUniqueID)
        {
            return BossFightOrderingPatch.PrepareInitialization(
                __instance,
                iBoss,
                iAreaHash,
                iUniqueID);
        }
    }

    public static class BossFightNetworkInitializePrefix<TMessage>
    {
        public static bool Prefix(object __instance, ref TMessage iMsg)
        {
            object message = iMsg;
            bool runOriginal = BossFightOrderingPatch.PrepareNetworkInitialize(
                __instance,
                ref message);
            iMsg = (TMessage)message;
            return runOriginal;
        }
    }
}
