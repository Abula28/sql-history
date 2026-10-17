using System.Buffers.Text;
using System.Text.Json;
using SqlHistory.Core.Models;

namespace SqlHistory.Hackathon.Api;

/// <summary>
/// Opaque, URL-safe routine ids. PostgreSQL signatures contain spaces, commas and
/// parentheses, so the identity is serialized to JSON and base64url-encoded.
/// </summary>
public static class RoutineId
{
    public static string Encode(StoredProcedureIdentity identity)
    {
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(identity));
    }

    public static bool TryDecode(string id, out StoredProcedureIdentity identity)
    {
        identity = null!;

        try
        {
            var decoded = JsonSerializer.Deserialize<StoredProcedureIdentity>(Base64Url.DecodeFromChars(id));
            if (decoded == null ||
                decoded.Provider != SourceProviders.PostgreSql ||
                string.IsNullOrEmpty(decoded.SchemaName) ||
                string.IsNullOrEmpty(decoded.ProcedureName) ||
                decoded.IdentityArguments == null ||
                !Enum.IsDefined(decoded.RoutineType))
            {
                return false;
            }

            identity = decoded;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    public static string DisplayName(StoredProcedureIdentity identity)
    {
        return $"{identity.SchemaName}.{identity.ProcedureName}({identity.IdentityArguments})";
    }
}
