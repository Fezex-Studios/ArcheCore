import { FastifyInstance } from "fastify";
import fs from "fs";
import crypto from "crypto";
import "dotenv/config"
import {GameDatabasePath} from "../ServerConfig";



// gamedata.db lives next to the auth server executable.
// Drop your populated database here before shipping.

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
                            "gamedata.db not found on server"
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
                    .send({ error: "gamedata.db not found on server" });
            }

            const stream =
                fs.createReadStream(GameDatabasePath);

            return reply
                .header("Content-Type",        "application/octet-stream")
                .header("Content-Disposition", "attachment; filename=\"gamedata.db\"")
                .send(stream);
        });
}