using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.Udon.Editor;
using VRC.Udon.Graph;
using EditedOdinTypeExtensions;
using System.Text;
using TooDon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.EditorBindings;
using VRC.Udon.UAssembly.Assembler;
using VRC.Udon.UAssembly.Interfaces;
using VRC.Udon.Wrapper;

public class UdonTypeExporter : MonoBehaviour
{
    static readonly Regex NonCtorRegex = new Regex(
        @"(?<namespace>[a-zA-Z0-9]+)\.__(?:(?<methodType>get|op|set|add|remove)_)?(?!ctor)(?<methodName>(?:m_|_|Get|Set)?[a-zA-Z0-9]+)?(?:__(?<inputs>[a-zA-Z0-9\*]+(?:_[a-zA-Z0-9]+)*))?__(?<outputs>.*)",
        RegexOptions.Compiled); //https://regexr.com/4r9ik

    static readonly Regex CtorRegex = new Regex(
        @"(?<namespace>[a-zA-Z0-9]+)\.__(?<methodType>)?(?<methodName>ctor)__(?<inputs>[a-zA-Z0-9\*]*(?:_[a-zA-Z0-9]+)*)__(?<outputs>.*)",
        RegexOptions.Compiled); //https://regexr.com/4racm

    static readonly Regex GenericsRegex = new Regex(
        @"(?<GenericBaseType>[a-zA-Z0-9\*]+(?:\.[a-zA-Z0-9\*]+)*)\<(?<GenericArguments>.*)\>",
        RegexOptions.Compiled); //https://regexr.com/4rh6o

    static IUdonWrapper Wrapper = new UdonDefaultWrapperFactory().GetWrapper();

    [MenuItem("TooDon/Export Udon Types")]
    static void ExportUdonTypes()
    {
        string path = EditorUtility.SaveFilePanel("Save Udon Types", "", "UdonNodeInfo", "dll");
        var typeResolver = new TypeResolverGroup(new List<IUAssemblyTypeResolver>()
        {
            new SystemTypeResolver(),
            new UnityEngineTypeResolver(),
            new VRCSDK2TypeResolver(),
            new UdonTypeResolver(),
            new ExceptionTypeResolver(),
            new UdonBehaviourTypeResolver(),
        });
        var rootType = new TDType();

        try
        {
            EditorUtility.DisplayProgressBar("Progress", "Parsing Definitions...", 1f / 2);
            ParseDefinitions(rootType, typeResolver);

            EditorUtility.DisplayProgressBar("Progress", "Saving to file...", 2f / 2);

            string codeString = UdonTypeDLLExporter.ExportTDTypeAsDLL(rootType, typeResolver);
            
            CompilerParameters parameters = new CompilerParameters();
            parameters.GenerateExecutable = false;
            parameters.CompilerOptions = "-nostdlib -noconfig";
            parameters.OutputAssembly = "Output.dll";
            CompilerResults r = CodeDomProvider.CreateProvider("CSharp")
                .CompileAssemblyFromSource(parameters, codeString);

            bool error = false;
            foreach (var s in r.Output)
            {
                if (s.Contains("warning"))
                    continue;
                error = true;
                Debug.Log(s);
            }
           
            File.WriteAllLines("source.txt", new []{codeString});

            File.Copy("Output.dll", path, true);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"Done\nOutput to: {path}");
    }

