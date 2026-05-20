using System.Text;

// Minimal NativeAOT fixture for dotnet-native-mcp tests.
var utf16GreetingBlob = new byte[] { 0xFF, (byte)'h', 0x00, (byte)'i', 0x00, 0xFF };
Console.WriteLine("NativeAOT fixture");
Console.WriteLine(Encoding.Unicode.GetString(utf16GreetingBlob.AsSpan(1, 4)));
