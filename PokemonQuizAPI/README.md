# PokemonQuiz API

This project includes a lightweight ASP.NET Core API and a React Native frontend for a multiplayer Pokémon quiz game. The server is configured to work out-of-the-box without an external MySQL database by using a JSON file fallback.

Quick start (development)

1. Clone the repository:
   git clone <repo-url>

2. Run the API:
   - Open a terminal in `PokemonQuizAPI`.
   - dotnet run

   On first run the server will detect no database and use a local `data/` folder to store Pokémon and rooms. The server will auto-seed 151 Pokémon in the background (this may take a few minutes).

3. Run the mobile app (Expo):
   - Open a terminal in `PokemonQuizFrontend/PokeQuiz`.
   - npm install
   - npx expo start

Admin UI

- The server serves a basic admin dashboard at `/admin` (static HTML). To protect this endpoint, set `Admin:ApiKey` in `appsettings.json` or via environment variable and include `X-Admin-Key` header when requesting.

Notes

- File fallback is intended for local development and demos. For production, configure a MySQL database and set `ConnectionStrings:PokemonQuizDB` in configuration.
- The auto-seeder uses PokeAPI and requires internet access.