    private static void ParseDefinitions(
        TDType rootType, TypeResolverGroup typeResolverGroup)
    {
        foreach (var definition in UdonEditorManager.Instance.GetNodeDefinitions())
        {
            if (StartsWithIgnoredKeyword(definition) | IsSpecialDefinition(definition))
                continue;
            //Try to match by the non constructor regex, if it fails fallback to the constructor regex.
            //Perhaps they can be combined but this works.
            var match = NonCtorRegex.Match(definition.fullName);
            if (match.Groups.Count != NonCtorRegex.GetGroupNumbers().Length)
                match = CtorRegex.Match(definition.fullName);
            var groups = match.Groups;
            //Make sure all groups are filled. If not then the regex failed.
            if (groups.Count == NonCtorRegex.GetGroupNumbers().Length)
            {
                var definitionName = groups["namespace"].ToString();
                var methodType = groups["methodType"].ToString();
                var methodName = groups["methodName"].ToString();
                var inputsRaw = groups["inputs"].ToString();
                var methodOutput = groups["outputs"].ToString();

                //For some reason underscores are allowed and I'm not quite sure how to deal with them, so let's just do this
                //Replace with -, split by _, replace - with _
                inputsRaw = inputsRaw.Replace("VRCSDKBaseVRC_", "VRCSDKBaseVRC-");
                var methodInputs = inputsRaw.Split('_');
                for (int i = 0; i < methodInputs.Length; i++)
                {
                    methodInputs[i] = methodInputs[i].Replace("-", "_");
                }
                var isStatic = (definition.inputNames.Length > 0 && definition.inputNames[0] != "instance");
                //Some of the methods don't have any inputs(so definition.inputNames[0] doesn't exist) so we have to check the wrapper
                try
                {
                    int outputCount = definition.outputs[0] != typeof(void) ? 1 : 0;
                    int inputParameterCount = Wrapper.GetExternFunctionParameterCount(definition.fullName) - outputCount;
                    if (definition.inputNames.Length == 0 && inputParameterCount == 0)
                        isStatic = true;
                }
                catch //Catch because the wrapper just throws for some unsupported externs that exist in node definitions
                {
                    
                }

                var fullUdonExternString = definition.fullName;
                var definitionType = typeResolverGroup.GetTypeFromTypeStringWithErrors(definitionName);
                var namespaceName = GetTypeFullName(definitionType);
                var definitionTDType = GetOrCreateTDType(rootType, namespaceName, typeResolverGroup);
                definitionTDType.UdonName = GenerateUdonName(definitionType);
                definitionTDType.CSharpType = definitionType;

                //Create TDTypes for all C# types encountered in the definition, and attach methods to them for each of the Extern functions
                var method = new Method
                {
                    FullUdonExternString = fullUdonExternString,
                    MethodName = methodName,
                    MethodType = methodType,
                    IsStatic = isStatic
                };

                foreach (var udonTypeName in methodInputs)
                {
                    if (udonTypeName != "")
                    {
                        var thisType = typeResolverGroup.GetTypeFromTypeStringWithErrors(udonTypeName);
                        var typeName = GetTypeFullName(thisType);
                        TDType tdType = GetOrCreateTDType(rootType, typeName, typeResolverGroup);
                        tdType.UdonName = GenerateUdonName(thisType);
                        tdType.CSharpType = thisType;
                        if (typeResolverGroup.GetTypeFromTypeStringWithErrors(tdType.UdonName) != thisType)
                        {
                            Debug.LogError(
                                $"Could not generate proper udon name for {thisType}. Generated: {tdType.UdonName}");
                        }

                        method.Inputs.Add(tdType);
                    }
                }

                if (methodOutput != "")
                {
                    var thisType = typeResolverGroup.GetTypeFromTypeStringWithErrors(methodOutput);
                    TDType tdType = GetOrCreateTDType(rootType, GetTypeFullName(thisType), typeResolverGroup);
                    tdType.UdonName = GenerateUdonName(thisType);
                    tdType.CSharpType = thisType;
                    method.Output = tdType;
                }

                if (method.IsStatic)
                    definitionTDType.StaticMethods.Add(method);
                else
                    definitionTDType.NonStaticMethods.Add(method);
            }
            else
            {
                Debug.LogError($"Unhandled definition: {definition.fullName}");
            }
        }
    }

    public static TDType GetOrCreateTDType(TDType rootType, string fullName, TypeResolverGroup typeResolverGroup)
    {
        bool containsGenericArguments = fullName.Contains("<");
        Queue<string> namespaces;
        string genericArguments = null;

        if (containsGenericArguments) //Generic types
        {
            var match = GenericsRegex.Match(fullName);
            var groups = match.Groups;
            var baseType = groups["GenericBaseType"].ToString();
            genericArguments = groups["GenericArguments"].ToString();
            namespaces = new Queue<string>(baseType.Split('.'));
        }
        else
        {
            namespaces = new Queue<string>(fullName.Split('.'));
        }

        var current = rootType;
        while (namespaces.Count > 0)
        {
            var name = namespaces.Dequeue();
            //Only the full string is "generic", so it must be the last thing in the queue.
            //IE. System.Collections.Generic isn't generic itself, but System.Collections.Generic.List is generic
            bool isGeneric = containsGenericArguments && namespaces.Count == 0;
            var child = current.Children.Find(x =>
                x.TypeName == name && x.IsGeneric == isGeneric && genericArguments == x.InputGenericArguments);
            if (child != null)
            {
                //Go down tree
                current = child;
            }
            else
            {
                //Create an go down tree
                var type = new TDType
                {
                    NamespaceName = current.FullName,
                    FullName = (current.FullName != null ? current.FullName + "." : "") + name,
                    TypeName = name,
                    InputGenericArguments = genericArguments
                };
                string attemptedUdonName = GenerateUdonName(type.FullName, true);//Try to generate udon name and set it if it's correct.
                if (typeResolverGroup.GetTypeFromTypeString(attemptedUdonName) != null)
                {
                    type.UdonName = attemptedUdonName;
                }
                    
                current.Children.Add(type);
                current = type;
                current.IsGeneric = isGeneric;
            }
        }

        if (current.IsGeneric)
        {
            current.IsGeneric = true;
            if (!genericArguments.Contains("<"))
            {
                current.AddGenericArguments(rootType, typeResolverGroup, genericArguments.Replace(" ", "").Split(','));
            }
            else
            {
                //Only one thing contains a nested generic argument in Udon currently
                //and luckily it looks like "thing<other<something, else>>"
                //which means it's a single layer of nesting
                //So for now we can just pass "other<something, else>" to GetOrCreateTDType
                //In the future this might change?
                current.AddGenericArguments(rootType, typeResolverGroup, genericArguments);
            }
        }

        if (fullName.Contains("[]"))
        {
            //Add base type for arrays
            GetOrCreateTDType(rootType, fullName.Replace("[]", ""), typeResolverGroup);
        }

        return current;
    }

