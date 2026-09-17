import {P2WCharacterListResponse} from "../../Shared/Packets/Response/P2WCharacterListResponse";
import net from "net";
import {SendPacket} from "../lib/PacketSender";
import {ProtocolPersistence} from "../../Shared/protocol.persistence";

export function SendP2WCharacterListResponse(
    socket:net.Socket,
    response: P2WCharacterListResponse)
{
    SendPacket(socket,ProtocolPersistence.P2WCharacterListResponse,response);
    console.log(`[Persistence] Sent character list (${response.Characters.length}) for AccountId=${response.AccountId}`);
}