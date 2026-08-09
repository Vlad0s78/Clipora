using System.Globalization;

if (args.Length != 5)
{
    Console.Error.Write("Expected: <mode> <started-marker> <q-marker> <source> <temporary-output>.");
    return 64;
}

string mode = args[0];
string startedMarker = args[1];
string qMarker = args[2];
string temporaryOutputPath = args[4];

switch (mode)
{
    case "progress":
        File.WriteAllText(temporaryOutputPath, "encoded");
        for (int index = 1; index <= 20; index++)
        {
            Console.Out.WriteLine($"frame={index.ToString(CultureInfo.InvariantCulture)}");
            Console.Out.WriteLine("fps=50.0");
            Console.Out.WriteLine("bitrate=800.0kbits/s");
            Console.Out.WriteLine($"total_size={(index * 1000).ToString(CultureInfo.InvariantCulture)}");
            Console.Out.WriteLine("out_time=00:00:00.500000");
            Console.Out.WriteLine("speed=2.0x");
            Console.Out.WriteLine("progress=continue");
            Console.Out.Flush();
            Thread.Sleep(10);
        }

        Console.Out.WriteLine("frame=21");
        Console.Out.WriteLine("total_size=21000");
        Console.Out.WriteLine("out_time=00:00:01.000000");
        Console.Out.WriteLine("progress=end");
        Console.Out.Flush();
        Console.Error.Write("runner diagnostic");
        return 0;

    case "failure":
        Console.Error.Write(new string('x', 12_000));
        return 7;

    case "graceful":
        Console.Out.WriteLine("out_time=00:00:01.000000");
        Console.Out.WriteLine("progress=continue");
        Console.Out.Flush();
        File.WriteAllText(startedMarker, "started");
        string? command = Console.In.ReadLine();
        if (string.Equals(command, "q", StringComparison.Ordinal))
        {
            File.WriteAllText(qMarker, "q");
            return 0;
        }

        return 8;

    case "unresponsive":
        File.WriteAllText(startedMarker, "started");
        Console.Out.WriteLine("progress=continue");
        Console.Out.Flush();
        while (true)
        {
            Thread.Sleep(TimeSpan.FromSeconds(30));
        }

    default:
        Console.Error.Write($"Unknown mode: {mode}");
        return 65;
}
