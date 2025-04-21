using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Reflection.Emit;

namespace CSToTS
{
    /// <summary>
    /// something we actually need for right now
    /// </summary>
    internal static class BlobReaderExt
    {
        public static OpCode ReadOpCode(this ref BlobReader reader)
        {
            byte code = reader.ReadByte();
            if (code != 0xFE)
            {
                return SingleByteOpCodes[code];
            }
            else
            {
                code = reader.ReadByte();
                return MultiByteOpCodes[code];
            }
        }

        private static readonly OpCode[] SingleByteOpCodes = new OpCode[256];
        private static readonly OpCode[] MultiByteOpCodes = new OpCode[256];

        static BlobReaderExt()
        {
            var fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.GetValue(null) is OpCode opCode)
                {
                    if (opCode.OpCodeType == OpCodeType.Nternal)
                    {
                        continue;
                    }

                    if (opCode.Size == 1)
                    {
                        SingleByteOpCodes[opCode.Value] = opCode;
                    }
                    else
                    {
                        MultiByteOpCodes[opCode.Value & 0xFF] = opCode;
                    }
                }
            }
        }
    }

    /// <summary>
    /// and this
    /// </summary>
    internal class SignatureTypeProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return elementType + "[]";
        }

        public string GetByReferenceType(string elementType)
        {
            return elementType + "&";
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return "FuncPtr";
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            return genericType + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(object? genericContext, int index)
        {
            return "!!" + index;
        }

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            return "!" + index;
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType;
        }

        public string GetPointerType(string elementType)
        {
            return elementType + "*";
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode.ToString();
        }

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            return reader.GetString(typeDefinition.Name);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeReference = reader.GetTypeReference(handle);
            return reader.GetString(typeReference.Name);
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var typeSpecification = reader.GetTypeSpecification(handle);
            return typeSpecification.DecodeSignature(this, genericContext);
        }
    }

    internal static class MethodExtractor
    {
        public static string FixInvalidName(string? name)
        {
            if (name == null)
            {
                return "_";
            }
            else if (name == "")
            {
                return "_null_";
            }
            else
            {
                switch (name)
                {
                    case "function":
                    case "@function":
                        return "func";
                }
            }
            return name.Replace("<", "__").Replace(">", "__").Replace(".", "_").Replace(" ", "_").Replace("|", "_").Replace(",", "_").Replace("@", "");
        }

        private static int MapRVAToOffset(this PEReader peReader, int rva)
        {
            foreach (var section in peReader.PEHeaders.SectionHeaders)
            {
                var relativeVirtualAddress = section.VirtualAddress;
                var sizeOfRawData = section.SizeOfRawData;
                var pointerToRawData = section.PointerToRawData;

                if (rva >= relativeVirtualAddress && rva < relativeVirtualAddress + sizeOfRawData)
                {
                    return rva - relativeVirtualAddress + pointerToRawData;
                }
            }
            throw new InvalidOperationException("RVA is not within any section.");
        }

        private static unsafe string[] _raw(MethodInfo method, Type containingType)
        {
            // Get the method's metadata token
            int metadataToken = method.MetadataToken;

            // Load the assembly containing the type
#pragma warning disable IL3000
            var assemblyPath = containingType.Assembly.Location;
#pragma warning restore IL3000
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);

            // Get the metadata reader
            var metadataReader = peReader.GetMetadataReader();

            // Get the method definition using the metadata token
            var methodHandle = (MethodDefinitionHandle)MetadataTokens.Handle(metadataToken);
            var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);

            // Get the method body and read IL bytes
            var methodBodyBlock = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
            var ilBytes = methodBodyBlock.GetILBytes() ?? [];

            GCHandle gct = GCHandle.Alloc(ilBytes, GCHandleType.Pinned);
            BlobReader reader = new((byte*)gct.AddrOfPinnedObject(), ilBytes.Length);

            List<string> code = [];
            Dictionary<int, string> loadArgs = [];
            List<string> stack = [];

            while (reader.RemainingBytes > 0)
            {
                int offset = reader.Offset;
                OpCode opcode = reader.ReadOpCode();
                Console.Write($"{offset:X4}: {opcode}");

                switch (opcode.OperandType)
                {
                    case OperandType.InlineField:
                        int token = reader.ReadInt32();
                        EntityHandle handle = MetadataTokens.EntityHandle(token);
                        if (handle.Kind == HandleKind.FieldDefinition)
                        {
                            var field = metadataReader.GetFieldDefinition((FieldDefinitionHandle)handle);
                            var fieldName = metadataReader.GetString(field.Name);
                            var fieldType = field.DecodeSignature(new SignatureTypeProvider(), null);
                            Debug.WriteLine($"InlineField: {fieldName} of type {fieldType}");
                            Console.Write($" {fieldName}");
                            stack.Add($"this.{FixInvalidName(fieldName)}");
                        }
                        break;
                    case OperandType.InlineMethod:
                        int mtoken = reader.ReadInt32();
                        var mhandle = MetadataTokens.EntityHandle(mtoken);
                        switch (mhandle.Kind)
                        {
                            case HandleKind.MethodDefinition:
                                var mmethod = metadataReader.GetMethodDefinition((MethodDefinitionHandle)mhandle);
                                var mdmethodName = metadataReader.GetString(mmethod.Name);
                                Console.Write($" {mdmethodName}");
                                break;
                            case HandleKind.MemberReference:
                                var memberReference = metadataReader.GetMemberReference((MemberReferenceHandle)mhandle);
                                var mrmethodName = metadataReader.GetString(memberReference.Name);
                                Console.Write($" {mrmethodName}");
                                break;
                            case HandleKind.MethodSpecification:
                                var methodSpec = metadataReader.GetMethodSpecification((MethodSpecificationHandle)mhandle);
                                ImmutableArray<string> sig = methodSpec.DecodeSignature(new SignatureTypeProvider(), null);
                                switch (methodSpec.Method.Kind)
                                {
                                    case HandleKind.MethodDefinition:
                                        var specMethod = metadataReader.GetMethodDefinition((MethodDefinitionHandle)methodSpec.Method);
                                        var specMethodName = metadataReader.GetString(specMethod.Name);

                                        string[] genParams = specMethod.GetGenericParameters().Count > 0 ? [.. specMethod.GetGenericParameters().Select(gp => metadataReader.GetString(metadataReader.GetGenericParameter(gp).Name))] : [];
                                        string gps = "";
                                        if (genParams.Length > 0)
                                        {
                                            for (int i = 0; i < genParams.Length; i++)
                                            {
                                                string? genParam = genParams[i];
                                                gps += genParam;
                                                if (i < genParams.Length - 1)
                                                {
                                                    gps += ", ";
                                                }
                                            }
                                        }

                                        string[] pars = specMethod.GetParameters().Count > 0 ? [.. specMethod.GetParameters().Select(p => metadataReader.GetString(metadataReader.GetParameter(p).Name))] : [];
                                        string ps = "";
                                        if (pars.Length > 0)
                                        {
                                            for (int i = 0; i < pars.Length; i++)
                                            {
                                                string? par = pars[i];
                                                if (par == "") // indicating that the method is being called by an instance (method is not static)
                                                {
                                                    continue;
                                                }
                                                ps += par;
                                                if (i < pars.Length - 1)
                                                {
                                                    ps += ", ";
                                                }
                                            }
                                        }

                                        Console.Write($" {metadataReader.GetString(metadataReader.GetTypeDefinition(specMethod.GetDeclaringType()).Name)}.{specMethodName}{(gps != "" ? $"<{gps}>" : gps)}({ps})");
                                        break;
                                    case HandleKind.MemberReference:
                                        var specMemberReference = metadataReader.GetMemberReference((MemberReferenceHandle)methodSpec.Method);
                                        var specMemberName = metadataReader.GetString(specMemberReference.Name);
                                        Console.Write($" {sig[0]} {specMemberName}()");
                                        break;
                                    default:
                                        Console.Write("Unknown method specification handle kind");
                                        break;
                                }
                                break;
                            default:
                                Console.Write("Unknown method handle kind");
                                break;
                        }
                        break;
                    case OperandType.InlineNone:
                        if (opcode.Name != null)
                        {
                            if (opcode.Name == "ret")
                            {
                                if (loadArgs.Count > 0)
                                {
                                    int n = loadArgs.Last().Key;
                                    loadArgs.Remove(n, out string? name);
                                    code.Add($"{stack[0]} = {name};");
                                }
                                else
                                {
                                    code.Add($"return {stack[0]};");
                                }
                            }
                            else if (opcode.Name.StartsWith("ldarg."))
                            {
                                int n = int.Parse(opcode.Name.Split('.')[1]);
                                ParameterInfo pi = method.GetParameters()[n];
                                loadArgs.Add(n, pi.Name ?? "_T_NULL");
                            }
                        }
                        break;
                    case OperandType.InlineTok:
                        int intoken = reader.ReadInt32();
                        var inhandle = MetadataTokens.EntityHandle(intoken);
                        string tokName;
                        switch (inhandle.Kind)
                        {
                            case HandleKind.TypeDefinition:
                                var type = metadataReader.GetTypeDefinition((TypeDefinitionHandle)inhandle);
                                var typeName = metadataReader.GetString(type.Name);
                                Console.Write($" {typeName} (type)");
                                tokName = typeName;
                                break;
                            case HandleKind.MethodDefinition:
                                var inmethod = metadataReader.GetMethodDefinition((MethodDefinitionHandle)inhandle);
                                var methodName = metadataReader.GetString(inmethod.Name);
                                Console.Write($" {methodName} (method - RVA: 0x{inmethod.RelativeVirtualAddress:X})");

                                int aam = peReader.MapRVAToOffset(inmethod.RelativeVirtualAddress);

                                tokName = methodName;
                                break;
                            case HandleKind.FieldDefinition:
                                var field = metadataReader.GetFieldDefinition((FieldDefinitionHandle)inhandle);
                                var fieldName = metadataReader.GetString(field.Name);
                                var fieldDeclaringType = metadataReader.GetTypeDefinition(field.GetDeclaringType());
                                var fieldTypeName = metadataReader.GetString(fieldDeclaringType.Name);
                                var fieldTypeNamespace = metadataReader.GetString(fieldDeclaringType.Namespace);
                                Console.WriteLine($"ldtoken Field: {fieldTypeNamespace}.{fieldTypeName}.{fieldName}");

                                // Get the RuntimeFieldHandle
                                var runtimeFieldType = Type.GetType($"{fieldTypeNamespace}.{fieldTypeName}")!.GetRuntimeField(fieldName)!.FieldType;

                                int aaf = peReader.MapRVAToOffset(field.GetRelativeVirtualAddress());

                                Type f = Type.GetType(metadataReader.GetString(metadataReader.GetTypeDefinition(field.GetDeclaringType()).Name))!;

                                int s = Marshal.SizeOf(f);

                                stream.Seek(aaf, SeekOrigin.Begin);
                                byte[] fData = new byte[s];
                                stream.Read(fData, 0, fData.Length);

                                Console.WriteLine(Encoding.UTF8.GetString(fData));

                                tokName = fieldName;
                                break;
                            default:
                                Console.Write("Unknown token kind");
                                tokName = "unknown";
                                break;
                        }

                        stack.Add(tokName);

                        break;
                    default:
                        break;
                }

                Console.WriteLine();
            }

            Console.WriteLine("Code:");
            foreach (string c in code)
            {
                Console.WriteLine(c);
            }

            Console.WriteLine();

            gct.Free();

            return [.. code];
        }

        public static unsafe string[] GetMethodCode(MethodInfo method, Type containingType)
        {
            return [.. _raw(method, containingType)];
        }
    }
}
