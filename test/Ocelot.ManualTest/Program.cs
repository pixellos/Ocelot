using Ocelot.ManualTest.Actions;
using System.Reflection;

var nl = Environment.NewLine;
var programName = Assembly.GetExecutingAssembly().GetName()?.Name?.Replace(".", " ") ?? "?";
do
{
    Console.Clear();
    Console.WriteLine($"{nl}Welcome to {programName} app!");
    Console.Write(@"What are you going to do?
  1. Run Ocelot with basic setup (default)
  2. Run Ocelot manual tests
  3. Run Playwright SSE manual tests
So, press 1, 2 or 3 > ");
    ConsoleKeyInfo info = Console.ReadKey(true);
    if (info.Key == ConsoleKey.D2)
    {
        Console.WriteLine((char)info.Key);
        ManualTests.Run(args);
    }
    else if (info.Key == ConsoleKey.D3)
    {
        Console.WriteLine((char)info.Key);
        await Issue941.RunAsync(args);
    }
    else
    {
        Console.WriteLine($"{(char)info.Key} -> 1 (default)");
        await Basic.RunAsync(args);
    }
}
while (!Quit());

bool Quit()
{
    Console.WriteLine(nl + "Enter Ctrl+Q to Quit, Ctrl+E to Exit, Ctrl+L to Clear the log");
    Console.Write("Or press any key to restart... ");
    ConsoleKeyInfo info = Console.ReadKey(true);
    if (info.Modifiers == ConsoleModifiers.Control)
    {
        if (info.Key == ConsoleKey.Q)
        {
            Console.WriteLine("Quitting...");
            Environment.ExitCode = 0;
            return true;
        }
        else if (info.Key == ConsoleKey.E)
        {
            Console.WriteLine("Exitting...");
            Environment.Exit(1);
        }
        else if (info.Key == ConsoleKey.L)
        {
            Console.WriteLine();
            Console.Clear();
        }
    }

    Console.WriteLine();
    return false;
}
