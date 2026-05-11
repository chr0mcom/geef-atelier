if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: HashPassword <password>");
    return 1;
}

Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(args[0], workFactor: 11));
return 0;
