import net from "net";
import {decode} from "@msgpack/msgpack";
import {W2PHelloWorldPacket} from "../../Shared/Packets/Requests/W2PHelloWorldPacket";


export async function W2PHelloWorldHandler(
    socket: net.Socket,
    payload:Uint8Array){
    const request = decode(payload) as W2PHelloWorldPacket;

    console.log(`[FROM WORLDSERVER]${request.Message}`);
}