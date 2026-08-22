# AChen authentication backend

This is a small ASP.NET Core 8 modular monolith for the first account slice. It uses SQLite locally, EF Core migrations, JWT access tokens, rotating refresh tokens and RFC Problem Details errors.

## Run locally

The committed configuration intentionally has no signing secret. Create one in .NET user secrets once:

```powershell
$keyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
$signingKey = [Convert]::ToBase64String($keyBytes)
dotnet user-secrets set "Auth:SigningKey" $signingKey --project Backend/src/AChen.Backend.Api
```

Then start the API from the repository root:

```powershell
dotnet run --project Backend/src/AChen.Backend.Api --launch-profile http
```

The local server listens on `http://127.0.0.1:5080`. Check `GET /health` and `GET /ready`. The SQLite database is created at `Backend/src/AChen.Backend.Api/Data/achen.db` and is ignored by Git.

For a phone or another machine, bind ASP.NET Core to the LAN interface and construct Unity's `BackendConfig` with the host computer's LAN IP. Use HTTPS behind a reverse proxy outside local development.

## API

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/auth/register` | Create user and session |
| POST | `/api/auth/login` | Login by username or email |
| POST | `/api/auth/refresh` | Rotate refresh token |
| POST | `/api/auth/logout` | Revoke refresh token |
| GET | `/api/auth/me` | Read current user with Bearer token |

Usernames contain 3-24 ASCII letters, numbers or underscores. Passwords contain 8-128 characters. Username and email uniqueness is case-insensitive.

## Unity usage

`Assets/Scripts/Network/AuthClient.cs` is a pure-code facade; it creates no GameObject and stores tokens only in memory.

```csharp
var auth = new AChen.Networking.AuthClient();
AuthUser user = await auth.RegisterAsync(username, email, password, cancellationToken);
user = await auth.LoginAsync(usernameOrEmail, password, cancellationToken);
user = await auth.GetCurrentUserAsync(cancellationToken); // refreshes once after a 401
await auth.LogoutAsync(cancellationToken);
```

Do not log tokens or passwords. Persistent login is intentionally not implemented yet; if it is added later, use the platform keystore rather than `PlayerPrefs`.

## Verify

```powershell
dotnet test Backend/AChen.Backend.sln
dotnet build Backend/AChen.Backend.sln -c Release
```
