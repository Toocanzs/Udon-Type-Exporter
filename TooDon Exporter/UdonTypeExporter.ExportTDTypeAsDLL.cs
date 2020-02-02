using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TooDon;
using UnityEngine;

public class UdonTypeDLLExporter
{
    private class Field
    {
        public string FieldName;
        public Method GetMethod;
        public Method SetMethod;

        public override int GetHashCode()
        {
            return FieldName.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is Field other)
                return FieldName.Equals(other.FieldName);
            return false;
        }
    }

    private class Class
    {
        public StringBuilder Methods = new StringBuilder();
        public List<Class> Children = new List<Class>();
        public HashSet<Field> Fields = new HashSet<Field>();
        public string UdonName;
        public string TypeName;
        public bool IsExtentionClass;
        public TDType Type;

        public string GenerateCode()
        {
            StringBuilder s = new StringBuilder();
            if (TypeName == "System" || TypeName == "Collections")
            {
                s.Append("namespace ");
            }
            else
            {
                s.AppendLine($"\t[UdonType(\"{UdonName}\")]");
                s.Append($"public ");
                if (IsExtentionClass)
                    s.Append("static ");
                switch (TypeName)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                    case "Int64":
                    case "UInt64":
                    case "IntPtr":
                    case "UIntPtr":
                    case "Single":
                    case "Double":
                    case "Void":
                    case "Decimal":
                        s.Append("struct ");
                        break;
                    case "String":
                        s.Append("sealed class ");
                        break;
                    case "Array":
                        s.Append("abstract class ");
                        break;
                    case "IEnumerator":
                        s.Append("interface ");
                        break;
                    default:
                        s.Append("class ");
                        break;
                }
            }

            if (IsExtentionClass)
                s.Append($"{UdonName}Extensions");
            else
                s.Append($"{TypeName}");

            if (Type.IsGeneric && Type.GenericArguments.Count > 0)
            {
                s.Append("<");
                for (int i = 0; i < Type.GenericArguments.Count; i++)
                {
                    s.Append($"T{i}");
                    if (i != Type.GenericArguments.Count - 1)
                        s.Append(", ");
                }

                s.Append(">");
            }

            s.AppendLine();
            s.AppendLine("\t{");
            foreach (var field in Fields)
            {
                Method method = field.GetMethod ?? field.SetMethod;
                if (field.GetMethod != null)
                    s.AppendLine($"\t[UdonGetMethod(\"{field.GetMethod.FullUdonExternString}\")]");
                if (field.SetMethod != null)
                    s.AppendLine($"\t[UdonSetMethod(\"{field.SetMethod.FullUdonExternString}\")]");
                s.Append("\tpublic ");
                if (method.IsStatic)
                    s.Append("static ");
                s.Append($"extern {method.Output} {field.FieldName} {{");
                if (field.GetMethod != null)
                    s.Append("get; ");
                if (field.SetMethod != null)
                    s.Append("set; ");
                s.AppendLine("}");
            }

            s.AppendLine(Methods.ToString());
            foreach (var childClass in Children)
            {
                s.AppendLine(childClass.GenerateCode());
            }

            s.AppendLine("\t}");
            return s.ToString();
        }


        public override int GetHashCode()
        {
            return Methods.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is Class other)
            {
                return Methods.Equals(other.Methods) &&
                       Type.GenericArguments.Count == other.Type.GenericArguments.Count;
            }

            return false;
        }
    }

    //TODO: Enums
    public static string ExportTDTypeAsDLL(TDType root)
    {
        HashSet<Class> parentlessClasses = new HashSet<Class>();
        HashSet<Class> extenstionClasses = new HashSet<Class>();
        Dictionary<string, Class> fullNameToClass = new Dictionary<string, Class>();

        void WriteTypeCode(TDType type)
        {
            if (string.IsNullOrEmpty(type.FullName) || type.FullName.Contains("*"))
                return;
            var fullName = type.FullName.Replace("[]", "Array");
            var typeName = type.TypeName.Replace("[]", "Array");
            bool extensionClass = type.FullName.EndsWith("[]");
            string dictionaryKey = type.IsGeneric ? type.FullName + type.GenericArguments.Count : type.FullName;
            //Create or get class
            if (!fullNameToClass.TryGetValue(dictionaryKey, out var Class))
            {
                Class = new Class
                {
                    UdonName = type.UdonName,
                    TypeName = typeName,
                    IsExtentionClass = extensionClass,
                    Type = type
                };
                fullNameToClass[dictionaryKey] = Class;

                //Add new class to children
                if (string.IsNullOrEmpty(type.NamespaceName))
                {
                    parentlessClasses.Add(Class);
                }
                else
                {
                    if (fullNameToClass.TryGetValue(type.NamespaceName, out var Parent))
                    {
                        if (extensionClass)
                            extenstionClasses.Add(Class);
                        else
                            Parent.Children.Add(Class);
                    }
                    else
                    {
                        Debug.LogError($"Parent class not created {type.NamespaceName}");
                    }
                }
            }

            if (type.UdonName != null)
            {
                bool anyContainStar = false;
                foreach (var method in type.StaticMethods.Union(type.NonStaticMethods))
                {
                    if (method.FullUdonExternString.Contains("*"))
                        anyContainStar = true;
                }

                if (anyContainStar
                ) //Not going to handle the char* and sbyte*. Can't even create them in Udon anyway. Will likely be removed
                    return;
                AddMethods(type.StaticMethods, Class, extensionClass, type.FullName);
                AddMethods(type.NonStaticMethods, Class, extensionClass, type.FullName);
            }
        }
        
        void VisitType(TDType type)
        {
            WriteTypeCode(type);
            foreach (var child in type.Children)
            {
                VisitType(child);
            }
        }

        //Visit tree and build classes/namespaces
        VisitType(root);
        
        //Required for -nostdlib
        fullNameToClass["System.Collections"].Methods.AppendLine("internal interface IEnumerable { }");
        var sb = fullNameToClass["System"].Methods;
        sb.AppendLine("public abstract class ValueType { }");
        sb.AppendLine("public abstract class Enum : ValueType { }");
        sb.AppendLine("public class Attribute { }");
        sb.AppendLine("public abstract class Delegate { }");
        sb.AppendLine("public abstract class MulticastDelegate : Delegate { }");
        sb.AppendLine("internal struct IntPtr { }");
        sb.AppendLine("internal struct UIntPtr { }");
        sb.AppendLine("public struct RuntimeTypeHandle { }");
        sb.AppendLine("public struct RuntimeMethodHandle { }");
        sb.AppendLine("public struct RuntimeFieldHandle { }");
        sb.AppendLine("public interface IDisposable { }");
        sb.AppendLine("public sealed class ParamArrayAttribute : Attribute { }");

        //Write all namespaces out
        var fullCode = new StringBuilder();
        foreach (var Class in parentlessClasses)
        {
            fullCode.AppendLine(Class.GenerateCode());
        }

        foreach (var Class in extenstionClasses)
        {
            fullCode.AppendLine(Class.GenerateCode());
        }

        fullCode.AppendLine(attributeClassString);

        return fullCode.ToString();
    }

    private static void AddMethods(List<Method> methods, Class Class, bool extensionClass, string rawFullName)
    {
        foreach (var method in methods)
        {
            if (method.MethodName == "set")
                continue; //TODO:Handle
            if (method.MethodName != "ctor")
            {
                AddUdonMethod(Class, method, extensionClass, rawFullName);
            }
            else
            {
                if (!extensionClass)
                {
                    Class.Methods.AppendLine($"\t[UdonConstructor(\"{method.FullUdonExternString}\")]");
                    Class.Methods.Append($"\tpublic extern ");
                    Class.Methods.Append(method.Output.TypeName);
                    Class.Methods.Append("(");
                    AddInputs(Class, method);
                    Class.Methods.AppendLine(");");
                }
                else
                {
                    //Array constructors can be treated as an extension method
                    AddUdonMethod(Class, method, extensionClass, rawFullName, "array_");
                }
            }
        }
    }

    private static void AddUdonMethod(Class Class, Method method, bool extensionClass, string rawFullName,
        string prefix = "")
    {
        if (method.MethodType == "op" || method.MethodType == "remove" || method.MethodType == "add")
            return;
        if ((method.MethodType == "get" || method.MethodType == "set") && !Class.IsExtentionClass)
        {
            Field field;
            if (Class.Fields.Contains(new Field {FieldName = method.MethodName}))
            {
                field = Class.Fields.First(t => t.FieldName == method.MethodName);
            }
            else
            {
                field = new Field {FieldName = method.MethodName};
            }

            if (method.MethodType == "get")
                field.GetMethod = method;
            else
                field.SetMethod = method;

            Class.Fields.Add(field);
            return;
        }

        Class.Methods.AppendLine($"\t[UdonMethod(\"{method.FullUdonExternString}\")]");
        Class.Methods.Append($"\tpublic ");
        if (method.IsStatic || extensionClass)
            Class.Methods.Append("static ");
        var returnType = method.Output.ToString();
        if (returnType == "System.Void")
            returnType = "void";
        Class.Methods.Append($"extern ");
        //Normal method
        Class.Methods.Append(returnType);
        Class.Methods.Append(" ");
        Class.Methods.Append(prefix);
        Class.Methods.Append($"{method.MethodName}(");
        if (extensionClass)
        {
            Class.Methods.Append($"this {rawFullName} __instance__");
            if (method.Inputs.Count > 0)
                Class.Methods.Append(", ");
        }

        AddInputs(Class, method);

        Class.Methods.AppendLine(");");
    }

    private static void AddInputs(Class Class, Method method)
    {
        for (int i = 0; i < method.Inputs.Count; i++)
        {
            TDType input = method.Inputs[i];
            Class.Methods.Append(input); //Type name
            Class.Methods.Append(
                $" {UdonTypeExporter.RemoveTypeSpecialCharacters(UdonTypeExporter.FirstCharacterToLower(input.UdonName), true, true)}_{i}"); //Parameter name
            if (i != method.Inputs.Count - 1)
                Class.Methods.Append(", ");
        }
    }

    static string attributeClassString = @"public class UdonType : System.Attribute
{
    public string UdonName;
    public UdonType(string udonName)
    {
        UdonName = udonName;
    }
}

public class UdonMethod : System.Attribute
{
    public string ExternString;
    public UdonMethod(string externString)
    {
        ExternString = externString;
    }
}

public class UdonSetMethod : UdonMethod
{
    public UdonSetMethod(string externString) : base(externString)
    {
    }
}

public class UdonGetMethod : UdonMethod
{
    public UdonGetMethod(string externString) : base(externString)
    {
    }
}
public class UdonConstructor : UdonMethod
{
    public UdonConstructor(string externString) : base(externString)
    {
    }
}

namespace System.Runtime.InteropServices
{
	public sealed class OutAttribute : Attribute {}
}
namespace System.Runtime.CompilerServices
{
	public sealed class ExtensionAttribute : Attribute {}
}";
}