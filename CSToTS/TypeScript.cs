using System.Numerics;
using System.Reflection;
using System.Text;

namespace CSToTS
{
	public static class TypeScript
	{
		/// <summary>
		/// C# types associated to TypeScript's primitive types, respectively compared within this dictionary.
		/// </summary>
		private static readonly Dictionary<Type, string> PrimitiveTypes = new()
		{
			{ typeof(bool), "boolean" },
			{ typeof(string), "string" },
			{ typeof(byte), "number" },
			{ typeof(sbyte), "number" },
			{ typeof(short), "number" },
			{ typeof(ushort), "number" },
			{ typeof(int), "number" },
			{ typeof(uint), "number" },
			{ typeof(long), "number" },
			{ typeof(ulong), "number" },
			{ typeof(float), "number" },
			{ typeof(double), "number" },
			{ typeof(decimal), "number" },
			{ typeof(nint), "number" },
			{ typeof(nuint), "number" },
			{ typeof(char), "string" },
			{ typeof(object), "any" },
			{ typeof(void), "void" },
			{ typeof(BigInteger), "bigint" }
		};
		/// <summary>
		/// Returns <see langword="true"/> if <see cref="PrimitiveTypes"/> has <paramref name="type"/>; otherwise; <see langword="false"/>.
		/// </summary>
		/// <param name="type">The given type</param>
		/// <returns><see langword="true"/> if <see cref="PrimitiveTypes"/> has <paramref name="type"/>; otherwise; <see langword="false"/>.</returns>
		private static bool IsBuiltInTypeScript(Type type) => PrimitiveTypes.ContainsKey(type);

		/// <summary>
		/// The temporary list that is used when walking through all associated types.<br/>This is only used so the order of types is correct and avoids 'object is undefined' errors.
		/// </summary>
		private static readonly List<Type> TempListWalking = [];
		/// <summary>
		/// "Walks" through the given type and recursively finds all associated types with the exception of CIL Instructions found inside methods.
		/// </summary>
		/// <param name="type">The given type</param>
		/// <param name="set">The main set of types to add to</param>
		private static void WalkingTypesFinder(Type type, HashSet<Type> set)
		{
			// If generic, get base
			if (type.IsGenericType)
			{
				type = type.GetGenericTypeDefinition();
			}

			// Skip if any of the following conditions is true:
			// - Type has already been added (or has the same name, will be changed soon)
			// - Type is an enum
			// - Type is a generic parameter (e.g. T in GenericClass<T>)
			// - Type is a respectfully matched typescript primitive type.
			if (TempListWalking.Contains(type) || TempListWalking.Any(t => type.Name == t.Name) || type == typeof(Enum) || type.IsGenericParameter || IsBuiltInTypeScript(type))
			{
				return;
			}

			// Allow to save base type of the following:
			// - Arrays
			// - Pointers
			// - Reference
			else if (type.IsArray || type.IsPointer || type.IsByRef)
			{
				WalkingTypesFinder(type.GetElementType()!, set);
				return;
			}

			TypeInfo ti = type.GetTypeInfo();
			TempListWalking.Add(type);

			// Implementations
			if (type.BaseType != null)
			{
				WalkingTypesFinder(type.BaseType, set);
			}
			if (ti.ImplementedInterfaces.Any())
			{
				foreach (Type iface in ti.ImplementedInterfaces)
				{
					WalkingTypesFinder(iface, set);
				}
			}

			// Constants and Fields
			if (ti.DeclaredFields.Any())
			{
				foreach (FieldInfo field in ti.DeclaredFields)
				{
					WalkingTypesFinder(field.FieldType, set);
				}
			}

			// Properties
			if (ti.DeclaredProperties.Any())
			{
				foreach (PropertyInfo property in ti.DeclaredProperties)
				{
					WalkingTypesFinder(property.PropertyType, set);
				}
			}

			// Constructors
			if (ti.DeclaredConstructors.Any(c => c.GetParameters().Length > 0))
			{
				foreach (ConstructorInfo method in ti.DeclaredConstructors)
				{
					foreach (ParameterInfo parameter in method.GetParameters())
					{
						WalkingTypesFinder(parameter.ParameterType, set);
					}
				}
			}

			// Methods
			if (ti.DeclaredMethods.Any())
			{
				foreach (MethodInfo method in ti.DeclaredMethods)
				{
					WalkingTypesFinder(method.ReturnType, set);
					foreach (ParameterInfo parameter in method.GetParameters())
					{
						WalkingTypesFinder(parameter.ParameterType, set);
					}
				}
			}

			set.Add(type);
		}

