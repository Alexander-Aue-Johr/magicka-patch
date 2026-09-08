using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate object AnimationClipGetter(object action);

    internal delegate bool AnimationDictionaryLookup<TDictionary, TValue>(
        TDictionary dictionary,
        string key,
        out TValue value);

    public static class AnimationClipCompatibilityPatch
    {
        private static Type actionType;
        private static Type animationActionType;
        private static Type animationsType;
        private static Type clipType;
        private static ConstructorInfo actionConstructor;
        private static AnimationClipGetter getClip;
        private static AnimationEntityState characterState;
        private static AnimationEntityState animatedState;
        private static PropertyInfo animationSpeedProperty;
        private static PropertyInfo blendTimeProperty;
        private static PropertyInfo loopAnimationProperty;
        private static readonly Hashtable dictionaryLookups = new Hashtable();

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "AnimationClipAction missing-key recovery",
                "org.magickacommunitypatch.animation-clip-constructor",
                FindConstructor,
                typeof(AnimationClipCompatibilityPatch).GetMethod(
                    "ConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition CharacterTemplateDefinition =
            RuntimePatchDefinition.Transpile(
                "CharacterTemplate invalid animation-slot filter",
                "org.magickacommunitypatch.character-template-animation-clip",
                assembly => FindRead(assembly, "CharacterTemplate"),
                typeof(AnimationClipCompatibilityPatch).GetMethod(
                    "TemplateReadTranspiler"));

        internal static readonly RuntimePatchDefinition PhysicsTemplateDefinition =
            RuntimePatchDefinition.Transpile(
                "PhysicsEntityTemplate invalid animation-slot filter",
                "org.magickacommunitypatch.physics-template-animation-clip",
                assembly => FindRead(assembly, "PhysicsEntityTemplate"),
                typeof(AnimationClipCompatibilityPatch).GetMethod(
                    "TemplateReadTranspiler"));

        internal static readonly RuntimePatchDefinition CharacterInitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "Character missing-idle initialization recovery",
                "org.magickacommunitypatch.character-initialize-animation-clip",
                FindCharacterInitialize,
                typeof(AnimationClipCompatibilityPatch).GetMethod(
                    "CharacterInitializeTranspiler"));

        internal static readonly RuntimePatchDefinition CharacterGoToDefinition =
            RuntimePatchDefinition.Prefix(
                "Character missing-idle crossfade recovery",
                "org.magickacommunitypatch.character-goto-animation-clip",
                assembly => FindAnimationMethod(assembly, "Character", "GoToAnimation"),
                target => CreateAnimationPrefix(target, "CharacterGoToPrefix"));

        internal static readonly RuntimePatchDefinition CharacterForceDefinition =
            RuntimePatchDefinition.Prefix(
                "Character missing-idle force recovery",
                "org.magickacommunitypatch.character-force-animation-clip",
                assembly => FindAnimationMethod(assembly, "Character", "ForceAnimation"),
                target => CreateAnimationPrefix(target, "CharacterForcePrefix"));

        internal static readonly RuntimePatchDefinition AnimatedGoToDefinition =
            RuntimePatchDefinition.Prefix(
                "AnimatedPhysicsEntity idle crossfade fallback",
                "org.magickacommunitypatch.animated-physics-goto-animation-clip",
                assembly => FindAnimationMethod(
                    assembly,
                    "AnimatedPhysicsEntity",
                    "GoToAnimation"),
                target => CreateAnimationPrefix(target, "AnimatedGoToPrefix"));

        internal static readonly RuntimePatchDefinition AnimatedForceDefinition =
            RuntimePatchDefinition.Prefix(
                "AnimatedPhysicsEntity idle force fallback",
                "org.magickacommunitypatch.animated-physics-force-animation-clip",
                assembly => FindAnimationMethod(
                    assembly,
                    "AnimatedPhysicsEntity",
                    "ForceAnimation"),
                target => CreateAnimationPrefix(target, "AnimatedForcePrefix"));

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Configure(assembly);
            return actionConstructor;
        }

        private static bool IsActionConstructor(ConstructorInfo constructor)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            return parameters.Length == 4 &&
                parameters[0].ParameterType.IsEnum &&
                parameters[1].ParameterType.FullName ==
                    "Microsoft.Xna.Framework.Content.ContentReader" &&
                parameters[2].ParameterType.FullName ==
                    "XNAnimation.AnimationClipDictionary" &&
                parameters[3].ParameterType.FullName ==
                    "XNAnimation.SkinnedModelBoneCollection";
        }

        private static MethodInfo FindRead(Assembly assembly, string typeName)
        {
            Configure(assembly);
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities." + typeName,
                true);
            MethodInfo[] matches = Array.FindAll(
                type.GetMethods(
                    BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "Read" &&
                    method.ReturnType == type &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.FullName ==
                        "Microsoft.Xna.Framework.Content.ContentReader");
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one " + typeName + ".Read method, found " +
                    matches.Length + ".");
            return matches[0];
        }

        private static MethodInfo FindCharacterInitialize(Assembly assembly)
        {
            Configure(assembly);
            Type character = assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            MethodInfo[] matches = Array.FindAll(
                character.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "Initialize" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 4 &&
                    method.GetParameters()[0].ParameterType.FullName ==
                        "Magicka.GameLogic.Entities.CharacterTemplate" &&
                    method.GetParameters()[1].ParameterType == typeof(int));
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one Character.Initialize method, found " +
                    matches.Length + ".");
            return matches[0];
        }

        private static MethodInfo FindAnimationMethod(
            Assembly assembly,
            string typeName,
            string methodName)
        {
            Configure(assembly);
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities." + typeName,
                true);
            Type[] parameters = methodName == "GoToAnimation"
                ? new Type[] { animationsType, typeof(float) }
                : new Type[] { animationsType };
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, methodName);
            return method;
        }

        private static MethodInfo CreateAnimationPrefix(
            MethodInfo target,
            string methodName)
        {
            Type prefixType = typeof(AnimationMethodPrefix<>).MakeGenericType(
                target.GetParameters()[0].ParameterType);
            MethodInfo prefix = prefixType.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public);
            if (prefix == null)
                throw new MissingMethodException(prefixType.FullName, methodName);
            return prefix;
        }

        private static void Configure(Assembly assembly)
        {
            Type currentAction = assembly.GetType(
                "Magicka.GameLogic.Entities.AnimationClipAction",
                true);
            if (currentAction == actionType && getClip != null)
                return;

            actionType = currentAction;
            ConstructorInfo[] constructors = actionType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            ConstructorInfo[] matches = Array.FindAll(
                constructors,
                constructor => IsActionConstructor(constructor));
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one AnimationClipAction content constructor, found " +
                    matches.Length + ".");
            actionConstructor = matches[0];
            animationsType = actionConstructor.GetParameters()[0].ParameterType;
            animationActionType = assembly.GetType(
                "Magicka.GameLogic.Entities.AnimationActions.AnimationAction",
                true);
            PropertyInfo clip = actionType.GetProperty(
                "Clip",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (clip == null || clip.GetIndexParameters().Length != 0)
                throw new MissingMemberException(actionType.FullName, "Clip");
            clipType = clip.PropertyType;
            getClip = CreateGetter(clip.GetGetMethod(true));
            animationSpeedProperty = RequireProperty(
                actionType,
                "AnimationSpeed",
                typeof(float));
            blendTimeProperty = RequireProperty(
                actionType,
                "BlendTime",
                typeof(float));
            loopAnimationProperty = RequireProperty(
                actionType,
                "LoopAnimation",
                typeof(bool));
            characterState = new AnimationEntityState(
                assembly.GetType("Magicka.GameLogic.Entities.Character", true),
                true,
                clipType);
            animatedState = new AnimationEntityState(
                assembly.GetType(
                    "Magicka.GameLogic.Entities.AnimatedPhysicsEntity",
                    true),
                false,
                clipType);
        }

        public static bool HandleGoToAnimation(
            object instance,
            object animation,
            float blendFallback,
            bool character)
        {
            AnimationEntityState state = character
                ? characterState
                : animatedState;
            int requested = Convert.ToInt32(animation);
            if (FindRequestedAction(instance, state, requested) != null)
                return true;

            object controller = state.Controller.GetValue(instance);
            int current = Convert.ToInt32(
                state.CurrentAnimation.GetValue(instance));
            bool finished = controller != null &&
                (bool)state.HasFinished.GetValue(controller, null);
            if (current == requested && !finished)
                return false;

            SetIdleState(instance, state);
            object idle = GetAction(
                state.AnimationSets.GetValue(instance) as Array,
                0,
                1);
            if (!IsUsable(idle) || controller == null)
                return false;

            state.ClipSpeed.SetValue(
                controller,
                animationSpeedProperty.GetValue(idle, null),
                null);
            float blend = (float)blendTimeProperty.GetValue(idle, null);
            bool loop = !character &&
                (bool)loopAnimationProperty.GetValue(idle, null);
            state.CrossFade.Invoke(
                controller,
                new object[]
                {
                    getClip(idle),
                    blend > 0f ? blend : blendFallback,
                    loop
                });
            return false;
        }

        public static bool HandleForceAnimation(
            object instance,
            object animation,
            bool character)
        {
            AnimationEntityState state = character
                ? characterState
                : animatedState;
            int requested = Convert.ToInt32(animation);
            if (FindRequestedAction(instance, state, requested) != null)
                return true;

            object controller = state.Controller.GetValue(instance);
            if (controller != null)
                state.ClearCrossfadeQueue.Invoke(controller, null);
            SetIdleState(instance, state);
            object idle = GetAction(
                state.AnimationSets.GetValue(instance) as Array,
                0,
                1);
            if (!IsUsable(idle) || controller == null)
                return false;

            state.ClipSpeed.SetValue(
                controller,
                animationSpeedProperty.GetValue(idle, null),
                null);
            state.StartClip.Invoke(
                controller,
                new object[]
                {
                    getClip(idle),
                    loopAnimationProperty.GetValue(idle, null)
                });
            state.OnCrossfadeFinished.Invoke(instance, null);
            return false;
        }

        private static object FindRequestedAction(
            object instance,
            AnimationEntityState state,
            int animation)
        {
            Array sets = state.AnimationSets.GetValue(instance) as Array;
            object action = GetAction(sets, 0, animation);
            if (IsUsable(action))
                return action;
            if (state.Equipment == null)
                return null;

            Array equipment = state.Equipment.GetValue(instance) as Array;
            if (equipment == null)
                return null;
            for (int index = 0; index < equipment.Length; index++)
            {
                object attachment = equipment.GetValue(index);
                if (attachment == null)
                    continue;
                object item = state.AttachmentItem.GetValue(attachment, null);
                if (item == null)
                    continue;
                int weaponClass = Convert.ToInt32(
                    state.ItemWeaponClass.GetValue(item, null));
                if (weaponClass == state.StaffWeaponClass)
                    continue;
                action = GetAction(sets, weaponClass, animation);
                if (IsUsable(action))
                    return action;
            }
            return null;
        }

        private static object GetAction(
            Array animationSets,
            int animationSet,
            int animation)
        {
            return TryGetAnimationAction(
                animationSets,
                animationSet,
                animation);
        }

        private static bool IsUsable(object action)
        {
            return action != null && getClip(action) != null;
        }

        private static void SetIdleState(
            object instance,
            AnimationEntityState state)
        {
            state.CurrentAnimation.SetValue(
                instance,
                Enum.ToObject(state.CurrentAnimation.FieldType, 1));
            state.CurrentSet.SetValue(
                instance,
                Enum.ToObject(state.CurrentSet.FieldType, 0));
        }

        public static TValue GetValueOrDefault<TDictionary, TValue>(
            TDictionary dictionary,
            string key)
        {
            if ((object)dictionary == null)
                return default(TValue);
            TValue value;
            AnimationDictionaryLookup<TDictionary, TValue> lookup =
                (AnimationDictionaryLookup<TDictionary, TValue>)
                    GetDictionaryLookup(
                        typeof(TDictionary),
                        typeof(TValue));
            return lookup(dictionary, key, out value)
                    ? value
                    : default(TValue);
        }

        private static Delegate GetDictionaryLookup(
            Type dictionaryType,
            Type valueType)
        {
            string key = dictionaryType.AssemblyQualifiedName + "|" +
                valueType.AssemblyQualifiedName;
            lock (dictionaryLookups.SyncRoot)
            {
                Delegate lookup = dictionaryLookups[key] as Delegate;
                if (lookup != null)
                    return lookup;
                lookup = CreateDictionaryLookup(dictionaryType, valueType);
                dictionaryLookups[key] = lookup;
                return lookup;
            }
        }

        private static Delegate CreateDictionaryLookup(
            Type dictionaryType,
            Type valueType)
        {
            MethodInfo method = dictionaryType.GetMethod(
                "TryGetValue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[]
                {
                    typeof(string),
                    valueType.MakeByRefType()
                },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(
                    dictionaryType.FullName,
                    "TryGetValue");

            DynamicMethod lookup = new DynamicMethod(
                "TryGetAnimationClip",
                typeof(bool),
                new Type[]
                {
                    dictionaryType,
                    typeof(string),
                    valueType.MakeByRefType()
                },
                typeof(AnimationClipCompatibilityPatch),
                true);
            ILGenerator il = lookup.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, method);
            il.Emit(OpCodes.Ret);
            Type delegateType = typeof(AnimationDictionaryLookup<,>)
                .MakeGenericType(dictionaryType, valueType);
            return lookup.CreateDelegate(delegateType);
        }

        public static void StoreIfUsable<TAction>(
            TAction[] actions,
            int index,
            TAction action)
            where TAction : class
        {
            if (actions != null && index >= 0 && index < actions.Length &&
                action != null && getClip(action) != null)
                actions[index] = action;
        }

        public static TAction TryGetAnimationAction<TAction>(
            TAction[][] animationSets,
            int animationSet,
            int animation)
            where TAction : class
        {
            if (animationSets == null || animationSet < 0 ||
                animationSet >= animationSets.Length)
                return null;
            TAction[] actions = animationSets[animationSet];
            if (actions == null || animation < 0 || animation >= actions.Length)
                return null;
            return actions[animation];
        }

        public static object TryGetAnimationAction(
            Array animationSets,
            int animationSet,
            int animation)
        {
            if (animationSets == null || animationSet < 0 ||
                animationSet >= animationSets.Length)
                return null;
            Array actions = animationSets.GetValue(animationSet) as Array;
            if (actions == null || animation < 0 || animation >= actions.Length)
                return null;
            return actions.GetValue(animation);
        }

        public static IEnumerable<CodeInstruction> ConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode != OpCodes.Call &&
                        result[index].opcode != OpCodes.Callvirt) ||
                    called == null || called.Name != "get_Item" ||
                    called.ReturnType != clipType)
                    continue;
                ParameterInfo[] parameters = called.GetParameters();
                if (parameters.Length != 1 ||
                    parameters[0].ParameterType != typeof(string))
                    continue;
                MethodInfo safeLookup = typeof(AnimationClipCompatibilityPatch)
                    .GetMethod("GetValueOrDefault")
                    .MakeGenericMethod(called.DeclaringType, clipType);
                result[index].opcode = OpCodes.Call;
                result[index].operand = safeLookup;
                replaced++;
            }
            if (replaced != 1)
                throw new InvalidOperationException(
                    "Expected one animation dictionary lookup, found " +
                    replaced + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> TemplateReadTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            if (actionConstructor == null)
                throw new InvalidOperationException(
                    "AnimationClipAction constructor was not configured.");
            MethodInfo store = typeof(AnimationClipCompatibilityPatch)
                .GetMethod("StoreIfUsable")
                .MakeGenericMethod(actionType);
            int replaced = 0;
            for (int index = 0; index + 1 < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Newobj ||
                    !Object.Equals(result[index].operand, actionConstructor) ||
                    result[index + 1].opcode != OpCodes.Stelem_Ref)
                    continue;
                result[index + 1].opcode = OpCodes.Call;
                result[index + 1].operand = store;
                replaced++;
            }
            if (replaced != 1)
                throw new InvalidOperationException(
                    "Expected one animation action array store, found " +
                    replaced + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> CharacterInitializeTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            Type character = actionType.Assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            FieldInfo sets = RequireField(character, "mAnimationClips");
            FieldInfo currentActions = RequireField(character, "mCurrentActions");
            FieldInfo forceUpdate = RequireField(character, "mForceAnimationUpdate");
            List<int> lookups = ReplaceNestedLookups(result, sets);
            if (lookups.Count != 1)
                throw new InvalidOperationException(
                    "Expected one Character.Initialize animation lookup, found " +
                    lookups.Count + ".");
            int storeIndex = FindFollowingLocalStore(result, lookups[0]);
            int continuation = FindFieldStore(result, storeIndex, "mNextAttackAnimation");
            Label valid = generator.DefineLabel();
            Label resume = generator.DefineLabel();
            result[storeIndex + 1].labels.Add(valid);
            result[continuation].labels.Add(resume);
            CodeInstruction actionLoad = LoadFromStore(result[storeIndex]);
            List<CodeInstruction> guard = new List<CodeInstruction>();
            guard.Add(actionLoad);
            guard.Add(new CodeInstruction(OpCodes.Brtrue, valid));
            guard.Add(new CodeInstruction(OpCodes.Ldarg_0));
            guard.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
            guard.Add(new CodeInstruction(OpCodes.Newarr, animationActionType));
            guard.Add(new CodeInstruction(OpCodes.Stfld, currentActions));
            guard.Add(new CodeInstruction(OpCodes.Ldarg_0));
            guard.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
            guard.Add(new CodeInstruction(OpCodes.Stfld, forceUpdate));
            guard.Add(new CodeInstruction(OpCodes.Br, resume));
            result.InsertRange(storeIndex + 1, guard);
            return result;
        }

        private static List<int> ReplaceNestedLookups(
            List<CodeInstruction> instructions,
            FieldInfo setsField)
        {
            MethodInfo[] candidates = Array.FindAll(
                typeof(AnimationClipCompatibilityPatch).GetMethods(
                    BindingFlags.Static | BindingFlags.Public),
                method => method.Name == "TryGetAnimationAction" &&
                    method.IsGenericMethodDefinition);
            if (candidates.Length != 1)
                throw new InvalidOperationException(
                    "Generic animation lookup helper was not found.");
            MethodInfo safe = candidates[0].MakeGenericMethod(actionType);
            List<int> replaced = new List<int>();
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(instructions[index].operand, setsField))
                    continue;
                int outer = FindOpcode(instructions, index + 1, index + 6, OpCodes.Ldelem_Ref);
                if (outer < 0)
                    continue;
                int inner = FindOpcode(instructions, outer + 1, outer + 6, OpCodes.Ldelem_Ref);
                if (inner < 0 || ContainsControlFlow(instructions, outer + 1, inner))
                    continue;
                instructions[outer].opcode = OpCodes.Nop;
                instructions[outer].operand = null;
                instructions[inner].opcode = OpCodes.Call;
                instructions[inner].operand = safe;
                replaced.Add(inner);
            }
            return replaced;
        }

        private static int FindFollowingLocalStore(
            IList<CodeInstruction> instructions,
            int start)
        {
            for (int index = start + 1;
                index < instructions.Count && index < start + 5;
                index++)
            {
                if (IsLocalStore(instructions[index]))
                    return index;
            }
            throw new InvalidOperationException(
                "Animation action local store was not found.");
        }

        private static int FindFieldStore(
            IList<CodeInstruction> instructions,
            int start,
            string fieldName)
        {
            for (int index = start + 1; index < instructions.Count; index++)
            {
                FieldInfo field = instructions[index].operand as FieldInfo;
                if (instructions[index].opcode == OpCodes.Stfld &&
                    field != null && field.Name == fieldName)
                    return Math.Max(0, index - 2);
            }
            throw new InvalidOperationException(
                "Field store " + fieldName + " was not found.");
        }

        private static int FindOpcode(
            IList<CodeInstruction> instructions,
            int start,
            int end,
            OpCode opcode)
        {
            for (int index = start;
                index < instructions.Count && index < end;
                index++)
            {
                if (instructions[index].opcode == opcode)
                    return index;
            }
            return -1;
        }

        private static bool ContainsControlFlow(
            IList<CodeInstruction> instructions,
            int start,
            int end)
        {
            for (int index = start; index < end; index++)
            {
                FlowControl flow = instructions[index].opcode.FlowControl;
                if (flow == FlowControl.Branch ||
                    flow == FlowControl.Cond_Branch ||
                    instructions[index].labels.Count != 0)
                    return true;
            }
            return false;
        }

        private static CodeInstruction LoadFromStore(CodeInstruction store)
        {
            int index = LocalIndex(store);
            if (index == 0)
                return new CodeInstruction(OpCodes.Ldloc_0);
            if (index == 1)
                return new CodeInstruction(OpCodes.Ldloc_1);
            if (index == 2)
                return new CodeInstruction(OpCodes.Ldloc_2);
            if (index == 3)
                return new CodeInstruction(OpCodes.Ldloc_3);
            return new CodeInstruction(OpCodes.Ldloc_S, store.operand);
        }

        private static bool IsLocalStore(CodeInstruction instruction)
        {
            OpCode opcode = instruction.opcode;
            return opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S ||
                opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 ||
                opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;
        }

        private static int LocalIndex(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Stloc_0)
                return 0;
            if (instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Stloc_1)
                return 1;
            if (instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Stloc_2)
                return 2;
            if (instruction.opcode == OpCodes.Ldloc_3 ||
                instruction.opcode == OpCodes.Stloc_3)
                return 3;
            LocalBuilder builder = instruction.operand as LocalBuilder;
            if (builder != null)
                return builder.LocalIndex;
            return Convert.ToInt32(instruction.operand);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static PropertyInfo RequireProperty(
            Type type,
            string name,
            Type propertyType)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || property.PropertyType != propertyType ||
                property.GetIndexParameters().Length != 0)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static AnimationClipGetter CreateGetter(MethodInfo getter)
        {
            if (getter == null)
                throw new MissingMethodException(actionType.FullName, "get_Clip");
            DynamicMethod method = new DynamicMethod(
                "GetAnimationClip",
                typeof(object),
                new Type[] { typeof(object) },
                typeof(AnimationClipCompatibilityPatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, actionType);
            il.Emit(OpCodes.Callvirt, getter);
            il.Emit(OpCodes.Ret);
            return (AnimationClipGetter)method.CreateDelegate(
                typeof(AnimationClipGetter));
        }
    }

    internal sealed class AnimationEntityState
    {
        internal readonly FieldInfo AnimationSets;
        internal readonly FieldInfo CurrentAnimation;
        internal readonly FieldInfo CurrentSet;
        internal readonly FieldInfo Controller;
        internal readonly FieldInfo Equipment;
        internal readonly PropertyInfo AttachmentItem;
        internal readonly PropertyInfo ItemWeaponClass;
        internal readonly int StaffWeaponClass;
        internal readonly PropertyInfo HasFinished;
        internal readonly PropertyInfo ClipSpeed;
        internal readonly MethodInfo ClearCrossfadeQueue;
        internal readonly MethodInfo CrossFade;
        internal readonly MethodInfo StartClip;
        internal readonly MethodInfo OnCrossfadeFinished;

        internal AnimationEntityState(
            Type entityType,
            bool includeEquipment,
            Type clipType)
        {
            AnimationSets = RequireField(entityType, "mAnimationClips");
            CurrentAnimation = RequireField(entityType, "mCurrentAnimation");
            CurrentSet = RequireField(entityType, "mCurrentAnimationSet");
            Controller = RequireField(entityType, "mAnimationController");
            Type controllerType = Controller.FieldType;
            HasFinished = RequireProperty(
                controllerType,
                "HasFinished",
                typeof(bool),
                false);
            ClipSpeed = RequireProperty(
                controllerType,
                "ClipSpeed",
                typeof(float),
                true);
            ClearCrossfadeQueue = RequireMethod(
                controllerType,
                "ClearCrossfadeQueue",
                Type.EmptyTypes);
            CrossFade = RequireMethod(
                controllerType,
                "CrossFade",
                new Type[] { clipType, typeof(float), typeof(bool) });
            StartClip = RequireMethod(
                controllerType,
                "StartClip",
                new Type[] { clipType, typeof(bool) });
            OnCrossfadeFinished = RequireMethod(
                entityType,
                "OnCrossfadeFinished",
                Type.EmptyTypes);

            if (!includeEquipment)
                return;
            Equipment = RequireField(entityType, "mEquipment");
            Type attachmentType = Equipment.FieldType.GetElementType();
            AttachmentItem = RequireProperty(
                attachmentType,
                "Item",
                null,
                false);
            Type itemType = AttachmentItem.PropertyType;
            ItemWeaponClass = RequireProperty(
                itemType,
                "WeaponClass",
                null,
                false);
            StaffWeaponClass = Convert.ToInt32(
                Enum.Parse(ItemWeaponClass.PropertyType, "Staff", true));
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static PropertyInfo RequireProperty(
            Type type,
            string name,
            Type propertyType,
            bool requireSetter)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || property.GetIndexParameters().Length != 0 ||
                (propertyType != null && property.PropertyType != propertyType) ||
                property.GetGetMethod(true) == null ||
                (requireSetter && property.GetSetMethod(true) == null))
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    parameters,
                    null);
                if (method != null && method.ReturnType == typeof(void))
                    return method;
            }
            throw new MissingMethodException(type.FullName, name);
        }
    }

    public static class AnimationMethodPrefix<TAnimation>
    {
        public static bool CharacterGoToPrefix(
            object __instance,
            TAnimation iAnimation,
            float iTime)
        {
            return AnimationClipCompatibilityPatch.HandleGoToAnimation(
                __instance,
                iAnimation,
                iTime,
                true);
        }

        public static bool CharacterForcePrefix(
            object __instance,
            TAnimation iAnimation)
        {
            return AnimationClipCompatibilityPatch.HandleForceAnimation(
                __instance,
                iAnimation,
                true);
        }

        public static bool AnimatedGoToPrefix(
            object __instance,
            TAnimation iAnimation,
            float iTime)
        {
            return AnimationClipCompatibilityPatch.HandleGoToAnimation(
                __instance,
                iAnimation,
                iTime,
                false);
        }

        public static bool AnimatedForcePrefix(
            object __instance,
            TAnimation iAnimation)
        {
            return AnimationClipCompatibilityPatch.HandleForceAnimation(
                __instance,
                iAnimation,
                false);
        }
    }
}
