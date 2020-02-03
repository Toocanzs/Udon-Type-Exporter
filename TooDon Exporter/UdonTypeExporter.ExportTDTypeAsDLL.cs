using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TooDon;
using UnityEngine;
using VRC.Udon.UAssembly.Assembler;

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
        public string FullName;
        public bool IsExtentionClass;
        public TDType Type;
        public bool IsEnum;
        public bool IsInterface;
        public bool IsStruct;
        public List<string> EnumNames = new List<string>();
        public HashSet<string> Interfaces = new HashSet<string>();
        public string InheritedClass = "";
        public bool IsNamespace;

        public string GenerateCode()
        {
            StringBuilder s = new StringBuilder();

            if (IsNamespace)
            {
                s.Append("namespace ");
            }
            else
            {
                s.AppendLine($"\t[UdonType(\"{UdonName}\")]");
                s.Append($"public ");
                if (IsExtentionClass)
                    s.Append("static ");

                if (TypeName == "String")
                    s.Append("sealed ");
                if (TypeName == "Array")
                    s.Append("abstract ");

                if (IsEnum)
                    s.Append("enum ");
                else if (IsInterface)
                    s.Append("interface ");
                else if (IsStruct)
                    s.Append("struct ");
                else
                    s.Append("class ");
            }

            if (IsExtentionClass)
                s.Append($"{UdonName}Extensions");
            else
            {
                if (IsNamespace)
                    s.Append($"{FullName}");
                else
                    s.Append($"{TypeName}");
            }


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

            if (InheritedClass != "" || Interfaces.Count > 0)
                s.Append(" : ");
            s.Append(InheritedClass);
            if (InheritedClass != "" && Interfaces.Count > 0)
                s.Append(", ");
            s.AppendLine(string.Join(", ", Interfaces));

            s.AppendLine("\t{");
            s.Append(string.Join(", ", EnumNames));

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

    public static string ExportTDTypeAsDLL(TDType root, TypeResolverGroup typeResolverGroup)
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
                    Type = type,
                    FullName = type.FullName
                };
                Class.IsNamespace = Class.TypeName == "System" || Class.TypeName == "Collections" ||
                                    (string.IsNullOrEmpty(Class.UdonName) && type.GenericArguments.Count == 0);
                if (type.CSharpType != null)
                {
                    Class.IsEnum = type.CSharpType.IsEnum;
                    Class.IsInterface = type.CSharpType.IsInterface;
                    Class.IsStruct = type.CSharpType.IsValueType;
                    if (!Class.IsEnum && !Class.IsExtentionClass)
                        Class.InheritedClass = type.CSharpType.GetFirstInheritedUdonType(typeResolverGroup);
                    if (!Class.IsExtentionClass)
                        type.CSharpType.AddTypeInterfaces(Class.Interfaces, typeResolverGroup);
                    if (Class.IsEnum)
                    {
                        var names = Enum.GetNames(type.CSharpType);
                        foreach (var name in names)
                        {
                            Class.EnumNames.Add(name);
                        }
                    }
                }

                fullNameToClass[dictionaryKey] = Class;

                //Add new class to children
                if (string.IsNullOrEmpty(type.NamespaceName) || Class.IsNamespace)
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
        var collections = fullNameToClass["System.Collections"].Methods;
        collections.AppendLine("public interface IEnumerable { }");
        var system = fullNameToClass["System"].Methods;
        system.AppendLine("public abstract class ValueType { }");
        system.AppendLine("public abstract class Enum : ValueType { }");
        system.AppendLine("public class Attribute { }");
        system.AppendLine("public abstract class Delegate { }");
        system.AppendLine("public abstract class MulticastDelegate : Delegate { }");
        system.AppendLine("internal struct IntPtr { }");
        system.AppendLine("internal struct UIntPtr { }");
        system.AppendLine("public struct RuntimeTypeHandle { }");
        system.AppendLine("public struct RuntimeMethodHandle { }");
        system.AppendLine("public struct RuntimeFieldHandle { }");
        system.AppendLine("public interface IDisposable { }");
        system.AppendLine("public sealed class ParamArrayAttribute : Attribute { }");

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
        var returnType = method.Output.ToString();
        if (returnType == "System.Void")
            returnType = "void";
        if (method.MethodType == "op")
        {
            bool atLeastOneInputIsContainedType =
                false; //For some reason there are operators defined in classes which are from their inherited class
            bool atLeastOneInOutIsContainedType = false;//Check output too
            foreach (var methodInput in method.Inputs)
            {
                if (methodInput == Class.Type)
                {
                    atLeastOneInputIsContainedType = true;
                }
            }

            if (method.Output == Class.Type)
                atLeastOneInOutIsContainedType = true;

            if (method.MethodName == "Implicit")
            {
                if (atLeastOneInOutIsContainedType)
                {
                    //[UdonMethod("VRCSDK3ComponentsVRCAudioBank.__op_Implicit__UnityEngineObject__SystemBoolean")]
                    //public static extern implicit operator System.Boolean(UnityEngine.Object unityEngineObject_0);   
                    Class.Methods.AppendLine($"\t[UdonMethod(\"{method.FullUdonExternString}\")]");
                    Class.Methods.Append($"\tpublic static extern implicit operator {returnType}(");
                    AddInputs(Class, method);
                    Class.Methods.AppendLine($");");
                }
                else
                {
                    return;
                }
            }
            else if (atLeastOneInputIsContainedType &&
                     !(method.MethodName == "ConditionalXor" //These can't be overloaded.
                       || method.MethodName == "ConditionalOr"
                       || method.MethodName == "ConditionalAnd"))
            {
                string operatorToken = null;
                switch (method.MethodName)
                {
                    case "Addition":
                        operatorToken = "+";
                        break;
                    case "UnaryMinus":
                    case "Subtraction":
                        operatorToken = "-";
                        break;
                    case "Multiply":
                    case "Multiplication":
                        operatorToken = "*";
                        break;
                    case "Division":
                        operatorToken = "/";
                        break;
                    case "Equality":
                        operatorToken = "==";
                        break;
                    case "Inequality":
                        operatorToken = "!=";
                        break;
                    case "LessThan":
                        operatorToken = "<";
                        break;
                    case "GreaterThan":
                        operatorToken = ">";
                        break;
                    case "LessThanOrEqual":
                        operatorToken = "<=";
                        break;
                    case "GreaterThanOrEqual":
                        operatorToken = ">=";
                        break;
                    case "LeftShift":
                        operatorToken = "<<";
                        break;
                    case "RightShift":
                        operatorToken = ">>";
                        break;
                    case "LogicalAnd":
                        operatorToken = "&";
                        break;
                    case "LogicalOr":
                        operatorToken = "|";
                        break;
                    case "LogicalXor":
                        operatorToken = "^";
                        break;
                    case "UnaryNegation":
                        operatorToken = "!";
                        break;
                    default:
                        Debug.LogError($"Unhandled op {method.MethodName}");
                        break;
                }

                Class.Methods.AppendLine($"\t[UdonMethod(\"{method.FullUdonExternString}\")]");
                Class.Methods.Append($"\tpublic static extern {returnType} operator {operatorToken}(");
                if (method.MethodName == "LeftShift" || method.MethodName == "RightShift")
                {
                    //Overloaded shift operator must have the type of the first operand be the containing type, and the type of the second operand must be int
                    Class.Methods.Append(method.Inputs[0]);
                    Class.Methods.Append(" ");
                    Class.Methods.Append(
                        $"{UdonTypeExporter.RemoveTypeSpecialCharacters(UdonTypeExporter.FirstCharacterToLower(method.Inputs[0].UdonName), true, true)}_0");
                    Class.Methods.Append(", System.Int32 systemInt32_1");
                }
                else
                {
                    AddInputs(Class, method);
                }

                Class.Methods.AppendLine(");");
                return;
            }
        }

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

        if (!Class.IsInterface)
            Class.Methods.Append($"\tpublic ");

        if (method.IsStatic || extensionClass)
            Class.Methods.Append("static ");

        if (!Class.IsInterface)
            Class.Methods.Append($"extern ");
        //Normal method
        Class.Methods.Append(returnType);
        Class.Methods.Append(" ");
        Class.Methods.Append(prefix);
        if (method.MethodType == "add")
            Class.Methods.Append("add_");
        if (method.MethodType == "remove")
            Class.Methods.Append("remove_");
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

