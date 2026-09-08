using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate Array ObjectArrayGetter(object instance);

    internal delegate void ObjectArraySetter(object instance, Array value);

    internal delegate int ObjectIntGetter(object instance);

    internal delegate void ObjectIntSetter(object instance, int value);

    internal static class StaticCollectionAccessors
    {
        internal static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field == null)
                    continue;
                if (field.FieldType != fieldType)
                    throw new MissingFieldException(type.FullName, name);
                return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
    }

    internal sealed class StaticCollectionState
    {
        private static readonly Hashtable sStates = new Hashtable();

        internal readonly ObjectArrayGetter GetObjects;
        internal readonly ObjectArraySetter SetObjects;
        internal readonly ObjectIntGetter GetCount;
        internal readonly ObjectIntSetter SetCount;

        private StaticCollectionState(Type collectionType)
        {
            Type objectArrayType =
                collectionType.GetGenericTypeDefinition().FullName ==
                    "Magicka.StaticWeakList`1"
                ? typeof(WeakReference[])
                : collectionType.GetGenericArguments()[0].MakeArrayType();
            FieldInfo objects = StaticCollectionAccessors.RequireField(
                collectionType,
                "mObjects",
                objectArrayType);
            FieldInfo count = StaticCollectionAccessors.RequireField(
                collectionType,
                "mCount",
                typeof(int));
            GetObjects = CreateArrayGetter(objects);
            SetObjects = CreateArraySetter(objects);
            GetCount = CreateIntGetter(count);
            SetCount = CreateIntSetter(count);
        }

        internal static StaticCollectionState For(
            object instance,
            string genericTypeName)
        {
            Type collectionType = FindCollectionType(
                instance.GetType(),
                genericTypeName);
            lock (sStates)
            {
                StaticCollectionState state =
                    (StaticCollectionState)sStates[collectionType];
                if (state == null)
                {
                    state = new StaticCollectionState(collectionType);
                    sStates.Add(collectionType, state);
                }
                return state;
            }
        }

        private static Type FindCollectionType(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType &&
                    current.GetGenericTypeDefinition().FullName == name)
                    return current;
            }
            throw new InvalidOperationException(
                type.FullName + " does not derive from " + name + ".");
        }

        private static ObjectArrayGetter CreateArrayGetter(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod(
                "Get" + field.Name + "Array",
                typeof(Array),
                new Type[] { typeof(object) },
                typeof(StaticCollectionState),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (ObjectArrayGetter)method.CreateDelegate(
                typeof(ObjectArrayGetter));
        }

        private static ObjectArraySetter CreateArraySetter(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod(
                "Set" + field.Name + "Array",
                typeof(void),
                new Type[] { typeof(object), typeof(Array) },
                typeof(StaticCollectionState),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, field.FieldType);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            return (ObjectArraySetter)method.CreateDelegate(
                typeof(ObjectArraySetter));
        }

        private static ObjectIntGetter CreateIntGetter(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod(
                "Get" + field.Name + "Int32",
                typeof(int),
                new Type[] { typeof(object) },
                typeof(StaticCollectionState),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (ObjectIntGetter)method.CreateDelegate(
                typeof(ObjectIntGetter));
        }

        private static ObjectIntSetter CreateIntSetter(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod(
                "Set" + field.Name + "Int32",
                typeof(void),
                new Type[] { typeof(object), typeof(int) },
                typeof(StaticCollectionState),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            return (ObjectIntSetter)method.CreateDelegate(
                typeof(ObjectIntSetter));
        }
    }
}
