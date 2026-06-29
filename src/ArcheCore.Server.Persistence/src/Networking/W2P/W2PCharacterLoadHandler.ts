import net from "net";

import { decode }
    from "@msgpack/msgpack";

import { db }
    from "../../Database/Database";

import type { CharacterRow }
    from "../../Shared/Interfaces/CharacterRow";

import type { W2PCharacterLoadRequest }
    from "../../Shared/Packets/Requests/W2PCharacterLoadRequest";

import type { P2WCharacterLoadResponse }
    from "../../Shared/Packets/Response/P2WCharacterLoadResponse";

import { SendP2WCharacterLoadResponse }
    from "../P2W/P2WCharacterLoadResponseSender";

export async function W2PCharacterLoadHandler(
    socket: net.Socket,
    payload: Uint8Array)
{
    const request =
        decode(payload) as W2PCharacterLoadRequest;

    const row =
        db.prepare(`
            SELECT *
            FROM characters
            WHERE character_id = ?
        `)
            .get(request.CharacterId) as CharacterRow;

    if (!row)
    {
        console.log(
            `[Persistence] Character ${request.CharacterId} not found — sending empty response`);

        SendP2WCharacterLoadResponse(
            socket,
            {
                Found:       false,
                CharacterId: request.CharacterId,
                Name:        "",
                Level:       0,
                X:           0,
                Y:           0,
                Z:           0
            });

        return;
    }

    const response: P2WCharacterLoadResponse =
        {
            Found:       true,
            CharacterId: row.character_id,
            Name:        row.name,
            Level:       row.level,
            X:           row.pos_x,
            Y:           row.pos_y,
            Z:           row.pos_z
        };

    SendP2WCharacterLoadResponse(
        socket,
        response);
}