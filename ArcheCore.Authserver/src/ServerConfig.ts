import "dotenv/config";

function requireEnv(name: string): string {
    const value = process.env[name];

    if (!value) {
        throw new Error(`Missing required environment variable: ${name}`);
    }

    return value;
}

export const Config = {
    port:             Number(process.env.PORT ?? 3000),
    authDatabasePath: requireEnv("AUTH_DATABASE_PATH"),
    gameDatabasePath: requireEnv("GAME_DATABASE_PATH"),
    internalSecret:   requireEnv("INTERNAL_SECRET"),      // shared with WorldServer only
};

export const AuthDatabasePath = Config.authDatabasePath;
export const GameDatabasePath = Config.gameDatabasePath;
export const port             = Config.port;