Deployment and local development guide

This repository contains two projects:
- `PokemonQuizAPI` — ASP.NET 9 Web API (SignalR) serving data and admin SPA
- `PokemonQuizFrontend/PokeQuiz` — React Native / Expo frontend

Local dev (quickstart)
1. Ensure prerequisites are installed: .NET 9 SDK, Node.js, npm (or yarn), Docker (optional).

2. Start MariaDB+Redis+API via Docker (recommended):
   - docker-compose up --build
   - This will expose API at http://localhost:5168 and map MariaDB and Redis ports.

3. Start frontend:
   - cd PokemonQuizFrontend/PokeQuiz
   - npm install
   - npx expo start

4. API DB initialization:
   - The API will ensure DB schema on startup by calling `EnsureDatabaseInitializedAsync()`.
   - If you prefer local MariaDB, edit `PokemonQuizAPI/appsettings.Development.json` or set env var `ConnectionStrings__Default`.

Troubleshooting
- If TypeScript can't resolve `@/components` imports, ensure `PokemonQuizFrontend/PokeQuiz/tsconfig.json` includes `baseUrl` and `paths` and that your editor is opened at the `PokeQuiz` folder.
- If SignalR errors occur, check that the hub URL is reachable and matches the API host/port.

Production notes
- Use environment variables for DB credentials and do not commit secrets.
- Configure HTTPS and reverse proxy (Nginx) in front of the API container.

Commands
- Build API locally: `cd PokemonQuizAPI && dotnet build` then `dotnet run`.
- TypeScript check: `cd PokemonQuizFrontend/PokeQuiz && npx tsc --noEmit --project .`.
- Docker compose: `docker-compose up --build`.
