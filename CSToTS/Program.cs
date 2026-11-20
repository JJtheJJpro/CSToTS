namespace CSToTS
{
    internal static class Program
    {
        private static void Main()
        {
            //string code = TypeScript.GetFullTypeScriptCode(Type.GetType("Interop")!);
            TypeScript.GetFullTypeScriptCode(Type.GetType("Interop")!);
            //TypeScript.GetFullTypeScriptCode(typeof(TestMembers<>), new TypeScriptOutputOptions
            //{
            //    FileLayout = TypeScriptOutputLayout.NamespaceDirectories,
            //    EmitNamespaces = false
            //});

            //Console.WriteLine(code);

            //File.WriteAllText("./file.ts", code);

            //Console.ReadLine();
        }
    }

	internal class TestMembers<T> : Oh
	{
		public static int MethodTest<T2>()
		{
			bool i = true;
			Console.WriteLine($"{i}");
            Console.Error.WriteLine($"I ate your {i} and {i}");
            return i ? 1 : 0;
		}
		private class Test
		{
            private static string MethodTest()
            {
                int i = 2;
                Console.WriteLine($"{i}");
                return $"mamma {i}";
            }
		}
	}
}
internal class Oh
{

}