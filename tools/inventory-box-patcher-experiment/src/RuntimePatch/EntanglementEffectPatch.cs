using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate object EntanglementRenderManagerGetter();

    internal delegate object EntanglementEffectGetter(
        object manager,
        int hash);

    internal delegate void EntanglementEffectRegistrar(
        object manager,
        object effect);

    internal delegate object EntanglementEffectFactory(
        object graphicsDevice,
        object contentManager);

    internal delegate object EntanglementDiffuseMapGetter(object meshPart);

    internal delegate void EntanglementDiffuseMapSetter(
        object effect,
        object texture);

    public static class EntanglementEffectPatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Entanglement";

        private static FieldInfo effectField;
        private static ConstructorInfo effectConstructor;
        private static MethodInfo diffuseMapSetter;
        private static Type modelMeshPartType;
        private static int effectHash;
        private static EntanglementRenderManagerGetter getRenderManager;
        private static EntanglementEffectGetter getEffect;
        private static EntanglementEffectRegistrar registerEffect;
        private static EntanglementEffectFactory createEffect;
        private static EntanglementDiffuseMapGetter getDiffuseMap;
        private static EntanglementDiffuseMapSetter setDiffuseMap;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Entanglement shared render effect",
                "org.magickacommunitypatch.entanglement-shared-effect",
                FindConstructor,
                typeof(EntanglementEffectPatch).GetMethod(
                    "ConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "Entanglement redundant effect update removal",
                "org.magickacommunitypatch.entanglement-effect-update",
                FindInitialize,
                typeof(EntanglementEffectPatch).GetMethod(
                    "InitializeTranspiler"));

        private static ConstructorInfo FindConstructor(Assembly targetAssembly)
        {
            Type entanglement = Configure(targetAssembly);
            ConstructorInfo[] constructors = entanglement.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (constructors.Length != 1 ||
                constructors[0].GetParameters().Length != 1)
                throw new InvalidOperationException(
                    "Expected one Entanglement constructor.");
            return constructors[0];
        }

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type entanglement = Configure(targetAssembly);
            MethodInfo method = entanglement.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    entanglement.FullName,
                    "Initialize");
            return method;
        }

        private static Type Configure(Assembly targetAssembly)
        {
            Type entanglement = targetAssembly.GetType(TypeName, true);
            if (effectField != null &&
                effectField.DeclaringType == entanglement)
                return entanglement;

            effectField = entanglement.GetField(
                "mEffect",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (effectField == null)
                throw new MissingFieldException(
                    entanglement.FullName,
                    "mEffect");

            Type effectType = effectField.FieldType;
            FieldInfo typeHash = effectType.GetField(
                "TYPEHASH",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (typeHash == null || typeHash.FieldType != typeof(int))
                throw new MissingFieldException(effectType.FullName, "TYPEHASH");
            effectHash = (int)typeHash.GetValue(null);

            Type graphicsDevice = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
            Type contentManager = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Content.ContentManager");
            modelMeshPartType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.ModelMeshPart");
            Type texture = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.Texture2D");
            Type skinnedEffect = RuntimeMember.FindLoadedType(
                "XNAnimation.Effects.SkinnedModelBasicEffect");

            effectConstructor = effectType.GetConstructor(
                new Type[] { graphicsDevice, contentManager });
            PropertyInfo effectDiffuseMap = effectType.GetProperty(
                "DiffuseMap",
                BindingFlags.Instance | BindingFlags.Public);
            diffuseMapSetter = effectDiffuseMap == null
                ? null
                : effectDiffuseMap.GetSetMethod();
            PropertyInfo meshEffect = modelMeshPartType.GetProperty(
                "Effect",
                BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo diffuseMap = skinnedEffect.GetProperty(
                "DiffuseMap0",
                BindingFlags.Instance | BindingFlags.Public);
            if (effectConstructor == null || diffuseMapSetter == null ||
                meshEffect == null || diffuseMap == null ||
                diffuseMapSetter.GetParameters()[0].ParameterType != texture ||
                diffuseMap.PropertyType != texture)
                throw new MissingMemberException(
                    "Entanglement effect construction contract is incomplete.");

            Type renderManager = RuntimeMember.FindLoadedType(
                "PolygonHead.RenderManager");
            PropertyInfo instance = renderManager.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo findEffect = renderManager.GetMethod(
                "GetEffect",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { typeof(int) },
                null);
            MethodInfo addEffect = FindRegisterEffect(
                renderManager,
                effectType);
            if (instance == null || instance.GetGetMethod() == null ||
                findEffect == null ||
                !findEffect.ReturnType.IsAssignableFrom(effectType) ||
                addEffect == null)
                throw new MissingMemberException(
                    "RenderManager effect registry contract is incomplete.");

            getRenderManager = CreateRenderManagerGetter(
                instance.GetGetMethod());
            getEffect = CreateEffectGetter(renderManager, findEffect);
            registerEffect = CreateEffectRegistrar(
                renderManager,
                addEffect);
            createEffect = CreateEffectFactory(
                graphicsDevice,
                contentManager,
                effectConstructor);
            getDiffuseMap = CreateDiffuseMapGetter(
                modelMeshPartType,
                meshEffect.GetGetMethod(),
                skinnedEffect,
                diffuseMap.GetGetMethod());
            setDiffuseMap = CreateDiffuseMapSetter(
                effectType,
                texture,
                diffuseMapSetter);
            return entanglement;
        }

        private static MethodInfo FindRegisterEffect(
            Type renderManager,
            Type effectType)
        {
            MethodInfo[] methods = renderManager.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "RegisterEffect" ||
                    parameters.Length != 1 ||
                    !parameters[0].ParameterType.IsAssignableFrom(effectType))
                    continue;
                if (result != null)
                    throw new AmbiguousMatchException(
                        "Multiple RenderManager.RegisterEffect methods match EntangleEffect.");
                result = method;
            }
            return result;
        }

        public static TEffect GetOrCreateSharedEffect<TEffect>(
            object graphicsDevice,
            object contentManager,
            object meshPart)
            where TEffect : class
        {
            object manager = getRenderManager();
            object effect = getEffect(manager, effectHash);
            if (effect == null)
            {
                effect = createEffect(graphicsDevice, contentManager);
                setDiffuseMap(effect, getDiffuseMap(meshPart));
                registerEffect(manager, effect);
            }
            return (TEffect)effect;
        }

        public static IEnumerable<CodeInstruction> ConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int allocations = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Newobj ||
                    !Object.Equals(result[index].operand, effectConstructor))
                    continue;
                if (index + 1 >= result.Count ||
                    result[index + 1].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index + 1].operand, effectField))
                    throw new InvalidOperationException(
                        "Entanglement effect allocation/store shape changed.");

                CodeInstruction meshPartLoad = FindMeshPartLoad(
                    result,
                    index);
                meshPartLoad.labels.AddRange(result[index].labels);
                result[index].labels.Clear();
                result.Insert(
                    index,
                    meshPartLoad);
                index++;
                MethodInfo helper = typeof(EntanglementEffectPatch).GetMethod(
                    "GetOrCreateSharedEffect").MakeGenericMethod(
                        effectField.FieldType);
                result[index].opcode = OpCodes.Call;
                result[index].operand = helper;
                allocations++;
            }
            if (allocations != 1)
                throw new InvalidOperationException(
                    "Expected one Entanglement effect allocation, found " +
                    allocations + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int setters = 0;
            for (int index = 6; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Callvirt ||
                    !Object.Equals(result[index].operand, diffuseMapSetter))
                    continue;
                if (result[index - 6].opcode != OpCodes.Ldarg_0 ||
                    result[index - 5].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index - 5].operand, effectField))
                    throw new InvalidOperationException(
                        "Entanglement diffuse-map assignment shape changed.");
                for (int remove = index - 6; remove <= index; remove++)
                {
                    result[remove].opcode = OpCodes.Nop;
                    result[remove].operand = null;
                }
                setters++;
            }
            if (setters != 1)
                throw new InvalidOperationException(
                    "Expected one Entanglement diffuse-map assignment, found " +
                    setters + ".");
            return result;
        }

        private static CodeInstruction FindMeshPartLoad(
            IList<CodeInstruction> instructions,
            int before)
        {
            for (int index = before - 1; index >= 0; index--)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if (method == null || method.ReturnType != modelMeshPartType ||
                    index + 1 >= before ||
                    !IsLocalStore(instructions[index + 1].opcode))
                    continue;
                return LoadForStore(instructions[index + 1]);
            }
            throw new InvalidOperationException(
                "Entanglement mesh-part local was not found.");
        }

        private static bool IsLocalStore(OpCode opcode)
        {
            return opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S ||
                opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 ||
                opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;
        }

        private static CodeInstruction LoadForStore(CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc_0)
                return new CodeInstruction(OpCodes.Ldloc_0);
            if (store.opcode == OpCodes.Stloc_1)
                return new CodeInstruction(OpCodes.Ldloc_1);
            if (store.opcode == OpCodes.Stloc_2)
                return new CodeInstruction(OpCodes.Ldloc_2);
            if (store.opcode == OpCodes.Stloc_3)
                return new CodeInstruction(OpCodes.Ldloc_3);
            if (store.opcode == OpCodes.Stloc_S)
                return new CodeInstruction(OpCodes.Ldloc_S, store.operand);
            if (store.opcode == OpCodes.Stloc)
                return new CodeInstruction(OpCodes.Ldloc, store.operand);
            throw new InvalidOperationException(
                "Unsupported Entanglement mesh-part local store.");
        }

        private static EntanglementRenderManagerGetter
            CreateRenderManagerGetter(MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "GetEntanglementRenderManager",
                typeof(object),
                Type.EmptyTypes,
                typeof(EntanglementEffectPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Call, getter);
            il.Emit(OpCodes.Ret);
            return (EntanglementRenderManagerGetter)method.CreateDelegate(
                typeof(EntanglementRenderManagerGetter));
        }

        private static EntanglementEffectGetter CreateEffectGetter(
            Type managerType,
            MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "GetEntanglementEffect",
                typeof(object),
                new Type[] { typeof(object), typeof(int) },
                typeof(EntanglementEffectPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, managerType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, getter);
            il.Emit(OpCodes.Ret);
            return (EntanglementEffectGetter)method.CreateDelegate(
                typeof(EntanglementEffectGetter));
        }

        private static EntanglementEffectRegistrar CreateEffectRegistrar(
            Type managerType,
            MethodInfo registrar)
        {
            DynamicMethod method = new DynamicMethod(
                "RegisterEntanglementEffect",
                typeof(void),
                new Type[] { typeof(object), typeof(object) },
                typeof(EntanglementEffectPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, managerType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, registrar.GetParameters()[0].ParameterType);
            il.Emit(OpCodes.Callvirt, registrar);
            if (registrar.ReturnType != typeof(void))
                il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            return (EntanglementEffectRegistrar)method.CreateDelegate(
                typeof(EntanglementEffectRegistrar));
        }

        private static EntanglementEffectFactory CreateEffectFactory(
            Type graphicsDevice,
            Type contentManager,
            ConstructorInfo constructor)
        {
            DynamicMethod method = new DynamicMethod(
                "CreateEntanglementEffect",
                typeof(object),
                new Type[] { typeof(object), typeof(object) },
                typeof(EntanglementEffectPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, graphicsDevice);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, contentManager);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Ret);
            return (EntanglementEffectFactory)method.CreateDelegate(
                typeof(EntanglementEffectFactory));
        }

        private static EntanglementDiffuseMapGetter CreateDiffuseMapGetter(
            Type meshPart,
            MethodInfo effectGetter,
            Type skinnedEffect,
            MethodInfo mapGetter)
        {
            DynamicMethod method = new DynamicMethod(
                "GetEntanglementDiffuseMap",
                typeof(object),
                new Type[] { typeof(object) },
                typeof(EntanglementEffectPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, meshPart);
            il.Emit(OpCodes.Callvirt, effectGetter);
            il.Emit(OpCodes.Castclass, skinnedEffect);
            il.Emit(OpCodes.Callvirt, mapGetter);
            il.Emit(OpCodes.Ret);
            return (EntanglementDiffuseMapGetter)method.CreateDelegate(
                typeof(EntanglementDiffuseMapGetter));
        }

        private static EntanglementDiffuseMapSetter CreateDiffuseMapSetter(
            Type effectType,
            Type textureType,
            MethodInfo setter)
        {
            DynamicMethod method = new DynamicMethod(
                "SetEntanglementDiffuseMap",
                typeof(void),
                new Type[] { typeof(object), typeof(object) },
                typeof(EntanglementEffectPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, effectType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, textureType);
            il.Emit(OpCodes.Callvirt, setter);
            il.Emit(OpCodes.Ret);
            return (EntanglementDiffuseMapSetter)method.CreateDelegate(
                typeof(EntanglementDiffuseMapSetter));
        }
    }
}
