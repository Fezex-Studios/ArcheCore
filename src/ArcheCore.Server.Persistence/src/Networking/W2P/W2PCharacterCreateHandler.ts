import net from "net";
import { decode } from "@msgpack/msgpack";
import { db } from "../../Database/Database";
import { W2PCreateCharacterRequest } from "../../Shared/Packets/Requests/W2PCreateCharacterRequest";
import { SendP2WCharacterCreateResponse } from "../P2W/P2WCharacterCreateResponseSender";

export async function W2PCharacterCreateHandler(
    socket: net.Socket,
    payload: Uint8Array)
{
    const request = decode(payload) as W2PCreateCharacterRequest;

    try
    {
        const result = db.prepare(`
            INSERT INTO characters (account_id, name, level, pos_x, pos_y, pos_z)
            VALUES (?, ?, 1, 0, 2, 0)
        `).run(request.AccountId, request.Name);

        console.log(
            `[Persistence] Created '${request.Name}' for AccountId=${request.AccountId}`);

        SendP2WCharacterCreateResponse(socket, {
            Success:     true,
            AccountId:   request.AccountId,
            CharacterId: result.lastInsertRowid as number,
            Name:        request.Name
        });
    }
    catch (e)
    {
        console.error(`[Persistence] Create failed: ${e}`);

        SendP2WCharacterCreateResponse(socket, {
            Success:     false,
            AccountId:   request.AccountId,
            CharacterId: 0,
            Name:        ""
        });
    }
}