    public static StringBuilder RemoveTypeSpecialCharacters(string name, bool replaceArray, bool replaceStar)
    {
        var s = new StringBuilder(name);
        if (replaceArray)
            s.Replace("[]", "Array");

        s.Replace(".", "");
        s.Replace("<", "");
        s.Replace(">", "");
        if (replaceStar)
            s.Replace("*", "Ref");
        s.Replace(", ", "");
        return s;
    }

    public static string GetTypeFullName(Type type)
    {
        StringBuilder s = new StringBuilder();
        if (type.IsGenericType)
        {
            s.Append(type.GetNiceFullName());
        }
        else
        {
            s.Append(type);
        }

        s.Replace("+", ".");
        return s.ToString();
    }

    public static string GenerateUdonName(Type type)
    {
        if (type == typeof(List<object>))
            return "ListT";
        bool replaceStar = (type != typeof(System.Char*) && type != typeof(System.SByte*));
        return GenerateUdonName(GetTypeFullName(type), replaceStar);
    }
    
    public static string GenerateUdonName(string fullName, bool replaceStar)
    {
        return RemoveTypeSpecialCharacters(fullName, true, replaceStar).ToString();
    }

    private static bool IsSpecialDefinition(UdonNodeDefinition definition)
    {
        return definition.fullName == "Block" || definition.fullName == "Branch" || definition.fullName == "While" ||
               definition.fullName == "For" || definition.fullName == "Get_Variable" ||
               definition.fullName == "Set_Variable" ||
               definition.fullName == "SubGraph" || definition.fullName == "Comment";
    }

    private static bool StartsWithIgnoredKeyword(UdonNodeDefinition definition)
    {
        return definition.fullName.StartsWith("Variable_") || definition.fullName.StartsWith("Event_") ||
               definition.fullName.StartsWith("Const_") || definition.fullName.StartsWith("Type_");
    }

    public static string FirstCharacterToLower(string str)
    {
        if (String.IsNullOrEmpty(str) || Char.IsLower(str, 0))
            return str;

        return Char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}

public static class TypeResolverGroupExtentions
{
    public static Type GetTypeFromTypeStringWithErrors(this TypeResolverGroup typeResolverGroup, string typeString)
    {
        var type = typeResolverGroup.GetTypeFromTypeString(typeString);
        if (type == null)
            Debug.LogError($"Unable to find type {typeString} in type resolver group");
        return type;
    }
}

namespace TooDon
{
    public class TDType
    {
        /// <summary>
        /// Type name of this type. Example: UnityEngine.Debug will have the TypeName "Debug"
        /// </summary>
        public string TypeName;

        /// <summary>
        /// Full name including the namespace
        /// </summary>
        public string FullName;

        /// <summary>
        /// Type name of this type's namespace. Example: UnityEngine.Debug will have the NamespaceName "UnityEngine"
        /// </summary>
        public string NamespaceName;

        /// <summary>
        /// The name as it appears in Udon's type resolver.
        /// </summary>
        public string UdonName;

        public Type CSharpType;

        private bool GeneratedGenericArguments = false;

        public List<Method> StaticMethods = new List<Method>();
        public List<Method> NonStaticMethods = new List<Method>();

        public List<TDType> Children = new List<TDType>();

        public bool IsGeneric;
        public List<TDType> GenericArguments = new List<TDType>();

        /// <summary>
        /// The generic argument string that was used to generate the list of generic arguments
        /// </summary>
        // Used to tell apart unique generic types. Like List<Thing> and List<Blah> are both List<>, but Thing != Blah, so they become different types.
        // Have to use the string because we might not have the list of generic arguments setup yet.
        public string InputGenericArguments;

        public void AddGenericArguments(TDType rootType, TypeResolverGroup typeResolverGroup, params string[] strings)
        {
            if (!GeneratedGenericArguments)
            {
                foreach (var genericArgument in strings)
                {
                    GenericArguments.Add(UdonTypeExporter.GetOrCreateTDType(rootType, genericArgument, typeResolverGroup));
                    GeneratedGenericArguments = true;
                }
            }
        }

        public override string ToString()
        {
            if (!IsGeneric)
                return FullName;

            StringBuilder s = new StringBuilder(FullName + "<");
            for (int i = 0; i < GenericArguments.Count; i++)
            {
                s.Append(GenericArguments[i]);
                if (i != GenericArguments.Count - 1)
                    s.Append(", ");
            }

            s.Append(">");
            return s.ToString();
        }
    }


    public class Method
    {
        public string MethodType;
        public string MethodName;
        public string FullUdonExternString;
        public List<TDType> Inputs = new List<TDType>();
        public TDType Output;
        public bool IsStatic;

        public override string ToString()
        {
            return FullUdonExternString;
        }
    }
}