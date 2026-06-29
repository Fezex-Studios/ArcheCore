import { FastifyInstance } from "fastify";
import { db }               from "../Database/Database";
import { HashPassword }     from "../Services/PasswordService";
import { RegisterRequest }  from "../Interfaces/RegisterRequest";
import { RegisterResponse } from "../Interfaces/RegisterResponse";

const USERNAME_MIN = 3;
const USERNAME_MAX = 20;
const PASSWORD_MIN = 8;
const PASSWORD_MAX = 72; // bcrypt silently truncates beyond 72 bytes
const USERNAME_PATTERN = /^[a-zA-Z0-9_]+$/;

export async function RegisterRoute(fastify: FastifyInstance) {
    fastify.post<{ Body: RegisterRequest }>(
        "/register",
        {
            config: {
                rateLimit: {
                    max:        5,      // same as login — registration is just as abusable
                    timeWindow: "1 minute"
                }
            }
        },
        async (request): Promise<RegisterResponse> => {
            const { Username, Password } = request.body ?? ({} as RegisterRequest);

            // ── Input validation ────────────────────────────────────────────
            if (typeof Username !== "string" || typeof Password !== "string") {
                return { Success: false, Message: "Username and password are required" };
            }

            if (Username.length < USERNAME_MIN || Username.length > USERNAME_MAX) {
                return {
                    Success: false,
                    Message: `Username must be between ${USERNAME_MIN} and ${USERNAME_MAX} characters`
                };
            }

            if (!USERNAME_PATTERN.test(Username)) {
                return {
                    Success: false,
                    Message: "Username may only contain letters, numbers, and underscores"
                };
            }

            if (Password.length < PASSWORD_MIN || Password.length > PASSWORD_MAX) {
                return {
                    Success: false,
                    Message: `Password must be between ${PASSWORD_MIN} and ${PASSWORD_MAX} characters`
                };
            }

            // ── Existence check ──────────────────────────────────────────────
            const existing = db.prepare(`
                SELECT account_id FROM accounts WHERE username = ?
            `).get(Username);

            if (existing) {
                // Same generic message as login uses for unknown/bad creds —
                // avoids confirming whether the registration attempt revealed
                // a username collision to anything scripting against this route.
                return { Success: false, Message: "Registration failed" };
            }

            try {
                const hash = await HashPassword(Password);

                // UNIQUE constraint on username is the real guard against a
                // race between the existence check above and this insert.
                db.prepare(`
                    INSERT INTO accounts (username, password_hash)
                    VALUES (?, ?)
                `).run(Username, hash);

                return { Success: true };
            } catch (err) {
                fastify.log.error(err, "Registration failed");

                // SQLITE_CONSTRAINT means someone else registered this
                // username in between our check and insert — same message.
                return { Success: false, Message: "Registration failed" };
            }
        }
    );
}