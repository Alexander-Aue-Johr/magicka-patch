using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Harmony;
using Harmony.ILCopying;

namespace Magicka.CommunityPatch.Runtime
{
    public static class SharedContentLifetimePatch
    {
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object left, object right)
            {
                return Object.ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        private sealed class ManagerState
        {
            internal readonly Dictionary<object, List<WeakReference>> Disposables =
                new Dictionary<object, List<WeakReference>>(
                    new ReferenceComparer());
        }

        private sealed class DisposableRecorder
        {
            private readonly List<WeakReference> values;

            internal DisposableRecorder(List<WeakReference> trackedValues)
            {
                values = trackedValues;
            }

            internal void Record(IDisposable value)
            {
                if (value != null)
                    values.Add(new WeakReference(value));
            }
        }

        private static Type commonType;
        private static Type referencedAssetType;
        private static FieldInfo referencesField;
        private static FieldInfo loadStackField;
        private static FieldInfo assetField;
        private static FieldInfo referenceCountField;
        private static FieldInfo childrenField;
        private static FieldInfo commonField;
        private static ConstructorInfo referencedAssetConstructor;
        private static MethodInfo cleanPathMethod;
        private static MethodInfo stackPushMethod;
        private static MethodInfo stackPopMethod;
        private static MethodInfo stackPeekMethod;
        private static PropertyInfo stackCountProperty;
        private static MethodInfo readAssetDefinition;
        private static readonly Dictionary<object, ManagerState> States =
            new Dictionary<object, ManagerState>(new ReferenceComparer());

        internal static readonly RuntimePatchDefinition ReleaseDefinition =
            RuntimePatchDefinition.Postfix(
                "Shared content disposable release",
                "org.magickacommunitypatch.shared-content-release",
                FindDec,
                CreateReleasePostfix);

        internal static readonly RuntimePatchDefinition FinalDefinition =
            RuntimePatchDefinition.Transpile(
                "Shared content final disposable release",
                "org.magickacommunitypatch.shared-content-final",
                FindOuterDispose,
                typeof(SharedContentLifetimePatch).GetMethod(
                    "FinalTranspiler"));

        private static void ResolveContracts(Assembly assembly)
        {
            if (commonType != null && commonType.Assembly == assembly)
                return;
            Type outer = assembly.GetType("Magicka.SharedContentManager", true);
            commonType = assembly.GetType(
                "Magicka.SharedContentManager+CommonContentManager", true);
            referencedAssetType = assembly.GetType(
                "Magicka.SharedContentManager+CommonContentManager+ReferencedAsset",
                true);
            referencesField = RequireField(commonType, "mReferences");
            loadStackField = RequireField(commonType, "mLoadStack");
            assetField = RequireField(referencedAssetType, "Asset");
            referenceCountField = RequireField(referencedAssetType, "References");
            childrenField = RequireField(referencedAssetType, "Children");
            commonField = RequireField(outer, "sCommon");
            referencedAssetConstructor = referencedAssetType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null, new Type[] { typeof(string) }, null);
            cleanPathMethod = outer.GetMethod("GetCleanPath",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new Type[] { typeof(string) }, null);
            Type stackType = loadStackField.FieldType;
            stackPushMethod = stackType.GetMethod("Push",
                new Type[] { referencedAssetType });
            stackPopMethod = stackType.GetMethod("Pop", Type.EmptyTypes);
            stackPeekMethod = stackType.GetMethod("Peek", Type.EmptyTypes);
            stackCountProperty = stackType.GetProperty("Count");
            readAssetDefinition = FindReadAsset(commonType);
            if (referencedAssetConstructor == null || cleanPathMethod == null ||
                stackPushMethod == null || stackPopMethod == null ||
                stackPeekMethod == null || stackCountProperty == null ||
                readAssetDefinition == null)
                throw new MissingMemberException(commonType.FullName,
                    "shared-content load contract");
        }

