using FCG.Users.MessagingProof;

if (args.Length != 3)
{
    Console.Error.WriteLine("Uso: MessagingProof <Notifications.Api.dll> <resultado.json> <commit-referencia>");
    return 2;
}

try
{
    await Laboratorio.ExecutarAsync(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args[2]);
    return 0;
}
catch (Exception exception)
{
    // O erro completo pode conter conexão ou payload. A evidência detalhada é sanitizada.
    Console.Error.WriteLine($"Prova interrompida: {exception.GetType().Name}. Consulte o resultado sanitizado.");
    return 1;
}
