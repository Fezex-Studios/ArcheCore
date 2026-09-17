import net from "net";
import { SendPacket } from "../lib/PacketSender";
import { ProtocolPersistence } from "../../Shared/protocol.persistence";
import { P2WCreateCharacterResponse } from "../../Shared/Packets/Response/P2WCreateCharacterResponse";

export function SendP2WCharacterCreateResponse(
    socket: net.Socket,
    response: P2WCreateCharacterResponse)
{
    SendPacket(
        socket,
        ProtocolPersistence.P2WCharacterCreateResponse,
        response);
}