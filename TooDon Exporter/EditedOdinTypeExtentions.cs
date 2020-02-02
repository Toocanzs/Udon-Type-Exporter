//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Sirenix IVS">
// Copyright (c) 2018 Sirenix IVS
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//-----------------------------------------------------------------------

//A few edits of the odin type extension methods.
//Edited to make the GetNiceFullName print out types like Udon types are printed
//Also removed everything that isn't used.

namespace EditedOdinTypeExtensions
{
    using System;
    using System.Text;

    /// <summary>
    /// Type method extensions.
    /// </summary>
    public static class TypeExtensions
    {
        private static string CreateNiceName(Type type)
        {
            if (type.IsArray)
            {
                int rank = type.GetArrayRank();
                return type.GetElementType().GetNiceName() + (rank == 1 ? "[]" : "[,]");
            }

            if (type.IsByRef)
            {
                return "ref " + type.GetElementType().GetNiceName();
            }

            if (type.IsGenericParameter)
            {
                return type.ToString();
            }

            var builder = new StringBuilder();
            var name = type.Name;
            var index = name.IndexOf("`");

            if (index != -1)
            {
                builder.Append(name.Substring(0, index));
            }
            else
            {
                builder.Append(name);
            }

            if (type.IsGenericType)
            {
                builder.Append('<');
                var args = type.GetGenericArguments();

                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (i != 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(GetNiceFullName(arg));
                }

                builder.Append('>');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Returns a nicely formatted name of a type.
        /// </summary>
        public static string GetNiceName(this Type type)
        {
            if (type.IsNested && type.IsGenericParameter == false)
            {
                return type.DeclaringType.GetNiceName() + "." + CreateNiceName(type);
            }

            return CreateNiceName(type);
        }

        /// <summary>
        /// Returns a nicely formatted full name of a type.
        /// </summary>
        public static string GetNiceFullName(this Type type)
        {
            string result;

            if (type.IsNested && type.IsGenericParameter == false)
            {
                return type.DeclaringType.GetNiceFullName() + "." + CreateNiceName(type);
            }

            result = CreateNiceName(type);

            if (type.Namespace != null)
            {
                result = type.Namespace + "." + result;
            }

            return result;
        }
    }
}