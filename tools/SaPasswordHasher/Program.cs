using Neurocorp.Api.Core.Services;

// Generates a PBKDF2 hash compatible with the API's IPasswordHasher implementation.
// Usage: dotnet run --project tools/SaPasswordHasher -- "YourStrongTempPassword"

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/SaPasswordHasher -- \"<password>\"");
    return 1;
}

var hasher = new Pbkdf2PasswordHasher();
var hash = hasher.Hash(args[0]);

Console.WriteLine(hash);
return 0;
