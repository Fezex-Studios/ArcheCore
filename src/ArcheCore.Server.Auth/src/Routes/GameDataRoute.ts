import { FastifyInstance } from "fastify";
import fs from "fs";
import crypto from "crypto";
import "dotenv/config"
import {GameDatabasePath} from "../ServerConfig";



// gamedata.bin lives next to the auth server executable.
// Drop the Editor's exported + encrypted binary here before shipping.
// (Point GameDatabasePath / your .env config at the .bin file now — this
// route never inspected the file's internal format, so nothing else here
// needs to change.)

function getDbHash(): string | null
{
    if (!fs.existsSync(GameDatabasePath))
        return null;

    const data = fs.readFileSync(GameDatabasePath);
    return crypto
        .createHash("sha256")
        .update(data)
        .digest("hex");
}

// Cache the hash in memory — recomputing a SHA-256 over a multi-MB file on
// every request is wasteful. Restart the server after updating gamedata.db.
const DB_HASH = getDbHash();

export async function GameDataRoute(
    fastify: FastifyInstance)
{
    // Returns the current DB hash so clients can decide whether to download.
    fastify.get(
        "/gamedata/version",
        {
            config: { rateLimit: false }
        },
        async (_request, reply) =>
        {
            const hash =
                getDbHash();

            if (!hash)
            {
                return reply
                    .status(503)
                    .send({
                        error:
                            "gamedata.bin not found on server"
                    });
            }

            return {
                hash
            };
        });

    // Streams the raw DB file to the client.
    fastify.get(
        "/gamedata/db",
        {
            config: { rateLimit: false }
        },
        async (_request, reply) =>
        {
            if (!DB_HASH || !fs.existsSync(GameDatabasePath))
            {
                return reply
                    .status(503)
                    .send({ error: "gamedata.bin not found on server" });
            }

            const stream =
                fs.createReadStream(GameDatabasePath);

            return reply
                .header("Content-Type",        "application/octet-stream")
                .header("Content-Disposition", "attachment; filename=\"gamedata.bin\"")
                .send(stream);
        });
}