		private static string GetConvertedName(Type type, bool @namespace = false)
		{
			if (@namespace)
			{
				string ret = IsBuiltInTypeScript(type) ? PrimitiveTypes[type] : FixInvalidName(type.Name);
				return ret.Contains('`') ? ret[..ret.IndexOf('`')] : ret;
			}
			string sb = "%";
			while (type.IsByRef || type.IsArray || type.IsPointer)
			{
				if (type.IsByRef)
				{
					type = type.GetElementType()!;
				}
				else if (type.IsArray)
				{
					sb = sb.Replace("%", "%[]");
					type = type.GetElementType()!;
				}
				else if (type.IsPointer)
				{
					sb = sb.Replace("%", "{ ptr: % }");
					type = type.GetElementType()!;
				}
			}
			string init;
			if (type.IsGenericType && type.Name.Split('`').Length > 1)
			{
				TypeInfo ti = type.GetTypeInfo();
				Type[] types = ti.GenericTypeArguments.Length != 0 ? ti.GenericTypeArguments : ti.GenericTypeParameters;
				string b = ti.Name.Split('`')[0] + "<";
				foreach (TypeInfo ti2 in types.Select(t => t.GetTypeInfo()))
				{
					b += GetConvertedName(ti2) + ", ";
				}
				init = b[..^2] + ">";
			}
			else
			{
				init = IsBuiltInTypeScript(type) ? PrimitiveTypes[type] : FixInvalidName(type.Name);
			}
			return sb.Replace("%", init);
		}
		private static string ExplicitInterfaceImplFix(string name)
		{
			string[] systems = name.Split('.');
			if (systems.Length == 1)
			{
				return "__explicit__" + systems[0];
			}
			return "__explicit__" + FixInvalidName(systems[^2]) + "_" + systems[^1];
		}
		private static string FixInvalidName(string? name) => MethodExtractor.FixInvalidName(name);
		private static bool ImplicitCoexistsWithExplicit(Type type, string explicitRawName)
		{
			/*
             * Returns false if only the explicit definition of given name exists within the given type.  Otherwise, true.
             * This method shouldn't be called when explicit definitions within given type don't exist.
             */

			return type.GetTypeInfo().DeclaredMethods.Any(m => m.Name == explicitRawName.Split('.').Last()) || type.GetTypeInfo().DeclaredProperties.Any(p => p.Name == explicitRawName.Split('.').Last());
		}
		private static string CreateProxyCode(Type type)
		{
			/*
             * Proxy code is code only created for classes/structs that implement at least one property or method from an interface EXPLICITLY.
             * The given type must be the class to check for all explicit items.  They are all marked Virtual and Private.
             * Don't call this if there are no explicit members.
             */

			StringBuilder sb = new();

			TypeInfo info = type.GetTypeInfo();

			foreach (Type t in type.GetInterfaces())
			{
				string name = GetConvertedName(t);

				sb.AppendLine($"public readonly ExplicitAs{FixInvalidName(name)}: {name} = new Proxy(this, {{");
				sb.AppendLine($"{Space(0, 1)}get(target, prop, receiver) {{");

				bool assertfail = true;

				if (info.DeclaredProperties.Any(ptemp => (ptemp.GetMethod != null && ptemp.GetMethod.IsVirtual && ptemp.GetMethod.IsPrivate) || (ptemp.SetMethod != null && ptemp.SetMethod.IsVirtual && ptemp.SetMethod.IsPrivate)))
				{
					assertfail = false;
					foreach (PropertyInfo pi in info.DeclaredProperties.Where(p => (p.GetMethod != null && p.GetMethod.IsVirtual && p.GetMethod.IsPrivate) || (p.SetMethod != null && p.SetMethod.IsVirtual && p.SetMethod.IsPrivate)))
					{
						sb.AppendLine($"{Space(0, 2)}if (prop === '{pi.Name.Split('.').Last()}') {{");
						sb.AppendLine($"{Space(0, 3)}return target.{ExplicitInterfaceImplFix(pi.Name)};");
						sb.AppendLine($"{Space(0, 2)}}}");
					}
					sb.AppendLine();
				}
				if (info.DeclaredMethods.Any(mtemp => mtemp.IsVirtual && mtemp.IsPrivate && !mtemp.IsSpecialName))
				{
					assertfail = false;
					foreach (MethodInfo mi in info.DeclaredMethods.Where(m => m.IsVirtual && m.IsPrivate && !m.IsSpecialName))
					{
						sb.AppendLine($"{Space(0, 2)}if (prop === '{mi.Name.Split('.').Last()}') {{");
						sb.AppendLine($"{Space(0, 3)}return target.{ExplicitInterfaceImplFix(mi.Name)}.bind(target);");
						sb.AppendLine($"{Space(0, 2)}}}");
					}
					sb.AppendLine();
				}

				if (assertfail)
				{
					throw new InvalidOperationException("The given type did not have explicit members.");
				}

				sb.AppendLine($"{Space(0, 2)}return Reflect.get(target, prop, receiver);");
				sb.AppendLine($"{Space(0, 1)}}}");
				sb.AppendLine("});");
			}

			return sb.ToString();
		}

