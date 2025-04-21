namespace CSToTS
{
    internal static class Program
    {
        private static void Main()
        {
            //string code = TypeScript.GetFullTypeScriptCode(Type.GetType("Interop")!);
			TypeScript.GetFullTypeScriptCode(typeof(TestMembers<>));

			//Console.WriteLine(code);

            //File.WriteAllText("./file.ts", code);

            //Console.ReadLine();
        }
    }

	internal class TestMembers<T> : Oh
	{
		private class Test
        {

        }
	}
}
internal class Oh
{

}