using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class NetworkServerForcedSyncScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        if (magicka.GetType(
            "Magicka.Network.RequestForcedPlayerStatusSync",
            false) == null)
        {
            report.AddNotApplicable(
                "forced_sync.invalid_sender",
                "Legacy network protocol has no forced player-status request");
            report.AddNotApplicable(
                "forced_sync.sparse_players",
                "Legacy network protocol has no forced player-status response");
            report.AddNotApplicable(
                "forced_sync.missing_avatar",
                "Legacy network protocol has no forced player-status response");
            return;
        }
        NetworkServerForcedSyncHarness harness =
            new NetworkServerForcedSyncHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("forced_sync.invalid_sender", harness.InvalidSender());
        report.Add("forced_sync.sparse_players", harness.SparsePlayers());
        report.Add("forced_sync.missing_avatar", harness.MissingAvatar());
    }
}

internal sealed class NetworkServerForcedSyncHarness
{
    private readonly Assembly magicka;
    private readonly bool runtimePatchEnabled;
    private readonly Type gameType;
    private readonly Type playerType;
    private readonly Type avatarType;
    private readonly Type entityType;
    private readonly Type networkGamerType;
    private readonly Type requestType;
    private readonly Type responseType;
    private readonly Type steamIdType;
    private readonly Type testAvatarType;
    private readonly FieldInfo gameSingletonField;
    private readonly FieldInfo gamePlayersField;
    private readonly FieldInfo playerIdField;
    private readonly FieldInfo playerGamerField;
    private readonly FieldInfo playerAvatarField;
    private readonly FieldInfo avatarPlayerField;
    private readonly FieldInfo networkGamerClientField;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo requestHandleField;
    private readonly FieldInfo responseCountField;
    private readonly FieldInfo responseUpdatesField;
    private readonly FieldInfo updateHandleField;
    private readonly PropertyInfo requestPacketTypeProperty;
    private readonly MethodInfo requestWrite;
    private readonly MethodInfo serverReadMessage;
    private readonly MethodInfo originalBuilder;
    private readonly object server;
    private readonly byte requestPacket;
    private readonly List<object> retainedAvatars = new List<object>();

    internal NetworkServerForcedSyncHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.magicka = magicka;
        this.runtimePatchEnabled = runtimePatchEnabled;
        gameType = magicka.GetType("Magicka.Game", true);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        networkGamerType = magicka.GetType(
            "Magicka.Gamers.NetworkGamer",
            true);
        requestType = magicka.GetType(
            "Magicka.Network.RequestForcedPlayerStatusSync",
            true);
        responseType = magicka.GetType(
            "Magicka.Network.ForceSyncPlayerStatusesMessage",
            true);
        Type updateType = magicka.GetType(
            "Magicka.Network.EntityUpdateMessage",
            true);
        Type serverType = magicka.GetType(
            "Magicka.Network.NetworkServer",
            true);
        gameSingletonField = RuntimeReflection.RequireField(
            gameType,
            "mSingelton");
        gamePlayersField = RuntimeReflection.RequireField(gameType, "mPlayers");
        playerIdField = RuntimeReflection.RequireField(playerType, "mID");
        playerGamerField = RuntimeReflection.RequireField(playerType, "mGamer");
        playerAvatarField = RuntimeReflection.RequireField(playerType, "mAvatar");
        avatarPlayerField = RuntimeReflection.RequireField(avatarType, "mPlayer");
        networkGamerClientField = RuntimeReflection.RequireField(
            networkGamerType,
            "mClientID");
        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        instancesField = RuntimeReflection.RequireField(entityType, "mInstances");
        requestHandleField = RuntimeReflection.RequireField(requestType, "Handle");
        responseCountField = RuntimeReflection.RequireField(
            responseType,
            "numPlayers");
        responseUpdatesField = RuntimeReflection.RequireField(
            responseType,
            "playerUpdateMessages");
        updateHandleField = RuntimeReflection.RequireField(updateType, "Handle");
        requestPacketTypeProperty = requestType.GetProperty(
            "PacketType",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        requestWrite = requestType.GetMethod(
            "Write",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(BinaryWriter) },
            null);
        serverReadMessage = FindReadMessage(serverType);
        steamIdType = serverReadMessage.GetParameters()[1].ParameterType;
        originalBuilder = FindOriginalBuilder(serverType);
        if (requestPacketTypeProperty == null || requestWrite == null)
            throw new MissingMemberException(requestType.FullName, "packet writer");
        object request = Activator.CreateInstance(requestType);
        requestPacket = Convert.ToByte(
            requestPacketTypeProperty.GetValue(request, null));
        server = FormatterServices.GetUninitializedObject(serverType);
        testAvatarType = CreateTestAvatarType(updateType);
    }

    internal ScenarioResult InvalidSender()
    {
        object previousGame = gameSingletonField.GetValue(null);
        IList instances = (IList)instancesField.GetValue(null);
        instances.Clear();
        try
        {
            object expectedId = CreateSteamId(111UL);
            object wrongSender = CreateSteamId(222UL);
            object requestedPlayer = CreatePlayer(0, expectedId, true);
            SetGamePlayers(new object[] { requestedPlayer });

            object stalePlayer = CreatePlayer(3, CreateSteamId(333UL), false);
            object staleAvatar = CreateAvatar(stalePlayer);
            instances.Add(staleAvatar);

            object request = Activator.CreateInstance(requestType);
            requestHandleField.SetValue(request, (ushort)0);
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(requestPacket);
            requestWrite.Invoke(request, new object[] { writer });
            writer.Flush();
            stream.Position = 0;

            string actual = Invoke(
                serverReadMessage,
                server,
                new object[] { new BinaryReader(stream), wrongSender });
            return new ScenarioResult(
                actual == "returned",
                actual,
                "returned");
        }
        finally
        {
            instances.Clear();
            gameSingletonField.SetValue(null, previousGame);
        }
    }

    internal ScenarioResult SparsePlayers()
    {
        object previousGame = gameSingletonField.GetValue(null);
        try
        {
            object first = CreatePlayer(1, CreateSteamId(101UL), true);
            object second = CreatePlayer(3, CreateSteamId(303UL), true);
            SetGamePlayers(new object[] { null, first, null, second });

            object response = null;
            string actual;
            MethodInfo builder = FindReplacementBuilder();
            if (builder != null)
            {
                try
                {
                    response = builder.Invoke(null, null);
                    actual = "returned";
                }
                catch (TargetInvocationException exception)
                {
                    Exception inner = exception.InnerException ?? exception;
                    actual = "exception:" + inner.GetType().Name;
                }
            }
            else
            {
                actual = Invoke(
                    originalBuilder,
                    server,
                    new object[] { 0, false });
            }

            string handles = response == null
                ? "<none>"
                : ReadResponseHandles(response);
            string detail = actual + ",handles:" + handles;
            return new ScenarioResult(
                actual == "returned" && handles == "1,3",
                detail,
                "returned,handles:1,3");
        }
        finally
        {
            gameSingletonField.SetValue(null, previousGame);
        }
    }

    internal ScenarioResult MissingAvatar()
    {
        object previousGame = gameSingletonField.GetValue(null);
        try
        {
            object player = CreatePlayer(0, CreateSteamId(101UL), false);
            SetGamePlayers(new object[] { player });
            string actual = Invoke(
                originalBuilder,
                server,
                new object[] { 0, false });
            return new ScenarioResult(
                actual == "returned",
                actual,
                "returned");
        }
        finally
        {
            gameSingletonField.SetValue(null, previousGame);
        }
    }

    private MethodInfo FindReplacementBuilder()
    {
        Type helper = null;
        if (runtimePatchEnabled)
        {
            helper = FindLoadedType(
                "Magicka.CommunityPatch.Runtime.NetworkServerForcedSyncPatch");
            return helper == null
                ? null
                : helper.GetMethod(
                "BuildForcedSyncPlayerStatuses",
                BindingFlags.Static | BindingFlags.Public);
        }
        helper = magicka.GetType(
            "Magicka.CommunityPatch.NetworkLifecycleCompatibility",
            false);
        return helper == null
            ? null
            : helper.GetMethod(
                "BuildForcedSyncPlayerStatuses",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
    }

    private string ReadResponseHandles(object response)
    {
        int count = Convert.ToInt32(responseCountField.GetValue(response));
        Array updates = (Array)responseUpdatesField.GetValue(response);
        List<string> handles = new List<string>();
        for (int index = 0; index < count; index++)
        {
            handles.Add(Convert.ToString(
                updateHandleField.GetValue(updates.GetValue(index))));
        }
        return String.Join(",", handles.ToArray());
    }

    private void SetGamePlayers(object[] values)
    {
        object game = FormatterServices.GetUninitializedObject(gameType);
        Array players = Array.CreateInstance(playerType, values.Length);
        for (int index = 0; index < values.Length; index++)
            players.SetValue(values[index], index);
        gamePlayersField.SetValue(game, players);
        gameSingletonField.SetValue(null, game);
    }

    private object CreatePlayer(int id, object clientId, bool withAvatar)
    {
        object player = FormatterServices.GetUninitializedObject(playerType);
        playerIdField.SetValue(player, id);
        object gamer = FormatterServices.GetUninitializedObject(networkGamerType);
        networkGamerClientField.SetValue(gamer, clientId);
        playerGamerField.SetValue(player, gamer);
        playerAvatarField.SetValue(player, new WeakReference(null));
        if (withAvatar)
        {
            object avatar = CreateAvatar(player);
            playerAvatarField.SetValue(player, new WeakReference(avatar));
        }
        return player;
    }

    private object CreateAvatar(object player)
    {
        object avatar = FormatterServices.GetUninitializedObject(testAvatarType);
        avatarPlayerField.SetValue(avatar, player);
        retainedAvatars.Add(avatar);
        return avatar;
    }

    private object CreateSteamId(ulong value)
    {
        ConstructorInfo constructor = steamIdType.GetConstructor(
            new Type[] { typeof(ulong) });
        if (constructor == null)
            throw new MissingMethodException(steamIdType.FullName, ".ctor(UInt64)");
        return constructor.Invoke(new object[] { value });
    }

    private Type CreateTestAvatarType(Type updateType)
    {
        AssemblyName name = new AssemblyName(
            "ForcedSyncTestAvatar" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
            name,
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name.Name);
        TypeBuilder type = module.DefineType(
            name.Name + ".Avatar",
            TypeAttributes.Public | TypeAttributes.Class,
            avatarType);
        DefineForwardingConstructor(type);
        MethodInfo original = avatarType.GetMethod(
            "IGetNetworkUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (original == null)
            throw new MissingMethodException(avatarType.FullName, "IGetNetworkUpdate");
        MethodBuilder replacement = type.DefineMethod(
            original.Name,
            MethodAttributes.Family | MethodAttributes.Virtual |
                MethodAttributes.HideBySig,
            typeof(void),
            new Type[] { updateType.MakeByRefType(), typeof(float) });
        ILGenerator il = replacement.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Initobj, updateType);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(replacement, original);
        return type.CreateType();
    }

    private void DefineForwardingConstructor(TypeBuilder type)
    {
        ConstructorInfo[] constructors = avatarType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (constructors.Length == 0)
            throw new MissingMethodException(avatarType.FullName, ".ctor");
        ConstructorInfo selected = constructors[0];
        ParameterInfo[] parameters = selected.GetParameters();
        Type[] parameterTypes = new Type[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
            parameterTypes[index] = parameters[index].ParameterType;
        ConstructorBuilder constructor = type.DefineConstructor(
            MethodAttributes.Public,
            selected.CallingConvention,
            parameterTypes);
        ILGenerator il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        for (int index = 0; index < parameterTypes.Length; index++)
            il.Emit(OpCodes.Ldarg, index + 1);
        il.Emit(OpCodes.Call, selected);
        il.Emit(OpCodes.Ret);
    }

    private static MethodInfo FindReadMessage(Type serverType)
    {
        MethodInfo[] methods = serverType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            MethodInfo method = methods[index];
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name == "ReadMessage" &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(BinaryReader))
                return method;
        }
        throw new MissingMethodException(serverType.FullName, "ReadMessage");
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }

    private static MethodInfo FindOriginalBuilder(Type serverType)
    {
        MethodInfo[] methods = serverType.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            MethodInfo method = methods[index];
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name == "SendForcedSyncMessageToClient" &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(int) &&
                parameters[1].ParameterType == typeof(bool))
                return method;
        }
        throw new MissingMethodException(
            serverType.FullName,
            "SendForcedSyncMessageToClient(Int32, Boolean)");
    }

    private static string Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
            return "returned";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return "exception:" + inner.GetType().Name;
        }
    }
}
