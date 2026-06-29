import net from "net";
import { decode } from "@msgpack/msgpack";
import {db} from "../../Database/Database";
import {W2PCharacterSaveRequest} from "../../Shared/Packets/Requests/W2PCharacterSaveRequest";

export async function W2PCharacterSaveHandler(
    socket: net.Socket,
    payload: Uint8Array)
{
    const save =
        decode(payload) as W2PCharacterSaveRequest;

    db.prepare(`
        INSERT OR REPLACE INTO characters
        (
            character_id,
            name,
            level,
            pos_x,
            pos_y,
            pos_z
        )
        VALUES (?, ?, ?, ?, ?, ?)
    `).run(
        save.CharacterId,
        save.Name,
        save.Level,
        save.X,
        save.Y,
        save.Z
    );

    console.log(
        `Saved ${save.Name}`);
}