public static class TypeExtensions
{
    public static string GetFirstInheritedUdonType(this Type c, TypeResolverGroup typeResolverGroup)
    {
        return RecurseInheritedType(c, c, typeResolverGroup);
    }

    public static void AddTypeInterfaces(this Type type, HashSet<string> interfaces,
        TypeResolverGroup typeResolverGroup)
    {
        if (type == null)
            return;
        foreach (var Interface in type.GetInterfaces())
        {
            if (typeResolverGroup.GetTypeFromTypeString(UdonTypeExporter.GenerateUdonName(Interface)) != null)
            {
                interfaces.Add(UdonTypeExporter.GetTypeFullName(Interface));
                Interface.AddTypeInterfaces(interfaces, typeResolverGroup);
            }
        }
    }

    private static string RecurseInheritedType(Type originalType, Type type, TypeResolverGroup typeResolverGroup)
    {
        if (type == null || type == typeof(object))
            return "";
        if (originalType == type)
        {
            return RecurseInheritedType(originalType, type.BaseType, typeResolverGroup);
        }

        if (typeResolverGroup.GetTypeFromTypeString(UdonTypeExporter.GenerateUdonName(type)) != null)
        {
            return UdonTypeExporter.GetTypeFullName(type);
        }

        if (type.BaseType != null)
            return RecurseInheritedType(originalType, type.BaseType, typeResolverGroup);

        return "";
    }
}