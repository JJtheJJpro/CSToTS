using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CSToTS
{
	public static partial class TypeScript
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
			{ typeof(BigInteger), "bigint" },
			{ typeof(Task), "Promise<void>" },
			{ typeof(ValueTask), "Promise<void>" }
		};
		private static readonly Dictionary<Type, string> GenericTypeNames = new()
		{
			{ typeof(List<>), "[]" },
			{ typeof(IList<>), "[]" },
			{ typeof(ICollection<>), "[]" },
			{ typeof(IEnumerable<>), "[]" },
			{ typeof(IReadOnlyCollection<>), "[]" },
			{ typeof(IReadOnlyList<>), "[]" },
			{ typeof(Dictionary<,>), "Map" },
			{ typeof(IDictionary<,>), "Map" },
			{ typeof(IReadOnlyDictionary<,>), "Map" },
			{ typeof(HashSet<>), "Set" },
			{ typeof(ISet<>), "Set" },
			{ typeof(Task<>), "Promise" },
			{ typeof(ValueTask<>), "Promise" }
		};
		private static readonly HashSet<Type> MethodBodySystemTypeExceptions =
		[
			typeof(Volatile),
			typeof(Encoding),

		];
		private static Type GetFieldType(FieldInfo field)
		{
			return field.GetCustomAttribute<FixedBufferAttribute>()?.ElementType ?? field.FieldType;
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
				$"{(!method.DeclaringType.IsInterface && method.IsAbstract ? "abstract " : "")}{(method.IsVirtual && method.IsPrivate ? ExplicitInterfaceImplFix(method.Name) : FixInvalidName(method.Name))}");
			if (method.IsGenericMethodDefinition)
			{
				sb.Append($"<{string.Join(", ", method.GetGenericArguments().Select(t => FixInvalidName(t.Name)))}>");
			}
			sb.Append('(');
			ParameterInfo[] array = method.GetParameters();
			string[] parameterNames = GetTypeScriptParameterNames(array);
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].ParameterType.IsByRef)
				{
					if (array[i].IsOut)
					{
						sb.Append($"{parameterNames[i]}: {{ ");
					}
					else
					{
						sb.Append($"{parameterNames[i]}: {{ ");
					}
				}
				sb.Append($"{(array[i].GetCustomAttribute<ParamArrayAttribute>() != null ? "..." : "")}{(array[i].ParameterType.IsByRef ? FixInvalidName(array[i].Name) : parameterNames[i])}{(array[i].IsOptional ? "?" : "")}: {GetTypeScriptParameterType(array[i])}");
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
				sb.AppendLine($"): {GetTypeScriptReturnType(method)};");
			}
			else
			{
				sb.Append($"){(method.ReturnType != typeof(void) || @interface ? $": {GetTypeScriptReturnType(method)}" : "")}");
				if (@interface)
				{
					sb.AppendLine(";");
				}
				else
				{
					sb.AppendLine(" {");
					foreach (string line in MethodExtractor.GetMethodCode(method, method.DeclaringType!))
						sb.AppendLine($"{Space(0, 1)}{line}");
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
						sb.Append($"{parameterNames[i]}: {{ ");
					}
					sb.Append($"{(array[i].GetCustomAttribute<ParamArrayAttribute>() != null ? "..." : "")}{(array[i].ParameterType.IsByRef ? FixInvalidName(array[i].Name) : parameterNames[i])}{(array[i].IsOptional ? "?" : "")}: {GetTypeScriptParameterType(array[i])}");
					if (array[i].ParameterType.IsByRef)
					{
						sb.Append(" }");
					}

					if (i < method.GetParameters().Length - 1)
					{
						sb.Append(", ");
					}
				}
				sb.Append($"){(method.ReturnType != typeof(void) ? $": {GetTypeScriptReturnType(method)}" : "")}");

				if (@interface)
				{
					sb.AppendLine(";");
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

		private static string GetTypeScriptParameterType(ParameterInfo parameter)
		{
			Type type = parameter.ParameterType.IsByRef
				? parameter.ParameterType.GetElementType()!
				: parameter.ParameterType;
			NullabilityState state = new NullabilityInfoContext().Create(parameter).ReadState;
			return GetTypeScriptTypeName(type, state);
		}

		private static string GetTypeScriptReturnType(MethodInfo method)
		{
			NullabilityState state = new NullabilityInfoContext().Create(method.ReturnParameter).ReadState;
			return GetTypeScriptTypeName(method.ReturnType, state);
		}

		private static string GetTypeScriptTypeName(Type type, NullabilityState nullabilityState)
		{
			string typeName = GetConvertedName(type);
			return !type.IsValueType && typeName != "any" && nullabilityState == NullabilityState.Nullable
				? $"{typeName} | null"
				: typeName;
		}

		public static string GetTypeScriptCode(FieldInfo field)
		{
			string modifiers = field.IsStatic
				? field.IsLiteral || field.IsInitOnly ? "static readonly " : "static "
				: field.IsInitOnly ? "readonly " : "";
			string fieldType = GetConvertedName(GetFieldType(field));
			if (field.IsDefined(typeof(FixedBufferAttribute), inherit: false))
			{
				fieldType += "[]";
			}
			string definiteAssignment = field.IsStatic ? "" : "!";
			return $"{(field.IsPublic ? "public" : "private")} {modifiers}{FixInvalidName(field.Name)}{definiteAssignment}: {fieldType};";
		}
		public static string GetTypeScriptCode(PropertyInfo property)
		{
			StringBuilder sb = new();

			if (property.GetMethod != null)
			{
				sb.Append($"{(!property.DeclaringType!.IsInterface ? (property.GetMethod.IsPublic ? "public " : "private ") : "")}{(property.GetMethod.IsStatic ? "static " : "")}{(!property.DeclaringType.IsInterface && property.GetMethod.IsAbstract ? "abstract " : "")}get " +
					$"{(property.GetMethod.IsVirtual && property.GetMethod.IsPrivate ? ExplicitInterfaceImplFix(property.Name) : FixInvalidName(property.Name))}(): {GetConvertedName(property.PropertyType)}");
				if (property.GetMethod.IsAbstract)
				{
					sb.AppendLine(";");
				}
				else if (property.GetMethod.IsVirtual && property.GetMethod.IsPrivate)
				{
					sb.AppendLine(" {");
					string defaultValue = property.PropertyType == typeof(string) ? "\"\"" : property.PropertyType == typeof(bool) ? "false" : "0";
					sb.AppendLine($"{Space(0, 1)}return {defaultValue};");
					sb.AppendLine("}");
				}
				else
				{
					sb.AppendLine(" {");
					foreach (string line in MethodExtractor.GetMethodCode(property.GetMethod, property.DeclaringType!))
						sb.AppendLine($"{Space(0, 1)}{line}");
					sb.AppendLine("}");
				}
			}
			if (property.SetMethod != null)
			{
				ParameterInfo pi = property.SetMethod.GetParameters()[0]; // There's only one: value
				sb.Append($"{(!property.DeclaringType!.IsInterface ? (property.SetMethod.IsPublic ? "public " : "private ") : "")}{(property.SetMethod.IsStatic ? "static " : "")}{(!property.DeclaringType.IsInterface && property.SetMethod.IsAbstract ? "abstract " : "")}set " +
					$"{(property.SetMethod.IsVirtual && property.SetMethod.IsPrivate ? ExplicitInterfaceImplFix(property.Name) : FixInvalidName(property.Name))}({pi.Name}: {GetConvertedName(pi.ParameterType)})");
				if (property.SetMethod.IsAbstract)
				{
					sb.AppendLine(";");
				}
				else if (property.SetMethod.IsVirtual && property.SetMethod.IsPrivate)
				{
					sb.AppendLine(" {");
					sb.AppendLine($"{Space(0, 1)}/* explicit interface property assignment */");
					sb.AppendLine("}");
				}
				else
				{
					sb.AppendLine(" {");
					foreach (string line in MethodExtractor.GetMethodCode(property.SetMethod, property.DeclaringType!))
						sb.AppendLine($"{Space(0, 1)}{line}");
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
			return GetTypeScriptCode(contructor, contructor.DeclaringType != null && HasTypeScriptBaseClass(contructor.DeclaringType));
		}

		private static string GetTypeScriptCode(ConstructorInfo contructor, bool callSuper)
		{
			StringBuilder sb = new();

			if (contructor.IsStatic)
			{
				sb.AppendLine("private static staticctor_initialize = (() => {");
				foreach (string line in MethodExtractor.GetConstructorCode(contructor))
					sb.AppendLine($"{Space(0, 1)}{line}");
				sb.AppendLine("})();");
			}
			else
			{
				sb.Append($"{(contructor.IsPublic ? "public" : contructor.IsPrivate ? "private" : "protected")} constructor(");
				sb.Append(GetConstructorParameters(contructor));
				sb.AppendLine(") {");

				if (callSuper)
				{
					sb.AppendLine($"{Space(0, 1)}super();");
				}
					foreach (string line in MethodExtractor.GetConstructorCode(contructor))
						sb.AppendLine($"{Space(0, 1)}{line}");
				sb.AppendLine("}");
			}

			return sb.ToString();
		}

		private static string GetConstructorParameters(ConstructorInfo constructor)
		{
			ParameterInfo[] parameters = constructor.GetParameters();
			string[] parameterNames = GetTypeScriptParameterNames(parameters);
			return string.Join(", ", parameters.Select((parameter, index) =>
				$"{parameterNames[index]}{(parameter.IsOptional ? "?" : "")}: {GetConvertedName(parameter.ParameterType)}"));
		}

		private static string[] GetTypeScriptParameterNames(ParameterInfo[] parameters)
		{
			HashSet<string> usedNames = [];
			string[] names = new string[parameters.Length];
			for (int i = 0; i < parameters.Length; i++)
			{
				ParameterInfo parameter = parameters[i];
				string baseName = parameter.ParameterType.IsByRef
					? parameter.IsOut ? "out" : "ref"
					: FixInvalidName(parameter.Name);
				string name = baseName;
				int suffix = 2;
				while (!usedNames.Add(name))
				{
					name = $"{baseName}{suffix++}";
				}
				names[i] = name;
			}
			return names;
		}

		private static bool IsIndexerAccessor(MethodInfo method)
		{
			Type? declaringType = method.DeclaringType;
			return declaringType != null
				&& declaringType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
					.Any(property => property.GetIndexParameters().Length > 0
						&& (property.GetMethod == method || property.SetMethod == method));
		}

		private static bool IsEventAccessor(MethodInfo method)
		{
			Type? declaringType = method.DeclaringType;
			return declaringType != null
				&& declaringType.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
					.Any(@event => @event.AddMethod == method || @event.RemoveMethod == method);
		}

		private static bool IsMethodLikeSpecialName(MethodInfo method) =>
			!method.IsSpecialName
			|| method.Name.StartsWith("op_", StringComparison.Ordinal)
			|| IsIndexerAccessor(method)
			|| IsEventAccessor(method);

		private static bool HasTypeScriptBaseClass(Type type) =>
			type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType);

		public static string GetTypeScriptClass(Type type, int r)
		{
			StringBuilder sb = new();
			TypeInfo exType = type.GetTypeInfo();

			sb.Append($"{Space(r, 0)}export {(exType.IsAbstract ? (exType.IsSealed ? "" : "abstract ") : "")}class {GetConvertedName(type)}"); // start first line with export <abstract> class [name]
			if (HasTypeScriptBaseClass(type))
			{
				sb.Append($" extends {GetConvertedName(exType.BaseType!)}"); // where classes inherit other classes
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
			if (exType.DeclaredFields.Any(f => f.IsStatic && !f.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
			{
				foreach (FieldInfo field in exType.DeclaredFields.Where(f => f.IsStatic && !f.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
				{
					sb.AppendLine($"{Space(r, 1)}{GetTypeScriptCode(field)}");
				}
				sb.AppendLine();
			}

			// static properties
			if (exType.DeclaredProperties.Any(p => p.GetIndexParameters().Length == 0
				&& (p.GetMethod != null && p.GetMethod.IsStatic || p.SetMethod != null && p.SetMethod.IsStatic)))
			{
				foreach (PropertyInfo prop in exType.DeclaredProperties.Where(p => p.GetIndexParameters().Length == 0
					&& (p.GetMethod != null && p.GetMethod.IsStatic || p.SetMethod != null && p.SetMethod.IsStatic)))
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
			if (exType.DeclaredFields.Any(f => !f.IsStatic && !f.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
			{
				foreach (FieldInfo field in exType.DeclaredFields.Where(f => !f.IsStatic && !f.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
				{
					sb.AppendLine($"{Space(r, 1)}{GetTypeScriptCode(field)}");
				}
				sb.AppendLine();
			}

			// properties
			if (exType.DeclaredProperties.Any(p => p.GetIndexParameters().Length == 0
				&& (p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic)))
			{
				foreach (PropertyInfo prop in exType.DeclaredProperties.Where(p => p.GetIndexParameters().Length == 0
					&& (p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic)))
				{
					string[] code = [.. GetTypeScriptCode(prop).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
				sb.AppendLine();
			}

			// Constructors
			ConstructorInfo[] constructors = [.. exType.DeclaredConstructors];
			foreach (ConstructorInfo staticConstructor in constructors.Where(c => c.IsStatic))
			{
				string[] code = [.. GetTypeScriptCode(staticConstructor).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine($"{Space(r, 1)}{c}");
				}
			}
			ConstructorInfo[] instanceConstructors = [.. constructors.Where(c => !c.IsStatic)];
			if (instanceConstructors.Length == 1)
			{
				string[] code = [.. GetTypeScriptCode(instanceConstructors[0]).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine($"{Space(r, 1)}{c}");
				}
			}
			else if (instanceConstructors.Length > 1)
			{
				ConstructorInfo[] orderedConstructors = [.. instanceConstructors
					.OrderByDescending(constructor => constructor.GetParameters().Length == 0)
					.ThenByDescending(constructor => GetOverloadSpecificity(constructor.GetParameters()))
					.ThenBy(constructor => constructor.GetParameters().Count(parameter => parameter.IsOptional))
					.ThenBy(constructor => constructor.GetParameters().Any(parameter =>
						parameter.GetCustomAttribute<ParamArrayAttribute>() != null))];
				foreach (ConstructorInfo constructor in orderedConstructors
					.DistinctBy(GetConstructorParameters, StringComparer.Ordinal))
				{
					string access = constructor.IsPublic ? "public" : constructor.IsPrivate ? "private" : "protected";
					sb.AppendLine($"{Space(r, 1)}{access} constructor({GetConstructorParameters(constructor)});");
				}

				ConstructorInfo implementation = orderedConstructors[0];
				string implementationAccess = implementation.IsPublic ? "public" : implementation.IsPrivate ? "private" : "protected";
				sb.AppendLine($"{Space(r, 1)}{implementationAccess} constructor(...__overloadArguments$: any[]) {{");
				foreach (ConstructorInfo constructor in orderedConstructors)
				{
					sb.AppendLine($"{Space(r, 2)}if ({GetOverloadDispatchCondition(constructor.GetParameters())}) {{");
					AppendOverloadParameterBindings(sb, constructor.GetParameters(), r + 3);
					if (HasTypeScriptBaseClass(type))
						sb.AppendLine($"{Space(r, 3)}super();");
					foreach (string line in MethodExtractor.GetConstructorCode(constructor))
						sb.AppendLine($"{Space(r, 3)}{line}");
					sb.AppendLine($"{Space(r, 3)}return;");
					sb.AppendLine($"{Space(r, 2)}}}");
				}
				sb.AppendLine($"{Space(r, 2)}throw new TypeError(\"No matching overload for constructor.\");");
				sb.AppendLine($"{Space(r, 1)}}}");
			}
			if (constructors.Length > 0)
			{
				sb.AppendLine();
			}
			if (exType.IsAbstract && exType.IsSealed)
			{
				sb.AppendLine($"{Space(r, 1)}private constructor() {{ }}");
				sb.AppendLine();
			}

			// methods
			if (exType.DeclaredMethods.Any(m => IsMethodLikeSpecialName(m) && !MethodExtractor.IsLocalFunction(m)))
			{
				foreach (IGrouping<string, MethodInfo> overloads in exType.DeclaredMethods
					.Where(m => IsMethodLikeSpecialName(m) && !MethodExtractor.IsLocalFunction(m))
					.GroupBy(m => $"{m.Name}:{m.IsStatic}"))
				{
					MethodInfo[] methods = [.. overloads];
					if (methods.Length == 1)
					{
						string[] code = [.. GetTypeScriptCode(methods[0], false).Split(Environment.NewLine).SkipLast(1)];
						foreach (string c in code)
						{
							sb.AppendLine($"{Space(r, 1)}{c}");
						}
						continue;
					}

					foreach (string signature in methods
						.Select(method => GetTypeScriptCode(method, true).Split(Environment.NewLine)[0])
						.Distinct(StringComparer.Ordinal))
					{
						sb.AppendLine($"{Space(r, 1)}{signature}");
					}

					MethodInfo? implementation = methods.FirstOrDefault(m => !m.IsAbstract);
					if (implementation != null)
					{
						string modifier = implementation.IsPublic ? "public " : "private ";
						string staticModifier = implementation.IsStatic ? "static " : "";
						string methodName = implementation.IsVirtual && implementation.IsPrivate
							? ExplicitInterfaceImplFix(implementation.Name)
							: FixInvalidName(implementation.Name);
						sb.AppendLine($"{Space(r, 1)}{modifier}{staticModifier}{methodName}(...__overloadArguments$: any[]): any {{");
						foreach (MethodInfo overload in methods
							.Where(m => !m.IsAbstract)
							.OrderByDescending(m => m.GetParameters().Length == 0)
							.ThenByDescending(GetOverloadSpecificity)
							.ThenBy(m => m.GetParameters().Count(parameter => parameter.IsOptional))
							.ThenBy(m => m.GetParameters().Any(parameter =>
								parameter.GetCustomAttribute<ParamArrayAttribute>() != null)))
						{
							sb.AppendLine($"{Space(r, 2)}if ({GetOverloadDispatchCondition(overload)}) {{");
							AppendOverloadParameterBindings(sb, overload, r + 3);
							foreach (string line in MethodExtractor.GetMethodCode(overload, type))
								sb.AppendLine($"{Space(r, 3)}{line}");
							if (overload.ReturnType == typeof(void))
								sb.AppendLine($"{Space(r, 3)}return;");
							sb.AppendLine($"{Space(r, 2)}}}");
						}
						sb.AppendLine($"{Space(r, 2)}throw new TypeError(\"No overload of {methodName} matches the provided arguments.\");");
						sb.AppendLine($"{Space(r, 1)}}}");
					}
				}
			}

			// proxy check
			if (exType.DeclaredMethods.Any(m => m.IsVirtual && m.IsPrivate)
				|| exType.DeclaredProperties.Any(p => (p.GetMethod != null && p.GetMethod.IsVirtual && p.GetMethod.IsPrivate) || (p.SetMethod != null && p.SetMethod.IsVirtual && p.SetMethod.IsPrivate)))
			{
				sb.AppendLine();
				string[] code = [.. CreateProxyCode(type).Split(Environment.NewLine).SkipLast(1)];
				foreach (string c in code)
				{
					sb.AppendLine($"{Space(r, 1)}{c}");
				}
			}

			sb.AppendLine($"{Space(r, 0)}}}");
			return sb.ToString();
		}
		public static string GetTypeScriptInterface(Type type, int r)
		{
			TypeInfo exType = type.GetTypeInfo();

			StringBuilder sb = new();

			sb.Append($"{Space(r, 0)}export interface {GetConvertedName(type)}");
			if (exType.ImplementedInterfaces.Any())
			{
				sb.Append(" extends");
				foreach (Type intf in exType.ImplementedInterfaces)
				{
					sb.Append($" {GetConvertedName(intf)},"); // where classes implement interfaces
				}
				sb.Remove(sb.Length - 1, 1);
			}
			sb.AppendLine(" {");

			// properties
			if (exType.DeclaredProperties.Any(p => p.GetIndexParameters().Length == 0
				&& (p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic)))
			{
				foreach (PropertyInfo prop in exType.DeclaredProperties.Where(p => p.GetIndexParameters().Length == 0
					&& (p.GetMethod != null && !p.GetMethod.IsStatic || p.SetMethod != null && !p.SetMethod.IsStatic)))
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
			if (exType.DeclaredMethods.Any(m => IsMethodLikeSpecialName(m) && !MethodExtractor.IsLocalFunction(m)
				&& !(m.IsStatic && m.Name.StartsWith("op_", StringComparison.Ordinal))))
			{
				foreach (MethodInfo method in exType.DeclaredMethods.Where(m => IsMethodLikeSpecialName(m)
					&& !MethodExtractor.IsLocalFunction(m)
					&& !(m.IsStatic && m.Name.StartsWith("op_", StringComparison.Ordinal))))
				{
					string[] code = [.. GetTypeScriptCode(method, true).Split(Environment.NewLine).SkipLast(1)];
					foreach (string c in code)
					{
						sb.AppendLine($"{Space(r, 1)}{c}");
					}
				}
			}

			sb.AppendLine($"{Space(r, 0)}}}");
			return sb.ToString();
		}
		/// <summary>
		/// Generates a TypeScript declaration for a class, interface, enum, or value type.
		/// </summary>
		/// <param name="type">The reflected type to convert.</param>
		/// <param name="namespace">Whether to wrap the declaration in its C# namespace.</param>
		/// <param name="r">The indentation depth.</param>
		/// <returns>The generated TypeScript code.</returns>
		public static string GetTypeScriptCode(Type type, bool @namespace, int r)
		{
			StringBuilder sb = new();
			TypeInfo exType = type.GetTypeInfo();
			string[] namespaceSegments = @namespace && type.Namespace != null
				? [.. type.Namespace.Split('.').Select(FixInvalidName)]
				: [];
			int typeIndent = r;

			foreach (string segment in namespaceSegments)
			{
				sb.AppendLine($"{Space(typeIndent++, 0)}export namespace {segment} {{");
			}

			if (exType.IsClass || exType.IsValueType && !exType.IsEnum)
			{
				sb.Append(GetTypeScriptClass(type, typeIndent));
			}
			else if (exType.IsInterface)
			{
				sb.Append(GetTypeScriptInterface(type, typeIndent));
			}
			else if (exType.IsEnum)
			{
				sb.Append(GetTypeScriptEnum(type, typeIndent));
			}
			else
			{
				throw new ArgumentException($"Cannot generate a TypeScript declaration for {type}.", nameof(type));
			}

			TypeInfo[] declaredNestedTypes = [.. exType.DeclaredNestedTypes
				.Where(nestedType => !nestedType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))];
			if (declaredNestedTypes.Length > 0)
			{
				sb.AppendLine();
				sb.AppendLine($"{Space(typeIndent, 0)}export namespace {GetConvertedName(type, true)} {{");
				foreach (TypeInfo nestedType in declaredNestedTypes)
				{
					sb.Append(GetTypeScriptCode(nestedType, false, typeIndent + 1));
					sb.AppendLine();
				}
				sb.AppendLine($"{Space(typeIndent, 0)}}}");
			}

			for (int i = namespaceSegments.Length - 1; i >= 0; i--)
			{
				sb.AppendLine($"{Space(i + r, 0)}}}");
			}

			return sb.ToString();
		}

	}
}
