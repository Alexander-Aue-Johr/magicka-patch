using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkPostInitializePatch
    {
        private const BindingFlags Any = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;
        [ThreadStatic]
        private static int decodeDepth;
        [ThreadStatic]
        private static string side;

        internal static readonly RuntimePatchDefinition ClientScopeDefinition =
            ScopeDefinition("NetworkClient", "client");
        internal static readonly RuntimePatchDefinition ServerScopeDefinition =
            ScopeDefinition("NetworkServer", "server");
        internal static readonly RuntimePatchDefinition ShieldDefinition =
            EntityDefinition("Magicka.GameLogic.Entities.Shield", 7,
                "spawn_shield_has_no_playstate_after_initialize");
        internal static readonly RuntimePatchDefinition BarrierDefinition =
            EntityDefinition("Magicka.GameLogic.Entities.Barrier", 12,
                "spawn_barrier_has_no_playstate_after_initialize");
        internal static readonly RuntimePatchDefinition WaveDefinition =
            EntityDefinition("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.WaveEntity",
                13, "spawn_wave_has_no_playstate_after_initialize");
        internal static readonly RuntimePatchDefinition VortexDefinition =
            EntityDefinition("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VortexEntity",
                2, "spawn_vortex_has_no_playstate_after_initialize");
        internal static readonly RuntimePatchDefinition MineDefinition =
            EntityDefinition("Magicka.GameLogic.Entities.SpellMine", 11,
                "spawn_mine_has_no_playstate_after_initialize");

        private static RuntimePatchDefinition ScopeDefinition(string type,
            string scopeSide)
        {
            return RuntimePatchDefinition.PrefixAndPostfix(
                type + " packet decode scope",
                "org.magickacommunitypatch." + type.ToLowerInvariant() +
                    "-decode-scope",
                assembly => FindReadMessage(assembly, type),
                target => typeof(NetworkDecodeScopePatch<>)
                    .MakeGenericType(target.DeclaringType)
                    .GetMethod(scopeSide == "client" ? "ClientPrefix" : "ServerPrefix"),
                target => typeof(NetworkDecodeScopePatch<>)
                    .MakeGenericType(target.DeclaringType).GetMethod("Postfix"));
        }

        private static RuntimePatchDefinition EntityDefinition(string type,
            int count, string reason)
        {
            return RuntimePatchDefinition.Postfix(
                type + " network post-initialize guard",
                "org.magickacommunitypatch." + type.ToLowerInvariant() +
                    "-network-post-initialize",
                assembly => FindInitialize(assembly, type, count),
                target => CreateEntityPostfix(target, reason));
        }

        private static MethodInfo FindReadMessage(Assembly assembly, string name)
        {
            Type type = assembly.GetType("Magicka.Network." + name, true);
            foreach (MethodInfo method in type.GetMethods(Any |
                BindingFlags.DeclaredOnly))
                if (method.Name == "ReadMessage" &&
                    method.GetParameters().Length == 2) return method;
            throw new MissingMethodException(type.FullName, "ReadMessage");
        }

        private static MethodInfo FindInitialize(Assembly assembly,
            string name, int count)
        {
            Type type = assembly.GetType(name, true);
            foreach (MethodInfo method in type.GetMethods(Any |
                BindingFlags.DeclaredOnly))
                if (method.Name == "Initialize" &&
                    method.GetParameters().Length == count) return method;
            throw new MissingMethodException(type.FullName,
                "Initialize/" + count);
        }

        private static MethodInfo CreateEntityPostfix(MethodInfo target,
            string reason)
        {
            Type adapter = typeof(NetworkEntityInitializePostfix<>).MakeGenericType(
                target.DeclaringType);
            if (reason.IndexOf("shield_") >= 0) return adapter.GetMethod("Shield");
            if (reason.IndexOf("barrier_") >= 0) return adapter.GetMethod("Barrier");
            if (reason.IndexOf("wave_") >= 0) return adapter.GetMethod("Wave");
            if (reason.IndexOf("vortex_") >= 0) return adapter.GetMethod("Vortex");
            return adapter.GetMethod("Mine");
        }

        public static void Enter(string value)
        {
            decodeDepth++;
            side = value;
        }

        public static void Leave()
        {
            if (decodeDepth > 0) decodeDepth--;
            if (decodeDepth == 0) side = null;
        }

        public static void Validate(object entity, string reason)
        {
            if (decodeDepth == 0 || entity == null) return;
            object playState = Get(entity, "PlayState");
            if (playState != null && Get(playState, "EntityManager") != null)
                return;
            RuntimePatchTelemetry.SendNetworkGuardDrop(side, "spawn",
                String.Empty, String.Empty, reason, String.Empty);
            throw new NetworkPacketRejectedException();
        }

        private static object Get(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(name, Any |
                    BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(target, null);
                FieldInfo field = type.GetField(name, Any |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
            }
            return null;
        }
    }

    public static class NetworkDecodeScopePatch<T>
    {
        public static void ClientPrefix() { NetworkPostInitializePatch.Enter("client"); }
        public static void ServerPrefix() { NetworkPostInitializePatch.Enter("server"); }
        public static void Postfix() { NetworkPostInitializePatch.Leave(); }
    }

    public static class NetworkEntityInitializePostfix<T>
    {
        public static void Shield(T __instance) { NetworkPostInitializePatch.Validate(
            __instance, "spawn_shield_has_no_playstate_after_initialize"); }
        public static void Barrier(T __instance) { NetworkPostInitializePatch.Validate(
            __instance, "spawn_barrier_has_no_playstate_after_initialize"); }
        public static void Wave(T __instance) { NetworkPostInitializePatch.Validate(
            __instance, "spawn_wave_has_no_playstate_after_initialize"); }
        public static void Vortex(T __instance) { NetworkPostInitializePatch.Validate(
            __instance, "spawn_vortex_has_no_playstate_after_initialize"); }
        public static void Mine(T __instance) { NetworkPostInitializePatch.Validate(
            __instance, "spawn_mine_has_no_playstate_after_initialize"); }
    }
}
