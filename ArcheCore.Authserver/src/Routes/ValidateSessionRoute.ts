import { FastifyInstance } from "fastify";
import { db }             from "../Database/Database";
import { Config }         from "../ServerConfig";

export async function ValidateSessionRoute(fastify: FastifyInstance) {
    fastify.post("/validate-session", async (request, reply) => {

        // ── Internal secret check ─────────────────────────────────────────────
        // This endpoint is called by the WorldServer only, never by clients.
        // The secret must match INTERNAL_SECRET in both .env files.
        if (request.headers["x-internal-secret"] !== Config.internalSecret) {
            return reply.status(403).send({ error: "Forbidden" });
        }

        const body = request.body as any;

        const session: any = db.prepare(`
            SELECT * FROM sessions WHERE token = ?
        `).get(body.Token);

        // Always delete — valid tokens are burned (one-shot), expired ones are cleaned up
        if (session) {
            db.prepare(`DELETE FROM sessions WHERE token = ?`).run(body.Token);
        }

        if (!session || new Date(session.expires_at) < new Date()) {
            return { Valid: false };
        }

        return { Valid: true, AccountId: session.account_id };
    });
}