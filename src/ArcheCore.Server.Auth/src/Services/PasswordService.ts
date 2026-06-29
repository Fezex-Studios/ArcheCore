import bcrypt from "bcrypt";

const SALT_ROUNDS = 12; // 10 is the default, 12 is recommended for public-facing

export async function HashPassword(password: string) {
    return await bcrypt.hash(password, SALT_ROUNDS);
}

export async function VerifyPassword(password: string, hash: string) {
    return await bcrypt.compare(password, hash);
}