        internal static IList<RuntimePatchDefinition> CreateLoadDefinitions(
            Assembly assembly)
        {
            ResolveContracts(assembly);
            MethodInfo load = FindLoadDefinition();
            List<Type> types = FindLoadedTypes(assembly);
            List<RuntimePatchDefinition> definitions =
                new List<RuntimePatchDefinition>(types.Count);
            for (int index = 0; index < types.Count; index++)
            {
                Type loadedType = types[index];
                MethodInfo closed = load.MakeGenericMethod(loadedType);
                MethodInfo prefix = typeof(SharedContentLifetimePatch)
                    .GetMethod("LoadPrefix").MakeGenericMethod(loadedType);
                string suffix = loadedType.FullName.Replace('+', '.');
                definitions.Add(RuntimePatchDefinition.Prefix(
                    "Shared content disposable ownership: " + suffix,
                    "org.magickacommunitypatch.shared-content-load." +
                        suffix.ToLowerInvariant(),
                    targetAssembly => closed,
                    target => prefix));
            }
            return definitions;
        }

        private static MethodInfo FindLoadDefinition()
        {
            MethodInfo[] methods = commonType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "Load" &&
                    methods[index].IsGenericMethodDefinition &&
                    methods[index].GetParameters().Length == 1)
                    return methods[index];
            throw new MissingMethodException(commonType.FullName, "Load<T>");
        }

        private static MethodInfo FindDec(Assembly assembly)
        {
            ResolveContracts(assembly);
            return commonType.GetMethod("Dec",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new Type[] { referencedAssetType, typeof(int) }, null);
        }

        private static MethodInfo FindOuterDispose(Assembly assembly)
        {
            ResolveContracts(assembly);
            Type outer = assembly.GetType("Magicka.SharedContentManager", true);
            return outer.GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null, new Type[] { typeof(bool) }, null);
        }

        private static List<Type> FindLoadedTypes(Assembly assembly)
        {
            Dictionary<Type, bool> found = new Dictionary<Type, bool>();
            Type[] types = assembly.GetTypes();
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                List<MethodBase> methods = new List<MethodBase>();
                try
                {
                    methods.AddRange(types[typeIndex].GetMethods(
                        BindingFlags.Static | BindingFlags.Instance |
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly));
                    methods.AddRange(types[typeIndex].GetConstructors(
                        BindingFlags.Static | BindingFlags.Instance |
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly));
                }
                catch
                {
                    continue;
                }
                for (int methodIndex = 0; methodIndex < methods.Count;
                    methodIndex++)
                    CollectLoadedTypes(methods[methodIndex], found);
            }
            List<Type> result = new List<Type>(found.Keys);
            result.Sort(delegate(Type left, Type right)
            {
                return String.CompareOrdinal(left.FullName, right.FullName);
            });
            if (result.Count == 0)
                throw new InvalidOperationException(
                    "No concrete shared-content Load types were found.");
            return result;
        }

        private static void CollectLoadedTypes(
            MethodBase method, IDictionary<Type, bool> found)
        {
            if (method == null || method.ContainsGenericParameters ||
                method.GetMethodBody() == null)
                return;
            try
            {
                DynamicMethod target = new DynamicMethod(
                    "ReadSharedContentCalls", typeof(void), Type.EmptyTypes,
                    typeof(SharedContentLifetimePatch), true);
                List<ILInstruction> instructions =
                    MethodBodyReader.GetInstructions(
                        target.GetILGenerator(), method);
                for (int index = 0; index < instructions.Count; index++)
                {
                    MethodInfo called = instructions[index].operand
                        as MethodInfo;
                    if (called == null || called.Name != "Load" ||
                        !called.IsGenericMethod ||
                        called.ContainsGenericParameters ||
                        called.GetParameters().Length != 1 ||
                        called.GetParameters()[0].ParameterType != typeof(string))
                        continue;
                    Type declaring = called.DeclaringType;
                    if (declaring.FullName !=
                            "Microsoft.Xna.Framework.Content.ContentManager" &&
                        declaring.FullName != "Magicka.SharedContentManager" &&
                        declaring != commonType)
                        continue;
                    Type loaded = called.GetGenericArguments()[0];
                    if (!loaded.ContainsGenericParameters)
                        found[loaded] = true;
                }
            }
            catch
            {
            }
        }

        private static MethodInfo CreateReleasePostfix(MethodInfo target)
        {
            return typeof(SharedContentLifetimePatch).GetMethod(
                "ReleasePostfix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        public static bool LoadPrefix<T>(
            object __instance, string assetName, ref T __result)
        {
            assetName = (string)cleanPathMethod.Invoke(
                null, new object[] { assetName });
            IDictionary references = referencesField.GetValue(__instance)
                as IDictionary;
            object referencedAsset = references[assetName];
            object stack = loadStackField.GetValue(__instance);
            if (referencedAsset == null)
            {
                referencedAsset = referencedAssetConstructor.Invoke(
                    new object[] { assetName });
                stackPushMethod.Invoke(stack,
                    new object[] { referencedAsset });
                try
                {
                    List<WeakReference> disposables =
                        GetDisposables(__instance, referencedAsset);
                    DisposableRecorder recorder =
                        new DisposableRecorder(disposables);
                    Action<IDisposable> record = recorder.Record;
                    MethodInfo read = readAssetDefinition.MakeGenericMethod(
                        typeof(T));
                    object asset;
                    try
                    {
                        asset = read.Invoke(__instance,
                            new object[] { assetName, record });
                    }
                    catch (TargetInvocationException exception)
                    {
                        DisposeAll(disposables);
                        RemoveDisposables(__instance, referencedAsset);
                        throw exception.InnerException;
                    }
                    assetField.SetValue(referencedAsset, asset);
                    RemoveAssetObjects(__instance, referencedAsset, disposables);
                }
                finally
                {
                    stackPopMethod.Invoke(stack, null);
                }
                if (!references.Contains(assetName))
                    references.Add(assetName, referencedAsset);
            }
            if (Convert.ToInt32(stackCountProperty.GetValue(stack, null)) > 0)
            {
                object parent = stackPeekMethod.Invoke(stack, null);
                IList children = childrenField.GetValue(parent) as IList;
                if (children != null && !children.Contains(referencedAsset))
                    children.Add(referencedAsset);
            }
            int count = Convert.ToInt32(
                referenceCountField.GetValue(referencedAsset));
            referenceCountField.SetValue(referencedAsset, count + 1);
            __result = (T)assetField.GetValue(referencedAsset);
            return false;
        }

        public static void ReleasePostfix<TReferencedAsset>(
            object __instance, TReferencedAsset iAsset)
        {
            if ((object)iAsset == null ||
                Convert.ToInt32(referenceCountField.GetValue(iAsset)) != 0)
                return;
            ManagerState state;
            if (!States.TryGetValue(__instance, out state))
                return;
            List<WeakReference> values;
            if (!state.Disposables.TryGetValue(iAsset, out values))
                return;
            DisposeUnregistered(__instance, values);
            state.Disposables.Remove(iAsset);
        }

        public static IEnumerable<CodeInstruction> FinalTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int dispose = -1;
            for (int index = 1; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    called != null && called.Name == "Dispose" &&
                    called.GetParameters().Length == 0 &&
                    result[index - 1].opcode == OpCodes.Ldsfld &&
                    Object.Equals(result[index - 1].operand, commonField))
                {
                    if (dispose >= 0)
                        throw new InvalidOperationException(
                            "Multiple final shared-content disposals found.");
                    dispose = index;
                }
            }
            if (dispose < 0)
                throw new InvalidOperationException(
                    "Final shared-content disposal changed.");
            CodeInstruction release = new CodeInstruction(
                OpCodes.Call,
                typeof(SharedContentLifetimePatch).GetMethod(
                    "ForceCurrentCommon"));
            release.labels.AddRange(result[dispose].labels);
            release.blocks.AddRange(result[dispose].blocks);
            result[dispose].labels.Clear();
            result[dispose].blocks.Clear();
            result.Insert(dispose, release);
            return result;
        }

        public static void ForceCurrentCommon()
        {
            object common = commonField.GetValue(null);
            if (common == null)
                return;
            ManagerState state;
            if (!States.TryGetValue(common, out state))
                return;
            foreach (KeyValuePair<object, List<WeakReference>> entry
                in state.Disposables)
                DisposeAll(entry.Value);
            state.Disposables.Clear();
            States.Remove(common);
        }

        private static List<WeakReference> GetDisposables(
            object manager, object referencedAsset)
        {
            ManagerState state;
            if (!States.TryGetValue(manager, out state))
            {
                state = new ManagerState();
                States.Add(manager, state);
            }
            List<WeakReference> values;
            if (!state.Disposables.TryGetValue(referencedAsset, out values))
            {
                values = new List<WeakReference>();
                state.Disposables.Add(referencedAsset, values);
            }
            return values;
        }

        private static void RemoveDisposables(
            object manager, object referencedAsset)
        {
            ManagerState state;
            if (!States.TryGetValue(manager, out state))
                return;
            state.Disposables.Remove(referencedAsset);
            if (state.Disposables.Count == 0)
                States.Remove(manager);
        }

        private static void RemoveAssetObjects(
            object manager,
            object referencedAsset,
            List<WeakReference> values)
        {
            object asset = assetField.GetValue(referencedAsset);
            IList children = childrenField.GetValue(referencedAsset) as IList;
            for (int index = values.Count - 1; index >= 0; index--)
            {
                object value = values[index].Target;
                if (value == null || Object.ReferenceEquals(value, asset) ||
                    IsChildAsset(children, value))
                    values.RemoveAt(index);
            }
        }

        private static bool IsChildAsset(IList children, object value)
        {
            if (children == null)
                return false;
            for (int index = 0; index < children.Count; index++)
                if (Object.ReferenceEquals(
                    assetField.GetValue(children[index]), value))
                    return true;
            return false;
        }

        private static void DisposeUnregistered(
            object manager, List<WeakReference> values)
        {
            IDictionary references = referencesField.GetValue(manager)
                as IDictionary;
            for (int index = 0; index < values.Count; index++)
            {
                object value = values[index].Target;
                if (value != null && !IsRegisteredAsset(references, value))
                    SafeDispose(value as IDisposable);
            }
        }

        private static bool IsRegisteredAsset(
            IDictionary references, object value)
        {
            if (references == null)
                return false;
            foreach (object referencedAsset in references.Values)
                if (Object.ReferenceEquals(
                    assetField.GetValue(referencedAsset), value))
                    return true;
            return false;
        }

        private static void DisposeAll(List<WeakReference> values)
        {
            for (int index = 0; index < values.Count; index++)
                SafeDispose(values[index].Target as IDisposable);
            values.Clear();
        }

        private static void SafeDispose(IDisposable value)
        {
            if (value == null)
                return;
            try
            {
                value.Dispose();
            }
            catch
            {
            }
        }

        private static MethodInfo FindReadAsset(Type type)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
            {
                MethodInfo[] methods = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int index = 0; index < methods.Length; index++)
                {
                    ParameterInfo[] parameters = methods[index].GetParameters();
                    if (methods[index].Name == "ReadAsset" &&
                        methods[index].IsGenericMethodDefinition &&
                        parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string))
                        return methods[index];
                }
            }
            return null;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
            {
                FieldInfo field = current.GetField(name,
                    BindingFlags.Static | BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
    }
}
