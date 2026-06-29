import { FastifyInstance } from "fastify";
import { db }             from "../Database/Database";
import { AccountRow }     from "../Interfaces/AccountRow";
import { VerifyPassword } from "../Services/PasswordService";
import { CreateToken }    from "../Services/SessionService";
import { LoginRequest }   from "../Interfaces/LoginRequest";
import { LoginResponse }  from "../Interfaces/LoginResponse";

const MAX_ATTEMPTS   = 5;
const LOCKOUT_MINUTES = 15;

export async function LoginRoute(fastify: FastifyInstance) {
    fastify.post<{ Body: LoginRequest }>(
        "/login",
        {
            config: {
                rateLimit: {
                    max:        5,      // stricter than the global 10
                    timeWindow: "1 minute"
                }
            }
        },
        async (request): Promise<LoginResponse> => {
            const { Username, Password } = request.body;

            // ── Lockout check ────────────────────────────────────────────────
            const failRow: any = db.prepare(`
                SELECT attempts, locked_until FROM failed_logins WHERE username = ?
            `).get(Username);

            if (failRow?.locked_until) {
                const lockedUntil = new Date(failRow.locked_until);

                if (lockedUntil > new Date()) {
                    const remaining = Math.ceil(
                        (lockedUntil.getTime() - Date.now()) / 60000
                    );

                    return {
                        Success: false,
                        Message: `Account locked. Try again in ${remaining} minute(s).`
                    };
                }

                // Lockout expired — reset
                db.prepare(`DELETE FROM failed_logins WHERE username = ?`).run(Username);
            }

            // ── Credential check ─────────────────────────────────────────────
            const account = db.prepare(`
                SELECT * FROM accounts WHERE username = ?
            `).get(Username) as AccountRow | undefined;

            // Always run bcrypt even if account not found — prevents timing attacks
            // that reveal whether a username exists
            const dummyHash = "$2b$12$invalidhashfortimingpurposesonly000000000000000000000000";
            const valid     = account
                ? await VerifyPassword(Password, account.password_hash)
                : (await VerifyPassword(Password, dummyHash), false);

            if (!account || !valid) {
                // Only record failure if the account actually exists
                if (account) {
                    const attempts = (failRow?.attempts ?? 0) + 1;

                    if (attempts >= MAX_ATTEMPTS) {
                        const lockedUntil = new Date(
                            Date.now() + LOCKOUT_MINUTES * 60 * 1000
                        ).toISOString();

                        db.prepare(`
                            INSERT INTO failed_logins (username, attempts, locked_until)
                            VALUES (?, ?, ?)
                            ON CONFLICT(username) DO UPDATE SET
                                attempts     = excluded.attempts,
                                locked_until = excluded.locked_until
                        `).run(Username, attempts, lockedUntil);

                        return {
                            Success: false,
                            Message: `Too many failed attempts. Account locked for ${LOCKOUT_MINUTES} minutes.`
                        };
                    }

                    db.prepare(`
                        INSERT INTO failed_logins (username, attempts, locked_until)
                        VALUES (?, ?, NULL)
                        ON CONFLICT(username) DO UPDATE SET attempts = excluded.attempts
                    `).run(Username, attempts);
                }

                // Same message whether account exists or not — don't leak info
                return { Success: false, Message: "Invalid username or password" };
            }

            // ── Success — clear failures, create session ──────────────────────
            db.prepare(`DELETE FROM failed_logins WHERE username = ?`).run(Username);

            const token = CreateToken();

            // INSERT OR REPLACE atomically kills the old session and creates a new one
            db.prepare(`
                INSERT OR REPLACE INTO sessions (token, account_id, expires_at)
                VALUES (?, ?, ?)
            `).run(
                token,
                account.account_id,
                new Date(Date.now() + 86400000).toISOString()
            );

            return { Success: true, Token: token };
        }
    );
}