		public static int SpaceCount { get; set; } = 4;
		public static string Space(int b, int n) => new(' ', SpaceCount * (b + n));

		public static string GetTypeScriptCode(MethodInfo method, bool @interface)
		{
			StringBuilder sb = new();

			sb.Append($"{(!method.DeclaringType!.IsInterface ? (method.IsPublic ? "public " : "private ") : "")}{(method.IsStatic ? "static " : "")}" +
				$"{(method.IsVirtual && method.IsPrivate ? ExplicitInterfaceImplFix(method.Name) : FixInvalidName(method.Name))}(");
			ParameterInfo[] array = method.GetParameters();
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].ParameterType.IsByRef)
				{
					if (array[i].IsOut)
					{
						sb.Append("out: { ");
					}
					else
					{
						sb.Append("ref: { ");
					}
				}
				sb.Append($"{FixInvalidName(array[i].Name)}: {GetConvertedName(array[i].ParameterType)}");
				if (array[i].ParameterType.IsByRef)
				{
					sb.Append(" }");
				}

				if (i < method.GetParameters().Length - 1)
				{
					sb.Append(", ");
				}
			}

			if (method.IsAbstract)
			{
				sb.AppendLine($"): {GetConvertedName(method.ReturnType)};");
			}
			else
			{
				sb.Append($"){(method.ReturnType != typeof(void) ? $": {GetConvertedName(method.ReturnType)}" : "")}");
				if (@interface)
				{
					sb.AppendLine("; // Method implementation exists, not written here due to TypeScript interface structure production");
				}
				else
				{
					sb.AppendLine(" {");
					sb.AppendLine($"{Space(0, 1)}throw new Error(\"not yet implemented\");");
					sb.AppendLine("}");
				}
			}

			if (method.IsVirtual && method.IsPrivate && !ImplicitCoexistsWithExplicit(method.DeclaringType, method.Name))
			{
				sb.Append($"public {method.Name.Split('.').Last()}(");
				for (int i = 0; i < array.Length; i++)
				{
					if (array[i].ParameterType.IsByRef)
					{
						if (array[i].IsOut)
						{
							sb.Append("out: { ");
						}
						else
						{
							sb.Append("ref: { ");
						}
					}
					sb.Append($"{FixInvalidName(array[i].Name)}: {GetConvertedName(array[i].ParameterType)}");
					if (array[i].ParameterType.IsByRef)
					{
						sb.Append(" }");
					}

					if (i < method.GetParameters().Length - 1)
					{
						sb.Append(", ");
					}
				}
				sb.Append($"){(method.ReturnType != typeof(void) ? $": {GetConvertedName(method.ReturnType)}" : "")}{(@interface ? "; // Method implementation exists, not written here for ease of use" : " {")}");

				if (@interface)
				{
					sb.AppendLine("; // Method implementation exists, not written here for ease of use");
				}
				else
				{
					sb.AppendLine(" {");
					sb.AppendLine($"{Space(0, 1)}throw new Error(\"Invalid call (use ExplicitAs property)\");");
					sb.AppendLine("}");
				}
			}

			return sb.ToString();
		}
		public static string GetTypeScriptCode(FieldInfo field)
		{
			return $"{(field.IsPublic ? "public" : "private")} {(field.IsStatic ? (field.IsInitOnly ? field.IsLiteral ? "const" : "static readonly" : "static") + " " : "")}{FixInvalidName(field.Name)}: {GetConvertedName(field.FieldType)};";
		}
		public static string GetTypeScriptCode(PropertyInfo property)
		{
			StringBuilder sb = new();

			if (property.GetMethod != null)
			{
				sb.Append($"{(!property.DeclaringType!.IsInterface ? (property.GetMethod.IsPublic ? "public " : "private ") : "")}{(property.GetMethod.IsStatic ? "static " : "")}get " +
					$"{(property.GetMethod.IsVirtual && property.GetMethod.IsPrivate ? ExplicitInterfaceImplFix(property.Name) : FixInvalidName(property.Name))}(): {GetConvertedName(property.PropertyType)}");
				if (property.GetMethod.IsAbstract)
				{
					sb.AppendLine(";");
				}
				else
				{
					sb.AppendLine(" {");
					sb.AppendLine($"{Space(0, 1)}throw new Error(\"not yet implemented\");");
					sb.AppendLine("}");
				}
			}
			if (property.SetMethod != null)
			{
				ParameterInfo pi = property.SetMethod.GetParameters()[0]; // There's only one: value
				sb.Append($"{(!property.DeclaringType!.IsInterface ? (property.SetMethod.IsPublic ? "public " : "private ") : "")}{(property.SetMethod.IsStatic ? "static " : "")}set " +
					$"{(property.SetMethod.IsVirtual && property.SetMethod.IsPrivate ? ExplicitInterfaceImplFix(property.Name) : FixInvalidName(property.Name))}({pi.Name}: {GetConvertedName(pi.ParameterType)})");
				if (property.SetMethod.IsAbstract)
				{
					sb.AppendLine(";");
				}
				else
				{
					sb.AppendLine(" {");
					sb.AppendLine($"{Space(0, 1)}throw new Error(\"not yet implemented\");");
					sb.AppendLine("}");
				}
			}

			if (property.GetMethod != null && property.GetMethod.IsVirtual && property.GetMethod.IsPrivate && !ImplicitCoexistsWithExplicit(property.DeclaringType!, property.Name))
			{
				sb.AppendLine($"public get {property.Name.Split('.').Last()}(): {GetConvertedName(property.GetMethod.ReturnType)} {{");
				sb.AppendLine($"{Space(0, 1)}throw new Error(\"Invalid call (use ExplicitAs property)\");");
				sb.AppendLine("}");
			}
			if (property.SetMethod != null && property.SetMethod.IsVirtual && property.SetMethod.IsPrivate && !ImplicitCoexistsWithExplicit(property.DeclaringType!, property.Name))
			{
				ParameterInfo pi = property.SetMethod.GetParameters()[0];
				sb.AppendLine($"public set {property.Name.Split('.').Last()}({pi.Name}: {GetConvertedName(pi.ParameterType)}) {{");
				sb.AppendLine($"{Space(0, 1)}throw new Error(\"Invalid call (use ExplicitAs property)\");");
				sb.AppendLine("}");
			}

			return sb.ToString();
		}
		public static string GetTypeScriptCode(ConstructorInfo contructor)
		{
			StringBuilder sb = new();

			if (contructor.IsStatic)
			{
				sb.AppendLine("private static staticctor_initialize = (() => {");
				sb.AppendLine($"{Space(0, 1)}throw new Error(\"not yet implemented\");");
				sb.AppendLine("})();");
				sb.AppendLine("private constructor() { }");
			}
			else
			{
				sb.Append($"{(contructor.IsPublic ? "public" : contructor.IsPrivate ? "private" : "protected")} constructor(");
				ParameterInfo[] array = contructor.GetParameters();
				for (int i = 0; i < array.Length; i++)
				{
					ParameterInfo pi = array[i];
					sb.Append($"{FixInvalidName(pi.Name)}: {GetConvertedName(pi.ParameterType)}");
					if (i < array.Length - 1)
					{
						sb.Append(", ");
					}
				}
				sb.AppendLine(") {");

				sb.AppendLine($"{Space(0, 1)}throw new Error(\"not yet implemented\");");
				sb.AppendLine("}");
			}

			return sb.ToString();
		}

		public static string GetTypeScriptClass(Type type, int r)
		{
			StringBuilder sb = new();
			TypeInfo exType = type.GetTypeInfo();

			sb.Append($"{Space(r, 0)}export {(exType.IsAbstract ? (exType.IsSealed ? "" : "abstract ") : "")}class {GetConvertedName(type)}"); // start first line with export <abstract> class [name]
			if (exType.BaseType != null && exType.BaseType != typeof(object))// && !exType.IsValueType)
			{
				sb.Append($" extends {GetConvertedName(exType.BaseType)}"); // where classes inherit other classes
			}
			if (exType.ImplementedInterfaces.Any())
			{
				sb.Append(" implements");
				foreach (Type intf in exType.ImplementedInterfaces)
				{
					sb.Append($" {GetConvertedName(intf)},"); // where classes implement interfaces
				}
				sb.Remove(sb.Length - 1, 1);
			}
			sb.AppendLine(" {");

			// static fields
			if (exType.DeclaredFields.Any(f => f.IsStatic))
			{
				foreach (FieldInfo field in exType.DeclaredFields.Where(f => f.IsStatic))
				{
					sb.AppendLine($"{Space(r, 1)}{GetTypeScriptCode(field)}");
				}
				sb.AppendLine();
			}

			// static properties
			if (exType.DeclaredProperties.Any(p => p.GetMethod != null && p.GetMethod.IsStatic || p.SetMethod != null && p.SetMethod.IsStatic))
			{
				foreach (PropertyInfo prop in exType.DeclaredProperties.Where(p => p.GetMethod != null && p.GetMethod.IsStatic || p.SetMethod != null && p.SetMethod.IsStatic))
				{
					string[] code = [.. GetTypeScriptCode(prop).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
				sb.AppendLine();
			}

			// fields
			if (exType.DeclaredFields.Any(f => !f.IsStatic))
			{
				foreach (FieldInfo field in exType.DeclaredFields.Where(f => !f.IsStatic))
				{
					sb.AppendLine($"{Space(r, 1)}{GetTypeScriptCode(field)}");
				}
				sb.AppendLine();
			}

			// properties
			if (exType.DeclaredProperties.Any(p => p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic))
			{
				foreach (PropertyInfo prop in exType.DeclaredProperties.Where(p => p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic))
				{
					string[] code = [.. GetTypeScriptCode(prop).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
				sb.AppendLine();
			}

			// constructors
			if (exType.DeclaredConstructors.Any())
			{
				foreach (ConstructorInfo cons in exType.DeclaredConstructors)
				{
					string[] code = [.. GetTypeScriptCode(cons).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
				sb.AppendLine();
			}

			// methods
			if (exType.DeclaredMethods.Any(m => !m.IsSpecialName))
			{
				foreach (MethodInfo method in exType.DeclaredMethods.Where(m => !m.IsSpecialName))
				{
					string[] code = [.. GetTypeScriptCode(method, false).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
			}

			// proxy check
			if (exType.DeclaredMethods.Any(m => m.IsVirtual && m.IsPrivate)
				&& exType.DeclaredProperties.Any(p => (p.GetMethod != null && p.GetMethod.IsVirtual && p.GetMethod.IsPrivate) || (p.SetMethod != null && p.SetMethod.IsVirtual && p.SetMethod.IsPrivate)))
			{
				sb.AppendLine();
				string[] code = [.. CreateProxyCode(type).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine($"{Space(r, 1)}{c}");
				}
			}

			if (sb.ToString().StartsWith("export class _ "))
			{

			}

			return sb.ToString();
		}
		public static string GetTypeScriptInterface(Type type, int r)
		{
			TypeInfo exType = type.GetTypeInfo();

			StringBuilder sb = new();

			sb.Append($"{Space(r, 0)}export interface {GetConvertedName(type)}");
			if (exType.ImplementedInterfaces.Any())
			{
				sb.Append(" implements");
				foreach (Type intf in exType.ImplementedInterfaces)
				{
					sb.Append($" {GetConvertedName(intf)},"); // where classes implement interfaces
				}
				sb.Remove(sb.Length - 1, 1);
			}
			sb.AppendLine(" {");

			// properties
			if (exType.DeclaredProperties.Any(p => p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic))
			{
				foreach (PropertyInfo prop in exType.DeclaredProperties.Where(p => p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic))
				{
					string[] code = [.. GetTypeScriptCode(prop).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
				sb.AppendLine();
			}

			// methods
			if (exType.DeclaredMethods.Any(m => !m.IsSpecialName))
			{
				foreach (MethodInfo method in exType.DeclaredMethods.Where(m => !m.IsSpecialName))
				{
					string[] code = [.. GetTypeScriptCode(method, true).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
			}

			return sb.ToString();
		}
		public static string GetTypeScriptEnum(Type type, int r)
		{
			TypeInfo exType = type.GetTypeInfo();

			StringBuilder sb = new();

			// Enums with ": <number type>" doesn't matter for ts.
			sb.AppendLine($"{Space(r, 0)}export enum {GetConvertedName(type)} {{");
			foreach ((string n, object v) in Enum.GetNames(type).Zip(Enum.GetValuesAsUnderlyingType(type).Cast<object>()))
			{
				sb.AppendLine($"{Space(r, 1)}{n} = {(exType.CustomAttributes.Any(attr => attr.AttributeType == typeof(FlagsAttribute)) ? $"0x{v:x}" : $"{v}")},");
			}

			return sb.ToString();
		}


		/// <summary>
		/// Get's the full code of a given type and converts it into equivalent TypeScript code.
		/// </summary>
		/// <param name="type">The given type</param>
		/// <param name="r">idk</param>
		/// <returns>TypeScript code of the given type</returns>
		public static string GetTypeScriptCode(Type type, bool @namespace, int r)
		{
			StringBuilder sb = new();
			TypeInfo exType = type.GetTypeInfo();

			if (exType.IsClass || exType.IsValueType && !exType.IsEnum)
			{
				string[] code = [.. GetTypeScriptClass(type, r).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine(c);
				}
			}
			else if (exType.IsInterface)
			{
				string[] code = [.. GetTypeScriptInterface(type, r).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine(c);
				}
			}
			else if (exType.IsEnum)
			{
				string[] code = [.. GetTypeScriptEnum(type, r).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine(c);
				}
			}

			sb.AppendLine($"{Space(r, 0)}}}");
			if (r == 0)
			{
				sb.AppendLine();
			}

			if (exType.DeclaredNestedTypes.Any())
			{
				//sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);
				sb.AppendLine($"{Space(r, 0)}export namespace {GetConvertedName(type, true)} {{");
				foreach (TypeInfo nestedType in exType.DeclaredNestedTypes)
				{
					string[] code = GetTypeScriptCode(nestedType, false, r + 1).Split(Environment.NewLine);
					foreach (string c in code)
					{
						sb.AppendLine(c);
					}
					sb.AppendLine();
				}
				sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);
				sb.AppendLine($"{Space(r, 0)}}}");
				if (r == 0)
				{
					sb.AppendLine();
				}
			}

			return sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length).ToString();
		}

		public static void GetFullTypeScriptCode(Type type)
		{
			Directory.CreateDirectory("./modules");

			// The Main List of Types to serialize and decompile
			HashSet<Type> mainSet = [];

			// Walk through and find all types
			WalkingTypesFinder(type, mainSet);
			TempListWalking.Clear();

			// Namespaces
			List<string> namespaces = [];
			foreach (Type t in mainSet)
			{
				if (!namespaces.Contains(t.Namespace ?? ""))
				{
					namespaces.Add(t.Namespace ?? "");
				}
			}
			namespaces.Sort();
			Dictionary<string, string> nscode = [];
			foreach (string n in namespaces)
			{
				nscode.Add(n, "");
			}
			namespaces.Clear();

			// Types
			foreach (Type t in mainSet)
			{
				if (t.Namespace != null)
				{
					string fileSpace = t.Namespace.Replace('.', '/');
					if (!Directory.Exists($"./modules/{fileSpace}"))
					{
						Directory.CreateDirectory($"./modules/{fileSpace}");
					}
					File.WriteAllText($"./modules/{fileSpace}/{t.Name}.ts", $"{GetTypeScriptCode(t, false, 0) + Environment.NewLine}");


				}
				else
				{
					File.WriteAllText($"./modules/{t.Name}.ts", GetTypeScriptCode(t, false, 0) + Environment.NewLine);
				}
			}

			//StringBuilder sb = new();
			//foreach (KeyValuePair<string, string> kvp in nscode)
			//{
			//	if (kvp.Key == "")
			//	{
			//		sb.Append(kvp.Value);
			//		continue;
			//	}
			//	sb.Append("export namespace ");
			//	sb.Append(kvp.Key);
			//	sb.AppendLine(" {");
			//	sb.Append(kvp.Value);
			//	sb.AppendLine("}");
			//}

			//return sb.ToString();
		}
	}
}
