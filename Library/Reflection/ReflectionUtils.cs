using System;
using System.Collections.Generic;
using System.Reflection;

namespace LevelEditorPlugin.Library.Reflection;

public static class ReflectionUtils
{
    public static void CallPrivateMethod(Type type, object instance, string function, params object[] parameters)
    {
        MethodInfo method = FindMethod(type, function);
        if (method == null)
            throw new MissingMethodException(type?.FullName, function);
        method.Invoke(instance, parameters);
    }

    public static void SetPrivateField(Type type, object instance, string memberName, object newValue)
    {
        FieldInfo field = FindField(type, memberName);
        if (field == null)
            throw new MissingFieldException(type?.FullName, memberName);
        field.SetValue(instance, newValue);
    }

    public static object GetPrivateField(Type type, object instance, string memberName)
    {
        FieldInfo field = FindField(type, memberName);
        if (field == null)
            throw new MissingFieldException(type?.FullName, memberName);
        return field.GetValue(instance);
    }

    private static FieldInfo FindField(Type type, string memberName)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        while (type != null)
        {
            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
                return field;
            type = type.BaseType;
        }
        return null;
    }

    private static MethodInfo FindMethod(Type type, string methodName)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        while (type != null)
        {
            MethodInfo method = type.GetMethod(methodName, flags);
            if (method != null)
                return method;
            type = type.BaseType;
        }
        return null;
    }

    public static bool GetBoolProperty(object instance, params string[] names)
    {
        if (instance == null)
            return false;

        Type type = instance.GetType();
        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name);
            if (property?.GetValue(instance) is bool value)
                return value;
        }

        return false;
    }

    public static string AddStringToList(string value)
    {
        int hash = Frosty.Hash.Fnv1.HashString(value);
        IDictionary<int, string> strings = (IDictionary<int, string>)typeof(FrostySdk.Utils).GetField("strings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        if (!strings.ContainsKey(hash))
        {
            strings.Add(hash, value);
        }

        return value;
    }
}
