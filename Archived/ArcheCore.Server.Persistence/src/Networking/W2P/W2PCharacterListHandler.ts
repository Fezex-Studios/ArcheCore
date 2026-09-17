import net from "net";
import {decode} from "@msgpack/msgpack";
import {W2PCharacterListRequest} from "../../Shared/Packets/Requests/W2PCharacterListRequest";
import {db} from "../../Database/Database";
import {CharacterRow} from "../../Shared/Interfaces/CharacterRow";
import {SendP2WCharacterListResponse} from "../P2W/P2WCharacterListResponse";

export async function W2PCharacterListHandler(
    socket:net.Socket,
    payload: Uint8Array){
    const request = decode(payload) as W2PCharacterListRequest;
    const rows = db.prepare(`
        SELECT * FROM characters WHERE account_id = ?`).all(request.AccountId) as CharacterRow[]

    SendP2WCharacterListResponse(socket,{
        AccountId:request.AccountId,
        Characters:rows.map(r=>({
            CharacterId:r.character_id,
            Name:r.name,
            Level:r.level
        }))
    })
}